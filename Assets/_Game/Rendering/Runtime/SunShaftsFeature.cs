using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace KryticalNova.Rendering
{
    /// <summary>
    /// Runtime strength, source and colour of the shafts, written by the day/night + weather presenter (time-of-day
    /// curve, visibility, cloud diffusion). The source is the sun by day and the moon at night. 0 skips the pass entirely.
    /// </summary>
    public static class SunShaftRuntime
    {
        public static float Intensity;
        /// <summary>The player's graphics setting. Not part of <see cref="Reset"/>: it is theirs, not the weather's.</summary>
        public static bool UserEnabled = true;
        /// <summary>Unit vector toward the light the shafts come from. Zero falls back to the sun (<c>_KN_SunDir</c>).</summary>
        public static Vector3 SourceDirection;
        /// <summary>Shaft colour; only its hue is used. Clear falls back to the sun colour (<c>_KN_SunColor</c>).</summary>
        public static Color Tint;

        public static void Reset()
        {
            Intensity = 0f;
            SourceDirection = Vector3.zero;
            Tint = Color.clear;
        }
    }

    /// <summary>
    /// Screen-space god rays from the sun (and, faintly, the moon): a quarter-res sky/cloud occlusion mask, radially
    /// blurred toward the source and added back before post-processing (so Bloom catches them). Buildings and clouds both cut the light.
    /// PC renderer only (needs the depth texture). See Assets/_Game/_Docs/Design/Rendering-Bible.md.
    /// </summary>
    public class SunShaftsFeature : ScriptableRendererFeature
    {
        [System.Serializable]
        public class Settings
        {
            [Tooltip("Runs after ComicStyle so the shafts are not outlined, and before post so Bloom picks them up.")]
            public RenderPassEvent renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing + 1;
            [Tooltip("Mask resolution divider (4 = quarter res).")]
            [Range(1, 8)] public int downsample = 4;
            [Tooltip("How tightly the light source hugs the sun. Higher = smaller bright source.")]
            [Range(10f, 5000f)] public float sourceTightness = 500f;
            [Tooltip("Length of the shafts (fraction of the way to the sun covered by the blur).")]
            [Range(0.1f, 2f)] public float density = 1f;
            [Range(0.8f, 1f)] public float decay = 0.95f;
            [Range(0.001f, 0.3f)] public float weight = 0.06f;
            [Tooltip("Overall brightness, multiplied by SunShaftRuntime.Intensity.")]
            [Range(0f, 4f)] public float intensity = 1.4f;
            [Tooltip("Comic look: posterize the shafts into this many bands. 0 = smooth.")]
            [Range(0, 8)] public int comicBands = 0;
            [Tooltip("How strongly clouds in front of the sun block the shafts.")]
            [Range(0f, 3f)] public float cloudOcclusion = 1.2f;
            [Tooltip("Shafts fade out once the sun is this far off-screen (in screen widths).")]
            [Range(0.05f, 2f)] public float offscreenFade = 0.5f;
            [Tooltip("Debug: show only the shaft buffer.")]
            public bool debugShaftsOnly;
        }

        public Settings settings = new Settings();
        [SerializeField] Shader shader;

        Material _material;
        ShaftPass _pass;

        public override void Create()
        {
            if (shader == null) shader = Shader.Find("Hidden/KryticalNova/SunShafts");
            _pass = new ShaftPass();
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            var cameraData = renderingData.cameraData;
            if (cameraData.cameraType == CameraType.Preview || cameraData.cameraType == CameraType.Reflection) return;

            float strength = SunShaftRuntime.UserEnabled ? SunShaftRuntime.Intensity * settings.intensity : 0f;
            if (strength <= 0.001f || shader == null) return;

            // Source (sun or moon) screen position; skip when it is behind the camera, fade as it leaves the screen.
            Camera cam = cameraData.camera;
            Vector3 sunDir = SunShaftRuntime.SourceDirection.sqrMagnitude > 0.25f
                ? SunShaftRuntime.SourceDirection : (Vector3)Shader.GetGlobalVector(ShaderIds.SunDir);
            Color tint = SunShaftRuntime.Tint.maxColorComponent > 0f ? SunShaftRuntime.Tint : (Color)Shader.GetGlobalVector(ShaderIds.SunColor);
            if (sunDir.y <= -0.02f) return;
            Vector3 viewport = cam.WorldToViewportPoint(cam.transform.position + sunDir * (cam.farClipPlane * 0.5f));
            if (viewport.z <= 0f) return;
            float outside = Mathf.Max(Mathf.Abs(viewport.x - 0.5f), Mathf.Abs(viewport.y - 0.5f)) - 0.5f;
            strength *= 1f - Mathf.Clamp01(outside / settings.offscreenFade);
            if (strength <= 0.001f) return;

            if (_material == null) _material = CoreUtils.CreateEngineMaterial(shader);
            _material.SetVector(ShaderIds.SunScreenPos, new Vector4(viewport.x, viewport.y, cam.aspect, 0f));
            _material.SetVector(ShaderIds.ShaftSourceDir, sunDir);
            _material.SetVector(ShaderIds.ShaftTint, tint);
            _material.SetVector(ShaderIds.ShaftParams, new Vector4(settings.sourceTightness, settings.density, settings.decay, settings.weight));
            _material.SetVector(ShaderIds.ShaftComposite, new Vector4(strength, settings.comicBands, settings.cloudOcclusion, settings.debugShaftsOnly ? 1f : 0f));

            _pass.renderPassEvent = settings.renderPassEvent;
            _pass.requiresIntermediateTexture = true;
            _pass.ConfigureInput(ScriptableRenderPassInput.Depth);
            _pass.Setup(_material, settings.downsample);
            renderer.EnqueuePass(_pass);
        }

        protected override void Dispose(bool disposing)
        {
            CoreUtils.Destroy(_material);
            _material = null;
        }

        static class ShaderIds
        {
            public static readonly int SunDir = Shader.PropertyToID("_KN_SunDir");
            public static readonly int SunColor = Shader.PropertyToID("_KN_SunColor");
            public static readonly int ShaftSourceDir = Shader.PropertyToID("_ShaftSourceDir");
            public static readonly int ShaftTint = Shader.PropertyToID("_ShaftTint");
            public static readonly int SunScreenPos = Shader.PropertyToID("_SunScreenPos");
            public static readonly int ShaftParams = Shader.PropertyToID("_ShaftParams");
            public static readonly int ShaftComposite = Shader.PropertyToID("_ShaftComposite");
            public static readonly int ShaftTex = Shader.PropertyToID("_ShaftTex");
        }

        class ShaftPass : ScriptableRenderPass
        {
            const int MaskPass = 0, BlurLongPass = 1, BlurShortPass = 2, CompositePass = 3;

            class PassData
            {
                public TextureHandle source;
                public TextureHandle shafts;
                public Material material;
                public int pass;
            }

            Material _material;
            int _downsample;

            public ShaftPass()
            {
                profilingSampler = new ProfilingSampler("Sun Shafts");
            }

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

                TextureDesc maskDesc = full;
                maskDesc.width = Mathf.Max(1, full.width / _downsample);
                maskDesc.height = Mathf.Max(1, full.height / _downsample);
                maskDesc.format = SystemInfo.IsFormatSupported(GraphicsFormat.R16_SFloat, GraphicsFormatUsage.Render)
                    ? GraphicsFormat.R16_SFloat : GraphicsFormat.R8G8B8A8_UNorm;
                maskDesc.depthBufferBits = DepthBits.None;
                maskDesc.msaaSamples = MSAASamples.None;
                maskDesc.filterMode = FilterMode.Bilinear;
                maskDesc.clearBuffer = false;
                maskDesc.name = "_SunShaftMaskA";
                TextureHandle maskA = renderGraph.CreateTexture(maskDesc);
                maskDesc.name = "_SunShaftMaskB";
                TextureHandle maskB = renderGraph.CreateTexture(maskDesc);

                // 1. Occlusion mask (reads scene depth + the cloud globals).
                AddBlit(renderGraph, "Sun Shafts Mask", color, maskA, MaskPass, default, resourceData.cameraDepthTexture);
                // 2. Two radial blurs: a long one, then a short one to smooth the streak steps.
                AddBlit(renderGraph, "Sun Shafts Blur Long", maskA, maskB, BlurLongPass, default, default);
                AddBlit(renderGraph, "Sun Shafts Blur Short", maskB, maskA, BlurShortPass, default, default);

                // 3. Composite over the camera colour.
                TextureDesc outDesc = full;
                outDesc.name = "_SunShaftsColor";
                outDesc.clearBuffer = false;
                TextureHandle destination = renderGraph.CreateTexture(outDesc);
                AddBlit(renderGraph, "Sun Shafts Composite", color, destination, CompositePass, maskA, default);
                resourceData.cameraColor = destination;
            }

            void AddBlit(RenderGraph renderGraph, string name, TextureHandle source, TextureHandle target, int pass,
                         TextureHandle shafts, TextureHandle depth)
            {
                using (var builder = renderGraph.AddRasterRenderPass<PassData>(name, out var passData, profilingSampler))
                {
                    passData.source = source;
                    passData.shafts = shafts;
                    passData.material = _material;
                    passData.pass = pass;

                    builder.UseTexture(source);
                    if (shafts.IsValid()) builder.UseTexture(shafts);
                    if (depth.IsValid()) builder.UseTexture(depth);
                    // Scene depth and the sky's cloud noise are bound as globals.
                    builder.UseAllGlobalTextures(true);
                    builder.SetRenderAttachment(target, 0);

                    builder.SetRenderFunc(static (PassData data, RasterGraphContext ctx) =>
                    {
                        if (data.shafts.IsValid()) data.material.SetTexture(ShaderIds.ShaftTex, data.shafts);
                        Blitter.BlitTexture(ctx.cmd, data.source, new Vector4(1f, 1f, 0f, 0f), data.material, data.pass);
                    });
                }
            }
        }
    }
}
