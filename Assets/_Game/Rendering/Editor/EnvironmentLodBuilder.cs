using System.Collections.Generic;
using System.IO;
using System.Text;
using KryticalNova.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Level of detail for every mesh the open scene draws (owner, 17 Oct 2026: "for all meshes at a distance").
    /// Two parts:
    /// <list type="number">
    /// <item>Lighter levels inside each mesh (Unity 6 Mesh LOD, picked by screen size, no extra objects). The Synty
    /// models get them from their import settings, so only their .meta files change (owner's pick A); our own mesh
    /// assets (the ruin chunks) get them made in place. Terrain trees are included through their prototypes.</item>
    /// <item>A distance past which each thing is not drawn: an LODGroup of one level on each placed prefab (all its
    /// meshes go together, so a building never loses pieces) or on a loose mesh, at the share of the screen its kind is
    /// hidden below (<see cref="WorldLodSettings.bands"/>). Marked with
    /// <see cref="GeneratedLod"/>, so running it again only refreshes the numbers.</item>
    /// </list>
    /// How near or far all of it switches follows the graphics preset at run time (<see cref="WorldLodSettings.tiers"/>).
    /// </summary>
    public static class EnvironmentLodBuilder
    {
        public const string SettingsPath = "Assets/_Game/Rendering/Data/Assets/WorldLodSettings.asset";
        public const string ReportPath = "Library/KryticalNova.LodBuild.txt";

        [MenuItem("Krytical Nova/Rendering/Build LODs (Open Scene)")]
        public static void BuildMenu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("[LOD] Leave Play first."); return; }
            Debug.Log(Build(true));
        }

        public static WorldLodSettings LoadSettings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<WorldLodSettings>(SettingsPath);
            if (settings != null) return settings;
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
            settings = ScriptableObject.CreateInstance<WorldLodSettings>();
            AssetDatabase.CreateAsset(settings, SettingsPath);
            AssetDatabase.SaveAssets();
            return settings;
        }

        /// <summary>Gives the open scenes' meshes their levels and hide distances; saves the scenes when asked.</summary>
        public static string Build(bool saveScenes)
        {
            WorldLodSettings settings = LoadSettings();
            var report = new StringBuilder("[LOD] ");

            // Every mesh drawn: the scene's own and the terrain's trees.
            var renderers = new List<MeshRenderer>();
            var meshes = new HashSet<Mesh>();
            for (int s = 0; s < SceneManager.sceneCount; s++)
            {
                Scene scene = SceneManager.GetSceneAt(s);
                if (!scene.isLoaded) continue;
                foreach (GameObject root in scene.GetRootGameObjects())
                    foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
                    {
                        if (!IsWorldMesh(renderer, out Mesh mesh)) continue;
                        renderers.Add(renderer);
                        meshes.Add(mesh);
                    }
            }
            int treeMeshes = 0;
            foreach (Terrain terrain in Object.FindObjectsByType<Terrain>(FindObjectsInactive.Include))
            {
                if (terrain.terrainData == null) continue;
                foreach (TreePrototype prototype in terrain.terrainData.treePrototypes)
                {
                    if (prototype.prefab == null) continue;
                    foreach (MeshFilter filter in prototype.prefab.GetComponentsInChildren<MeshFilter>(true))
                        if (filter.sharedMesh != null && meshes.Add(filter.sharedMesh)) treeMeshes++;
                }
            }

            // 1. Lighter levels inside the meshes.
            var models = new SortedSet<string>();
            var own = new List<Mesh>();
            int tooSmall = 0, already = 0;
            foreach (Mesh mesh in meshes)
            {
                if (Triangles(mesh) < settings.minTrianglesToSimplify) { tooSmall++; continue; }
                string path = AssetDatabase.GetAssetPath(mesh);
                if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/")) continue; // built in, or made at run time
                if (AssetImporter.GetAtPath(path) is ModelImporter) models.Add(path);
                else if (path.EndsWith(".asset") || path.EndsWith(".mesh"))
                {
                    if (mesh.lodCount > 1) already++;
                    else own.Add(mesh);
                }
            }

            int switchedOn = 0;
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (string path in models)
                {
                    var importer = (ModelImporter)AssetImporter.GetAtPath(path);
                    if (importer.generateMeshLods && importer.maximumMeshLod == settings.maxMeshLods) { already++; continue; }
                    importer.generateMeshLods = true;
                    importer.maximumMeshLod = settings.maxMeshLods;
                    importer.SaveAndReimport();
                    switchedOn++;
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            int made = 0;
            foreach (Mesh mesh in own)
            {
                MeshLodUtility.GenerateMeshLods(mesh, settings.maxMeshLods);
                if (mesh.lodCount > 1) { EditorUtility.SetDirty(mesh); made++; }
            }
            if (made > 0) AssetDatabase.SaveAssets();

            // 2. The distance past which each thing is not drawn: one group per placed prefab, or per loose mesh.
            var owners = new Dictionary<GameObject, List<Renderer>>();
            foreach (MeshRenderer renderer in renderers)
            {
                if (renderer == null) continue;
                GameObject owner = PrefabUtility.GetNearestPrefabInstanceRoot(renderer.gameObject);
                if (owner == null) owner = renderer.gameObject;
                if (!owners.TryGetValue(owner, out List<Renderer> list)) owners[owner] = list = new List<Renderer>();
                list.Add(renderer);
            }
            int groups = 0, refreshed = 0, skipped = 0;
            var perKind = new Dictionary<LodCategory, int>();
            foreach (KeyValuePair<GameObject, List<Renderer>> pair in owners)
            {
                GameObject owner = pair.Key;
                LODGroup group = owner.GetComponent<LODGroup>();
                GeneratedLod mark = owner.GetComponent<GeneratedLod>();
                if (mark == null && owner.GetComponentInParent<LODGroup>(true) != null) { skipped++; continue; } // its own already
                if (mark == null && owner.GetComponentInChildren<LODGroup>(true) != null) { skipped++; continue; }
                Bounds b = pair.Value[0].bounds;
                foreach (Renderer each in pair.Value) b.Encapsulate(each.bounds);
                LodCategory kind = settings.CategoryOf(owner.name, b.size);
                if (!settings.TryGetBand(kind, out WorldLodSettings.Band band)) continue;

                if (group == null) { group = Undo.AddComponent<LODGroup>(owner); groups++; }
                else refreshed++;
                if (mark == null) mark = Undo.AddComponent<GeneratedLod>(owner);
                mark.category = kind;
                group.fadeMode = LODFadeMode.None;
                group.SetLODs(new[] { new LOD(band.hiddenBelow, pair.Value.ToArray()) });
                group.RecalculateBounds();
                EditorUtility.SetDirty(group);
                EditorUtility.SetDirty(mark);
                perKind[kind] = perKind.TryGetValue(kind, out int n) ? n + 1 : 1;
            }

            if (saveScenes)
                for (int s = 0; s < SceneManager.sceneCount; s++)
                {
                    Scene scene = SceneManager.GetSceneAt(s);
                    if (!scene.isLoaded) continue;
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                }

            report.Append($"{meshes.Count} meshes ({treeMeshes} from terrain trees), {renderers.Count} scene renderers. ");
            report.Append($"Mesh LOD: switched on in {switchedOn} of {models.Count} models, made in {made} of {own.Count} own meshes, ");
            report.Append($"{already} already had it, {tooSmall} too small to lighten. ");
            report.Append($"Hide distances: {groups} new, {refreshed} refreshed, {skipped} left with their own LODGroup (");
            foreach (KeyValuePair<LodCategory, int> kind in perKind) report.Append($"{kind.Key} {kind.Value}, ");
            report.Append(").");
            File.WriteAllText(ReportPath, report.ToString());
            return report.ToString();
        }

        /// <summary>A mesh of the world: not part of a character, not an effect, not editor-only.</summary>
        private static bool IsWorldMesh(MeshRenderer renderer, out Mesh mesh)
        {
            mesh = null;
            if (renderer.CompareTag("EditorOnly")) return false;
            var filter = renderer.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) return false;
            if (renderer.GetComponentInParent<Animator>(true) != null) return false; // a character's weapon, an NPC
            if (renderer.GetComponentInParent<Unity.Netcode.NetworkObject>(true) != null) return false; // spawned things
            Material material = renderer.sharedMaterial;
            if (material == null || material.shader == null) return false;
            string shader = material.shader.name;
            if (shader.Contains("Particle") || shader.StartsWith("KryticalNova/Nova")) return false;
            mesh = filter.sharedMesh;
            return true;
        }

        private static long Triangles(Mesh mesh)
        {
            long triangles = 0;
            for (int i = 0; i < mesh.subMeshCount; i++) triangles += mesh.GetIndexCount(i) / 3;
            return triangles;
        }
    }
}
