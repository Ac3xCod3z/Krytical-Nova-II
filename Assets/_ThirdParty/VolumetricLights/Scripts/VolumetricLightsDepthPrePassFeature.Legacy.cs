using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

#if !UNITY_6000_4_OR_NEWER
namespace VolumetricLights {

    public partial class VolumetricLightsDepthPrePassFeature {

        public partial class DepthRenderPass {

            public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor) {
                if (settings.transparentLayerMask != filterSettings.layerMask || settings.alphaCutoutLayerMask != currentCutoutLayerMask) {
                    filterSettings = new FilteringSettings(RenderQueueRange.transparent, settings.transparentLayerMask);
                    if (settings.alphaCutoutLayerMask != currentCutoutLayerMask) {
                        FindAlphaClippingRenderers();
                    }
                    currentCutoutLayerMask = settings.alphaCutoutLayerMask;
                    SetupKeywords();
                }
                RenderTextureDescriptor depthDesc = cameraTextureDescriptor;
                depthDesc.colorFormat = RenderTextureFormat.Depth;
                depthDesc.depthBufferBits = 24;
                depthDesc.msaaSamples = 1;

                cmd.GetTemporaryRT(ShaderParams.CustomDepthTexture, depthDesc, FilterMode.Point);
                cmd.SetGlobalTexture(ShaderParams.CustomDepthTexture, m_Depth);
                ConfigureTarget(m_Depth);
                ConfigureClear(ClearFlag.All, Color.black);
            }

            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData) {

                if (settings.transparentLayerMask == 0 && settings.alphaCutoutLayerMask == 0) return;

                CommandBuffer cmd = CommandBufferPool.Get(m_ProfilerTag);
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                if (settings.alphaCutoutLayerMask != 0) {
                    if (depthOnlyMaterialCutOff == null) {
                        Shader depthOnlyCutOff = Shader.Find(m_DepthOnlyShader);
                        depthOnlyMaterialCutOff = new Material(depthOnlyCutOff);
                    }
                    int renderersCount = cutOutRenderers.Count;
                    if (depthOverrideMaterials == null || depthOverrideMaterials.Length < renderersCount) {
                        depthOverrideMaterials = new Material[renderersCount];
                    }
                    for (int k = 0; k < renderersCount; k++) {
                        Renderer renderer = cutOutRenderers[k];
                        if (renderer != null && renderer.isVisible) {
                            Material mat = renderer.sharedMaterial;
                            if (mat != null) {
                                if (depthOverrideMaterials[k] == null) {
                                    depthOverrideMaterials[k] = Instantiate(depthOnlyMaterialCutOff);
                                    depthOverrideMaterials[k].EnableKeyword(ShaderParams.SKW_CUSTOM_DEPTH_ALPHA_TEST);
                                }
                                Material overrideMaterial = depthOverrideMaterials[k];
                                overrideMaterial.SetFloat(ShaderParams.CustomDepthAlphaCutoff, settings.alphaCutOff);
                                if (mat.HasProperty(ShaderParams.CustomDepthBaseMap)) {
                                    overrideMaterial.SetTexture(ShaderParams.CustomDepthBaseMap, mat.GetTexture(ShaderParams.CustomDepthBaseMap));
                                } else if (mat.HasProperty(ShaderParams.MainTex)) {
                                    overrideMaterial.SetTexture(ShaderParams.CustomDepthBaseMap, mat.GetTexture(ShaderParams.MainTex));
                                }
                                if (mat.HasProperty(ShaderParams.CullMode)) {
                                    overrideMaterial.SetInt(ShaderParams.CullMode, mat.GetInt(ShaderParams.CullMode));
                                } else {
                                    overrideMaterial.SetInt(ShaderParams.CullMode, (int)settings.semiTransparentCullMode);
                                }
                                cmd.DrawRenderer(renderer, overrideMaterial);
                            }
                        }
                    }

                }

                if (settings.transparentLayerMask != 0) {
                    SortingCriteria sortingCriteria = SortingCriteria.CommonTransparent;
                    var drawSettings = CreateDrawingSettings(shaderTagIdList, ref renderingData, sortingCriteria);
                    drawSettings.perObjectData = PerObjectData.None;
                    if (settings.useOptimizedDepthOnlyShader) {
                        if (depthOnlyMaterial == null) {
                            Shader depthOnly = Shader.Find(m_DepthOnlyShader);
                            depthOnlyMaterial = new Material(depthOnly);
                        }
                        depthOnlyMaterial.SetInt(ShaderParams.CullMode, (int)settings.transparentCullMode);
                        drawSettings.overrideMaterial = depthOnlyMaterial;
                    }
                    context.DrawRenderers(renderingData.cullResults, ref drawSettings, ref filterSettings);
                }

                context.ExecuteCommandBuffer(cmd);

                CommandBufferPool.Release(cmd);
            }
        }
    }
}
#endif
