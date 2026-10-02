// Shared weather-wind globals for foliage and water.
// Published every frame by KryticalNova.World.PrecipitationController / FoliageInteractorSystem.
#ifndef KN_WIND_INCLUDED
#define KN_WIND_INCLUDED

float4 _KN_WindVector;          // xy = wind (m/s, world XZ, gust included), z = speed (m/s)
float4 _KN_WindParams;          // x = gust now 0-1, y = gustiness 0-1, z = strength 0-1 (speed / full-wind speed)

#define KN_MAX_INTERACTORS 8
float4 _KN_Interactors[KN_MAX_INTERACTORS]; // xyz = world position (feet), w = radius (m)
int    _KN_InteractorCount;

// Normalised wind direction in world XZ (falls back to +Z when calm).
float2 KNWindDir()
{
    float2 w = _KN_WindVector.xy;
    float len = length(w);
    return len > 0.05 ? w / len : float2(0.0, 1.0);
}

float KNWindStrength() { return _KN_WindParams.z; }
float KNWindGust()     { return _KN_WindParams.x; }

// Cheap per-instance hash in [0,1).
float KNHash21(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
}

#endif
