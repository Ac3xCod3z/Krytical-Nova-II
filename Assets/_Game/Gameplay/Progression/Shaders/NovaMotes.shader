// The sparks in orbit round a Nova (NovaHalo). One mesh holds every spark as a tiny quad; this shader puts each on
// its own tilted circle round the star, moves it along, and turns its quad to the camera. No particle system: the
// motion is a function of time, so every machine shows the same thing and nothing has to be simulated.
// Per vertex: uv0 = the corner of the quad (-1..1); uv1 = (place on the circle 0..1, radius as a share of the
// Nova's, turns a second, size); uv2 = (tilt of the circle about X, about Z, in radians; the spark's number; twinkle seed).
Shader "KryticalNova/NovaMotes"
{
    Properties
    {
        [HDR] _Color ("Colour", Color) = (1, 0.95, 0.8, 1)
        _Brightness ("Brightness", Float) = 2
        _Radius ("Nova radius (metres)", Float) = 0.2
        _Count ("Sparks shown", Float) = 12
        _Size ("Spark size (metres)", Float) = 0.012
        _Flash ("Flash (added brightness, set by code)", Float) = 0
        _MinPixels ("Smallest a spark is ever drawn (pixels)", Range(0, 6)) = 2.5
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+18" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Motes"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Brightness;
                half _Radius;
                half _Count;
                half _Size;
                half _Flash;
                half _MinPixels;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 corner : TEXCOORD0;
                float4 orbit : TEXCOORD1;
                float4 extra : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 corner : TEXCOORD0;
                half light : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                // Where on its circle the spark is now.
                float angle = (input.orbit.x + _Time.y * input.orbit.z) * 6.2831853;
                float3 onCircle = float3(cos(angle), 0.0, sin(angle)) * input.orbit.y * _Radius;
                // Lean the circle: about X, then about Z.
                float sx, cx, sz, cz;
                sincos(input.extra.x, sx, cx);
                sincos(input.extra.y, sz, cz);
                onCircle = float3(onCircle.x, onCircle.y * cx - onCircle.z * sx, onCircle.y * sx + onCircle.z * cx);
                onCircle = float3(onCircle.x * cz - onCircle.y * sz, onCircle.x * sz + onCircle.y * cz, onCircle.z);

                // Sparks beyond the count are folded away to nothing.
                float shown = step(input.extra.z + 0.5, _Count);
                float twinkle = 0.55 + 0.45 * sin(_Time.y * (2.0 + input.extra.w * 4.0) + input.extra.w * 30.0);
                float size = _Size * input.orbit.w * shown * (0.7 + 0.3 * twinkle);

                float3 centreWS = TransformObjectToWorld(onCircle);
                // Never smaller than a couple of pixels, or the sparks are lost at play distance.
                float depth = max(0.01, -TransformWorldToView(centreWS).z);
                float pixel = depth * 2.0 / (abs(UNITY_MATRIX_P[1][1]) * _ScreenParams.y);
                size = max(size, _MinPixels * 0.5 * pixel * shown);
                float3 right = UNITY_MATRIX_V[0].xyz;
                float3 up = UNITY_MATRIX_V[1].xyz;
                float3 positionWS = centreWS + (right * input.corner.x + up * input.corner.y) * size;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.corner = input.corner;
                output.light = twinkle * shown;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // A soft dot with a four-pointed glint.
                float r = length(input.corner);
                float disc = saturate(1.0 - r);
                disc = disc * disc * 1.4;
                float glint = saturate(1.0 - abs(input.corner.x) * 9.0) * saturate(1.0 - abs(input.corner.y))
                            + saturate(1.0 - abs(input.corner.y) * 9.0) * saturate(1.0 - abs(input.corner.x));
                float light = (disc + glint * 0.5) * input.light * (_Brightness + _Flash);
                return half4(_Color.rgb * light, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
