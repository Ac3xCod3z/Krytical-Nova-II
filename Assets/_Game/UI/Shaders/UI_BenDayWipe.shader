// Full-screen comic transition: Ben-Day dots grow from one corner until they merge into solid ink (_Progress 0 -> 1),
// and shrink away again on the next screen. Replaces a plain fade to black between menu scenes.
Shader "KryticalNova/UI/BenDayWipe"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Ink Colour", Color) = (0.078, 0.071, 0.169, 1)
        _Progress ("Progress", Range(0, 1)) = 0
        _Direction ("Sweep Direction (xy)", Vector) = (1, 1, 0, 0)
        _Softness ("Sweep Width", Range(0.05, 1)) = 0.5
        _DotScale ("Dot Size Multiplier", Range(0.5, 8)) = 4

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" }

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
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            #include "Assets/_Game/Rendering/Shaders/Comic/ComicHalftone.hlsl"

            struct appdata_t
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float4 screenPos : TEXCOORD0;
            };

            fixed4 _Color;
            float _Progress;
            float4 _Direction;
            float _Softness;
            float _DotScale;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                OUT.vertex = UnityObjectToClipPos(v.vertex);
                OUT.color = v.color * _Color;
                OUT.screenPos = ComputeScreenPos(OUT.vertex);
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                float2 uv = IN.screenPos.xy / IN.screenPos.w;
                float2 dir = normalize(_Direction.xy + float2(1e-5, 0));
                // 0 where the sweep starts, 1 where it ends.
                float t = saturate(dot(uv - 0.5, dir) * 0.7071 + 0.5);
                // Each point's dot grows as the sweep front passes it; the front is _Softness wide.
                half coverage = saturate((_Progress * (1.0 + _Softness) - t) / _Softness);
                half dots = coverage >= 0.999 ? 1.0 : ComicHalftone(uv * _ScreenParams.xy / max(_DotScale, 0.01), coverage);
                fixed4 color = IN.color;
                color.a *= dots;
                return color;
            }
            ENDCG
        }
    }
}
