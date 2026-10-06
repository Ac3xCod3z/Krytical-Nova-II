// The glow a tempered weapon wears (WeaponVisualMount). Drawn as a second, additive pass over the weapon's own
// material, so it works on any weapon whatever shader that uses: a rim that hugs the silhouette, a faint fill so
// flat blades still read, a slow breath, and for the brightest looks a scatter of sparkles. The colour is the
// Temper band's; the strength is the rarity's (TemperSettings). HDR, so an intensity above 1 blooms.
Shader "KryticalNova/TemperGlow"
{
    Properties
    {
        [HDR] _GlowColor ("Colour", Color) = (1, 0.86, 0.24, 1)
        _Intensity ("Intensity", Float) = 1
        _RimPower ("Rim power", Range(0.5, 8)) = 3
        _Fill ("Fill", Range(0, 1)) = 0.12
        _Pulse ("Pulse", Range(0, 1)) = 0.15
        _PulseSpeed ("Pulse speed", Float) = 0.6
        _Sparkle ("Sparkle", Range(0, 4)) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+10" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "TemperGlow"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Back
            Offset -1, -1

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

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

            CBUFFER_START(UnityPerMaterial)
                half4 _GlowColor;
                half _Intensity;
                half _RimPower;
                half _Fill;
                half _Pulse;
                half _PulseSpeed;
                half _Sparkle;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.viewWS = GetWorldSpaceViewDir(positionWS);
                output.positionOS = input.positionOS.xyz;
                return output;
            }

            float Hash(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            half4 frag(Varyings input) : SV_Target
            {
                half3 normal = normalize(input.normalWS);
                half3 view = normalize(input.viewWS);
                half rim = pow(saturate(1.0 - abs(dot(normal, view))), _RimPower);
                half breath = 1.0 + _Pulse * sin(_Time.y * _PulseSpeed * 6.2831853);
                half glow = (rim + _Fill) * breath;

                // Sparkles: cells of the model's own space that flash for a moment each, so they ride with the weapon.
                float tick = floor(_Time.y * 6.0);
                float cell = Hash(floor(input.positionOS * 70.0) + tick);
                half sparkle = step(0.975, cell) * _Sparkle;

                return half4(_GlowColor.rgb * _Intensity * (glow + sparkle), 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
