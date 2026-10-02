#ifndef REDFASTGI_EMISSION_INCLUDED
#define REDFASTGI_EMISSION_INCLUDED

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

float4 colorAndEmission(float3 color, float3 emissive)
{
    float a = length(emissive);
    float s = saturate(a / MAX_EMISSION);

    [flatten]
    if (s < 1.0f / 255.0f)
        return float4(color, 0);
    else
        return float4(lerp(color, emissive / a, saturate(a)), s);
}

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

#endif