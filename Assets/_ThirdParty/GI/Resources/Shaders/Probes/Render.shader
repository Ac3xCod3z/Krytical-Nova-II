Shader "GI/Probes"
{
	SubShader
	{
		Tags { "RenderType" = "Opaque" }
		LOD 100

		Pass
		{
			CGPROGRAM

            #pragma enable_d3d11_debug_symbols

			#pragma vertex vert
			#pragma fragment frag

            #pragma multi_compile_fragment __ RADIANCE
            #pragma multi_compile_fragment __ DEPTHS

            #pragma multi_compile_fragment __ PROBES_SIMPLE

            #if !SHADER_API_GLES3 && !SHADER_API_GLES
                #pragma multi_compile_fragment __ PROBES_SH3
            #else
            #endif

            ///////////////////////////////////////////////////////////////////////////////////////////////////////////

            #include "UnityCG.cginc"

			#include "../Shared/Defines.hlsl"
            #include "../Shared/Probes.hlsl"
            #include "../Shared/Octahedral.hlsl"

            ///////////////////////////////////////////////////////////////////////////////////////////////////////////

            float ProbeCell;
            uint ProbeCascadeCount;

            ByteAddressBuffer ProbeArrays;
            ByteAddressBuffer ProbeEntries;
            ByteAddressBuffer ProbeSH;

            Texture2D<float4> ProbeRadianceAtlas;
            Texture2D<float4> ProbeTraceAtlas;

            ///////////////////////////////////////////////////////////////////////////////////////////////////////////

            void vert(in float4 in_position : POSITION,
                      uint in_index : SV_InstanceID,

                      out float4 out_position : SV_POSITION,
                      out float3 out_normal : TEXCOORD0,
                      out uint out_index : TEXCOORD1)
            {
                uint entry_index = ProbeArrays.Load((PROBE_ARRAY_ENTRY + in_index) * 4);

                uint cell_index = ProbeEntries.Load(entry_index * PROBE_ENTRY_STRIDE + PROBE_ENTRY_CELL) & UNFLAG;
                uint cascade = cell_index / PROBE_CASCADE_CELL_COUNT;

                float cell_dim = ProbeCell * (1u << cascade);

                float3 position = asfloat(ProbeEntries.Load3(entry_index * PROBE_ENTRY_STRIDE + PROBE_ENTRY_POSITION));
				position += in_position.xyz * cell_dim * 0.25f;

                out_position = mul(UNITY_MATRIX_VP, float4(position, 1.0f));
				out_normal = normalize(in_position.xyz);
                out_index = entry_index;
            }

            ///////////////////////////////////////////////////////////////////////////////////////////////////////////

            fixed4 frag(float4 in_position : SV_POSITION,
                        float3 in_normal : TEXCOORD0,
                        uint in_index : TEXCOORD1) : SV_Target
            {
                float3 normal = normalize(in_normal);

                float2 uv = directionToOctahedralMap(normal);

                uint probe_atlas_x = in_index % PROBE_ATLAS_COUNT_X;
                uint probe_atlas_y = in_index / PROBE_ATLAS_COUNT_X;

                #if DEPTHS

                    uint texel_x = (uint)(uv.x * PROBE_RES);
                    uint texel_y = (uint)(uv.y * PROBE_RES);

                    uint atlas_x = probe_atlas_x * PROBE_RES + texel_x;
                    uint atlas_y = probe_atlas_y * PROBE_RES + texel_y;

                    float max_depth = ProbeCell * 64;

                    float3 result = saturate(ProbeRadianceAtlas.Load(uint3(atlas_x, atlas_y, 0)).w / max_depth);

                #elif RADIANCE

                    uint texel_x = (uint)(uv.x * PROBE_RES);
                    uint texel_y = (uint)(uv.y * PROBE_RES);

                    uint atlas_x = probe_atlas_x * PROBE_RES + texel_x;
                    uint atlas_y = probe_atlas_y * PROBE_RES + texel_y;

                    float3 result = ProbeRadianceAtlas.Load(uint3(atlas_x, atlas_y, 0)).rgb;

                #elif PROBES_SIMPLE

                    float3 result = loadProbeColor(ProbeSH, in_index);

                #elif PROBES_SH3

                    SH3 probe = loadProbeSH3(ProbeSH, in_index);
                    float3 result = evaluateSH3(probe, normal);

                #else

                    SH2 probe = loadProbeSH2(ProbeSH, in_index);
                    float3 result = evaluateSH2(probe, normal);

                #endif

                return float4(result, 1);
            }

            ///////////////////////////////////////////////////////////////////////////////////////////////////////////

            ENDCG
        }
    }
}
