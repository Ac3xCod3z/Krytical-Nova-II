using KryticalNova.World;
using UnityEditor;
using UnityEngine;
using VolumetricLights;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Builds the lit street lamp: a prefab variant of Synty's SM_Prop_Streetlight_01 (the Synty file is never edited)
    /// with a "Lamp" child — a downward Baked spot light under the lamp head that feeds the APV "Night" scenario, a
    /// Kronnect VolumetricLight cone for fog, and a <see cref="StreetLampController"/> that runs the cone from dusk to
    /// dawn. StarterTownBuilder places this variant wherever an intact street light goes. Re-runnable.
    /// </summary>
    public static class StreetLightBuilder
    {
        private const string SourcePath = "Assets/Synty/PolygonSciFiCity/Prefabs/Props/SM_Prop_Streetlight_01.prefab";
        public const string Folder = "Assets/_Game/Environment/Props/StreetLights";
        public const string VariantPath = Folder + "/SM_Prop_Streetlight_01_Lit.prefab";
        // Lamp head underside sits at y ≈ 5.18, z 1.2–2.33 on the Synty mesh (arm along local +Z).
        private static readonly Vector3 LampLocal = new Vector3(0f, 5.05f, 1.8f);

        [MenuItem("Krytical Nova/Zone/Build Lit Street Light Variant")]
        public static void Build()
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePath);
            if (source == null) { Debug.LogError($"[StreetLight] Missing {SourcePath}"); return; }
            WeatherVfxBuilder.EnsureFolder(Folder);

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
            try
            {
                instance.name = "SM_Prop_Streetlight_01_Lit";
                var lampGo = new GameObject("Lamp");
                lampGo.transform.SetParent(instance.transform, false);
                lampGo.transform.localPosition = LampLocal;
                lampGo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // straight down

                var light = lampGo.AddComponent<Light>();
                light.type = LightType.Spot;
                light.spotAngle = 115f;
                light.innerSpotAngle = 60f;
                light.range = 14f;
                light.intensity = 10f;
                light.color = new Color(1f, 0.85f, 0.63f);  // warm sodium-ish, sits in the colour bible's warm lights
                light.shadows = LightShadows.None;
                light.lightmapBakeType = LightmapBakeType.Baked; // APV only: no realtime cost
                light.bounceIntensity = 1f;

                var cone = lampGo.AddComponent<VolumetricLight>();
                cone.profile = null;
                cone.raymarchPreset = RaymarchPresets.Faster;
                cone.density = 0.12f;
                cone.brightness = 0.04f;
                cone.mediumAlbedo = new Color(1f, 0.92f, 0.82f);
                cone.useNoise = true;
                cone.noiseScale = 4f;
                cone.windDirection = new Vector3(0.02f, 0.01f, 0f);
                cone.attenuationMode = AttenuationMode.Simple;
                cone.enableShadows = false;
                cone.enableDustParticles = false;
                cone.autoToggle = true;               // far cones cost nothing
                cone.distanceStartDimming = 45f;
                cone.distanceDeactivation = 65f;
                cone.alwaysOn = false;

                var controller = instance.AddComponent<StreetLampController>();
                var so = new SerializedObject(controller);
                so.FindProperty("cone").objectReferenceValue = cone;
                so.ApplyModifiedPropertiesWithoutUndo();

                // Saving an instance of the Synty prefab produces a variant of it.
                PrefabUtility.SaveAsPrefabAsset(instance, VariantPath);
                Debug.Log($"[StreetLight] Built {VariantPath}.");
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }
    }
}
