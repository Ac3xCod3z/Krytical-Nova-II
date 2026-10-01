using System.Collections.Generic;
using KryticalNova.Networking;
using KryticalNova.World;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// One-click, re-runnable setup for the sky / day-night / weather system:
    /// bakes the cloud noise, creates the default profiles + sky material, builds the weather VFX
    /// and wires everything into the open scene. Existing assets are kept (only missing ones are created),
    /// so tuning done in the inspector is never overwritten.
    /// </summary>
    public static class WorldEnvironmentSetup
    {
        private const string Root = "Assets/GameData/World";
        private const string WeatherFolder = Root + "/Weather";
        private const string MaterialPath = Root + "/Materials/M_StylizedSky.mat";
        private const string DayCyclePath = Root + "/DC_Default.asset";
        private const string CatalogPath = Root + "/WeatherCatalog.asset";

        [MenuItem("Krytical Nova/World/Setup Sky + Weather (All Steps)")]
        public static void SetupAll()
        {
            CreateAssets();
            WeatherVfxBuilder.BuildAll();
            SetupScene();
        }

        // ---------------------------------------------------------------- assets

        [MenuItem("Krytical Nova/World/Create Default Assets")]
        public static void CreateAssets()
        {
            WeatherVfxBuilder.EnsureFolder(WeatherFolder);
            WeatherVfxBuilder.EnsureFolder(Root + "/Materials");
            WeatherVfxBuilder.EnsureFolder(Root + "/Textures");

            if (AssetDatabase.LoadAssetAtPath<Texture2D>(CloudNoiseBaker.DefaultPath) == null)
                CloudNoiseBaker.Bake(CloudNoiseBaker.DefaultPath);

            LoadOrCreate<DayCycleProfile>(DayCyclePath, _ => { });

            if (AssetDatabase.LoadAssetAtPath<Material>(MaterialPath) == null)
            {
                var mat = new Material(Shader.Find("Skybox/KryticalNova/Stylized Sky"));
                AssetDatabase.CreateAsset(mat, MaterialPath);
            }

            var clear = Weather("WP_Clear", w =>
            {
                Cover(w, 0f, 0.12f); w.cirrusAmount = 0.35f; w.cloudShadowStrength = 0.35f;
                w.fogDensity = 0.0012f; w.windSpeed = 3f; w.saturation = 5f;
                Duration(w, 300f, 700f);
            });
            var few = Weather("WP_FewClouds", w =>
            {
                Cover(w, 0.15f, 0.3f); w.cirrusAmount = 0.3f; w.cloudShadowStrength = 0.5f;
                w.fogDensity = 0.0014f; w.windSpeed = 4f;
                Duration(w, 300f, 600f);
            });
            var partly = Weather("WP_PartlyCloudy", w =>
            {
                Cover(w, 0.35f, 0.55f); w.cirrusAmount = 0.2f; w.cloudShadowStrength = 0.6f;
                w.sunIntensityMultiplier = 0.95f; w.fogDensity = 0.0016f; w.windSpeed = 5f;
                Duration(w, 300f, 600f);
            });
            var mostly = Weather("WP_MostlyCloudy", w =>
            {
                Cover(w, 0.6f, 0.78f); w.cloudDensity = 0.92f; w.cloudDarkness = 0.15f; w.cirrusAmount = 0.1f;
                w.skyDesaturation = 0.15f; w.skyDarkening = 0.1f; w.sunIntensityMultiplier = 0.75f;
                w.ambientMultiplier = 0.95f; w.shadowMultiplier = 0.6f; w.cloudShadowStrength = 0.55f;
                w.fogDensity = 0.0022f; w.windSpeed = 6f; w.saturation = -10f;
                Duration(w, 240f, 500f);
            });
            var overcast = Weather("WP_Overcast", w =>
            {
                Cover(w, 0.88f, 1f); w.cloudDensity = 0.97f; w.cloudSoftness = 0.3f; w.cloudDarkness = 0.3f;
                w.cloudTint = new Color(0.85f, 0.87f, 0.9f); w.cirrusAmount = 0f;
                w.skyDesaturation = 0.45f; w.skyDarkening = 0.2f; w.sunIntensityMultiplier = 0.35f;
                w.ambientMultiplier = 1.05f; w.shadowMultiplier = 0.1f; w.cloudShadowStrength = 0.05f;
                w.fogDensity = 0.0035f; w.fogTint = new Color(0.7f, 0.72f, 0.75f, 0.4f);
                w.windSpeed = 6f; w.saturation = -25f; w.contrast = -5f;
                Duration(w, 240f, 500f);
            });
            var fog = Weather("WP_Fog", w =>
            {
                Cover(w, 0.7f, 0.9f); w.cloudSoftness = 0.35f; w.cloudDarkness = 0.1f; w.cirrusAmount = 0f;
                w.skyDesaturation = 0.5f; w.skyDarkening = 0.1f; w.sunIntensityMultiplier = 0.4f;
                w.ambientMultiplier = 1.1f; w.shadowMultiplier = 0.15f; w.cloudShadowStrength = 0f;
                w.fogDensity = 0.02f; w.fogTint = new Color(0.75f, 0.78f, 0.8f, 0.6f);
                w.windSpeed = 1f; w.mist = 1f; w.saturation = -30f; w.contrast = -10f;
                w.transitionSeconds = 120f; Duration(w, 180f, 420f);
            });
            var lightRain = Weather("WP_LightRain", w =>
            {
                Cover(w, 0.85f, 0.97f); w.cloudDarkness = 0.35f; w.cloudTint = new Color(0.8f, 0.82f, 0.86f); w.cirrusAmount = 0f;
                w.skyDesaturation = 0.5f; w.skyDarkening = 0.25f; w.sunIntensityMultiplier = 0.3f;
                w.ambientMultiplier = 0.9f; w.shadowMultiplier = 0.08f; w.cloudShadowStrength = 0f;
                w.fogDensity = 0.005f; w.fogTint = new Color(0.62f, 0.65f, 0.7f, 0.5f);
                w.windSpeed = 7f; w.precipitation = PrecipitationType.Rain; w.precipitationIntensity = 0.35f;
                w.saturation = -25f; w.contrast = -5f;
                Duration(w, 200f, 420f);
            });
            var rain = Weather("WP_Rain", w =>
            {
                Cover(w, 0.95f, 1f); w.cloudDarkness = 0.5f; w.cloudTint = new Color(0.7f, 0.73f, 0.78f); w.cirrusAmount = 0f;
                w.skyDesaturation = 0.6f; w.skyDarkening = 0.38f; w.sunIntensityMultiplier = 0.2f;
                w.ambientMultiplier = 0.8f; w.shadowMultiplier = 0f; w.cloudShadowStrength = 0f;
                w.fogDensity = 0.008f; w.fogTint = new Color(0.5f, 0.54f, 0.6f, 0.6f);
                w.windSpeed = 10f; w.precipitation = PrecipitationType.Rain; w.precipitationIntensity = 0.8f;
                w.saturation = -35f;
                Duration(w, 200f, 420f);
            });
            var storm = Weather("WP_Thunderstorm", w =>
            {
                Cover(w, 1f, 1f); w.cloudDarkness = 0.7f; w.cloudTint = new Color(0.55f, 0.58f, 0.68f); w.cirrusAmount = 0f;
                w.skyDesaturation = 0.65f; w.skyDarkening = 0.55f; w.sunIntensityMultiplier = 0.1f;
                w.ambientMultiplier = 0.65f; w.shadowMultiplier = 0f; w.cloudShadowStrength = 0f;
                w.fogDensity = 0.01f; w.fogTint = new Color(0.4f, 0.43f, 0.52f, 0.7f);
                w.windSpeed = 16f; w.precipitation = PrecipitationType.Rain; w.precipitationIntensity = 1f;
                w.lightningPerMinute = 8f; w.saturation = -40f; w.contrast = 10f;
                Duration(w, 180f, 360f);
            });
            var snow = Weather("WP_Snow", w =>
            {
                Cover(w, 0.9f, 1f); w.cloudSoftness = 0.3f; w.cloudDarkness = 0.2f; w.cloudTint = new Color(0.9f, 0.92f, 0.95f); w.cirrusAmount = 0f;
                w.skyDesaturation = 0.6f; w.skyDarkening = 0.12f; w.sunIntensityMultiplier = 0.4f;
                w.ambientMultiplier = 1.15f; w.shadowMultiplier = 0.12f; w.cloudShadowStrength = 0f;
                w.fogDensity = 0.007f; w.fogTint = new Color(0.85f, 0.88f, 0.92f, 0.6f);
                w.windSpeed = 3f; w.precipitation = PrecipitationType.Snow; w.precipitationIntensity = 0.6f;
                w.saturation = -30f; w.contrast = -8f;
                Duration(w, 240f, 480f);
            });
            var blizzard = Weather("WP_Blizzard", w =>
            {
                Cover(w, 1f, 1f); w.cloudDarkness = 0.35f; w.cloudTint = new Color(0.85f, 0.87f, 0.92f); w.cirrusAmount = 0f;
                w.skyDesaturation = 0.75f; w.skyDarkening = 0.25f; w.sunIntensityMultiplier = 0.2f;
                w.ambientMultiplier = 1.1f; w.shadowMultiplier = 0f; w.cloudShadowStrength = 0f;
                w.fogDensity = 0.025f; w.fogTint = new Color(0.88f, 0.9f, 0.94f, 0.8f);
                w.windSpeed = 18f; w.precipitation = PrecipitationType.Snow; w.precipitationIntensity = 1f; w.mist = 0.3f;
                w.saturation = -45f; w.contrast = -10f;
                Duration(w, 150f, 300f);
            });

            // Markov chain (only filled when a profile has no links yet, so hand edits survive re-runs).
            Link(clear, (few, 3f), (partly, 1f), (fog, 0.4f));
            Link(few, (clear, 2f), (partly, 3f));
            Link(partly, (few, 2f), (mostly, 2f), (lightRain, 0.5f));
            Link(mostly, (partly, 2f), (overcast, 2f), (lightRain, 1f));
            Link(overcast, (mostly, 2f), (lightRain, 2f), (rain, 1f), (fog, 1f), (snow, 0.4f));
            Link(fog, (overcast, 1f), (mostly, 2f));
            Link(lightRain, (overcast, 2f), (rain, 2f), (mostly, 1f));
            Link(rain, (lightRain, 2f), (storm, 1.5f), (overcast, 1f));
            Link(storm, (rain, 3f), (lightRain, 1f));
            Link(snow, (overcast, 2f), (blizzard, 1f));
            Link(blizzard, (snow, 3f));

            var catalog = LoadOrCreate<WeatherCatalog>(CatalogPath, _ => { });
            if (catalog.Count == 0)
            {
                catalog.SetProfiles(new List<WeatherProfile> { clear, few, partly, mostly, overcast, fog, lightRain, rain, storm, snow, blizzard });
                EditorUtility.SetDirty(catalog);
            }

            AssetDatabase.SaveAssets();
            Debug.Log("[WorldEnvironmentSetup] Default sky/weather assets ready in " + Root);
        }

        private static void Cover(WeatherProfile w, float min, float max) { w.coverageMin = min; w.coverageMax = max; }
        private static void Duration(WeatherProfile w, float min, float max) { w.durationMin = min; w.durationMax = max; }

        private static WeatherProfile Weather(string name, System.Action<WeatherProfile> init)
        {
            return LoadOrCreate($"{WeatherFolder}/{name}.asset", init);
        }

        private static void Link(WeatherProfile from, params (WeatherProfile profile, float weight)[] links)
        {
            if (from.next.Count > 0) return;
            foreach (var (profile, weight) in links)
                from.next.Add(new WeatherProfile.NextWeather { profile = profile, weight = weight });
            EditorUtility.SetDirty(from);
        }

        private static T LoadOrCreate<T>(string path, System.Action<T> init) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
            asset = ScriptableObject.CreateInstance<T>();
            init(asset);
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        // ---------------------------------------------------------------- scene

        [MenuItem("Krytical Nova/World/Setup Scene Objects")]
        public static void SetupScene()
        {
            var dayCycle = AssetDatabase.LoadAssetAtPath<DayCycleProfile>(DayCyclePath);
            var catalog = AssetDatabase.LoadAssetAtPath<WeatherCatalog>(CatalogPath);
            var skyMaterial = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            var noise = AssetDatabase.LoadAssetAtPath<Texture2D>(CloudNoiseBaker.DefaultPath);
            if (dayCycle == null || catalog == null || skyMaterial == null)
            {
                Debug.LogError("[WorldEnvironmentSetup] Run 'Create Default Assets' first.");
                return;
            }
            var startWeather = catalog.Get(catalog.IndexOf("WP_FewClouds"));

            // --- networked clock / weather state
            var env = Object.FindFirstObjectByType<WorldEnvironmentNetwork>(FindObjectsInactive.Include);
            if (env == null)
            {
                var go = new GameObject("World Environment");
                Undo.RegisterCreatedObjectUndo(go, "Create World Environment");
                go.AddComponent<NetworkObject>();
                go.AddComponent<AlwaysRelevantNetworkObject>();
                env = go.AddComponent<WorldEnvironmentNetwork>();
            }
            var envSo = new SerializedObject(env);
            envSo.FindProperty("dayCycle").objectReferenceValue = dayCycle;
            envSo.FindProperty("weatherCatalog").objectReferenceValue = catalog;
            if (envSo.FindProperty("startWeather").objectReferenceValue == null)
                envSo.FindProperty("startWeather").objectReferenceValue = startWeather;
            envSo.ApplyModifiedProperties();

            // --- client presentation
            var presenter = Object.FindFirstObjectByType<EnvironmentPresenter>(FindObjectsInactive.Include);
            GameObject presenterGo;
            if (presenter == null)
            {
                presenterGo = new GameObject("Environment Presenter");
                Undo.RegisterCreatedObjectUndo(presenterGo, "Create Environment Presenter");
                presenter = presenterGo.AddComponent<EnvironmentPresenter>();
            }
            presenterGo = presenter.gameObject;

            var lightning = GetOrAdd<LightningController>(presenterGo);
            var precipitation = GetOrAdd<PrecipitationController>(presenterGo);
            var ambience = GetOrAdd<WeatherAmbience>(presenterGo);

            Light keyLight = RenderSettings.sun;
            if (keyLight == null)
            {
                foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                    if (l.type == LightType.Directional) { keyLight = l; break; }
            }
            var volume = Object.FindFirstObjectByType<Volume>();

            var pSo = new SerializedObject(presenter);
            pSo.FindProperty("keyLight").objectReferenceValue = keyLight;
            pSo.FindProperty("skyMaterial").objectReferenceValue = skyMaterial;
            pSo.FindProperty("postVolume").objectReferenceValue = volume;
            pSo.FindProperty("cloudNoise").objectReferenceValue = noise;
            pSo.FindProperty("lightning").objectReferenceValue = lightning;
            pSo.FindProperty("previewDayCycle").objectReferenceValue = dayCycle;
            if (pSo.FindProperty("previewWeather").objectReferenceValue == null)
                pSo.FindProperty("previewWeather").objectReferenceValue = startWeather;
            pSo.ApplyModifiedProperties();

            // Rigs live under the presenter; the controller moves them to the camera at runtime.
            var rain = EnsureRig(presenterGo.transform, WeatherVfxBuilder.RainPath);
            var splash = EnsureRig(presenterGo.transform, WeatherVfxBuilder.RainSplashPath);
            var snow = EnsureRig(presenterGo.transform, WeatherVfxBuilder.SnowPath);
            var blizzard = EnsureRig(presenterGo.transform, WeatherVfxBuilder.BlizzardPath);
            var mist = EnsureRig(presenterGo.transform, WeatherVfxBuilder.MistPath);

            var prSo = new SerializedObject(precipitation);
            SetRig(prSo, "rain", rain, 9f, 0.6f, true, false);
            SetRig(prSo, "rainSplash", splash, 0.05f, 0f, true, true);
            SetRig(prSo, "snow", snow, -1f, 1f, true, false);
            SetRig(prSo, "blizzard", blizzard, 8f, 1.5f, true, false);
            SetRig(prSo, "mist", mist, 0f, 0.3f, false, true);
            prSo.ApplyModifiedProperties();

            var lSo = new SerializedObject(lightning);
            lSo.FindProperty("strikePrefab").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<GameObject>(WeatherVfxBuilder.LightningPath)?.GetComponent<ParticleSystem>();
            lSo.FindProperty("thunderSource").objectReferenceValue = EnsureAudio(presenterGo.transform, "Thunder Audio");
            lSo.ApplyModifiedProperties();

            var aSo = new SerializedObject(ambience);
            aSo.FindProperty("sourceA").objectReferenceValue = EnsureAudio(presenterGo.transform, "Ambience A");
            aSo.FindProperty("sourceB").objectReferenceValue = EnsureAudio(presenterGo.transform, "Ambience B");
            aSo.ApplyModifiedProperties();

            // --- render settings
            RenderSettings.skybox = skyMaterial;
            RenderSettings.sun = keyLight;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.0015f;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.reflectionIntensity = 0.3f;

            EditorSceneManager.MarkSceneDirty(presenterGo.scene);
            Debug.Log("[WorldEnvironmentSetup] Scene wired: World Environment (networked) + Environment Presenter.");
        }

        private static T GetOrAdd<T>(GameObject go) where T : Component
        {
            return go.TryGetComponent<T>(out var c) ? c : Undo.AddComponent<T>(go);
        }

        private static ParticleSystem EnsureRig(Transform parent, string prefabPath)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null) return null;
            var existing = parent.Find(prefab.name);
            if (existing != null) return existing.GetComponent<ParticleSystem>();
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            Undo.RegisterCreatedObjectUndo(instance, "Create weather rig");
            return instance.GetComponent<ParticleSystem>();
        }

        private static AudioSource EnsureAudio(Transform parent, string name)
        {
            var t = parent.Find(name);
            if (t == null)
            {
                t = new GameObject(name).transform;
                t.SetParent(parent, false);
                Undo.RegisterCreatedObjectUndo(t.gameObject, "Create audio");
            }
            var source = t.TryGetComponent<AudioSource>(out var s) ? s : t.gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            return source;
        }

        private static void SetRig(SerializedObject so, string field, ParticleSystem system, float height, float wind, bool shelter, bool grounded)
        {
            var rig = so.FindProperty(field);
            rig.FindPropertyRelative("system").objectReferenceValue = system;
            rig.FindPropertyRelative("heightOffset").floatValue = height;
            rig.FindPropertyRelative("windInfluence").floatValue = wind;
            rig.FindPropertyRelative("useShelter").boolValue = shelter;
            rig.FindPropertyRelative("groundAnchored").boolValue = grounded;
            rig.FindPropertyRelative("lookAhead").floatValue = grounded && !shelter ? 0f : 6f;
        }
    }
}
