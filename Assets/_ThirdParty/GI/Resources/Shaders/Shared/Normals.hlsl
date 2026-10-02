#ifndef REDFASTGI_NORMALS_INCLUDED
#define REDFASTGI_NORMALS_INCLUDED

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

#include "Input.hlsl"
#include "Packing.hlsl"

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

half3 unpackNormal(half3 pn)
{
    half2 remappedOctNormalWS = half2(unpack888ToFloat2(pn));          // values between [ 0, +1]
    half2 octNormalWS = remappedOctNormalWS.xy * half(2.0) - half(1.0);// values between [-1, +1]
    return normalize(half3(unpackNormalOctQuadEncode(octNormalWS)));              // values between [-1, +1]
}

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

half3 packNormal(float3 normalWS)
{
    float2 octNormalWS = packNormalOctQuadEncode(normalWS);           // values between [-1, +1], must use fp32 on some platforms.
    float2 remappedOctNormalWS = saturate(octNormalWS * 0.5 + 0.5);   // values between [ 0,  1]

    return packFloat2To888(remappedOctNormalWS);      // values between [ 0,  1]
}

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

#if REDFASTGI_VR

    #if CAMERA_NORMALS

        Texture2DArray _CameraNormalsTexture;
        SamplerState sampler_CameraNormalsTexture;

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////

        float3 loadNormals(float2 uv)
        {
            float3 n = _CameraNormalsTexture.SampleLevel(sampler_CameraNormalsTexture, float3(uv, SLICE_ARRAY_INDEX), 0).rgb;

            #if defined(_GBUFFER_NORMALS_OCT)
                return unpackNormal(n);
            #else
                return n;
            #endif
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////

    #else

        Texture2DArray _GBuffer2;
        SamplerState sampler_GBuffer2;

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////

        float3 loadNormals(float2 uv)
        {
            float3 n = _GBuffer2.SampleLevel(sampler_GBuffer2, float3(uv, SLICE_ARRAY_INDEX), 0).rgb;

            #if defined(_GBUFFER_NORMALS_OCT)
                return unpackNormal(n);
            #else
                return n;
            #endif
        }

    #endif

    ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

#else

    ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    #if CAMERA_NORMALS

        Texture2D _CameraNormalsTexture;
        SamplerState sampler_CameraNormalsTexture;

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////

        float3 loadNormals(float2 uv)
        {
            float3 n = _CameraNormalsTexture.SampleLevel(sampler_CameraNormalsTexture, uv, 0).rgb;

            #if defined(_GBUFFER_NORMALS_OCT)
                return unpackNormal(n);
            #else
                return n;
            #endif
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////

    #else

        Texture2D _GBuffer2;
        SamplerState sampler_GBuffer2;

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////

        float3 loadNormals(float2 uv)
        {
            float3 n = _GBuffer2.SampleLevel(sampler_GBuffer2, uv, 0).rgb;

            #if defined(_GBUFFER_NORMALS_OCT)
                return unpackNormal(n);
            #else
                return n;
            #endif
        }

    #endif

#endif

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

#endif