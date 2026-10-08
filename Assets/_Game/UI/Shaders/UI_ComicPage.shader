// A page of the menus' comic book (MenuBook): the picture of a menu screen on its face, bare paper on its back.
// Where the page turns away from the reader it is shaded the comic way, with Ben-Day dots on the shared screen grid
// rather than a gradient. Unlit: the book is drawn by its own camera and needs no light.
Shader "KryticalNova/UI/Comic Page"
{
    Properties
    {
        _MainTex ("Picture", 2D) = "white" {}
        _Picture ("Shows The Picture (0 = bare paper)", Range(0, 1)) = 1
        _Paper ("Paper", Color) = (0.957, 0.933, 0.875, 1)
        _Ink ("Ink", Color) = (0.078, 0.071, 0.169, 1)
        _Tint ("Dot Colour On Bare Paper", Color) = (0.298, 0.322, 0.749, 1)
        _Shade ("Shade Where The Page Turns Away", Range(0, 1)) = 0.7
        _Flat ("Lean That Still Counts As Flat", Range(0, 0.2)) = 0.06
        _Rest ("Dots On Bare Paper At Rest", Range(0, 1)) = 0.12
        _DotScale ("Dot Size Multiplier", Range(0.5, 8)) = 2
        _Border ("Ink Line Round The Page (share of its height)", Range(0, 0.05)) = 0.008
        _Aspect ("Page Width Over Height", Float) = 1.7778
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "Page"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/_Game/Rendering/Shaders/Comic/ComicHalftone.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Paper;
                half4 _Ink;
                half4 _Tint;
                half _Picture;
                half _Shade;
                half _Flat;
                half _Rest;
                float _DotScale;
                float _Border;
                float _Aspect;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                // From the camera, not from the world's origin: the book stands far off, where a world position
                // is too coarse to tell the slope of the paper between two pixels.
                float3 fromEye : TEXCOORD1;
                float4 screenPos : TEXCOORD2;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.fromEye = positionWS - _WorldSpaceCameraPos;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.uv = input.uv;
                output.screenPos = ComputeScreenPos(output.positionCS);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // The page is bent every frame by the book and its stored normals are not: read the slope of the
                // surface from the picture itself.
                float3 normal = normalize(cross(ddy(input.fromEye), ddx(input.fromEye)));
                float3 toEye = IsPerspectiveProjection() ? normalize(-input.fromEye) : -GetViewForwardDir();
                half facing = abs(dot(normal, toEye));
                // The book's camera sees the corners of a flat page at a slant: that much lean is not a curl.
                half away = saturate((1.0 - facing - _Flat) * 1.45);

                float2 pixel = input.screenPos.xy / input.screenPos.w * _ScreenParams.xy / max(_DotScale, 0.01);

                half3 picture = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv * _MainTex_ST.xy + _MainTex_ST.zw).rgb;
                // Face: the screen as it was, darkening into ink dots as it curls away.
                half3 face = lerp(picture, picture * _Ink.rgb * 1.6, ComicHalftone(pixel, away * _Shade) * 0.85);
                face *= 1.0 - away * _Shade * 0.25;

                // Back: paper, a light field of dots at rest that thickens where the page curls.
                half3 back = lerp(_Paper.rgb, _Tint.rgb, ComicHalftone(pixel, saturate(_Rest + away * _Shade)));
                float2 edge = min(input.uv, 1.0 - input.uv);
                edge.x *= _Aspect;
                half rule = step(min(edge.x, edge.y), _Border);
                back = lerp(back, _Ink.rgb, rule);

                return half4(lerp(back, face, _Picture), 1.0);
            }
            ENDHLSL
        }
    }
}
