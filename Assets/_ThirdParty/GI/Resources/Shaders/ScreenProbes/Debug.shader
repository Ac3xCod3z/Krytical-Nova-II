Shader "GI/ScreenTraceDebug"
{
    SubShader
    {
        HLSLINCLUDE

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

        Texture2D<float4> ScreenProbeRadianceAtlas;

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////

        float4 Frag(in float4 in_position : SV_POSITION,
                    in float2 in_uv       : TEXCOORD0) : SV_TARGET
        {
            uint2 dim;
            ScreenProbeRadianceAtlas.GetDimensions(dim.x, dim.y);

            uint2 coord = uint2(in_position.xy);

            if (any(coord >= dim))
                discard;

            return float4(ScreenProbeRadianceAtlas[coord].rgb, 1);
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////

        ENDHLSL

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////

        Pass
        {
            ZTest Always
            ZWrite Off
            Cull Off
            Blend One OneMinusSrcAlpha

            HLSLPROGRAM
                #pragma vertex Vert
                #pragma fragment Frag
            ENDHLSL
        }
    }
}