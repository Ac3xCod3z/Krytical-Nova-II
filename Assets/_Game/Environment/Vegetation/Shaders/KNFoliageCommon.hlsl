// Material inputs + vertex wind/interaction displacement shared by every pass of
// KryticalNova/Foliage Wind, so the lit, shadow, depth and depth-normals passes all
// bend identically (ComicPost outlines read depth/normals and must line up).
#ifndef KN_FOLIAGE_COMMON_INCLUDED
#define KN_FOLIAGE_COMMON_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "KNWind.hlsl"

TEXTURE2D(_BaseMap);
SAMPLER(sampler_BaseMap);

CBUFFER_START(UnityPerMaterial)
    float4 _BaseMap_ST;
    half4  _BaseColor;
    half4  _ColorVariation;     // rgb warm tint, a = amount
    half4  _ColorVariation2;    // rgb cool tint
    half   _Cutoff;
    half   _NormalUp;           // 0 = mesh normals, 1 = straight up (soft, even lighting for grass)
    float  _BendHeight;         // object-space height at which bending is full
    float  _TrunkHeight;        // tree mode: object-space height below which nothing moves
    float  _CanopyRadius;       // tree mode: object-space radius where branch flutter is full
    float  _WindStrength;       // lean + sway scale
    float  _FlutterStrength;    // fast small motion (leaf / blade tips)
    float  _IdleSway;           // motion left on a dead-calm day
    float  _InteractStrength;   // how far interactors push blades aside
    float  _WindFadeDistance;   // beyond this camera distance displacement fades out (cost + shimmer)
CBUFFER_END

// Returns the displaced world position. pivotWS = object origin (per instance under instancing).
float3 KNFoliageDisplace(float3 positionOS, float3 positionWS)
{
    float3 pivotWS = float3(UNITY_MATRIX_M[0][3], UNITY_MATRIX_M[1][3], UNITY_MATRIX_M[2][3]);
    float heightWS = max(positionWS.y - pivotWS.y, 0.0);

    float camDist = distance(GetCameraPositionWS(), pivotWS);
    float fade = saturate(1.0 - (camDist - _WindFadeDistance) / max(_WindFadeDistance * 0.25, 1.0));
    if (fade <= 0.0) return positionWS;

    float2 dir = KNWindDir();
    float2 side = float2(-dir.y, dir.x);
    float strength = KNWindStrength();
    float gust = KNWindGust();
    float t = _Time.y;
    float rnd = KNHash21(pivotWS.xz);

#if defined(_TREE_MODE)
    // Trunk stays planted; motion grows above the trunk and toward branch ends.
    float h = saturate((positionOS.y - _TrunkHeight) / max(_BendHeight - _TrunkHeight, 0.01));
    float weight = h * h;
    float radial = saturate(length(positionOS.xz) / max(_CanopyRadius, 0.01));
#else
    // Blade bases stay planted; tips move most (squared = stiff base).
    float h = saturate(positionOS.y / max(_BendHeight, 0.01));
    float weight = h * h;
    float radial = 1.0;
#endif

    // Every wave runs faster in more wind. That part of its phase comes summed up from the CPU (KNWind.hlsl):
    // time x strength would jump with every gust.
    float phase = KNWindPhase();
    // Gust waves rolling across the field along the wind, so neighbouring plants move together.
    float wave = sin(dot(pivotWS.xz, dir) * 0.12 - (1.2 * t + 2.5 * phase) + rnd * 0.8) * 0.5 + 0.5;
    float lean = strength * (0.45 + 0.55 * gust) * (0.55 + 0.45 * wave);
    float sway = sin(1.3 * t + 2.2 * phase + rnd * 6.2831) * (_IdleSway + 0.25 * strength);
    float flutter = sin(7.0 * t + 9.0 * phase + dot(positionWS, float3(1.7, 2.3, 1.3))) * _FlutterStrength * (0.15 + strength) * radial;

    float2 offset = dir * (lean + sway) * _WindStrength + side * sway * 0.35 * _WindStrength;
    offset *= weight * max(heightWS, 0.05);
    float3 displaced = positionWS;
    displaced.xz += offset * fade;
    displaced += float3(side.x, 0.35, side.y) * flutter * weight * fade;

    // Interactors (players / NPCs): push blades away and flatten them a little.
    float2 push = 0.0;
    float flatten = 0.0;
    [loop] for (int i = 0; i < _KN_InteractorCount; i++)
    {
        float4 it = _KN_Interactors[i];
        float2 d = pivotWS.xz - it.xz;
        float dist = length(d);
        float vertical = abs(pivotWS.y - it.y);
        float f = saturate(1.0 - dist / max(it.w, 0.01)) * step(vertical, 1.5);
        f *= f;
        push += (dist > 0.001 ? d / dist : float2(1.0, 0.0)) * f;
        flatten = max(flatten, f);
    }
    displaced.xz += push * _InteractStrength * weight * heightWS * fade;
    displaced.y -= flatten * 0.55 * _InteractStrength * weight * heightWS * fade;

    // Keep blade length roughly constant: the further it leans, the lower the tip.
    float bend = length(displaced.xz - positionWS.xz);
    displaced.y -= min(bend * bend / max(heightWS * 2.0, 0.05), heightWS * 0.6);
    return displaced;
}

half3 KNFoliageNormal(half3 normalWS)
{
    return normalize(lerp(normalWS, half3(0, 1, 0), _NormalUp));
}

half3 KNFoliageTint(half3 albedo)
{
    // Light colour variation: broad meadow patches (warm/dry vs. cool/lush) plus a little per-plant jitter.
    float3 pivotWS = float3(UNITY_MATRIX_M[0][3], UNITY_MATRIX_M[1][3], UNITY_MATRIX_M[2][3]);
    float2 p = pivotWS.xz;
    half patch = (half)(sin(p.x * 0.09 + sin(p.y * 0.061) * 2.1) * sin(p.y * 0.083 + sin(p.x * 0.047) * 1.7) * 0.5 + 0.5);
    half jitter = (half)KNHash21(p * 0.37 + 11.0);
    half t = saturate(patch * 0.75 + jitter * 0.35 - 0.05);
    half3 tint = lerp(_ColorVariation2.rgb, _ColorVariation.rgb, t);
    half brightness = 0.92 + 0.16 * (half)KNHash21(p * 1.91 + 3.0);
    return albedo * lerp(half3(1, 1, 1), tint, _ColorVariation.a) * brightness;
}

#endif
