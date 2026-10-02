// Stand-in material for world props that block the camera's view of the player (driven by OccluderFader).
// Mirrors the Synty Generic_Basic inputs (albedo / normal / emission atlases) with URP PBR lighting, plus a
// screen-space ordered (Bayer) dither: as _Fade rises, a growing share of pixels is discarded, which reads as a
// comic stipple and needs no transparency sorting. Depth/normals are dithered the same way so the comic outlines
// trace the holes; the shadow caster is NOT dithered, so a faded wall still casts its full shadow.
Shader "KryticalNova/Occluder Dither"
{
    Properties
    {
        _Albedo_Map ("Albedo", 2D) = "white" {}
        _BaseColor ("Tint", Color) = (1, 1, 1, 1)
        [Normal] _Normal_Map ("Normal", 2D) = "bump" {}
        _Normal_Amount ("Normal Amount", Range(0, 2)) = 0.5
        _Emission_Map ("Emission", 2D) = "black" {}
        _Emission_Color ("Emission Colour", Color) = (0, 0, 0, 0)
        _Smoothness ("Smoothness", Range(0, 1)) = 0.2
        _Metallic ("Metallic", Range(0, 1)) = 0
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.5
        _Fade ("Fade (0 solid, 1 gone)", Range(0, 1)) = 0
        _DitherScale ("Dither Cell (px)", Range(1, 4)) = 2
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "AlphaTest" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_Albedo_Map);   SAMPLER(sampler_Albedo_Map);
        TEXTURE2D(_Normal_Map);   SAMPLER(sampler_Normal_Map);
        TEXTURE2D(_Emission_Map); SAMPLER(sampler_Emission_Map);

        CBUFFER_START(UnityPerMaterial)
            float4 _Albedo_Map_ST;
            half4  _BaseColor;
            half   _Normal_Amount;
            half4  _Emission_Color;
            half   _Smoothness;
            half   _Metallic;
            half   _Cutoff;
            half   _Fade;
            half   _DitherScale;
        CBUFFER_END

        // 4x4 ordered dither, thresholds in (0, 1). Pixel survives while its threshold exceeds _Fade.
        void DitherClip(float2 positionCS, half albedoAlpha)
        {
            clip(albedoAlpha - _Cutoff);
            if (_Fade <= 0.001) return;
            const float bayer[16] = {
                 0.0,  8.0,  2.0, 10.0,
                12.0,  4.0, 14.0,  6.0,
                 3.0, 11.0,  1.0,  9.0,
                15.0,  7.0, 13.0,  5.0 };
            uint2 p = (uint2)floor(positionCS / max(_DitherScale, 1.0)) % 4;
            float threshold = (bayer[p.y * 4 + p.x] + 0.5) / 16.0;
            clip(threshold - _Fade);
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half3  normalWS   : TEXCOORD2;
                half4  tangentWS  : TEXCOORD3;
                half   fogFactor  : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs nrm = GetVertexNormalInputs(input.normalOS, input.tangentOS);
                o.positionCS = pos.positionCS;
                o.positionWS = pos.positionWS;
                o.normalWS = nrm.normalWS;
                o.tangentWS = half4(nrm.tangentWS, input.tangentOS.w * GetOddNegativeScale());
                o.uv = TRANSFORM_TEX(input.uv, _Albedo_Map);
                o.fogFactor = ComputeFogFactor(pos.positionCS.z);
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                half4 albedo = SAMPLE_TEXTURE2D(_Albedo_Map, sampler_Albedo_Map, input.uv) * _BaseColor;
                DitherClip(input.positionCS.xy, albedo.a);

                half3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_Normal_Map, sampler_Normal_Map, input.uv), _Normal_Amount);
                half3 bitangent = input.tangentWS.w * cross(input.normalWS, input.tangentWS.xyz);
                half3 normalWS = NormalizeNormalPerPixel(TransformTangentToWorld(normalTS,
                    half3x3(input.tangentWS.xyz, bitangent, input.normalWS)));

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.positionCS = input.positionCS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                inputData.fogCoord = input.fogFactor;
                inputData.bakedGI = SampleSH(normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = albedo.rgb;
                surface.metallic = _Metallic;
                surface.smoothness = _Smoothness;
                surface.normalTS = normalTS;
                surface.occlusion = 1;
                surface.emission = SAMPLE_TEXTURE2D(_Emission_Map, sampler_Emission_Map, input.uv).rgb * _Emission_Color.rgb;
                surface.alpha = 1;

                half4 color = UniversalFragmentPBR(inputData, surface);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                color.a = 1;
                return color;
            }
            ENDHLSL
        }

        // Solid shadows: a faded wall keeps its full shadow on the ground.
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings Vert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.uv = TRANSFORM_TEX(input.uv, _Albedo_Map);
                return o;
            }

            half Frag(Varyings input) : SV_Target
            {
                DitherClip(input.positionCS.xy, SAMPLE_TEXTURE2D(_Albedo_Map, sampler_Albedo_Map, input.uv).a * _BaseColor.a);
                return input.positionCS.z;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half3 normalWS : TEXCOORD1; };

            Varyings Vert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.uv = TRANSFORM_TEX(input.uv, _Albedo_Map);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                DitherClip(input.positionCS.xy, SAMPLE_TEXTURE2D(_Albedo_Map, sampler_Albedo_Map, input.uv).a * _BaseColor.a);
                return half4(NormalizeNormalPerPixel(input.normalWS), 0.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
