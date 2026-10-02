#ifndef REDFASTGI_PROBE_GRID_INCLUDED
#define REDFASTGI_PROBE_GRID_INCLUDED

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

uint positionToProbeCascadeUnclamped(float3 position)
{
    float3 coord = (position - ReferencePosition) / ProbeCell;
    float max_coord = max(abs(coord.x), max(abs(coord.y), abs(coord.z)));
    float cascade = log2(max_coord / (PROBE_CASCADE_DIM / 2 - 1));

    return uint(ceil(max(cascade, 0)));
}

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

uint positionToProbeCascade(float3 position)
{
    uint cascade = positionToProbeCascadeUnclamped(position);
    return min(cascade, ProbeCascadeCount - 1);
}

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

uint3 positionToProbeCellCoord(float3 position, uint cascade)
{
    float cell_dim = ProbeCell * (1u << cascade);

    int3 cell_coord_i = int3(floor(position / cell_dim)) - int3(ProbeCascadeOrigins[cascade].xyz);
    uint3 cell_coord = uint3(clamp(cell_coord_i, 0, PROBE_CASCADE_DIM - 1));

    return cell_coord;
}

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

float3 positionToProbeCellCoordFloat(float3 position, uint cascade)
{
    float cell_dim = ProbeCell * (1u << cascade);
    return (position / cell_dim) - ProbeCascadeOrigins[cascade].xyz;
}

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

uint positionToProbeCell(float3 position, uint cascade)
{
    uint3 cell_coord = positionToProbeCellCoord(position, cascade);

    uint cell_index = cell_coord.x +
                      (cell_coord.y * PROBE_CASCADE_DIM) +
                      (cell_coord.z * PROBE_CASCADE_DIM * PROBE_CASCADE_DIM) +
                      (cascade * PROBE_CASCADE_CELL_COUNT);

    return cell_index;
}

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

float positionToProbeMaxCellOffset(float3 position)
{
    float cell_dim = ProbeCell * (1u << positionToProbeCascade(position));
    return cell_dim * 2.0f;
}

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

#endif