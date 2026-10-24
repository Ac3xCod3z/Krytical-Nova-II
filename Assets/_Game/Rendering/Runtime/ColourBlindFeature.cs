using KryticalNova.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace KryticalNova.Rendering
{
    /// <summary>
    /// The colour-blind correction of the whole picture (owner, 24 Oct 2026; Settings ▸ Accessibility): after
    /// post-processing, the frame is corrected for the deficiency the player chose (<see cref="Accessibility.ColourBlind"/>)
    /// at the strength they chose. Costs nothing while the setting is Off. Added to the PC renderer by Krytical Nova ▸
    /// Rendering ▸ Ensure Colour-Blind Correction.
    /// </summary>
    public sealed class ColourBlindFeature : ScriptableRendererFeature
    {
        [Tooltip("Hidden/KryticalNova/ColourBlindCorrection. Referenced so a build keeps it.")]
        [SerializeField] private Shader shader;

        private Material material;
        private CorrectionPass pass;

        public override void Create()
        {
            pass = new CorrectionPass { renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (Accessibility.ColourBlind == ColourBlindMode.Off || Accessibility.ColourBlindStrength <= 0f) return;
            CameraType type = renderingData.cameraData.cameraType;
            if (type == CameraType.Preview || type == CameraType.Reflection) return;
            if (material == null)
            {
                if (shader == null) shader = Shader.Find("Hidden/KryticalNova/ColourBlindCorrection");
                if (shader == null) return;
                material = CoreUtils.CreateEngineMaterial(shader);
            }
            material.SetFloat(ShaderIds.Mode, (float)Accessibility.ColourBlind);
            material.SetFloat(ShaderIds.Strength, Accessibility.ColourBlindStrength);
            pass.material = material;
            pass.requiresIntermediateTexture = true;
            renderer.EnqueuePass(pass);
        }

        protected override void Dispose(bool disposing)
        {
            CoreUtils.Destroy(material);
            material = null;
        }

        private static class ShaderIds
        {
            public static readonly int Mode = Shader.PropertyToID("_Mode");
            public static readonly int Strength = Shader.PropertyToID("_Strength");
        }

        private sealed class CorrectionPass : ScriptableRenderPass
        {
            public Material material;

            private sealed class PassData
            {
                public TextureHandle source;
                public Material material;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var resourceData = frameData.Get<UniversalResourceData>();
                if (resourceData.isActiveTargetBackBuffer || material == null) return;

                TextureHandle source = resourceData.activeColorTexture;
                TextureDesc desc = renderGraph.GetTextureDesc(source);
                desc.name = "_ColourBlindColor";
                desc.clearBuffer = false;
                TextureHandle destination = renderGraph.CreateTexture(desc);

                using (var builder = renderGraph.AddRasterRenderPass<PassData>("Colour-Blind Correction", out var passData, profilingSampler))
                {
                    passData.source = source;
                    passData.material = material;
                    builder.UseTexture(source);
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
