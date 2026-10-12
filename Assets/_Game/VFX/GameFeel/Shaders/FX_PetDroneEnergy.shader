// The Drone Core's energy: what its guns, its beam and its swarm are drawn with. One shader with four patterns, each
// on a simple mesh carried by a particle (so the particle system decides when it is there, how big and how bright,
// and this decides what it looks like):
//   Streaks  a tube or a ball: bands that whirl round it or run down it (the blur round a spinning barrel, a beam)
//   Radial   a flat disc: rays from its middle (a muzzle flash, a camera flash)
//   Ring     a flat disc: one ring (the shock of a shot)
//   Holo     any mesh: scan lines that run through it in the world (the cube coming apart, a scanning cone)
// The moving parts are the All In 1 Shader Nodes library's own functions (Assets/Plugins/AllIn1ShaderNodes): Twist UV
// and Scroll Texture move the bands, Radial Pattern Lines draws the rays, Hologram the scan lines, Glow the heat.
// Drawn over the scene without writing depth; bright colours are for the bloom.
Shader "KryticalNova/FX/Pet Drone Energy"
{
    Properties
    {
        [KeywordEnum(Streaks, Radial, Ring, Holo)] _Pattern ("Pattern", Float) = 0
        [HDR] _ColorA ("Colour (thin parts)", Color) = (0.2, 0.7, 1, 1)
        [HDR] _ColorB ("Colour (thick parts)", Color) = (1, 1, 1, 1)
        _Glow ("Glow (All In 1)", Range(0, 8)) = 1.5
        [HDR] _GlowColor ("Glow colour", Color) = (0.3, 0.8, 1, 1)
        _Core ("Solid share", Range(0, 1)) = 0
        _EdgeSoft ("Fades where it turns away", Range(0, 1)) = 0
        _EdgePower ("... how fast", Range(0.2, 6)) = 1.5
        _EndFade ("Fades at both ends (v)", Range(0, 0.5)) = 0.15

        [Header(Streaks)]
        _Bands ("Bands round it", Float) = 9
        _Sharp ("Band sharpness", Range(0.5, 8)) = 2.5
        _Break ("Bands break up along it", Range(0, 1)) = 0.5
        _AlongFreq ("... how many times", Float) = 2
        _ScrollX ("Scroll round (turns a second)", Float) = 3
        _ScrollY ("Scroll along (lengths a second)", Float) = 0
        _Twist ("Twist (All In 1)", Range(-1.5, 1.5)) = 0

        [Header(Radial)]
        _Segments ("Rays", Float) = 12
        _Spin ("Spin (turns a second)", Float) = 0.5
        _Spacing ("Ray spacing", Range(0, 1)) = 0.35
        _Blur ("Ray blur", Range(0.001, 1)) = 0.35
        _Hot ("Hot middle", Range(0, 1)) = 0.3

        [Header(Ring)]
        _RingRadius ("Ring radius", Range(0, 1)) = 0.8
        _RingWidth ("Ring width", Range(0.01, 0.5)) = 0.12

        [Header(Holo)]
        _HoloDirection ("Line direction", Vector) = (0, 1, 0, 0)
        _HoloFrequency ("Lines a metre", Float) = 22
        _HoloSpeed ("Line speed", Float) = 1.2
        _HoloAccentFrequency ("Accent lines a metre", Float) = 5
        _HoloAccentSpeed ("Accent speed", Float) = -2.5
        _HoloBase ("Between the lines", Range(0, 1)) = 0.12

        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Blend onto the scene (One = adds light)", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Cull Off
        ZWrite Off
        Blend SrcAlpha [_DstBlend]

        Pass
        {
            Name "Energy"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma shader_feature_local _PATTERN_STREAKS _PATTERN_RADIAL _PATTERN_RING _PATTERN_HOLO

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"
            #include "Assets/Plugins/AllIn1ShaderNodes/Shaders/AllIn1Node_SG_Helper.hlsl"
            #include "Assets/Plugins/AllIn1ShaderNodes/Shaders/AllIn1NodeLibrary_ProceduralShapes.hlsl"
            #include "Assets/Plugins/AllIn1ShaderNodes/Shaders/AllIn1NodeLibrary_UVEffects.hlsl"
            #include "Assets/Plugins/AllIn1ShaderNodes/Shaders/AllIn1NodeLibrary_ColorEffects.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ColorA;
                half4 _ColorB;
                half4 _GlowColor;
                half4 _HoloDirection;
                half _Glow;
                half _Core;
                half _EdgeSoft;
                half _EdgePower;
                half _EndFade;
                half _Bands;
                half _Sharp;
                half _Break;
                half _AlongFreq;
                half _ScrollX;
                half _ScrollY;
                half _Twist;
                half _Segments;
                half _Spin;
                half _Spacing;
                half _Blur;
                half _Hot;
                half _RingRadius;
                half _RingWidth;
                half _HoloFrequency;
                half _HoloSpeed;
                half _HoloAccentFrequency;
                half _HoloAccentSpeed;
                half _HoloBase;
                half _DstBlend;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            half Hash(half n) { return frac(sin(n * 127.1) * 43758.5453); }

            // Bands round a tube (u), each of its own brightness, that break up along it (v). The same band is met
            // again after a whole turn, so the tube has no seam.
            half Bands(float2 uv)
            {
                half count = max(1.0, floor(_Bands));
                half x = uv.x * count;
                half i = floor(x);
                half f = smoothstep(0.0, 1.0, frac(x));
                half a = Hash(fmod(i, count) + 1.0);
                half b = Hash(fmod(i + 1.0, count) + 1.0);
                half band = lerp(a, b, f);
                half along = 0.5 + 0.5 * sin((uv.y * _AlongFreq + a) * 6.2831853);
                return pow(saturate(band * lerp(1.0, along, _Break)), _Sharp);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float time = _Time.y;
                float2 uv = input.uv;
                half mask = 0.0;

            #if defined(_PATTERN_RADIAL)
                half r = length(uv - 0.5) * 2.0;
                half rays = RadialPatternLines(uv, _Segments, frac(time * _Spin), _Spacing, _Blur);
                half reach = 1.0 - smoothstep(0.15, 1.0, r);
                half hot = 1.0 - smoothstep(0.0, max(0.01, _Hot), r);
                mask = saturate(rays * reach + hot);
            #elif defined(_PATTERN_RING)
                half r = length(uv - 0.5) * 2.0;
                mask = 1.0 - smoothstep(0.0, _RingWidth, abs(r - _RingRadius));
                mask *= 1.0 - smoothstep(0.9, 1.0, r);
            #elif defined(_PATTERN_HOLO)
                half4 lines = Hologram(half4(1, 1, 1, 1), input.positionWS, time, _HoloDirection, _HoloFrequency, _HoloSpeed, 0.5,
                    2.0, 3.0, _HoloBase, _HoloAccentFrequency, _HoloAccentSpeed, 1.0, 0.8, half4(1, 1, 1, 1));
                mask = lines.a;
            #else
                float2 moved = TwistUV(uv, _Twist, 0.5, 0.5, 0.75, float4(1, 1, 0, 0));
                moved = ScrollTexture(moved, time, _ScrollX, _ScrollY);
                mask = max(Bands(moved), _Core);
            #endif

            #if !defined(_PATTERN_RADIAL) && !defined(_PATTERN_RING)
                // A round thing fades where its skin turns away from the eye, and a length of it fades at both ends.
                half facing = saturate(abs(dot(normalize(input.normalWS), normalize(GetWorldSpaceViewDir(input.positionWS)))));
                mask *= lerp(1.0, pow(facing, _EdgePower), _EdgeSoft);
                mask *= smoothstep(0.0, max(0.001, _EndFade), uv.y) * (1.0 - smoothstep(1.0 - max(0.001, _EndFade), 1.0, uv.y));
            #endif

                half4 colour = lerp(_ColorA, _ColorB, mask);
                colour.a = mask * _ColorA.a;
                colour = Glow(colour, _Glow, _GlowColor.rgb, 1.0);
                colour *= input.color;
                return colour;
            }
            ENDHLSL
        }
    }
}
