//------------------------------------------------------------------------------------------------------------------
// Volumetric Lights
// Created by Kronnect
//------------------------------------------------------------------------------------------------------------------

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

#if !UNITY_6000_4_OR_NEWER
namespace VolumetricLights {

    public partial class VolumetricLightsOcclusionDepthFeature {

        public partial class OcclusionDepthPass {

            public override void Configure (CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor) {
                if (light == null || light.rt == null) return;
                EnsureHandle();
                ConfigureTarget(light.occlusionDepthHandle);
                ConfigureClear(ClearFlag.Depth, Color.clear);
            }

            public override void Execute (ScriptableRenderContext context, ref RenderingData renderingData) {
                if (light == null || light.rt == null) return;

                CommandBuffer cmd = CommandBufferPool.Get(m_ProfilerTag);
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                var drawSettings = CreateDrawingSettings(shaderTagIdList, ref renderingData, SortingCriteria.CommonOpaque);
                drawSettings.perObjectData = PerObjectData.None;
                context.DrawRenderers(renderingData.cullResults, ref drawSettings, ref filterSettings);

                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }
        }
    }
}
#endif
