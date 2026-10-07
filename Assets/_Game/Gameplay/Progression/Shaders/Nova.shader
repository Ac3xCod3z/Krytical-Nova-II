// The Nova: the disc of light a character earns through Ascension (NovaHalo). Drawn whole on one quad, so every
// part stays crisp at any size and turns without a script. NovaSettings decides what each of the twelve tiers
// shows: tier 1 is a thin ring; later tiers fill the disc, add inner rings, spokes, a turning band of glyphs and
// rays, until the last is a full rayed Nova in every colour. Additive and HDR: above 1 it blooms as well.
Shader "KryticalNova/Nova"
{
    Properties
    {
        [HDR] _Inner ("Colour at the centre", Color) = (0.8, 0.86, 0.95, 1)
        [HDR] _Outer ("Colour at the ring and beyond", Color) = (0.62, 0.7, 0.85, 1)
        _Ring ("Ring radius (1 = edge of the quad)", Range(0.2, 1)) = 0.75
        _RingWidth ("Ring width (ring radii)", Range(0.005, 0.2)) = 0.04
        _Fill ("Light inside the ring", Range(0, 1)) = 0
        _InnerRings ("Inner rings", Range(0, 3)) = 0
        _Spokes ("Spokes", Range(0, 24)) = 0
        _Glyphs ("Glyphs in the band", Range(0, 48)) = 0
        _Rays ("Rays", Range(0, 32)) = 0
        _RayLength ("Ray length (ring radii)", Range(0, 1)) = 0
        _Prism ("Spectrum round the ring", Range(0, 1)) = 0
        _Brightness ("Brightness", Float) = 1.5
        _Spin ("Turns per second", Float) = 0.066
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+20" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Nova"
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
                half4 _Inner;
                half4 _Outer;
                half _Ring;
                half _RingWidth;
                half _Fill;
                half _InnerRings;
                half _Spokes;
                half _Glyphs;
                half _Rays;
                half _RayLength;
                half _Prism;
                half _Brightness;
                half _Spin;
            CBUFFER_END

            #define TAU 6.2831853

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
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
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            float Hash(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            // A circle of light at radius 'at', 'width' across, with a soft edge one pixel wide.
            float Band(float r, float at, float width, float soft)
            {
                return 1.0 - smoothstep(width * 0.5, width * 0.5 + soft, abs(r - at));
            }

            half3 Spectrum(float hue)
            {
                half3 k = half3(1.0, 2.0 / 3.0, 1.0 / 3.0);
                half3 p = abs(frac(hue + k) * 6.0 - 3.0);
                return lerp(half3(1, 1, 1), saturate(p - 1.0), 0.8);
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 p = input.uv * 2.0 - 1.0;
                float edge = length(p);
                float r = edge / max(_Ring, 0.01);          // 1 at the ring
                float angle = atan2(p.y, p.x);
                float turn = _Time.y * _Spin * TAU;
                float soft = fwidth(r) * 1.5;
                float width = _RingWidth;

                // The ring itself, and the light it sheds to either side.
                float light = Band(r, 1.0, width, soft);
                light += 0.3 * exp(-abs(r - 1.0) * 10.0);

                // Light filling the disc, strongest at the centre.
                light += _Fill * (1.0 - smoothstep(0.0, 1.0, r));

                // Inner rings, each thinner and fainter than the one outside it.
                float ringAt[3] = { 0.72, 0.5, 0.3 };
                for (int i = 0; i < 3; i++)
                    light += step(i + 0.5, _InnerRings) * Band(r, ringAt[i], width * 0.6, soft) * (0.75 - 0.12 * i);

                // Spokes from the centre to the ring, turning slowly.
                if (_Spokes > 0.5)
                {
                    float cell = TAU / _Spokes;
                    float fromSpoke = abs(frac((angle + turn) / cell + 0.5) - 0.5) * cell * r; // distance across, ring radii
                    float spoke = 1.0 - smoothstep(width * 0.2, width * 0.2 + soft, fromSpoke);
                    light += spoke * step(0.1, r) * step(r, 1.0) * 0.6;
                }

                // A band of glyphs just inside the ring, turning the other way: each a 3 x 3 pattern of its own.
                if (_Glyphs > 0.5)
                {
                    float along = frac((angle - turn * 0.6) / TAU) * _Glyphs;
                    float glyph = floor(along);
                    float2 inGlyph = float2((frac(along) - 0.18) / 0.64, (r - 0.79) / 0.15);
                    if (inGlyph.x > 0.0 && inGlyph.x < 1.0 && inGlyph.y > 0.0 && inGlyph.y < 1.0)
                    {
                        float2 mark = floor(inGlyph * 3.0);
                        float2 inMark = abs(frac(inGlyph * 3.0) - 0.5);
                        float on = step(0.42, Hash(mark + glyph * 7.13 + 1.0));
                        light += on * step(max(inMark.x, inMark.y), 0.4) * 0.85;
                    }
                }

                // Rays past the ring: every second one shorter, each tapering to a point.
                if (_Rays > 0.5 && _RayLength > 0.001)
                {
                    float along = frac((angle + turn * 0.25) / TAU) * _Rays;
                    float number = floor(along);
                    float across = abs(frac(along) - 0.5) * 2.0;   // 0 down the middle of a ray, 1 between two
                    float reach = _RayLength * (fmod(number, 2.0) < 0.5 ? 1.0 : 0.6);
                    float out01 = saturate((r - 1.0) / reach);
                    float halfWidth = 0.42 * (1.0 - out01);
                    float ray = (1.0 - smoothstep(halfWidth, halfWidth + 0.08, across)) * step(1.0, r) * step(r, 1.0 + reach);
                    light += ray * (1.0 - out01 * 0.65) * 0.8;
                }

                half3 colour = lerp(_Inner.rgb, _Outer.rgb, saturate(r));
                half3 spectrum = Spectrum(frac(angle / TAU + _Time.y * 0.05));
                colour = lerp(colour, spectrum * max(max(colour.r, colour.g), colour.b), _Prism * smoothstep(0.45, 1.0, r));

                float breath = 1.0 + 0.08 * sin(_Time.y * 1.7);
                float fade = 1.0 - smoothstep(0.86, 1.0, edge);     // nothing reaches the quad's own edge
                return half4(colour * (light * _Brightness * breath * fade), 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
