using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Builds the wind-reactive foliage assets: KryticalNova/Foliage Wind materials using the Synty atlases,
    /// and prefab variants of the Synty grass / flower / bush / tree prefabs that swap in those materials
    /// (terrain details and terrain trees render a prefab's own material). The shared Synty atlas materials
    /// are never modified, because props use them too.
    /// </summary>
    public static class FoliageAssetBuilder
    {
        public const string MaterialFolder = "Assets/Materials/Foliage";
        public const string PrefabFolder = "Assets/Prefabs/Environment/Foliage";
        private const string ShaderName = "KryticalNova/Foliage Wind";

        private const string GenericAtlasMat = "Assets/Synty/PolygonGeneric/Materials/Alts/Generic_01_A.mat";
        private const string SciFiAtlasMat = "Assets/Synty/PolygonSciFiCity/Materials/Alts/PolygonScifi_01_A.mat";
        public const string GenericNature = "Assets/Synty/PolygonGeneric/Prefabs/Environment/";
        public const string SciFiEnvironments = "Assets/Synty/PolygonSciFiCity/Prefabs/Environments/";

        public enum Kind { Grass, TallGrass, Bush, Tree }

        // Synty prefab name → how it moves. Anything not listed is left alone (and loses wind materials it had).
        private static readonly Dictionary<string, Kind> Kinds = new Dictionary<string, Kind>
        {
            { "SM_Gen_Env_Grass_01", Kind.Grass }, { "SM_Gen_Env_Grass_02", Kind.Grass }, { "SM_Gen_Env_Grass_03", Kind.Grass },
            { "SM_Gen_Env_Grass_04", Kind.TallGrass }, { "SM_Gen_Env_Grass_05", Kind.TallGrass },
            { "SM_Gen_Env_Grass_Tall_01", Kind.TallGrass }, { "SM_Gen_Env_Grass_Tall_02", Kind.TallGrass },
            { "SM_Gen_Env_Grass_Tall_03", Kind.TallGrass }, { "SM_Gen_Env_Grass_Tall_04", Kind.TallGrass },
            { "SM_Gen_Env_Flowers_01", Kind.TallGrass }, { "SM_Gen_Env_Flowers_02", Kind.TallGrass },
            { "SM_Gen_Env_Flowers_03", Kind.TallGrass }, { "SM_Gen_Env_Flowers_04", Kind.TallGrass },
            { "SM_Gen_Env_Flowers_05", Kind.TallGrass }, { "SM_Gen_Env_Flowers_06", Kind.TallGrass },
            { "SM_Gen_Env_Flowers_07", Kind.TallGrass }, { "SM_Gen_Env_Flowers_08", Kind.TallGrass },
            { "SM_Gen_Env_Fern_01", Kind.Bush }, { "SM_Gen_Env_Fern_02", Kind.Bush }, { "SM_Gen_Env_Fern_03", Kind.Bush },
            { "SM_Gen_Env_Bush_01", Kind.Bush }, { "SM_Gen_Env_Bush_02", Kind.Bush },
            { "SM_Gen_Env_Bush_03", Kind.Bush }, { "SM_Gen_Env_Bush_04", Kind.Bush },
            { "SM_Gen_Env_Tree_01", Kind.Tree }, { "SM_Gen_Env_Tree_02", Kind.Tree }, { "SM_Gen_Env_Tree_03", Kind.Tree },
            { "SM_Gen_Env_Tree_Pine_01", Kind.Tree }, { "SM_Gen_Env_Tree_Pine_02", Kind.Tree }, { "SM_Gen_Env_Tree_Pine_03", Kind.Tree },
            { "SM_Env_Grass_Patch_01", Kind.TallGrass }, { "SM_Env_Grass_Patch_02", Kind.TallGrass }, { "SM_Env_Grass_Patch_03", Kind.TallGrass },
            // SciFi hedges are left out on purpose: their concrete planter is part of the same mesh and would sway.
            { "SM_Env_Tree_01", Kind.Tree }, { "SM_Env_Tree_02", Kind.Tree }, { "SM_Env_Tree_03", Kind.Tree },
        };

        [MenuItem("Krytical Nova/Zone/Foliage/Rebuild Foliage Materials and Variants")]
        public static void RebuildMenu()
        {
            EnsureAssets();
            ApplyToScene();
        }

        /// <summary>Creates/refreshes every foliage material and prefab variant.</summary>
        public static void EnsureAssets()
        {
            foreach (var pair in Kinds) Variant(pair.Key);
            AssetDatabase.SaveAssets();
        }

        /// <summary>The wind-reactive variant of a Synty foliage prefab (created on demand), or null if it isn't foliage.</summary>
        public static GameObject Variant(string syntyName)
        {
            if (!Kinds.TryGetValue(syntyName, out var kind)) return null;
            string variantPath = $"{PrefabFolder}/{syntyName}_Wind.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(variantPath);
            var source = LoadSource(syntyName);
            if (source == null) return null;

            WeatherVfxBuilder.EnsureFolder(PrefabFolder);
            if (existing == null)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
                SwapMaterials(instance, kind);
                existing = PrefabUtility.SaveAsPrefabAsset(instance, variantPath);
                Object.DestroyImmediate(instance);
            }
            else
            {
                // Keep the variant's materials in sync with the current material set.
                var contents = PrefabUtility.LoadPrefabContents(variantPath);
                SwapMaterials(contents, kind);
                PrefabUtility.SaveAsPrefabAsset(contents, variantPath);
                PrefabUtility.UnloadPrefabContents(contents);
            }
            return existing;
        }

        public static bool IsFoliage(string syntyName) => Kinds.ContainsKey(syntyName);

        /// <summary>Gives every player / combat-entity prefab a FoliageInteractor so grass parts around them.</summary>
        [MenuItem("Krytical Nova/Zone/Foliage/Add Grass Interactors To Character Prefabs")]
        public static void EnsureInteractors()
        {
            int added = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs", "Assets/GameData" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null || prefab.GetComponent<KryticalNova.World.FoliageInteractor>() != null) continue;
                if (prefab.GetComponent<KryticalNova.Player.PointAndClickPlayer>() == null &&
                    prefab.GetComponent<KryticalNova.Progression.CombatEntity>() == null) continue;
                var contents = PrefabUtility.LoadPrefabContents(path);
                contents.AddComponent<KryticalNova.World.FoliageInteractor>();
                PrefabUtility.SaveAsPrefabAsset(contents, path);
                PrefabUtility.UnloadPrefabContents(contents);
                added++;
            }
            Debug.Log($"[Foliage] Added grass interactors to {added} prefabs.");
        }

        /// <summary>Swaps wind materials onto foliage already placed in the open scene (town lawns, parks, outskirts).</summary>
        [MenuItem("Krytical Nova/Zone/Foliage/Apply Wind Materials To Scene Foliage")]
        public static void ApplyToScene()
        {
            int swapped = 0;
            foreach (var renderer in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include))
            {
                var root = PrefabUtility.GetOutermostPrefabInstanceRoot(renderer.gameObject);
                if (root == null) continue;
                var source = PrefabUtility.GetCorrespondingObjectFromOriginalSource(root);
                if (source == null) continue;
                if (!Kinds.TryGetValue(source.name, out var kind))
                {
                    // No longer foliage (e.g. hedges in planters): drop any wind material override.
                    if (HasWindMaterial(renderer))
                    {
                        PrefabUtility.RevertObjectOverride(renderer, InteractionMode.UserAction);
                        swapped++;
                    }
                    continue;
                }
                if (SwapRenderer(renderer, kind)) swapped++;
            }
            if (swapped > 0) EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log($"[Foliage] Wind materials applied to {swapped} scene renderers.");
        }

        private static bool HasWindMaterial(Renderer r)
        {
            foreach (var m in r.sharedMaterials)
                if (m != null && m.shader != null && m.shader.name == ShaderName) return true;
            return false;
        }

        private static GameObject LoadSource(string name)
        {
            return AssetDatabase.LoadAssetAtPath<GameObject>(GenericNature + name + ".prefab")
                   ?? AssetDatabase.LoadAssetAtPath<GameObject>(SciFiEnvironments + name + ".prefab");
        }

        private static void SwapMaterials(GameObject go, Kind kind)
        {
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true)) SwapRenderer(r, kind);
        }

        private static bool SwapRenderer(MeshRenderer r, Kind kind)
        {
            var mats = r.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < mats.Length; i++)
            {
                var m = mats[i];
                if (m == null) continue;
                bool scifi;
                if (m.shader != null && m.shader.name == ShaderName) scifi = m.name.Contains("SciFi");
                else if (m.name.StartsWith("PolygonScifi")) scifi = true;
                else if (m.name.StartsWith("Generic_")) scifi = false;
                else continue;
                var wind = Material(kind, scifi);
                if (mats[i] != wind) { mats[i] = wind; changed = true; }
            }
            if (changed)
            {
                Undo.RecordObject(r, "Foliage wind material");
                r.sharedMaterials = mats;
            }
            // Grass and flowers are too small to matter in the shadow map; skip the pass.
            var shadows = kind == Kind.Grass || kind == Kind.TallGrass ? ShadowCastingMode.Off : ShadowCastingMode.On;
            if (r.shadowCastingMode != shadows) { r.shadowCastingMode = shadows; changed = true; }
            return changed;
        }

        /// <summary>The shared wind material for a foliage kind and atlas (created or refreshed).</summary>
        public static Material Material(Kind kind, bool scifi)
        {
            WeatherVfxBuilder.EnsureFolder(MaterialFolder);
            string path = $"{MaterialFolder}/M_FoliageWind_{kind}_{(scifi ? "SciFi" : "Generic")}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;

            var shader = Shader.Find(ShaderName);
            if (shader == null) { Debug.LogError($"[Foliage] Shader '{ShaderName}' not found."); return null; }
            mat = new Material(shader) { enableInstancing = true };
            var atlas = AssetDatabase.LoadAssetAtPath<Material>(scifi ? SciFiAtlasMat : GenericAtlasMat);
            if (atlas != null && atlas.HasProperty("_Albedo_Map")) mat.SetTexture("_BaseMap", atlas.GetTexture("_Albedo_Map"));

            mat.SetFloat("_AlphaClip", 1f);
            mat.EnableKeyword("_ALPHATEST_ON");
            mat.SetFloat("_Cutoff", 0.5f);
            mat.SetColor("_ColorVariation2", new Color(0.72f, 0.9f, 0.78f, 1f));
            switch (kind)
            {
                case Kind.Grass:
                    Set(mat, cull: 0, normalUp: 0.7f, bend: 0.4f, wind: 1.1f, flutter: 0.03f, idle: 0.04f, push: 1.0f, fade: 60f);
                    break;
                case Kind.TallGrass:
                    Set(mat, cull: 0, normalUp: 0.6f, bend: 1.0f, wind: 0.9f, flutter: 0.04f, idle: 0.035f, push: 0.9f, fade: 60f);
                    break;
                case Kind.Bush:
                    Set(mat, cull: 2, normalUp: 0.25f, bend: 1.3f, wind: 0.12f, flutter: 0.05f, idle: 0.02f, push: 0.25f, fade: 90f);
                    mat.SetFloat("_TreeMode", 1f); mat.EnableKeyword("_TREE_MODE");
                    mat.SetFloat("_TrunkHeight", 0f); mat.SetFloat("_CanopyRadius", 1.5f);
                    mat.SetColor("_ColorVariation", new Color(0.9f, 1f, 0.8f, 0.2f));
                    break;
                case Kind.Tree:
                    Set(mat, cull: 2, normalUp: 0.2f, bend: 8f, wind: 0.05f, flutter: 0.06f, idle: 0.015f, push: 0f, fade: 220f);
                    mat.SetFloat("_TreeMode", 1f); mat.EnableKeyword("_TREE_MODE");
                    mat.SetFloat("_TrunkHeight", 1.2f); mat.SetFloat("_CanopyRadius", 2.4f);
                    mat.SetColor("_ColorVariation", new Color(0.9f, 1f, 0.82f, 0.18f));
                    break;
            }
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        private static void Set(Material m, float cull, float normalUp, float bend, float wind, float flutter, float idle, float push, float fade)
        {
            m.SetFloat("_Cull", cull);
            m.SetFloat("_NormalUp", normalUp);
            m.SetFloat("_BendHeight", bend);
            m.SetFloat("_WindStrength", wind);
            m.SetFloat("_FlutterStrength", flutter);
            m.SetFloat("_IdleSway", idle);
            m.SetFloat("_InteractStrength", push);
            m.SetFloat("_WindFadeDistance", fade);
        }
    }
}
