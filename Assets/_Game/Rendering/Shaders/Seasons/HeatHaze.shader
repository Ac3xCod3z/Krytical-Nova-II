// Summer heat haze at a distance (item 21 part 3), driven by HeatHazeFeature.
// Pass 0 (half res) builds a shimmer offset field: rising value noise, faded in by linear depth from _HazeParams.x
//        (near) over 1/_HazeParams.y metres, so nothing near the camera moves; the sky shimmers only just above the horizon.
// Pass 1 reads the camera colour through that field. A shifted read that would land on something near the camera is
//        dropped, so near objects never smear over the far ground.
// No keywords: no new variants. Never touches the sun or sky.
Shader "Hidden/KryticalNova/HeatHaze"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off Blend Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

        float4 _HazeParams;   // x near metres, y 1/(far-near), z amplitude (screen heights) x strength, w sky fade (view dir up share)
        float4 _HazeNoise;    // x cells across, y cells up, z rise speed
        float4 _HazeLocal;    // x local box count (0 = none), y amplitude x strength, z haze height (m)
        float4 _HazeBoxes[32];  // x, z centre, y ground height, w half size
        TEXTURE2D(_HazeOffsetTex);
        SAMPLER(sampler_HazeOffsetTex);

        float HazeHash(float2 p)
        {
            p = frac(p * float2(123.34, 456.21));
            p += dot(p, p + 45.32);
            return frac(p.x * p.y);
        }

        float HazeNoise(float2 p)
        {
            float2 i = floor(p), f = frac(p);
            float2 u = f * f * (3.0 - 2.0 * f);
            float a = HazeHash(i), b = HazeHash(i + float2(1, 0)), c = HazeHash(i + float2(0, 1)), d = HazeHash(i + float2(1, 1));
            return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
        }

        // 0 .. 1 inside the hot boxes (lava cracks), soft at the edges and fading up to the haze height
        float LocalMask(float2 uv, float rawDepth)
        {
            int count = (int)_HazeLocal.x;
            if (count <= 0) return 0.0;
            float3 ws = ComputeWorldSpacePosition(uv, rawDepth, UNITY_MATRIX_I_VP);
            float m = 0.0;
            for (int i = 0; i < count; i++)
            {
                float4 b = _HazeBoxes[i];
                float2 q = abs(ws.xz - b.xz) / max(b.w, 0.01);
                float side = 1.0 - smoothstep(0.7, 1.0, max(q.x, q.y));
                float h = (ws.y - b.y) / max(_HazeLocal.z, 0.01);
                m = max(m, side * (1.0 - smoothstep(0.6, 1.0, h)) * step(-0.5, h));
            }
            return m;
        }

        // 0 near the camera .. 1 from the far distance on; the low sky counts as far, the high sky not at all
        float HazeMask(float2 uv, float rawDepth)
        {
            #if UNITY_REVERSED_Z
            bool sky = rawDepth <= 0.00001;
            #else
            bool sky = rawDepth >= 0.99999;
            #endif
            if (sky)
            {
                float3 ws = ComputeWorldSpacePosition(uv, UNITY_RAW_FAR_CLIP_VALUE, UNITY_MATRIX_I_VP);
                float up = normalize(ws - _WorldSpaceCameraPos).y;
                return saturate(1.0 - up / max(_HazeParams.w, 0.001));
            }
            float eye = LinearEyeDepth(rawDepth, _ZBufferParams);
            return smoothstep(0.0, 1.0, saturate((eye - _HazeParams.x) * _HazeParams.y));
        }
        ENDHLSL

        Pass
        {
            Name "HazeOffsets"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            float4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float rawDepth = SampleSceneDepth(uv);
                float mask = _HazeParams.z > 0.0 ? HazeMask(uv, rawDepth) : 0.0;
                float local = LocalMask(uv, rawDepth);
                if (mask <= 0.0 && local <= 0.0) return float4(0, 0, 0, 0);
                float t = _Time.y * _HazeNoise.z;
                float2 p = uv * _HazeNoise.xy;
                // two layers rising at different speeds read as heat cells boiling up
                float n1 = HazeNoise(p + float2(0.0, -t)) - 0.5;
                float n2 = HazeNoise(p * 1.7 + float2(17.3, -t * 1.4)) - 0.5;
                float2 offset = float2(n1 * 0.6, n2) * (_HazeParams.z * mask + _HazeLocal.y * local);
                offset.x *= _ScreenParams.y / _ScreenParams.x;   // screen heights -> uv on both axes
                return float4(offset, 0, 0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "HazeComposite"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float2 offset = SAMPLE_TEXTURE2D(_HazeOffsetTex, sampler_HazeOffsetTex, uv).xy;
                float2 moved = uv + offset;
                // a read that lands on something near the camera stays put (no smear of near edges over the far ground)
                float movedDepth = SampleSceneDepth(moved);
                float keep = max(_HazeParams.z > 0.0 ? HazeMask(moved, movedDepth) : 0.0, LocalMask(moved, movedDepth));
                moved = lerp(uv, moved, step(0.001, keep));
                return SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, moved);
            }
            ENDHLSL
        }
    }
}
