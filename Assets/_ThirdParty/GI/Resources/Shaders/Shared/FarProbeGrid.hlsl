#ifndef REDFASTGI_FAR_PROBE_GRID_INCLUDED
#define REDFASTGI_FAR_PROBE_GRID_INCLUDED

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

uint positionToFarProbeCascadeUnclamped(float3 position)
{
    float3 coord = (position - ReferencePosition) / FarProbeCell;
    float max_coord = max(abs(coord.x), max(abs(coord.y), abs(coord.z)));
    float cascade = log2(max_coord / (FAR_PROBE_CASCADE_DIM / 2 - 1));

    return uint(ceil(max(cascade, 0)));
}

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

uint positionToFarProbeCascade(float3 position)
{
    uint cascade = positionToFarProbeCascadeUnclamped(position);
    return min(cascade, FarProbeCascadeCount - 1);
}

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

uint3 positionToFarProbeCellCoord(float3 position, uint cascade)
{
    float cell_dim = FarProbeCell * (1u << cascade);

    int3 cell_coord_i = int3(floor(position / cell_dim)) - int3(FarProbeCascadeOrigins[cascade].xyz);
    uint3 cell_coord = uint3(clamp(cell_coord_i, 0, FAR_PROBE_CASCADE_DIM - 1));

    return cell_coord;
}

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

float3 positionToFarProbeCellCoordFloat(float3 position, uint cascade)
{
    float cell_dim = FarProbeCell * (1u << cascade);
    return (position / cell_dim) - FarProbeCascadeOrigins[cascade].xyz;
}

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

uint positionToFarProbeCell(float3 position, uint cascade)
{
    uint3 cell_coord = positionToFarProbeCellCoord(position, cascade);

    uint cell_index = cell_coord.x +
                      (cell_coord.y * FAR_PROBE_CASCADE_DIM) +
                      (cell_coord.z * FAR_PROBE_CASCADE_DIM * FAR_PROBE_CASCADE_DIM) +
                      (cascade * FAR_PROBE_CASCADE_CELL_COUNT);

    return cell_index;
}

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

float minFarProbeTraceDistanceAtCascade(uint cascade)
{
    static const float SQRT_3 = sqrt(3.0f);

    float cell_dim = FarProbeCell * (1u << cascade);
    return cell_dim * SQRT_3;
}

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

float minFarProbeTraceDistance(float3 position)
{
    uint cascade = positionToFarProbeCascade(position);
    return minFarProbeTraceDistanceAtCascade(cascade);
}

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

#endif