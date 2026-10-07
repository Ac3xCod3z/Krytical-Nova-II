// The air round a Nova's star (NovaHalo), on one quad that always faces the camera. Two passes:
//  - a patch of night behind the Nova: a soft dark disc with a few faint stars. The rings and the glow are added
//    light, and added light vanishes against a bright sky; over this they read at noon as they do at midnight;
//  - the corona: the star's glow falling away, and for the brighter tiers its rays, turning slowly.
// The star itself (NovaCore) is solid and writes depth, so the middle of this quad is hidden behind it.
Shader "KryticalNova/Nova"
{
    Properties
    {
        [HDR] _Inner ("Glow at the star", Color) = (1, 1, 1, 1)
        [HDR] _Outer ("Glow further out and the rays", Color) = (0.6, 0.8, 1, 1)
        _Core ("Star radius (1 = edge of the quad)", Range(0.01, 0.6)) = 0.08
        _Reach ("Nova radius (1 = edge of the quad)", Range(0.05, 1)) = 0.4
        _Corona ("Glow strength", Range(0, 2)) = 0.5
        _Rays ("Rays", Range(0, 16)) = 0
        _RayLength ("Ray length (Nova radii)", Range(0, 2)) = 1
        _Prism ("Spectrum round the rays", Range(0, 1)) = 0
        _Brightness ("Brightness", Float) = 1.5
        _Backdrop ("Darkness of the patch behind", Range(0, 1)) = 0.4
        _Flash ("Flash (added brightness, set by code)", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+15" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _Inner;
            half4 _Outer;
            half _Core;
            half _Reach;
            half _Corona;
            half _Rays;
            half _RayLength;
            half _Prism;
            half _Brightness;
            half _Backdrop;
            half _Flash;
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
        ENDHLSL

        // The patch of night. Drawn first, blended over the scene.
        Pass
        {
            Name "Backdrop"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            half4 frag(Varyings input) : SV_Target
            {
                clip(_Backdrop - 0.001);
                float2 p = input.uv * 2.0 - 1.0;
                float r = length(p) / max(_Reach, 0.01);            // 1 at the Nova's own radius
                float dark = _Backdrop * (1.0 - smoothstep(0.35, 1.45, r));

                // A few faint stars, each on its own slow twinkle.
                float2 cell = floor(p * 26.0);
                float2 inCell = frac(p * 26.0) - 0.5;
                float seed = Hash(cell);
                float star = step(0.93, seed) * (1.0 - smoothstep(0.0, 0.22, length(inCell)));
                star *= 0.5 + 0.5 * sin(_Time.y * (1.0 + seed * 3.0) + seed * 40.0);
                star *= (1.0 - smoothstep(0.6, 1.5, r)) * _Backdrop * 1.6;

                half3 night = half3(0.01, 0.015, 0.04);
                half3 colour = lerp(night, _Outer.rgb * 0.6 + 0.4, saturate(star));
                return half4(colour, saturate(dark + star));
            }
            ENDHLSL
        }

        // The corona and its rays. Added light.
        Pass
        {
            Name "Corona"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

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
                float r = edge / max(_Reach, 0.01);                  // 1 at the Nova's own radius
                float core = _Core / max(_Reach, 0.01);              // the star's radius on the same scale
                float angle = atan2(p.y, p.x);

                // The glow: tight and hot at the star's limb, then a long soft fall through the rings.
                float fromStar = max(0.0, r - core);
                float tight = exp(-fromStar * 7.0) * 1.2;
                float wide = exp(-fromStar * 2.2) * 0.28;

                // Rays: thin spikes from the star, every second one shorter, breathing a little, turning slowly.
                float rays = 0.0;
                if (_Rays > 0.5)
                {
                    float along = frac((angle + _Time.y * 0.08) / TAU) * _Rays;
                    float number = floor(along);
                    float across = abs(frac(along) - 0.5) * 2.0;        // 0 down the middle of a ray
                    float reach = _RayLength * (fmod(number, 2.0) < 0.5 ? 1.0 : 0.62);
                    reach *= 0.9 + 0.1 * sin(_Time.y * 1.3 + number * 1.7);
                    float out01 = saturate(fromStar / max(reach, 0.01));
                    float width = 0.16 * (1.0 - out01) * (1.0 - out01);
                    rays = (1.0 - smoothstep(width, width + 0.05, across)) * (1.0 - out01) * step(core, r) * 0.9;
                }

                half3 colour = lerp(_Inner.rgb, _Outer.rgb, saturate(fromStar * 1.6));
                half3 spectrum = Spectrum(frac(angle / TAU + _Time.y * 0.05));
                colour = lerp(colour, spectrum * max(max(colour.r, colour.g), colour.b), _Prism * smoothstep(0.1, 0.8, fromStar));

                float breath = 1.0 + 0.07 * sin(_Time.y * 1.7);
                float fade = 1.0 - smoothstep(0.45, 1.0, edge);       // nothing reaches the quad's own edge
                // A flash burns at the star and along the rays; it does not light the whole patch of air.
                float light = ((tight + wide) * _Corona + rays) * _Brightness
                            + (tight * max(_Corona, 0.45) + wide * 0.3 + rays) * _Flash;
                light *= breath * fade;
                return half4(colour * light, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
