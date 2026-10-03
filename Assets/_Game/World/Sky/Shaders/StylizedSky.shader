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

        [Header(Sun and Moon Art)]
        _BodySaturation("Art Saturation", Range(0, 1.5)) = 0.75
        _BodyContrast("Art Contrast", Range(0.2, 1.5)) = 0.8
        _BodyInkLift("Ink Outline Lift (light bleeds over the black lines)", Range(0, 1)) = 0.4
        _SunGlare("Sun Glare (surface light washing out the art)", Range(0, 1)) = 0.55
        _SunGlareRadius("Sun Glare Radius (of the art square)", Range(0.05, 1)) = 0.4
        _SunGlareHDR("Sun Glare HDR Boost (feeds Bloom)", Range(0, 6)) = 0.8
        _MoonGlare("Moon Glare", Range(0, 1)) = 0.2
        _MoonGlareRadius("Moon Glare Radius", Range(0.05, 1)) = 0.45
        _BodyHaze("Haze (always)", Range(0, 1)) = 0.08
        _BodyHorizonHaze("Extra Haze Near Horizon", Range(0, 1)) = 0.6
        _BodyHazeHeight("Haze Fade Height (sin elevation)", Range(0.02, 1)) = 0.35
        _MoonHalo("Moon Halo", Range(0, 2)) = 0.3
        _MoonHaloPower("Moon Halo Tightness", Range(4, 512)) = 90

        [Header(Sun Rays Heat Shimmer)]
        _SunRayStart("Ray Start (art radius, disc edge ~0.5)", Range(0.2, 0.9)) = 0.52
        _SunRayVisibility("Ray Visibility", Range(0, 1)) = 0.3
        _SunShimmerAmount("Shimmer Amount (uv)", Range(0, 0.08)) = 0.03
        _SunShimmerSpeed("Shimmer Speed", Range(0, 0.5)) = 0.09
        _SunShimmerScale("Shimmer Scale", Range(0.5, 12)) = 5
        _SunShimmerGlow("Shimmer On Glow (fraction of the ray wobble)", Range(0, 1)) = 0.3

        [Header(Sunset)]
        _SunsetHeight("Sunset Grade Height (sin elevation)", Range(0.02, 0.6)) = 0.28
        _SunsetGrade("Sunset Grade Strength", Range(0, 1)) = 0.85
        _SunsetFlatten("Sunset Flatten (vertical squash)", Range(0, 0.3)) = 0.15

        [Header(Moon Phase)]
        _MoonDisc("Moon Disc In Art (centre uv xy, radius uv zw)", Vector) = (0.495, 0.5, 0.23, 0.209)
        _MoonEarthshine("Dark Side Earthshine", Range(0, 0.5)) = 0.015
        _MoonDarkAlpha("Dark Side Opacity", Range(0, 1)) = 0.95

        [Header(Cloud Rim Glow)]
        _BodyRimGlow("Cloud Rim Glow Near Sun/Moon", Range(0, 8)) = 2.5

        [Header(Rainbow)]
        _RainbowIntensity("Rainbow Intensity", Range(0, 2)) = 1
        _RainbowWidth("Rainbow Width", Range(0.5, 2)) = 1
        _RainbowSecondary("Secondary Bow Strength", Range(0, 1)) = 0.35

        [Header(22 Degree Halo)]
        _HaloIntensity("Halo Intensity", Range(0, 2)) = 0.6
        _HaloWidth("Halo Width (degrees)", Range(0.5, 4)) = 1.5
        _HaloMoon("Moon Halo Strength (relative)", Range(0, 1)) = 0.45

        [Header(Clouds)]
        _CloudHorizonFade("Cloud Horizon Fade", Range(0.01, 0.5)) = 0.16
        _CloudLightOffset("Cloud Light Sample Offset (uv)", Range(0, 0.1)) = 0.025
        _CloudLightContrast("Cloud Light Contrast", Range(0, 8)) = 2.5
        _CloudAmbientLift("Cloud Shadow Lift", Range(0, 1)) = 0.35
        _SilverLining("Silver Lining", Range(0, 4)) = 1.2
        _CirrusScale("Cirrus Scale (integer)", Float) = 2
        _CirrusThreshold("Cirrus Threshold", Range(0, 1)) = 0.55
        _CirrusStrength("Cirrus Streaks Strength (0 = off)", Range(0, 1)) = 0

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
                float _CirrusStrength;
                float _BodySaturation;
                float _BodyContrast;
                float _BodyInkLift;
                float _SunGlare;
                float _SunGlareRadius;
                float _SunGlareHDR;
                float _MoonGlare;
                float _MoonGlareRadius;
                float _BodyHaze;
                float _BodyHorizonHaze;
                float _BodyHazeHeight;
                float _MoonHalo;
                float _MoonHaloPower;
                float _SunRayStart;
                float _SunRayVisibility;
                float _SunShimmerAmount;
                float _SunShimmerSpeed;
                float _SunShimmerScale;
                float _SunsetHeight;
                float _SunsetGrade;
                float _SunsetFlatten;
                float4 _MoonDisc;
                float _MoonEarthshine;
                float _MoonDarkAlpha;
                float _BodyRimGlow;
                float _RainbowIntensity;
                float _RainbowWidth;
                float _RainbowSecondary;
                float _SunShimmerGlow;
                float _HaloIntensity;
                float _HaloWidth;
                float _HaloMoon;
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
            float4 _KN_BodyTexParams;     // x tan(sun tex radius), y tan(moon tex radius), z sun tex on, w moon tex on
            float4 _KN_BodyTexBrightness; // x sun tex brightness × visibility, y moon tex brightness × visibility, z weather diffusion (0 clear .. 1 overcast)
            float4 _KN_BodyTexAspect;     // x sun art width/height, y moon art width/height
            float4 _KN_MoonPhase;         // x phase 0..1 (0 new, 0.5 full), y illuminated fraction
            float4 _KN_Rainbow;           // x strength 0..1
            float4 _KN_Halo;              // x sun 22° halo strength 0..1, y moon halo strength 0..1
            float4 _KN_SunShimmer;        // x heat shimmer multiplier (0 none, 1 clear midday, >1 GM boost)
            TEXTURE2D(_KN_SunTex);  SAMPLER(sampler_KN_SunTex);
            TEXTURE2D(_KN_MoonTex); SAMPLER(sampler_KN_MoonTex);
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

            // Gnomonic projection of the view direction onto a square billboard centred on a sky body, kept upright.
            // Returns uv in [0,1] over the art; 'inside' is 0 behind the body and outside the square.
            // tanRadius sizes the art's height; aspect (width/height) keeps non-square art undistorted.
            float2 BodyUV(float3 d, float3 bodyDir, float tanRadius, float aspect, out half inside)
            {
                float3 right = normalize(cross(float3(0, 1, 0), bodyDir) + float3(1e-4, 0, 0));
                float3 upv = cross(bodyDir, right);
                float facing = dot(d, bodyDir);
                float2 plane = float2(dot(d, right), dot(d, upv)) / (max(facing, 1e-3) * tanRadius);
                float2 uv = float2(plane.x / max(aspect, 0.01), plane.y) * 0.5 + 0.5;
                inside = step(0.0, facing) * step(0.0, uv.x) * step(uv.x, 1.0) * step(0.0, uv.y) * step(uv.y, 1.0);
                return uv;
            }

            half InsideUnit(float2 uv)
            {
                return step(0.0, uv.x) * step(uv.x, 1.0) * step(0.0, uv.y) * step(uv.y, 1.0);
            }

            // Real sunset colours: the atmosphere filters the disc to orange and deep red, keyed off brightness.
            half3 SunsetRamp(half luma)
            {
                half3 deep = half3(0.55, 0.07, 0.03);
                half3 orange = half3(1.0, 0.42, 0.08);
                half3 warm = half3(1.0, 0.82, 0.55);
                return lerp(lerp(deep, orange, smoothstep(0.0, 0.55, luma)), warm, smoothstep(0.55, 1.0, luma));
            }

            // Visible-spectrum colour for t: 0 = violet .. 1 = red.
            half3 Spectrum(half t)
            {
                half h = lerp(0.78, 0.0, saturate(t));
                return saturate(abs(frac(h + half3(0.0, 2.0 / 3.0, 1.0 / 3.0)) * 6.0 - 3.0) - 1.0);
            }

            // Primary (~42°) and secondary (~51°) bows around the antisolar point, with Alexander's dark band between.
            // Returns rgb to add and a multiplier for the sky behind.
            half3 Rainbow(float3 d, float3 sunDir, half strength, out half skyScale)
            {
                skyScale = 1.0;
                if (strength <= 0.0001 || sunDir.y <= 0.0) return 0.0; // no sun above the horizon, no bow
                float theta = degrees(acos(clamp(dot(d, -sunDir), -1.0, 1.0)));
                half w = _RainbowWidth;

                // Primary: violet inside (40.6°) .. red outside (42.3°).
                half t1 = (theta - 41.45) / (0.85 * w) * 0.5 + 0.5;
                half band1 = smoothstep(-0.35, 0.1, t1) * smoothstep(1.35, 0.9, t1);
                // Secondary: colours reversed, red inside (50°) .. violet outside (53°).
                half t2 = (theta - 51.5) / (1.5 * w) * 0.5 + 0.5;
                half band2 = smoothstep(-0.35, 0.1, t2) * smoothstep(1.35, 0.9, t2) * _RainbowSecondary;

                half3 col = Spectrum(t1) * band1 + Spectrum(1.0 - t2) * band2;
                // Alexander's dark band between the bows; a brighter sky just inside the primary.
                half dark = smoothstep(42.3, 43.5, theta) * smoothstep(50.0, 48.8, theta);
                half inner = smoothstep(41.0, 32.0, theta) * smoothstep(18.0, 30.0, theta);
                skyScale = 1.0 - 0.07 * strength * dark + 0.05 * strength * inner;
                return col * strength;
            }

            // 22° ice-crystal halo: a thin soft ring, faint red inner edge fading to blue-white outside, with the
            // slightly darker sky just inside it. Returns rgb to add (pre-scaled by strength); skyScale darkens the gap.
            half3 Halo(float3 d, float3 bodyDir, half strength, out half skyScale)
            {
                skyScale = 1.0;
                if (strength <= 0.0001) return 0.0;
                float theta = degrees(acos(clamp(dot(d, bodyDir), -1.0, 1.0)));
                half w = _HaloWidth;
                half x = (theta - 22.0) / w;
                half ring = exp(-x * x * 2.0);
                // Real halos are sharp-ish and reddish on the inside, washing out to white then pale blue.
                half3 col = lerp(Spectrum(saturate(0.55 - x * 0.35)), 1.0.xxx, 0.6);
                half inner = smoothstep(14.0, 20.0, theta) * smoothstep(22.0 - w * 0.6, 20.0 - w, theta);
                skyScale = 1.0 - 0.08 * strength * inner;
                return col * ring * strength;
            }

            // Stylized-realism treatment for the sun/moon art: the body's own light washes out its centre, ink lines
            // are lifted by that light, colours are softened, and the atmosphere hazes it toward the sky near the horizon.
            // Returns rgb (already scaled by brightness, HDR at the glare core) and the coverage alpha.
            half3 LightTint(half3 lightColor)
            {
                return lightColor / max(max(lightColor.r, max(lightColor.g, lightColor.b)), 1e-3);
            }

            half4 StylizeBody(half4 art, float2 uv, float elevation, half3 lightColor, half brightness,
                              half glare, half glareRadius, half hdrBoost, half3 skyBehind)
            {
                half3 tint = LightTint(lightColor);
                half3 c = art.rgb;
                half luma = dot(c, half3(0.299, 0.587, 0.114));
                c = lerp(luma.xxx, c, _BodySaturation);
                c = saturate(lerp(0.5.xxx, c, _BodyContrast));
                c += (1.0 - c) * _BodyInkLift * tint;

                half r = length(uv - 0.5) * 2.0;
                half core = saturate(1.0 - r / glareRadius);
                core *= core;
                c = lerp(c, tint, saturate(glare * core));
                c *= brightness * (1.0 + core * glare * hdrBoost);

                half low = 1.0 - smoothstep(0.0, _BodyHazeHeight, elevation);
                half haze = saturate(_BodyHaze + low * _BodyHorizonHaze);
                c = lerp(c, skyBehind, haze * 0.75);
                return half4(c, art.a * (1.0 - haze * 0.35));
            }

            // Overcast / rain: the art dissolves into a pale, soft disc of light (r = 0 centre .. 1 art edge).
            half4 DiffuseBody(half4 look, half r, half3 tint, half brightness, half diffusion)
            {
                half4 pale = half4(lerp(tint, 1.0.xxx, 0.5) * brightness * 0.85, exp(-r * r * 7.0) * 0.8);
                return lerp(look, pale, diffusion);
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
                    cirrus = smoothstep(_CirrusThreshold, _CirrusThreshold + 0.3, c) * _KN_CloudExtra.x * horizonFade * _CirrusStrength;
                #endif

                    cloudAlpha = dens * _KN_CloudParams.y * horizonFade;
                    cloudColor = lerp(_KN_CloudShade.rgb, _KN_CloudLit.rgb, light);
                    cloudColor *= 1.0 - _KN_CloudParams.w * smoothstep(0.25, 1.0, cloud.y);

                    // Thin edges glow when backlit by the sun.
                    half edge = saturate(dens * (1.0 - dens) * 4.0);
                    cloudColor += _KN_SunColor.rgb * pow(saturate(sunDot), 8.0) * edge * _SilverLining;
                    // Clouds crossing the sun/moon catch a bright inked rim (HDR for the sun, so Bloom picks it up).
                    float sunSize = atan(_KN_BodyTexParams.x) * 0.5;
                    float moonSize = atan(_KN_BodyTexParams.y) * 0.5;
                    half sunRim = smoothstep(cos(sunSize * 4.0), cos(sunSize * 0.6), sunDot);
                    half moonRim = smoothstep(cos(moonSize * 4.0), cos(moonSize * 0.6), moonDot);
                    half clearSky = 1.0 - _KN_BodyTexBrightness.z;
                    cloudColor += _KN_SunColor.rgb * sunRim * edge * _BodyRimGlow * clearSky;
                    cloudColor += _KN_MoonColor.rgb * moonRim * edge * _BodyRimGlow * 0.4 * clearSky * _KN_MoonPhase.y;
                    cloudColor += _KN_SkyParams.z * (0.5 + 0.5 * dens) * half3(0.75, 0.82, 1.0);
                }

                // --- night sky
                UNITY_BRANCH
                if (_KN_SkyParams.y > 0.001)
                    sky += Stars(d) * _StarBrightness * _KN_SkyParams.y * aboveHorizon * (1.0 - cloudAlpha) * (1.0 - cirrus);

                // Textured bodies are sampled unconditionally (uniform control flow for derivatives), then masked.
                half diffusion = _KN_BodyTexBrightness.z;
                half sunInside, moonInside;
                float2 sunUV = BodyUV(d, _KN_SunDir.xyz, _KN_BodyTexParams.x, _KN_BodyTexAspect.x, sunInside);
                float2 moonUV = BodyUV(d, _KN_MoonDir.xyz, _KN_BodyTexParams.y, _KN_BodyTexAspect.y, moonInside);

                // Sunset: refraction flattens the low sun vertically.
                half sunLow = 1.0 - smoothstep(0.0, _SunsetHeight, _KN_SunDir.y);
                sunUV.y = 0.5 + (sunUV.y - 0.5) / (1.0 - _SunsetFlatten * sunLow);
                half sunR = length(sunUV - 0.5) * 2.0;
                // Heat shimmer: only the spikes waver (the disc stays solid), driven by the cloud noise texture.
                half rayMask = smoothstep(_SunRayStart, _SunRayStart + 0.12, sunR);
                float2 shimmerUV = sunUV * _SunShimmerScale + float2(_Time.y, -_Time.y * 0.7) * _SunShimmerSpeed;
                half3 shimmerNoise = SAMPLE_TEXTURE2D_LOD(_KN_CloudNoise, sampler_KN_CloudNoise, shimmerUV, 0).rga;
                half shimmerAmt = _SunShimmerAmount * _KN_SunShimmer.x;
                sunUV += (shimmerNoise.xy - 0.5) * shimmerAmt * rayMask;
                sunInside *= InsideUnit(sunUV);

                half4 sunArt = SAMPLE_TEXTURE2D(_KN_SunTex, sampler_KN_SunTex, sunUV);
                half4 moonArt = SAMPLE_TEXTURE2D(_KN_MoonTex, sampler_KN_MoonTex, moonUV);

                float moonCos = _KN_MoonParams.x;
                half moon = Disc(moonDot, moonCos);
                float3 moonSide = normalize(cross(_KN_MoonDir.xyz, float3(0, 1, 0.001)));
                float3 shadowCenter = normalize(_KN_MoonDir.xyz + moonSide * sqrt(1.0 - moonCos * moonCos) * _MoonCrescent * 1.6);
                moon *= 1.0 - 0.9 * Disc(dot(d, shadowCenter), moonCos);
                sky += _KN_MoonColor.rgb * moon * _MoonBrightness * aboveHorizon * (1.0 - cloudAlpha * 0.95) * (1.0 - _KN_BodyTexParams.w);
                half4 moonLook = StylizeBody(moonArt, moonUV, _KN_MoonDir.y, _KN_MoonColor.rgb, _KN_BodyTexBrightness.y,
                                             _MoonGlare, _MoonGlareRadius, 0.5, sky);
                half moonLit = 1.0;      // lit-side mask over the moon's disc
                half moonOnDisc = 0.0;   // 1 inside the disc, 0 around it
                {
                    // Phase: a lit-side mask over the art. Sphere normal from the art's disc; shards outside the disc
                    // take the rim normal so they phase with the side they float on.
                    float2 p = (moonUV - _MoonDisc.xy) / _MoonDisc.zw;
                    float pr = length(p);
                    float3 n = pr < 1.0 ? float3(p, sqrt(saturate(1.0 - pr * pr))) : float3(p / max(pr, 1e-4), 0.0);
                    float phaseAngle = _KN_MoonPhase.x * 6.2831853;
                    float3 L = float3(sin(phaseAngle), 0.0, -cos(phaseAngle));
                    half lit = smoothstep(-0.08, 0.08, dot(n, L));
                    moonLook.rgb *= lerp(_MoonEarthshine, 1.0, lit);
                    moonLook.a *= lerp(_MoonDarkAlpha, 1.0, lit);
                    moonLit = lit;
                    moonOnDisc = (1.0 - smoothstep(0.92, 1.04, pr)) * moonInside * _KN_BodyTexParams.w;
                }
                half moonR = length((moonUV - _MoonDisc.xy) / _MoonDisc.zw) * 0.5;
                moonLook = DiffuseBody(moonLook, moonR, LightTint(_KN_MoonColor.rgb), _KN_BodyTexBrightness.y * _KN_MoonPhase.y, diffusion);
                half moonArtAlpha = moonLook.a * moonInside * _KN_BodyTexParams.w * aboveHorizon * saturate(_KN_BodyTexBrightness.y * 4.0);
                sky = lerp(sky, moonLook.rgb, moonArtAlpha);

                // --- sun
                half sunDisc = Disc(sunDot, _KN_SunParams.x);
                sky += _KN_SunColor.rgb * sunDisc * _KN_SunParams.w * aboveHorizon * (1.0 - cloudAlpha) * (1.0 - _KN_BodyTexParams.z);
                {
                    // Sunset grade: map the art onto the atmosphere's red/orange ramp and flatten its contrast.
                    half grade = sunLow * _SunsetGrade;
                    half luma = dot(sunArt.rgb, half3(0.299, 0.587, 0.114));
                    sunArt.rgb = lerp(sunArt.rgb, SunsetRamp(luma), grade);
                    sunArt.rgb = lerp(0.5.xxx, sunArt.rgb, 1.0 - 0.35 * grade);
                }
                half4 sunLook = StylizeBody(sunArt, sunUV, _KN_SunDir.y, _KN_SunColor.rgb, _KN_BodyTexBrightness.x,
                                            _SunGlare, _SunGlareRadius, _SunGlareHDR, sky);
                {
                    // Spikes are barely there: washed toward the glow, low opacity, slow uneven flicker.
                    half3 tint = LightTint(_KN_SunColor.rgb);
                    half flicker = 0.75 + 0.25 * sin(_Time.y * 1.7 + shimmerNoise.z * 6.2831);
                    sunLook.rgb = lerp(sunLook.rgb, tint * _KN_BodyTexBrightness.x * 1.2, rayMask * 0.6);
                    sunLook.a *= lerp(1.0, _SunRayVisibility * flicker, rayMask);
                }
                sunLook = DiffuseBody(sunLook, sunR, LightTint(_KN_SunColor.rgb), _KN_BodyTexBrightness.x, diffusion);
                half sunArtAlpha = sunLook.a * sunInside * _KN_BodyTexParams.z * aboveHorizon * saturate(_KN_BodyTexBrightness.x * 4.0);
                sky = lerp(sky, sunLook.rgb, sunArtAlpha);

                // --- compose clouds
                sky = lerp(sky, _KN_CloudLit.rgb, cirrus * 0.55 * (1.0 - cloudAlpha));
                sky = lerp(sky, cloudColor, cloudAlpha);

                // Sun glow scatters through thin cloud, mostly blocked by thick cloud.
                // Heat shimmer also bends the glow (the disc core stays solid): wobble the direction a little.
                float3 glowDir = d;
                {
                    float3 right = normalize(cross(float3(0, 1, 0), _KN_SunDir.xyz) + float3(1e-4, 0, 0));
                    float3 upv = cross(_KN_SunDir.xyz, right);
                    half coreMask = smoothstep(0.35, 0.7, sunR);
                    float2 wob = (shimmerNoise.xy - 0.5) * shimmerAmt * _SunShimmerGlow * coreMask * _KN_BodyTexParams.x * 2.0;
                    glowDir = normalize(d + right * wob.x + upv * wob.y);
                }
                half glow = pow(saturate(dot(glowDir, _KN_SunDir.xyz)), _KN_SunParams.y) * _KN_SunParams.z;
                sky += _KN_SunColor.rgb * glow * (1.0 - cloudAlpha * 0.7);
                // Soft moonlight halo, scattered the same way.
                // The glow is light off the lit face: kept off the unlit part of the disc so the shadow stays dark.
                half moonHalo = pow(saturate(moonDot), _MoonHaloPower) * _MoonHalo * _KN_MoonPhase.y;
                moonHalo *= lerp(1.0, moonLit, moonOnDisc);
                sky += _KN_MoonColor.rgb * moonHalo * aboveHorizon * (1.0 - cloudAlpha * 0.7);
                // Under overcast the body is only a pale smear of light glowing through the cloud deck.
                half deckGlow = pow(saturate(sunDot), 300.0) * diffusion * cloudAlpha;
                sky += LightTint(_KN_SunColor.rgb) * _KN_BodyTexBrightness.x * deckGlow * 0.5 * aboveHorizon;

                // Rare 22° halos (presenter gates: thin high cloud, no rain, day hash). Shows through cirrus, not thick cloud.
                {
                    half haloScale, moonHaloScale;
                    half haloMask = aboveHorizon * (1.0 - cloudAlpha * 0.8);
                    half3 sunRing = Halo(d, _KN_SunDir.xyz, _KN_Halo.x * _HaloIntensity, haloScale);
                    half3 moonRing = Halo(d, _KN_MoonDir.xyz, _KN_Halo.y * _HaloIntensity * _HaloMoon * _KN_MoonPhase.y, moonHaloScale);
                    sky *= lerp(1.0, haloScale * moonHaloScale, haloMask);
                    half3 ringLight = sunRing * LightTint(_KN_SunColor.rgb) * max(_KN_BodyTexBrightness.x, 0.25)
                                    + moonRing * LightTint(_KN_MoonColor.rgb) * 0.6;
                    // Soft-add (screen-like): fades as the sky behind gets bright, so it never clips to white.
                    sky += ringLight * haloMask * saturate(1.0 - dot(sky, half3(0.299, 0.587, 0.114)) * 0.6);
                }

                // Rainbow after rain (strength already gated by the presenter: moisture, clearing sky, low sun).
                half rainbowScale;
                half3 bow = Rainbow(d, _KN_SunDir.xyz, _KN_Rainbow.x * _RainbowIntensity, rainbowScale);
                half bowMask = smoothstep(0.0, 0.06, d.y) * (1.0 - cloudAlpha * 0.6);
                sky *= lerp(1.0, rainbowScale, bowMask);
                sky += bow * bowMask * (0.35 + dot(sky, half3(0.299, 0.587, 0.114)));
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
