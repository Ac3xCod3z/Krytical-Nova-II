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
    /// renders a terrain-fitted ground fog for fog weather (shadowed sun shafts between buildings) and low
    /// dusk mist volumes over the lakes and rivers; VolumetricAtmosphereDriver switches both by weather and time. Kronnect Volumetric Lights is registered on the PC renderer for local
    /// lamps/torches (add a VolumetricLight next to any point/spot light). Exponential RenderSettings fog stays for
    /// distance haze. PC renderer only; mobile keeps the cheap path. Re-runnable: it updates what it owns.
    /// </summary>
    public static class AtmosphereSetup
    {
        private const string PcRendererPath = "Assets/_Game/Rendering/Settings/PC_Renderer.asset";
        public const string MistProfilePath = "Assets/_Game/World/Data/Assets/Weather/VF_GroundMist.asset";
        public const string WaterMistProfilePath = "Assets/_Game/World/Data/Assets/Weather/VF_WaterMist.asset";
        private const string WaterMistRootName = "Water Mist";
        private const float RiverSegmentLength = 70f; // merge river points so the whole river is ~a dozen volumes
        private const string MistName = "Ground Mist";
        private const string ManagerName = "Volumetric Fog Manager";
        private const string LightingRootName = "Lighting";
        private const int GroundLayer = 3; // Zone01 Terrain

        [MenuItem("Krytical Nova/Lighting/Setup Volumetric Atmosphere")]
        public static void Setup()
        {
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(PcRendererPath);
            if (renderer == null) { Debug.LogError($"[Atmosphere] Missing {PcRendererPath}"); return; }
            // After transparents: the water surface is transparent, so fog drawn before it gets painted over and no mist
            // ever shows above lakes or rivers. (renderPassEventOrder is the value the feature actually injects at.)
            RendererFeatureUtil.Ensure<VolumetricFogRenderFeature>(renderer, "VolumetricFog", f =>
            {
                f.renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
                f.renderPassEventOrder = (int)RenderPassEvent.AfterRenderingTransparents;
            });
            RendererFeatureUtil.Ensure<VolumetricLights.VolumetricLightsRenderFeature>(renderer, "VolumetricLights", f => { });

            var profile = EnsureProfile();
            var keyLight = RenderSettings.sun != null ? RenderSettings.sun
                : Object.FindObjectsByType<Light>(FindObjectsSortMode.None).FirstOrDefault(l => l.type == LightType.Directional);

            var lightingRoot = GameObject.Find(LightingRootName);
            var manager = EnsureManager(keyLight, lightingRoot != null ? lightingRoot.transform : null);
            var mist = EnsureMist(profile, lightingRoot != null ? lightingRoot.transform : null);
            var waterMists = EnsureWaterMists(EnsureWaterProfile(), lightingRoot != null ? lightingRoot.transform : null);
            EnsureDriver(mist, waterMists);

            EditorSceneManager.MarkSceneDirty(mist.gameObject.scene);
            Debug.Log($"[Atmosphere] Volumetric fog + lights features on PC_Renderer; '{MistName}' uses {MistProfilePath}; " +
                      $"manager synced to '{(manager.sun != null ? manager.sun.name : "none")}'.");
        }

        private static VolumetricFogProfile EnsureProfile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumetricFogProfile>(MistProfilePath);
            bool created = profile == null;
            if (created) profile = ScriptableObject.CreateInstance<VolumetricFogProfile>();

            // Soft, slowly drifting fog that hugs the terrain. This is the FULL fog-weather look (WP_Fog,
            // volumetricFog = 1); VolumetricAtmosphereDriver scales it by the weather and turns it off when 0.
            profile.shape = VolumetricFogShape.Box;
            if (profile.noiseTexture == null) profile.noiseTexture = Resources.Load<Texture2D>("Textures/NoiseTex256");
            profile.density = 0.12f;         // per-metre: a 1 km box accumulates fast
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
            profile.terrainFogHeight = 14f;
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
            // Off in the saved scene: VolumetricAtmosphereDriver wakes it in fog weather, so edit mode stays clear.
            fog.gameObject.SetActive(false);
            EditorUtility.SetDirty(fog);
            return fog;
        }

        /// <summary>Thin low mist for dusk over still water: warm-lit, flat, close to the surface.</summary>
        private static VolumetricFogProfile EnsureWaterProfile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumetricFogProfile>(WaterMistProfilePath);
            bool created = profile == null;
            if (created) profile = ScriptableObject.CreateInstance<VolumetricFogProfile>();
            // Box, not Sphere: a sphere forces a round footprint, which turned the river strips into 53 m blobs.
            profile.shape = VolumetricFogShape.Box;
            profile.border = 0.35f;             // soft edges so the boxes never show
            if (profile.noiseTexture == null) profile.noiseTexture = Resources.Load<Texture2D>("Textures/NoiseTex256");
            profile.density = 0.3f;             // a 7 m layer needs far more than the 14 m ground fog to read
            profile.noiseScale = 12f;
            profile.noiseFinalMultiplier = 0.8f;
            profile.raymarchMinStep = 0.4f;
            profile.jittering = 0.5f;
            profile.turbulence = 0.25f;
            profile.windDirection = new Vector3(0.008f, 0f, 0.004f);
            profile.albedo = new Color(0.9f, 0.88f, 0.86f, 0.5f);
            // Not sun-lit: at dusk the key light fades to ~0 while it hands over to the moon, which left the mist black
            // exactly when it exists. VolumetricAtmosphereDriver feeds it the sky's ambient colour instead.
            profile.dayNightCycle = false;
            profile.sunDirection = Vector3.up;
            profile.sunIntensity = 1.6f;
            profile.sunColor = new Color(0.41f, 0.31f, 0.38f);
            profile.ambientLightMultiplier = 0.7f;
            profile.lightDiffusionModel = DiffusionModel.Smooth;
            profile.lightDiffusionPower = 18f;
            profile.lightDiffusionIntensity = 0.6f; // dusk sun glows through it
            profile.diffusionFloor = 0.8f;
            profile.receiveShadows = false;
            profile.terrainFit = false;
            profile.distantFog = false;
            // The lakes lie 300-480 m from town: the mist has to read from there, not only at the shore.
            profile.maxDistance = 650f;
            profile.maxDistanceFallOff = 0.3f;
            if (created) AssetDatabase.CreateAsset(profile, WaterMistProfilePath);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        /// <summary>One flat ellipsoid per lake and one per ~70 m of river, at the water line (rebuilt each run).</summary>
        private static VolumetricFog[] EnsureWaterMists(VolumetricFogProfile profile, Transform parent)
        {
            var zone = AssetDatabase.LoadAssetAtPath<KryticalNova.World.ZoneTerrainProfile>(ZoneTerrainGenerator.ProfilePath);
            if (zone == null) return new VolumetricFog[0];

            var old = GameObject.Find(WaterMistRootName);
            if (old != null) Undo.DestroyObjectImmediate(old);
            var root = new GameObject(WaterMistRootName);
            Undo.RegisterCreatedObjectUndo(root, "Water Mist");
            if (parent != null) root.transform.SetParent(parent, false);

            var result = new System.Collections.Generic.List<VolumetricFog>();
            float y = zone.waterLevel + 2.8f;
            const float Height = 7f;
            int i = 0;
            foreach (var lake in zone.lakes)
            {
                float d = lake.radius * 2.8f; // lakes wobble ±35%: cover the whole shore (the soft border eats the rim)
                result.Add(MistVolume(root.transform, $"Lake {++i}", new Vector3(lake.center.x, y, lake.center.y), 0f, new Vector3(d, Height, d), profile));
            }
            int r = 0;
            float width = zone.riverWidth + zone.riverBankWidth * 2f;
            foreach (var river in zone.rivers)
            {
                var pts = river.points;
                if (pts == null || pts.Count < 2) continue;
                Vector2 start = pts[0];
                float run = 0f;
                for (int k = 1; k < pts.Count; k++)
                {
                    run += Vector2.Distance(pts[k - 1], pts[k]);
                    bool last = k == pts.Count - 1;
                    if (run < RiverSegmentLength && !last) continue;
                    Vector2 end = pts[k];
                    Vector2 mid = (start + end) * 0.5f;
                    Vector2 dir = end - start;
                    float len = dir.magnitude + width * 0.5f;
                    float yaw = Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg;
                    result.Add(MistVolume(root.transform, $"River {++r}", new Vector3(mid.x, y, mid.y), yaw, new Vector3(width * 1.4f, Height, len), profile));
                    start = end;
                    run = 0f;
                }
            }
            return result.ToArray();
        }

        private static VolumetricFog MistVolume(Transform parent, string name, Vector3 position, float yaw, Vector3 size, VolumetricFogProfile profile)
        {
            var go = VolumetricFogManager.CreateFogVolume(name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            go.transform.localScale = size;
            var fog = go.GetComponent<VolumetricFog>();
            fog.profile = profile;
            fog.enableNativeLights = false;
            fog.enablePointLights = false;
            fog.enableAPV = true;           // baked lamp light tints the mist at night
            fog.apvIntensityMultiplier = 0.6f;
            fog.enabled = false;
            fog.enabled = true;
            go.SetActive(false);            // the driver wakes it at dusk
            return fog;
        }

        private static void EnsureDriver(VolumetricFog mist, VolumetricFog[] waterMists)
        {
            var presenter = Object.FindAnyObjectByType<EnvironmentPresenter>(FindObjectsInactive.Include);
            var host = presenter != null ? presenter.gameObject : mist.gameObject;
            if (!host.TryGetComponent<VolumetricAtmosphereDriver>(out var driver))
                driver = Undo.AddComponent<VolumetricAtmosphereDriver>(host);
            var so = new SerializedObject(driver);
            so.FindProperty("fog").objectReferenceValue = mist;
            var arr = so.FindProperty("waterMists");
            arr.arraySize = waterMists.Length;
            for (int i = 0; i < waterMists.Length; i++) arr.GetArrayElementAtIndex(i).objectReferenceValue = waterMists[i];
            so.ApplyModifiedProperties();
        }
    }
}
