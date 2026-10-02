#ifndef REDFASTGI_INSTANCES_INCLUDED
#define REDFASTGI_INSTANCES_INCLUDED

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

uint loadAssetMapping(ByteAddressBuffer buffer, uint instance)
{
    uint offset = instance * 160;
    return buffer.Load(offset);
}

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

uint loadHashID(ByteAddressBuffer buffer, uint instance)
{
    uint offset = instance * 160 + 4;
    return buffer.Load(offset);
}

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

float3 loadScale(ByteAddressBuffer buffer, uint instance)
{
    uint offset = instance * 160 + 16;
    return asfloat(buffer.Load3(offset));
}

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

float4x4 loadTransform(ByteAddressBuffer buffer, uint instance)
{
    uint offset = instance * 160 + 32;

    return float4x4(
        asfloat(buffer.Load4(offset + 0)),
        asfloat(buffer.Load4(offset + 16)),
        asfloat(buffer.Load4(offset + 32)),
        asfloat(buffer.Load4(offset + 48))
    );
}

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

float4x4 loadInverseTransform(ByteAddressBuffer buffer, uint instance)
{
    uint offset = instance * 160 + 32 + 64;

    return float4x4(
        asfloat(buffer.Load4(offset + 0)),
        asfloat(buffer.Load4(offset + 16)),
        asfloat(buffer.Load4(offset + 32)),
        asfloat(buffer.Load4(offset + 48))
    );
}

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

#endif