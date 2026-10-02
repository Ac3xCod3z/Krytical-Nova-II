// Full-screen comic pass: Sobel outlines from depth + normals, comic-styled cast
// shadows (read straight from the main light shadow map, so it works on terrain and
// any material), and optional luminance-driven Ben-Day dots.
// Driven by ComicStyleFeature (URP renderer feature).
Shader "Hidden/KryticalNova/ComicPost"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off Blend Off

        Pass
        {
            Name "ComicPost"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            // Always evaluate cascaded main-light shadows. Soft filtering (used when the
            // light's shadows are Soft) gives a smooth sample that ShadowAt() then
            // re-thresholds into a crisp, anti-aliased comic edge without stair-steps.
            #define _MAIN_LIGHT_SHADOWS_CASCADE
            #define _SHADOWS_SOFT
            #define _SHADOWS_SOFT_HIGH

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"
            #include "ComicHalftone.hlsl"
            #include "Assets/_Game/World/Sky/Shaders/SkyCommon.hlsl"

            // Outlines
            half4  _OutlineColor;
            float  _OutlineEnabled;
            float  _OutlineThickness;       // pixels, up close
            float  _OutlineOpacity;
            float  _DepthThreshold;         // relative depth change that counts as an edge
            float  _NormalThreshold;        // normal change that counts as an edge
            float  _GrazingAngleBoost;      // raises the depth threshold on surfaces seen edge-on
            float4 _OutlineDistance;        // x = thin start, y = thin end, z = min thickness px, w = far opacity
            float2 _CreaseFade;             // interior (normal) lines fade out between x and y
            float2 _OutlineFade;            // all lines fade out between x and y

            // Comic cast shadows
            float  _ShadowEnabled;
            half4  _ShadowColor;            // multiplied into shadowed pixels
            float  _ShadowOpacity;
            float  _ShadowEdgeSoftness;     // 0 = razor edge, 0.5 = original soft falloff
            float  _ShadowPattern;          // 0 = solid, 1 = dots, 2 = hatching
            float  _ShadowPatternCoverage;
            float  _ShadowPatternOpacity;
            float  _ShadowHatchSpacing;
            float  _ShadowHatchAngle;
            half4  _ShadowOutlineColor;
            float  _ShadowOutlineThickness; // 0 = off

            // Post dots
            float  _PostHalftoneEnabled;
            float  _PostHalftoneThreshold;  // luminance below which dots start appearing

            bool IsSky(float rawDepth)
            {
            #if UNITY_REVERSED_Z
                return rawDepth <= 1e-6;
            #else
                return rawDepth >= 1.0 - 1e-6;
            #endif
            }

            float3 WorldPosAt(float2 uv)
            {
                return ComputeWorldSpacePosition(uv, SampleSceneDepth(uv), UNITY_MATRIX_I_VP);
            }

            // 1 = lit, 0 = in the main light's cast shadow.
            half ShadowAt(float3 posWS)
            {
                half atten = MainLightRealtimeShadow(TransformWorldToShadowCoord(posWS));
                atten = smoothstep(0.5 - _ShadowEdgeSoftness, 0.5 + _ShadowEdgeSoftness, atten);
                return lerp(atten, 1.0, GetMainLightShadowFade(posWS));
            }

            half3 ApplyComicShadow(half3 color, float2 uv, float3 posWS, float2 screenPx)
            {
                half shadow = 1.0 - ShadowAt(posWS);

                // Ink line traced around the shadow silhouette.
                half rim = 0.0;
                if (_ShadowOutlineThickness > 0.0)
                {
                    float2 o = _BlitTexture_TexelSize.xy * _ShadowOutlineThickness;
                    half s0 = 1.0 - ShadowAt(WorldPosAt(uv + float2(o.x, 0)));
                    half s1 = 1.0 - ShadowAt(WorldPosAt(uv - float2(o.x, 0)));
                    half s2 = 1.0 - ShadowAt(WorldPosAt(uv + float2(0, o.y)));
                    half s3 = 1.0 - ShadowAt(WorldPosAt(uv - float2(0, o.y)));
                    half maxS = max(max(s0, s1), max(s2, s3));
                    half minS = min(min(s0, s1), min(s2, s3));
                    rim = saturate((maxS - minS) * 2.0) * shadow;
                }

                // Soft cloud shadows: one sample, no ink rim or dots, own strength (not faded with
                // cast-shadow opacity). Shares the sky's cloud field so they line up with the sky.
                half cloud = 0.0;
                UNITY_BRANCH
                if (_KN_CloudShadow.x > 0.0)
                    cloud = KN_CloudShadowAt(posWS) * (1.0 - shadow);

                if (shadow <= 0.001 && cloud <= 0.001)
                    return color;

                half3 shaded = lerp(color, color * _ShadowColor.rgb, _ShadowOpacity);

                half pattern = 0.0;
                if (_ShadowPattern > 1.5)
                    pattern = ComicHatch(screenPx, _ShadowHatchSpacing, _ShadowHatchAngle, _ShadowPatternCoverage);
                else if (_ShadowPattern > 0.5)
                    pattern = ComicHalftone(screenPx, _ShadowPatternCoverage);
                shaded = lerp(shaded, shaded * _ComicInkColor.rgb, pattern * _ShadowPatternOpacity);

                color = lerp(color, shaded, shadow);
                color = lerp(color, color * _ShadowColor.rgb, cloud);
                return lerp(color, _ShadowOutlineColor.rgb, rim * _ShadowOutlineColor.a);
            }

            // Returns edge strength; `thicknessPx` already distance-adjusted.
            half ComputeOutline(float2 uv, float rawDepth, float centerDepth, float thicknessPx)
            {
                float2 texel = _BlitTexture_TexelSize.xy * thicknessPx;

                // Sobel kernels
                const float2 offsets[8] = {
                    float2(-1,  1), float2(0,  1), float2(1,  1),
                    float2(-1,  0),                float2(1,  0),
                    float2(-1, -1), float2(0, -1), float2(1, -1)
                };
                const float kx[8] = { -1, 0, 1, -2, 2, -1, 0, 1 };
                const float ky[8] = {  1, 2, 1,  0, 0, -1, -2, -1 };

                float  gxD = 0, gyD = 0;
                float3 gxN = 0, gyN = 0;

                [unroll]
                for (int i = 0; i < 8; i++)
                {
                    float2 suv = uv + offsets[i] * texel;
                    float  d = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                    float3 n = SampleSceneNormals(suv);
                    gxD += d * kx[i]; gyD += d * ky[i];
                    gxN += n * kx[i]; gyN += n * ky[i];
                }

                float3 centerNormal = SampleSceneNormals(uv);

                // Surfaces seen nearly edge-on have large natural depth gradients;
                // raise the threshold there so floors/walls don't fill with ink.
                float3 posWS = ComputeWorldSpacePosition(uv, rawDepth, UNITY_MATRIX_I_VP);
                float3 viewDir = normalize(_WorldSpaceCameraPos - posWS);
                float grazing = 1.0 - saturate(dot(centerNormal, viewDir));
                float depthThreshold = _DepthThreshold * (1.0 + grazing * grazing * _GrazingAngleBoost);

                float depthEdge = sqrt(gxD * gxD + gyD * gyD) / max(centerDepth, 1e-3);
                depthEdge = smoothstep(depthThreshold, depthThreshold * 1.5, depthEdge);

                float normalEdge = sqrt(dot(gxN, gxN) + dot(gyN, gyN));
                normalEdge = smoothstep(_NormalThreshold, _NormalThreshold * 1.5, normalEdge);

                // Interior crease lines thin out with distance so small/far characters
                // keep a clean silhouette instead of turning into a mass of ink.
                float creaseFade = 1.0 - smoothstep(_CreaseFade.x, max(_CreaseFade.y, _CreaseFade.x + 0.01), centerDepth);
                normalEdge *= creaseFade;

                return saturate(max(depthEdge, normalEdge));
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                float2 screenPx = uv * _ScreenParams.xy;
                half4 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);

                float rawDepth = SampleSceneDepth(uv);
                if (IsSky(rawDepth))
                    return color;

                float3 posWS = ComputeWorldSpacePosition(uv, rawDepth, UNITY_MATRIX_I_VP);
                float centerDepth = LinearEyeDepth(rawDepth, _ZBufferParams);

                if (_ShadowEnabled > 0.5)
                    color.rgb = ApplyComicShadow(color.rgb, uv, posWS, screenPx);

                if (_PostHalftoneEnabled > 0.5)
                {
                    half lum = saturate(Luminance(color.rgb));
                    half coverage = saturate((_PostHalftoneThreshold - lum) / max(_PostHalftoneThreshold, 1e-3));
                    color.rgb = ComicApplyDots(color.rgb, screenPx, coverage);
                }

                if (_OutlineEnabled > 0.5)
                {
                    // Thickness eases from full size to a thin minimum with distance.
                    float t = smoothstep(_OutlineDistance.x, max(_OutlineDistance.y, _OutlineDistance.x + 0.01), centerDepth);
                    float thickness = lerp(_OutlineThickness, min(_OutlineDistance.z, _OutlineThickness), t);

                    half edge = ComputeOutline(uv, rawDepth, centerDepth, thickness);

                    float fade = 1.0 - smoothstep(_OutlineFade.x, max(_OutlineFade.y, _OutlineFade.x + 0.01), centerDepth);
                    half opacity = _OutlineOpacity * lerp(1.0, _OutlineDistance.w, t) * fade;
                    color.rgb = lerp(color.rgb, _OutlineColor.rgb, edge * opacity * _OutlineColor.a);
                }

                return color;
            }
            ENDHLSL
        }
    }
}
