// Grass, flowers, bushes and trees that move with the weather wind and part around players.
// Lighting matches KryticalNova/Comic Toon Lit (cel band + Ben-Day dots).
// All motion is in the vertex stage (no vertex texture fetches); every pass runs the same
// displacement so shadows and the ComicPost outline (depth/normals) stay glued to the mesh.
// Grass = default mode (bends from the base). Trees/bushes = _TREE_MODE (trunk planted,
// canopy sways, branch ends flutter).
Shader "KryticalNova/Foliage Wind"
{
    Properties
    {
        [MainTexture] _BaseMap("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        _ColorVariation("Colour Variation A (rgb warm tint, a amount)", Color) = (0.92, 1.0, 0.7, 0.4)
        _ColorVariation2("Colour Variation B (rgb cool tint)", Color) = (0.72, 0.9, 0.78, 1)

        [Toggle(_ALPHATEST_ON)] _AlphaClip("Alpha Clip", Float) = 1
        _Cutoff("Alpha Cutoff", Range(0, 1)) = 0.5
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 0
        _NormalUp("Normals Toward Up", Range(0, 1)) = 0.6

        [Header(Wind)]
        [Toggle(_TREE_MODE)] _TreeMode("Tree Mode", Float) = 0
        _BendHeight("Bend Height (object units)", Float) = 0.8
        _TrunkHeight("Trunk Height (tree mode)", Float) = 1.5
        _CanopyRadius("Canopy Radius (tree mode)", Float) = 2.5
        _WindStrength("Wind Strength", Range(0, 2)) = 1
        _FlutterStrength("Flutter Strength", Range(0, 0.3)) = 0.04
        _IdleSway("Calm-Day Sway", Range(0, 0.2)) = 0.03
        _InteractStrength("Player Push Strength", Range(0, 2)) = 0.9
        _WindFadeDistance("Motion Fade Distance (m)", Float) = 60
    }

    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "RenderPipeline" = "UniversalPipeline" "Queue" = "AlphaTest" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex FoliageVert
            #pragma fragment FoliageFrag

            #pragma shader_feature_local _ALPHATEST_ON
            #pragma shader_feature_local_vertex _TREE_MODE

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            #include "KNFoliageCommon.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Assets/_Game/Rendering/Shaders/Comic/ComicHalftone.hlsl"

            half _ComicShadowThreshold;
            half _ComicShadowSoftness;
            half _ComicShadowBrightness;
            half _ComicDotStart;
            half _ComicPostShadows;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half3  normalWS   : TEXCOORD2;
                half3  tint       : TEXCOORD3;
                half   fogFactor  : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings FoliageVert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 positionWS = KNFoliageDisplace(input.positionOS.xyz, TransformObjectToWorld(input.positionOS.xyz));
                output.positionWS = positionWS;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.normalWS = KNFoliageNormal(TransformObjectToWorldNormal(input.normalOS));
                output.tint = KNFoliageTint(_BaseColor.rgb);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half ToonBand(half intensity)
            {
                half soft = max(_ComicShadowSoftness, 0.001);
                return smoothstep(_ComicShadowThreshold - soft, _ComicShadowThreshold + soft, intensity);
            }

            half4 FoliageFrag(Varyings input, bool isFrontFace : SV_IsFrontFace) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 albedoAlpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
            #if defined(_ALPHATEST_ON)
                clip(albedoAlpha.a * _BaseColor.a - _Cutoff);
            #endif
                half3 albedo = albedoAlpha.rgb * input.tint;

                half3 normalWS = normalize(input.normalWS);
                // Two-sided blades: light the back face like the front.
                normalWS = isFrontFace ? normalWS : lerp(-normalWS, normalWS, _NormalUp);

                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                half4 shadowMask = half4(1, 1, 1, 1);
            #ifdef _LIGHT_LAYERS
                uint meshRenderingLayers = GetMeshRenderingLayer();
            #endif

                half3 ambient = SampleSH(normalWS);

                Light mainLight = GetMainLight(shadowCoord, input.positionWS, shadowMask);
                half mainShadow = _ComicPostShadows > 0.5 ? 1.0 : mainLight.shadowAttenuation;
                half mainIntensity = saturate(dot(normalWS, mainLight.direction)) * mainShadow * mainLight.distanceAttenuation;
            #ifdef _LIGHT_LAYERS
                if (!IsMatchingLightLayer(mainLight.layerMask, meshRenderingLayers)) mainIntensity = 0;
            #endif
                half3 lighting = mainLight.color * lerp(_ComicShadowBrightness, 1.0, ToonBand(mainIntensity));
                half lightAmount = mainIntensity;

            #if defined(_ADDITIONAL_LIGHTS)
                uint lightCount = GetAdditionalLightsCount();
                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);

                #if USE_CLUSTER_LIGHT_LOOP
                [loop] for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
                {
                    CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK
                    Light light = GetAdditionalLight(lightIndex, input.positionWS, shadowMask);
                    half intensity = saturate(dot(normalWS, light.direction)) * light.shadowAttenuation * light.distanceAttenuation;
                    lighting += light.color * ToonBand(intensity);
                    lightAmount += intensity * saturate(Luminance(light.color));
                }
                #endif

                LIGHT_LOOP_BEGIN(lightCount)
                    Light light = GetAdditionalLight(lightIndex, input.positionWS, shadowMask);
                    half intensity = saturate(dot(normalWS, light.direction)) * light.shadowAttenuation * light.distanceAttenuation;
                    lighting += light.color * ToonBand(intensity);
                    lightAmount += intensity * saturate(Luminance(light.color));
                LIGHT_LOOP_END
            #endif

                half3 color = albedo * (lighting + ambient);
                half dotStart = max(_ComicDotStart, 0.001);
                color = ComicApplyDots(color, input.positionCS.xy, saturate((dotStart - saturate(lightAmount)) / dotStart));
                color = MixFog(color, input.fogFactor);
                return half4(color, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0 Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ShadowVert
            #pragma fragment ClipFrag
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma shader_feature_local_vertex _TREE_MODE
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            #include "KNFoliageCommon.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };

            Varyings ShadowVert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                float3 positionWS = KNFoliageDisplace(input.positionOS.xyz, TransformObjectToWorld(input.positionOS.xyz));
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDir = normalize(_LightPosition - positionWS);
            #else
                float3 lightDir = _LightDirection;
            #endif
                output.positionCS = ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDir)));
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half4 ClipFrag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
            #if defined(_ALPHATEST_ON)
                clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a * _BaseColor.a - _Cutoff);
            #endif
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask R Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DepthVert
            #pragma fragment ClipFrag
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma shader_feature_local_vertex _TREE_MODE
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            #include "KNFoliageCommon.hlsl"

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID UNITY_VERTEX_OUTPUT_STEREO };

            Varyings DepthVert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                float3 positionWS = KNFoliageDisplace(input.positionOS.xyz, TransformObjectToWorld(input.positionOS.xyz));
                output.positionCS = TransformWorldToHClip(positionWS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half ClipFrag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
            #if defined(_ALPHATEST_ON)
                clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a * _BaseColor.a - _Cutoff);
            #endif
                return input.positionCS.z;
            }
            ENDHLSL
        }

        // Required so the ComicPost outline pass sees the bent normals/depth.
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DepthNormalsVert
            #pragma fragment DepthNormalsFrag
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma shader_feature_local_vertex _TREE_MODE
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            #include "KNFoliageCommon.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half3 normalWS : TEXCOORD1; UNITY_VERTEX_INPUT_INSTANCE_ID UNITY_VERTEX_OUTPUT_STEREO };

            Varyings DepthNormalsVert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                float3 positionWS = KNFoliageDisplace(input.positionOS.xyz, TransformObjectToWorld(input.positionOS.xyz));
                output.positionCS = TransformWorldToHClip(positionWS);
                output.normalWS = KNFoliageNormal(TransformObjectToWorldNormal(input.normalOS));
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            void DepthNormalsFrag(Varyings input, bool isFrontFace : SV_IsFrontFace
                , out half4 outNormalWS : SV_Target0
            #ifdef _WRITE_RENDERING_LAYERS
                , out uint outRenderingLayers : SV_Target1
            #endif
            )
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            #if defined(_ALPHATEST_ON)
                clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a * _BaseColor.a - _Cutoff);
            #endif
                float3 normalWS = normalize(input.normalWS);
                normalWS = isFrontFace ? normalWS : lerp(-normalWS, normalWS, _NormalUp);
            #if defined(_GBUFFER_NORMALS_OCT)
                float2 oct = saturate(PackNormalOctQuadEncode(normalWS) * 0.5 + 0.5);
                outNormalWS = half4(PackFloat2To888(oct), 0.0);
            #else
                outNormalWS = half4(NormalizeNormalPerPixel(normalWS), 0.0);
            #endif
            #ifdef _WRITE_RENDERING_LAYERS
                outRenderingLayers = EncodeMeshRenderingLayer();
            #endif
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
