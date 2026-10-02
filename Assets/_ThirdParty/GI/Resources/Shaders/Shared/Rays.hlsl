#ifndef REDFASTGI_RAYS_INCLUDED
#define REDFASTGI_RAYS_INCLUDED

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

struct Hit
{
    bool has_hit;
    float dist;
};

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

struct HitSurface
{
    float3 normal;
    float3 color;
    float emission;
};

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

uint encodeRay(uint2 atlas_coord, uint2 texel)
{
    return ((atlas_coord.x & 0x3FF) << 0) |
        ((atlas_coord.y & 0x3FF) << 10) |
        ((texel.x & 0x1F) << 20) |
        ((texel.y & 0x1F) << 25);
}

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

void decodeRay(uint data, inout uint2 atlas_coord, inout uint2 texel)
{
    atlas_coord.x = (data >> 0) & 0x3FF;
    atlas_coord.y = (data >> 10) & 0x3FF;
    texel.x = (data >> 20) & 0x1F;
    texel.y = (data >> 25) & 0x1F;
}

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

uint encodeCoord(uint2 screen_coord)
{
    return ((screen_coord.x & 0xFFFF) << 0) |
           ((screen_coord.y & 0xFFFF) << 16);
}

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

void decodeCoord(uint data, inout uint2 screen_coord)
{
    screen_coord.x = (data >> 0) & 0xFFFF;
    screen_coord.y = (data >> 16) & 0xFFFF;
}

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

float3x3 tangentBasis(float3 tangent_z)
{
    const float tangent_sign = tangent_z.z >= 0 ? 1 : -1;
    const float a = -rcp(tangent_sign + tangent_z.z);
    const float b = tangent_z.x * tangent_z.y * a;

    float3 tangent_x = float3(1 + tangent_sign * a * tangent_z.x * tangent_z.x, tangent_sign * b, -tangent_sign * tangent_z.x);
    float3 tangent_y = float3(b, tangent_sign + a * tangent_z.y * tangent_z.y, -tangent_z.y);

    return float3x3(tangent_x, tangent_y, tangent_z);
}

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

#endif