using UnityEngine;

namespace VolumetricFogAndMist2 {

    public static class ShaderParams {
        public static readonly int MainTex = Shader.PropertyToID("_MainTex");
        public static readonly int NoiseTex = Shader.PropertyToID("_NoiseTex");
        public static readonly int DetailTex = Shader.PropertyToID("_DetailTex");
        public static readonly int DetailOffset = Shader.PropertyToID("_DetailOffset");
        public static readonly int DetailColor = Shader.PropertyToID("_DetailColor");
        public static readonly int DetailData = Shader.PropertyToID("_DetailData");
        public static readonly int Density = Shader.PropertyToID("_Density");
        public static readonly int NativeLightsData = Shader.PropertyToID("_NativeLightsData");
        public static readonly int APVIntensityMultiplier = Shader.PropertyToID("_APVIntensityMultiplier");
        public static readonly int APVMaxIntensity = Shader.PropertyToID("_APVMaxIntensity");
        public static readonly int ShadowData = Shader.PropertyToID("_ShadowData");
        public static readonly int LightDiffusionData = Shader.PropertyToID("_LightDiffusionData");
        public static readonly int DiffusionFloor = Shader.PropertyToID("_DiffusionFloor");
        public static readonly int DeepObscurance = Shader.PropertyToID("_DeepObscurance");
        public static readonly int NoiseScale = Shader.PropertyToID("_NoiseScale");
        public static readonly int SpecularThreshold = Shader.PropertyToID("_SpecularThreshold");
        public static readonly int SpecularIntensity = Shader.PropertyToID("_SpecularIntensity");
        public static readonly int SpecularColor = Shader.PropertyToID("_SpecularColor");
        public static readonly int NoiseFinalMultiplier = Shader.PropertyToID("_NoiseFinalMultiplier");
        public static readonly int NoiseStrength = Shader.PropertyToID("_NoiseStrength");
        public static readonly int TurbulenceAmount = Shader.PropertyToID("_TurbulenceAmount");
        public static readonly int FogOfWarCenterAdjusted = Shader.PropertyToID("_FogOfWarCenterAdjusted");
        public static readonly int FogOfWarSize = Shader.PropertyToID("_FogOfWarSize");
        public static readonly int FogOfWarCenter = Shader.PropertyToID("_FogOfWarCenter");
        public static readonly int FogOfWarTexture = Shader.PropertyToID("_FogOfWar");
        public static readonly int DistanceData = Shader.PropertyToID("_DistanceData");
        public static readonly int MaxDistanceData = Shader.PropertyToID("_MaxDistanceData");
        public static readonly int DepthGradientTexture = Shader.PropertyToID("_DepthGradientTex");
        public static readonly int HeightGradientTexture = Shader.PropertyToID("_HeightGradientTex");
        public static readonly int RaymarchSettings = Shader.PropertyToID("_RayMarchSettings");
        public static readonly int NearStepping = Shader.PropertyToID("_NearStepping");
        public static readonly int SunDir = Shader.PropertyToID("_SunDir");
        public static readonly int Color = Shader.PropertyToID("_Color");
        public static readonly int WindDirection = Shader.PropertyToID("_WindDirection");
        public static readonly int DetailWindDirection = Shader.PropertyToID("_DetailWindDirection");
        public static readonly int LightColor = Shader.PropertyToID("_LightColor");
        public static readonly int BoundsBorder = Shader.PropertyToID("_BoundsBorder");
        public static readonly int BoundsExtents = Shader.PropertyToID("_BoundsExtents");
        public static readonly int BoundsCenter = Shader.PropertyToID("_BoundsCenter");
        public static readonly int BoundsData = Shader.PropertyToID("_BoundsData");
        public static readonly int RotationInvMatrix = Shader.PropertyToID("_InvRotMatrix");
        public static readonly int RotationMatrix = Shader.PropertyToID("_RotMatrix");
        public static readonly int SurfaceCaptureMatrix = Shader.PropertyToID("_SurfaceCaptureMatrix");
        public static readonly int SurfaceDepthTexture = Shader.PropertyToID("_SurfaceDepthTexture");
        public static readonly int SurfaceData = Shader.PropertyToID("_SurfaceData");
        public static readonly int PointLightCount = Shader.PropertyToID("_VF2_PointLightCount");
        public static readonly int PointLightParams = Shader.PropertyToID("_VF2_PointLightParams");
        public static readonly int PointLightPositions = Shader.PropertyToID("_VF2_FogPointLightPosition");
        public static readonly int PointLightColors = Shader.PropertyToID("_VF2_PointLightColor");
        public static readonly int VoidCount = Shader.PropertyToID("_VF2_FogVoidCount");
        public static readonly int VoidSizes = Shader.PropertyToID("_VF2_FogVoidSizes");
        public static readonly int VoidPositions = Shader.PropertyToID("_VF2_FogVoidPositions");
        public static readonly int VoidMatrices = Shader.PropertyToID("_VF2_FogVoidMatrices");
        public const string CustomDepthTextureName = "_CustomDepthTexture";
        public static readonly int CustomDepthTexture = Shader.PropertyToID(CustomDepthTextureName);
        public static readonly int CustomDepthAlphaCutoff = Shader.PropertyToID("_AlphaCutOff");
        public static readonly int CustomDepthBaseMap = Shader.PropertyToID("_BaseMap");
        public static readonly int SpotLightCount = Shader.PropertyToID("_VF2_SpotLightCount");
        public static readonly int SpotLightPositions = Shader.PropertyToID("_VF2_FogSpotLightPosition");
        public static readonly int SpotLightDirections = Shader.PropertyToID("_VF2_FogSpotLightDirection");
        public static readonly int SpotLightColors = Shader.PropertyToID("_VF2_SpotLightColor");
        public static readonly int BlueNoiseTexture = Shader.PropertyToID("_BlueNoise");
        public static readonly int DistantFogData = Shader.PropertyToID("_DistantFogData");
        public static readonly int DistantFogData2 = Shader.PropertyToID("_DistantFogData2");
        public static readonly int DistantFogNoiseData = Shader.PropertyToID("_DistantFogNoiseData");
        public static readonly int DistantFogDistanceNoiseData = Shader.PropertyToID("_DistantFogDistanceNoiseData");
        public static readonly int DistantFogNoiseTexture = Shader.PropertyToID("_DistantFogNoiseTexture");
        public static readonly int DistantFogNoiseWind = Shader.PropertyToID("_DistantFogNoiseWind");
        public static readonly int CullMode = Shader.PropertyToID("_Cull");
        public static readonly int SkipRendering = Shader.PropertyToID("_SkipRendering");
        public static readonly int VolumeLayerMask = Shader.PropertyToID("_VFVolumeLayer");

        public const string SKW_CUSTOM_DEPTH_ALPHA_TEST = "DEPTH_PREPASS_ALPHA_TEST";

        public const string SKW_SHAPE_BOX = "VF2_SHAPE_BOX";
        public const string SKW_SHAPE_SPHERE = "VF2_SHAPE_SPHERE";
        public const string SKW_POINT_LIGHTS = "VF2_POINT_LIGHTS";
        public const string SKW_NATIVE_LIGHTS = "VF2_NATIVE_LIGHTS";
        public const string SKW_VOIDS = "VF2_VOIDS";
        public const string SKW_FOW = "VF2_FOW";
        public const string SKW_RECEIVE_SHADOWS = "VF2_RECEIVE_SHADOWS";
        public const string SKW_DISTANCE = "VF2_DISTANCE";
        public const string SKW_CONSTANT_DENSITY = "VF2_CONSTANT_DENSITY";
        public const string SKW_DETAIL_NOISE = "VF2_DETAIL_NOISE";
        public const string SKW_SURFACE = "VF2_SURFACE";
        public const string SKW_DEPTH_PREPASS = "VF2_DEPTH_PREPASS";
        public const string SKW_DEPTH_GRADIENT = "VF2_DEPTH_GRADIENT";
        public const string SKW_HEIGHT_GRADIENT = "VF2_HEIGHT_GRADIENT";
        public const string SKW_DIRECTIONAL_COOKIE = "VF2_LIGHT_COOKIE";
        public const string SKW_APV = "VF2_APV";
        public const string SKW_DIFFUSION_SMOOTH = "VF2_DIFFUSION_SMOOTH";
        public const string SKW_DIFFUSION_STRONG = "VF2_DIFFUSION_STRONG";

        public const string SKW_DISTANT_FOG_NOISE = "VF2_DISTANT_FOG_NOISE";
        public const string SKW_DISTANT_FOG_PEELING = "VF2_DISTANT_FOG_PEELING";
    }

}

