Shader "GI/ScreenProbesIntegrate"
{
    SubShader
    {
        ZTest Always
        ZWrite Off
        Cull Back
        Blend One Zero

        Pass
        {
            CGPROGRAM

            #pragma multi_compile_fragment __ CAMERA_NORMALS

            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            #pragma vertex vert
            #pragma fragment frag

            ///////////////////////////////////////////////////////////////////////////////////////////////////////////

            #include "UnityCG.cginc"

            #include "../Shared/Defines.hlsl"
            #include "../Shared/Input.hlsl"
            #include "../Shared/Depth.hlsl"
            #include "../Shared/Probes.hlsl"
            #include "../Shared/Normals.hlsl"

            ///////////////////////////////////////////////////////////////////////////////////////////////////////////

            ByteAddressBuffer ScreenProbes;
            ByteAddressBuffer ScreenProbeSH;

            uint ScreenProbeCountX;
            uint ScreenProbeCountY;

            Texture2D<uint> ScreenProbeAdaptiveMapping;

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

            uint probeIndex(uint2 coord)
            {
                coord = min(coord, uint2(ScreenProbeCountX, ScreenProbeCountY) - 1);
                return coord.x + coord.y * ScreenProbeCountX;
            }

            ///////////////////////////////////////////////////////////////////////////////////////////////////////////

            float3 probePosition(uint entry)
            {
                return asfloat(ScreenProbes.Load3(entry * SCREEN_PROBE_STRIDE + SCREEN_PROBE_POSITION));
            }

            ///////////////////////////////////////////////////////////////////////////////////////////////////////////

            half4 frag(float4 in_position : SV_POSITION,
                       float2 in_uv       : TEXCOORD0) : SV_Target
            {
                float2 uv = in_position.xy / _ScaledScreenParams.xy;

                float sampled_depth = sampleSceneDepth(uv);
                float depth = actualDepth(sampled_depth);

                #if UNITY_REVERSED_Z
                if (depth <= DEPTH_EPSILON_ZERO)
                #else
                if (depth >= DEPTH_EPSILON_ONE)
                #endif
                    discard;

                uint2 coord = uint2(in_position.xy);

                float linear_depth = LinearEyeDepth(sampled_depth, _ZBufferParams);

                uint2 local_coord = coord % SCREEN_PROBE_DIM;
                uint2 probe_coord = coord / SCREEN_PROBE_DIM;

                static const float DIM_INV = 1.0f / SCREEN_PROBE_DIM;
                float2 bilinear_weights = local_coord * DIM_INV;

                float4 bilinear_interpolation_weights = float4((1 - bilinear_weights.x) * (1 - bilinear_weights.y),
                                                               bilinear_weights.x * (1 - bilinear_weights.y),
                                                               (1 - bilinear_weights.x) * bilinear_weights.y,
                                                               bilinear_weights.x * bilinear_weights.y);

                uint4 entries;
                entries.x = probeIndex(uint2(probe_coord.x,     probe_coord.y));
                entries.y = probeIndex(uint2(probe_coord.x + 1, probe_coord.y));
                entries.z = probeIndex(uint2(probe_coord.x,     probe_coord.y + 1));
                entries.w = probeIndex(uint2(probe_coord.x + 1, probe_coord.y + 1));

                float3 probe_position_00 = probePosition(entries.x);
                float3 probe_position_10 = probePosition(entries.y);
                float3 probe_position_01 = probePosition(entries.z);
                float3 probe_position_11 = probePosition(entries.w);

                float3 position = worldSpacePosition(uv, depth, unity_MatrixInvVP);
                float3 normal = loadNormals(uv);

                float4 scene_plane = float4(normal, dot(position, normal));

                float4 plane_distances;
                plane_distances.x = abs(dot(float4(probe_position_00, -1), scene_plane));
                plane_distances.y = abs(dot(float4(probe_position_10, -1), scene_plane));
                plane_distances.z = abs(dot(float4(probe_position_01, -1), scene_plane));
                plane_distances.w = abs(dot(float4(probe_position_11, -1), scene_plane));

                float4 relative_depth_difference = plane_distances / linear_depth;

                float4 depth_weights = exp2(-1000.0f * (relative_depth_difference * relative_depth_difference));

                float4 interpolation_weights = bilinear_interpolation_weights;
                interpolation_weights *= depth_weights;

                float total_weight = dot(interpolation_weights, 1);
                float base_weights = total_weight;

                //

                float3 adaptive_diffuse = 0;
                float adaptive_interpolation_weight = 0;

                uint2 adaptive_coord = coord / SCREEN_PROBE_ADAPTIVE_DIM;
                uint adaptive_entry = ScreenProbeAdaptiveMapping[adaptive_coord];

                [branch]
                if (adaptive_entry > 0) // assume entries before the adaptive one are all non-adaptive probes
                {
                    float3 adaptive_position = probePosition(adaptive_entry);

                    float adaptive_plane_distance = abs(dot(float4(adaptive_position, -1), scene_plane));
                    float adaptive_relative_depth_difference = adaptive_plane_distance / linear_depth;
                    adaptive_interpolation_weight = exp2(-1000.0f * (adaptive_relative_depth_difference * adaptive_relative_depth_difference));

                    total_weight += adaptive_interpolation_weight;
                    adaptive_diffuse = evaluateSH2(loadProbeSH2(ScreenProbeSH, adaptive_entry), normal);
                }

                static const float EPSILON = 0.001f;

                [flatten]
                if (total_weight < EPSILON)
                {
                    interpolation_weights = lerp(bilinear_interpolation_weights, interpolation_weights, total_weight / EPSILON);
                    total_weight = adaptive_interpolation_weight + 1;
                }

                float limited_total_weight = max(total_weight, EPSILON);

                interpolation_weights /= limited_total_weight;
                adaptive_interpolation_weight /= limited_total_weight;

                //

                float3 diffuse = adaptive_diffuse * adaptive_interpolation_weight;
                diffuse += evaluateSH2(loadProbeSH2(ScreenProbeSH, entries.x), normal) * interpolation_weights.x;
                diffuse += evaluateSH2(loadProbeSH2(ScreenProbeSH, entries.y), normal) * interpolation_weights.y;
                diffuse += evaluateSH2(loadProbeSH2(ScreenProbeSH, entries.z), normal) * interpolation_weights.z;
                diffuse += evaluateSH2(loadProbeSH2(ScreenProbeSH, entries.w), normal) * interpolation_weights.w;

                return float4(diffuse, 1);
                // return float4(adaptive_interpolation_weight, adaptive_interpolation_weight, adaptive_interpolation_weight, 1);
            }

            ///////////////////////////////////////////////////////////////////////////////////////////////////////////

            ENDCG
        }
    }
}
