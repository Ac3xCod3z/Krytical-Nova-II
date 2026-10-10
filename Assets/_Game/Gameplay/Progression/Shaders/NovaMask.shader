// What lets a Nova hide the characters, pets and effects behind it and nothing else (NovaHalo). A disc behind the
// Nova, facing the camera, drawn several times in the solid pass with nothing but depth and stencil, in this order:
//   scenery (already drawn)
//   1. "sky"    at the far plane: marks the pixels of the disc where nothing has been drawn, which will be sky;
//   2. "mask"   writes the disc's depth: whatever is drawn next and lies behind it is not drawn at all;
//   characters, pets, Hostiles (ActorDrawOrder puts them here)
//   3. "open"   where the disc is still what is seen and the pixel was marked as sky: marks it again;
//   4. "clear"  those pixels get the far plane's depth back, so the sky, which is drawn next, fills them;
//   5. "tidy"   takes the marks off;
//   sky
//   6. "again"  first thing in the blended pass: the disc's depth once more, so sparkles and other light behind
//               the Nova stay hidden against the sky too.
// It has no depth-only pass, so it is not in the depth texture: the ink, the fog and the ambient shading see
// straight through it. Each stage is a material made in code from the properties below.
// The disc does not end on a line: over the outer _Feather of its radius it is broken into dots on the screen that
// shrink to nothing at the rim, the same dots in every stage, so what it hides comes back into view through them
// instead of being cut off on a circle.
Shader "KryticalNova/NovaMask"
{
    Properties
    {
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("Depth test", Float) = 4
        [Toggle] _ZWrite ("Writes depth", Float) = 0
        [Toggle] _Far ("Drawn at the far plane", Float) = 0
        _StencilRef ("Stencil value", Float) = 0
        _StencilRead ("Stencil bits read", Float) = 0
        _StencilWrite ("Stencil bits written", Float) = 0
        [Enum(UnityEngine.Rendering.CompareFunction)] _StencilComp ("Stencil test", Float) = 8
        [Enum(UnityEngine.Rendering.StencilOp)] _StencilPass ("Stencil on pass", Float) = 0
        _Feather ("Share of the radius that is rim", Range(0, 0.9)) = 0.45
        _DotSize ("Rim dot size (pixels at 1080p)", Float) = 5
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Mask"
            Tags { "LightMode" = "UniversalForward" }
            ColorMask 0
            Cull Off
            ZTest [_ZTest]
            ZWrite [_ZWrite]
            Stencil
            {
                Ref [_StencilRef]
                ReadMask [_StencilRead]
                WriteMask [_StencilWrite]
                Comp [_StencilComp]
                Pass [_StencilPass]
            }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half _ZTest;
                half _ZWrite;
                half _Far;
                half _StencilRef;
                half _StencilRead;
                half _StencilWrite;
                half _StencilComp;
                half _StencilPass;
                half _Feather;
                half _DotSize;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 disc : TEXCOORD0; // the disc's own plane: 1 from the middle is its rim
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.disc = input.positionOS.xy;
                if (_Far > 0.5)
                {
                    #if UNITY_REVERSED_Z
                    output.positionCS.z = 0.0;
                    #else
                    output.positionCS.z = output.positionCS.w;
                    #endif
                }
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // 1 in the solid middle, 0 at the rim.
                float kept = 1.0 - smoothstep(1.0 - _Feather, 1.0, length(input.disc));
                if (_Feather > 0.001)
                {
                    // Dots on the screen's own grid: each cell is hidden out from its middle as far as `kept` says.
                    float cell = max(2.0, _DotSize * _ScreenParams.y / 1080.0);
                    float fromMiddle = length(frac(input.positionCS.xy / cell) - 0.5) / 0.7072;
                    clip(kept * 1.02 - fromMiddle);
                }
                return 0;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
