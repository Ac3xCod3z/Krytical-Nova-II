// Shared by GrassGenerate.compute (writes) and GrassBlades.shader (reads). Keep in sync with
// KryticalNova.World.Grass.GrassRenderer.InstanceStride (32 bytes).
#ifndef KN_GRASS_COMMON_INCLUDED
#define KN_GRASS_COMMON_INCLUDED

struct GrassInstance
{
    float4 posRot;   // xyz root position (world), w yaw (radians)
    float3 size;     // x uniform scale (height / mesh height), y width multiplier, z random 0-1
    uint   packed;   // bits 24-31 moisture, 0-23 debug colour (rgb8, 0 = none)
};

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
