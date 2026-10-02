using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

#if !UNITY_6000_4_OR_NEWER
namespace VolumetricLights {

    public partial class VolumetricLightsRenderFeature {

        partial class VolumetricLightsRenderPass {

            public override void Configure (CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor) {
                RenderTextureDescriptor lightBufferDesc = cameraTextureDescriptor;
                if (settings.blendMode != BlendMode.Additive) {
                    lightBufferDesc.colorFormat = RenderTextureFormat.ARGBHalf;
                }
                lightBufferDesc.width = GetScaledSize(cameraTextureDescriptor.width, settings.downscaling);
                lightBufferDesc.height = GetScaledSize(cameraTextureDescriptor.height, settings.downscaling);
                lightBufferDesc.depthBufferBits = 0;
                lightBufferDesc.useMipMap = false;
                lightBufferDesc.msaaSamples = 1;
                cmd.GetTemporaryRT(ShaderParams.LightBuffer, lightBufferDesc, FilterMode.Bilinear);
                ConfigureTarget(m_LightBuffer);
                ConfigureClear(ClearFlag.Color, new Color(0, 0, 0, 0));
                ConfigureInput(ScriptableRenderPassInput.Depth);
            }

            public override void Execute (ScriptableRenderContext context, ref RenderingData renderingData) {

                CommandBuffer cmd = CommandBufferPool.Get(m_ProfilerTag);
                cmd.SetGlobalInt(ShaderParams.ForcedInvisible, 0);
                context.ExecuteCommandBuffer(cmd);

                if ((settings.downscaling <= 1f && settings.blurPasses < 1) || VolumetricLight.volumetricLights.Count == 0) {
                    CommandBufferPool.Release(cmd);
                    return;
                }

                foreach (VolumetricLight vl in VolumetricLight.volumetricLights) {
                    if (vl != null && vl.meshRenderer != null) {
                        vl.meshRenderer.renderingLayerMask = renderingLayer;
                    }
                }

                cmd.Clear();

                var sortFlags = SortingCriteria.CommonTransparent;
                var drawSettings = CreateDrawingSettings(shaderTagIdList, ref renderingData, sortFlags);
                var filterSettings = filteringSettings;
                filterSettings.renderingLayerMask = renderingLayer;

                context.DrawRenderers(renderingData.cullResults, ref drawSettings, ref filterSettings);

                RenderTargetIdentifier lightBuffer = new RenderTargetIdentifier(ShaderParams.LightBuffer, 0, CubemapFace.Unknown, -1);
                cmd.SetGlobalTexture(ShaderParams.LightBuffer, lightBuffer);

                CommandBufferPool.Release(cmd);

            }
        }

        partial class BlurRenderPass {

            public override void Configure (CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor) {
                sourceDesc = cameraTextureDescriptor;
                ConfigureInput(ScriptableRenderPassInput.Depth);
            }

            public override void Execute (ScriptableRenderContext context, ref RenderingData renderingData) {

#if UNITY_2022_1_OR_NEWER
                passData.source = renderer.cameraColorTargetHandle;
#else
                passData.source = renderer.cameraColorTarget;
#endif
                CommandBuffer cmd = CommandBufferPool.Get(m_strProfilerTag);
                ExecutePass(passData, cmd);
                context.ExecuteCommandBuffer(cmd);

                CommandBufferPool.Release(cmd);
            }
        }
    }
}
#endif
