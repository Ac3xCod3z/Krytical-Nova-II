// Colour-blind correction (owner, 24 Oct 2026; Settings ▸ Accessibility). Daltonization: the frame is seen as a
// player with the chosen colour deficiency would see it (Machado et al. 2009, full severity), and the colour
// difference they would lose is moved into the channels they can tell apart (Fidaner et al.). _Strength blends
// between the frame as made (0) and fully corrected (1). Driven by ColourBlindFeature after post-processing.
Shader "Hidden/KryticalNova/ColourBlindCorrection"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off Blend Off

        Pass
        {
            Name "ColourBlindCorrection"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            // 1 protanopia, 2 deuteranopia, 3 tritanopia
            float _Mode;
            float _Strength;

            static const float3x3 Protan = float3x3(
                 0.152286,  1.052583, -0.204868,
                 0.114503,  0.786281,  0.099216,
                -0.003882, -0.048116,  1.051998);
            static const float3x3 Deutan = float3x3(
                 0.367322,  0.860646, -0.227968,
                 0.280085,  0.672501,  0.047413,
                -0.011820,  0.042940,  0.968881);
            static const float3x3 Tritan = float3x3(
                 1.255528, -0.076749, -0.178779,
                -0.078411,  0.930809,  0.147602,
                 0.004733,  0.691367,  0.303900);

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                half4 colour = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                float3 c = colour.rgb;

                float3x3 m = _Mode < 1.5 ? Protan : (_Mode < 2.5 ? Deutan : Tritan);
                float3 seen = mul(m, c);
                float3 lost = c - seen;

                float3 shift = _Mode < 2.5
                    // Red-green: the red difference is shown as green and blue.
                    ? float3(0.0, 0.7 * lost.r + lost.g, 0.7 * lost.r + lost.b)
                    // Blue-yellow: the blue difference is shown as red and green.
                    : float3(lost.r + 0.7 * lost.b, lost.g + 0.7 * lost.b, 0.0);

                colour.rgb = max(0.0, c + shift * _Strength);
                return colour;
            }
            ENDHLSL
        }
    }
}
