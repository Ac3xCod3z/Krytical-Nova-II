using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace KryticalNova.Rendering
{
    /// <summary>
    /// Comic-book look: screen-space Sobel outlines (depth + normals) and Ben-Day dots.
    /// Also publishes the global dot settings used by the "KryticalNova/Comic Toon Lit" shader,
    /// so lit-driven dots on materials and post-process dots share one screen grid.
    /// Add to the URP Renderer asset (e.g. Assets/Settings/PC_Renderer.asset).
    /// </summary>
    /// <summary>
    /// Runtime modifiers applied on top of the renderer asset's serialized comic settings (the feature
    /// re-publishes its settings every frame, so direct global writes would be overwritten).
    /// Driven by the day/night + weather presenter; identity values leave the authored look unchanged.
    /// </summary>
    public static class ComicStyleRuntime
    {
        public static float ShadowOpacityMultiplier = 1f;
        public static Color ShadowTint = Color.white;
        public static float ShadowTintWeight;
        public static float ShadowBrightnessMultiplier = 1f;
        public static float DotStartOffset;

        public static void Reset()
        {
            ShadowOpacityMultiplier = 1f;
            ShadowTint = Color.white;
            ShadowTintWeight = 0f;
            ShadowBrightnessMultiplier = 1f;
            DotStartOffset = 0f;
        }
    }

    public class ComicStyleFeature : ScriptableRendererFeature
    {
        [System.Serializable]
        public class OutlineSettings
        {
            public bool enabled = true;
            public Color color = Color.black;
            [Range(0.5f, 5f)] public float thickness = 1.25f;
            [Range(0f, 1f)] public float opacity = 1f;
            [Tooltip("Relative depth change (fraction of distance) that becomes a line. Lower = more lines.")]
            [Range(0.01f, 1f)] public float depthThreshold = 0.1f;
            [Tooltip("Normal change that becomes a line. Lower = more crease lines.")]
            [Range(0.05f, 3f)] public float normalThreshold = 0.6f;
            [Tooltip("Suppresses false lines on floors/walls viewed at shallow angles.")]
            [Range(0f, 20f)] public float grazingAngleBoost = 6f;

            [Header("Distance")]
            [Tooltip("Lines start getting thinner at this camera distance (m).")]
            public float thinStartDistance = 6f;
            [Tooltip("Lines reach Min Thickness at this camera distance (m).")]
            public float thinEndDistance = 25f;
            [Tooltip("Line width far away, in pixels (never thicker than Thickness).")]
            [Range(0.1f, 3f)] public float minThickness = 0.5f;
            [Tooltip("Line opacity multiplier far away.")]
            [Range(0f, 1f)] public float farOpacity = 0.6f;
            [Tooltip("Interior crease lines (armour panels, folds) start fading here (m). Silhouettes stay.")]
            public float creaseFadeStart = 8f;
            [Tooltip("Interior crease lines are gone at this distance (m).")]
            public float creaseFadeEnd = 20f;
            [Tooltip("All lines start fading at this camera distance (m).")]
            public float fadeStart = 60f;
            [Tooltip("All lines are fully gone at this camera distance (m).")]
            public float fadeEnd = 120f;
        }

        public enum ShadowPattern { Solid = 0, Dots = 1, Hatching = 2 }

        [System.Serializable]
        public class ShadowSettings
        {
            [Tooltip("Restyles cast shadows from the main light on every surface, including terrain.")]
            public bool enabled = true;
            [Tooltip("Colour multiplied into shadowed areas. Cool blues/purples read as comic ink.")]
            public Color color = new Color(0.55f, 0.55f, 0.85f, 1f);
            [Range(0f, 1f)] public float opacity = 0.6f;
            [Tooltip("0 = razor-sharp comic edge, 0.5 = the light's original soft edge.")]
            [Range(0.01f, 0.5f)] public float edgeSoftness = 0.12f;
            public ShadowPattern pattern = ShadowPattern.Dots;
            [Tooltip("Dot size / line width inside shadows.")]
            [Range(0f, 1f)] public float patternCoverage = 0.3f;
            [Range(0f, 1f)] public float patternOpacity = 0.7f;
            [Tooltip("Hatching line spacing in pixels at 1080p.")]
            [Range(2f, 32f)] public float hatchSpacing = 6f;
            [Range(-90f, 90f)] public float hatchAngle = 45f;
            [Tooltip("Ink line traced around shadow shapes. Alpha 0 turns it off.")]
            public Color outlineColor = new Color(0.05f, 0.05f, 0.12f, 1f);
            [Range(0f, 4f)] public float outlineThickness = 1.5f;
        }

        [System.Serializable]
        public class HalftoneSettings
        {
            [Tooltip("Dot cell size in pixels at 1080p. Scales with resolution.")]
            [Range(2f, 32f)] public float dotSize = 7f;
            [Range(0f, 90f)] public float angle = 45f;
            [Range(0.5f, 3f)] public float softness = 1f;
            [Tooltip("Colour multiplied into the dots (black / deep blue / purple ink).")]
            public Color inkColor = new Color(0.08f, 0.07f, 0.18f, 1f);
            [Range(0f, 1f)] public float opacity = 0.85f;

            [Header("Comic Toon Lit materials")]
            [Tooltip("N·L value where the cel band flips from light to shadow.")]
            [Range(0f, 1f)] public float shadowThreshold = 0.3f;
            [Range(0.001f, 0.5f)] public float shadowSoftness = 0.03f;
            [Tooltip("Fraction of the main light still reaching the shadow side.")]
            [Range(0f, 1f)] public float shadowBrightness = 0.35f;
            [Tooltip("Light level below which dots begin to appear.")]
            [Range(0.05f, 1f)] public float dotStart = 0.6f;

            [Header("Post-process dots (for non-toon materials, e.g. Synty)")]
            [Tooltip("Adds dots to dark screen areas based on final luminance. Works with any material, " +
                     "but can't tell a dark texture from a shadow — prefer Comic Toon Lit where possible.")]
            public bool postProcessDots = false;
            [Range(0.05f, 1f)] public float luminanceThreshold = 0.35f;
        }

        public RenderPassEvent renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
        public OutlineSettings outlines = new OutlineSettings();
        public ShadowSettings shadows = new ShadowSettings();
        public HalftoneSettings halftone = new HalftoneSettings();
        [SerializeField] Shader postShader;

        Material _material;
        ComicPass _pass;

        public override void Create()
        {
            if (postShader == null)
                postShader = Shader.Find("Hidden/KryticalNova/ComicPost");

            _pass = new ComicPass();
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            var cameraType = renderingData.cameraData.cameraType;
            if (cameraType == CameraType.Preview || cameraType == CameraType.Reflection)
                return;

            bool passNeeded = outlines.enabled || halftone.postProcessDots || shadows.enabled;

            // Toon materials read these even when the full-screen pass is idle.
            PushGlobals(passNeeded && shadows.enabled);

            if (!passNeeded)
                return;

            if (_material == null)
            {
                if (postShader == null)
                    return;
                _material = CoreUtils.CreateEngineMaterial(postShader);
            }

            _pass.renderPassEvent = renderPassEvent;
            _pass.requiresIntermediateTexture = true;
            _pass.ConfigureInput(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal);
            _pass.Setup(_material, outlines, shadows, halftone);
            renderer.EnqueuePass(_pass);
        }

        void PushGlobals(bool postShadows)
        {
            Shader.SetGlobalFloat(ShaderIds.PostShadows, postShadows ? 1f : 0f);
            Shader.SetGlobalFloat(ShaderIds.DotSize, halftone.dotSize);
            Shader.SetGlobalFloat(ShaderIds.DotAngle, halftone.angle);
            Shader.SetGlobalFloat(ShaderIds.DotSoftness, halftone.softness);
            Shader.SetGlobalColor(ShaderIds.InkColor, halftone.inkColor);
            Shader.SetGlobalFloat(ShaderIds.DotOpacity, halftone.opacity);
            Shader.SetGlobalFloat(ShaderIds.ShadowThreshold, halftone.shadowThreshold);
            Shader.SetGlobalFloat(ShaderIds.ShadowSoftness, halftone.shadowSoftness);
            Shader.SetGlobalFloat(ShaderIds.ShadowBrightness, halftone.shadowBrightness * ComicStyleRuntime.ShadowBrightnessMultiplier);
            Shader.SetGlobalFloat(ShaderIds.DotStart, Mathf.Clamp01(halftone.dotStart + ComicStyleRuntime.DotStartOffset));
        }

        protected override void Dispose(bool disposing)
        {
            CoreUtils.Destroy(_material);
            _material = null;
        }

        static class ShaderIds
        {
            public static readonly int DotSize = Shader.PropertyToID("_ComicDotSize");
            public static readonly int DotAngle = Shader.PropertyToID("_ComicDotAngle");
            public static readonly int DotSoftness = Shader.PropertyToID("_ComicDotSoftness");
            public static readonly int InkColor = Shader.PropertyToID("_ComicInkColor");
            public static readonly int DotOpacity = Shader.PropertyToID("_ComicDotOpacity");
            public static readonly int ShadowThreshold = Shader.PropertyToID("_ComicShadowThreshold");
            public static readonly int ShadowSoftness = Shader.PropertyToID("_ComicShadowSoftness");
            public static readonly int ShadowBrightness = Shader.PropertyToID("_ComicShadowBrightness");
            public static readonly int DotStart = Shader.PropertyToID("_ComicDotStart");
            public static readonly int PostShadows = Shader.PropertyToID("_ComicPostShadows");

            public static readonly int OutlineEnabled = Shader.PropertyToID("_OutlineEnabled");
            public static readonly int OutlineColor = Shader.PropertyToID("_OutlineColor");
            public static readonly int OutlineThickness = Shader.PropertyToID("_OutlineThickness");
            public static readonly int OutlineOpacity = Shader.PropertyToID("_OutlineOpacity");
            public static readonly int DepthThreshold = Shader.PropertyToID("_DepthThreshold");
            public static readonly int NormalThreshold = Shader.PropertyToID("_NormalThreshold");
            public static readonly int GrazingAngleBoost = Shader.PropertyToID("_GrazingAngleBoost");
            public static readonly int OutlineFade = Shader.PropertyToID("_OutlineFade");
            public static readonly int OutlineDistance = Shader.PropertyToID("_OutlineDistance");
            public static readonly int CreaseFade = Shader.PropertyToID("_CreaseFade");

            public static readonly int ShadowEnabled = Shader.PropertyToID("_ShadowEnabled");
            public static readonly int ShadowColor = Shader.PropertyToID("_ShadowColor");
            public static readonly int ShadowOpacity = Shader.PropertyToID("_ShadowOpacity");
            public static readonly int ShadowEdgeSoftness = Shader.PropertyToID("_ShadowEdgeSoftness");
            public static readonly int ShadowPatternId = Shader.PropertyToID("_ShadowPattern");
            public static readonly int ShadowPatternCoverage = Shader.PropertyToID("_ShadowPatternCoverage");
            public static readonly int ShadowPatternOpacity = Shader.PropertyToID("_ShadowPatternOpacity");
            public static readonly int ShadowHatchSpacing = Shader.PropertyToID("_ShadowHatchSpacing");
            public static readonly int ShadowHatchAngle = Shader.PropertyToID("_ShadowHatchAngle");
            public static readonly int ShadowOutlineColor = Shader.PropertyToID("_ShadowOutlineColor");
            public static readonly int ShadowOutlineThickness = Shader.PropertyToID("_ShadowOutlineThickness");
            public static readonly int PostHalftoneEnabled = Shader.PropertyToID("_PostHalftoneEnabled");
            public static readonly int PostHalftoneThreshold = Shader.PropertyToID("_PostHalftoneThreshold");
        }

        class ComicPass : ScriptableRenderPass
        {
            class PassData
            {
                public TextureHandle source;
                public Material material;
            }

            Material _material;

            public ComicPass()
            {
                profilingSampler = new ProfilingSampler("Comic Style");
            }

            public void Setup(Material material, OutlineSettings o, ShadowSettings sh, HalftoneSettings h)
            {
                _material = material;
                material.SetFloat(ShaderIds.OutlineEnabled, o.enabled ? 1f : 0f);
                material.SetColor(ShaderIds.OutlineColor, o.color);
                material.SetFloat(ShaderIds.OutlineThickness, o.thickness);
                material.SetFloat(ShaderIds.OutlineOpacity, o.opacity);
                material.SetFloat(ShaderIds.DepthThreshold, o.depthThreshold);
                material.SetFloat(ShaderIds.NormalThreshold, o.normalThreshold);
                material.SetFloat(ShaderIds.GrazingAngleBoost, o.grazingAngleBoost);
                material.SetVector(ShaderIds.OutlineFade, new Vector4(o.fadeStart, o.fadeEnd, 0f, 0f));
                material.SetVector(ShaderIds.OutlineDistance, new Vector4(o.thinStartDistance, o.thinEndDistance, o.minThickness, o.farOpacity));
                material.SetVector(ShaderIds.CreaseFade, new Vector4(o.creaseFadeStart, o.creaseFadeEnd, 0f, 0f));

                material.SetFloat(ShaderIds.ShadowEnabled, sh.enabled ? 1f : 0f);
                material.SetColor(ShaderIds.ShadowColor, Color.Lerp(sh.color, ComicStyleRuntime.ShadowTint, ComicStyleRuntime.ShadowTintWeight));
                material.SetFloat(ShaderIds.ShadowOpacity, sh.opacity * ComicStyleRuntime.ShadowOpacityMultiplier);
                material.SetFloat(ShaderIds.ShadowEdgeSoftness, sh.edgeSoftness);
                material.SetFloat(ShaderIds.ShadowPatternId, (float)sh.pattern);
                material.SetFloat(ShaderIds.ShadowPatternCoverage, sh.patternCoverage);
                material.SetFloat(ShaderIds.ShadowPatternOpacity, sh.patternOpacity);
                material.SetFloat(ShaderIds.ShadowHatchSpacing, sh.hatchSpacing);
                material.SetFloat(ShaderIds.ShadowHatchAngle, sh.hatchAngle);
                material.SetColor(ShaderIds.ShadowOutlineColor, sh.outlineColor);
                material.SetFloat(ShaderIds.ShadowOutlineThickness, sh.outlineThickness);
                material.SetFloat(ShaderIds.PostHalftoneEnabled, h.postProcessDots ? 1f : 0f);
                material.SetFloat(ShaderIds.PostHalftoneThreshold, h.luminanceThreshold);
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var resourceData = frameData.Get<UniversalResourceData>();
                if (resourceData.isActiveTargetBackBuffer)
                    return;

                TextureHandle source = resourceData.activeColorTexture;
                TextureDesc desc = renderGraph.GetTextureDesc(source);
                desc.name = "_ComicStyleColor";
                desc.clearBuffer = false;
                TextureHandle destination = renderGraph.CreateTexture(desc);

                using (var builder = renderGraph.AddRasterRenderPass<PassData>("Comic Style", out var passData, profilingSampler))
                {
                    passData.source = source;
                    passData.material = _material;

                    builder.UseTexture(source);
                    if (resourceData.cameraDepthTexture.IsValid())
                        builder.UseTexture(resourceData.cameraDepthTexture);
                    if (resourceData.cameraNormalsTexture.IsValid())
                        builder.UseTexture(resourceData.cameraNormalsTexture);
                    // The shadow pass publishes the main light shadow map as a global.
                    builder.UseAllGlobalTextures(true);
                    builder.SetRenderAttachment(destination, 0);

                    builder.SetRenderFunc(static (PassData data, RasterGraphContext ctx) =>
                    {
                        Blitter.BlitTexture(ctx.cmd, data.source, new Vector4(1f, 1f, 0f, 0f), data.material, 0);
                    });
                }

                resourceData.cameraColor = destination;
            }
        }
    }
}
