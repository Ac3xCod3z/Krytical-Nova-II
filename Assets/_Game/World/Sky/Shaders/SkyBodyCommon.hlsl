#ifndef KN_SKY_BODY_COMMON_INCLUDED
#define KN_SKY_BODY_COMMON_INCLUDED

// A Unique Sky (item 24): the custom sun's drawing code, COPIED from StylizedSky.shader so the second sun is drawn the same way
// from the same art (T_Sun via the _KN_SunTex global) without touching the custom sun's files (owner, 14 Nov: "My custom sun
// better not change and that new sun should also be a custom sun"). Keep these in step with StylizedSky.shader by hand.
// The including shader declares the art-styling floats in its UnityPerMaterial block (_BodySaturation, _BodyContrast,
// _BodyInkLift, _BodyHaze, _BodyHorizonHaze, _BodyHazeHeight); the presenter copies their values from the sky material.

TEXTURE2D(_KN_SunTex); SAMPLER(sampler_KN_SunTex);
float4 _KN_BodyTexParams;     // x tan(sun tex radius), y tan(moon tex radius), z sun tex on, w moon tex on (read only)
float4 _KN_BodyTexBrightness; // x sun tex brightness x visibility, y moon, z weather diffusion (read only)
float4 _KN_BodyTexAspect;     // x sun art width/height (read only)

// Gnomonic projection of the view direction onto a square billboard centred on a sky body, kept upright.
float2 KN_BodyUV(float3 d, float3 bodyDir, float tanRadius, float aspect, out half inside)
{
    float3 right = normalize(cross(float3(0, 1, 0), bodyDir) + float3(1e-4, 0, 0));
    float3 upv = cross(bodyDir, right);
    float facing = dot(d, bodyDir);
    float2 plane = float2(dot(d, right), dot(d, upv)) / (max(facing, 1e-3) * tanRadius);
    float2 uv = float2(plane.x / max(aspect, 0.01), plane.y) * 0.5 + 0.5;
    inside = step(0.0, facing) * step(0.0, uv.x) * step(uv.x, 1.0) * step(0.0, uv.y) * step(uv.y, 1.0);
    return uv;
}

half KN_InsideUnit(float2 uv)
{
    return step(0.0, uv.x) * step(uv.x, 1.0) * step(0.0, uv.y) * step(uv.y, 1.0);
}

half3 KN_LightTint(half3 lightColor)
{
    return lightColor / max(max(lightColor.r, max(lightColor.g, lightColor.b)), 1e-3);
}

half4 KN_StylizeBody(half4 art, float2 uv, float elevation, half3 lightColor, half brightness,
                     half glare, half glareRadius, half hdrBoost, half3 skyBehind)
{
    half3 tint = KN_LightTint(lightColor);
    half3 c = art.rgb;
    half luma = dot(c, half3(0.299, 0.587, 0.114));
    c = lerp(luma.xxx, c, _BodySaturation);
    c = saturate(lerp(0.5.xxx, c, _BodyContrast));
    c += (1.0 - c) * _BodyInkLift * tint;

    half r = length(uv - 0.5) * 2.0;
    half core = saturate(1.0 - r / glareRadius);
    core *= core;
    c = lerp(c, tint, saturate(glare * core));
    c *= brightness * (1.0 + core * glare * hdrBoost);

    half low = 1.0 - smoothstep(0.0, _BodyHazeHeight, elevation);
    half haze = saturate(_BodyHaze + low * _BodyHorizonHaze);
    c = lerp(c, skyBehind, haze * 0.75);
    return half4(c, art.a * (1.0 - haze * 0.35));
}

// Overcast / rain: the art dissolves into a pale, soft disc of light (r = 0 centre .. 1 art edge).
half4 KN_DiffuseBody(half4 look, half r, half3 tint, half brightness, half diffusion)
{
    half4 pale = half4(lerp(tint, 1.0.xxx, 0.5) * brightness * 0.85, exp(-r * r * 7.0) * 0.8);
    return lerp(look, pale, diffusion);
}

#endif
