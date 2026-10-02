#ifndef REDFASTGI_SURFELS_INCLUDED
#define REDFASTGI_SURFELS_INCLUDED

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

uint positionToSurfelCascade(float3 position)
{
    float3 coord = (position - ReferencePosition) / SURFEL_CELL_DIM;
    float max_coord = max(abs(coord.x), max(abs(coord.y), abs(coord.z)));
    float cascade = log2(max_coord / (SURFEL_CASCADE_DIM / 2 - 1));

    return uint(clamp(ceil(max(cascade, 0)), 0, SURFEL_CASCADE_COUNT - 1));
}

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

uint positionToSurfelCell(float3 position, float3 normal, uint cascade)
{
    float cell_dim = SURFEL_CELL_DIM * (1u << cascade);

    float3 lookup_position = position + normal * 0.5 * cell_dim;

    int3 cell_coord_i = int3(floor(lookup_position / cell_dim)) - int3(SurfelCascadeOrigins[cascade].xyz);
    uint3 cell_coord = uint3(clamp(cell_coord_i, 0, SURFEL_CASCADE_DIM - 1));

    uint cell_index = cell_coord.x +
                      (cell_coord.y * SURFEL_CASCADE_DIM) +
                      (cell_coord.z * SURFEL_CASCADE_DIM * SURFEL_CASCADE_DIM) +
                      (cascade * SURFEL_CASCADE_CELL_COUNT);

    return cell_index;
}

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

float3 surfelEntryPosition(float3 origin, float3 position, uint cascade)
{
    float cell_dim = SURFEL_CELL_DIM * (1u << cascade);

    float3 offset = origin - position;
    offset *= cell_dim / max(cell_dim / 0.5, length(offset));

    return position + offset;
}

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

#endif