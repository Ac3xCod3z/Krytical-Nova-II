// The season on the town's Synty materials (item 21 part 3, owner's option A): the Custom Function node of the KN copies of
// Synty's Generic_Basic and Polygon_Triplanar graphs (KryticalNova/Synty/...) calls this just before Base Color. A renderer whose
// rendering layer carries a tree kind (bits 8-11; the season presenter stamps the town's Synty trees by name at runtime) takes the
// trees' look (each kind's fall colour, buds and blossom, frost and snow on the canopy top); everything else takes the buildings'
// (snow on roofs and ledges by slope, drifts, trodden slushy roads, footprints and ploughed paths; walls never; bit 7 "No Snow"
// never). Colour here, no vertex lift on Synty meshes; fallen leaves clip through KNSeasonSyntyAlpha on a graph that alpha-clips
// (Generic_Basic) and turn dead-leaf brown on one that does not. Until a presenter publishes it returns the colour it was given.
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
    {
        // no clip on Synty's graph: a fallen clump turns the dead-leaf brown of its kind instead, so the canopy reads thinner
        bool gone = KNSeasonLeafGone(c, PositionWS, pivotWS, layer);
        c = KNSeasonFoliage(c, n, PositionWS, pivotWS, layer);
        if (gone) c = (half3)_KN_LeafLate[KNSeasonLeafKind(layer)].rgb * 0.45h;
    }
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

// Owner 15 Nov (T2): the graph's Alpha goes through this, so a fallen leaf is clipped in every pass (colour, shadows, depth) and a tree
// thins clump by clump to bare, then leafs out again. Albedo = the texture's own colour, as the colour node gets it. -1 clips at any
// threshold; on an opaque surface the alpha is not drawn, so the rest of the tree is unchanged.
void KNSeasonSyntyAlpha_float(float Alpha, float3 Albedo, float3 PositionWS, out float Out)
{
#if defined(SHADERGRAPH_PREVIEW)
    Out = Alpha;
#else
    uint layer = asuint(unity_RenderingLayer.x);
    Out = Alpha;
    [branch] if (KNSeasonLeafKind(layer) != 0u)
    {
        float3 pivotWS = GetObjectToWorldMatrix()._m03_m13_m23;
        if (KNSeasonLeafGone((half3)Albedo, PositionWS, pivotWS, layer)) Out = -1.0;
    }
#endif
}

void KNSeasonSyntyAlpha_half(half Alpha, half3 Albedo, float3 PositionWS, out half Out)
{
    float o;
    KNSeasonSyntyAlpha_float((float)Alpha, (float3)Albedo, PositionWS, o);
    Out = (half)o;
}

#endif
