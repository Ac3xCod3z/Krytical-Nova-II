using System.Linq;
using KryticalNova.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using VolumetricFogAndMist2;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Volumetric atmosphere for the open zone (Rendering-Bible.md, "Volumetrics"): Kronnect Volumetric Fog &amp; Mist 2
    /// renders a terrain-fitted ground mist that receives the sun's shadow map, so the sun/moon scatters through it as
    /// shadowed light shafts between buildings. Kronnect Volumetric Lights is registered on the PC renderer for local
    /// lamps/torches (add a VolumetricLight next to any point/spot light). Exponential RenderSettings fog stays for
    /// distance haze. PC renderer only; mobile keeps the cheap path. Re-runnable: it updates what it owns.
    /// </summary>
    public static class AtmosphereSetup
    {
        private const string PcRendererPath = "Assets/_Game/Rendering/Settings/PC_Renderer.asset";
        public const string MistProfilePath = "Assets/_Game/World/Data/Assets/Weather/VF_GroundMist.asset";
        private const string MistName = "Ground Mist";
        private const string ManagerName = "Volumetric Fog Manager";
        private const string LightingRootName = "Lighting";
        private const int GroundLayer = 3; // Zone01 Terrain

        [MenuItem("Krytical Nova/Lighting/Setup Volumetric Atmosphere")]
        public static void Setup()
        {
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(PcRendererPath);
            if (renderer == null) { Debug.LogError($"[Atmosphere] Missing {PcRendererPath}"); return; }
            RendererFeatureUtil.Ensure<VolumetricFogRenderFeature>(renderer, "VolumetricFog", f => { });
            RendererFeatureUtil.Ensure<VolumetricLights.VolumetricLightsRenderFeature>(renderer, "VolumetricLights", f => { });

            var profile = EnsureProfile();
            var keyLight = RenderSettings.sun != null ? RenderSettings.sun
                : Object.FindObjectsByType<Light>(FindObjectsSortMode.None).FirstOrDefault(l => l.type == LightType.Directional);

            var lightingRoot = GameObject.Find(LightingRootName);
            var manager = EnsureManager(keyLight, lightingRoot != null ? lightingRoot.transform : null);
            var mist = EnsureMist(profile, lightingRoot != null ? lightingRoot.transform : null);
            EnsureDriver(mist);

            EditorSceneManager.MarkSceneDirty(mist.gameObject.scene);
            Debug.Log($"[Atmosphere] Volumetric fog + lights features on PC_Renderer; '{MistName}' uses {MistProfilePath}; " +
                      $"manager synced to '{(manager.sun != null ? manager.sun.name : "none")}'.");
        }

        private static VolumetricFogProfile EnsureProfile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumetricFogProfile>(MistProfilePath);
            bool created = profile == null;
            if (created) profile = ScriptableObject.CreateInstance<VolumetricFogProfile>();

            // Low, soft, slowly drifting mist that hugs the terrain; weather scales density at runtime.
            profile.shape = VolumetricFogShape.Box;
            if (profile.noiseTexture == null) profile.noiseTexture = Resources.Load<Texture2D>("Textures/NoiseTex256");
            profile.density = 0.04f;         // per-metre: a 1 km box accumulates fast
            profile.noiseScale = 22f;
            profile.noiseFinalMultiplier = 0.7f;
            profile.useDetailNoise = false;
            profile.raymarchMinStep = 0.6f;     // cheap: thin layer, long steps
            profile.jittering = 0.5f;
            profile.turbulence = 0.35f;
            profile.windDirection = new Vector3(0.015f, 0f, 0.008f);
            profile.albedo = new Color(0.86f, 0.87f, 0.9f, 0.6f);
            profile.brightness = 1f;
            profile.deepObscurance = 0.85f;
            profile.dayNightCycle = true;       // light colour/intensity from the manager's sun (our sun/moon light)
            profile.ambientLightMultiplier = 0.6f;
            profile.lightDiffusionModel = DiffusionModel.Smooth;
            profile.lightDiffusionPower = 24f;
            profile.lightDiffusionIntensity = 0.35f;
            profile.lightDiffusionBackScatter = 0.2f;
            profile.diffusionFloor = 0.75f;
            profile.receiveShadows = true;      // buildings cut the sun -> volumetric shafts in the streets
            profile.shadowMaxDistance = 120f;
            profile.terrainFit = true;
            profile.terrainFitResolution = VolumetricFog.HeightmapCaptureResolution._256;
            profile.terrainFogHeight = 8f;
            profile.terrainLayerMask = 1 << GroundLayer; // terrain only, or rooftops get captured as ground
            profile.terrainFogMinAltitude = -5f;
            profile.terrainFogMaxAltitude = 90f;
            profile.distantFog = false;         // RenderSettings exponential fog already owns the horizon
            profile.maxDistance = 300f;
            profile.maxDistanceFallOff = 0.5f;

            if (created) AssetDatabase.CreateAsset(profile, MistProfilePath);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        private static VolumetricFogManager EnsureManager(Light keyLight, Transform parent)
        {
            var manager = Object.FindAnyObjectByType<VolumetricFogManager>(FindObjectsInactive.Include);
            if (manager == null)
            {
                var go = new GameObject(ManagerName);
                Undo.RegisterCreatedObjectUndo(go, "Volumetric Fog Manager");
                manager = go.AddComponent<VolumetricFogManager>();
            }
            Undo.RecordObject(manager, "Volumetric Fog Manager");
            manager.gameObject.name = ManagerName;
            if (parent != null) manager.transform.SetParent(parent, false);
            manager.mainManager = true;
            manager.sun = keyLight;
            manager.moon = null;            // one light plays both roles (EnvironmentPresenter swaps it at dusk)
            manager.downscaling = 2f;       // half-res raymarch + edge-preserving upscale
            manager.downscalingEdgeDepthThreshold = 0.05f;
            manager.blurPasses = 1;
            manager.blurDownscaling = 1f;
            manager.blurEdgePreserve = true;
            manager.scattering = 0f;        // Bloom already owns glow
            manager.ditherStrength = 0.4f;
            EditorUtility.SetDirty(manager);
            return manager;
        }

        private static VolumetricFog EnsureMist(VolumetricFogProfile profile, Transform parent)
        {
            var fog = Object.FindObjectsByType<VolumetricFog>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(f => f.name == MistName);
            if (fog == null)
            {
                var go = VolumetricFogManager.CreateFogVolume(MistName);
                Undo.RegisterCreatedObjectUndo(go, "Ground Mist");
                fog = go.GetComponent<VolumetricFog>();
            }
            Undo.RecordObject(fog, "Ground Mist");
            if (parent != null) fog.transform.SetParent(parent, false);
            // Covers the whole 1 km terrain; terrain fit keeps the layer glued to hills and the river bed.
            fog.transform.SetPositionAndRotation(new Vector3(0f, 20f, 0f), Quaternion.identity);
            fog.transform.localScale = new Vector3(1000f, 110f, 1000f);
            fog.profile = profile;
            fog.enableNativeLights = false;
            fog.enablePointLights = false;
            fog.enableAPV = true;           // night mist picks up the baked probe lighting
            fog.apvIntensityMultiplier = 0.6f;
            fog.enableFade = false;
            // Re-enable so the fog rebuilds its materials / noise targets for the new profile.
            fog.forceTerrainCaptureUpdate = true;
            fog.enabled = false;
            fog.enabled = true;
            EditorUtility.SetDirty(fog);
            return fog;
        }

        private static void EnsureDriver(VolumetricFog mist)
        {
            var presenter = Object.FindAnyObjectByType<EnvironmentPresenter>(FindObjectsInactive.Include);
            var host = presenter != null ? presenter.gameObject : mist.gameObject;
            if (!host.TryGetComponent<VolumetricAtmosphereDriver>(out var driver))
                driver = Undo.AddComponent<VolumetricAtmosphereDriver>(host);
            var so = new SerializedObject(driver);
            so.FindProperty("fog").objectReferenceValue = mist;
            so.ApplyModifiedProperties();
        }
    }
}
