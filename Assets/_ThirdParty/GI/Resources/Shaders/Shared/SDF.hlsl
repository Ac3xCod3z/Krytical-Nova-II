#ifndef REDFASTGI_SDF_INCLUDED
#define REDFASTGI_SDF_INCLUDED

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

#include "AABB.hlsl"

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

struct SDF
{
    uint base_brick_offset;
    uint3 brick_counts;
    uint mip_count;
    float empty_step;
    float bias;
    float3 volume_min;
    float3 volume_max;
    float value_scale;
};

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

SDF loadSDF(in ByteAddressBuffer assets, uint asset)
{
    SDF sdf;

    uint base_offset = ASSET_STRIDE * asset;

    uint4 row0 = assets.Load4(base_offset);
    uint4 row1 = assets.Load4(base_offset + 16);
    uint4 row2 = assets.Load4(base_offset + 32);

    sdf.base_brick_offset = row0.x;
    sdf.brick_counts = uint3(row0.y & 0xFF,
                             (row0.y >> 8) & 0xFF,
                             (row0.y >> 16) & 0xFF);
    sdf.empty_step = asfloat(row0.z);
    sdf.bias = asfloat(row0.w);
    sdf.volume_min = asfloat(row1.xyz);
    sdf.value_scale = asfloat(row1.w);
    sdf.volume_max = asfloat(row2.xyz);

    return sdf;
}

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

float sampleDistanceField(ByteAddressBuffer bricks, sampler3D atlas, float3 uvw, in SDF sdf)
{
    float3 brick_uvw = uvw * float3(sdf.brick_counts);
    uint3 brick = uint3(brick_uvw);
    uint brick_index = brick.x + (brick.y + brick.z * sdf.brick_counts.y) * sdf.brick_counts.x;
    uint brick_value = bricks.Load((sdf.base_brick_offset + brick_index) * 4);

    float d = sdf.empty_step;

    if (brick_value != INVALID_ID)
    {
        float3 local_uvw = (brick_uvw - brick + BRICK_PADDING) * ((float)BRICK_PAYLOAD / (float)BRICK_DIM);

        static const uint DIM = ATLAS_DIM / BRICK_DIM;

        float3 brick_offset = float3(brick_value & 0xFF,
                                     (brick_value >> 8) & 0xFF,
                                     (brick_value >> 16) & 0xFF);

        float3 atlas_uvw = (brick_offset + local_uvw) / (float)DIM;

        d = (tex3Dlod(atlas, float4(atlas_uvw, 0)).x * 2 - 1) * sdf.value_scale;
    }

    return d;
}

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

float3 sdfAtlasUVW(ByteAddressBuffer bricks, float3 uvw, in SDF sdf)
{
    float3 brick_uvw = uvw * float3(sdf.brick_counts);
    uint3 brick = uint3(brick_uvw);
    uint brick_index = brick.x + (brick.y + brick.z * sdf.brick_counts.y) * sdf.brick_counts.x;
    uint brick_value = bricks.Load((sdf.base_brick_offset + brick_index) * 4);

    float3 atlas_uvw = 0;

    if (brick_value != INVALID_ID)
    {
        float3 local_uvw = (brick_uvw - brick + BRICK_PADDING) * ((float)BRICK_PAYLOAD / (float)BRICK_DIM);

        static const uint DIM = ATLAS_DIM / BRICK_DIM;

        float3 brick_offset = float3(brick_value & 0xFF,
            (brick_value >> 8) & 0xFF,
            (brick_value >> 16) & 0xFF);

        atlas_uvw = (brick_offset + local_uvw) / (float)DIM;
    }

    return atlas_uvw;
}

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

float4 sampleVoxel(ByteAddressBuffer bricks, sampler3D atlas, float3 uvw, in SDF sdf)
{
    float3 brick_uvw = uvw * float3(sdf.brick_counts);
    uint3 brick = uint3(brick_uvw);
    uint brick_index = brick.x + (brick.y + brick.z * sdf.brick_counts.y) * sdf.brick_counts.x;
    uint brick_value = bricks.Load((sdf.base_brick_offset + brick_index) * 4);

    float4 color = 0;

    if (brick_value != INVALID_ID)
    {
        float3 local_uvw = (brick_uvw - brick + BRICK_PADDING) * ((float)BRICK_PAYLOAD / (float)BRICK_DIM);

        static const uint DIM = ATLAS_DIM / BRICK_DIM;

        float3 brick_offset = float3(brick_value & 0xFF,
            (brick_value >> 8) & 0xFF,
            (brick_value >> 16) & 0xFF);

        float3 atlas_uvw = (brick_offset + local_uvw) / (float)DIM;

        color = tex3Dlod(atlas, float4(atlas_uvw, 0));
    }

    return color;
}

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

float3 sampleDistanceFieldGradient(ByteAddressBuffer bricks, sampler3D atlas, float3 uvw, in SDF sdf)
{
    float3 voxel_offset = 0.5f / (sdf.brick_counts * BRICK_PAYLOAD + 1);

    float r = sampleDistanceField(bricks, atlas, float3(uvw.x + voxel_offset.x, uvw.y, uvw.z), sdf);
    float l = sampleDistanceField(bricks, atlas, float3(uvw.x - voxel_offset.x, uvw.y, uvw.z), sdf);
    float u = sampleDistanceField(bricks, atlas, float3(uvw.x, uvw.y + voxel_offset.y, uvw.z), sdf);
    float d = sampleDistanceField(bricks, atlas, float3(uvw.x, uvw.y - voxel_offset.y, uvw.z), sdf);
    float f = sampleDistanceField(bricks, atlas, float3(uvw.x, uvw.y, uvw.z + voxel_offset.z), sdf);
    float b = sampleDistanceField(bricks, atlas, float3(uvw.x, uvw.y, uvw.z - voxel_offset.z), sdf);

    return float3(r - l, u - d, f - b);
}

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

bool traceInstance(ByteAddressBuffer assets,
                   ByteAddressBuffer instances,
                   ByteAddressBuffer bricks,
                   sampler3D sdf_atlas,
                   sampler3D voxel_atlas,

                   uint instance_id,
                   uint asset_id,
                   float3 ray_origin,
                   float3 ray_direction,
                   float min_trace_distance,
                   float max_trace_distance,

                   inout Hit hit,
                   inout HitSurface surface)
{
    float4x4 transform_inverted = loadInverseTransform(instances, instance_id);
    SDF sdf = loadSDF(assets, asset_id);

    float3 world_ray_end = ray_origin + ray_direction * max_trace_distance;
    float3 volume_ray_start = mul(transform_inverted, float4(ray_origin, 1)).xyz;
    float3 volume_ray_end = mul(transform_inverted, float4(world_ray_end, 1)).xyz;
    float3 volume_ray_direction = volume_ray_end - volume_ray_start;

    float volume_max_trace_distance = length(volume_ray_direction);
    float volume_min_trace_distance = volume_max_trace_distance * (min_trace_distance / max_trace_distance);
    volume_ray_direction /= volume_max_trace_distance;

    float2 bound_intersection = intersectAABB(volume_ray_start, volume_ray_end, sdf.volume_min, sdf.volume_max);

    bound_intersection *= volume_max_trace_distance;
    bound_intersection.x = max(bound_intersection.x, volume_min_trace_distance);

    bool result = false;

    [branch]
    if (bound_intersection.x < bound_intersection.y)
    {
        float sample_ray_time = bound_intersection.x;

        uint max_steps = 32;
        float min_step_size = distance(sdf.volume_min, sdf.volume_max) * 0.003f;
        bool has_hit = false;
        float max_distance = 0;

        [loop]
        for (uint step_index = 0; step_index < max_steps; step_index++)
        {
            float3 sample_volume_position = volume_ray_start + volume_ray_direction * sample_ray_time;
            float3 sample_volume_position_clamped = clamp(sample_volume_position, sdf.volume_min, sdf.volume_max - 0.0001);
            float distance_clamped = distance(sample_volume_position, sample_volume_position_clamped);
            float distance_field = distance_clamped + sampleDistanceField(bricks, sdf_atlas, (sample_volume_position_clamped - sdf.volume_min) / (sdf.volume_max - sdf.volume_min), sdf);

            max_distance = max(distance_field, max_distance);

            float surface_expansion = sdf.bias * saturate(max_distance / (2.0f * sdf.bias));

            if (distance_field < surface_expansion)
            {
                has_hit = true;
                break;
            }

            float step_distance = max(distance_field, min_step_size);
            sample_ray_time += step_distance;

            min_step_size *= 1.05f;

            if (sample_ray_time > bound_intersection.y + surface_expansion)
                break;
        }

        [branch]
        if (has_hit)
        {
            float3 volume_scale = loadScale(instances, instance_id);
            float hit_distance = length(volume_ray_direction * sample_ray_time * volume_scale);

            [branch]
            if (hit_distance < hit.dist)
            {
                float3 sample_volume_position = volume_ray_start + volume_ray_direction * sample_ray_time;

                float3 gradient = sampleDistanceFieldGradient(bricks, sdf_atlas, (sample_volume_position - sdf.volume_min) / (sdf.volume_max - sdf.volume_min), sdf);
                float gradient_length = length(gradient);
                float3 normal = gradient_length > 0.0f ? (gradient / gradient_length) : 0;
                normal = normalize(mul(float4(normal, 1), transform_inverted).xyz);

                hit.has_hit = true;
                hit.dist = hit_distance;
                surface.normal = normal;

                float4 s = sampleVoxel(bricks, voxel_atlas, (sample_volume_position - sdf.volume_min) / (sdf.volume_max - sdf.volume_min), sdf);
                surface.color = s.rgb;
                surface.emission = s.a * MAX_EMISSION;

                result = true;
            }
        }
    }

    return result;
}

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

float traceInstanceShadow(ByteAddressBuffer assets,
                          ByteAddressBuffer instances,
                          ByteAddressBuffer bricks,
                          sampler3D sdf_atlas,

                          uint instance_id,
                          uint asset_id,
                          float3 ray_origin,
                          float3 ray_direction,
                          float min_trace_distance,
                          float max_trace_distance)
{
    float4x4 transform_inverted = loadInverseTransform(instances, instance_id);
    SDF sdf = loadSDF(assets, asset_id);

    float3 world_ray_end = ray_origin + ray_direction * max_trace_distance;
    float3 volume_ray_start = mul(transform_inverted, float4(ray_origin, 1)).xyz;
    float3 volume_ray_end = mul(transform_inverted, float4(world_ray_end, 1)).xyz;
    float3 volume_ray_direction = volume_ray_end - volume_ray_start;

    float volume_max_trace_distance = length(volume_ray_direction);
    float volume_min_trace_distance = volume_max_trace_distance * (min_trace_distance / max_trace_distance);
    volume_ray_direction /= volume_max_trace_distance;

    float2 bound_intersection = intersectAABB(volume_ray_start, volume_ray_end, sdf.volume_min, sdf.volume_max);

    bound_intersection *= volume_max_trace_distance;
    bound_intersection.x = max(bound_intersection.x, volume_min_trace_distance);

    bool result = false;

    [branch]
    if (bound_intersection.x < bound_intersection.y)
    {
        float sample_ray_time = bound_intersection.x;

        uint max_steps = 32;
        float min_step_size = distance(sdf.volume_min, sdf.volume_max) * 0.003f;
        uint step_index = 0;
        float max_distance = 0;

        [loop]
        for (; step_index < max_steps; step_index++)
        {
            float3 sample_volume_position = volume_ray_start + volume_ray_direction * sample_ray_time;
            float3 sample_volume_position_clamped = clamp(sample_volume_position, sdf.volume_min, sdf.volume_max - 0.0001);
            float distance_clamped = distance(sample_volume_position, sample_volume_position_clamped);
            float distance_field = distance_clamped + sampleDistanceField(bricks, sdf_atlas, (sample_volume_position_clamped - sdf.volume_min) / (sdf.volume_max - sdf.volume_min), sdf);

            max_distance = max(distance_field, max_distance);

            float surface_expansion = sdf.bias * saturate(max_distance / (2.0f * sdf.bias));

            if (distance_field < surface_expansion)
            {
                result = true;
                break;
            }

            float step_distance = max(distance_field, min_step_size);
            sample_ray_time += step_distance;

            min_step_size *= 1.05f;

            if (sample_ray_time > bound_intersection.y + surface_expansion)
                break;
        }
    }

    return 1 - result;
}

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

#endif