Shader "GI/FarProbes"
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
            #include "../Shared/Octahedral.hlsl"

            ///////////////////////////////////////////////////////////////////////////////////////////////////////////

            float FarProbeCell;
            uint FarProbeCascadeCount;
            float4 FarProbeCascadeOrigins[FAR_PROBE_MAX_CASCADES];

            ByteAddressBuffer FarProbeArrays;
            ByteAddressBuffer FarProbeEntries;

            Texture2D<float4> FarProbeRadianceAtlas;

            ///////////////////////////////////////////////////////////////////////////////////////////////////////////

            void vert(in float4 in_position : POSITION,
                      uint in_index : SV_InstanceID,

                      out float4 out_position : SV_POSITION,
                      out float3 out_normal : TEXCOORD0,
                      out uint out_index : TEXCOORD1)
            {
                uint entry_index = FarProbeArrays.Load((FAR_PROBE_ARRAY_ENTRY + in_index) * 4);

                uint cell_index = FarProbeEntries.Load(entry_index * FAR_PROBE_ENTRY_STRIDE + FAR_PROBE_ENTRY_CELL) & UNFLAG;
                uint cascade = cell_index / FAR_PROBE_CASCADE_CELL_COUNT;
                float cell_dim = FarProbeCell * (1u << cascade);

                float3 position = asfloat(FarProbeEntries.Load3(entry_index * FAR_PROBE_ENTRY_STRIDE + FAR_PROBE_ENTRY_POSITION));
                position += in_position.xyz * cell_dim * 0.33f;

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

                uint texel_x = (uint)(uv.x * PROBE_RES);
                uint texel_y = (uint)(uv.y * PROBE_RES);

                uint atlas_x = in_index % FAR_PROBE_ATLAS_COUNT_X;
                uint atlas_y = in_index / FAR_PROBE_ATLAS_COUNT_X;

                uint2 coord = uint2(atlas_x, atlas_y) * PROBE_RES + uint2(texel_x, texel_y);

                float3 radiance = FarProbeRadianceAtlas[coord].rgb;

                return float4(radiance, 1);
            }

            ///////////////////////////////////////////////////////////////////////////////////////////////////////////

            ENDCG
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////

        Pass
        {
            CGPROGRAM

            #pragma vertex vert
            #pragma fragment frag

            ///////////////////////////////////////////////////////////////////////////////////////////////////////////

            #include "UnityCG.cginc"

            #include "../Shared/Defines.hlsl"
            #include "../Shared/Octahedral.hlsl"

            ///////////////////////////////////////////////////////////////////////////////////////////////////////////

            float FarProbeCell;
            uint FarProbeCascadeCount;
            float4 FarProbeCascadeOrigins[FAR_PROBE_MAX_CASCADES];

            ByteAddressBuffer FarProbeArrays;
            ByteAddressBuffer FarProbeEntries;

            Texture2D<float4> FarProbeGatherAtlas;

            ///////////////////////////////////////////////////////////////////////////////////////////////////////////

            void vert(in float4 in_position : POSITION,
                      uint in_index : SV_InstanceID,

                      out float4 out_position : SV_POSITION,
                      out float3 out_normal : TEXCOORD0,
                      out uint out_index : TEXCOORD1)
            {
                uint entry_index = FarProbeArrays.Load((FAR_PROBE_ARRAY_ENTRY + in_index) * 4);

                uint cell_index = FarProbeEntries.Load(entry_index * FAR_PROBE_ENTRY_STRIDE + FAR_PROBE_ENTRY_CELL) & UNFLAG;
                uint cascade = cell_index / FAR_PROBE_CASCADE_CELL_COUNT;
                float cell_dim = FarProbeCell * (1u << cascade);

                float3 position = asfloat(FarProbeEntries.Load3(entry_index * FAR_PROBE_ENTRY_STRIDE + FAR_PROBE_ENTRY_POSITION));
                position += in_position.xyz * cell_dim * 0.33f;

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

                uint texel_x = (uint)(uv.x * FAR_PROBE_RES);
                uint texel_y = (uint)(uv.y * FAR_PROBE_RES);

                uint atlas_x = in_index % FAR_PROBE_ATLAS_COUNT_X;
                uint atlas_y = in_index / FAR_PROBE_ATLAS_COUNT_X;

                uint2 coord = uint2(atlas_x, atlas_y) * FAR_PROBE_RES + uint2(texel_x, texel_y);

                float3 radiance = FarProbeGatherAtlas[coord].rgb;

                return float4(radiance, 1);
            }

            ///////////////////////////////////////////////////////////////////////////////////////////////////////////

            ENDCG
        }
    }
}
