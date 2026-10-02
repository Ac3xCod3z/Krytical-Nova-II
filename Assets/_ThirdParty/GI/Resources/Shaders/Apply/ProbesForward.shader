Shader "GI/ApplyProbesForward"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _BaseColor("Color", Color) = (1,1,1,1)

        _Cutoff("Alpha Cutoff", Range(0.0, 1.0)) = 0.5
        _Cull("__cull", Float) = 2.0
        [HideInInspector] _AlphaToMask("__alphaToMask", Float) = 0.0
        [ToggleUI] _AlphaClip("__clip", Float) = 0.0
    }
    SubShader
    {

        Pass
        {
            Name "Normal"
            Blend One One
            Cull[_Cull]
            AlphaToMask[_AlphaToMask]

            ///////////////////////////////////////////////////////////////////////////////////////////////////////////

            CGPROGRAM

            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile_fragment __ _SCREEN_SPACE_OCCLUSION

            #pragma multi_compile_fragment __ PROBES_OCCLUSION
            #pragma multi_compile_fragment __ PROBES_DIFFUSE_ONLY

            #pragma multi_compile_fragment __ PROBES_SH3 PROBES_SIMPLE

            #pragma shader_feature_local_fragment _ALPHATEST_ON

            ///////////////////////////////////////////////////////////////////////////////////////////////////////////

            #include "UnityCG.cginc"

            #include "../Shared/Defines.hlsl"
            #include "../Shared/Depth.hlsl"
            #include "../Shared/Probes.hlsl"

            ///////////////////////////////////////////////////////////////////////////////////////////////////////////

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
            float _Cutoff;

            Texture2D _ScreenSpaceOcclusionTexture;
            SamplerState sampler_ScreenSpaceOcclusionTexture;

            ///////////////////////////////////////////////////////////////////////////////////////////////////////////

            #include "../Shared/ProbeGrid.hlsl"
            #include "../Shared/Apply.hlsl"
            #include "../Shared/Transform.hlsl"

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

            half4 frag(v2f i) : SV_Target
            {
                half4 color = tex2D(_MainTex, i.uv) * _BaseColor;

                #if defined(_ALPHATEST_ON)

                    half clipped_alpha = color.a >= _Cutoff ? float(color.a) : 0.0;
                    clip(clipped_alpha - 0.0001);

                #endif

                float3 light = applyGIAt(i.world, normalize(i.normal));

                #if defined(_SCREEN_SPACE_OCCLUSION)
                    float2 uv = i.vertex.xy / _ScaledScreenParams.xy;
                    light *= _ScreenSpaceOcclusionTexture.SampleLevel(sampler_ScreenSpaceOcclusionTexture, uv, 0).x;
                #endif

                color.rgb *= light;

                return color;
            }

            ///////////////////////////////////////////////////////////////////////////////////////////////////////////

            ENDCG
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////

        Pass
        {
            Name "DiffuseOnly"
            Blend One Zero
            Cull[_Cull]
            AlphaToMask[_AlphaToMask]

            CGPROGRAM

            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile_fragment __ _SCREEN_SPACE_OCCLUSION

            #pragma multi_compile_fragment __ PROBES_OCCLUSION
            #pragma multi_compile_fragment __ PROBES_DIFFUSE_ONLY

            #pragma multi_compile_fragment __ PROBES_SH3 PROBES_SIMPLE

            #pragma shader_feature_local_fragment _ALPHATEST_ON

            ///////////////////////////////////////////////////////////////////////////////////////////////////////////

            #include "UnityCG.cginc"

            #include "../Shared/Defines.hlsl"
            #include "../Shared/Depth.hlsl"
            #include "../Shared/Probes.hlsl"

            ///////////////////////////////////////////////////////////////////////////////////////////////////////////

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
            float _Cutoff;

            Texture2D _ScreenSpaceOcclusionTexture;
            SamplerState sampler_ScreenSpaceOcclusionTexture;

            ///////////////////////////////////////////////////////////////////////////////////////////////////////////

            #include "../Shared/ProbeGrid.hlsl"
            #include "../Shared/Apply.hlsl"
            #include "../Shared/Transform.hlsl"

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

            v2f vert (appdata v)
            {
                v2f o;
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);

                o.world = objectToWorld(v.vertex);
                o.normal = objectToWorldNormal(v.normal);
                o.vertex = worldToClipSpace(o.world);

                return o;
            }

            ///////////////////////////////////////////////////////////////////////////////////////////////////////////

            half4 frag (v2f i) : SV_Target
            {
                half4 color = tex2D(_MainTex, i.uv) * _BaseColor;

                #if defined(_ALPHATEST_ON)

                    half clipped_alpha = color.a >= _Cutoff ? float(color.a) : 0.0;
                    clip(clipped_alpha - 0.0001);

                #endif

                float3 light = applyGIAt(i.world, normalize(i.normal));

                #if defined(_SCREEN_SPACE_OCCLUSION)
                    float2 uv = i.vertex.xy / _ScaledScreenParams.xy;
                    light *= _ScreenSpaceOcclusionTexture.SampleLevel(sampler_ScreenSpaceOcclusionTexture, uv, 0).x;
                #endif

                color.rgb = light;
                color.a = 1;

                return color;
            }

            ///////////////////////////////////////////////////////////////////////////////////////////////////////////

            ENDCG
        }
    }
}
