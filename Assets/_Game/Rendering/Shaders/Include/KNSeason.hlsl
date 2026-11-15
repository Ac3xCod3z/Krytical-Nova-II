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
// Part 2 (owner, 14 Nov 2026): drifts, shelter, trodden roads, ploughed paths, spring and summer.
float4 _KN_SnowField;      // x 1 / drift size (m), y drift amount, z ripple amount, w slope thinning
float4 _KN_SnowShelterK;   // x thinning under a canopy, y windward pile, z share of snow a road keeps, w sun-facing thaw now (0 unless thawing)
float4 _KN_SnowDepth;      // x the deepest snow may lie (m, the ceiling), y how far a path presses the raised snow
float4 _KN_SlushColor;     // rgb trodden snow (multiplies the snow colour)
float4 _KN_SnowShelterRect;// xy world XZ of the shelter map's corner, z 1 / its width (m), w 1 while there is one
TEXTURE2D(_KN_SnowShelter);// r under a canopy, g a windward pile, b a road
SAMPLER(sampler_KN_SnowShelter);
float4 _KN_SeasonSpring;   // x grass grown 0-1, y brown after the snow, z fresh green, w buds
float4 _KN_SeasonSummer;   // x blossom, y grass height at the end of Winter (share), z golden grass, w heat 0-1
float4 _KN_ThawGrass;      // rgb the brown of grass after the snow (multiply)
float4 _KN_SpringGreen;    // rgb the fresh green (multiply)
float4 _KN_BudColor;       // rgb pale buds
float4 _KN_GoldenGrass;    // rgb golden dry-spell grass (multiply)
float4 _KN_LeafBlossom[16];// per tree kind: rgb blossom colour, a 1 = it blossoms

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

// The shelter map here (r canopy, g pile, b road); 0 outside it or with none (Low).
float4 KNSnowShelter(float2 xz)
{
    float4 shelter = 0.0;
    [branch] if (_KN_SnowShelterRect.w >= 0.5)
    {
        float2 at = (xz - _KN_SnowShelterRect.xy) * _KN_SnowShelterRect.z;
        float2 toEdge = min(at, 1.0 - at);
        float edge = min(toEdge.x, toEdge.y);
        [branch] if (edge > 0.0)
            shelter = SAMPLE_TEXTURE2D_LOD(_KN_SnowShelter, sampler_KN_SnowShelter, at, 0) * saturate(edge * 10.0);
    }
    return shelter;
}

// How much a surface holds snow by its slope (owner 14 Nov: like real life): 1 flatter than the full angle, 0 steeper than the none
// angle, smooth between. Walls never.
float KNSnowSlope(float3 normalWS)
{
    float y = normalWS.y;
    return _KN_SnowShape.y > 0.0 ? saturate((y - _KN_SnowShape.x) * _KN_SnowShape.y) : saturate((y - 0.45) / 0.4);
}

// Where snow lies thicker and thinner (owner 14 Nov: the max is a ceiling, never a flat sheet): the slope rule, thinner on slopes, low
// wavy drifts and dips, ripples, thinner under canopies, piled against the windward side of walls, thin on trodden roads. 1 = the mean.
float KNSnowField(float3 positionWS, float3 normalWS, float4 shelter, float road)
{
    float slope = KNSnowSlope(normalWS);
    float2 xz = positionWS.xz;
    float drift = (KNSeasonNoise(xz * max(_KN_SnowField.x, 0.001)) - 0.5) * 2.0 * _KN_SnowField.y;
    float ripple = (KNSeasonNoise(xz * 1.3 + 17.0) - 0.5) * 2.0 * _KN_SnowField.z;
    float f = slope * lerp(1.0 - _KN_SnowField.w, 1.0, slope) * (1.0 + drift + ripple);
    f *= 1.0 - shelter.r * _KN_SnowShelterK.x;
    f *= lerp(1.0, _KN_SnowShelterK.z, road);
    f += shelter.g * _KN_SnowShelterK.y * slope;
    return max(f, 0.0);
}

// The snow on a surface: cover x how well it holds snow, broken by noise; bias > 0 whitens sooner (roofs). road 0-1 = trodden.
float KNSnowMaskAt(float3 positionWS, float3 normalWS, float bias, float4 shelter, float road)
{
    float cover = saturate(_KN_SeasonParams.x + bias * step(0.001, _KN_SeasonParams.x));
    float up = saturate(KNSnowField(positionWS, normalWS, shelter, road));
#if defined(UNIVERSAL_INPUT_INCLUDED)
    // a thaw takes the sun-facing patches first
    cover = saturate(cover - _KN_SnowShelterK.w * saturate(dot(normalWS, _MainLightPosition.xyz)));
#endif
    float n = KNSeasonNoise(positionWS.xz * 0.35) * 0.6 + KNSeasonNoise(positionWS.xz * 1.7) * 0.4;
    float soft = max(_KN_SeasonMisc.z, 0.01);
    // full cover reaches 1 everywhere it holds; a thin cover shows only in the low (noise) spots
    return smoothstep(-soft, soft, cover * up * 1.15 - n * (1.0 - cover * 0.85) * 0.9);
}

float KNSnowMask(float3 positionWS, float3 normalWS, float bias)
{
    float4 shelter = KNSnowShelter(positionWS.xz);
    return KNSnowMaskAt(positionWS, normalWS, bias, shelter, shelter.b);
}

// Metres of snow lying here (0 on walls and where it is thin): the mean depth x the field, never above the ceiling, pressed down where
// a body ploughed a path.
float KNSnowDepth(float3 positionWS, float3 normalWS)
{
    float4 shelter = KNSnowShelter(positionWS.xz);
    float ceiling = _KN_SnowDepth.x > 0.0 ? _KN_SnowDepth.x : 1.0;
    float depth = min(_KN_SnowShape.z * KNSnowField(positionWS, normalWS, shelter, shelter.b), ceiling);
    return depth * (1.0 - KNSnowTrack(positionWS.xz) * _KN_SnowDepth.y);
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
        float4 shelter = KNSnowShelter(positionWS.xz);
        float snow = KNSnowMaskAt(positionWS, normalWS, 0.0, shelter, shelter.b);
        float press = KNSnowTrack(positionWS.xz);
        float depth = _KN_SnowShape.z * snow * lerp(1.0, _KN_SnowShelterK.z, shelter.b);
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
        // a trodden road: packed, darker, slushy (owner 14 Nov: thinner, trodden roads)
        packed *= (half3)lerp(float3(1.0, 1.0, 1.0), _KN_SlushColor.rgb, shelter.b);
        albedo = lerp(albedo, packed, (half)(snow * (1.0 - through)));
        smoothness = lerp(smoothness, lerp(0.15h, 0.5h, (half)shelter.b), (half)snow);
    }
}

// Buildings and props (Comic Toon Lit): roofs, ledges and planter tops whiten first; walls never. The layer's thin edge shades a
// little, as a lip of snow does. A renderer whose rendering layer has bit 7 ("No Snow": interiors, characters) never gets any.
half3 KNSeasonSurface(half3 albedo, float3 normalWS, float3 positionWS, uint renderingLayer)
{
    half3 result = albedo;
    [branch] if (KNSeasonOn() && _KN_SeasonParams.x > 0.001 && (renderingLayer & 128u) == 0u)
    {
        // rendering layer bit 6 = a road or path (the season presenter tags them by material): trodden
        float4 shelter = KNSnowShelter(positionWS.xz);
        float road = (renderingLayer & 64u) != 0u ? 1.0 : 0.0;
        float snow = KNSnowMaskAt(positionWS, normalWS, _KN_SeasonParams.z * (1.0 - road), float4(shelter.r, 0.0, 0.0, 0.0), road);
        float lip = snow * (1.0 - snow) * 4.0;
        half3 colour = (half3)_KN_SnowColor.rgb * (half3)lerp(float3(1.0, 1.0, 1.0), _KN_SlushColor.rgb, road);
        result = lerp(albedo, colour * (half)(1.0 - 0.2 * lip), (half)snow);
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
            // spring: pale buds on the twigs, then blossom on the kinds that flower (clump by clump), before the leaves are out
            result = lerp(result, (half3)_KN_BudColor.rgb * (lum * 1.8h), leaf * (half)(_KN_SeasonSpring.w * 0.6));
            float4 bloom = _KN_LeafBlossom[kind];
            [branch] if (bloom.a > 0.5 && _KN_SeasonSummer.x > 0.001)
            {
                float clump = smoothstep(0.35, 0.6, KNSeasonNoise(positionWS.xz * 3.1 + positionWS.y * 2.3));
                result = lerp(result, (half3)bloom.rgb, leaf * (half)(_KN_SeasonSummer.x * clump));
            }
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
        // spring: brown and flattened after the snow, then a fresh green; summer: golden on long dry spells
        result *= lerp(float3(1.0, 1.0, 1.0), _KN_ThawGrass.rgb, _KN_SeasonSpring.y);
        result *= lerp(float3(1.0, 1.0, 1.0), _KN_SpringGreen.rgb, _KN_SeasonSpring.z);
        result *= lerp(float3(1.0, 1.0, 1.0), _KN_GoldenGrass.rgb, _KN_SeasonSummer.z * (0.55 + 0.45 * height01));
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
    // spring: short after the winter, growing back to full height (1 = full; the presenter publishes 1 with no season)
    float height = KNSeasonOn() ? lerp(saturate(_KN_SeasonSummer.y), 1.0, saturate(_KN_SeasonSpring.x)) : 1.0;
    [branch] if (KNSeasonOn() && _KN_SeasonParams.x > 0.001)
    {
        float open = 1.0 - KNSnowTrack(rootWS.xz);
        [branch] if (_KN_SnowShape.w > 0.0)
        {
            float depth = KNSnowDepth(rootWS, float3(0.0, 1.0, 0.0)) * _KN_SnowShape.w * open;
            height = bladeHeight <= depth * 0.95 ? 0.0 : height;
        }
        else if (_KN_SeasonParams.x > 0.5)
        {
            float cover = _KN_SeasonParams.x * open;
            height *= lerp(1.0, _KN_SeasonMisc.w, saturate((cover - 0.5) * 2.0));
        }
    }
    return height;
}

#endif
