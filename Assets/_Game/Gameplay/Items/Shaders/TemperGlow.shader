// The glow a tempered weapon wears (WeaponVisualMount), and an evolved pet (PetFollower). Drawn on shells: copies
// of the model's mesh over the model's own material, so it works on any model whatever shader that uses.
//
// The glow is a rim of light AROUND the model, never a tint ON it. These are flat-shaded, low-poly meshes: a rim
// worked out from the surface normal lights whole faces at once and the model ends up painted in the glow colour.
// So the rim is drawn as a hull instead: the back faces of the mesh pushed outward. The model itself hides the
// hull everywhere but outside its silhouette, which leaves a band of light hugging the outline.
//
// One material is one such band. GlowRim (C#) stacks a few, each wider and fainter than the one before, so the
// light falls away from the model like something it gives off, and does not depend on the camera's bloom to do so.
// On top: a slow breath, and for the brightest looks a scatter of small sparkles on the surface. The colour is the
// Temper band's (or the pet stage's); the strength is the rarity's (TemperSettings). HDR, so above 1 it blooms too.
Shader "KryticalNova/TemperGlow"
{
    Properties
    {
        [HDR] _GlowColor ("Colour", Color) = (1, 0.86, 0.24, 1)
        _Intensity ("Intensity", Float) = 1
        _RimPower ("Rim power (higher = thinner rim)", Range(0.5, 8)) = 3
        _Width ("Rim width at rim power 3 (metres)", Range(0.001, 0.08)) = 0.012
        _MinPixels ("Least rim width on screen (pixels)", Range(0, 6)) = 1.5
        _Widen ("This layer: width x", Float) = 1
        _Dim ("This layer: brightness x", Range(0, 1)) = 1
        _Pulse ("Pulse", Range(0, 1)) = 0.15
        _PulseSpeed ("Pulse speed", Float) = 0.6
        _Sparkle ("Sparkle", Range(0, 4)) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+10" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _GlowColor;
            half _Intensity;
            half _RimPower;
            half _Width;
            half _MinPixels;
            half _Widen;
            half _Dim;
            half _Pulse;
            half _PulseSpeed;
            half _Sparkle;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        half Breath()
        {
            return 1.0 + _Pulse * sin(_Time.y * _PulseSpeed * 6.2831853);
        }
        ENDHLSL

        // The band of light. Every triangle that faces away from the camera is thickened into a little prism: the
        // triangle pushed outward, joined back to where it was by a skirt along each edge. Neighbouring faces of a
        // hard-edged mesh move apart when pushed along their own normals; the skirts close those gaps, so the band is
        // unbroken on any mesh with no smoothed normals baked for it. (A geometry stage: Direct3D and Vulkan.)
        // The stencil bit keeps a pixel to one layer however many prisms overlap there, and keeps a wider layer out
        // from under the narrower ones drawn before it (GlowRim orders them by queue).
        Pass
        {
            Name "Rim"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Off
            Stencil { Ref 128 ReadMask 128 WriteMask 128 Comp NotEqual Pass Replace }

            HLSLPROGRAM
            #pragma require geometry
            #pragma vertex vert
            #pragma geometry geom
            #pragma fragment frag

            struct ToGeometry
            {
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            ToGeometry vert(Attributes input)
            {
                ToGeometry output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            // A vertex pushed out along its normal by the band's width. Far from the camera a centimetre is less
            // than a pixel and the rim would vanish, so the push is never smaller than a set width on screen.
            float4 Pushed(ToGeometry v, float4 baseCS)
            {
                float width = _Width * (3.0 / max(_RimPower, 0.5)) * _Widen;
                float4 pushedCS = TransformWorldToHClip(v.positionWS + v.normalWS * width);
                float2 basePoint = baseCS.xy / baseCS.w;
                float2 offset = pushedCS.xy / pushedCS.w - basePoint;
                float pixels = length(offset * _ScreenParams.xy * 0.5);
                float least = _MinPixels * _Widen;
                if (pixels < least && pixels > 1e-4) pushedCS.xy = (basePoint + offset * (least / pixels)) * pushedCS.w;
                return pushedCS;
            }

            void Emit(inout TriangleStream<Varyings> stream, float4 positionCS)
            {
                Varyings output;
                output.positionCS = positionCS;
                stream.Append(output);
            }

            [maxvertexcount(15)]
            void geom(triangle ToGeometry input[3], inout TriangleStream<Varyings> stream)
            {
                // Only what faces away from the camera: the model hides it everywhere but past its own outline.
                float3 centre = (input[0].positionWS + input[1].positionWS + input[2].positionWS) / 3.0;
                float3 facing = normalize(input[0].normalWS + input[1].normalWS + input[2].normalWS);
                if (dot(facing, GetWorldSpaceNormalizeViewDir(centre)) > 0.03) return;

                float4 at[3];
                float4 pushed[3];
                for (int i = 0; i < 3; i++)
                {
                    at[i] = TransformWorldToHClip(input[i].positionWS);
                    pushed[i] = Pushed(input[i], at[i]);
                }

                Emit(stream, pushed[0]);
                Emit(stream, pushed[1]);
                Emit(stream, pushed[2]);
                stream.RestartStrip();
                for (int e = 0; e < 3; e++)
                {
                    int next = e == 2 ? 0 : e + 1;
                    Emit(stream, at[e]);
                    Emit(stream, at[next]);
                    Emit(stream, pushed[e]);
                    Emit(stream, pushed[next]);
                    stream.RestartStrip();
                }
            }

            half4 frag(Varyings input) : SV_Target
            {
                return half4(_GlowColor.rgb * _Intensity * _Dim * Breath(), 1.0);
            }
            ENDHLSL
        }

        // Sparkles, for the brightest looks: small cells of the model's own space that flash for a moment each, so
        // they ride with it. Nothing else is drawn on the surface.
        Pass
        {
            Name "Sparkles"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Back
            Offset -1, -1

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionOS : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.positionOS = input.positionOS.xyz;
                return output;
            }

            float Hash(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            half4 frag(Varyings input) : SV_Target
            {
                clip(_Sparkle - 0.001);
                float tick = floor(_Time.y * 6.0);
                float cell = Hash(floor(input.positionOS * 70.0) + tick);
                half sparkle = step(0.975, cell) * _Sparkle;
                return half4(_GlowColor.rgb * _Intensity * sparkle, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
