// The season on the town's Synty materials (item 21 part 3, owner's option A): the Custom Function node of the KN copies of
// Synty's Generic_Basic and Polygon_Triplanar graphs (KryticalNova/Synty/...) calls this just before Base Color. A renderer whose
// rendering layer carries a tree kind (bits 8-11; the season presenter stamps the town's Synty trees by name at runtime) takes the
// trees' look (each kind's fall colour, buds and blossom, frost and snow on the canopy top); everything else takes the buildings'
// (snow on roofs and ledges by slope, drifts, trodden slushy roads, footprints and ploughed paths; walls never; bit 7 "No Snow"
// never). Colour only: no vertex lift on Synty meshes, no clip. Until a presenter publishes it returns the colour it was given.
#ifndef KN_SEASON_SYNTY_INCLUDED
#define KN_SEASON_SYNTY_INCLUDED

#include "Assets/_Game/Rendering/Shaders/Include/KNSeason.hlsl"

void KNSeasonSynty_float(float3 Albedo, float3 NormalWS, float3 PositionWS, out float3 Out)
{
#if defined(SHADERGRAPH_PREVIEW)
    Out = Albedo;
#else
    uint layer = asuint(unity_RenderingLayer.x);
    float3 pivotWS = GetObjectToWorldMatrix()._m03_m13_m23;
    float3 n = normalize(NormalWS);
    half3 c = (half3)Albedo;
    [branch] if (KNSeasonLeafKind(layer) != 0u)
        c = KNSeasonFoliage(c, n, PositionWS, pivotWS, layer);
    else
        c = KNSeasonSurface(c, n, PositionWS, layer);
    Out = (float3)c;
#endif
}

void KNSeasonSynty_half(half3 Albedo, half3 NormalWS, float3 PositionWS, out half3 Out)
{
    float3 o;
    KNSeasonSynty_float((float3)Albedo, (float3)NormalWS, PositionWS, o);
    Out = (half3)o;
}

#endif
