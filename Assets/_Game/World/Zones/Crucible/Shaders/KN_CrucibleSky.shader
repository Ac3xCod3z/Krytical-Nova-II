// The Crucible's sky dome (owner's list 16 Nov 2026, section 4; Fable's SkyDome_R500): Fable's painted night (aurora, peak ring,
// lava plain) with the aurora alive (curtains brighten and drift), twinkling stars, and Zone01's own moon drawn from the sky globals
// the EnvironmentPresenter sets every frame (_KN_MoonDir / _KN_MoonTex / _KN_BodyTex* / _KN_MoonPhase), so the dome hides the
// skybox without losing the moon. Unlit, no fog, no shadows; one texture fetch for the sky, one for the moon.
Shader "KN/Crucible Sky Dome"
{
    Properties
    {
        _MainTex ("Painted sky (equirect, Fable)", 2D) = "black" {}
        _Exposure ("Exposure", Range(0, 4)) = 1
        _AuroraBoost ("Aurora live boost", Range(0, 2)) = 0.7
        _AuroraSpeed ("Aurora drift speed", Range(0, 1)) = 0.06
        _AuroraBand ("Aurora band (v from, v to)", Vector) = (0.52, 0.85, 0, 0)
        _StarDensity ("Star density", Range(0, 1)) = 0.35
        _StarBrightness ("Star brightness", Range(0, 4)) = 1.4
        _MoonBrightness ("Moon brightness", Range(0, 4)) = 1.2
        _MoonEarthshine ("Moon dark side", Range(0, 1)) = 0.12
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry+10" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "Unlit"
            Cull Off
            ZWrite On
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_KN_MoonTex); SAMPLER(sampler_KN_MoonTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float _Exposure, _AuroraBoost, _AuroraSpeed, _StarDensity, _StarBrightness, _MoonBrightness, _MoonEarthshine;
                float4 _AuroraBand;
            CBUFFER_END
            float4 _KN_MoonDir, _KN_MoonColor, _KN_BodyTexParams, _KN_BodyTexBrightness, _KN_BodyTexAspect, _KN_MoonPhase;

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 positionWS : TEXCOORD1; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            float Hash(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p); f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), f.x), lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), f.x), f.y);
            }

            half4 frag(Varyings i) : SV_Target
            {
                half3 sky = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).rgb * _Exposure;
                float t = _Time.y;
                float3 d = normalize(i.positionWS - _WorldSpaceCameraPos);

                // live aurora: only the saturated green / violet curtains in the band are pushed, by drifting vertical streaks
                half band = smoothstep(_AuroraBand.x, _AuroraBand.x + 0.05, i.uv.y) * (1.0 - smoothstep(_AuroraBand.y - 0.08, _AuroraBand.y, i.uv.y));
                half sat = saturate((max(sky.r, max(sky.g, sky.b)) - min(sky.r, min(sky.g, sky.b))) * 3.0);
                float streak = Noise(float2(i.uv.x * 90.0 + t * _AuroraSpeed * 20.0, i.uv.y * 3.0 - t * 0.05));
                float sweep = Noise(float2(i.uv.x * 6.0 - t * _AuroraSpeed, t * 0.03));
                sky *= 1.0 + band * sat * _AuroraBoost * (streak * 0.6 + sweep * 0.8 - 0.5);

                // stars above the band, twinkling; hidden where the painting is bright
                float2 sc = i.uv * float2(900.0, 450.0);
                float2 cell = floor(sc);
                float h = Hash(cell);
                float star = step(1.0 - _StarDensity * 0.08, h) * (1.0 - smoothstep(0.0, 0.35, length(frac(sc) - 0.5)));
                star *= 0.6 + 0.4 * sin(t * (2.0 + h * 5.0) + h * 40.0);
                half dark = 1.0 - saturate(dot(sky, half3(0.3, 0.6, 0.1)) * 6.0);
                sky += star * dark * _StarBrightness * smoothstep(_AuroraBand.x - 0.02, 0.62, i.uv.y);

                // Zone01's moon (art, phase, brightness from the presenter); only above the horizon
                float3 md = _KN_MoonDir.xyz;
                float facing = dot(d, md);
                if (facing > 0.0 && md.y > -0.05 && _KN_BodyTexParams.y > 0.0)
                {
                    float3 right = normalize(cross(float3(0, 1, 0), md) + float3(1e-4, 0, 0));
                    float3 upv = cross(md, right);
                    float2 plane = float2(dot(d, right), dot(d, upv)) / (facing * _KN_BodyTexParams.y);
                    float2 uv = float2(plane.x / max(_KN_BodyTexAspect.y, 0.01), plane.y) * 0.5 + 0.5;
                    if (all(uv >= 0.0) && all(uv <= 1.0))
                    {
                        half4 art = SAMPLE_TEXTURE2D(_KN_MoonTex, sampler_KN_MoonTex, uv);
                        float2 p = uv * 2.0 - 1.0;
                        float pr = length(p);
                        float3 n = pr < 1.0 ? float3(p, sqrt(saturate(1.0 - pr * pr))) : float3(p / max(pr, 1e-4), 0.0);
                        float pa = _KN_MoonPhase.x * 6.2831853;
                        half lit = smoothstep(-0.08, 0.08, dot(n, float3(sin(pa), 0.0, -cos(pa))));
                        half3 moon = art.rgb * lerp(_MoonEarthshine, 1.0, lit) * _MoonBrightness * max(_KN_BodyTexBrightness.y, 0.35);
                        sky = lerp(sky, moon, art.a * saturate(md.y * 20.0 + 1.0));
                    }
                }
                return half4(sky, 1.0);
            }
            ENDHLSL
        }
    }
}
