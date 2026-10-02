#ifndef KRYTICAL_COMIC_HALFTONE_INCLUDED
#define KRYTICAL_COMIC_HALFTONE_INCLUDED

// Global Ben-Day dot settings, pushed every frame by ComicStyleFeature so the
// post pass and every ComicToonLit material share one screen-aligned dot grid.
float  _ComicDotSize;      // Cell size in pixels at 1080p (scaled with resolution)
float  _ComicDotAngle;     // Grid rotation in degrees (classic print uses 45)
float  _ComicDotSoftness;  // Edge anti-aliasing multiplier
half4  _ComicInkColor;     // Colour multiplied into the dots
half   _ComicDotOpacity;   // Strength of the dots

// Returns 1 inside a dot, 0 outside. `coverage` 0..1 maps to dot size:
// 0 = no dot, ~0.5 = dots touching, 1 = dots overlap into solid ink.
// Uses pixel coordinates so the pattern stays flat to the screen and never
// stretches with perspective.
half ComicHalftone(float2 screenPx, half coverage)
{
    float cellPx = (_ComicDotSize > 0.0 ? _ComicDotSize : 8.0) * (_ScreenParams.y / 1080.0);
    float s, c;
    sincos(radians(_ComicDotAngle), s, c);
    float2 p = float2(c * screenPx.x - s * screenPx.y, s * screenPx.x + c * screenPx.y) / max(cellPx, 1.0);

    float d = length(frac(p) - 0.5);
    float radius = sqrt(saturate(coverage)) * 0.7072; // 0.7072 reaches the cell corners
    float aa = max(fwidth(d), 1e-4) * max(_ComicDotSoftness, 0.5);
    half dotMask = 1.0 - smoothstep(radius - aa, radius + aa, d);
    return coverage > 0.001 ? dotMask : 0.0;
}

// Diagonal hatch lines on a screen-aligned grid. `cellPx` is line spacing in pixels
// at 1080p, `angle` in degrees, `coverage` 0..1 = line width relative to spacing.
half ComicHatch(float2 screenPx, float cellPx, float angle, half coverage)
{
    cellPx = max(cellPx, 1.0) * (_ScreenParams.y / 1080.0);
    float s, c;
    sincos(radians(angle), s, c);
    float x = (c * screenPx.x - s * screenPx.y) / max(cellPx, 1.0);

    float distToLine = 0.5 - abs(frac(x) - 0.5);
    float halfWidth = saturate(coverage) * 0.5;
    float aa = max(fwidth(x), 1e-4);
    half mask = 1.0 - smoothstep(halfWidth - aa, halfWidth + aa, distToLine);
    return coverage > 0.001 ? mask : 0.0;
}

// Multiplies the ink colour into `color` wherever a dot lands.
half3 ComicApplyDots(half3 color, float2 screenPx, half coverage)
{
    half dotMask = ComicHalftone(screenPx, coverage);
    half opacity = _ComicDotOpacity > 0.0 ? _ComicDotOpacity : 0.0;
    return lerp(color, color * _ComicInkColor.rgb, dotMask * opacity);
}

#endif
