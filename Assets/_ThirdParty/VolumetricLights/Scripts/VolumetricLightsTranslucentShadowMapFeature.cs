using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using System;
#if UNITY_2023_3_OR_NEWER
using UnityEngine.Rendering.RenderGraphModule;
#endif

namespace VolumetricLights {

    public partial class VolumetricLightsTranslucentShadowMapFeature : ScriptableRendererFeature {

        static class ShaderParams {
            public static int MainTex = Shader.PropertyToID("_MainTex");
            public static int BaseMap = Shader.PropertyToID("_BaseMap");
            public static int BaseColor = Shader.PropertyToID("_BaseColor");
            public static int TranslucencyIntensity = Shader.PropertyToID("_TranslucencyIntensity");
            public static int MainTex_ST = Shader.PropertyToID("_MainTex_ST");
            public static int Color = Shader.PropertyToID("_Color");
        }


        public partial class TranspRenderPass : ScriptableRenderPass {

            public VolumetricLightsTranslucentShadowMapFeature settings;

            const string m_strProfilerTag = "TranslucencyPrePass";
            const string m_TranspShader = "Hidden/VolumetricLights/TransparentMultiply";
            const string m_TranspDepthOnlyShader = "Hidden/VolumetricLights/TransparentDepthWrite";

            class PassData {
#if UNITY_2022_3_OR_NEWER
                public RTHandle source;
#else
                public RenderTargetIdentifier source;
#endif
#if UNITY_2023_3_OR_NEWER
                public TextureHandle depthTexture;
#endif
            }

            static Material transpOverrideMat, transpDepthOnlyMat;
            static Material[] transpOverrideMaterials;
            static VolumetricLight light;

            ScriptableRenderer renderer;
#if UNITY_2022_1_OR_NEWER
            RTHandle cameraDepth;
#else
            RenderTargetIdentifier cameraDepth;
#endif

            public TranspRenderPass (VolumetricLightsTranslucentShadowMapFeature settings) {
                this.settings = settings;
                renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
            }

            public void Setup (ScriptableRenderer renderer, VolumetricLight light) {
                this.renderer = renderer;
                TranspRenderPass.light = light;
            }

#if UNITY_2023_3_OR_NEWER

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData) {

                using (var builder = renderGraph.AddUnsafePass<PassData>(m_strProfilerTag, out var passData)) {

                    builder.AllowPassCulling(false);

                    UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();

                    if (light.translucencyMapHandle == null || passData.source == null || passData.source.rt != light.translucentMap) {
                        if (light.translucencyMapHandle != null) {
                            RTHandles.Release(light.translucencyMapHandle);
                        }
                        light.translucencyMapHandle = RTHandles.Alloc(light.translucentMap);
                    }

                    passData.source = light.translucencyMapHandle;
                    // passData.depthTexture = resourceData.activeDepthTexture;
                    // builder.UseTexture(resourceData.activeDepthTexture, AccessFlags.Read);
                    // ConfigureInput(ScriptableRenderPassInput.Depth);

                    builder.SetRenderFunc((PassData passData, UnsafeGraphContext context) => {
                        CommandBuffer cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                        cmd.SetRenderTarget(passData.source); //, passData.depthTexture);
                        cmd.ClearRenderTarget(false, true, Color.white);
                        ExecutePass(cmd);
                    });
                }
            }
#endif
            static void ExecutePass (CommandBuffer cmd) {

                if (transpOverrideMat == null) {
                    Shader transpShader = Shader.Find(m_TranspShader);
                    transpOverrideMat = new Material(transpShader);
                }

                if (transpDepthOnlyMat == null) {
                    Shader transpDepthOnlyShader = Shader.Find(m_TranspDepthOnlyShader);
                    transpDepthOnlyMat = new Material(transpDepthOnlyShader);
                }

                int renderersCount = VolumetricLightsTranslucency.objects.Count;
                if (transpOverrideMaterials == null || transpOverrideMaterials.Length < renderersCount) {
                    transpOverrideMaterials = new Material[renderersCount];
                }

                Bounds lightBounds = light.GetBounds();
                float intensity = 10f * light.shadowTranslucencyIntensity / (light.brightness + 0.001f);
                for (int k = 0; k < renderersCount; k++) {
                    VolumetricLightsTranslucency transpObject = VolumetricLightsTranslucency.objects[k];
                    if (transpObject == null) continue;
                    if (transpObject.intensityMultiplier <= 0) continue;
                    if (transpObject.theRenderer == null) continue;
                    if (!lightBounds.Intersects(transpObject.theRenderer.bounds)) continue;

                    Material mat = transpObject.theRenderer.sharedMaterial;
                    if (mat == null) continue;

                    if (transpObject.preserveOriginalShader) {
                        cmd.DrawRenderer(transpObject.theRenderer, mat);
                        cmd.DrawRenderer(transpObject.theRenderer, transpDepthOnlyMat);
                    } else {

                        if (transpObject.intensityMultiplier <= 0) continue;

                        if (transpOverrideMaterials[k] == null) {
                            transpOverrideMaterials[k] = Instantiate(transpOverrideMat);
                        }
                        Material overrideMaterial = transpOverrideMaterials[k];
                        Texture texture = Texture2D.whiteTexture;
                        Vector2 textureScale = Vector2.one;
                        Vector2 textureOffset = Vector2.zero;
                        Color color = Color.white;
                        if (!string.IsNullOrEmpty(transpObject.baseTexturePropertyName) && mat.HasProperty(transpObject.baseTexturePropertyName)) {
                            texture = mat.GetTexture(transpObject.baseTexturePropertyName);
                            textureScale = mat.GetTextureScale(transpObject.baseTexturePropertyName);
                            textureOffset = mat.GetTextureOffset(transpObject.baseTexturePropertyName);
                        } else if (mat.HasProperty(ShaderParams.BaseMap)) {
                            texture = mat.GetTexture(ShaderParams.BaseMap);
                            textureScale = mat.GetTextureScale(ShaderParams.BaseMap);
                            textureOffset = mat.GetTextureOffset(ShaderParams.BaseMap);
                        } else if (mat.HasProperty(ShaderParams.MainTex)) {
                            texture = mat.GetTexture(ShaderParams.MainTex);
                            textureScale = mat.GetTextureScale(ShaderParams.MainTex);
                            textureOffset = mat.GetTextureOffset(ShaderParams.MainTex);
                        }

                        if (!string.IsNullOrEmpty(transpObject.baseColorPropertyName) && mat.HasProperty(transpObject.baseColorPropertyName)) {
                            color = mat.GetColor(transpObject.baseColorPropertyName);
                        } else
                        if (mat.HasProperty(ShaderParams.BaseColor)) {
                            color = mat.GetColor(ShaderParams.BaseColor);
                        } else
                        if (mat.HasProperty(ShaderParams.Color)) {
                            color = mat.GetColor(ShaderParams.Color);
                        }
                        overrideMaterial.SetTexture(ShaderParams.MainTex, texture);
                        overrideMaterial.SetColor(ShaderParams.Color, color);
                        overrideMaterial.SetVector(ShaderParams.MainTex_ST, new Vector4(textureScale.x, textureScale.y, textureOffset.x, textureOffset.y));
                        overrideMaterial.SetVector(ShaderParams.TranslucencyIntensity, new Vector4(intensity * transpObject.intensityMultiplier, light.shadowTranslucencyBlend, 0, 0));
                        cmd.DrawRenderer(transpObject.theRenderer, overrideMaterial);
                    }
                }

            }

            public void CleanUp () {
                if (light != null) {
                    RTHandles.Release(light.translucencyMapHandle);
                }
                if (transpOverrideMaterials != null) {
                    for (int k = 0; k < transpOverrideMaterials.Length; k++) {
                        DestroyImmediate(transpOverrideMaterials[k]);
                    }
                }
            }

        }

        [Tooltip("If this translucent shadow map render feature can execute on overlay cameras.")]
        public bool ignoreOverlayCamera = true;

        TranspRenderPass m_ScriptablePass;

        public override void Create () {
            m_ScriptablePass = new TranspRenderPass(this);
        }

        private void OnDestroy () {
            if (m_ScriptablePass != null) {
                m_ScriptablePass.CleanUp();
            }
        }

        bool needsTranslucencyTextureCleanUp;

        public override void AddRenderPasses (ScriptableRenderer renderer, ref RenderingData renderingData) {
            if (ignoreOverlayCamera && renderingData.cameraData.renderType == CameraRenderType.Overlay) return;

            int renderersCount = VolumetricLightsTranslucency.objects.Count;
            if (renderersCount == 0) {
                if (!needsTranslucencyTextureCleanUp) return;
                needsTranslucencyTextureCleanUp = false;
            } else {
                needsTranslucencyTextureCleanUp = true;
            }

            Camera cam = renderingData.cameraData.camera;
            Transform parent = cam.transform.parent;
            if (parent == null) return;
            VolumetricLight light = parent.GetComponent<VolumetricLight>();
            if (light == null || !light.shadowTranslucency || light.translucentMap == null) return;
            if (cam.cameraType == CameraType.Reflection && (cam.cullingMask & (1 << light.gameObject.layer)) == 0) return;

            m_ScriptablePass.Setup(renderer, light);
            renderer.EnqueuePass(m_ScriptablePass);
        }

    }



}

