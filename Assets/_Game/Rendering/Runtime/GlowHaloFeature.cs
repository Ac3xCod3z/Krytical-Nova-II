using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace KryticalNova.Rendering
{
    /// <summary>
    /// The halo of light round glowing things (<see cref="GlowHalo"/>: tempered weapons, evolved pets). Screen-space:
    /// the glowing renderers are drawn into a mask, the mask is jump-flooded into "nearest glowing pixel" for every
    /// pixel round it, and the light is laid over the finished frame from that distance. It hugs any outline, falls
    /// away evenly, and costs the same however many things glow; with none on screen it adds no pass at all.
    /// The stronger looks add to it in the same pass, in the light's own colour: sparks that twinkle, lightning
    /// and flame.
    /// Runs after ComicStyle, so the comic ink and dots stay under the light and the model keeps its own outline.
    /// PC renderer only (needs the depth texture). See Assets/_Game/_Docs/Design/Rendering-Bible.md.
    /// </summary>
    public class GlowHaloFeature : ScriptableRendererFeature
    {
        [System.Serializable]
        public class Settings
        {
            [Tooltip("After ComicStyle and SunShafts: the light lies over the inked frame and is not outlined or dotted itself.")]
            public RenderPassEvent renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing + 2;
            [Tooltip("How the light falls away from the outline: 1 = an even ramp, higher = brighter near the model and a longer, fainter tail.")]
            [Range(0.5f, 4f)] public float softness = 1.8f;
            [Tooltip("Pixels of dark kept between the model and its light, so the model's ink outline stays black.")]
            [Range(0f, 4f)] public float inkGap = 1f;
            [Tooltip("How much of what is behind the light is covered by it. 0 = pure added light (burns to white on a bright sky), 1 = a solid colour.")]
            [Range(0f, 1f)] public float solidity = 0.45f;
            [Tooltip("A halo is never thinner than this on screen, however far away.")]
            [Range(1f, 16f)] public float minWidthPixels = 3f;
            [Tooltip("...and never wider than this, however close the camera. Also sets how far the flood has to reach.")]
            [Range(8f, 128f)] public float maxWidthPixels = 56f;
            [Tooltip("Something standing this far (metres) in front of a glowing thing hides its light completely; nearer than that it is lit a little. Keep it small: a large value lets the light of a weapon behind its owner show on the owner's body.")]
            [Range(0.01f, 1f)] public float depthFade = 0.02f;
            [Tooltip("Where a glowing part meets one kept dark (a blade and its hilt, a gun and its grip), how softly the light ends, as a share of the halo's width. 0 = a hard cut.")]
            [Range(0f, 1f)] public float zoneSoftness = 0.3f;
            [Tooltip("Halos start to fade at this camera distance (metres)...")]
            public float fadeStart = 55f;
            [Tooltip("...and are gone at this one.")]
            public float fadeEnd = 80f;
            [Tooltip("Flood at half resolution: a quarter of the cost, no visible difference.")]
            public bool halfResolution = true;

            [Header("Twinkle (looks that ask for it: sparks in the light and on the glowing part, nowhere else)")]
            [Tooltip("The room one spark has to itself, in metres on the glowing thing: its dot is a small part of it and its rays reach towards its edge. Larger = fewer sparks, further apart.")]
            [Range(0.01f, 0.2f)] public float twinkleSize = 0.05f;
            [Tooltip("...and never smaller than this on screen, however far away.")]
            [Range(2f, 16f)] public float twinkleMinPixels = 6f;
            [Tooltip("How many times a second a spot can light. A spark swells and fades over one such beat: lower = slower, softer.")]
            [Range(0.5f, 20f)] public float twinkleRate = 2.2f;
            [Tooltip("The share of spots lit at any moment: more of them = a busier glitter.")]
            [Range(0f, 0.3f)] public float twinkleShare = 0.04f;
            [Tooltip("How pale a spark is: 0 = the glow's own colour, 1 = white. Some paleness is what lets it read against its own glow; its heart is always white.")]
            [Range(0f, 1f)] public float twinkleWhite = 0.55f;
            [Tooltip("Size of a spark's dot, as a share of its room.")]
            [Range(0.05f, 0.6f)] public float twinkleDot = 0.24f;
            [Tooltip("How far its four rays reach at the height of its swell, as a share of its room. 0 = a plain dot.")]
            [Range(0f, 0.9f)] public float twinkleRay = 0.72f;
            [Tooltip("How thin the rays are, as a share of its room.")]
            [Range(0.01f, 0.3f)] public float twinkleRayWidth = 0.06f;

            [Header("Lightning (looks that ask for it: thin arcs that crawl round the outline, in the glow's own colour)")]
            [Tooltip("The stretch of the weapon one arc runs along, in metres.")]
            [Range(0.05f, 1f)] public float boltLength = 0.34f;
            [Tooltip("How many times a second a stretch can be struck.")]
            [Range(0.2f, 10f)] public float boltRate = 2.4f;
            [Tooltip("How often a strike happens when it can: 0 = never, 1 = every time. Lower = calmer.")]
            [Range(0f, 1f)] public float boltChance = 0.42f;
            [Tooltip("Thickness of an arc, in pixels.")]
            [Range(0.5f, 6f)] public float boltWidthPixels = 1.3f;
            [Tooltip("How bright an arc is.")]
            [Range(0f, 6f)] public float boltBrightness = 1.9f;
            [Tooltip("How far an arc wanders from the outline, as a share of the halo's width.")]
            [Range(0f, 1f)] public float boltWander = 0.55f;
            [Tooltip("How white an arc is: 0 = the glow's own colour, 1 = white.")]
            [Range(0f, 1f)] public float boltWhite = 0.65f;

            [Header("Flame (looks that ask for it: tongues that rise from the outline and stream towards the tip, in the glow's own colour)")]
            [Tooltip("The length of one tongue along the weapon, in metres.")]
            [Range(0.02f, 0.6f)] public float flameSize = 0.13f;
            [Tooltip("How fast the tongues travel towards the tip, in metres a second.")]
            [Range(0f, 3f)] public float flameSpeed = 0.5f;
            [Tooltip("How much of the light is tongues: 0 = the steady halo, 1 = between the tongues it drops to half its reach.")]
            [Range(0f, 1f)] public float flameDepth = 0.9f;
            [Tooltip("How far a tongue leans towards the tip as it rises.")]
            [Range(0f, 3f)] public float flameLean = 1.1f;
            [Tooltip("How much brighter the heart of a tongue is than the steady light.")]
            [Range(0f, 2f)] public float flameHeat = 0.55f;

            [Header("Embers (looks that ask for it: the light broken up like a flame seen close, in shades of the glow's own colour)")]
            [Tooltip("The size of one ember, in metres on the glowing thing.")]
            [Range(0.01f, 0.4f)] public float emberSize = 0.07f;
            [Tooltip("How fast the embers stream towards the tip, in metres a second.")]
            [Range(0f, 2f)] public float emberSpeed = 0.3f;
            [Tooltip("How ragged they make the light's edge: 0 = an even reach, 1 = in a trough the light does not leave the outline.")]
            [Range(0f, 1f)] public float emberEdge = 0.5f;
            [Tooltip("How patchy they make the light's body: the share of its brightness lost in a trough.")]
            [Range(0f, 1f)] public float emberPatch = 0.45f;
            [Tooltip("How much hotter a hot patch is: brighter, and paler towards white.")]
            [Range(0f, 1.5f)] public float emberHeat = 0.5f;
            [Tooltip("How much deeper a trough's colour is: 0 = the glow's own, 1 = far more saturated.")]
            [Range(0f, 1f)] public float emberDeep = 0.7f;
        }

        public Settings settings = new Settings();
        [SerializeField] Shader shader;

        // The most things in one view that carry sparks, lightning or flame; any beyond that only glow. Matches the shader's arrays.
        const int MaxPins = 32;

        /// <summary>
        /// Holds the glow's clock at a moment of the caller's choosing (seconds): for stills that must show a given
        /// instant of the breath, the sparks, the lightning and the flame (a sheet of looks, a capture). Null = the game's own time.
        /// </summary>
        public static float? ClockOverride;

        static float Clock => ClockOverride ?? Time.time;

        /// <summary>
        /// While true no light is drawn at all: for captures that compare a frame with the light and without it
        /// (the glow leak check). Never left on.
        /// </summary>
        public static bool Suspended;

        // Enter Play Mode runs without a domain reload.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Suspended = false;
            ClockOverride = null;
        }

        Material _material;
        HaloPass _pass;
        readonly List<Draw> _draws = new List<Draw>(64);
        readonly Pins _pins = new Pins();
        readonly Plane[] _planes = new Plane[6];
        static readonly Vector3[] Corners = new Vector3[8];

        struct Draw
        {
            public Renderer renderer;
            public int submeshes;
            public Vector4 color;
            public Vector4 width;
            public Vector4 zone;            // x = GlowZoneMode, y = 1 with a dark box
            public Matrix4x4 worldToZone;
            public Matrix4x4 worldToDark;
            public float pin;               // its place in the pin lists + 1, or 0 for a plain glow
        }

        /// <summary>What sparks, lightning and flame need of each thing that carries them, for the shader.</summary>
        sealed class Pins
        {
            public readonly Vector4[] origins = new Vector4[MaxPins];   // xyz = the thing's origin (a weapon's grip), w = how bright its sparks are
            public readonly Vector4[] tips = new Vector4[MaxPins];      // xyz = the way its tip points
            public readonly Vector4[] auras = new Vector4[MaxPins];     // x = how strong its lightning is, y = how much of its light is flame, z = the thing's own number (0-1)
            public int count;
            public float clock;
        }

        public override void Create()
        {
            if (shader == null) shader = Shader.Find("Hidden/KryticalNova/GlowHalo");
            _pass = new HaloPass();
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            var halos = GlowHalo.Active;
            if (halos.Count == 0 || shader == null || Suspended) return;
            var cameraData = renderingData.cameraData;
            if (cameraData.cameraType == CameraType.Preview || cameraData.cameraType == CameraType.Reflection) return;

            Camera camera = cameraData.camera;
            if (!Collect(camera, halos, out Rect area)) return;

            if (_material == null) _material = CoreUtils.CreateEngineMaterial(shader);
            _pass.renderPassEvent = settings.renderPassEvent;
            _pass.requiresIntermediateTexture = true;
            _pass.ConfigureInput(ScriptableRenderPassInput.Depth);
            _pass.Setup(_material, settings, _draws, _pins, area);
            renderer.EnqueuePass(_pass);
        }

        /// <summary>
        /// What glows in front of this camera, and the part of the screen it and its light can reach. False when
        /// nothing does: then the feature adds no pass.
        /// </summary>
        bool Collect(Camera camera, IReadOnlyList<GlowHalo> halos, out Rect area)
        {
            _draws.Clear();
            _pins.count = 0;
            _pins.clock = Clock;
            GeometryUtility.CalculateFrustumPlanes(camera, _planes);
            Vector3 eye = camera.transform.position;
            float farthest = settings.fadeEnd * settings.fadeEnd;
            float xMin = 1f, yMin = 1f, xMax = 0f, yMax = 0f;
            bool everything = false;
            var widthLimits = new Vector2(settings.minWidthPixels, settings.maxWidthPixels);

            for (int h = 0; h < halos.Count; h++)
            {
                GlowHalo halo = halos[h];
                bool evaluated = false;
                Vector3 light = default;
                float width = 0f, pin = 0f;
                for (int r = 0; r < halo.RendererCount; r++)
                {
                    Renderer source = halo.RendererAt(r);
                    if (source == null || !source.enabled || !source.gameObject.activeInHierarchy) continue;
                    if ((camera.cullingMask & (1 << source.gameObject.layer)) == 0) continue;
                    Bounds bounds = source.bounds;
                    if ((bounds.center - eye).sqrMagnitude > farthest) continue;
                    if (!GeometryUtility.TestPlanesAABB(_planes, bounds)) continue;

                    if (!evaluated)
                    {
                        halo.Evaluate(_pins.clock, out light, out width);
                        evaluated = true;
                        if (halo.Pinned && _pins.count < MaxPins && halo.TryGetPin(out Vector3 origin, out Vector3 towardsTip))
                        {
                            int at = _pins.count++;
                            _pins.origins[at] = new Vector4(origin.x, origin.y, origin.z, halo.Twinkle);
                            _pins.tips[at] = new Vector4(towardsTip.x, towardsTip.y, towardsTip.z, 0f);
                            _pins.auras[at] = new Vector4(halo.Lightning, halo.Flame, halo.Beat, halo.Ember);
                            pin = at + 1;
                        }
                    }
                    if (width <= 0f || light == Vector3.zero) break;
                    halo.GetZone(r, out GlowZoneMode zoneMode, out Matrix4x4 worldToZone, out bool hasDark, out Matrix4x4 worldToDark);
                    _draws.Add(new Draw
                    {
                        renderer = source,
                        submeshes = halo.SubmeshCountAt(r),
                        color = light,
                        width = new Vector4(width, widthLimits.x, widthLimits.y, 0f),
                        zone = new Vector4((float)zoneMode, hasDark ? 1f : 0f, 0f, 0f),
                        worldToZone = worldToZone,
                        worldToDark = worldToDark,
                        pin = pin,
                    });
                    if (!everything) everything = !Grow(camera, bounds, ref xMin, ref yMin, ref xMax, ref yMax);
                }
            }

            area = new Rect(0f, 0f, 1f, 1f);
            if (_draws.Count == 0) return false;
            if (!everything) area = Rect.MinMaxRect(Mathf.Clamp01(xMin), Mathf.Clamp01(yMin), Mathf.Clamp01(xMax), Mathf.Clamp01(yMax));
            return true;
        }

        // Grows the screen rectangle (0-1) to hold these bounds. False when they reach behind the camera: then the
        // whole screen is used.
        static bool Grow(Camera camera, Bounds bounds, ref float xMin, ref float yMin, ref float xMax, ref float yMax)
        {
            Vector3 c = bounds.center, e = bounds.extents;
            for (int i = 0; i < 8; i++)
                Corners[i] = c + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z);
            for (int i = 0; i < 8; i++)
            {
                Vector3 view = camera.WorldToViewportPoint(Corners[i]);
                if (view.z <= camera.nearClipPlane) return false;
                if (view.x < xMin) xMin = view.x;
                if (view.y < yMin) yMin = view.y;
                if (view.x > xMax) xMax = view.x;
                if (view.y > yMax) yMax = view.y;
            }
            return true;
        }

        protected override void Dispose(bool disposing)
        {
            CoreUtils.Destroy(_material);
            _material = null;
        }

        static class ShaderIds
        {
            public static readonly int GlowAttr = Shader.PropertyToID("_GlowAttr");
            public static readonly int GlowDepth = Shader.PropertyToID("_GlowDepth");
            public static readonly int GlowSeeds = Shader.PropertyToID("_GlowSeeds");
            public static readonly int GlowBlock = Shader.PropertyToID("_GlowBlock");
            public static readonly int GlowBorder = Shader.PropertyToID("_GlowBorder");
            public static readonly int GlowPin = Shader.PropertyToID("_GlowPin");
            public static readonly int GlowPins = Shader.PropertyToID("_GlowPins");
            public static readonly int GlowTips = Shader.PropertyToID("_GlowTips");
            public static readonly int GlowAuras = Shader.PropertyToID("_GlowAuras");
            public static readonly int GlowClock = Shader.PropertyToID("_GlowClock");
            public static readonly int GlowTwinkle = Shader.PropertyToID("_GlowTwinkle");
            public static readonly int GlowTwinkleLook = Shader.PropertyToID("_GlowTwinkleLook");
            public static readonly int GlowBolt = Shader.PropertyToID("_GlowBolt");
            public static readonly int GlowBoltLook = Shader.PropertyToID("_GlowBoltLook");
            public static readonly int GlowFlame = Shader.PropertyToID("_GlowFlame");
            public static readonly int GlowFlameLook = Shader.PropertyToID("_GlowFlameLook");
            public static readonly int GlowEmber = Shader.PropertyToID("_GlowEmber");
            public static readonly int GlowAttrTexelSize = Shader.PropertyToID("_GlowAttr_TexelSize");
            public static readonly int GlowColor = Shader.PropertyToID("_GlowColor");
            public static readonly int GlowWidth = Shader.PropertyToID("_GlowWidth");
            public static readonly int GlowFade = Shader.PropertyToID("_GlowFade");
            public static readonly int GlowZone = Shader.PropertyToID("_GlowZone");
            public static readonly int GlowZoneMatrix = Shader.PropertyToID("_GlowZoneMatrix");
            public static readonly int GlowDarkMatrix = Shader.PropertyToID("_GlowDarkMatrix");
            public static readonly int GlowFlood = Shader.PropertyToID("_GlowFlood");
            public static readonly int GlowShape = Shader.PropertyToID("_GlowShape");
        }

        class HaloPass : ScriptableRenderPass
        {
            const int MaskPass = 0, SeedPass = 1, FloodPass = 2, CompositePass = 3, BlockPass = 4;
            // Further than anything drawn: where no fading prop stands.
            const float NoBlock = 1e9f;
            const int MostBlockDraws = OccluderFader.MostFadedRenderers * 2, MostPartsOfAProp = 8;

            class BlockData
            {
                public Material material;
            }
            const int MaxFloodSteps = 8;

            class MaskData
            {
                public Material material;
                public List<Draw> draws;
                public Vector4 fade;
            }

            class FullData
            {
                public Material material;
                public MaterialPropertyBlock block;
                public int pass;
                public TextureHandle attr, depth, seeds, blockers;
                public Vector4 attrTexelSize, flood, shape, border;
                public Settings settings;   // the composite alone reads the looks of sparks, lightning and flame from it
                public Pins pins;
                public Rect scissor;
            }

            Material _material;
            Settings _settings;
            List<Draw> _draws;
            Pins _pins;
            Rect _area;
            // One block per full-screen pass of a frame: each is filled when its pass runs and read by its own draw.
            readonly MaterialPropertyBlock[] _blocks = new MaterialPropertyBlock[MaxFloodSteps + 2];

            public HaloPass()
            {
                profilingSampler = new ProfilingSampler("Glow Halo");
            }

            public void Setup(Material material, Settings settings, List<Draw> draws, Pins pins, Rect area)
            {
                _material = material;
                _settings = settings;
                _draws = draws;
                _pins = pins;
                _area = area;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var resourceData = frameData.Get<UniversalResourceData>();
                if (resourceData.isActiveTargetBackBuffer || !resourceData.cameraDepthTexture.IsValid()) return;

                TextureHandle color = resourceData.activeColorTexture;
                TextureDesc full = renderGraph.GetTextureDesc(color);
                int scale = _settings.halfResolution ? 2 : 1;
                int floodWidth = Mathf.Max(1, (full.width + scale - 1) / scale);
                int floodHeight = Mathf.Max(1, (full.height + scale - 1) / scale);

                TextureDesc attrDesc = full;
                attrDesc.format = GraphicsFormat.R16G16B16A16_SFloat;
                attrDesc.depthBufferBits = DepthBits.None;
                attrDesc.msaaSamples = MSAASamples.None;
                attrDesc.filterMode = FilterMode.Point;
                attrDesc.clearBuffer = true;
                attrDesc.clearColor = Color.clear;
                attrDesc.name = "_GlowHaloAttr";
                TextureHandle attr = renderGraph.CreateTexture(attrDesc);

                // The glowing surface's depth, and which pinned thing (sparks, lightning, flame) it belongs to.
                TextureDesc depthDesc = attrDesc;
                depthDesc.format = SystemInfo.IsFormatSupported(GraphicsFormat.R32G32_SFloat, GraphicsFormatUsage.Render)
                    ? GraphicsFormat.R32G32_SFloat : GraphicsFormat.R32G32B32A32_SFloat;
                depthDesc.name = "_GlowHaloDepth";
                TextureHandle depth = renderGraph.CreateTexture(depthDesc);

                // Two screen positions a texel: the nearest glowing pixel and the nearest dark one. Screen positions
                // need more than half-float precision above 2048 pixels.
                TextureDesc seedDesc = attrDesc;
                seedDesc.width = floodWidth;
                seedDesc.height = floodHeight;
                seedDesc.format = SystemInfo.IsFormatSupported(GraphicsFormat.R16G16B16A16_UNorm, GraphicsFormatUsage.Render)
                    ? GraphicsFormat.R16G16B16A16_UNorm : GraphicsFormat.R32G32B32A32_SFloat;
                seedDesc.name = "_GlowHaloSeedsA";
                TextureHandle seedsA = renderGraph.CreateTexture(seedDesc);
                seedDesc.name = "_GlowHaloSeedsB";
                TextureHandle seedsB = renderGraph.CreateTexture(seedDesc);

                var attrTexelSize = new Vector4(1f / full.width, 1f / full.height, full.width, full.height);

                // What the light can touch: the glowing things' own rectangle, grown by the widest halo.
                float reach = _settings.maxWidthPixels + _settings.inkGap + 2f;
                var scissor = Rect.MinMaxRect(
                    Mathf.Floor(_area.xMin * full.width - reach), Mathf.Floor(_area.yMin * full.height - reach),
                    Mathf.Ceil(_area.xMax * full.width + reach), Mathf.Ceil(_area.yMax * full.height + reach));
                scissor = Rect.MinMaxRect(Mathf.Max(0f, scissor.xMin), Mathf.Max(0f, scissor.yMin),
                    Mathf.Min(full.width, scissor.xMax), Mathf.Min(full.height, scissor.yMax));
                if (scissor.width < 1f || scissor.height < 1f) return;
                var floodScissor = Rect.MinMaxRect(Mathf.Floor(scissor.xMin / scale), Mathf.Floor(scissor.yMin / scale),
                    Mathf.Min(floodWidth, Mathf.Ceil(scissor.xMax / scale)), Mathf.Min(floodHeight, Mathf.Ceil(scissor.yMax / scale)));

                // 0. The props the camera is fading, drawn whole: the scene's depth has their holes in it, and a
                // glow behind one would shine through each hole. Nearly always there are none, and this only clears.
                TextureDesc blockDesc = attrDesc;
                blockDesc.format = GraphicsFormat.R32_SFloat;
                blockDesc.clearColor = new Color(NoBlock, NoBlock, NoBlock, NoBlock);
                blockDesc.name = "_GlowHaloBlock";
                TextureHandle blockers = renderGraph.CreateTexture(blockDesc);
                using (var builder = renderGraph.AddRasterRenderPass<BlockData>("Glow Halo Block", out var data, profilingSampler))
                {
                    data.material = _material;
                    builder.SetRenderAttachment(blockers, 0);
                    builder.SetGlobalTextureAfterPass(blockers, ShaderIds.GlowBlock);
                    builder.AllowPassCulling(false);
                    builder.SetRenderFunc(static (BlockData d, RasterGraphContext ctx) =>
                    {
                        // Never more draws than the fader may have pieces faded, each with a handful of parts: whatever
                        // is in the list, this pass cannot grow into a second drawing of the world (one wall of a
                        // combined static mesh once listed that mesh's 208 parts, and this drew them all).
                        var faded = OccluderFader.Faded;
                        int draws = 0;
                        for (int i = 0; i < faded.Count && draws < MostBlockDraws; i++)
                        {
                            Renderer prop = faded[i].renderer;
                            if (prop == null || !prop.enabled || !prop.gameObject.activeInHierarchy) continue;
                            int parts = Mathf.Min(faded[i].submeshes, MostPartsOfAProp);
                            for (int s = 0; s < parts && draws < MostBlockDraws; s++, draws++)
                                ctx.cmd.DrawRenderer(prop, d.material, s, BlockPass);
                        }
                    });
                }

                // 1. The mask: every glowing renderer, where the camera can see it.
                using (var builder = renderGraph.AddRasterRenderPass<MaskData>("Glow Halo Mask", out var data, profilingSampler))
                {
                    data.material = _material;
                    data.draws = _draws;
                    data.fade = new Vector4(_settings.fadeStart, _settings.fadeEnd, 0f, 0f);

                    builder.UseTexture(resourceData.cameraDepthTexture);
                    builder.UseTexture(blockers);
                    builder.UseAllGlobalTextures(true);
                    builder.SetRenderAttachment(attr, 0);
                    builder.SetRenderAttachment(depth, 1);
                    builder.AllowGlobalStateModification(true);

                    builder.SetRenderFunc(static (MaskData d, RasterGraphContext ctx) =>
                    {
                        ctx.cmd.SetGlobalVector(ShaderIds.GlowFade, d.fade);
                        for (int i = 0; i < d.draws.Count; i++)
                        {
                            Draw draw = d.draws[i];
                            if (draw.renderer == null) continue;
                            ctx.cmd.SetGlobalVector(ShaderIds.GlowColor, draw.color);
                            ctx.cmd.SetGlobalVector(ShaderIds.GlowWidth, draw.width);
                            ctx.cmd.SetGlobalVector(ShaderIds.GlowZone, draw.zone);
                            ctx.cmd.SetGlobalMatrix(ShaderIds.GlowZoneMatrix, draw.worldToZone);
                            ctx.cmd.SetGlobalMatrix(ShaderIds.GlowDarkMatrix, draw.worldToDark);
                            ctx.cmd.SetGlobalFloat(ShaderIds.GlowPin, draw.pin);
                            for (int s = 0; s < draw.submeshes; s++)
                                ctx.cmd.DrawRenderer(draw.renderer, d.material, s, MaskPass);
                        }
                    });
                }

                // 2. Seeds, then the flood: jumps halve from the widest halo down to one texel.
                int block = 0;
                var flood = new Vector4(scale, 1f, 0f, 0f);
                AddFull(renderGraph, "Glow Halo Seed", SeedPass, block++, seedsA, false, attr, default, default, default, default,
                    attrTexelSize, flood, default, floodScissor);

                // The last step is taken twice: a jump flood misses the true nearest pixel here and there (small parts
                // sticking out of a long edge), which shows as streaks in the light, and one more pass at the
                // smallest jump mends nearly all of them.
                int jump = Mathf.NextPowerOfTwo(Mathf.Max(1, Mathf.CeilToInt(reach / scale)));
                TextureHandle from = seedsA, to = seedsB;
                bool again = true;
                for (int step = 0; step < MaxFloodSteps && jump >= 1; step++)
                {
                    flood.y = jump;
                    AddFull(renderGraph, "Glow Halo Flood", FloodPass, block++, to, false, attr, default, from, default, default,
                        attrTexelSize, flood, default, floodScissor);
                    (from, to) = (to, from);
                    if (jump == 1 && again) again = false;
                    else jump /= 2;
                }

                // 3. The light, with its sparks, lightning and flame, over the frame.
                var shape = new Vector4(_settings.softness, _settings.inkGap, _settings.solidity, _settings.depthFade);
                AddFull(renderGraph, "Glow Halo Composite", CompositePass, block, color, true, attr, depth, from,
                    resourceData.cameraDepthTexture, blockers, attrTexelSize, flood, shape, scissor);
            }

            void AddFull(RenderGraph renderGraph, string name, int pass, int block, TextureHandle target, bool keepTarget,
                         TextureHandle attr, TextureHandle depth, TextureHandle seeds, TextureHandle sceneDepth, TextureHandle blockers,
                         Vector4 attrTexelSize, Vector4 flood, Vector4 shape, Rect scissor)
            {
                using (var builder = renderGraph.AddRasterRenderPass<FullData>(name, out var data, profilingSampler))
                {
                    data.material = _material;
                    data.block = _blocks[block] ??= new MaterialPropertyBlock();
                    data.pass = pass;
                    data.attr = attr;
                    data.depth = depth;
                    data.seeds = seeds;
                    data.blockers = blockers;
                    data.attrTexelSize = attrTexelSize;
                    data.flood = flood;
                    data.shape = shape;
                    data.border = new Vector4(_settings.zoneSoftness, 0f, 0f, 0f);
                    data.settings = pass == CompositePass ? _settings : null;
                    data.pins = pass == CompositePass ? _pins : null;
                    data.scissor = scissor;

                    builder.UseTexture(attr);
                    if (depth.IsValid()) builder.UseTexture(depth);
                    if (seeds.IsValid()) builder.UseTexture(seeds);
                    if (blockers.IsValid()) builder.UseTexture(blockers);
                    if (sceneDepth.IsValid())
                    {
                        builder.UseTexture(sceneDepth);
                        builder.UseAllGlobalTextures(true);
                    }
                    builder.SetRenderAttachment(target, 0, keepTarget ? AccessFlags.ReadWrite : AccessFlags.Write);

                    builder.SetRenderFunc(static (FullData d, RasterGraphContext ctx) =>
                    {
                        d.block.Clear();
                        d.block.SetTexture(ShaderIds.GlowAttr, d.attr);
                        if (d.depth.IsValid()) d.block.SetTexture(ShaderIds.GlowDepth, d.depth);
                        if (d.seeds.IsValid()) d.block.SetTexture(ShaderIds.GlowSeeds, d.seeds);
                        if (d.blockers.IsValid()) d.block.SetTexture(ShaderIds.GlowBlock, d.blockers);
                        d.block.SetVector(ShaderIds.GlowAttrTexelSize, d.attrTexelSize);
                        d.block.SetVector(ShaderIds.GlowFlood, d.flood);
                        d.block.SetVector(ShaderIds.GlowShape, d.shape);
                        d.block.SetVector(ShaderIds.GlowBorder, d.border);
                        if (d.pins != null)
                        {
                            Settings s = d.settings;
                            d.block.SetVectorArray(ShaderIds.GlowPins, d.pins.origins);
                            d.block.SetVectorArray(ShaderIds.GlowTips, d.pins.tips);
                            d.block.SetVectorArray(ShaderIds.GlowAuras, d.pins.auras);
                            d.block.SetFloat(ShaderIds.GlowClock, d.pins.clock);
                            d.block.SetVector(ShaderIds.GlowTwinkle, new Vector4(s.twinkleSize, s.twinkleRate, s.twinkleShare, s.twinkleMinPixels));
                            d.block.SetVector(ShaderIds.GlowTwinkleLook, new Vector4(s.twinkleWhite, s.twinkleDot, s.twinkleRay, s.twinkleRayWidth));
                            d.block.SetVector(ShaderIds.GlowBolt, new Vector4(s.boltLength, s.boltRate, s.boltChance, s.boltWidthPixels));
                            d.block.SetVector(ShaderIds.GlowBoltLook, new Vector4(s.boltBrightness, s.boltWander, s.boltWhite, 0f));
                            d.block.SetVector(ShaderIds.GlowFlame, new Vector4(s.flameSize, s.flameSpeed, s.flameDepth, s.flameLean));
                            d.block.SetVector(ShaderIds.GlowFlameLook, new Vector4(s.flameHeat, s.emberHeat, s.emberDeep, 0f));
                            d.block.SetVector(ShaderIds.GlowEmber, new Vector4(s.emberSize, s.emberSpeed, s.emberEdge, s.emberPatch));
                        }
                        ctx.cmd.EnableScissorRect(d.scissor);
                        ctx.cmd.DrawProcedural(Matrix4x4.identity, d.material, d.pass, MeshTopology.Triangles, 3, 1, d.block);
                        ctx.cmd.DisableScissorRect();
                    });
                }
            }
        }
    }
}
