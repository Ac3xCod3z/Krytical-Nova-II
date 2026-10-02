#ifndef REDFASTGI_GDF_INCLUDED
#define REDFASTGI_GDF_INCLUDED

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

#include "AABB.hlsl"

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

uint GDFCascadeCount;
uint4 GDFCascades;
float4 GDFCascadeOrigins[GDF_MAX_CASCADES];
float4 GDFCascadePositions[GDF_MAX_CASCADES];

sampler3D GDFSDF;

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

uint gdfCascade(float3 position)
{
    float3 coord = (position - ReferencePosition) / GDF_SDF_CELL_DIM;
    float max_coord = max(abs(coord.x), max(abs(coord.y), abs(coord.z)));
    float cascade = log2(max_coord / (GDF_CASCADE_DIM / 2 - 1));

    return min(uint(ceil(max(cascade, 0))), GDFCascadeCount - 1);
}

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

float sampleGDF(float3 position, uint cascade)
{
    float cell_dim = GDF_SDF_CELL_DIM * (1 << cascade);
    float3 uvw = (position / cell_dim - GDFCascadeOrigins[cascade].xyz) / (float)GDF_CASCADE_DIM;

    float s = GDF_BASE_STEP * (1 << cascade);
    float d = s;

    [flatten]
    if (all(uvw >= 0) && all(uvw < 1))
    {
        float w = 1.0f / (GDFCascadeCount + 1);
        uvw.x = (uvw.x + GDFCascades[cascade]) * w;

        d = (tex3Dlod(GDFSDF, float4(uvw, 0)).x * 2 - 1) * s;
    }

    return d;
}

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

float3 gdfUVW(float3 position, uint cascade)
{
    float cell_dim = GDF_SDF_CELL_DIM * (1 << cascade);
    float3 uvw = saturate((position / cell_dim - GDFCascadeOrigins[cascade].xyz) / (float)GDF_CASCADE_DIM);

    float w = 1.0f / (GDFCascadeCount + 1);
    uvw.x = (uvw.x + GDFCascades[cascade]) * w;

    return uvw;
}

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

bool traceGlobal(float3 ray_origin,
                 float3 ray_direction,
                 float  min_trace_distance,
                 float  max_trace_distance,

                 uint max_steps,

                 inout Hit hit)
{
    float3 ray_end = ray_origin + ray_direction * max_trace_distance;
    float cascade_size = GDF_CASCADE_DIM * (1 << (GDFCascadeCount - 1)) * GDF_SDF_CELL_DIM;
    float2 bound_intersection = intersectAABB(ray_origin, ray_end, GDFCascadePositions[GDFCascadeCount - 1].xyz, float3(cascade_size, cascade_size, cascade_size));

    bound_intersection *= max_trace_distance;
    bound_intersection.x = max(bound_intersection.x, min_trace_distance);

    float ray_travel = min_trace_distance;
    float max_distance = 0;
    bool has_hit = false;

    float min_step_size = 0.05;

    [loop]
    for (uint step_index = 0; step_index < max_steps; step_index++)
    {
        float3 sample_position = ray_origin + ray_travel * ray_direction;

        uint sample_cascade = gdfCascade(sample_position);
        float distance_field = sampleGDF(sample_position, sample_cascade);

        float bias = 0.05 * (1 << sample_cascade);

        max_distance = max(distance_field, max_distance);

        float surface_expansion = bias * saturate(ray_travel / (2.0f * bias));

        if (distance_field < surface_expansion)
        {
            has_hit = true;
            break;
        }

        float step_distance = max(distance_field, min_step_size);
        ray_travel += step_distance;

        min_step_size *= 1.05f;

        if (ray_travel > bound_intersection.y + surface_expansion)
            break;
    }

    bool result = false;

    [branch]
    if (has_hit && ray_travel < hit.dist)
    {
        hit.has_hit = true;
        hit.dist = ray_travel;

        result = true;
    }

    return result;
}

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

float traceGlobalShadow(float3 ray_origin,
                        float3 ray_direction,
                        float  min_trace_distance,
                        float  max_trace_distance,

                        uint max_steps)
{
    float3 ray_end = ray_origin + ray_direction * max_trace_distance;
    float cascade_size = GDF_CASCADE_DIM * (1 << (GDFCascadeCount - 1)) * GDF_SDF_CELL_DIM;
    float2 bound_intersection = intersectAABB(ray_origin, ray_end, GDFCascadePositions[GDFCascadeCount - 1].xyz, float3(cascade_size, cascade_size, cascade_size));

    bound_intersection *= max_trace_distance;
    bound_intersection.x = max(bound_intersection.x, min_trace_distance);

    float ray_travel = min_trace_distance;
    float max_distance = 0;
    bool has_hit = false;

    float min_step_size = 0.05;

    [loop]
    for (uint step_index = 0; step_index < max_steps; step_index++)
    {
        float3 sample_position = ray_origin + ray_travel * ray_direction;

        uint sample_cascade = gdfCascade(sample_position);
        float distance_field = sampleGDF(sample_position, sample_cascade);

        float bias = 0.05 * (1 << sample_cascade);

        max_distance = max(distance_field, max_distance);

        float surface_expansion = bias * saturate(ray_travel / (2.0f * bias));

        if (distance_field < surface_expansion)
        {
            has_hit = true;
            break;
        }

        float step_distance = max(distance_field, min_step_size);
        ray_travel += step_distance;

        min_step_size *= 1.05f;

        if (ray_travel > bound_intersection.y + surface_expansion)
            break;
    }

    return 1 - has_hit;
}

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

#endif