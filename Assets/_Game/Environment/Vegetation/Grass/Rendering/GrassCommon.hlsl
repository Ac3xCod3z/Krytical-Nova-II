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

#endif
