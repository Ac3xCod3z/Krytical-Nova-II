// A lit rim (owner, 10 Nov 2026): light from the key light (the moon at night) catching the body's edges, the way a studio does it.
// Drawn as one extra additive pass over the character's own mesh, so it only ever adds light, never tints:
//   rim = pow(1 - saturate(N.V), _RimPower) * saturate(N.L) * shadowAttenuation * lightColour * _RimTint * _RimIntensity * _KN_NightAmount
// It shows only on surfaces that face the light and are seen at a grazing angle, follows the real normals as a soft gradient,
// goes out where the body stands in shadow, and leaves no line on the unlit side. _KN_NightAmount (EnvironmentPresenter) is 0 by day
// and carries the night brightness slider.
Shader "KN/LitRim"
{
    Properties
    {
        _RimPower ("Rim power (higher = thinner edge)", Range(1, 8)) = 3
        _RimIntensity ("Rim intensity", Range(0, 6)) = 3
        _RimTint ("Rim tint (cool moonlight)", Color) = (0.72, 0.84, 1, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Geometry+10" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "LitRim"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Back
            Offset -1, -1

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _RimPower;
                float _RimIntensity;
                float4 _RimTint;
            CBUFFER_END
            float _KN_NightAmount;

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; UNITY_VERTEX_INPUT_INSTANCE_ID };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                VertexPositionInputs p = GetVertexPositionInputs(input.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float night = _KN_NightAmount;
                if (night <= 0.001) return 0;
                float3 n = normalize(input.normalWS);
                float3 v = normalize(GetWorldSpaceViewDir(input.positionWS));
                Light light = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                float facing = saturate(dot(n, light.direction));               // faces the moon
                float grazing = pow(1.0 - saturate(dot(n, v)), _RimPower);       // seen edge-on
                float rim = grazing * facing * light.shadowAttenuation;
                float3 col = light.color * _RimTint.rgb * (rim * _RimIntensity * night);
                return half4(col, 0);
            }
            ENDHLSL
        }
    }
}
