// Shared cloud field used by the stylized skybox AND the ComicPost cloud-shadow term,
// so the shadows sweeping the ground line up with the clouds drawn in the sky.
// All values are globals published by KryticalNova.World.EnvironmentPresenter.
#ifndef KN_SKY_COMMON_INCLUDED
#define KN_SKY_COMMON_INCLUDED

TEXTURE2D(_KN_CloudNoise);      // R = cloud shapes, G = detail, B = cirrus streaks, A = macro coverage variation
SAMPLER(sampler_KN_CloudNoise);

float4 _KN_CloudParams;         // x coverage 0-1, y density (opacity), z edge softness, w core darkness
float4 _KN_CloudShape;          // x plane height (m), y 1/tile size (1/m), z detail scale (integer), w detail erosion
float4 _KN_CloudWind;           // xy base scroll (uv, wrapped), zw detail scroll (uv, wrapped)
float4 _KN_CloudLightDir;       // xyz direction toward the active key light (sun or moon)
float4 _KN_CloudShadow;         // x ground shadow strength, y mip bias (softness)

// World XZ on the cloud plane → noise uv.
float2 KN_CloudUV(float2 planeXZ)
{
    return planeXZ * _KN_CloudShape.y + _KN_CloudWind.xy;
}

// Coverage → shape threshold. Shapes span roughly [-0.25, 1] after detail erosion, so the
// threshold is pushed past both ends: coverage 0 = empty sky, coverage 1 = solid overcast.
// Returns x = opacity mask (0-1), y = depth inside the cloud (0-1) — depth keeps overcast skies
// from going flat, because it still varies when the mask is fully saturated.
half2 KN_CloudCoverageMask(half shape, half macro)
{
    half soft = _KN_CloudParams.z;
    half coverage = saturate(_KN_CloudParams.x + (macro - 0.5h) * 0.3h * (1.0h - _KN_CloudParams.x));
    half threshold = lerp(1.0h + soft, -0.3h - soft, coverage);
    half mask = smoothstep(threshold - soft, threshold + soft, shape);
    half depth = saturate((shape - threshold) / max(1.05h - threshold, 0.2h));
    return half2(mask, depth);
}

// Full-quality cloud sample (2 texture reads).
half2 KN_CloudDensity(float2 uv)
{
    half4 n = SAMPLE_TEXTURE2D(_KN_CloudNoise, sampler_KN_CloudNoise, uv);
    half detail = SAMPLE_TEXTURE2D(_KN_CloudNoise, sampler_KN_CloudNoise, uv * _KN_CloudShape.z + _KN_CloudWind.zw).g;
    half shape = n.r - (1.0h - detail) * _KN_CloudShape.w;
    return KN_CloudCoverageMask(shape, n.a);
}

// Single-read cloud sample at an explicit mip (ground shadows, light probe sample, low quality sky).
half2 KN_CloudDensityLod(float2 uv, float lod)
{
    half4 n = SAMPLE_TEXTURE2D_LOD(_KN_CloudNoise, sampler_KN_CloudNoise, uv, lod);
    half shape = n.r - 0.5h * _KN_CloudShape.w;
    return KN_CloudCoverageMask(shape, n.a);
}

// 0 = clear, 1 = fully under cloud shadow, for a world position. Projects along the key light.
half KN_CloudShadowAt(float3 positionWS)
{
    float3 L = _KN_CloudLightDir.xyz;
    float2 planeXZ = positionWS.xz + L.xz * ((_KN_CloudShape.x - positionWS.y) / max(L.y, 0.15));
    return KN_CloudDensityLod(KN_CloudUV(planeXZ), _KN_CloudShadow.y).x * _KN_CloudParams.y * _KN_CloudShadow.x;
}

#endif
