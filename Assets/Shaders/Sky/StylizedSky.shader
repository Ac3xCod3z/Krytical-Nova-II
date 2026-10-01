// Stylized procedural skybox: time-of-day gradient, sun/moon discs, procedural stars and soft
// wind-driven clouds — all in one pass drawn once by URP after opaques (hidden pixels are
// depth-rejected, no overdraw). The sky sits at the far plane, so ComicPost's IsSky() leaves it
// free of ink outlines. Driven entirely by globals from KryticalNova.World.EnvironmentPresenter.
//
// Cost: full quality = 4 texture samples + ALU per visible sky pixel; _KN_SKY_LOW = 1 sample.
Shader "Skybox/KryticalNova/Stylized Sky"
{
    Properties
    {
        [Header(Stars)]
        _StarDensity("Star Cells Per Face", Range(20, 400)) = 160
        _StarChance("Star Chance", Range(0, 1)) = 0.07
        _StarSize("Star Size", Range(0.02, 0.5)) = 0.12
        _StarBrightness("Star Brightness", Range(0, 8)) = 2.5
        _StarTwinkle("Star Twinkle", Range(0, 1)) = 0.5

        [Header(Moon)]
        _MoonCrescent("Moon Crescent", Range(0, 1)) = 0.55
        _MoonBrightness("Moon Brightness", Range(0, 8)) = 2

        [Header(Clouds)]
        _CloudHorizonFade("Cloud Horizon Fade", Range(0.01, 0.5)) = 0.16
        _CloudLightOffset("Cloud Light Sample Offset (uv)", Range(0, 0.1)) = 0.025
        _CloudLightContrast("Cloud Light Contrast", Range(0, 8)) = 2.5
        _CloudAmbientLift("Cloud Shadow Lift", Range(0, 1)) = 0.35
        _SilverLining("Silver Lining", Range(0, 4)) = 1.2
        _CirrusScale("Cirrus Scale (integer)", Float) = 2
        _CirrusThreshold("Cirrus Threshold", Range(0, 1)) = 0.55

        [Header(Horizon)]
        _HorizonFogHeight("Horizon Fog Height", Range(0.01, 0.6)) = 0.18
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off

        Pass
        {
            Name "StylizedSky"

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fragment _ _KN_SKY_LOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "SkyCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _StarDensity;
                float _StarChance;
                float _StarSize;
                float _StarBrightness;
                float _StarTwinkle;
                float _MoonCrescent;
                float _MoonBrightness;
                float _CloudHorizonFade;
                float _CloudLightOffset;
                float _CloudLightContrast;
                float _CloudAmbientLift;
                float _SilverLining;
                float _CirrusScale;
                float _CirrusThreshold;
                float _HorizonFogHeight;
            CBUFFER_END

            // Globals from EnvironmentPresenter
            float4 _KN_SunDir;      // xyz toward sun
            float4 _KN_MoonDir;     // xyz toward moon
            float4 _KN_SkyZenith;
            float4 _KN_SkyHorizon;
            float4 _KN_SkyGround;
            float4 _KN_SkyParams;   // x horizon falloff, y star visibility, z lightning flash, w horizon fog amount
            float4 _KN_SunColor;    // rgb = sun colour × visibility
            float4 _KN_SunParams;   // x cos(disc size), y glow power, z glow intensity, w disc HDR intensity
            float4 _KN_MoonColor;   // rgb = moon colour × visibility
            float4 _KN_MoonParams;  // x cos(disc size)
            float4 _KN_CloudLit;
            float4 _KN_CloudShade;
            float4 _KN_CloudExtra;  // x cirrus amount

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 dirWS      : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.dirWS = TransformObjectToWorldDir(input.positionOS.xyz, false);
                return o;
            }

            // Dave Hoskins' hash12 — no visible lattice patterns.
            float Hash21(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            // Disc mask with a ~15% soft edge; cosSize = cos(angular radius).
            half Disc(float cosAngle, float cosSize)
            {
                float edge = (1.0 - cosSize) * 0.3;
                return smoothstep(cosSize - edge, cosSize + edge * 0.25, cosAngle);
            }

            // Procedural stars on a cube-face cell grid (ALU only).
            half Stars(float3 d)
            {
                float3 a = abs(d);
                float2 uv; float face;
                if (a.x >= a.y && a.x >= a.z)      { uv = d.yz / a.x; face = d.x > 0 ? 0 : 1; }
                else if (a.y >= a.z)               { uv = d.xz / a.y; face = d.y > 0 ? 2 : 3; }
                else                               { uv = d.xy / a.z; face = d.z > 0 ? 4 : 5; }

                float2 g = uv * _StarDensity;
                float2 cell = floor(g);
                float2 seed = cell + face * 1013.0;
                float present = step(1.0 - _StarChance, Hash21(seed));
                float2 center = float2(Hash21(seed + 17.1), Hash21(seed + 31.7)) * 0.7 + 0.15;
                float dist = length(frac(g) - center);
                float size = _StarSize * (0.5 + Hash21(seed + 5.3));
                float twinkle = 1.0 - _StarTwinkle * (0.5 + 0.5 * sin(_Time.y * (1.5 + Hash21(seed + 9.9) * 3.0) + Hash21(seed) * 6.2831));
                return present * smoothstep(size, size * 0.3, dist) * twinkle;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float3 d = normalize(input.dirWS);
                float up = saturate(d.y);

                // --- gradient
                half3 sky = lerp(_KN_SkyZenith.rgb, _KN_SkyHorizon.rgb, pow(1.0 - up, _KN_SkyParams.x));
                sky = lerp(sky, _KN_SkyGround.rgb, saturate(-d.y * 6.0));

                float sunDot = dot(d, _KN_SunDir.xyz);
                float moonDot = dot(d, _KN_MoonDir.xyz);
                half aboveHorizon = smoothstep(-0.02, 0.01, d.y);

                // --- clouds
                half cloudAlpha = 0.0;
                half3 cloudColor = 0.0;
                half cirrus = 0.0;
                half horizonFade = smoothstep(0.0, _CloudHorizonFade, d.y);

                // Not branched: implicit-derivative samples must stay in uniform control flow.
                // Below-horizon sky is almost always depth-rejected behind terrain anyway.
                {
                    float dist = _KN_CloudShape.x / max(d.y, 0.02);
                    float2 uv = KN_CloudUV(_WorldSpaceCameraPos.xz + d.xz * dist);

                #if defined(_KN_SKY_LOW)
                    half2 cloud = KN_CloudDensityLod(uv, 0);
                    half dens = cloud.x;
                    half light = 1.0 - cloud.y * 0.45;
                #else
                    half2 cloud = KN_CloudDensity(uv);
                    half dens = cloud.x;
                    // Soft painted shading: deeper toward the light = this side is in the cloud's shadow.
                    float2 toLight = normalize(_KN_CloudLightDir.xz + 1e-4) * _CloudLightOffset;
                    half depthToLight = KN_CloudDensityLod(uv + toLight, 0).y;
                    half light = saturate((cloud.y - depthToLight) * _CloudLightContrast + 0.65);
                    light = lerp(_CloudAmbientLift, 1.0, light) * (1.0 - cloud.y * 0.3);

                    half c = SAMPLE_TEXTURE2D(_KN_CloudNoise, sampler_KN_CloudNoise, uv * _CirrusScale + _KN_CloudWind.zw * 0.5).b;
                    cirrus = smoothstep(_CirrusThreshold, _CirrusThreshold + 0.3, c) * _KN_CloudExtra.x * horizonFade;
                #endif

                    cloudAlpha = dens * _KN_CloudParams.y * horizonFade;
                    cloudColor = lerp(_KN_CloudShade.rgb, _KN_CloudLit.rgb, light);
                    cloudColor *= 1.0 - _KN_CloudParams.w * smoothstep(0.25, 1.0, cloud.y);

                    // Thin edges glow when backlit by the sun.
                    half edge = saturate(dens * (1.0 - dens) * 4.0);
                    cloudColor += _KN_SunColor.rgb * pow(saturate(sunDot), 8.0) * edge * _SilverLining;
                    cloudColor += _KN_SkyParams.z * (0.5 + 0.5 * dens) * half3(0.75, 0.82, 1.0);
                }

                // --- night sky
                UNITY_BRANCH
                if (_KN_SkyParams.y > 0.001)
                    sky += Stars(d) * _StarBrightness * _KN_SkyParams.y * aboveHorizon * (1.0 - cloudAlpha) * (1.0 - cirrus);

                float moonCos = _KN_MoonParams.x;
                half moon = Disc(moonDot, moonCos);
                float3 moonSide = normalize(cross(_KN_MoonDir.xyz, float3(0, 1, 0.001)));
                float3 shadowCenter = normalize(_KN_MoonDir.xyz + moonSide * sqrt(1.0 - moonCos * moonCos) * _MoonCrescent * 1.6);
                moon *= 1.0 - 0.9 * Disc(dot(d, shadowCenter), moonCos);
                sky += _KN_MoonColor.rgb * moon * _MoonBrightness * aboveHorizon * (1.0 - cloudAlpha * 0.95);

                // --- sun
                half sunDisc = Disc(sunDot, _KN_SunParams.x);
                sky += _KN_SunColor.rgb * sunDisc * _KN_SunParams.w * aboveHorizon * (1.0 - cloudAlpha);

                // --- compose clouds
                sky = lerp(sky, _KN_CloudLit.rgb, cirrus * 0.55 * (1.0 - cloudAlpha));
                sky = lerp(sky, cloudColor, cloudAlpha);

                // Sun glow scatters through thin cloud, mostly blocked by thick cloud.
                half glow = pow(saturate(sunDot), _KN_SunParams.y) * _KN_SunParams.z;
                sky += _KN_SunColor.rgb * glow * (1.0 - cloudAlpha * 0.7);
                sky += _KN_SkyParams.z * 0.15 * half3(0.75, 0.82, 1.0);

                // --- horizon meets distance fog without a seam
                half fogBand = 1.0 - saturate(max(d.y, 0.0) / _HorizonFogHeight);
                sky = lerp(sky, unity_FogColor.rgb, _KN_SkyParams.w * fogBand * fogBand);

                return half4(sky, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
