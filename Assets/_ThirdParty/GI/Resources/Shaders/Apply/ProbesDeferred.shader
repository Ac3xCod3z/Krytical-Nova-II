Shader "GI/ApplyProbesDeferred"
{
    Properties {
         ApplyDest("DstMode", Float) = 0
    }

    SubShader
    {
        ZTest Always
        ZWrite Off
        Cull Front
        Blend One [ApplyDest]

        Pass
        {
            CGPROGRAM

            #pragma multi_compile_fragment __ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            #pragma multi_compile_fragment __ BUILTIN_GBUFFER

            #pragma multi_compile_fragment __ PROBES_OCCLUSION
            #pragma multi_compile_fragment __ PROBES_DIFFUSE_ONLY

            #pragma multi_compile_fragment __ PROBES_SH3 PROBES_SIMPLE

            #pragma vertex vert
            #pragma fragment frag

            ///////////////////////////////////////////////////////////////////////////////////////////////////////////

            #include "UnityCG.cginc"

            #include "../Shared/Defines.hlsl"
            #include "../Shared/Depth.hlsl"
            #include "../Shared/Probes.hlsl"
            #include "../Shared/Normals.hlsl"

            ///////////////////////////////////////////////////////////////////////////////////////////////////////////

            #if BUILTIN_GBUFFER
                sampler2D _CameraGBufferTexture0;
            #else
                sampler2D _GBuffer0;
            #endif

            Texture2D _ScreenSpaceOcclusionTexture;
            SamplerState sampler_ScreenSpaceOcclusionTexture;

            float ProbeCell;
            uint ProbeCascadeCount;
            float4 ProbeCascadeOrigins[PROBE_MAX_CASCADES];

            float3 ReferencePosition;

            ByteAddressBuffer ProbeGrid;
            ByteAddressBuffer ProbeEntries;
            ByteAddressBuffer ProbeSH;

            sampler2D ProbeDepthAtlas;

            ///////////////////////////////////////////////////////////////////////////////////////////////////////////

            #include "../Shared/ProbeGrid.hlsl"
            #include "../Shared/Apply.hlsl"

            ///////////////////////////////////////////////////////////////////////////////////////////////////////////

            void vert(in float4 in_position : POSITION,
                      in float2 in_uv       : TEXCOORD,

                      out float4 out_position : SV_POSITION,
                      out float2 out_uv       : TEXCOORD0)
            {
                out_position = in_position;
                out_uv = in_uv;
            }

            ///////////////////////////////////////////////////////////////////////////////////////////////////////////

            half4 frag(float4 in_position : SV_POSITION,
                       float2 in_uv       : TEXCOORD0) : SV_Target
            {
                float2 uv = in_position.xy / _ScaledScreenParams.xy;
                float depth = actualDepth(sampleSceneDepth(uv));

                #if UNITY_REVERSED_Z
                if (depth <= DEPTH_EPSILON_ZERO)
                #else
                if (depth >= DEPTH_EPSILON_ONE)
                #endif
                    discard;

                float3 world_position = worldSpacePosition(uv, depth, unity_MatrixInvVP);

                #if BUILTIN_GBUFFER
                    float3 base_color = tex2Dlod(_CameraGBufferTexture0, float4(uv, 0, 0)).rgb;
                #else
                    float3 base_color = tex2Dlod(_GBuffer0, float4(uv, 0, 0)).rgb;
                #endif

                float3 normal = loadNormals(uv);

                float3 light = applyGIAt(world_position, normal);

                #if defined(_SCREEN_SPACE_OCCLUSION)
                    light *= _ScreenSpaceOcclusionTexture.SampleLevel(sampler_ScreenSpaceOcclusionTexture, uv, 0).x;
                #endif

                #if PROBES_DIFFUSE_ONLY
                    base_color = 1;
                #endif

                return float4(base_color * light, 0);
            }

            ///////////////////////////////////////////////////////////////////////////////////////////////////////////

            ENDCG
        }
    }
}
