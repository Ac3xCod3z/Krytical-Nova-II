using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace GI
{
    // RenderGraph port: URP 17.6+ (Unity 6.6) removed the legacy Execute(ScriptableRenderContext, ref RenderingData) path.
    // Clears _transparents.Target and draws transparent renderers into it with the TransparentDepth override shader.
    class UniversalTransparentDepthPass : ScriptableRenderPass
    {
        private class PassData
        {
            public RendererListHandle RendererList;
        }

        private static readonly List<ShaderTagId> ShaderTags = new List<ShaderTagId>
        {
            new ShaderTagId("SRPDefaultUnlit"),
            new ShaderTagId("UniversalForward"),
            new ShaderTagId("UniversalForwardOnly"),
        };

        private Transparents _transparents;
        private Shader _overrideShader;
        private FilteringSettings _filtering = new FilteringSettings(RenderQueueRange.transparent);

        private RTHandle _targetHandle;
        private RenderTexture _targetHandleSource;

        public UniversalTransparentDepthPass(Transparents transparents, RenderPassEvent render_event)
        {
            _transparents = transparents;
            renderPassEvent = render_event;
            profilingSampler = new ProfilingSampler("Transparent Depth");

            _overrideShader = Resources.Load<Shader>("Shaders/Render/TransparentDepth");
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var target = _transparents.Target;

            if (target == null)
                return;

            // Transparents recreates its RenderTexture on resize, so refresh the wrapping RTHandle when it changes.
            if (_targetHandle == null || _targetHandleSource != target)
            {
                _targetHandle?.Release();
                _targetHandle = RTHandles.Alloc(target);
                _targetHandleSource = target;
            }

            var cameraData = frameData.Get<UniversalCameraData>();
            var renderingData = frameData.Get<UniversalRenderingData>();
            var lightData = frameData.Get<UniversalLightData>();

            var drawing = RenderingUtils.CreateDrawingSettings(ShaderTags, renderingData, cameraData, lightData, SortingCriteria.CommonTransparent);
            drawing.overrideShader = _overrideShader;
            drawing.overrideShaderPassIndex = 0;

            var targetTexture = renderGraph.ImportTexture(_targetHandle);

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Transparent Depth", out var passData, profilingSampler))
            {
                passData.RendererList = renderGraph.CreateRendererList(new RendererListParams(renderingData.cullResults, drawing, _filtering));

                builder.UseRendererList(passData.RendererList);
                builder.SetRenderAttachment(targetTexture, 0, AccessFlags.Write);
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);

                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                {
                    context.cmd.ClearRenderTarget(false, true, new Color(0, 0, 0, 0));
                    context.cmd.DrawRendererList(data.RendererList);
                });
            }
        }
    }
}
