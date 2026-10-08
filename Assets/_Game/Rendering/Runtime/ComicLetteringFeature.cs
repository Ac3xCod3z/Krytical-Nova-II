using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace KryticalNova.Rendering
{
    /// <summary>
    /// Comic lettering: the words and marks of a hit, and anything else on the <see cref="LayerName"/> layer. It is
    /// laid over the finished picture the way lettering is laid over a drawn page: after ComicStyle has inked and
    /// dotted the frame, and after SunShafts and GlowHalo. Drawn with the other transparents it had the outline of
    /// whoever stood behind a word inked across its letters, and the light of their weapon on top of that.
    /// It is still hidden by what really stands in front of it (it tests against the scene's depth), and URP's own
    /// post (grain, toning) still goes over it. The renderer's transparent pass leaves the layer out
    /// (PostProcessSetup.EnsureComicLettering), so nothing is drawn twice.
    /// See Assets/_Game/_Docs/Design/Rendering-Bible.md, "Frame order".
    /// </summary>
    public class ComicLetteringFeature : ScriptableRendererFeature
    {
        public const string LayerName = "ComicLettering";

        [Tooltip("After ComicStyle, SunShafts and GlowHalo: lettering is not inked, dotted or lit over.")]
        public RenderPassEvent renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing + 3;

        LetteringPass _pass;
        int _layers;

        public override void Create()
        {
            _pass = new LetteringPass();
            _layers = LayerMask.GetMask(LayerName);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (_layers == 0) _layers = LayerMask.GetMask(LayerName); // the layer can be added after the feature
            if (_layers == 0) return;
            var cameraData = renderingData.cameraData;
            if (cameraData.cameraType == CameraType.Reflection) return;
            if ((cameraData.camera.cullingMask & _layers) == 0) return;

            _pass.renderPassEvent = renderPassEvent;
            _pass.Setup(_layers);
            renderer.EnqueuePass(_pass);
        }

        sealed class LetteringPass : ScriptableRenderPass
        {
            // What URP's own transparent pass draws: the lettering's shaders are ordinary unlit particle shaders.
            static readonly List<ShaderTagId> ShaderTags = new List<ShaderTagId>
            {
                new ShaderTagId("SRPDefaultUnlit"),
                new ShaderTagId("UniversalForward"),
                new ShaderTagId("UniversalForwardOnly"),
            };

            int _layers;

            class PassData
            {
                public RendererListHandle renderers;
            }

            public LetteringPass()
            {
                profilingSampler = new ProfilingSampler("Comic Lettering");
            }

            public void Setup(int layers) => _layers = layers;

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var resourceData = frameData.Get<UniversalResourceData>();
                var renderingData = frameData.Get<UniversalRenderingData>();
                var cameraData = frameData.Get<UniversalCameraData>();
                var lightData = frameData.Get<UniversalLightData>();

                DrawingSettings drawing = RenderingUtils.CreateDrawingSettings(ShaderTags, renderingData, cameraData, lightData,
                    SortingCriteria.CommonTransparent);
                var filtering = new FilteringSettings(RenderQueueRange.transparent, _layers);
                var parameters = new RendererListParams(renderingData.cullResults, drawing, filtering);

                using (var builder = renderGraph.AddRasterRenderPass<PassData>("Comic Lettering", out var passData, profilingSampler))
                {
                    passData.renderers = renderGraph.CreateRendererList(parameters);
                    builder.UseRendererList(passData.renderers);
                    builder.SetRenderAttachment(resourceData.activeColorTexture, 0);
                    // Read only: lettering is hidden by the scene and writes no depth of its own.
                    builder.SetRenderAttachmentDepth(resourceData.activeDepthTexture, AccessFlags.Read);
                    builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                        context.cmd.DrawRendererList(data.renderers));
                }
            }
        }
    }
}
