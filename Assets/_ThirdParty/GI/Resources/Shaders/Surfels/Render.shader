Shader "GI/Surfels"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        LOD 100

        Pass
        {
            CGPROGRAM

            #pragma vertex vert
            #pragma fragment frag

            ///////////////////////////////////////////////////////////////////////////////////////////////////////////

            #include "UnityCG.cginc"

            #include "../Shared/Defines.hlsl"

            ///////////////////////////////////////////////////////////////////////////////////////////////////////////

            ByteAddressBuffer SurfelArrays;
            ByteAddressBuffer SurfelEntries;
            ByteAddressBuffer SurfelRadiance;

            float4 SurfelCascadeOrigins[SURFEL_CASCADE_COUNT];

            ///////////////////////////////////////////////////////////////////////////////////////////////////////////

            void vert(in float4 in_position : POSITION,
                      uint in_index : SV_InstanceID,

                      out float4 out_position : SV_POSITION,
                      out float3 out_normal : TEXCOORD0,
                      out uint out_index : TEXCOORD1)
            {
                uint entry_index = SurfelArrays.Load((SURFEL_ARRAY_ENTRY + in_index) * 4);
                uint cell_index = SurfelEntries.Load(entry_index * SURFEL_ENTRY_STRIDE + SURFEL_ENTRY_CELL) & UNFLAG;

                uint cascade = cell_index / SURFEL_CASCADE_CELL_COUNT;
                uint cascade_cell = cell_index % SURFEL_CASCADE_CELL_COUNT;
                uint3 cell_coord = uint3(cascade_cell % SURFEL_CASCADE_DIM,
                                         (cascade_cell / SURFEL_CASCADE_DIM) % SURFEL_CASCADE_DIM,
                                         cascade_cell / (SURFEL_CASCADE_DIM * SURFEL_CASCADE_DIM));

                float cell_dim = SURFEL_CELL_DIM * (1 << cascade);

                float3 position = asfloat(SurfelEntries.Load3(SURFEL_ENTRY_STRIDE * entry_index + SURFEL_ENTRY_POSITION));
                position += in_position.xyz * cell_dim * 0.4;

                out_position = mul(UNITY_MATRIX_VP, float4(position, 1.0f));
                out_normal = normalize(in_position.xyz);
                out_index = entry_index;
            }

            ///////////////////////////////////////////////////////////////////////////////////////////////////////////

            fixed4 frag(float4 in_position : SV_POSITION,
                        float3 in_normal : TEXCOORD0,
                        uint in_index : TEXCOORD1) : SV_Target
            {
                float3 radiance = asfloat(SurfelRadiance.Load3(in_index * RADIANCE_STRIDE));

                return float4(radiance, 1);
            }

            ///////////////////////////////////////////////////////////////////////////////////////////////////////////

            ENDCG
        }
    }
}
