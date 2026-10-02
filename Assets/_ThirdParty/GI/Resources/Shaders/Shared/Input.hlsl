#ifndef REDFASTGI_INPUT_INCLUDED
#define REDFASTGI_INPUT_INCLUDED

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

#ifdef UNITY_CG_INCLUDED

    float4 _ScaledScreenParams;
    float4x4 unity_MatrixInvVP;

    #define UNITY_MATRIX_I_VP unity_MatrixInvVP
    #define UNITY_MATRIX_M unity_ObjectToWorld

    ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    #define SafeNormalize normalize

    ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    float LinearEyeDepth(float depth, float4 zBufferParam)
    {
        return 1.0 / (zBufferParam.z * depth + zBufferParam.w);
    }

#else

    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

    ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    #if !defined(REDFASTGI_VR)

        #if defined(STEREO_INSTANCING_ON) || defined(STEREO_MULTIVIEW_ON)
            #define REDFASTGI_VR 1
        #else
            #define REDFASTGI_VR 0
        #endif

    #endif

    ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    #if REDFASTGI_URP

    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Input.hlsl"

    #else

    ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    int unity_StereoEyeIndex;

    #if REDFASTGI_VR
        #define SLICE_ARRAY_INDEX unity_StereoEyeIndex
    #else
        #define SLICE_ARRAY_INDEX 0
    #endif

    #if REDFASTGI_VR
        #define glstate_matrix_projection unity_StereoMatrixP[unity_StereoEyeIndex]
        #define unity_MatrixV unity_StereoMatrixV[unity_StereoEyeIndex]
        #define unity_MatrixInvV unity_StereoMatrixInvV[unity_StereoEyeIndex]
        #define unity_MatrixInvVP unity_StereoMatrixInvVP[unity_StereoEyeIndex]
        #define unity_MatrixVP unity_StereoMatrixVP[unity_StereoEyeIndex]

        #define unity_CameraProjection unity_StereoCameraProjection[unity_StereoEyeIndex]
        #define unity_CameraInvProjection unity_StereoCameraInvProjection[unity_StereoEyeIndex]
        #define unity_WorldToCamera unity_StereoWorldToCamera[unity_StereoEyeIndex]
        #define unity_CameraToWorld unity_StereoCameraToWorld[unity_StereoEyeIndex]
        #define _WorldSpaceCameraPos unity_StereoWorldSpaceCameraPos[unity_StereoEyeIndex]
    #endif

    #define UNITY_MATRIX_P glstate_matrix_projection
    #define UNITY_MATRIX_V unity_MatrixV
    #define UNITY_MATRIX_I_V unity_MatrixInvV
    #define UNITY_MATRIX_I_VP unity_MatrixInvVP
    #define UNITY_MATRIX_VP unity_MatrixVP
    #define UNITY_MATRIX_M unity_ObjectToWorld

    CBUFFER_START(UnityPerCamera)

        #if !REDFASTGI_VR
            float3 _WorldSpaceCameraPos;
            float4x4 unity_MatrixInvVP;
        #endif

        float4 _ProjectionParams;
        float4 _ScreenParams;
        float4 _ScaledScreenParams;
        float4 _ZBufferParams;

    CBUFFER_END

    CBUFFER_START(UnityPerCameraRare)
        float4 unity_CameraWorldClipPlanes[6];

        #if !REDFASTGI_VR
            float4x4 unity_CameraProjection;
            float4x4 unity_CameraInvProjection;
            float4x4 unity_WorldToCamera;
            float4x4 unity_CameraToWorld;
        #endif
    CBUFFER_END

    ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    #endif // REDFASTGI_URP

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

#endif // UNITY_CG_INCLUDED

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

#endif
