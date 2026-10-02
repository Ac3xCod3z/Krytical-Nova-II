#ifndef REDFASTGI_DEPTH_INCLUDED
#define REDFASTGI_DEPTH_INCLUDED

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

#include "Input.hlsl"

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

#if REDFASTGI_VR

    Texture2DArray _CameraDepthTexture;
    SamplerState sampler_CameraDepthTexture;

    Texture2DArray _LastCameraDepthTexture;
    SamplerState sampler_LastCameraDepthTexture;

    ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    float sampleSceneDepth(float2 uv)
    {
        return _CameraDepthTexture.SampleLevel(sampler_CameraDepthTexture, float3(uv, SLICE_ARRAY_INDEX), 0).r;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    float sampleLastSceneDepth(float2 uv)
    {
        return _LastCameraDepthTexture.SampleLevel(sampler_LastCameraDepthTexture, float3(uv, SLICE_ARRAY_INDEX), 0).r;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

#else

    ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    Texture2D _CameraDepthTexture;
    SamplerState sampler_CameraDepthTexture;

    Texture2D _LastCameraDepthTexture;
    SamplerState sampler_LastCameraDepthTexture;

    ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    float sampleSceneDepth(float2 uv)
    {
        return _CameraDepthTexture.SampleLevel(sampler_CameraDepthTexture, uv, 0).r;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    float sampleLastSceneDepth(float2 uv)
    {
        return _LastCameraDepthTexture.SampleLevel(sampler_LastCameraDepthTexture, uv, 0).r;
    }

#endif

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

float actualDepth(float depth)
{
	#if UNITY_REVERSED_Z
        return depth;
    #else
        // Adjust Z to match NDC for OpenGL ([-1, 1])
        return lerp(UNITY_NEAR_CLIP_VALUE, 1, depth);
    #endif
}

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

float4 clipSpacePosition(float2 position_ndc, float device_depth)
{
    float4 position_cs = float4(position_ndc * 2.0 - 1.0, device_depth, 1.0);

#if UNITY_UV_STARTS_AT_TOP
    position_cs.y = -position_cs.y;
#endif

    return position_cs;
}

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

float3 viewSpacePosition(float2 position_ndc, float device_depth, float4x4 projection_inverse)
{
    float4 position_cs = clipSpacePosition(position_ndc, device_depth);
    float4 position_vs = mul(projection_inverse, position_cs);
    position_vs.z = -position_vs.z;

    return position_vs.xyz / position_vs.w;
}

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

float3 viewSpacePositionAtFrag(float2 position)
{
    float2 uv = position / _ScaledScreenParams.xy;
    float depth = actualDepth(sampleSceneDepth(uv));

    return viewSpacePosition(uv, depth, UNITY_MATRIX_I_VP);
}

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

float3 worldSpacePosition(float2 position_ndc, float device_depth, float4x4 view_projection_inverse)
{
    float4 position_cs  = clipSpacePosition(position_ndc, device_depth);
    float4 position_ws = mul(view_projection_inverse, position_cs);

    return position_ws.xyz / position_ws.w;
}

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

float3 cameraRayAtFrag(float2 position)
{
    float2 uv = position / _ScaledScreenParams.xy;

    #if UNITY_REVERSED_Z
        float depth = 0;
    #else
        float depth = 1;
    #endif

    float3 position_ws = worldSpacePosition(uv, depth, UNITY_MATRIX_I_VP);
    return normalize(position_ws - _WorldSpaceCameraPos);
}

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

#endif