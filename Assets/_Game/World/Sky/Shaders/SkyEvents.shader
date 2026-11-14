// A Unique Sky (owner's NIGHT list item 24): the sky's events on a far overlay dome drawn by SkyEventPresenter over the custom sky,
// for the game camera only. The custom sky (StylizedSky.shader) is untouched; this pass only adds on top of it:
//   star map (stars only: owner 14 Nov, no constellation lines), one of three nebulas (Soul Tide, Ember Rose, Veil Ribbon), shooting stars, The Pale Wanderer comet, the aurora (additive),
//   the amber second sun (the custom sun's own art via the copied SkyBodyCommon.hlsl; colour only, it lights nothing),
//   the eclipse: deep-blue wash, the moon disc sliding over the sun, the pearl corona at totality.
// Cost: one star-map sample, one cloud sample, one sun-art sample; the rest are uniform-gated branches (no new keywords).
Shader "Krytical Nova/Sky Events"
{
    Properties
    {
        // Copied at run time from the sky material, so the second sun is styled exactly like the custom sun.
        [HideInInspector] _BodySaturation("Body Saturation", Float) = 1
        [HideInInspector] _BodyContrast("Body Contrast", Float) = 1
        [HideInInspector] _BodyInkLift("Body Ink Lift", Float) = 0
        [HideInInspector] _BodyHaze("Body Haze", Float) = 0
        [HideInInspector] _BodyHorizonHaze("Body Horizon Haze", Float) = 0.5
        [HideInInspector] _BodyHazeHeight("Body Haze Height", Float) = 0.15
        [HideInInspector] _SunGlare("Sun Glare", Float) = 0.5
        [HideInInspector] _SunGlareRadius("Sun Glare Radius", Float) = 0.5
        [HideInInspector] _SunGlareHDR("Sun Glare HDR", Float) = 1
        [HideInInspector] _CloudHorizonFade("Cloud Horizon Fade", Float) = 0.1
        [HideInInspector] _ToonEdge("Toon Edge", Float) = 0
        [HideInInspector] _HorizonFogHeight("Horizon Fog Height", Float) = 0.2
    }
    SubShader
    {
        // After the skybox (drawn after opaques) and before every other transparent.
        Tags { "Queue" = "Transparent-499" "RenderType" = "Transparent" "IgnoreProjector" = "True" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off
        ZTest LEqual
        Blend One OneMinusSrcAlpha

        Pass
        {
            Name "SkyEvents"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _BodySaturation;
                float _BodyContrast;
                float _BodyInkLift;
                float _BodyHaze;
                float _BodyHorizonHaze;
                float _BodyHazeHeight;
                float _SunGlare;
                float _SunGlareRadius;
                float _SunGlareHDR;
                float _CloudHorizonFade;
                float _ToonEdge;
                float _HorizonFogHeight;
            CBUFFER_END

            #include "SkyCommon.hlsl"      // read only: the cloud field, so stars sit behind clouds
            #include "SkyBodyCommon.hlsl"  // the custom sun's copied drawing code

            // the custom sky's globals, read only
            float4 _KN_SunDir;
            float4 _KN_SkyZenith;
            float4 _KN_SkyHorizon;
            float4 _KN_SkyParams;   // x horizon falloff, y star visibility, z lightning, w horizon fog
            float4 _KN_SunParams;   // x cos(sun disc size)

            // the sky events' globals (SkyEventPresenter)
            TEXTURE2D(_KN_StarMap);   SAMPLER(sampler_KN_StarMap);   // lat-long dome: rgb stars + band
            TEXTURE2D(_KN_NebulaTex); SAMPLER(sampler_KN_NebulaTex);
            float4 _KN_StarParams;    // x map on, y brightness
            float4 _KN_StarBasisX, _KN_StarBasisY, _KN_StarBasisZ;
            float4 _KN_Nebula;        // xyz direction, w amount
            float4 _KN_NebulaParams;  // x tan(half size)
            float4 _KN_MeteorA[4];    // xyz start, w progress 0-1
            float4 _KN_MeteorB[4];    // xyz end, w brightness (0 = slot empty)
            float4 _KN_StreakParams;  // x 1/(2 sigma^2) of the trail, y of the head (radians; widths in SkyEventSettings)
            float4 _KN_Comet;         // xyz head, w amount
            float4 _KN_CometParams;   // x tail length (rad)
            float4 _KN_Sun2;          // xyz direction, w amount
            float4 _KN_Sun2Color;     // rgb colour, w tan(radius)
            float4 _KN_Sun2Params;    // x brightness
            float4 _KN_Eclipse;       // x cover 0-1, y corona 0-1, z darkness at totality, w moon disc size (share of the sun art)
            float4 _KN_CoronaColor;
            float4 _KN_EclipseSky;    // rgb deep-blue wash
            float4 _KN_Aurora;        // x amount
            float4 _KN_AuroraLow, _KN_AuroraHigh;

            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dirWS : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 dir = input.positionOS.xyz;
                o.positionCS = TransformWorldToHClip(GetCameraPositionWS() + dir);
                // on the far plane, like the skybox: anything in the world stands in front of it
                o.positionCS.z = UNITY_RAW_FAR_CLIP_VALUE * o.positionCS.w;
                o.dirWS = dir;
                return o;
            }

            half4 StarMapAt(float3 d)
            {
                float x = dot(d, _KN_StarBasisX.xyz), y = dot(d, _KN_StarBasisY.xyz), z = dot(d, _KN_StarBasisZ.xyz);
                float2 uv = float2(frac(atan2(y, x) * (0.5 / PI)), asin(clamp(z, -1.0, 1.0)) / PI + 0.5);
                return SAMPLE_TEXTURE2D_LOD(_KN_StarMap, sampler_KN_StarMap, uv, 0);
            }

            float2 Tangent(float3 d, float3 c, out float k)
            {
                float3 r = normalize(cross(float3(0, 1, 0), c) + float3(1e-4, 0, 0));
                float3 u = cross(c, r);
                k = dot(d, c);
                return float2(dot(d, r), dot(d, u)) / max(k, 1e-4);
            }

            half Streak(float3 d, float4 a, float4 b)
            {
                if (b.w <= 0.0) return 0.0;
                float p = a.w;
                float3 head = normalize(lerp(a.xyz, b.xyz, saturate(p * 1.25)));
                float3 tail = normalize(lerp(a.xyz, b.xyz, saturate(p * 1.25 - 0.5)));
                float3 seg = head - tail;
                float t = saturate(dot(d - tail, seg) / max(dot(seg, seg), 1e-8));
                float dist = length(d - (tail + seg * t));
                half streakLine = exp(-dist * dist * _KN_StreakParams.x) * t * t;
                float dh = length(d - head);
                half headGlow = exp(-dh * dh * _KN_StreakParams.y);
                return (streakLine + headGlow) * sin(PI * saturate(p)) * b.w;
            }

            // premultiplied layering: out = C + sky * (1 - A)
            void Over(inout half3 C, inout half A, half3 rgb, half a)
            {
                C = rgb * a + C * (1.0 - a);
                A = a + A * (1.0 - a);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float3 d = normalize(input.dirWS);
                half aboveHorizon = smoothstep(-0.02, 0.01, d.y);

                // the cloud field in front of the sky events (one sample)
                float cdist = _KN_CloudShape.x / max(d.y, 0.02);
                half dens = KN_CloudDensityLod(KN_CloudUV(GetCameraPositionWS().xz + d.xz * cdist), 0).x;
                dens = lerp(dens, smoothstep(0.12h, 0.5h, dens), _ToonEdge);
                half cloudA = saturate(dens * _KN_CloudParams.y * smoothstep(0.0, _CloudHorizonFade, d.y));
                half fogBand = 1.0 - saturate(max(d.y, 0.0) / max(_HorizonFogHeight, 1e-3));
                half gate = aboveHorizon * (1.0 - cloudA) * (1.0 - _KN_SkyParams.w * fogBand * fogBand);

                half3 C = 0.0;
                half A = 0.0;

                // ---- eclipse: the whole sky washes to deep blue (owner: "close to what night time is")
                half eclipse = _KN_Eclipse.x;
                [branch] if (eclipse > 0.0)
                    Over(C, A, _KN_EclipseSky.rgb, eclipse * _KN_Eclipse.z);

                // ---- night block: the star map, nebula, streaks, comet, aurora (they also show through an eclipse)
                half vis = max(_KN_SkyParams.y, eclipse * _KN_Eclipse.z * 0.7);
                [branch] if (vis > 0.001)
                {
                    half3 night = 0.0;
                    [branch] if (_KN_StarParams.x > 0.5)
                    {
                        half3 map = StarMapAt(d).rgb;
                        night += map.rgb * _KN_StarParams.y;
                    }
                    [branch] if (_KN_Nebula.w > 0.0)
                    {
                        float nk;
                        float2 np = Tangent(d, _KN_Nebula.xyz, nk) / _KN_NebulaParams.x * 0.5 + 0.5;
                        night += SAMPLE_TEXTURE2D_LOD(_KN_NebulaTex, sampler_KN_NebulaTex, np, 0).rgb * KN_InsideUnit(np) * step(0.0, nk) * _KN_Nebula.w;
                    }
                    half streaks = 0.0;
                    [unroll] for (int si = 0; si < 4; si++) streaks += Streak(d, _KN_MeteorA[si], _KN_MeteorB[si]);
                    night += streaks * half3(1.0, 0.96, 0.88) * 1.6;
                    [branch] if (_KN_Comet.w > 0.0)
                    {
                        // the tail points away from the sun
                        float3 ch = _KN_Comet.xyz; float3 away = -_KN_SunDir.xyz;
                        float3 tailDir = normalize(away - ch * dot(away, ch) + float3(0, 1e-4, 0)); float3 side = cross(ch, tailDir);
                        float ck = dot(d, ch); float cu = dot(d, tailDir) / max(ck, 1e-4), cv = dot(d, side) / max(ck, 1e-4); float cl = _KN_CometParams.x;
                        float w0 = 0.004 + max(cu, 0) * 0.12, w1 = 0.002 + max(cu, 0) * 0.03;
                        half dust = exp(-(cv / w0) * (cv / w0)) * exp(-max(cu, 0) / (cl * 0.35)) * step(0, cu);
                        float iv = (cv + cu * 0.05) / w1; half ion = exp(-iv * iv) * exp(-max(cu, 0) / (cl * 0.6)) * step(0, cu);
                        half chead = exp(-(cu * cu + cv * cv) / 9.0e-6);
                        night += (dust * half3(1, 0.94, 0.82) * 0.7 + ion * half3(0.5, 0.86, 1) * 0.5 + chead * 1.2) * step(0, ck) * _KN_Comet.w;
                    }
                    C += night * vis * gate;

                    [branch] if (_KN_Aurora.x > 0.0)
                    {
                        // a curtain over the pole quarter; seen through thin cloud only
                        float3 north = normalize(float3(_KN_StarBasisZ.x, 0, _KN_StarBasisZ.z) + float3(0, 0, 1e-4)); float3 eastv = cross(float3(0, 1, 0), north);
                        float az = atan2(dot(d, eastv), dot(d, north)); float el = asin(saturate(d.y)); half spread = exp(-az * az / 1.2);
                        float base = 0.30 + 0.06 * sin(az * 3 + _Time.y * 0.05) + 0.03 * sin(az * 7 - _Time.y * 0.08); float upk = (el - base) / 0.35;
                        half curtain = upk > 0 ? exp(-upk * 3.2) : exp(upk * 14);
                        half rays = SAMPLE_TEXTURE2D_LOD(_KN_CloudNoise, sampler_KN_CloudNoise, float2(az * 0.9 + _Time.y * 0.004, 0.37), 0).g; rays = rays * rays * 1.8 + 0.15;
                        C += lerp(_KN_AuroraLow.rgb, _KN_AuroraHigh.rgb, saturate(upk)) * curtain * rays * spread * _KN_Aurora.x * vis * aboveHorizon * (1.0 - cloudA * 0.6) * 0.9;
                    }
                }

                // ---- the second sun: the custom sun's art, amber, its own size. Colour on the sky only: it lights nothing.
                [branch] if (_KN_Sun2.w > 0.0 && _KN_BodyTexParams.z > 0.5)
                {
                    half s2Inside;
                    float2 s2uv = KN_BodyUV(d, _KN_Sun2.xyz, _KN_Sun2Color.w, _KN_BodyTexAspect.x, s2Inside);
                    s2Inside *= KN_InsideUnit(s2uv);
                    half4 art = SAMPLE_TEXTURE2D_LOD(_KN_SunTex, sampler_KN_SunTex, s2uv, 0);
                    half3 tint = KN_LightTint(_KN_Sun2Color.rgb);
                    half luma = dot(art.rgb, half3(0.299, 0.587, 0.114));
                    art.rgb = lerp(art.rgb, saturate(luma * 1.25) * tint, 0.8);
                    half3 skyBehind = lerp(_KN_SkyZenith.rgb, _KN_SkyHorizon.rgb, pow(1.0 - saturate(d.y), _KN_SkyParams.x));
                    half bright = _KN_Sun2Params.x;
                    half4 look = KN_StylizeBody(art, s2uv, _KN_Sun2.y, _KN_Sun2Color.rgb, bright, _SunGlare, _SunGlareRadius, _SunGlareHDR, skyBehind);
                    look = KN_DiffuseBody(look, length(s2uv - 0.5) * 2.0, tint, bright, _KN_BodyTexBrightness.z);
                    half a = saturate(look.a * s2Inside * aboveHorizon * (1.0 - cloudA) * _KN_Sun2.w);
                    Over(C, A, look.rgb, a);
                    half glow = pow(saturate(dot(d, _KN_Sun2.xyz)), 900.0) * 0.35;
                    C += tint * glow * bright * _KN_Sun2.w * aboveHorizon * (1.0 - cloudA * 0.7);
                }

                // ---- eclipse: the moon disc slides over the sun; the pearl corona flares at totality
                [branch] if (eclipse > 0.0)
                {
                    float sunTan = _KN_BodyTexParams.z > 0.5 ? _KN_BodyTexParams.x * _KN_Eclipse.w : tan(acos(_KN_SunParams.x));
                    float ek; float2 ep = Tangent(d, _KN_SunDir.xyz, ek);
                    float mr = sunTan * 1.02;
                    float2 mc = float2(1.0, 0.35) * (1.0 - eclipse) * 2.1 * mr;
                    half disc = saturate((mr - length(ep - mc)) / max(mr * 0.03, 1e-5) + 0.5) * step(0.0, ek);
                    Over(C, A, half3(0.012, 0.014, 0.022), disc * saturate(eclipse * 6.0) * aboveHorizon * (1.0 - cloudA));

                    float er = length(ep), ang = atan2(ep.y, ep.x);
                    half streamer = (0.55 + 0.45 * pow(sin(ang * 5.0 + 0.7), 2.0)) * (0.7 + 0.3 * sin(ang * 13.0 + 2.1));
                    half cor = exp(-max(er - mr, 0.0) / (mr * (0.25 + 0.9 * streamer * streamer * streamer))) * step(mr * 0.98, er) * step(0.0, ek);
                    C += _KN_CoronaColor.rgb * cor * _KN_Eclipse.y * 1.6 * aboveHorizon * (1.0 - cloudA * 0.8);
                }

                return half4(C, A);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
