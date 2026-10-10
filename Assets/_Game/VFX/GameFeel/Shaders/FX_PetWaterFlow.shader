// Faux water for a pet that swims through the air: ribbons that come to a point at both ends, with an edge that
// wavers, bands of lighter and deeper water that run down them and a rim of foam. It is drawn on the trails of a
// particle system (u runs along the ribbon from its head to its tail, v across it), so the particles decide where
// the water goes and this decides what it looks like. Solid, flat and cut out, like the rest of the pets' effects:
// it writes depth, so the comic outline inks it.
Shader "KryticalNova/FX/Pet Water Flow"
{
    Properties
    {
        _WaterColor ("Water", Color) = (0.25, 0.72, 0.84, 1)
        _DeepColor ("Deep water", Color) = (0.16, 0.5, 0.72, 1)
        _FoamColor ("Foam", Color) = (0.97, 0.99, 1, 1)
        _FlowSpeed ("Flow speed (bands a second)", Float) = 1.6
        _Bands ("Bands along a ribbon", Float) = 2
        _Waver ("Edge waver", Range(0, 0.5)) = 0.18
        _Foam ("Foam rim", Range(0, 1)) = 0.26
    }

    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _WaterColor;
            half4 _DeepColor;
            half4 _FoamColor;
            half _FlowSpeed;
            half _Bands;
            half _Waver;
            half _Foam;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            float2 uv : TEXCOORD0;
            half4 color : COLOR;
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float2 uv : TEXCOORD0;
            half4 color : COLOR;
        };

        Varyings Vert(Attributes input)
        {
            Varyings output;
            output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
            output.uv = input.uv;
            output.color = input.color;
            return output;
        }

        // How deep inside the ribbon this point is: 1 on its middle line where it is widest, 0 on its edge, below
        // 0 outside it. The ribbon swells from a point at its head and thins to a point at its tail, and its edge
        // wavers as the water runs.
        half Inside(float2 uv)
        {
            half across = abs(uv.y * 2.0 - 1.0);
            half taper = smoothstep(0.0, 0.22, uv.x) * (1.0 - smoothstep(0.5, 1.0, uv.x));
            half waver = 1.0 + _Waver * sin(uv.x * 17.0 - _Time.y * _FlowSpeed * 5.0);
            return taper * waver - across;
        }
        ENDHLSL

        Pass
        {
            Name "Water"
            ZWrite On

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            half4 Frag(Varyings input) : SV_Target
            {
                half inside = Inside(input.uv);
                clip(inside - 0.04);
                // Bands of lighter water run from the head of the ribbon to its tail.
                half band = frac(input.uv.x * _Bands - _Time.y * _FlowSpeed);
                half3 colour = lerp(_DeepColor.rgb, _WaterColor.rgb, step(0.45, band));
                colour = lerp(_FoamColor.rgb, colour, step(_Foam, inside));
                return half4(colour * input.color.rgb, 1.0);
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
            #pragma vertex Vert
            #pragma fragment FragDepth

            half4 FragDepth(Varyings input) : SV_Target
            {
                clip(Inside(input.uv) - 0.04);
                return input.positionCS.z;
            }
            ENDHLSL
        }
    }
}
