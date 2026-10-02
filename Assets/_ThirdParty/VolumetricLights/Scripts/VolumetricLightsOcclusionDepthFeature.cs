//------------------------------------------------------------------------------------------------------------------
// Volumetric Lights
// Created by Kronnect
//------------------------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#if UNITY_2023_3_OR_NEWER
using UnityEngine.Rendering.RenderGraphModule;
#endif

namespace VolumetricLights {

    public partial class VolumetricLightsOcclusionDepthFeature : ScriptableRendererFeature {

        public partial class OcclusionDepthPass : ScriptableRenderPass {

            const string m_ProfilerTag = "Volumetric Lights Occlusion Depth";

            static readonly List<ShaderTagId> shaderTagIdList = new List<ShaderTagId>();
            static FilteringSettings filterSettings;
            static VolumetricLight light;

            public OcclusionDepthPass () {
                renderPassEvent = RenderPassEvent.BeforeRenderingOpaques;
                shaderTagIdList.Clear();
                shaderTagIdList.Add(new ShaderTagId("DepthOnly"));
                filterSettings = new FilteringSettings(RenderQueueRange.opaque, -1);
            }

            public void Setup (VolumetricLight light) {
                OcclusionDepthPass.light = light;
                filterSettings.renderQueueRange = light.shadowIncludeTransparent ? RenderQueueRange.all : RenderQueueRange.opaque;
            }

            static void EnsureHandle () {
                if (light.occlusionDepthHandle == null || light.occlusionDepthHandle.rt != light.rt) {
                    if (light.occlusionDepthHandle != null) {
                        RTHandles.Release(light.occlusionDepthHandle);
                    }
                    light.occlusionDepthHandle = RTHandles.Alloc(light.rt);
                }
            }

#if UNITY_2023_3_OR_NEWER

            class PassData {
                public RendererListHandle rendererListHandle;
                public RTHandle depth;
            }

            public override void RecordRenderGraph (RenderGraph renderGraph, ContextContainer frameData) {

                if (light == null || light.rt == null) return;
                EnsureHandle();

                using (var builder = renderGraph.AddUnsafePass<PassData>(m_ProfilerTag, out var passData)) {

                    builder.AllowPassCulling(false);

                    UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
                    UniversalLightData lightData = frameData.Get<UniversalLightData>();
                    UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();

                    var drawingSettings = CreateDrawingSettings(shaderTagIdList, renderingData, cameraData, lightData, SortingCriteria.CommonOpaque);
                    drawingSettings.perObjectData = PerObjectData.None;
                    RendererListParams listParams = new RendererListParams(renderingData.cullResults, drawingSettings, filterSettings);
                    passData.rendererListHandle = renderGraph.CreateRendererList(listParams);
                    passData.depth = light.occlusionDepthHandle;
                    builder.UseRendererList(passData.rendererListHandle);

                    builder.SetRenderFunc((PassData passData, UnsafeGraphContext context) => {
                        CommandBuffer cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                        cmd.SetRenderTarget(passData.depth);
                        cmd.ClearRenderTarget(true, false, Color.clear);
                        cmd.DrawRendererList(passData.rendererListHandle);
                    });
                }
            }
#endif

        }

        OcclusionDepthPass m_ScriptablePass;

        public override void Create () {
            name = "Volumetric Lights Occlusion Depth";
            m_ScriptablePass = new OcclusionDepthPass();
        }

        // Only inject on the per-light OcclusionCam (a child camera of a VolumetricLight).
        public override void AddRenderPasses (ScriptableRenderer renderer, ref RenderingData renderingData) {
            Camera cam = renderingData.cameraData.camera;
            Transform parent = cam.transform.parent;
            if (parent == null) return;
            if (cam.gameObject.name != "OcclusionCam") return;
            VolumetricLight light = parent.GetComponent<VolumetricLight>();
            if (light == null || light.rt == null) return;
            m_ScriptablePass.Setup(light);
            renderer.EnqueuePass(m_ScriptablePass);
        }

    }

}
