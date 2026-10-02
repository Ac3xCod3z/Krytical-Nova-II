Shader "GI/SDF"
{
    SubShader
    {
        CGINCLUDE

        #pragma enable_d3d11_debug_symbols

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////

        struct Vertex
        {
            float4 position : SV_POSITION;
            float2 texcoord : TEXCOORD0;
        };

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////

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

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////

        #include "UnityCG.cginc"

        #include "../Shared/Defines.hlsl"
        #include "../Shared/Depth.hlsl"
        #include "../Shared/Rays.hlsl"
        #include "../Shared/Instances.hlsl"
        #include "../Shared/SDF.hlsl"

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////

        uint BufferCount;
        float FarClipPlane;

        ByteAddressBuffer SDFAssets;
        ByteAddressBuffer SDFBricks;
        ByteAddressBuffer DFInstances;

        sampler3D SDFAtlas;
        sampler3D VoxelAtlas;

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////

        float4 Frag(in float4 in_position : SV_POSITION,
                    in float2 in_uv       : TEXCOORD0) : SV_TARGET
        {
            float3 camera_origin = _WorldSpaceCameraPos;
            float3 camera_ray = cameraRayAtFrag(in_position.xy);

            float min_trace_distance = 0.01;
            float max_trace_distance = FarClipPlane;

            Hit hit;
            hit.has_hit = false;
            hit.dist = max_trace_distance;

            HitSurface surface;

            bool result = false;

            [loop]
            for (uint instance_id = 0; instance_id < BufferCount; instance_id++)
            {
                uint asset_id = loadAssetMapping(DFInstances, instance_id);

                [branch]
                if (asset_id != INVALID_ID)
                {
                    if (traceInstance(SDFAssets,
                                      DFInstances,
                                      SDFBricks,
                                      SDFAtlas,
                                      VoxelAtlas,

                                      instance_id,
                                      asset_id,
                                      camera_origin,
                                      camera_ray,
                                      min_trace_distance,
                                      max_trace_distance,

                                      hit,
                                      surface))
                    {
                        result = true;
                    }
                }
            }

            if (hit.has_hit)
            {
                float n_dot_l = saturate(dot(surface.normal, normalize(float3(1, 1, 1))) * 0.5 + 0.5) * 0.9 + 0.1;
                float3 color = surface.color;

                return float4(color * (n_dot_l + surface.emission), 1);
            }
            else
                return float4(0, 0, 0, 0.75);
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////

        ENDCG

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////

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