// The corpse of a Hostile burning away. The body keeps its texture and a plain version of its lighting while the
// "Fade Burn" of the All In 1 Shader Nodes library eats it along a noise, with a bright edge where it burns.
// HostileCorpse swaps this in for the model's own materials when the dissolve starts and drives _FadeAmount.
Shader "KryticalNova/Hostiles/Corpse Dissolve"
{
    Properties
    {
        _BaseMap ("Base Map", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        _EmissionMap ("Emission Map", 2D) = "black" {}
        [HDR] _EmissionColor ("Emission Color", Color) = (0, 0, 0, 1)
        _FadeTex ("Dissolve Noise", 2D) = "white" {}
        _FadeAmount ("Dissolve Amount", Range(0, 1)) = 0
        _FadePower ("Noise Power", Range(0.2, 4)) = 1
        _FadeTransition ("Edge Softness", Range(0.001, 0.2)) = 0.02
        _FadeBurnWidth ("Burn Width", Range(0, 0.3)) = 0.07
        [HDR] _FadeBurnColor ("Burn Color", Color) = (0.2, 3, 3.4, 1)
        _FadeTiling ("Noise Tiling (per metre)", Float) = 0.8
    }

    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" "RenderPipeline" = "UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half4 _EmissionColor;
            half4 _FadeBurnColor;
            float _FadeAmount;
            float _FadePower;
            float _FadeTransition;
            float _FadeBurnWidth;
            float _FadeTiling;
        CBUFFER_END

        sampler2D _BaseMap;
        sampler2D _EmissionMap;
        sampler2D _FadeTex;

        // The library is written for Shader Graph and Amplify; these are the two macros its functions need here.
        #define TEX_PARAM sampler2D
        #define SAMPLE_TEX2D(texName, uv) tex2D(texName, uv)
        #include "Assets/Plugins/AllIn1ShaderNodes/Shaders/AllIn1NodeLibrary_AlphaEffects.hlsl"

        // Synty models are painted from a palette: a whole limb can share one texel, so the UVs of the model
        // would dissolve it in blocks. The noise is laid over the body in world space instead.
        float2 NoiseUV(float3 positionWS)
        {
            return positionWS.xz + positionWS.y * float2(0.61, 0.37);
        }

        float4 Burn(float3 rgb, float3 positionWS)
        {
            return Fade_Burn(rgb, 1.0, _FadePower, _FadeAmount, _FadeTransition, _FadeBurnWidth, _FadeBurnColor,
                _FadeTex, NoiseUV(positionWS), float2(_FadeTiling, _FadeTiling));
        }

        struct DepthAttributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
        struct DepthVaryings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; };

        DepthVaryings DepthVert(DepthAttributes input)
        {
            DepthVaryings output;
            output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
            output.positionCS = TransformWorldToHClip(output.positionWS);
            output.normalWS = TransformObjectToWorldNormal(input.normalOS);
            return output;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float fogFactor : TEXCOORD3;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half3 albedo = tex2D(_BaseMap, input.uv).rgb * _BaseColor.rgb;
                half3 normal = normalize(input.normalWS);
                Light light = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half diffuse = saturate(dot(normal, light.direction)) * light.shadowAttenuation;
                half3 lit = albedo * (light.color * diffuse + SampleSH(normal));
                lit += tex2D(_EmissionMap, input.uv).rgb * _EmissionColor.rgb;

                float4 burned = Burn(lit, input.positionWS);
                clip(burned.a - 0.5);
                burned.rgb = MixFog(burned.rgb, input.fogFactor);
                return half4(burned.rgb, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;

            DepthVaryings ShadowVert(DepthAttributes input)
            {
                DepthVaryings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(output.positionWS, output.normalWS, _LightDirection));
                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                output.positionCS = positionCS;
                return output;
            }

            half4 ShadowFrag(DepthVaryings input) : SV_Target
            {
                clip(Burn(0, input.positionWS).a - 0.5);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag

            half4 DepthFrag(DepthVaryings input) : SV_Target
            {
                clip(Burn(0, input.positionWS).a - 0.5);
                return input.positionCS.z;
            }
            ENDHLSL
        }

        // The comic ink pass reads depth and normals: without this a dissolving body would keep its whole outline.
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthNormalsFrag

            half4 DepthNormalsFrag(DepthVaryings input) : SV_Target
            {
                clip(Burn(0, input.positionWS).a - 0.5);
                return half4(normalize(input.normalWS), 0);
            }
            ENDHLSL
        }
    }
}
