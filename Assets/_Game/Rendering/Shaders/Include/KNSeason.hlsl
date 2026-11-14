// Seasons (owner's DAY list item 21): the season's look and the snow cover, as a few shader globals that SeasonPresenter
// (World) publishes on each client. The ground (KryticalNova/Terrain Lit), buildings (Comic Toon Lit), grass (GrassBlades)
// and trees (Foliage Wind) include this file: no extra pass, no keyword, ALU only plus one texel of the snow track map.
//
// Safe default (build 12 black-sky lesson): until a presenter publishes, _KN_SeasonParams.w is 0 and every function here
// returns exactly what it was given, so a missing or broken presenter means "no season", never a black or white world.
#ifndef KN_SEASON_INCLUDED
#define KN_SEASON_INCLUDED

float4 _KN_SeasonParams;   // x snow cover 0-1, y frost 0-1, z roof bias, w 1 while a presenter publishes
float4 _KN_SeasonGrass;    // rgb grass colour multiply, a how pale the tips go (frost)
float4 _KN_SeasonLeafFrom; // rgb leaf colour blended from, a how strongly (0 = the art's own green)
float4 _KN_SeasonLeafTo;   // rgb leaf colour blended to, a how strongly
float4 _KN_SeasonMisc;     // x leaf blend 0-1, y per-tree spread of the blend, z snow edge softness, w grass flatten under deep snow
float4 _KN_SnowColor;      // rgb flat comic snow colour, a track rim darkness
float4 _KN_SnowTrackRect;  // xy world XZ of the track map's corner, z 1 / its width in metres, w 1 while there is a map
TEXTURE2D(_KN_SnowTrack);  // r how pressed the snow is (footprints and trails), 0 untouched
SAMPLER(sampler_KN_SnowTrack);

bool KNSeasonOn() { return _KN_SeasonParams.w > 0.5; }

// Cheap world-space value noise (no texture): a ragged snow edge, and low spots fill first.
float KNSeasonHash(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
}

float KNSeasonNoise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = KNSeasonHash(i), b = KNSeasonHash(i + float2(1, 0));
    float c = KNSeasonHash(i + float2(0, 1)), d = KNSeasonHash(i + float2(1, 1));
    return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
}

// How pressed the snow is here (0 untouched, 1 a fresh footprint). 0 outside the map or with none.
float KNSnowTrack(float2 xz)
{
    [branch] if (_KN_SnowTrackRect.w < 0.5) return 0.0;
    float2 at = (xz - _KN_SnowTrackRect.xy) * _KN_SnowTrackRect.z;
    float2 toEdge = min(at, 1.0 - at);
    if (min(toEdge.x, toEdge.y) <= 0.0) return 0.0;
    return SAMPLE_TEXTURE2D_LOD(_KN_SnowTrack, sampler_KN_SnowTrack, at, 0).r * saturate(min(toEdge.x, toEdge.y) * 12.0);
}

// The snow on a surface: cover x how much it faces up, broken by noise; bias > 0 whitens sooner (roofs).
float KNSnowMask(float3 positionWS, float3 normalWS, float bias)
{
    float cover = saturate(_KN_SeasonParams.x + bias * step(0.001, _KN_SeasonParams.x));
    float up = saturate((normalWS.y - 0.45) / 0.4);
    float n = KNSeasonNoise(positionWS.xz * 0.35) * 0.6 + KNSeasonNoise(positionWS.xz * 1.7) * 0.4;
    float soft = max(_KN_SeasonMisc.z, 0.01);
    // full cover reaches 1 everywhere it faces up; a thin cover shows only in the low (noise) spots
    return smoothstep(-soft, soft, cover * up * 1.15 - n * (1.0 - cover * 0.85) * 0.9);
}

// The ground: albedo to flat snow (smoother, slightly brighter), pushed off where feet pressed it, with a thin darker rim.
void KNSeasonGround(inout half3 albedo, inout half smoothness, float3 normalWS, float3 positionWS)
{
    [branch] if (!KNSeasonOn() || _KN_SeasonParams.x <= 0.001) return;
    float snow = KNSnowMask(positionWS, normalWS, 0.0);
    float press = KNSnowTrack(positionWS.xz);
    float rim = saturate(press * 4.0) * (1.0 - press) * _KN_SnowColor.a;
    snow *= 1.0 - press;
    albedo = lerp(albedo, (half3)_KN_SnowColor.rgb * (1.0 - rim), (half)snow);
    smoothness = lerp(smoothness, 0.15h, (half)snow);
}

// Buildings and props (Comic Toon Lit): roofs and ledges whiten first; walls never. A renderer whose rendering layer has
// bit 7 ("No Snow": interiors, characters) never gets any.
half3 KNSeasonSurface(half3 albedo, float3 normalWS, float3 positionWS, uint renderingLayer)
{
    [branch] if (!KNSeasonOn() || _KN_SeasonParams.x <= 0.001 || (renderingLayer & 128u) != 0u) return albedo;
    float snow = KNSnowMask(positionWS, normalWS, _KN_SeasonParams.z);
    return lerp(albedo, (half3)_KN_SnowColor.rgb, (half)snow);
}

// Trees and bushes (Foliage Wind): the green of the shared atlas turns with the season, each tree in its own time;
// frost and snow settle on the up-facing part of the canopy. pivotWS = the plant's root (per-tree hash).
half3 KNSeasonFoliage(half3 albedo, float3 normalWS, float3 positionWS, float3 pivotWS)
{
    [branch] if (!KNSeasonOn()) return albedo;
    // the leaves: how green this texel is (trunks and rocks in the same atlas stay as they are)
    half leaf = saturate((albedo.g - max(albedo.r, albedo.b)) * 6.0h);
    float h = KNSeasonHash(pivotWS.xz * 0.173 + 7.1);
    float spread = _KN_SeasonMisc.y;
    float b = saturate(_KN_SeasonMisc.x * (1.0 + spread) - h * spread);
    float4 leafTint = lerp(_KN_SeasonLeafFrom, _KN_SeasonLeafTo, b);
    half lum = dot(albedo, half3(0.3h, 0.59h, 0.11h));
    half3 turned = (half3)leafTint.rgb * (lum * 1.8h);
    albedo = lerp(albedo, turned, leaf * (half)leafTint.a);
    // frost on the top of the canopy, then snow as it deepens
    float up = saturate(normalWS.y * 0.5 + 0.5);
    float frost = _KN_SeasonParams.y * up * up;
    float snow = KNSnowMask(positionWS, normalWS, 0.05) * 0.85;
    return lerp(albedo, (half3)_KN_SnowColor.rgb, (half)saturate(max(frost * 0.6, snow)));
}

// Grass blades: the season's colour, pale tips in frost, and snow-white blades under cover.
float3 KNSeasonGrassColour(float3 col, float height01, float3 rootWS)
{
    [branch] if (!KNSeasonOn()) return col;
    col *= _KN_SeasonGrass.rgb;
    col = lerp(col, _KN_SnowColor.rgb, _KN_SeasonGrass.a * height01 * height01);
    float cover = _KN_SeasonParams.x * (1.0 - KNSnowTrack(rootWS.xz));
    return lerp(col, _KN_SnowColor.rgb, saturate(cover * 1.4 - 0.3) * (0.4 + 0.6 * height01));
}

// Grass under deep snow is pressed down (1 = standing, smaller = flatter); a track lets it stand again.
float KNSeasonGrassHeight(float3 rootWS)
{
    [branch] if (!KNSeasonOn() || _KN_SeasonParams.x <= 0.5) return 1.0;
    float cover = _KN_SeasonParams.x * (1.0 - KNSnowTrack(rootWS.xz));
    return lerp(1.0, _KN_SeasonMisc.w, saturate((cover - 0.5) * 2.0));
}

#endif
