// Seasons (owner's DAY list item 21): the season's look and the snow, as a few shader globals that SeasonPresenter (World)
// publishes on each client. The ground (KryticalNova/Terrain Lit), buildings (Comic Toon Lit), grass (GrassBlades) and trees
// (Foliage Wind) include this file: no extra pass, no keyword, ALU only plus a texel or three of the footprint map.
//
// Owner, 14 Nov 2026: snow settles only on surfaces flat enough to hold it (roofs, planters, canopies, the ground), it has real
// depth (the ground rises, grass shorter than the snow is buried), feet leave prints, and through Autumn every tree kind turns its
// own colour, the grass goes dull, and the canopies thin until they are almost bare before Winter.
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
float4 _KN_SnowColor;      // rgb flat comic snow colour, a footprint rim strength
float4 _KN_SnowShape;      // x cos(the steepest angle snow holds on), y 1 / (cos(full) - cos(none)), z depth now (m), w raised share
float4 _KN_SeasonFoliage;  // x leaves turned 0-1, y canopy left 1 .. bare, z autumn dullness 0-1, w per-tree spread
float4 _KN_SeasonDull;     // rgb the dull, almost yellow autumn tone (multiply)
float4 _KN_LeafPeak[16];   // per tree kind (rendering layer bits 8-11): rgb fall colour at its height, a 1 = evergreen
float4 _KN_LeafLate[16];   // per tree kind: rgb the colour it fades to as the leaves fall
float4 _KN_SnowTrackRect;  // xy world XZ of the footprint map's corner, z 1 / its width in metres, w 1 while there is a map
TEXTURE2D(_KN_SnowTrack);  // r how pressed the snow is (footprints), 0 untouched
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

// How pressed the snow is here (0 untouched, 1 a fresh print). 0 outside the map or with none.
float KNSnowTrack(float2 xz)
{
    float press = 0.0;
    [branch] if (_KN_SnowTrackRect.w >= 0.5)
    {
        float2 at = (xz - _KN_SnowTrackRect.xy) * _KN_SnowTrackRect.z;
        float2 toEdge = min(at, 1.0 - at);
        float edge = min(toEdge.x, toEdge.y);
        [branch] if (edge > 0.0)
            press = SAMPLE_TEXTURE2D_LOD(_KN_SnowTrack, sampler_KN_SnowTrack, at, 0).r * saturate(edge * 12.0);
    }
    return press;
}

// How much a surface holds snow by its slope (owner 14 Nov: like real life): 1 flatter than the full angle, 0 steeper than the none
// angle, smooth between. Walls never.
float KNSnowSlope(float3 normalWS)
{
    float y = normalWS.y;
    return _KN_SnowShape.y > 0.0 ? saturate((y - _KN_SnowShape.x) * _KN_SnowShape.y) : saturate((y - 0.45) / 0.4);
}

// The snow on a surface: cover x how well it holds snow, broken by noise; bias > 0 whitens sooner (roofs).
float KNSnowMask(float3 positionWS, float3 normalWS, float bias)
{
    float cover = saturate(_KN_SeasonParams.x + bias * step(0.001, _KN_SeasonParams.x));
    float up = KNSnowSlope(normalWS);
    float n = KNSeasonNoise(positionWS.xz * 0.35) * 0.6 + KNSeasonNoise(positionWS.xz * 1.7) * 0.4;
    float soft = max(_KN_SeasonMisc.z, 0.01);
    // full cover reaches 1 everywhere it holds; a thin cover shows only in the low (noise) spots
    return smoothstep(-soft, soft, cover * up * 1.15 - n * (1.0 - cover * 0.85) * 0.9);
}

// Metres of snow lying here (0 on walls and where it is thin).
float KNSnowDepth(float3 positionWS, float3 normalWS)
{
    float n = KNSeasonNoise(positionWS.xz * 0.21);
    return _KN_SnowShape.z * KNSnowSlope(normalWS) * (0.8 + 0.4 * n);
}

// The snow's depth as a real raised layer: an object-space vertex moved up by the depth lying on it (World Effects scales it; Low = 0).
// Every pass of a shader that lifts must call it alike, so depth, shadows and colour agree.
float3 KNSnowLiftOS(float3 positionOS, float3 normalOS)
{
    float3 result = positionOS;
    [branch] if (KNSeasonOn() && _KN_SnowShape.z * _KN_SnowShape.w > 0.001)
    {
        float3 positionWS = TransformObjectToWorld(positionOS);
        float3 normalWS = normalize(TransformObjectToWorldNormal(normalOS));
        float lift = KNSnowDepth(positionWS, normalWS) * _KN_SnowShape.w;
        result = TransformWorldToObject(positionWS + float3(0.0, lift, 0.0));
    }
    return result;
}

// The same for a world-space vertex (shaders that work in world space, Foliage Wind).
float3 KNSnowLiftWS(float3 positionWS, float3 normalWS)
{
    float3 result = positionWS;
    [branch] if (KNSeasonOn() && _KN_SnowShape.z * _KN_SnowShape.w > 0.001)
        result.y += KNSnowDepth(positionWS, normalWS) * _KN_SnowShape.w;
    return result;
}

// The ground: albedo to snow (smoother), with footprints: in deep snow a print is a shaded dent of packed snow with a bright pushed-up
// rim, in a thin cover the ground shows through it.
void KNSeasonGround(inout half3 albedo, inout half smoothness, float3 normalWS, float3 positionWS)
{
    [branch] if (KNSeasonOn() && _KN_SeasonParams.x > 0.001)
    {
        float snow = KNSnowMask(positionWS, normalWS, 0.0);
        float press = KNSnowTrack(positionWS.xz);
        float depth = _KN_SnowShape.z * snow;
        float deep = saturate(depth / 0.06);
        float through = press * (1.0 - deep);
        // the dent's walls: lit from the sun's side, shaded on the far side (a cheap slope from three samples)
        float e = 0.03;
        float2 slope = float2(KNSnowTrack(positionWS.xz + float2(e, 0.0)) - press, KNSnowTrack(positionWS.xz + float2(0.0, e)) - press) / e;
        float side = 0.0;
    #if defined(UNIVERSAL_INPUT_INCLUDED)
        side = clamp(dot(slope, normalize(_MainLightPosition.xz + 1e-4)) * 0.18, -0.45, 0.45);
    #endif
        float rim = saturate(press * 4.0) * (1.0 - press) * _KN_SnowColor.a;
        // packed snow in the print reads clearly darker and bluer than the fresh snow round it, its walls lit and shaded
        half3 packed = (half3)_KN_SnowColor.rgb * (half3)lerp(float3(1.0, 1.0, 1.0), float3(0.5, 0.57, 0.72), saturate(press * 1.4) * deep);
        packed *= (half)saturate(1.0 + side + rim * 0.8);
        albedo = lerp(albedo, packed, (half)(snow * (1.0 - through)));
        smoothness = lerp(smoothness, 0.15h, (half)snow);
    }
}

// Buildings and props (Comic Toon Lit): roofs, ledges and planter tops whiten first; walls never. The layer's thin edge shades a
// little, as a lip of snow does. A renderer whose rendering layer has bit 7 ("No Snow": interiors, characters) never gets any.
half3 KNSeasonSurface(half3 albedo, float3 normalWS, float3 positionWS, uint renderingLayer)
{
    half3 result = albedo;
    [branch] if (KNSeasonOn() && _KN_SeasonParams.x > 0.001 && (renderingLayer & 128u) == 0u)
    {
        float snow = KNSnowMask(positionWS, normalWS, _KN_SeasonParams.z);
        float lip = snow * (1.0 - snow) * 4.0;
        result = lerp(albedo, (half3)_KN_SnowColor.rgb * (half)(1.0 - 0.2 * lip), (half)snow);
    }
    return result;
}

// How green this foliage texel is (leaves, not trunks or rocks in the same atlas).
half KNSeasonLeafness(half3 albedo) { return saturate((albedo.g - max(albedo.r, albedo.b)) * 6.0h); }

uint KNSeasonLeafKind(uint renderingLayer) { return (renderingLayer >> 8) & 15u; }

// True where a leaf has fallen (late Autumn, Winter, early Spring): each tree thins in its own time, clump by clump. Evergreens never.
// albedo = the texture's own colour (before any season tint). Call it in every pass that clips, alike.
bool KNSeasonLeafGone(half3 albedo, float3 positionWS, float3 pivotWS, uint renderingLayer)
{
    bool gone = false;
    [branch] if (KNSeasonOn() && _KN_SeasonFoliage.y < 0.999)
    {
        float4 peak = _KN_LeafPeak[KNSeasonLeafKind(renderingLayer)];
        if (peak.a < 0.5 && KNSeasonLeafness(albedo) > 0.4)
        {
            float h = KNSeasonHash(pivotWS.xz * 0.173 + 7.1);
            float canopy = saturate(_KN_SeasonFoliage.y + (h - 0.5) * _KN_SeasonFoliage.w * 0.5);
            float n = KNSeasonNoise(positionWS.xz * 2.7 + positionWS.y * 1.9) * 0.7 + KNSeasonNoise(positionWS.xy * 6.1 + positionWS.z) * 0.3;
            gone = n > canopy;
        }
    }
    return gone;
}

// Trees and bushes (Foliage Wind): the season's tint; through Autumn every kind turns its own colour (each tree its own mix and
// time) and the leaves go dull; frost and snow settle on the up-facing part of the canopy. pivotWS = the plant's root.
half3 KNSeasonFoliage(half3 albedo, float3 normalWS, float3 positionWS, float3 pivotWS, uint renderingLayer)
{
    half3 result = albedo;
    [branch] if (KNSeasonOn())
    {
        half leaf = KNSeasonLeafness(albedo);
        float h = KNSeasonHash(pivotWS.xz * 0.173 + 7.1);
        float h2 = KNSeasonHash(pivotWS.zx * 0.311 + 3.7);
        float spread = _KN_SeasonMisc.y;
        float b = saturate(_KN_SeasonMisc.x * (1.0 + spread) - h * spread);
        float4 leafTint = lerp(_KN_SeasonLeafFrom, _KN_SeasonLeafTo, b);
        half lum = dot(albedo, half3(0.3h, 0.59h, 0.11h));
        result = lerp(albedo, (half3)leafTint.rgb * (lum * 1.8h), leaf * (half)leafTint.a);

        uint kind = KNSeasonLeafKind(renderingLayer);
        float4 peak = _KN_LeafPeak[kind];
        float4 late = _KN_LeafLate[kind];
        bool evergreen = peak.a > 0.5;
        if (!evergreen)
        {
            float fs = _KN_SeasonFoliage.w;
            float turn = saturate(_KN_SeasonFoliage.x * (1.0 + fs) - h * fs);
            float falling = saturate((1.0 - _KN_SeasonFoliage.y) * 1.5);
            float3 fall = lerp(peak.rgb, late.rgb, saturate(h2 * 0.55 + falling * 0.6));
            fall *= 0.82 + 0.36 * KNSeasonHash(pivotWS.xz * 0.57 + 1.3);
            result = lerp(result, (half3)fall * (lum * 1.9h), leaf * (half)turn);
        }
        result *= (half3)lerp(float3(1.0, 1.0, 1.0), _KN_SeasonDull.rgb, _KN_SeasonFoliage.z * leaf * (evergreen ? 0.3 : 1.0));

        // frost on the top of the canopy, then snow as it deepens
        float up = saturate(normalWS.y * 0.5 + 0.5);
        float frost = _KN_SeasonParams.y * up * up;
        float snow = KNSnowMask(positionWS, normalWS, 0.05) * 0.85;
        result = lerp(result, (half3)_KN_SnowColor.rgb, (half)saturate(max(frost * 0.6, snow)));
    }
    return result;
}

// Grass blades: the season's colour, dull through Autumn, pale tips in frost, a dusting of snow on blades that stand out of it.
float3 KNSeasonGrassColour(float3 col, float height01, float3 rootWS)
{
    float3 result = col;
    [branch] if (KNSeasonOn())
    {
        result *= _KN_SeasonGrass.rgb;
        result *= lerp(float3(1.0, 1.0, 1.0), _KN_SeasonDull.rgb, _KN_SeasonFoliage.z);
        result = lerp(result, _KN_SnowColor.rgb, _KN_SeasonGrass.a * height01 * height01);
        float cover = _KN_SeasonParams.x * (1.0 - KNSnowTrack(rootWS.xz));
        float dusting = _KN_SnowShape.w > 0.0 ? 0.35 : 1.0;   // raised snow buries the base; the tips only carry a dusting
        result = lerp(result, _KN_SnowColor.rgb, saturate(cover * 1.4 - 0.3) * (0.4 + 0.6 * height01) * dusting);
    }
    return result;
}

// Grass under snow: with the snow raised, a blade shorter than the depth here is buried (drawn as nothing) and taller ones stick out
// of it; with it flat (Low), deep cover presses the grass down. A footprint lets it stand again. Returns the blade's height scale.
float KNSeasonGrassHeight(float3 rootWS, float bladeHeight)
{
    float height = 1.0;
    [branch] if (KNSeasonOn() && _KN_SeasonParams.x > 0.001)
    {
        float open = 1.0 - KNSnowTrack(rootWS.xz);
        [branch] if (_KN_SnowShape.w > 0.0)
        {
            float depth = KNSnowDepth(rootWS, float3(0.0, 1.0, 0.0)) * _KN_SnowShape.w * open;
            height = bladeHeight <= depth * 0.95 ? 0.0 : 1.0;
        }
        else if (_KN_SeasonParams.x > 0.5)
        {
            float cover = _KN_SeasonParams.x * open;
            height = lerp(1.0, _KN_SeasonMisc.w, saturate((cover - 0.5) * 2.0));
        }
    }
    return height;
}

#endif
