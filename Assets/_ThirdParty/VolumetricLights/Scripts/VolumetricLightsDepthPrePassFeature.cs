using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using System.Collections.Generic;
using System;
#if UNITY_2023_3_OR_NEWER
using UnityEngine.Rendering.RenderGraphModule;
#endif

namespace VolumetricLights {

    public partial class VolumetricLightsDepthPrePassFeature : ScriptableRendererFeature {

        static class ShaderParams {
            public static int MainTex = Shader.PropertyToID("_MainTex");
            public static int CustomDepthTexture = Shader.PropertyToID("_CustomDepthTexture");
            public static int CustomDepthAlphaCutoff = Shader.PropertyToID("_AlphaCutOff");
            public static int CustomDepthBaseMap = Shader.PropertyToID("_BaseMap");
            public static int CullMode = Shader.PropertyToID("_Cull");

            public const string SKW_DEPTH_PREPASS = "VF2_DEPTH_PREPASS";
            public const string SKW_CUSTOM_DEPTH_ALPHA_TEST = "DEPTH_PREPASS_ALPHA_TEST";
        }


        public partial class DepthRenderPass : ScriptableRenderPass {

            public VolumetricLightsDepthPrePassFeature settings;

            const string m_ProfilerTag = "CustomDepthPrePass";
            const string m_DepthOnlyShader = "Hidden/VolumetricLights/DepthOnly";
            const string m_CustomDepthTextureName = "_CustomDepthTexture";

            static FilteringSettings filterSettings;
            int currentCutoutLayerMask;
            static readonly List<ShaderTagId> shaderTagIdList = new List<ShaderTagId>();
            readonly List<Renderer> cutOutRenderers = new List<Renderer>();

            RTHandle m_Depth;
            Material depthOnlyMaterial, depthOnlyMaterialCutOff;
            Material[] depthOverrideMaterials;

            public DepthRenderPass(VolumetricLightsDepthPrePassFeature settings) {
                this.settings = settings;
                renderPassEvent = RenderPassEvent.AfterRenderingOpaques;

                //m_Depth.Init("_CustomDepthTexture");
                m_Depth = RTHandles.Alloc(ShaderParams.CustomDepthTexture, name: m_CustomDepthTextureName);
                shaderTagIdList.Clear();
                shaderTagIdList.Add(new ShaderTagId("SRPDefaultUnlit"));
                shaderTagIdList.Add(new ShaderTagId("UniversalForward"));
                shaderTagIdList.Add(new ShaderTagId("LightweightForward"));
                filterSettings = new FilteringSettings(RenderQueueRange.transparent, 0);
                SetupKeywords();
                FindAlphaClippingRenderers();
            }

            void SetupKeywords() {
                if (settings.transparentLayerMask != 0 || settings.alphaCutoutLayerMask != 0) {
                    Shader.EnableKeyword(ShaderParams.SKW_DEPTH_PREPASS);
                } else {
                    Shader.DisableKeyword(ShaderParams.SKW_DEPTH_PREPASS);
                }
            }

            void FindAlphaClippingRenderers() {
                cutOutRenderers.Clear();
                if (settings.alphaCutoutLayerMask == 0) return;
                Renderer[] rr = Misc.FindObjectsOfType<Renderer>();
                for (int r = 0; r < rr.Length; r++) {
                    if (((1 << rr[r].gameObject.layer) & settings.alphaCutoutLayerMask) != 0) {
                        cutOutRenderers.Add(rr[r]);
                    }
                }
            }

#if UNITY_2023_3_OR_NEWER

            class PassData {
                public RendererListHandle rendererListHandle;
                public UniversalCameraData cameraData;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData) {

                if (settings.transparentLayerMask == 0 && settings.alphaCutoutLayerMask == 0) {
                    SetupKeywords();
                    return;
                }

                using (var builder = renderGraph.AddUnsafePass<PassData>(m_ProfilerTag, out var passData)) {

                    builder.AllowPassCulling(false);

                    UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
                    UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
                    UniversalLightData lightData = frameData.Get<UniversalLightData>();
                    UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                    passData.cameraData = cameraData;

                    SortingCriteria sortingCriteria = SortingCriteria.CommonTransparent;
                    var drawingSettings = CreateDrawingSettings(shaderTagIdList, renderingData, cameraData, lightData, sortingCriteria);
                    drawingSettings.perObjectData = PerObjectData.None;
                    if (settings.useOptimizedDepthOnlyShader) {
                        if (depthOnlyMaterial == null) {
                            Shader depthOnly = Shader.Find(m_DepthOnlyShader);
                            depthOnlyMaterial = new Material(depthOnly);
                        }
                        depthOnlyMaterial.SetInt(ShaderParams.CullMode, (int)settings.transparentCullMode);
                        drawingSettings.overrideMaterial = depthOnlyMaterial;
                    }
                    RendererListParams listParams = new RendererListParams(renderingData.cullResults, drawingSettings, filterSettings);
                    passData.rendererListHandle = renderGraph.CreateRendererList(listParams);
                    builder.UseRendererList(passData.rendererListHandle);

                    builder.SetRenderFunc((PassData passData, UnsafeGraphContext context) => {

                        CommandBuffer cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);

                        if (settings.transparentLayerMask != filterSettings.layerMask || settings.alphaCutoutLayerMask != currentCutoutLayerMask) {
                            filterSettings = new FilteringSettings(RenderQueueRange.transparent, settings.transparentLayerMask);
                            if (settings.alphaCutoutLayerMask != currentCutoutLayerMask) {
                                FindAlphaClippingRenderers();
                            }
                            currentCutoutLayerMask = settings.alphaCutoutLayerMask;
                            SetupKeywords();
                        }
                        RenderTextureDescriptor depthDesc = passData.cameraData.cameraTargetDescriptor;
                        depthDesc.colorFormat = RenderTextureFormat.Depth;
                        depthDesc.depthBufferBits = 24;
                        depthDesc.msaaSamples = 1;

                        cmd.GetTemporaryRT(ShaderParams.CustomDepthTexture, depthDesc, FilterMode.Point);
                        cmd.SetGlobalTexture(ShaderParams.CustomDepthTexture, m_Depth);
                        RenderTargetIdentifier rti = new RenderTargetIdentifier(ShaderParams.CustomDepthTexture, 0, CubemapFace.Unknown, -1);
                        cmd.SetRenderTarget(rti);
                        cmd.ClearRenderTarget(true, true, Color.black);

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
                            cmd.DrawRendererList(passData.rendererListHandle);
                        }
                    });
                }
            }

#endif

            /// Cleanup any allocated resources that were created during the execution of this render pass.
            public override void FrameCleanup(CommandBuffer cmd) {
                if (cmd == null) return;
                cmd.ReleaseTemporaryRT(ShaderParams.CustomDepthTexture);
            }

            public void CleanUp() {
                RTHandles.Release(m_Depth);
            }
        }

        [Tooltip("Optionally specify which transparent layers must be included in the depth prepass. Use only to avoid fog clipping with certain transparent objects.")]
        public LayerMask transparentLayerMask;
        [Tooltip("Uses an optimized shader to compute depth for objects in the Transparent Layer Mask. Disable to use each object's own shader (useful for shaders with vertex transformations like wind or for custom alpha clipping not exposed via _BaseMap/_MainTex).")]
        public bool useOptimizedDepthOnlyShader = true;
        [Tooltip("Cull mode used by the optimized depth-only shader when rendering Transparent Layer Mask objects.")]
        public CullMode transparentCullMode = CullMode.Back;
        [Tooltip("Optionally specify which semi-transparent (materials using alpha clipping or cut-off) must be included in the depth prepass. Use only to avoid fog clipping with certain transparent objects.")]
        public LayerMask alphaCutoutLayerMask;
        [Tooltip("Optionally determines the alpha cut off for semitransparent objects.")]
        [Range(0, 1)]
        public float alphaCutOff;
        [Tooltip("Fallback cull mode used by the depth-only override material for Alpha Cutout Layer Mask objects when their material does not expose a _Cull property.")]
        public CullMode semiTransparentCullMode = CullMode.Back;

        [Tooltip("If this depth pre-pass render feature can execute on reflection probes.")]
        public bool ignoreReflectionProbes = true;

        [Tooltip("If this depth pre-pass render feature can execute on overlay cameras.")]
        public bool ignoreOverlayCamera = true;

        DepthRenderPass m_ScriptablePass;
        public static bool installed;

        public override void Create() {
            m_ScriptablePass = new DepthRenderPass(this);
        }

        void OnDestroy() {
            installed = false;
            if (m_ScriptablePass != null) {
                m_ScriptablePass.CleanUp();
            }
            Shader.DisableKeyword(ShaderParams.SKW_DEPTH_PREPASS);
        }

        // Here you can inject one or multiple render passes in the renderer.
        // This method is called when setting up the renderer once per-camera.
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData) {
            if (ignoreOverlayCamera && renderingData.cameraData.renderType == CameraRenderType.Overlay) return;
            Camera cam = renderingData.cameraData.camera;
            if (cam.targetTexture != null && cam.targetTexture.format == RenderTextureFormat.Depth) return; // ignore occlusion cams!
            if (ignoreReflectionProbes && cam.cameraType == CameraType.Reflection) return;

            installed = true;
            renderer.EnqueuePass(m_ScriptablePass);
        }

    }



}