// A ring of a Nova (NovaHalo): a band of light on a real ring mesh, so it turns in three dimensions and passes
// behind and in front of the star. The band is soft at its edges and brightest down its middle; along it runs a
// pattern (plain, dashes, or a row of glyphs) and, for the brighter tiers, a point of light chasing its tail.
// u runs once round the ring, v across the band; uv1 is each vertex's offset from the middle line of the band,
// which lets the band be kept a couple of pixels wide however far away it is, so the rings still read at play
// distance. Every ring of every Nova uses this one material: what differs is handed over per ring by NovaHalo.
Shader "KryticalNova/NovaRing"
{
    Properties
    {
        [HDR] _Color ("Colour", Color) = (0.6, 0.8, 1, 1)
        _Brightness ("Brightness", Float) = 1.5
        _Pattern ("Pattern (0 plain, 1 dashes, 2 glyphs)", Float) = 0
        _Marks ("Dashes or glyphs round the ring", Float) = 24
        _Comet ("Running light (turns a second, 0 = none)", Float) = 0
        _Prism ("Spectrum round the ring", Range(0, 1)) = 0
        _Flash ("Flash (added brightness, set by code)", Float) = 0
        _Fade ("How much of it shows (set by code)", Range(0, 1)) = 1
        _MinPixels ("Thinnest a band is ever drawn (pixels)", Range(0, 6)) = 2.2
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+17" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Ring"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Brightness;
                half _Pattern;
                half _Marks;
                half _Comet;
                half _Prism;
                half _Flash;
                half _Fade;
                half _MinPixels;
            CBUFFER_END

            // In a fight the Nova swells on the beat of the fight music (BeatGlow sets it for every material at once; 0 out of one).
            half _KN_BeatPulse;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float3 across : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                // Widen the band about its middle line until it is _MinPixels across on this screen.
                float3 middleWS = TransformObjectToWorld(input.positionOS.xyz - input.across);
                float3 acrossWS = TransformObjectToWorld(input.positionOS.xyz) - middleWS;
                float depth = max(0.01, -TransformWorldToView(middleWS).z);
                float pixel = depth * 2.0 / (abs(UNITY_MATRIX_P[1][1]) * _ScreenParams.y);   // metres a pixel covers there
                float grow = max(1.0, _MinPixels * pixel / max(2.0 * length(acrossWS), 0.00001));
                output.positionCS = TransformWorldToHClip(middleWS + acrossWS * grow);
                output.uv = input.uv;
                return output;
            }

            float Hash(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            half3 Spectrum(float hue)
            {
                half3 k = half3(1.0, 2.0 / 3.0, 1.0 / 3.0);
                half3 p = abs(frac(hue + k) * 6.0 - 3.0);
                return lerp(half3(1, 1, 1), saturate(p - 1.0), 0.8);
            }

            half4 frag(Varyings input) : SV_Target
            {
                float along = input.uv.x;
                float across = input.uv.y;

                // Soft at both edges, with a bright line down the middle.
                float soft = smoothstep(0.0, 0.35, across) * smoothstep(1.0, 0.65, across);
                float spine = pow(saturate(sin(across * 3.14159265)), 8.0);
                float band = soft * 0.55 + spine * 0.9;

                float pattern = 1.0;
                if (_Pattern > 1.5)
                {
                    // Glyphs: each cell round the ring is a 3 x 3 pattern of its own, on a faint band.
                    float cell = floor(along * _Marks);
                    float2 inGlyph = float2((frac(along * _Marks) - 0.16) / 0.68, (across - 0.14) / 0.72);
                    float marked = 0.0;
                    if (inGlyph.x > 0.0 && inGlyph.x < 1.0 && inGlyph.y > 0.0 && inGlyph.y < 1.0)
                    {
                        float2 mark = floor(inGlyph * 3.0);
                        float2 inMark = abs(frac(inGlyph * 3.0) - 0.5);
                        marked = step(0.42, Hash(mark + cell * 7.13 + 1.0)) * step(max(inMark.x, inMark.y), 0.4);
                    }
                    // The glyphs are the light; the band only frames them with a thin line at each edge.
                    float rails = pow(saturate(1.0 - abs(across - 0.06) * 18.0), 2.0) + pow(saturate(1.0 - abs(across - 0.94) * 18.0), 2.0);
                    band = marked * 1.1 + rails * 0.8 + soft * 0.06;
                }
                else if (_Pattern > 0.5)
                {
                    float d = frac(along * _Marks);
                    pattern = smoothstep(0.0, 0.07, d) * smoothstep(0.62, 0.55, d);
                }

                // A point of light running round the ring, with a tail behind it.
                float comet = 0.0;
                if (abs(_Comet) > 0.001)
                {
                    float behind = frac((_Time.y * _Comet - along) * sign(_Comet));
                    comet = exp(-behind * 10.0) * 2.4 * soft;
                }

                half3 colour = _Color.rgb;
                colour = lerp(colour, Spectrum(frac(along + _Time.y * 0.05)) * max(max(colour.r, colour.g), colour.b), _Prism);
                float light = (band * pattern + comet) * (_Brightness + _Flash) * (1.0 + _KN_BeatPulse) * _Fade;
                return half4(colour * light, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
