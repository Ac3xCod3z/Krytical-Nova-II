Shader "GI/GDF"
{
    SubShader
    {
        CGINCLUDE

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////

        struct Vertex
        {
            float4 position : SV_POSITION;
            float2 texcoord : TEXCOORD0;
        };

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////

        void Vert(in uint vertex_id : SV_VertexID,

                  out float4 out_position : SV_POSITION,
                  out float2 out_uv : TEXCOORD0)
        {
            float2 screen = 0;

            switch (vertex_id)
            {
                case 0:
                    screen = float2(0, 1);
                    break;

                case 1:
                    screen = float2(1, 0);
                    break;

                case 2:
                    screen = float2(1, 1);
                    break;

                case 3:
                    screen = float2(0, 1);
                    break;

                case 4:
                    screen = float2(0, 0);
                    break;

                case 5:
                    screen = float2(1, 0);
                    break;
            }

            float2 p = screen * float2(2, -2) - float2(1, -1);

            out_position = float4(p, 0, 1);
            out_uv = screen;
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////

        #include "UnityCG.cginc"

        #include "../Shared/Defines.hlsl"
        #include "../Shared/Depth.hlsl"
        #include "../Shared/Rays.hlsl"

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////

        float3 ReferencePosition;

        sampler3D VoxelAtlas;

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////

        #include "../Shared/GDF.hlsl"
        #include "../Shared/GDFVoxel.hlsl"

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////

        float4 Frag(in float4 in_position : SV_POSITION,
                    in float2 in_uv       : TEXCOORD0) : SV_TARGET
        {
            float3 camera_origin = _WorldSpaceCameraPos;
            float3 camera_ray = cameraRayAtFrag(in_position.xy);

            float min_trace_distance = 0.01;
            float max_trace_distance = GDF_GRID_DIM * GDF_CELL_DIM;

            Hit hit;
            hit.has_hit = false;
            hit.dist = max_trace_distance;

            float3 normal;
            float4 color_emission;

            if (traceGlobal(camera_origin,
                            camera_ray,
                            min_trace_distance,
                            max_trace_distance,

                            128,

                            hit))
            {
                float3 hit_position = camera_origin + hit.dist * camera_ray;
                uint cascade = gdfCascade(hit_position);

                float3 uvw = gdfUVW(hit_position, cascade);
                normal = gdfNormalAtUVW(uvw, cascade);
                color_emission = gdfColorAndEmission(VoxelAtlas, hit_position, cascade);
            }

            if (hit.has_hit)
            {
                float n_dot_l = saturate(dot(normal, normalize(float3(1, 1, 1)))) * 0.4 + 0.6;

                return float4(color_emission.rgb * (color_emission.a + n_dot_l), 1);
            }
            else
                return float4(0, 0, 0, 0);
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////

        ENDCG

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////

        Pass
        {
            ZTest Always
            ZWrite Off
            Cull Off
            Blend One OneMinusSrcAlpha

            CGPROGRAM
                #pragma vertex Vert
                #pragma fragment Frag
            ENDCG
        }
    }
}