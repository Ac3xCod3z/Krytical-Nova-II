// A player de-rezzing: killed off the ground (in the air, on a wall, in a tree, on a roof), the body is deleted where it
// is instead of falling (owner, 17 Oct 2026). Unlike the Hostiles' burn (a noise eating the body), it glitches, then a
// bright band in the faction's colour sweeps it from the head down; above the band the body breaks into voxels that
// drift up and out and vanish cell by cell, scanned with hologram lines. Built on the All In 1 Shader Nodes library
// (Glitch, VertexVoxel, Hologram). Derez (GameFeel) swaps it in for the body's and the pet's materials and drives it.
Shader "KryticalNova/Player/Derez"
{
    Properties
    {
        _BaseMap ("Base Map", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        _EmissionMap ("Emission Map", 2D) = "black" {}
        [HDR] _EmissionColor ("Emission Color", Color) = (0, 0, 0, 1)
        _Progress ("Progress", Range(0, 1)) = 0
        _Top ("Top of the body (world Y)", Float) = 2
        _Bottom ("Feet (world Y)", Float) = 0
        _Center ("Centre of the body (world)", Vector) = (0, 1, 0, 0)
        [HDR] _Tint ("Faction tint", Color) = (0.4, 2.2, 3, 1)
        _VoxelSize ("Voxels per metre", Float) = 7
        _GlitchAmount ("Glitch", Range(0, 0.3)) = 0
        _Rise ("Rise per metre above the band", Float) = 0.9
        _Spread ("Spread per metre above the band", Float) = 0.35
        _Band ("Band width (m)", Float) = 0.16
        _Vanish ("Metres above the band to vanish over", Float) = 1.1
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
            half4 _Tint;
            float4 _Center;
            float _Progress;
            float _Top;
            float _Bottom;
            float _VoxelSize;
            float _GlitchAmount;
            float _Rise;
            float _Spread;
            float _Band;
            float _Vanish;
        CBUFFER_END

        sampler2D _BaseMap;
        sampler2D _EmissionMap;

        // The library's URP helper speaks of lights: URP's lighting comes first.
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        // The library is written for Shader Graph and Amplify; these are the macros its functions need here.
        #define TEX_PARAM sampler2D
        #define SAMPLE_TEX2D(texName, uv) tex2D(texName, uv)
        #define SAMPLE_TEX2D_LOD(texName, uv) tex2Dlod(texName, uv)
        #include "Assets/Plugins/AllIn1ShaderNodes/Shaders/AllIn1Node_CommonFunctions.hlsl"
        #include "Assets/Plugins/AllIn1ShaderNodes/Shaders/AllIn1Node_URP_Helper.hlsl"
        #include "Assets/Plugins/AllIn1ShaderNodes/Shaders/AllIn1NodeLibrary_VertexEffects.hlsl"
        #include "Assets/Plugins/AllIn1ShaderNodes/Shaders/AllIn1NodeLibrary_ColorEffects.hlsl"

        // Where the band is now: from just over the head down to just under the feet.
        float SweepY() { return lerp(_Top + 0.08, _Bottom - 0.3, saturate(_Progress)); }

        // One random number per voxel of the body, fixed in the world while it de-rezzes.
        float CellRandom(float3 positionWS)
        {
            float3 cell = floor(positionWS * _VoxelSize);
            return frac(sin(dot(cell, float3(12.9898, 78.233, 37.719))) * 43758.5453);
        }

        // The body as it de-rezzes: glitched sideways; above the band, snapped to voxels and pulled up and apart,
        // all of it together (no part moves on its own, so nothing tears into long shards).
        float3 Derez(float3 positionOS, out float3 restingWS)
        {
            float3 glitched = Glitch(positionOS, float4(1, 0, 0.35, 0), 1, 11, 7, _GlitchAmount, _Time.y);
            restingWS = TransformObjectToWorld(positionOS);
            float3 ws = TransformObjectToWorld(glitched);
            float above = restingWS.y - SweepY();
            if (above > 0)
            {
                float3 local = VertexVoxel(ws - _Center.xyz, _VoxelSize, saturate(above / 0.3));
                float3 outward = float3(local.x, 0, local.z);
                local += float3(0, above * _Rise, 0) + outward * above * _Spread;
                ws = _Center.xyz + local;
            }
            return ws;
        }

        // Clip what has gone: cells vanish one by one the higher they have risen past the band.
        void ClipGone(float3 restingWS)
        {
            float above = restingWS.y - SweepY();
            if (above <= 0) return;
            clip(CellRandom(restingWS) - saturate(above / _Vanish));
        }

        struct DepthAttributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
        struct DepthVaryings { float4 positionCS : SV_POSITION; float3 restingWS : TEXCOORD0; float3 normalWS : TEXCOORD1; };

        DepthVaryings DepthVert(DepthAttributes input)
        {
            DepthVaryings output;
            float3 ws = Derez(input.positionOS.xyz, output.restingWS);
            output.positionCS = TransformWorldToHClip(ws);
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
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 restingWS : TEXCOORD2;
                float3 normalWS : TEXCOORD3;
                float fogFactor : TEXCOORD4;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = Derez(input.positionOS.xyz, output.restingWS);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                ClipGone(input.restingWS);

                half3 albedo = tex2D(_BaseMap, input.uv).rgb * _BaseColor.rgb;
                half3 normal = normalize(input.normalWS);
                Light light = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half diffuse = saturate(dot(normal, light.direction)) * light.shadowAttenuation;
                half3 colour = albedo * (light.color * diffuse + SampleSH(normal));
                colour += tex2D(_EmissionMap, input.uv).rgb * _EmissionColor.rgb;

                float above = input.restingWS.y - SweepY();
                // The band: a hot line in the faction's colour, white at its core.
                float band = saturate(1 - abs(above) / max(_Band, 0.01));
                // Above it: what is left is a hologram of the body in the faction's colour, scanned with lines.
                if (above > 0)
                {
                    // What is left is the body as a hologram in the faction's colour, scanned with lines.
                    float4 holo = Hologram(float4(1, 1, 1, 1), input.restingWS, _Time.y, float4(0, 1, 0, 0),
                        9, 1.6, 0.5, 2.2, 2, 0.25, 2.5, 0.7, 1, 0.6, float4(1, 1, 1, 1));
                    colour = _Tint.rgb * (0.3 + 0.7 * holo.a) + colour * 0.12;
                }
                // A flicker of the colour over the whole body while it glitches.
                colour += _Tint.rgb * _GlitchAmount * 2.5;
                // The band: the colour, hot, with a thin white core.
                colour = lerp(colour, _Tint.rgb * 2.2 + band * band * band * 1.5, band);

                colour = MixFog(colour, input.fogFactor);
                return half4(colour, 1);
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
                float3 ws = Derez(input.positionOS.xyz, output.restingWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(ws, output.normalWS, _LightDirection));
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
                ClipGone(input.restingWS);
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
                ClipGone(input.restingWS);
                return input.positionCS.z;
            }
            ENDHLSL
        }

        // The comic ink pass reads depth and normals: the outline de-rezzes with the body.
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
                ClipGone(input.restingWS);
                return half4(normalize(input.normalWS), 0);
            }
            ENDHLSL
        }
    }
}
