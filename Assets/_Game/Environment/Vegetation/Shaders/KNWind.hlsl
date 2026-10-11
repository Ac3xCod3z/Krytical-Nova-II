// Shared weather-wind globals for foliage and water.
// Published every frame by KryticalNova.World.PrecipitationController / FoliageInteractorSystem.
#ifndef KN_WIND_INCLUDED
#define KN_WIND_INCLUDED

float4 _KN_WindVector;          // xy = wind (m/s, world XZ, gust included), z = speed (m/s)
float4 _KN_WindParams;          // x = gust now 0-1, y = gustiness 0-1, z = strength 0-1 (speed / full-wind speed)
// Wind strength and wind travel summed up over time on the CPU. A wave's phase must never be written as
// time x (a + b x strength): strength changes every frame with the gusts, and time is large, so every change threw the
// whole field to another point of its wave (the stutter of 10 Oct 2026, worse the longer a session ran). Write it as
// a x time + b x KNWindPhase() instead. The sums are wrapped at 20 pi, so b must be a multiple of 0.1.
float4 _KN_WindPhase;           // x = sum of strength over time (s), y = the same one frame ago, w = 1 once published
float4 _KN_WindDrift;           // xy = sum of wind direction x (1.2 + 3 x strength) over time, zw = one frame ago

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

// The running sum of wind strength: what a wave's speed-up with the wind is multiplied by.
float KNWindPhase()    { return _KN_WindPhase.x; }

// How far the local gust cells have drifted downwind. Before anything has published it (the Scene view outside
// Play) they drift at their calm speed along the wind as it is.
float2 KNWindDrift(float t)
{
    return _KN_WindPhase.w > 0.5 ? _KN_WindDrift.xy : KNWindDir() * (t * 1.2);
}

// Cheap per-instance hash in [0,1).
float KNHash21(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
}

#endif
