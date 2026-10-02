#ifndef REDFASTGI_TERRAIN_INCLUDED
#define REDFASTGI_TERRAIN_INCLUDED

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

#include "AABB.hlsl"

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

sampler2D HeightMap;
sampler2D NormalMap;
sampler2D ColorMap;
uint2 HeightMapResolution;

float3 TerrainMin;
float3 TerrainMax;
float3 TerrainScale;
float3 TerrainPosition;
float3 TerrainColor;

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

float terrainHeightAtLocal(float3 local_position)
{
    float2 sample_xz = (local_position.xz - TerrainMin.xz) / TerrainScale.xz;
    float2 sample_uv = sample_xz / float2(HeightMapResolution);

    float sampled_height = tex2Dlod(HeightMap, float4(sample_uv, 0, 0)).r;
    return sampled_height * TerrainScale.y * 2;
}

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

float distanceToTerrain(float3 position)
{
    float3 local_position = position - TerrainPosition;

    return local_position.y - terrainHeightAtLocal(local_position);
}

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

float3 terrainNormalAtLocal(float3 local_position)
{
    float2 hit_xz = (local_position.xz - TerrainMin.xz) / TerrainScale.xz;
    float2 hit_uv = saturate(hit_xz / float2(HeightMapResolution));

    #if TERRAIN_NORMALS

        float3 normal = normalize(tex2Dlod(NormalMap, float4(hit_uv, 0, 0)).rgb * 2 - 1);

    #else

        float2 ns = 1.0 / float2(HeightMapResolution);

        float w = tex2Dlod(HeightMap, float4(hit_uv + float2(ns.x, 0), 0, 0)).r;
        float e = tex2Dlod(HeightMap, float4(hit_uv - float2(ns.x, 0), 0, 0)).r;
        float s = tex2Dlod(HeightMap, float4(hit_uv + float2(0, ns.y), 0, 0)).r;
        float n = tex2Dlod(HeightMap, float4(hit_uv - float2(0, ns.y), 0, 0)).r;

        float dydx = e - w;
        float dydz = n - s;

        dydx *= TerrainScale.y * 2;
        dydz *= TerrainScale.y * 2;

        float3 normal = normalize(float3(dydx, 0.5f, dydz));

    #endif

    return normal;
}

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

float3 terrainNormal(float3 position)
{
    return terrainNormalAtLocal(position - TerrainPosition);
}

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

float3 terrainColorAtLocal(float3 local_position)
{
    float2 hit_xz = (local_position.xz - TerrainMin.xz) / TerrainScale.xz;
    float2 hit_uv = saturate(hit_xz / float2(HeightMapResolution));

    #if TERRAIN_COLORS

        float3 color = tex2Dlod(ColorMap, float4(hit_uv, 0, 0)).rgb;

    #else

        float3 color = TerrainColor;

    #endif

    return color;
}

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

float3 terrainColor(float3 position)
{
    return terrainColorAtLocal(position - TerrainPosition);
}

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

bool traceTerrain(float3 ray_origin,
                  float3 ray_direction,
                  float  min_trace_distance,
                  float  max_trace_distance,

                  uint max_steps,

                  inout Hit hit,
                  inout HitSurface surface)
{
    float3 world_ray_end = ray_origin + ray_direction * max_trace_distance;

    float3 local_ray_start = ray_origin - TerrainPosition;
    float3 local_ray_end = world_ray_end - TerrainPosition;

    float2 intersection = intersectAABB(local_ray_start, local_ray_end, TerrainMin, TerrainMax);

    intersection *= max_trace_distance;
    intersection.x = max(intersection.x, min_trace_distance);

    bool result = false;

    [branch]
    if (intersection.x < intersection.y)
    {
        float current_sample = intersection.x;

        float min_step_size = 0.2;
        const float bias = 0.2;
        uint step_index = 0;
        bool has_hit = false;

        float3 initial_position = local_ray_start + ray_direction * current_sample;
        float initial_height = initial_position.y - terrainHeightAtLocal(initial_position);
        bool was_above = initial_height >= 0.01;

        [loop]
        for (; step_index < max_steps; step_index++)
        {
            float3 sample_position = local_ray_start + ray_direction * current_sample;
            float actual_height = terrainHeightAtLocal(sample_position);

            float distance_field = sample_position.y - actual_height;
            bool is_above = distance_field >= 0;

            [flatten]
            if (ray_direction.y < 0)
                distance_field /= min(1.0f, -ray_direction.y + 0.25f);

            distance_field = max(distance_field * 0.5, min(1, distance_field));

            float surface_expansion = bias * saturate(current_sample / (2.0f * bias));

            if (distance_field < surface_expansion && was_above)
            {
                current_sample += distance_field - surface_expansion;
                has_hit = true;
                break;
            }

            float step_distance = max(distance_field, min_step_size);
            current_sample += step_distance;
            was_above = is_above;

            if (current_sample > intersection.y)
                break;
        }

        float hit_distance = current_sample;

        if (has_hit && hit_distance < hit.dist)
        {
            float3 hit_position = local_ray_start + ray_direction * hit_distance;

            float2 hit_xz = (hit_position.xz - TerrainMin.xz) / TerrainScale.xz;
            float2 hit_uv = saturate(hit_xz / float2(HeightMapResolution));

            #if TERRAIN_NORMALS

                float3 normal = normalize(tex2Dlod(NormalMap, float4(hit_uv, 0, 0)).rgb * 2 - 1);

            #else

                float2 ns = 1.0 / float2(HeightMapResolution);

                float w = tex2Dlod(HeightMap, float4(hit_uv + float2(ns.x, 0), 0, 0)).r;
                float e = tex2Dlod(HeightMap, float4(hit_uv - float2(ns.x, 0), 0, 0)).r;
                float s = tex2Dlod(HeightMap, float4(hit_uv + float2(0, ns.y), 0, 0)).r;
                float n = tex2Dlod(HeightMap, float4(hit_uv - float2(0, ns.y), 0, 0)).r;

                float dydx = e - w;
                float dydz = n - s;

                dydx *= TerrainScale.y * 2;
                dydz *= TerrainScale.y * 2;

                float3 normal = normalize(float3(dydx, 0.5f, dydz));

            #endif

            #if TERRAIN_COLORS

                surface.color = tex2Dlod(ColorMap, float4(hit_uv, 0, 0)).rgb;

            #else

                surface.color = TerrainColor;

            #endif

            hit.has_hit = true;
            hit.dist = hit_distance;

            surface.normal = normal;
            surface.emission = 0;

            result = true;
        }
    }

    return result;
}

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

#endif