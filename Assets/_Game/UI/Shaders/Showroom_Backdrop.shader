// Backdrop behind the live character in the creator and on character select: a comic "burst". The centre colour
// is printed over the edge colour with Ben-Day dots that shrink away from the centre, on the same screen-aligned
// dot grid as the rest of the game (ComicHalftone.hlsl). Unlit, so the showroom lights only shape the character.
Shader "KryticalNova/UI/Showroom Backdrop"
{
    Properties
    {
        _BaseColor ("Edge Colour", Color) = (0.078, 0.071, 0.169, 1)
        _GlowColor ("Centre Colour", Color) = (0.30, 0.32, 0.75, 1)
        _Centre ("Centre Offset From Pivot (metres)", Vector) = (0, -1, 0, 0)
        _Radius ("Radius (metres)", Range(0.5, 20)) = 5
        _Falloff ("Falloff", Range(0.25, 4)) = 1.4
        _DotScale ("Dot Size Multiplier", Range(0.25, 6)) = 2
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        Pass
        {
            Name "Unlit"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/_Game/Rendering/Shaders/Comic/ComicHalftone.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _GlowColor;
                float4 _Centre;
                float _Radius;
                float _Falloff;
                float _DotScale;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 metres : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                // Object space scaled to metres, so the burst stays round on a stretched quad.
                float3 axisX = float3(UNITY_MATRIX_M[0].x, UNITY_MATRIX_M[1].x, UNITY_MATRIX_M[2].x);
                float3 axisY = float3(UNITY_MATRIX_M[0].y, UNITY_MATRIX_M[1].y, UNITY_MATRIX_M[2].y);
                output.metres = input.positionOS.xy * float2(length(axisX), length(axisY));
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float distance01 = length(input.metres - _Centre.xy) / max(_Radius, 0.01);
                half coverage = pow(saturate(1.0 - distance01), _Falloff);
                half dots = coverage >= 0.999 ? 1.0 : ComicHalftone(input.positionCS.xy / max(_DotScale, 0.01), coverage);
                return half4(lerp(_BaseColor.rgb, _GlowColor.rgb, dots), 1.0);
            }
            ENDHLSL
        }
    }
}
