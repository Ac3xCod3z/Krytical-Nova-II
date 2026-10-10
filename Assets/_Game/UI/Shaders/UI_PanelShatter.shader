// The way into the world: the picture of the Loading page is cut into comic panels (ink lines draw in along the
// cuts), and the panels fly off one after another, each turning as it goes, to show the game behind them.
// _Progress 0 = the whole picture, 1 = nothing left. The panels and where each one goes are set from code
// (MenuTransition), a different cut every time.
Shader "KryticalNova/UI/PanelShatter"
{
    Properties
    {
        [PerRendererData] _MainTex ("Picture", 2D) = "white" {}
        _Ink ("Ink Colour", Color) = (0.078, 0.071, 0.169, 1)
        _Accent ("Accent Colour", Color) = (0.298, 0.322, 0.749, 1)
        _Progress ("Progress", Range(0, 1)) = 0
        _LineIn ("Share Of The Time The Lines Take", Range(0.02, 0.6)) = 0.22
        _Fly ("Share Of The Time One Panel Takes", Range(0.1, 1)) = 0.45
        _Border ("Line Width (of the view's height)", Range(0, 0.03)) = 0.006
        _Grow ("Growth Of A Leaving Panel", Range(0, 1)) = 0.25
        _DotScale ("Dot Size Multiplier", Range(0.5, 8)) = 3
        _Wash ("Accent Wash On A Leaving Panel", Range(0, 1)) = 0.55

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

            #define MAX_PANELS 8

            struct appdata_t
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
                float4 screenPos : TEXCOORD1;
            };

            sampler2D _MainTex;
            fixed4 _Ink;
            fixed4 _Accent;
            float _Progress;
            float _LineIn;
            float _Fly;
            float _Border;
            float _Grow;
            float _DotScale;
            float _Wash;
            float _PanelCount;
            float4 _Panels[MAX_PANELS]; // xMin, yMin, xMax, yMax of each panel, in the picture's own 0..1
            float4 _Moves[MAX_PANELS];  // xy: how far it goes (in views), z: how far it turns (radians), w: when it leaves (0 first, 1 last)

            v2f vert(appdata_t v)
            {
                v2f OUT;
                OUT.vertex = UnityObjectToClipPos(v.vertex);
                OUT.color = v.color;
                OUT.uv = v.texcoord;
                OUT.screenPos = ComputeScreenPos(OUT.vertex);
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                float aspect = _ScreenParams.x / _ScreenParams.y;
                float lines = saturate(_Progress / _LineIn);
                float border = _Border * lines;
                float edgeSoft = 1.5 / _ScreenParams.y;

                // The first panel to leave is on top of the ones still lying there.
                float2 source = 0;
                float inside = -1, left = 0;
                for (int i = 0; i < MAX_PANELS; i++)
                {
                    if (i >= _PanelCount) break;
                    float4 panel = _Panels[i];
                    float4 move = _Moves[i];
                    float t = saturate((_Progress - _LineIn - move.w * (1.0 - _LineIn - _Fly)) / _Fly);
                    float gone = t * t; // it starts slowly and is thrown clear

                    float2 centre = (panel.xy + panel.zw) * 0.5;
                    float2 p = IN.uv - centre - move.xy * gone;
                    p.x *= aspect;
                    float s, c;
                    sincos(-move.z * gone, s, c);
                    p = float2(p.x * c - p.y * s, p.x * s + p.y * c) / (1.0 + _Grow * gone);

                    float2 halfSize = (panel.zw - panel.xy) * 0.5;
                    halfSize.x *= aspect;
                    float2 within = halfSize - abs(p);
                    float edge = min(within.x, within.y);
                    if (edge < 0.0) continue;

                    source = float2(p.x / aspect, p.y) + centre;
                    inside = edge;
                    left = t;
                    break;
                }
                if (inside < 0.0) return fixed4(0, 0, 0, 0);

                fixed4 color = tex2Dlod(_MainTex, float4(source, 0, 0));
                color.a = 1.0;
                // A panel on its way out takes the accent in dots, the way a struck figure does.
                float2 screenPx = IN.screenPos.xy / IN.screenPos.w * _ScreenParams.xy;
                half dots = ComicHalftone(screenPx / max(_DotScale, 0.01), saturate(left * left * 1.6) * _Wash);
                color.rgb = lerp(color.rgb, _Accent.rgb, dots * saturate(left * 1.5));
                // The ink line round the panel. A panel that has not started keeps a hard outer edge: no gap to its neighbour.
                color.rgb = lerp(color.rgb, _Ink.rgb, (1.0 - smoothstep(border, border + edgeSoft, inside)) * saturate(lines * 8.0));
                color.a *= left > 0.0 ? saturate(inside / edgeSoft) : 1.0;
                color.a *= 1.0 - smoothstep(0.8, 1.0, left);
                return color * IN.color;
            }
            ENDCG
        }
    }
}
