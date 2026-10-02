#ifndef REDFASTGI_APPLY_INCLUDED
#define REDFASTGI_APPLY_INCLUDED

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

#include "Octahedral.hlsl"

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

float DiffuseMultiplier;

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

float _gi_sqr_(float x)
{
    return x * x;
}

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

float3 lightGIAt(float3 world_position, uint cascade, float3 cell_coord_f, float3 normal, float3 normal_bias)
{
    int3 cell_coord_i = int3(cell_coord_f);

    //

    float3 cell_f = frac(cell_coord_f);

    static const float2 UV_SCALE = float2(1.0f / float(PROBE_RES + 2),
                                          1.0f / float(PROBE_RES + 2));

    static const float2 UV_OFFSET = float2(1.0 / float(PROBE_ATLAS_COUNT_X),
                                           1.0 / float(PROBE_ATLAS_COUNT_Y));

    float3 irradiance_sum = 0;
    float weight_sum = 0;

    for (uint i = 0; i < 8; i++)
    {
        int3 offset = int3(i, i >> 1, i >> 2) & 0x1;
        int3 cell_coord = cell_coord_i + offset;

        if (all(cell_coord >= 0) && all(cell_coord < int(PROBE_CASCADE_DIM)))
        {
            uint cell_index = cell_coord.x +
                              (cell_coord.y * PROBE_CASCADE_DIM) +
                              (cell_coord.z * PROBE_CASCADE_DIM * PROBE_CASCADE_DIM) +
                              (cascade * PROBE_CASCADE_CELL_COUNT);

            uint entry_index = ProbeGrid.Load(cell_index * 4);

            [branch]
            if (entry_index != INVALID_ID)
            {
                float3 probe_position = asfloat(ProbeEntries.Load3(entry_index * PROBE_ENTRY_STRIDE + PROBE_ENTRY_POSITION));

                #if PROBES_SIMPLE

                    float3 irradiance_probe = loadProbeColor(ProbeSH, entry_index);

                #elif PROBES_SH3

                    SH3 probe = loadProbeSH3(ProbeSH, entry_index);
                    float3 irradiance_probe = evaluateSH3(probe, normal);

                #else

                    SH2 probe = loadProbeSH2(ProbeSH, entry_index);
                    float3 irradiance_probe = evaluateSH2(probe, normal);

                #endif

                float3 trilinear = lerp(1.0f - cell_f, cell_f, offset);

                float3 true_direction = normalize(probe_position - world_position);
                float backface_weight = max(0.0001, (dot(true_direction, normal) + 1.0) * 0.5);
                float weight = backface_weight * backface_weight;

                #if PROBES_OCCLUSION
                {
                    float3 probe_to_position = world_position - probe_position + normal_bias;

                    float2 local_normal_uv = (directionToOctahedralMap(normalize(probe_to_position)) * PROBE_RES + 1) * UV_SCALE;

                    uint probe_atlas_x = entry_index % PROBE_ATLAS_COUNT_X;
                    uint probe_atlas_y = entry_index / PROBE_ATLAS_COUNT_X;

                    float2 normal_uv = float2(float(probe_atlas_x) + local_normal_uv.x,
                                              float(probe_atlas_y) + local_normal_uv.y) * UV_OFFSET;

                    float d = length(probe_to_position);
                    float2 mean = tex2Dlod(ProbeDepthAtlas, float4(normal_uv, 0, 0)).xy;
                    float variance = abs(_gi_sqr_(mean.x) - mean.y);

                    float chebyshev = variance / (variance + _gi_sqr_(max(d - mean.x, 0.0001)));
                    chebyshev = max(chebyshev, 0);

                    weight *= (d <= mean.x) ? 1 : chebyshev;
                }
                #endif

                weight *= trilinear.x * trilinear.y * trilinear.z;

                float3 local = sqrt(irradiance_probe * 0.5);

                irradiance_sum += local * weight;
                weight_sum += weight;
            }
        }
    }

    float3 irradiance = irradiance_sum / max(0.0001, weight_sum);

    return irradiance * irradiance;
}

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

float3 applyGIAt(float3 world_position, float3 normal)
{
    uint cascade = positionToProbeCascade(world_position);
    float bias = ProbeCell * (1 << cascade) * PROBE_QUERY_BIAS;

    float3 direction = normalize(ReferencePosition - world_position);
    float3 query_normal = normalize(normal + direction);

    float3 query_position = world_position + query_normal * bias;

    static const float NORMAL_BIAS = 0.2f; // only for occlusion
    float3 normal_bias = (normal + 3.0f * direction) * NORMAL_BIAS * (1 << cascade) * ProbeCell;

    float3 cell_coord = positionToProbeCellCoordFloat(query_position, cascade);

    float3 light = lightGIAt(world_position, cascade, cell_coord, normal, normal_bias);

    float3 r3 = abs(cell_coord - PROBE_BLEND_RANGE);
    float r = max(r3.x, max(r3.y, r3.z)) / PROBE_BLEND_RANGE;
    float d = saturate((max(r, 1 - PROBE_BLEND_BORDER) - PROBE_BLEND_BORDER) / PROBE_BLEND_THRESHOLD);

    [branch]
    if (d > 0.01 && cascade + 1 < ProbeCascadeCount)
    {
        uint new_cascade = cascade + 1;
        float3 new_coord = positionToProbeCellCoordFloat(query_position, new_cascade);
        float3 new_light = lightGIAt(world_position, new_cascade, new_coord, normal, normal_bias);

        light = lerp(light, new_light, d);
    }
    else
        light = lerp(light, 0, d);

    return light * DiffuseMultiplier;
}

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

#endif