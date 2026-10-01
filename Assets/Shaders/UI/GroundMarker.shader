// Flat, unlit, alpha-blended decal quad for ground rings (move destination, selected target).
// Uses the white Synty HUD ring sprites as a mask; _Color is HDR so the ring can feed bloom.
// A dilated copy of the mask is drawn underneath in _OutlineColor so rings stay readable on bright ground.
// Depth-tested but never writes depth, and pulled toward the camera so it doesn't z-fight the ground.
Shader "KryticalNova/GroundMarker"
{
    Properties
    {
        [MainTexture] _MainTex ("Ring Mask", 2D) = "white" {}
        [HDR] _Color ("Tint", Color) = (0.45, 0.87, 0.77, 1)
        _Opacity ("Opacity", Range(0, 1)) = 1
        _OutlineColor ("Outline", Color) = (0, 0, 0, 0.65)
        _OutlineWidth ("Outline Width (UV)", Range(0, 0.05)) = 0.014
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+10" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        ZWrite Off
        ZTest LEqual
        Cull Off
        Offset -2, -2
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "GroundMarker"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color;
                half _Opacity;
                half4 _OutlineColor;
                float _OutlineWidth;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 mask = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);

                // 8-tap dilation of the mask for the outline.
                const float2 taps[8] = { float2(1, 0), float2(-1, 0), float2(0, 1), float2(0, -1),
                                         float2(0.7071, 0.7071), float2(-0.7071, 0.7071), float2(0.7071, -0.7071), float2(-0.7071, -0.7071) };
                half dilated = mask.a;
                [unroll] for (int i = 0; i < 8; i++)
                    dilated = max(dilated, SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv + taps[i] * _OutlineWidth).a);

                half fill = mask.a * _Color.a;
                half outline = dilated * _OutlineColor.a;
                half alpha = max(fill, outline) * _Opacity;
                half3 color = lerp(_OutlineColor.rgb, mask.rgb * _Color.rgb, saturate(fill));
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}
