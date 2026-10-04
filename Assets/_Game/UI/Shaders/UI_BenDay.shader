// uGUI sprite with the game's Ben-Day dots printed over it. The dots use the same screen-aligned grid as the
// world's comic pass (ComicHalftone.hlsl), so menus and the 3D scene behind them read as one printed page.
// Dot coverage follows a gradient across the screen (_Direction), from _CoverageFrom to _CoverageTo.
Shader "KryticalNova/UI/BenDay"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _InkColor ("Dot Colour", Color) = (0.078, 0.071, 0.169, 1)
        _DotOpacity ("Dot Opacity", Range(0, 1)) = 0.35
        _CoverageFrom ("Coverage At Start", Range(0, 1)) = 0.0
        _CoverageTo ("Coverage At End", Range(0, 1)) = 0.45
        _Direction ("Gradient Direction (xy)", Vector) = (0, -1, 0, 0)
        _DotScale ("Dot Size Multiplier", Range(0.25, 4)) = 1

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="True" }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend One OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            #include "Assets/_Game/Rendering/Shaders/Comic/ComicHalftone.hlsl"
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata_t
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                float4 screenPos : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;
            float4 _MainTex_ST;
            fixed4 _InkColor;
            float _DotOpacity;
            float _CoverageFrom;
            float _CoverageTo;
            float4 _Direction;
            float _DotScale;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.worldPosition = v.vertex;
                OUT.vertex = UnityObjectToClipPos(OUT.worldPosition);
                OUT.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                OUT.color = v.color * _Color;
                OUT.screenPos = ComputeScreenPos(OUT.vertex);
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                half4 color = (tex2D(_MainTex, IN.texcoord) + _TextureSampleAdd) * IN.color;

                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001);
                #endif

                float2 uv = IN.screenPos.xy / IN.screenPos.w;
                // 0 at the start of the gradient, 1 at the end, measured across the whole screen.
                float2 dir = normalize(_Direction.xy + float2(1e-5, 0));
                float t = saturate(dot(uv - 0.5, dir) + 0.5);
                half coverage = lerp(_CoverageFrom, _CoverageTo, t);
                half dots = ComicHalftone(uv * _ScreenParams.xy / max(_DotScale, 0.01), coverage) * _DotOpacity;
                color.rgb = lerp(color.rgb, _InkColor.rgb, dots);

                color.rgb *= color.a; // premultiplied
                return color;
            }
            ENDCG
        }
    }
}
