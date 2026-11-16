// Shared by GrassGenerate.compute (writes) and GrassBlades.shader (reads). Keep in sync with
// KryticalNova.World.Grass.GrassRenderer.InstanceStride (32 bytes).
#ifndef KN_GRASS_COMMON_INCLUDED
#define KN_GRASS_COMMON_INCLUDED

struct GrassInstance
{
    float4 posRot;   // xyz root position (world), w yaw (radians)
    float3 size;     // x uniform scale (height / mesh height), y width multiplier, z random 0-1
    uint   packed;   // bits 24-31 moisture, 16-23 appear-at (soft fade), 0-15 debug colour (rgb565, 0 = none)
};

// ---- Soft fade (grass plan step 2). Each blade keeps a stable rank from its cell hash. Its "appear-at" is the distance
// LOD share at which it shows: rank / (density x type density). The vertex shader grows it from zero height to full
// as the LOD passes that point, so no blade ever blinks on or off between frames. Band 0 = the old hard cut.
#define KN_GRASS_APPEAR_SCALE 127.5   // appear-at 0-2 in 8 bits

// The distance LOD of a pass at camDist (the same in the generator and the blades). pass 0 near, 1 far tufts, 2 fill.
// lodP = (full to, middle to, keep middle, keep far); fadeP = (hand-off start, near end, far end, far keep at end).
float KNGrassLod(int passIndex, float camDist, float4 lodP, float4 fadeP, float passMax)
{
    float handOff = smoothstep(fadeP.x, fadeP.y, camDist);
    if (passIndex == 1) return handOff * lerp(1.0, fadeP.w, smoothstep(fadeP.y, fadeP.z, camDist));
    if (passIndex == 2) return 1.0 - smoothstep(passMax * 0.6, passMax, camDist);
    return (camDist < lodP.x ? 1.0
        : camDist < lodP.y ? lerp(1.0, lodP.z, smoothstep(lodP.x, lodP.y, camDist))
        : lerp(lodP.z, lodP.w, smoothstep(lodP.y, fadeP.y, camDist))) * (1.0 - handOff);
}

// How grown a blade is (0-1) at this LOD. Centred on its appear-at, band wide (relative), so on average as much grass
// shows as with the old cut. The generator keeps a blade exactly while this is above zero.
float KNGrassGrowth(float lod, float appearAt, float band)
{
    float start = appearAt * (1.0 - 0.5 * band);
    if (band <= 0.0) return lod > start ? 1.0 : 0.0;
    return saturate((lod - start) / max(appearAt * band, 1e-4));
}

// Kept lawns (world XZ rectangles: xmin, zmin, xmax, zmax). Grass fills them and is cut off straight at their edges.
#define KN_MAX_LAWN_RECTS 8
float4 _KN_LawnRects[KN_MAX_LAWN_RECTS];
int    _KN_LawnRectCount;

// Distance from p to the nearest lawn edge: negative inside a lawn, positive outside every lawn.
float KNLawnDistance(float2 p, out int lawn)
{
    float best = 1e6;
    lawn = -1;
    for (int i = 0; i < _KN_LawnRectCount; i++)
    {
        float4 r = _KN_LawnRects[i];
        float2 q = abs(p - (r.xy + r.zw) * 0.5) - (r.zw - r.xy) * 0.5;
        float d = length(max(q, 0.0)) + min(max(q.x, q.y), 0.0);
        if (d < best) { best = d; lawn = i; }
    }
    if (best >= 0.0) lawn = -1;
    return best;
}

#endif
