// Concord 040 thruster plume (owner, 9 Nov 2026). Unlit, additive, no shadows, no depth/opaque texture, no refraction.
// UV time math only (no CPU per frame); one material per look, SRP-batched and GPU-instanced.
//   _HIGH   : close look from the platform edge: second noise layer, brighter core, flicker.   (2 texture samples)
//   (none)  : the plume cone with one noise sample.                                              (1 texture sample)
//   _CARD   : far look: a camera-facing card (billboard around Y done in the vertex shader), one noise sample.
// Mesh UVs: x around / across, y 0 at the nozzle to 1 at the tip. Vertex colour r = 1 on the bright inner cone.
Shader "KryticalNova/Thruster"
{
    Properties
    {
        [HDR] _CoreColor ("Core colour", Color) = (1.3, 1.25, 1.1, 1)
        [HDR] _EdgeColor ("Edge colour", Color) = (0.0, 0.9, 2.2, 1)
        _NoiseTex ("Noise (R, tiling)", 2D) = "white" {}
        _Intensity ("Intensity", Float) = 1.4
        _Speed ("Scroll speed", Float) = 1.6
        _NoiseScale ("Noise scale", Vector) = (2, 1.5, 0, 0)
        [Toggle(_HIGH)] _High ("High look", Float) = 0
        [Toggle(_CARD)] _Card ("Card (billboard)", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Plume"
            Blend One One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma shader_feature_local _HIGH
            #pragma shader_feature_local _CARD
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_NoiseTex); SAMPLER(sampler_NoiseTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _NoiseTex_ST;
                half4 _CoreColor;
                half4 _EdgeColor;
                half _Intensity;
                half _Speed;
                float4 _NoiseScale;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; half4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half edge : TEXCOORD1; half inner : TEXCOORD2; float seed : TEXCOORD3; };

            Varyings vert(Attributes IN)
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                Varyings OUT;
                float3 posWS;
                half edge;
#if defined(_CARD)
                float3 centre = TransformObjectToWorld(float3(0, 0, 0));
                float3 dirV = normalize(_WorldSpaceCameraPos - centre + float3(0, 1e-3, 0));
                // hangs straight down seen from the side, turns to face the camera when seen from far below (a plume seen end-on is a glow)
                float steep = saturate(abs(dirV.y) * 1.8);
                float3 up = normalize(lerp(float3(0, 1, 0), UNITY_MATRIX_V[1].xyz, steep));
                float3 right = normalize(cross(up, dirV));
                float sx = length(UNITY_MATRIX_M._m00_m10_m20), sy = length(UNITY_MATRIX_M._m01_m11_m21);
                posWS = centre + right * (IN.positionOS.x * sx) + up * (IN.positionOS.y * sy);
                edge = abs(IN.uv.x * 2 - 1);
#else
                posWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 nWS = TransformObjectToWorldNormal(IN.normalOS);
                float3 vWS = normalize(_WorldSpaceCameraPos - posWS);
                edge = 1 - saturate(abs(dot(nWS, vWS)));
#endif
                OUT.positionCS = TransformWorldToHClip(posWS);
                OUT.uv = IN.uv;
                OUT.edge = edge;
                OUT.inner = IN.color.r;
                OUT.seed = UNITY_MATRIX_M._m03 * 0.37 + UNITY_MATRIX_M._m23 * 0.21;   // each thruster flickers on its own
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float t = _Time.y * _Speed;
                float2 uvA = float2(IN.uv.x * _NoiseScale.x, IN.uv.y * _NoiseScale.y - t) * _NoiseTex_ST.xy;
                half n = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, uvA).r;
                half along = IN.uv.y;
                half flicker = 0.85h + 0.15h * sin(_Time.y * 23.0 + IN.seed);
#if defined(_HIGH)
                float2 uvB = float2(IN.uv.x * _NoiseScale.x * 2.3 + 0.37, IN.uv.y * _NoiseScale.y * 1.7 - t * 1.9) * _NoiseTex_ST.xy;
                half n2 = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, uvB).r;
                n = n * (0.5h + n2);
                flicker *= 0.9h + 0.1h * sin(_Time.y * 41.0 + IN.seed * 2.0);
#endif
                half core = pow(saturate(1.0h - IN.edge * 1.5h - along * 0.9h), 3.0h) + IN.inner * 0.35h;
                half fade = pow(saturate(1.0h - along), 1.35h) * pow(saturate(1.0h - IN.edge), 0.6h);
                fade *= saturate(along * 12.0h + 0.35h);          // soft start at the nozzle lip
                half3 col = lerp(_EdgeColor.rgb, _CoreColor.rgb, saturate(core));
                half energy = fade * (0.3h + 1.3h * n) * flicker * _Intensity * (1.0h + IN.inner);
                return half4(col * energy, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
