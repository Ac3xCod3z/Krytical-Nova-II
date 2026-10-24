// The star in the middle of a Nova (NovaHalo): a small sphere of slow-moving plasma, hot in the middle and the
// tier's colour at its limb. It is drawn solid and writes depth, so the rings pass behind it and in front of it as
// real rings would, and it reads against any sky. HDR: above 1 it blooms as well.
Shader "KryticalNova/NovaCore"
{
    Properties
    {
        [HDR] _Inner ("Hot middle", Color) = (1, 1, 1, 1)
        [HDR] _Outer ("Limb", Color) = (0.6, 0.8, 1, 1)
        _Brightness ("Brightness", Float) = 1.5
        _Flash ("Flash (added brightness, set by code)", Float) = 0
        _Prism ("Spectrum over the surface", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+16" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Core"
            Tags { "LightMode" = "UniversalForward" }
            Blend Off
            ZWrite On
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Inner;
                half4 _Outer;
                half _Brightness;
                half _Flash;
                half _Prism;
            CBUFFER_END

            // In a fight the Nova swells on the beat of the fight music (BeatGlow sets it for every material at once; 0 out of one).
            half _KN_BeatPulse;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 viewWS : TEXCOORD1;
                float3 positionOS : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.viewWS = GetWorldSpaceNormalizeViewDir(positionWS);
                output.positionOS = input.positionOS.xyz;
                return output;
            }

            half3 Spectrum(float hue)
            {
                half3 k = half3(1.0, 2.0 / 3.0, 1.0 / 3.0);
                half3 p = abs(frac(hue + k) * 6.0 - 3.0);
                return lerp(half3(1, 1, 1), saturate(p - 1.0), 0.7);
            }

            half4 frag(Varyings input) : SV_Target
            {
                float facing = saturate(dot(normalize(input.normalWS), normalize(input.viewWS)));
                float limb = pow(1.0 - facing, 1.6);

                // Plasma: two bands of slow turbulence sliding over each other.
                float3 p = input.positionOS * 2.0;
                float t = _Time.y;
                float bands = sin(p.y * 9.0 + t * 1.1 + sin(p.x * 7.0 + t * 0.6) * 1.6);
                bands += sin(p.z * 11.0 - t * 0.9 + sin(p.y * 6.0 - t * 0.4) * 1.3);
                float plasma = 0.82 + 0.18 * bands * 0.5;

                half3 outer = _Outer.rgb;
                outer = lerp(outer, Spectrum(frac(atan2(p.z, p.x) / 6.2831853 + t * 0.06)) * max(max(outer.r, outer.g), outer.b), _Prism);
                half3 colour = lerp(_Inner.rgb, outer, saturate(limb * 1.25)) * plasma;
                colour += _Inner.rgb * pow(facing, 4.0) * 0.5;      // the white-hot middle
                colour += outer * pow(limb, 3.0) * 0.8;             // a bright edge, so it reads as a globe
                return half4(colour * (_Brightness + _Flash) * (1.0 + _KN_BeatPulse), 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
