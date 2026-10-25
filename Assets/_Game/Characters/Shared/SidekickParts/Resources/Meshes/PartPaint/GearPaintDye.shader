// Redraws a painted gear part's paint in a colourway (GearPaint.MaterialFor): in each dye region the colourway's colour
// takes the place of the paint's at the paint's own brightness, so the painted shading and detail stay.
Shader "Hidden/KN/GearPaintDye"
{
    Properties
    {
        _MainTex ("Paint", 2D) = "white" {}
        _Regions ("Dye regions (R main, G second, B metal, A leather)", 2D) = "black" {}
        _RegionBrightness ("Mean brightness per region", Vector) = (0.5, 0.5, 0.5, 0.5)
        _ColourA ("Main cloth", Color) = (1, 1, 1, 1)
        _ColourB ("Second cloth", Color) = (1, 1, 1, 1)
        _ColourC ("Plate and metal", Color) = (1, 1, 1, 1)
        _ColourD ("Leather and trim", Color) = (1, 1, 1, 1)
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            sampler2D _Regions;
            float4 _RegionBrightness;
            float4 _ColourA, _ColourB, _ColourC, _ColourD;

            float Brightness(float3 c) { return dot(c, float3(0.2126, 0.7152, 0.0722)); }

            // The colourway's colour at the brightness the paint has here, relative to the region's own mean.
            float3 Dye(float3 paint, float3 colour, float mean)
            {
                float scale = Brightness(paint) / max(mean, 0.02);
                return saturate(colour * scale);
            }

            fixed4 frag(v2f_img i) : SV_Target
            {
                float4 paint = tex2D(_MainTex, i.uv);
                float4 w = tex2D(_Regions, i.uv);
                float total = w.r + w.g + w.b + w.a;
                if (total <= 0.001) return paint;
                float3 dyed = Dye(paint.rgb, _ColourA.rgb, _RegionBrightness.x) * w.r
                            + Dye(paint.rgb, _ColourB.rgb, _RegionBrightness.y) * w.g
                            + Dye(paint.rgb, _ColourC.rgb, _RegionBrightness.z) * w.b
                            + Dye(paint.rgb, _ColourD.rgb, _RegionBrightness.w) * w.a;
                dyed /= total;
                return float4(lerp(paint.rgb, dyed, saturate(total)), paint.a);
            }
            ENDCG
        }
    }
}
