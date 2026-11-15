using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace KryticalNova.Rendering
{
    /// <summary>
    /// The summer heat haze now (owner's DAY list item 21, part 3), written each frame by the season presenter (World) and read by
    /// <see cref="HeatHazeFeature"/>. Strength 0 skips the pass entirely (no season, night, a cool day, or World Effects Low).
    /// </summary>
    public static class HeatHazeRuntime
    {
        /// <summary>0 none .. 1 a hot afternoon at full strength (already scaled by the World Effects tier).</summary>
        public static float Strength;
        /// <summary>Metres from the camera where the shimmer begins (x) and is full (y).</summary>
        public static Vector2 Distance = new Vector2(60f, 220f);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { Strength = 0f; Distance = new Vector2(60f, 220f); }
    }

    /// <summary>
    /// Heat haze at a distance on the hottest afternoons: one half-res pass builds a shimmer offset field from scene depth (nothing near
    /// the camera moves, the far ground and the low sky do), then the camera colour is read through it. No keywords (no new variants);
    /// it costs nothing while <see cref="HeatHazeRuntime.Strength"/> is 0, which it is on Low. It never touches the sun or sky.
    /// </summary>
    public sealed class HeatHazeFeature : ScriptableRendererFeature
    {
        [System.Serializable]
        public sealed class Settings
        {
            public RenderPassEvent renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
            [Tooltip("The offset field's resolution divider (2 = half res).")]
            [Range(1, 4)] public int downsample = 2;
            [Tooltip("The largest shift of a pixel at full haze, in screen heights.")]
            [Range(0f, 0.02f)] public float amplitude = 0.0035f;
            [Tooltip("Shimmer cells across (x) and up (y) the screen.")]
            public Vector2 noiseScale = new Vector2(70f, 26f);
            [Tooltip("How fast the shimmer rises (screen heights a second, times the cell size).")]
            [Range(0f, 6f)] public float riseSpeed = 1.8f;
            [Tooltip("The sky shimmers only this far above the horizon (the up share of the view direction).")]
            [Range(0.01f, 0.5f)] public float skyFade = 0.12f;
        }

        public Settings settings = new Settings();
        [SerializeField] Shader shader;

        Material _material;
        HazePass _pass;

        static readonly int ParamsId = Shader.PropertyToID("_HazeParams");
        static readonly int NoiseId = Shader.PropertyToID("_HazeNoise");

        public override void Create()
        {
            if (shader == null) shader = Shader.Find("Hidden/KryticalNova/HeatHaze");
            _pass = new HazePass();
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            CameraType type = renderingData.cameraData.cameraType;
            if (type == CameraType.Preview || type == CameraType.Reflection) return;
            float strength = HeatHazeRuntime.Strength;
            if (strength <= 0.001f || shader == null) return;
            if (_material == null) _material = CoreUtils.CreateEngineMaterial(shader);

            Vector2 d = HeatHazeRuntime.Distance;
            float near = Mathf.Max(1f, d.x), far = Mathf.Max(near + 1f, d.y);
            _material.SetVector(ParamsId, new Vector4(near, 1f / (far - near), strength * settings.amplitude, settings.skyFade));
            _material.SetVector(NoiseId, new Vector4(settings.noiseScale.x, settings.noiseScale.y, settings.riseSpeed, 0f));

            _pass.renderPassEvent = settings.renderPassEvent;
            _pass.requiresIntermediateTexture = true;
            _pass.ConfigureInput(ScriptableRenderPassInput.Depth);
            _pass.Setup(_material, settings.downsample);
            renderer.EnqueuePass(_pass);
        }

        protected override void Dispose(bool disposing)
        {
            // only our own material: never RenderSettings (the sky's quit-crash rule)
            CoreUtils.Destroy(_material);
            _material = null;
        }

        sealed class HazePass : ScriptableRenderPass
        {
            const int OffsetPass = 0, CompositePass = 1;
            static readonly int OffsetTexId = Shader.PropertyToID("_HazeOffsetTex");

            sealed class PassData
            {
                public TextureHandle source;
                public TextureHandle offsets;
                public Material material;
                public int pass;
            }

            Material _material;
            int _downsample;

            public HazePass() { profilingSampler = new ProfilingSampler("Heat Haze"); }

            public void Setup(Material material, int downsample)
            {
                _material = material;
                _downsample = Mathf.Max(1, downsample);
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var resourceData = frameData.Get<UniversalResourceData>();
                if (resourceData.isActiveTargetBackBuffer || !resourceData.cameraDepthTexture.IsValid()) return;

                TextureHandle color = resourceData.activeColorTexture;
                TextureDesc full = renderGraph.GetTextureDesc(color);

                TextureDesc offDesc = full;
                offDesc.width = Mathf.Max(1, full.width / _downsample);
                offDesc.height = Mathf.Max(1, full.height / _downsample);
                offDesc.format = SystemInfo.IsFormatSupported(GraphicsFormat.R16G16_SFloat, GraphicsFormatUsage.Render)
                    ? GraphicsFormat.R16G16_SFloat : GraphicsFormat.R8G8B8A8_UNorm;
                offDesc.depthBufferBits = DepthBits.None;
                offDesc.msaaSamples = MSAASamples.None;
                offDesc.filterMode = FilterMode.Bilinear;
                offDesc.clearBuffer = false;
                offDesc.name = "_HeatHazeOffsets";
                TextureHandle offsets = renderGraph.CreateTexture(offDesc);

                AddBlit(renderGraph, "Heat Haze Offsets", color, offsets, OffsetPass, default, resourceData.cameraDepthTexture);

                TextureDesc outDesc = full;
                outDesc.name = "_HeatHazeColor";
                outDesc.clearBuffer = false;
                TextureHandle destination = renderGraph.CreateTexture(outDesc);
                AddBlit(renderGraph, "Heat Haze Composite", color, destination, CompositePass, offsets, resourceData.cameraDepthTexture);
                resourceData.cameraColor = destination;
            }

            void AddBlit(RenderGraph renderGraph, string name, TextureHandle source, TextureHandle target, int pass,
                         TextureHandle offsets, TextureHandle depth)
            {
                using (var builder = renderGraph.AddRasterRenderPass<PassData>(name, out var passData, profilingSampler))
                {
                    passData.source = source;
                    passData.offsets = offsets;
                    passData.material = _material;
                    passData.pass = pass;

                    builder.UseTexture(source);
                    if (offsets.IsValid()) builder.UseTexture(offsets);
                    if (depth.IsValid()) builder.UseTexture(depth);
                    builder.UseAllGlobalTextures(true);   // scene depth is a global
                    builder.SetRenderAttachment(target, 0);

                    builder.SetRenderFunc(static (PassData data, RasterGraphContext ctx) =>
                    {
                        if (data.offsets.IsValid()) data.material.SetTexture(OffsetTexId, data.offsets);
                        Blitter.BlitTexture(ctx.cmd, data.source, new Vector4(1f, 1f, 0f, 0f), data.material, data.pass);
                    });
                }
            }
        }
    }
}
