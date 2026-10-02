using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Baked GI for hand-placed lights, per the Rendering Bible: the sun/moon directional light stays realtime
    /// (it moves with the day cycle), while point/spot/area lights you place by hand are Baked into Adaptive Probe
    /// Volumes. APV Sky Occlusion keeps sky ambient following the day cycle instead of freezing it at bake time.
    /// Static environment contributes GI and receives it from the probes (no lightmaps to manage).
    /// </summary>
    public static class LightingSetup
    {
        private const string PcPipelinePath = "Assets/_Game/Rendering/Settings/PC_RPAsset.asset";
        private const string LightingRootName = "Lighting";
        private const int AdaptiveProbeVolumes = 1; // UniversalRenderPipelineAsset light probe system

        [MenuItem("Krytical Nova/Lighting/Setup Baked GI (APV) For Open Scene")]
        public static void Setup()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(scene.path)) { Debug.LogError("[LightingSetup] Save the scene first."); return; }
            string folder = System.IO.Path.GetDirectoryName(scene.path).Replace('\\', '/');

            EnableApvOnPc();
            var settings = EnsureLightingSettings(scene, folder);
            var root = EnsureLightingRoot(scene);
            EnsureProbeVolume(root);
            EnsureBakingSet(scene, folder);
            int lights = ConfigureLights(root);
            int statics = ConfigureStaticGeometry();

            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log($"[LightingSetup] {scene.name}: APV on (PC), lighting settings '{settings.name}', " +
                      $"{lights} hand-placed light(s) set to Baked, {statics} static renderer(s) contribute GI. " +
                      "Run Krytical Nova > Lighting > Bake Lighting.");
        }

        [MenuItem("Krytical Nova/Lighting/Bake Lighting")]
        public static void Bake()
        {
            if (!Lightmapping.BakeAsync()) Debug.LogError("[LightingSetup] Bake could not start (already baking?).");
        }

        private static void EnableApvOnPc()
        {
            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PcPipelinePath);
            if (asset == null) return;
            var so = new SerializedObject(asset);
            var prop = so.FindProperty("m_LightProbeSystem");
            if (prop != null && prop.intValue != AdaptiveProbeVolumes)
            {
                prop.intValue = AdaptiveProbeVolumes;
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(asset);
            }
        }

        private static LightingSettings EnsureLightingSettings(UnityEngine.SceneManagement.Scene scene, string folder)
        {
            if (Lightmapping.TryGetLightingSettings(out var existing) && existing != null && AssetDatabase.Contains(existing))
                return existing;

            string path = $"{folder}/LS_{scene.name}.lighting";
            var settings = AssetDatabase.LoadAssetAtPath<LightingSettings>(path);
            if (settings == null)
            {
                settings = new LightingSettings { name = "LS_" + scene.name };
                AssetDatabase.CreateAsset(settings, path);
            }
            settings.bakedGI = true;
            settings.realtimeGI = false;
            settings.mixedBakeMode = MixedLightingMode.IndirectOnly;
            settings.lightmapper = LightingSettings.Lightmapper.ProgressiveGPU;
            settings.environmentSampleCount = 256;
            settings.indirectSampleCount = 256;
            settings.maxBounces = 2;
            EditorUtility.SetDirty(settings);
            Lightmapping.lightingSettings = settings;
            return settings;
        }

        private static Transform EnsureLightingRoot(UnityEngine.SceneManagement.Scene scene)
        {
            foreach (var go in scene.GetRootGameObjects())
                if (go.name == LightingRootName) return go.transform;
            var root = new GameObject(LightingRootName);
            Undo.RegisterCreatedObjectUndo(root, "Create Lighting Root");
            return root.transform;
        }

        private static void EnsureProbeVolume(Transform root)
        {
            if (Object.FindAnyObjectByType<ProbeVolume>() != null) return;
            var go = new GameObject("Adaptive Probe Volume (Global)");
            go.transform.SetParent(root, false);
            var volume = go.AddComponent<ProbeVolume>();
            volume.mode = ProbeVolume.Mode.Global;
            Undo.RegisterCreatedObjectUndo(go, "Create Probe Volume");
        }

        private static void EnsureBakingSet(UnityEngine.SceneManagement.Scene scene, string folder)
        {
            string guid = AssetDatabase.AssetPathToGUID(scene.path);
            ProbeVolumeBakingSet set = null;
            foreach (var setGuid in AssetDatabase.FindAssets("t:ProbeVolumeBakingSet"))
            {
                var candidate = AssetDatabase.LoadAssetAtPath<ProbeVolumeBakingSet>(AssetDatabase.GUIDToAssetPath(setGuid));
                if (candidate != null && candidate.sceneGUIDs.Contains(guid)) { set = candidate; break; }
            }
            if (set == null)
            {
                string path = $"{folder}/APV_{scene.name}.asset";
                set = AssetDatabase.LoadAssetAtPath<ProbeVolumeBakingSet>(path);
                if (set == null)
                {
                    set = ScriptableObject.CreateInstance<ProbeVolumeBakingSet>();
                    AssetDatabase.CreateAsset(set, path);
                }
                set.TryAddScene(guid);
            }
            // Sky light is re-evaluated at runtime from the (day-cycle driven) ambient probe; only occlusion is baked.
            set.skyOcclusion = true;
            // Density: 1 m spacing over a 600 m zone baked ~650 MB of probe data. 3 m near geometry (bricks grow to
            // 3 × 3^levels m in open space) keeps a zone in the tens of MB and still resolves lamp-scale bounce.
            set.minDistanceBetweenProbes = 3f;
            set.simplificationLevels = 3;
            EditorUtility.SetDirty(set);
            AssetDatabase.SaveAssets();
        }

        /// <summary>Directional lights stay realtime; every other light is a hand-placed fixture and bakes.</summary>
        private static int ConfigureLights(Transform root)
        {
            int count = 0;
            foreach (var light in Object.FindObjectsByType<Light>())
            {
                if (light.type == LightType.Directional)
                {
                    light.lightmapBakeType = LightmapBakeType.Realtime;
                    continue;
                }
                Undo.RecordObject(light, "Bake Light");
                light.lightmapBakeType = LightmapBakeType.Baked;
                count++;
            }
            return count;
        }

        /// <summary>Anything already marked static (environment, buildings, terrain) contributes GI and reads APV.</summary>
        private static int ConfigureStaticGeometry()
        {
            var seen = new HashSet<GameObject>();
            int count = 0;
            foreach (var renderer in Object.FindObjectsByType<MeshRenderer>())
            {
                var go = renderer.gameObject;
                var flags = GameObjectUtility.GetStaticEditorFlags(go);
                if (flags == 0 || !seen.Add(go)) continue;
                GameObjectUtility.SetStaticEditorFlags(go, flags | StaticEditorFlags.ContributeGI);
                renderer.receiveGI = ReceiveGI.LightProbes;
                count++;
            }
            foreach (var terrain in Object.FindObjectsByType<Terrain>())
            {
                var go = terrain.gameObject;
                GameObjectUtility.SetStaticEditorFlags(go, GameObjectUtility.GetStaticEditorFlags(go) | StaticEditorFlags.ContributeGI);
            }
            return count;
        }
    }
}
