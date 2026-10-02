#ifndef REDFASTGI_GDF_VOXEL_INCLUDED
#define REDFASTGI_GDF_VOXEL_INCLUDED

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

#include "Packing.hlsl"

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

Texture3D<uint> GDFMap;

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

float3 gdfNormalAtUVW(float3 uvw, uint cascade)
{
    float w = 1.0f / (GDFCascadeCount + 1);

    static const float O = 0.0005;
    float Ox = O * w;

    float s = GDF_BASE_STEP * (1 << cascade);

    float r = (tex3Dlod(GDFSDF, float4(float3(uvw.x + Ox, uvw.y,     uvw.z    ), 0)).x * 2 - 1) * s;
    float l = (tex3Dlod(GDFSDF, float4(float3(uvw.x - Ox, uvw.y,     uvw.z    ), 0)).x * 2 - 1) * s;
    float u = (tex3Dlod(GDFSDF, float4(float3(uvw.x,      uvw.y + O, uvw.z    ), 0)).x * 2 - 1) * s;
    float d = (tex3Dlod(GDFSDF, float4(float3(uvw.x,      uvw.y - O, uvw.z    ), 0)).x * 2 - 1) * s;
    float f = (tex3Dlod(GDFSDF, float4(float3(uvw.x,      uvw.y,     uvw.z + O), 0)).x * 2 - 1) * s;
    float b = (tex3Dlod(GDFSDF, float4(float3(uvw.x,      uvw.y,     uvw.z - O), 0)).x * 2 - 1) * s;

    float3 gradient = float3(r - l, u - d, f - b);

    float gradient_length = length(gradient);
    float3 normal = gradient_length > 0.0f ? (gradient / gradient_length) : 0;

    return normal;
}

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

float4 gdfColorAndEmission(sampler3D atlas, float3 position, uint cascade)
{
    float cell_dim = GDF_SDF_CELL_DIM * (1 << cascade);
    uint3 coord = clamp(position / cell_dim - GDFCascadeOrigins[cascade].xyz, 0, GDF_CASCADE_DIM - 1);
    coord.x += GDFCascades[cascade] * GDF_CASCADE_DIM;

    uint map = GDFMap[coord];
    float3 uvw = unpackR11G11B10(map);

    float4 color = tex3Dlod(atlas, float4(uvw, 0));

    return float4(color.rgb, color.a * MAX_EMISSION);
}

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

#endif