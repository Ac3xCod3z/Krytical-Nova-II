using System.Collections.Generic;
using System.Threading.Tasks;
using Hanzzz.MeshDemolisher;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Bakes a library of broken towers with Mesh Demolisher: each Synty tower is Voronoi-fractured, the lower cells stay
    /// standing as a jagged stump and the upper cells lie toppled around its base. Every variant is saved as a prefab
    /// (meshes in one asset per variant, cut faces in dark ruin concrete) that StarterTownBuilder drops into the ruined
    /// ring. Fracturing runs on a background thread (DemolishAsync) with a timeout, always at the world origin (the
    /// library's exact predicates misbehave far from it), so the editor never stalls. Run once; re-run to refresh.
    /// </summary>
    public static class RuinDemolisher
    {
        public const string LibraryFolder = "Assets/_Game/Environment/Ruins/Prefabs";
        private const string MeshFolder = "Assets/_Game/Environment/Ruins/Meshes";
        private const string BuildingFolder = "Assets/Synty/PolygonSciFiCity/Prefabs/Buildings/";
        private const int Variants = 2;
        private const int TimeoutMs = 30000;
        public const string StandingName = "Standing";
        public const string FallenName = "Fallen";

        // Towers that fracture cleanly (SM_Bld_Background_Lrg_01 is rejected by the library's input checks).
        public static readonly string[] Sources =
        {
            "SM_Bld_Background_Lrg_02", "SM_Bld_Background_Lrg_03", "SM_Bld_Background_Med_01", "SM_Bld_Background_Med_03",
            "SM_Bld_Background_Med_05", "SM_Bld_Background_Med_06", "SM_Bld_Background_Med_09",
            "SM_Bld_Large_01", "SM_Bld_Large_03", "SM_Bld_Large_06",
        };

        private static bool running;

        public static string PrefabPath(string source, int variant) => $"{LibraryFolder}/RUIN_{source}_v{variant}.prefab";

        /// <summary>Baked variants per source tower name (only the ones that exist on disk).</summary>
        public static Dictionary<string, List<GameObject>> Library()
        {
            var lib = new Dictionary<string, List<GameObject>>();
            foreach (string source in Sources)
                for (int v = 0; v < Variants; v++)
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(source, v));
                    if (prefab == null) continue;
                    if (!lib.TryGetValue(source, out var list)) lib[source] = list = new List<GameObject>();
                    list.Add(prefab);
                }
            return lib;
        }

        [MenuItem("Krytical Nova/Zone/Bake Demolished Ruin Library")]
        public static async void BakeLibrary()
        {
            if (running) { Debug.LogWarning("[RuinDemolisher] A bake is already running."); return; }
            running = true;
            int made = 0, failed = 0, total = Sources.Length * Variants, step = 0;
            // Synty models import without Read/Write; the fracture reads vertices off the main thread, so enable it
            // for the bake and restore the original setting afterwards (no net change to the Synty imports).
            var restore = new List<ModelImporter>();
            try
            {
                foreach (string source in Sources)
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BuildingFolder + source + ".prefab");
                    var mesh = prefab != null ? prefab.GetComponentInChildren<MeshFilter>()?.sharedMesh : null;
                    if (mesh == null || mesh.isReadable) continue;
                    if (AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(mesh)) is ModelImporter importer && !importer.isReadable)
                    {
                        importer.isReadable = true;
                        importer.SaveAndReimport();
                        restore.Add(importer);
                    }
                }

                WeatherVfxBuilder.EnsureFolder(LibraryFolder);
                WeatherVfxBuilder.EnsureFolder(MeshFolder);
                var demolisher = new MeshDemolisher();
                foreach (string source in Sources)
                    for (int v = 0; v < Variants; v++)
                    {
                        if (EditorUtility.DisplayCancelableProgressBar("Demolishing towers", $"{source} v{v}", (float)step++ / total)) return;
                        if (await BakeOne(demolisher, source, v)) made++;
                        else failed++;
                    }
            }
            finally
            {
                foreach (var importer in restore)
                {
                    importer.isReadable = false;
                    importer.SaveAndReimport();
                }
                EditorUtility.ClearProgressBar();
                AssetDatabase.SaveAssets();
                running = false;
                Debug.Log($"[RuinDemolisher] Baked {made} ruin variants into {LibraryFolder} ({failed} failed). Rebuild the town to place them.");
            }
        }

        private static async Task<bool> BakeOne(MeshDemolisher demolisher, string source, int variant)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BuildingFolder + source + ".prefab");
            var filter = prefab != null ? prefab.GetComponentInChildren<MeshFilter>() : null;
            if (filter == null || filter.sharedMesh == null) return false;
            Mesh sourceMesh = filter.sharedMesh;
            Bounds b = sourceMesh.bounds;
            var rng = new System.Random(StableHash(source) + variant * 977);

            var target = new GameObject("_DemolishTarget");
            var points = new List<Transform>();
            List<GameObject> fragments = null;
            try
            {
                target.AddComponent<MeshFilter>().sharedMesh = sourceMesh;
                target.AddComponent<MeshRenderer>().sharedMaterial = filter.GetComponent<MeshRenderer>().sharedMaterial;

                // More cells up top (where the tower breaks apart), a few low so the stump edge is jagged.
                int count = 10 + rng.Next(5);
                for (int i = 0; i < count; i++)
                {
                    float t = i < 3 ? Range(rng, 0.05f, 0.45f) : Range(rng, 0.4f, 0.98f);
                    var p = new GameObject("_DemolishPoint").transform;
                    p.position = new Vector3(b.center.x + Range(rng, -0.4f, 0.4f) * b.size.x, b.min.y + t * b.size.y,
                                             b.center.z + Range(rng, -0.4f, 0.4f) * b.size.z);
                    points.Add(p);
                }

                var task = demolisher.DemolishAsync(target, points, ColorBible.RuinInteriorMaterial());
                if (await Task.WhenAny(task, Task.Delay(TimeoutMs)) != task)
                {
                    Debug.LogWarning($"[RuinDemolisher] {source} v{variant} timed out; skipped.");
                    return false;
                }
                if (task.IsFaulted)
                {
                    Debug.LogWarning($"[RuinDemolisher] {source} v{variant} failed: {task.Exception?.GetBaseException().Message}");
                    return false;
                }
                fragments = task.Result;
                if (fragments == null || fragments.Count < 3) return false;
                SaveVariant(source, variant, fragments, b, rng);
                return true;
            }
            finally
            {
                foreach (var p in points) if (p != null) Object.DestroyImmediate(p.gameObject);
                if (fragments != null) foreach (var f in fragments) if (f != null) Object.DestroyImmediate(f);
                Object.DestroyImmediate(target);
            }
        }

        /// <summary>Stump cells stay; cells above the break line topple outward and come to rest on the ground (y = 0).</summary>
        private static void SaveVariant(string source, int variant, List<GameObject> fragments, Bounds b, System.Random rng)
        {
            string meshPath = $"{MeshFolder}/RUIN_{source}_v{variant}.asset";
            if (AssetDatabase.LoadMainAssetAtPath(meshPath) != null) AssetDatabase.DeleteAsset(meshPath);

            var root = new GameObject($"RUIN_{source}_v{variant}");
            var standing = new GameObject(StandingName).transform;
            var fallen = new GameObject(FallenName).transform;
            standing.SetParent(root.transform, false);
            fallen.SetParent(root.transform, false);

            float breakY = b.min.y + b.size.y * Range(rng, 0.32f, 0.55f);
            float fallAngle = Range(rng, 0f, Mathf.PI * 2f); // the tower falls mostly one way
            bool first = true;
            for (int i = 0; i < fragments.Count; i++)
            {
                var frag = fragments[i];
                var mf = frag.GetComponent<MeshFilter>();
                var mesh = mf.sharedMesh;
                mesh.name = $"RUIN_{source}_v{variant}_{i}";
                if (first) { AssetDatabase.CreateAsset(mesh, meshPath); first = false; }
                else AssetDatabase.AddObjectToAsset(mesh, meshPath);

                var piece = new GameObject($"Piece {i}");
                piece.AddComponent<MeshFilter>().sharedMesh = mesh;
                piece.AddComponent<MeshRenderer>().sharedMaterials = frag.GetComponent<MeshRenderer>().sharedMaterials;

                if (frag.transform.position.y < breakY)
                {
                    piece.transform.SetParent(standing, false);
                    piece.transform.SetPositionAndRotation(frag.transform.position, frag.transform.rotation);
                }
                else
                {
                    piece.transform.SetParent(fallen, false);
                    float a = fallAngle + Range(rng, -0.9f, 0.9f);
                    float dist = b.size.y * Range(rng, 0.15f, 0.6f);
                    piece.transform.SetPositionAndRotation(
                        new Vector3(Mathf.Cos(a) * dist, 0f, Mathf.Sin(a) * dist),
                        Quaternion.Euler(Range(rng, 40f, 110f) * (rng.Next(2) == 0 ? 1f : -1f), Range(rng, 0f, 360f), Range(rng, -30f, 30f)));
                    var rb = piece.GetComponent<Renderer>().bounds;
                    piece.transform.position += new Vector3(0f, -0.25f - rb.min.y, 0f);
                }
                piece.AddComponent<BoxCollider>();
            }

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath(source, variant));
            Object.DestroyImmediate(root);
        }

        private static int StableHash(string s)
        {
            unchecked
            {
                int h = 23;
                foreach (char c in s) h = h * 31 + c;
                return h & 0x7FFFFFFF;
            }
        }

        private static float Range(System.Random rng, float min, float max) => min + (float)rng.NextDouble() * (max - min);
    }
}
