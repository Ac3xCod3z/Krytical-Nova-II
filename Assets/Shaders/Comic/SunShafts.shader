// Screen-space sun shafts (god rays), driven by SunShaftsFeature.
// Pass 0 builds a quarter-res occlusion mask: open sky near the sun, thinned by the same cloud field the
//        skybox draws (SkyCommon), so buildings AND clouds cut the light.
// Pass 1/2 radially blur that mask toward the sun's screen position (a long step, then a short one).
//        The steps are baked per pass: material values set inside a render-graph pass are read when the
//        command buffer executes, so one shared _BlurStep would end up the same for every pass.
// Pass 3 adds the shafts over the camera colour, optionally posterized into comic bands.
Shader "Hidden/KryticalNova/SunShafts"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off Blend Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
        #include "../Sky/SkyCommon.hlsl"

        float4 _KN_SunDir;
        float4 _KN_SunColor;

        float4 _SunScreenPos;     // xy = sun viewport position (CPU, for culling/fade only), z = aspect (w/h)
        float4 _ShaftParams;      // x source tightness (power), y sample density, z decay, w weight
        float4 _ShaftComposite;   // x intensity, y comic bands (0 = smooth), z cloud occlusion strength
        TEXTURE2D(_ShaftTex);
        SAMPLER(sampler_ShaftTex);

        // Sun position in the same uv convention as ComputeWorldSpacePosition (handles the D3D flip), so the
        // blur marches toward the sun the mask was built around.
        float2 SunUV()
        {
            float3 sunWS = _WorldSpaceCameraPos + _KN_SunDir.xyz * 1000.0;
            return ComputeNormalizedDeviceCoordinates(sunWS, UNITY_MATRIX_VP);
        }

        #define SAMPLES 24
        // step = fraction of the distance to the sun covered by this blur pass.
        // normalize = true averages the samples (a smoothing pass that keeps the brightness).
        half RadialBlur(float2 uv, float stepScale, bool normalize)
        {
            float2 delta = (SunUV() - uv) * (stepScale * _ShaftParams.y / SAMPLES);
            half illumination = 0.0;
            half total = 0.0;
            half decay = 1.0;
            UNITY_UNROLL
            for (int i = 0; i < SAMPLES; i++)
            {
                illumination += SAMPLE_TEXTURE2D_LOD(_BlitTexture, sampler_LinearClamp, uv, 0).r * decay;
                total += decay;
                decay *= _ShaftParams.z;
                uv += delta;
            }
            return normalize ? illumination / max(total, 1e-3) : illumination * _ShaftParams.w;
        }

        bool IsSky(float rawDepth)
        {
        #if UNITY_REVERSED_Z
            return rawDepth <= 1e-6;
        #else
            return rawDepth >= 1.0 - 1e-6;
        #endif
        }
        ENDHLSL

        Pass
        {
            Name "ShaftMask"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float rawDepth = SampleSceneDepth(uv);
                if (!IsSky(rawDepth)) return 0;

                float3 farWS = ComputeWorldSpacePosition(uv, UNITY_RAW_FAR_CLIP_VALUE, UNITY_MATRIX_I_VP);
                float3 d = normalize(farWS - _WorldSpaceCameraPos);
                half source = pow(saturate(dot(d, _KN_SunDir.xyz)), _ShaftParams.x);

                // Same projection the skybox uses, so a cloud in front of the sun blocks its shafts.
                float dist = _KN_CloudShape.x / max(d.y, 0.02);
                half cloud = KN_CloudDensityLod(KN_CloudUV(_WorldSpaceCameraPos.xz + d.xz * dist), 1).x * _KN_CloudParams.y;
                half open = 1.0 - saturate(cloud * _ShaftComposite.z) * smoothstep(0.0, 0.05, d.y);
                return half4(source * open, 0, 0, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShaftBlurLong"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            half4 Frag(Varyings input) : SV_Target { return half4(RadialBlur(input.texcoord, 1.0, false), 0, 0, 1); }
            ENDHLSL
        }

        Pass
        {
            Name "ShaftBlurShort"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            half4 Frag(Varyings input) : SV_Target { return half4(RadialBlur(input.texcoord, 0.25, true), 0, 0, 1); }
            ENDHLSL
        }

        Pass
        {
            Name "ShaftComposite"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                half4 color = SAMPLE_TEXTURE2D_LOD(_BlitTexture, sampler_LinearClamp, uv, 0);
                half shafts = SAMPLE_TEXTURE2D_LOD(_ShaftTex, sampler_ShaftTex, uv, 0).r;
                half bands = _ShaftComposite.y;
                if (bands > 0.5)
                {
                    // Comic look: soft-edged steps instead of a smooth gradient.
                    half stepped = floor(shafts * bands) / bands;
                    shafts = lerp(stepped, shafts, 0.25);
                }
                half3 tint = _KN_SunColor.rgb / max(max(_KN_SunColor.r, max(_KN_SunColor.g, _KN_SunColor.b)), 1e-3);
                color.rgb += tint * shafts * _ShaftComposite.x;
                if (_ShaftComposite.w > 0.5) color.rgb = shafts.xxx; // debug: shafts only
                return color;
            }
            ENDHLSL
        }
    }
}
