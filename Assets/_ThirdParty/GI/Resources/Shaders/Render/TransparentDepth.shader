Shader "GI/TransparentDepth"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Cutoff("Alpha Cutoff", Range(0.0, 1.0)) = 0.5
    }
    SubShader
    {
        Pass
        {
            Blend One One

            ///////////////////////////////////////////////////////////////////////////////////////////////////////////

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #pragma shader_feature_local_fragment _ALPHATEST_ON

            ///////////////////////////////////////////////////////////////////////////////////////////////////////////

            #include "UnityCG.cginc"

            #include "../Shared/Defines.hlsl"
            #include "../Shared/Depth.hlsl"
            #include "../Shared/Probes.hlsl"
            #include "../Shared/Octahedral.hlsl"

            ///////////////////////////////////////////////////////////////////////////////////////////////////////////

            float GIApplyDest;

            float ProbeCell;
            uint ProbeCascadeCount;
            float4 ProbeCascadeOrigins[PROBE_MAX_CASCADES];

            float3 ReferencePosition;

            ByteAddressBuffer ProbeGrid;
            ByteAddressBuffer ProbeEntries;
            ByteAddressBuffer ProbeSH;

            sampler2D ProbeDepthAtlas;

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _BaseColor;
            half _Cutoff;

            ///////////////////////////////////////////////////////////////////////////////////////////////////////////

            #include "../Shared/ProbeGrid.hlsl"
            #include "../Shared/Apply.hlsl"
            #include "../Shared/Transform.hlsl"
            #include "../Shared/Packing.hlsl"

            ///////////////////////////////////////////////////////////////////////////////////////////////////////////

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv     : TEXCOORD0;
            };

            ///////////////////////////////////////////////////////////////////////////////////////////////////////////

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float3 world : TEXCOORD1;
                float3 normal : TEXCOORD2;
                float4 vertex : SV_POSITION;
            };

            ///////////////////////////////////////////////////////////////////////////////////////////////////////////

            v2f vert(appdata v)
            {
                v2f o;
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);

                o.world = objectToWorld(v.vertex);
                o.normal = objectToWorldNormal(v.normal);
                o.vertex = worldToClipSpace(o.world);

                return o;
            }

            ///////////////////////////////////////////////////////////////////////////////////////////////////////////

            float3 cellCoordToCellIndex(float3 cell_coord_f, uint cascade)
            {
                int3 cell_coord = int3(round(cell_coord_f));

                uint cell_index = cell_coord.x +
                                  (cell_coord.y * PROBE_CASCADE_DIM) +
                                  (cell_coord.z * PROBE_CASCADE_DIM * PROBE_CASCADE_DIM) +
                                  (cascade * PROBE_CASCADE_CELL_COUNT);

                return cell_index;
            }

            ///////////////////////////////////////////////////////////////////////////////////////////////////////////

            float4 frag(v2f i) : SV_Target
            {
                half4 color = tex2D(_MainTex, i.uv) * _BaseColor;

                #if defined(_ALPHATEST_ON)

                    half clipped_alpha = color.a >= _Cutoff ? float(color.a) : 0.0;
                    clip(clipped_alpha - 0.0001);

                #endif

                uint sample_index = normalToSampleIndex(i.normal);

                float3 direction = normalize(ReferencePosition - i.world);
                float3 query_normal = normalize(i.normal + direction);

                uint cascade = positionToProbeCascade(i.world);
                float bias = ProbeCell * (1 << cascade) * PROBE_QUERY_BIAS;

                float3 query_position = i.world + query_normal * bias;

                float3 cell_coord = positionToProbeCellCoordFloat(query_position, cascade);
                uint cell_index = cellCoordToCellIndex(cell_coord, cascade);

                float r = packByte(cell_index & 0xFF);
                float g = packByte((cell_index >> 8) & 0xFF);
                float b = packByte((cell_index >> 16) & 0xFF);
                float a = packByte(sample_index);

                return float4(r, g, b, a);
            }

            ///////////////////////////////////////////////////////////////////////////////////////////////////////////

            ENDCG
        }
    }
}
