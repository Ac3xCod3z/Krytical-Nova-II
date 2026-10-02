#ifndef REDFASTGI_AABB_INCLUDED
#define REDFASTGI_AABB_INCLUDED

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

float2 intersectAABB(float3 ray_origin, float3 ray_end, float3 box_min, float3 box_max)
{
    float3 inv_direction = 1.0f / (ray_end - ray_origin);

    float3 i0 = (box_min - ray_origin) * inv_direction;
    float3 i1 = (box_max - ray_origin) * inv_direction;

    float3 close = min(i0, i1);
    float3 far = max(i0, i1);

    float2 intersections;
    intersections.x = max(close.x, max(close.y, close.z));
    intersections.y = min(far.x,   min(far.y,   far.z));

    return saturate(intersections);
}

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

#endif