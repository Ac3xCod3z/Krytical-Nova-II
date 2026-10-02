using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

#if !UNITY_6000_4_OR_NEWER
namespace VolumetricLights {

    public partial class VolumetricLightsTranslucentShadowMapFeature {

        public partial class TranspRenderPass {

            public override void OnCameraSetup (CommandBuffer cmd, ref RenderingData renderingData) {
                base.OnCameraSetup(cmd, ref renderingData);
#if UNITY_2022_1_OR_NEWER
                cameraDepth = renderer.cameraDepthTargetHandle;
#else
                cameraDepth = renderer.cameraDepthTarget;
#endif
            }

            public override void Configure (CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor) {

                if (light.translucencyMapHandle == null || light.translucencyMapHandle.rt != light.translucentMap) {
                    if (light.translucencyMapHandle != null) {
                        RTHandles.Release(light.translucencyMapHandle);
                    }
                    light.translucencyMapHandle = RTHandles.Alloc(light.translucentMap);
                }

                ConfigureTarget(light.translucencyMapHandle);
                ConfigureClear(ClearFlag.Color, Color.white);
            }

            public override void Execute (ScriptableRenderContext context, ref RenderingData renderingData) {
                CommandBuffer cmd = CommandBufferPool.Get(m_strProfilerTag);
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                ExecutePass(cmd);

                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }
        }
    }
}
#endif
