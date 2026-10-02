#ifndef REDFASTGI_PROBES_INCLUDED
#define REDFASTGI_PROBES_INCLUDED

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

#define VectorSH2 float4

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

struct SH2
{
    VectorSH2 r;
    VectorSH2 g;
    VectorSH2 b;
};

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

VectorSH2 addSH2Vector(VectorSH2 a, VectorSH2 b)
{
    return a + b;
}

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

VectorSH2 mulSH2Vector(VectorSH2 a, float b)
{
    return a * b;
}

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

SH2 addSH2(SH2 a, SH2 b)
{
    SH2 result = a;
    result.r += b.r;
    result.g += b.g;
    result.b += b.b;

    return result;
}

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

SH2 mulSH2(SH2 a, float b)
{
    SH2 result;
    result.r = a.r * b;
    result.g = a.g * b;
    result.b = a.b * b;

    return result;
}

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

VectorSH2 cosineSH2(float3 direction)
{
    return float4(0.886227f, -1.02333f * direction.y, 1.02333f * direction.z, -1.02333f * direction.x);
}

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

VectorSH2 basisSH2(float3 direction)
{
    return float4(0.2820948, -0.4886025 * direction.y, 0.4886025 * direction.z, -0.4886025 * direction.x);
}

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

float3 evaluateSH2(SH2 sh, float3 direction)
{
    VectorSH2 coeffs = basisSH2(direction);

    float3 result;
    result.r = max(dot(coeffs, sh.r), 0);
    result.g = max(dot(coeffs, sh.g), 0);
    result.b = max(dot(coeffs, sh.b), 0);

    return result;
}

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

float evaluateSH2BandGeometrics(float4 sh, float3 normal)
{
    float R0 = sh.x;
    float R0div = 1.0f / max(0.000001f, R0);

    float3 R1 = 0.5f * float3(-sh.w, -sh.y, sh.z);
    float lenR1 = max(0.000001f, length(R1));

    float q = 0.5f * (1.0f + dot(R1 / lenR1, normal));

    float p = 1.0f + 2.0f * lenR1 * R0div;
    float a = (1.0f - lenR1 * R0div) / (1.0f + lenR1 * R0div);

    return R0 * (a + (1.0f - a) * (p + 1.0f) * pow(abs(q), p));
}

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

float3 evaluateSH2Geometrics(SH2 sh, float3 normal)
{
    return float3(evaluateSH2BandGeometrics(sh.r, normal),
                  evaluateSH2BandGeometrics(sh.g, normal),
                  evaluateSH2BandGeometrics(sh.b, normal));
}

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

struct VectorSH3
{
    float4 v0;
    float4 v1;
    float  v2;
};

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

struct SH3
{
    VectorSH3 r;
    VectorSH3 g;
    VectorSH3 b;
};

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

VectorSH3 addSH3Vector(VectorSH3 a, VectorSH3 b)
{
    VectorSH3 result;

    result.v0 = a.v0 + b.v0;
    result.v1 = a.v1 + b.v1;
    result.v2 = a.v2 + b.v2;

    return result;
}

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

void appendSH3Vector(inout VectorSH3 a, VectorSH3 b)
{
    a.v0 = a.v0 + b.v0;
    a.v1 = a.v1 + b.v1;
    a.v2 = a.v2 + b.v2;
}

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

VectorSH3 mulSH3Vector(VectorSH3 a, float b)
{
    VectorSH3 result;

    result.v0 = a.v0 * b;
    result.v1 = a.v1 * b;
    result.v2 = a.v2 * b;

    return result;
}

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

SH3 mulSH3(SH3 a, float b)
{
    SH3 result;

    result.r.v0 = a.r.v0 * b;
    result.r.v1 = a.r.v1 * b;
    result.r.v2 = a.r.v2 * b;

    result.g.v0 = a.g.v0 * b;
    result.g.v1 = a.g.v1 * b;
    result.g.v2 = a.g.v2 * b;

    result.b.v0 = a.b.v0 * b;
    result.b.v1 = a.b.v1 * b;
    result.b.v2 = a.b.v2 * b;

    return result;
}

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

SH3 addSH3(SH3 a, SH3 b)
{
    SH3 result = a;

    result.r.v0 += b.r.v0;
    result.r.v1 += b.r.v1;
    result.r.v2 += b.r.v2;

    result.g.v0 += b.g.v0;
    result.g.v1 += b.g.v1;
    result.g.v2 += b.g.v2;

    result.b.v0 += b.b.v0;
    result.b.v1 += b.b.v1;
    result.b.v2 += b.b.v2;

    return result;
}

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

float convolveSH3(VectorSH3 a, VectorSH3 b)
{
    return dot(a.v0, b.v0) + dot(a.v1, b.v1) + a.v2 * b.v2;
}

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

VectorSH3 basisSH3(half3 direction)
{
    VectorSH3 result;

    result.v0.x = 0.282095f;
    result.v0.y = -0.488603f * direction.y;
    result.v0.z = 0.488603f * direction.z;
    result.v0.w = -0.488603f * direction.x;

    float3 squared = direction * direction;

    result.v1.x = 1.092548f * direction.x * direction.y;
    result.v1.y = -1.092548f * direction.y * direction.z;
    result.v1.z = 0.315392f * (3.0f * squared.z - 1.0f);
    result.v1.w = -1.092548f * direction.x * direction.z;
    result.v2 = 0.546274f * (squared.x - squared.y);

    return result;
}

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

VectorSH3 cosineSH3(float3 direction)
{
    VectorSH3 result;
    result.v0.x = 0.886226925453f;
    result.v0.y = -1.02332670795f * direction.y;
    result.v0.z = 1.02332670795f * direction.z;
    result.v0.w = -1.02332670795f * direction.x;

    float3 squared = direction * direction;

    result.v1.x = 0.85808553081f * direction.x * direction.y;
    result.v1.y = -0.85808553081f * direction.y * direction.z;
    result.v1.z = 0.2477079561f * (3 * squared.z - 1);
    result.v1.w = -0.85808553081f * direction.x * direction.z;
    result.v2 = 0.429042765405f * (squared.x - squared.y);

    return result;
}

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

float3 evaluateSH3(SH3 sh, float3 direction)
{
    VectorSH3 coeffs = basisSH3(direction);

    float3 result;
    result.r = max(convolveSH3(coeffs, sh.r), 0);
    result.g = max(convolveSH3(coeffs, sh.g), 0);
    result.b = max(convolveSH3(coeffs, sh.b), 0);

    return result;
}

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

SH2 loadProbeSH2(ByteAddressBuffer probes, uint entry)
{
    uint base = entry * SH2_STRIDE;

    SH2 probe;
    probe.r = asfloat(probes.Load4(base + FLOAT4_STRIDE * 0));
    probe.g = asfloat(probes.Load4(base + FLOAT4_STRIDE * 1));
    probe.b = asfloat(probes.Load4(base + FLOAT4_STRIDE * 2));

    return probe;
}

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

void storeProbeSH2(RWByteAddressBuffer probes, uint entry, SH2 probe)
{
    uint base = entry * SH2_STRIDE;

    probes.Store4(base + FLOAT4_STRIDE * 0, asuint(probe.r));
    probes.Store4(base + FLOAT4_STRIDE * 1, asuint(probe.g));
    probes.Store4(base + FLOAT4_STRIDE * 2, asuint(probe.b));
}

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

void storeEmptyProbeSH2(RWByteAddressBuffer probes, uint entry)
{
    uint base = entry * SH2_STRIDE;

    probes.Store4(base + FLOAT4_STRIDE * 0, asuint(float4(0, 0, 0, 0)));
    probes.Store4(base + FLOAT4_STRIDE * 1, asuint(float4(0, 0, 0, 0)));
    probes.Store4(base + FLOAT4_STRIDE * 2, asuint(float4(0, 0, 0, 0)));
}

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

SH3 loadProbeSH3(ByteAddressBuffer probes, uint entry)
{
    uint base = entry * SH3_STRIDE;

    SH3 probe;

    probe.r.v0 = asfloat(probes.Load4(base + FLOAT4_STRIDE * 0));
    probe.r.v1 = asfloat(probes.Load4(base + FLOAT4_STRIDE * 1));
    probe.g.v0 = asfloat(probes.Load4(base + FLOAT4_STRIDE * 2));
    probe.g.v1 = asfloat(probes.Load4(base + FLOAT4_STRIDE * 3));
    probe.b.v0 = asfloat(probes.Load4(base + FLOAT4_STRIDE * 4));
    probe.b.v1 = asfloat(probes.Load4(base + FLOAT4_STRIDE * 5));

    float3 v2 = asfloat(probes.Load3(base + FLOAT4_STRIDE * 6));
    probe.r.v2 = v2.r;
    probe.g.v2 = v2.g;
    probe.b.v2 = v2.b;

    return probe;
}

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

void storeProbeSH3(RWByteAddressBuffer probes, uint entry, SH3 probe)
{
    uint base = entry * SH3_STRIDE;

    probes.Store4(base + FLOAT4_STRIDE * 0, asuint(probe.r.v0));
    probes.Store4(base + FLOAT4_STRIDE * 1, asuint(probe.r.v1));
    probes.Store4(base + FLOAT4_STRIDE * 2, asuint(probe.g.v0));
    probes.Store4(base + FLOAT4_STRIDE * 3, asuint(probe.b.v1));
    probes.Store4(base + FLOAT4_STRIDE * 4, asuint(probe.b.v0));
    probes.Store4(base + FLOAT4_STRIDE * 5, asuint(probe.b.v1));

    float3 v2 = float3(probe.r.v2, probe.g.v2, probe.b.v2);
    probes.Store3(base + FLOAT4_STRIDE * 6, asuint(v2));
}

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

void storeEmptyProbeSH3(RWByteAddressBuffer probes, uint entry)
{
    uint base = entry * SH3_STRIDE;

    probes.Store4(base + FLOAT4_STRIDE * 0, asuint(float4(0, 0, 0, 0)));
    probes.Store4(base + FLOAT4_STRIDE * 1, asuint(float4(0, 0, 0, 0)));
    probes.Store4(base + FLOAT4_STRIDE * 2, asuint(float4(0, 0, 0, 0)));
    probes.Store4(base + FLOAT4_STRIDE * 3, asuint(float4(0, 0, 0, 0)));
    probes.Store4(base + FLOAT4_STRIDE * 4, asuint(float4(0, 0, 0, 0)));
    probes.Store4(base + FLOAT4_STRIDE * 5, asuint(float4(0, 0, 0, 0)));
    probes.Store3(base + FLOAT4_STRIDE * 6, asuint(float3(0, 0, 0)));
}

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

float3 loadProbeColor(ByteAddressBuffer probes, uint entry)
{
    return asfloat(probes.Load3(entry * FLOAT4_STRIDE));
}

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

void storeProbeColor(RWByteAddressBuffer probes, uint entry, float3 probe)
{
    uint base = entry * SH2_STRIDE;

    probes.Store3(entry * FLOAT4_STRIDE, asuint(probe));
}

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

#endif