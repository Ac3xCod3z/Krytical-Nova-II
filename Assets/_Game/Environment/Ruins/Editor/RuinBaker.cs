using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RipVertices.QuickDestruction;
using RipVertices.QuickDestruction.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Bakes the static ruin library with Quick Destruction (RipVertices). Each Synty source is fractured once per
    /// variant (QD's editor bake pipeline), then the fragments are dropped in an isolated edit-mode physics scene to
    /// settle into one of three damage tiers:
    ///   Damaged   – a corner bitten out of the top, the loose chunks lying at the foot;
    ///   Collapsed – a jagged stump with the upper floors toppled to one side;
    ///   Rubble    – everything above knee height slumped into a heap;
    ///   Sliced    – (towers) one angled cut, the top half leaning on the bottom half.
    /// The settled pieces are merged into static meshes ("Standing" + clustered "Fallen" chunks, cut faces in ruin
    /// concrete) and saved as plain prefabs in this system's folder; no Quick Destruction runtime component, rigidbody
    /// or generated vendor asset survives the bake, so ruins cost what any static mesh costs and need no network sync.
    /// Always fractures at the world origin. Re-run to refresh; StarterTownBuilder places the results.
    /// </summary>
    public static class RuinBaker
    {
        public enum Tier { Damaged, Collapsed, Rubble, Sliced }
        public const string LeaningName = "Chunk_Leaning"; // a Sliced top half: placed as baked, never re-snapped

        public const string LibraryFolder = "Assets/_Game/Environment/Ruins/Prefabs";
        private const string MeshFolder = "Assets/_Game/Environment/Ruins/Meshes";
        private const string ProfilePath = "Assets/_Game/Environment/Ruins/Profiles/DP_RuinConcrete.asset";
        private const string InteriorMaterialPath = "Assets/_Game/Environment/Buildings/Materials/BuildingPalette/M_Ruin_Interior.mat";
        private const string BuildingFolder = "Assets/Synty/PolygonSciFiCity/Prefabs/Buildings/";
        public const string StandingName = "Standing";
        public const string FallenName = "Fallen";
        public const int Variants = 2;

        public const string Wall = "SM_Bld_City_Wall_01";
        public const string WallTower = "SM_Bld_City_Wall_Tower_01";

        public static readonly string[] TowerSources =
        {
            "SM_Bld_Background_Lrg_01", "SM_Bld_Background_Lrg_02", "SM_Bld_Background_Lrg_03",
            "SM_Bld_Background_Med_01", "SM_Bld_Background_Med_03", "SM_Bld_Background_Med_05",
            "SM_Bld_Background_Med_06", "SM_Bld_Background_Med_09",
            "SM_Bld_Large_01", "SM_Bld_Large_03", "SM_Bld_Large_06",
        };

        private static IEnumerable<string> AllSources => TowerSources.Concat(new[] { Wall, WallTower });

        public static string PrefabPath(string source, Tier tier, int variant) => $"{LibraryFolder}/RUIN_{source}_{tier}_v{variant}.prefab";
        private static string MeshPath(string source, Tier tier, int variant) => $"{MeshFolder}/RUIN_{source}_{tier}_v{variant}.asset";

        /// <summary>Baked variants by source name and tier (only those on disk).</summary>
        public static Dictionary<string, Dictionary<Tier, List<GameObject>>> Library()
        {
            var lib = new Dictionary<string, Dictionary<Tier, List<GameObject>>>();
            foreach (string source in AllSources)
            foreach (Tier tier in Enum.GetValues(typeof(Tier)))
                for (int v = 0; v < Variants; v++)
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(source, tier, v));
                    if (prefab == null) continue;
                    if (!lib.TryGetValue(source, out var byTier)) lib[source] = byTier = new Dictionary<Tier, List<GameObject>>();
                    if (!byTier.TryGetValue(tier, out var list)) byTier[tier] = list = new List<GameObject>();
                    list.Add(prefab);
                }
            return lib;
        }

        [MenuItem("Krytical Nova/Zone/Bake Ruin Library (Quick Destruction)")]
        public static void BakeLibraryMenu() => BakeLibrary(AllSources.ToArray());

        public static void BakeLibrary(string[] sources)
        {
            var bake = FindBakeMethod();
            if (bake == null) { Debug.LogError("[RuinBaker] Quick Destruction's BakePipeline.Bake was not found."); return; }
            WeatherVfxBuilder.EnsureFolder(LibraryFolder);
            WeatherVfxBuilder.EnsureFolder(MeshFolder);
            var profile = EnsureProfile();

            var active = SceneManager.GetActiveScene();
            var temp = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            int made = 0, failed = 0, step = 0, total = sources.Length * Variants;
            try
            {
                foreach (string source in sources)
                    for (int v = 0; v < Variants; v++)
                    {
                        if (EditorUtility.DisplayCancelableProgressBar("Baking ruins", $"{source} v{v}", (float)step++ / total)) return;
                        try { made += BakeSource(bake, profile, temp, source, v); }
                        catch (Exception e) { failed++; Debug.LogError($"[RuinBaker] {source} v{v}: {e}"); }
                    }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                EditorSceneManager.CloseScene(temp, true);
                SceneManager.SetActiveScene(active);
                AssetDatabase.SaveAssets();
                Debug.Log($"[RuinBaker] Baked {made} ruin prefabs into {LibraryFolder} ({failed} source variants failed). Rebuild the town to place them.");
            }
        }

        // ---------------------------------------------------------------- bake

        private static int BakeSource(MethodInfo bake, DestructionProfile profile, Scene temp, string source, int variant)
        {
            var sourcePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BuildingFolder + source + ".prefab");
            if (sourcePrefab == null) { Debug.LogWarning($"[RuinBaker] Missing source prefab {source}."); return 0; }

            SceneManager.SetActiveScene(temp);
            var src = (GameObject)PrefabUtility.InstantiatePrefab(sourcePrefab, temp);
            src.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            bool isWall = source == Wall;
            profile.Seed = StableHash(source) + variant * 7919;
            profile.FragmentCount = isWall ? 32 : source == WallTower ? 40 : 55;

            var args = bake.GetParameters().Select(p => p.HasDefaultValue ? p.DefaultValue : null).ToArray();
            args[0] = src; args[1] = profile; args[2] = false; // placeInScene: no, we only want the prefab
            args[3] = true; // allowUnsafeOpenFullSolid: some Synty towers have open edges; stray caps vanish in a ruin
            object outcome = bake.Invoke(null, args);
            Object.DestroyImmediate(src);
            var type = outcome.GetType();
            if (!(bool)type.GetField("Success").GetValue(outcome))
                throw new Exception((string)type.GetField("Error").GetValue(outcome));
            string destructiblePath = (string)type.GetField("PrefabPath").GetValue(outcome);
            string outputFolder = (string)type.GetField("OutputFolder").GetValue(outcome);

            int made = 0;
            try
            {
                var destructible = AssetDatabase.LoadAssetAtPath<GameObject>(destructiblePath);
                foreach (Tier tier in Enum.GetValues(typeof(Tier)))
                {
                    if (tier == Tier.Sliced) continue; // made from the intact source below
                    var rng = new System.Random(StableHash(source) ^ (variant * 31 + (int)tier * 1013));
                    if (BakeTier(destructible, source, tier, variant, rng)) made++;
                }
                if (TowerSources.Contains(source) && BakeSliced(sourcePrefab, source, variant)) made++;
            }
            finally
            {
                // The vendor's generated destructible + manifest are only an intermediate.
                if (!string.IsNullOrEmpty(outputFolder) && AssetDatabase.IsValidFolder(outputFolder)) AssetDatabase.DeleteAsset(outputFolder);
            }
            return made;
        }

        private static bool BakeTier(GameObject destructible, string source, Tier tier, int variant, System.Random rng)
        {
            // A preview scene owns its own PhysicsScene, so simulating it never touches the open zone.
            var sim = EditorSceneManager.NewPreviewScene();
            var root = (GameObject)Object.Instantiate(destructible);
            SceneManager.MoveGameObjectToScene(root, sim);
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            SceneManager.MoveGameObjectToScene(ground, sim);
            ground.transform.position = new Vector3(0f, -0.5f, 0f);
            ground.transform.localScale = new Vector3(400f, 1f, 400f);
            try
            {
                var fractured = root.transform.Find("Fractured");
                if (fractured == null) throw new Exception("No 'Fractured' pool in the destructible.");
                var intact = root.transform.Find("Intact");
                if (intact != null) Object.DestroyImmediate(intact.gameObject);
                foreach (var c in root.GetComponents<Component>())
                    if (!(c is Transform) && !(c is Rigidbody)) Object.DestroyImmediate(c);
                foreach (var rbRoot in root.GetComponents<Rigidbody>()) Object.DestroyImmediate(rbRoot);
                fractured.gameObject.SetActive(true);

                var frags = new List<Rigidbody>();
                foreach (Transform t in fractured)
                {
                    if (!t.name.StartsWith("Frag_")) { Object.DestroyImmediate(t.gameObject); continue; }
                    foreach (var mb in t.GetComponents<MonoBehaviour>()) Object.DestroyImmediate(mb);
                    var rb = t.GetComponent<Rigidbody>();
                    if (rb == null) continue;
                    frags.Add(rb);
                }
                if (frags.Count == 0) return false;

                Bounds all = Encapsulate(frags.Select(r => r.GetComponent<Renderer>()));
                float height = Mathf.Max(0.1f, all.size.y);
                float maxDim = Mathf.Max(all.size.x, all.size.y, all.size.z);
                Vector3 toppleDir = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f) * Vector3.forward;
                bool isWall = source == Wall;
                if (isWall) toppleDir = (rng.NextDouble() < 0.5 ? 1f : -1f) * Vector3.forward; // walls fall to a face

                // Decide which fragments break loose.
                var falling = new HashSet<Rigidbody>();
                Vector3 bite = new Vector3(
                    Mathf.Lerp(all.min.x, all.max.x, rng.NextDouble() < 0.5 ? 0.15f : 0.85f),
                    all.min.y + height * Mathf.Lerp(0.8f, 1f, (float)rng.NextDouble()),
                    Mathf.Lerp(all.min.z, all.max.z, rng.NextDouble() < 0.5 ? 0.15f : 0.85f));
                float cut = tier == Tier.Collapsed ? Mathf.Lerp(0.3f, 0.55f, (float)rng.NextDouble())
                          : tier == Tier.Rubble ? 0.1f : 1f;
                if (isWall && tier == Tier.Collapsed) cut = Mathf.Lerp(0.15f, 0.35f, (float)rng.NextDouble()); // breach

                foreach (var rb in frags)
                {
                    Vector3 c = rb.GetComponent<Renderer>().bounds.center;
                    float h01 = (c.y - all.min.y) / height;
                    float jag = ((float)rng.NextDouble() - 0.5f) * 0.16f;
                    bool loose = tier switch
                    {
                        Tier.Damaged => Vector3.Distance(c, bite) < maxDim * (isWall ? 0.3f : 0.38f),
                        Tier.Collapsed when isWall => h01 > cut + jag && Mathf.Abs(c.x - all.center.x) < all.extents.x * 0.55f,
                        _ => h01 > cut + jag,
                    };
                    if (loose) falling.Add(rb);
                }
                DropUnsupported(frags, falling, all.min.y);

                // Settle. Standing pieces are kinematic, so they keep their real (non-convex) shape and loose pieces
                // come to rest on actual geometry instead of invisible hulls across rooms and ledges.
                foreach (var rb in frags)
                {
                    bool loose = falling.Contains(rb);
                    var col = rb.GetComponent<MeshCollider>();
                    if (col != null) col.convex = loose;
                    rb.isKinematic = !loose;
                    rb.useGravity = true;
                    rb.mass = Mathf.Clamp(rb.mass, 1f, 400f);
                    rb.linearDamping = 0.05f;
                    rb.angularDamping = 0.2f;
                }
                float push = tier == Tier.Rubble ? 2.5f : tier == Tier.Collapsed ? 4f : 1.5f;
                foreach (var rb in falling)
                {
                    Vector3 c = rb.worldCenterOfMass;
                    Vector3 outward = new Vector3(c.x - all.center.x, 0f, c.z - all.center.z).normalized;
                    Vector3 dir = tier == Tier.Rubble ? outward : (toppleDir + outward * 0.4f).normalized;
                    float h01 = (c.y - all.min.y) / height;
                    rb.linearVelocity = dir * push * (0.5f + h01);
                    rb.angularVelocity = new Vector3((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f);
                }
                SimulateUntilRest(sim.GetPhysicsScene(), falling);

                // Keep what landed near the building; drop strays that bounced off into the distance or fell through.
                float keepRadius = maxDim * 1.6f + 6f;
                var standing = frags.Where(r => !falling.Contains(r)).ToList();
                var fallen = falling.Where(r => r.position.y > -2f &&
                    new Vector2(r.position.x - all.center.x, r.position.z - all.center.z).magnitude < keepRadius).ToList();

                return SaveRuin(source, tier, variant, root.transform, standing, fallen);
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(sim);
            }
        }

        /// <summary>
        /// Anything still standing must connect to the ground through other standing pieces; otherwise it would hang in
        /// the air above the hole the bite / cut left. Flood-fill from ground-touching fragments; the rest fall too.
        /// </summary>
        private static void DropUnsupported(List<Rigidbody> frags, HashSet<Rigidbody> falling, float baseY)
        {
            var standing = frags.Where(r => !falling.Contains(r)).ToList();
            var bounds = standing.Select(r => { var b = r.GetComponent<Renderer>().bounds; b.Expand(0.1f); return b; }).ToList();
            var supported = new bool[standing.Count];
            var queue = new Queue<int>();
            for (int i = 0; i < standing.Count; i++)
                if (bounds[i].min.y < baseY + 0.35f) { supported[i] = true; queue.Enqueue(i); }
            while (queue.Count > 0)
            {
                int i = queue.Dequeue();
                for (int j = 0; j < standing.Count; j++)
                    if (!supported[j] && bounds[i].Intersects(bounds[j])) { supported[j] = true; queue.Enqueue(j); }
            }
            for (int i = 0; i < standing.Count; i++)
                if (!supported[i]) falling.Add(standing[i]);
        }

        /// <summary>Steps physics until every loose piece is at rest (or 15 s), so nothing is captured mid-fall.</summary>
        private static void SimulateUntilRest(PhysicsScene physics, ICollection<Rigidbody> bodies)
        {
            const float dt = 0.02f;
            for (int step = 1; step <= 750; step++)
            {
                physics.Simulate(dt);
                if (step < 100 || step % 25 != 0) continue;
                bool resting = true;
                foreach (var rb in bodies)
                    if (rb.linearVelocity.sqrMagnitude > 0.0025f || rb.angularVelocity.sqrMagnitude > 0.01f) { resting = false; break; }
                if (resting) return;
            }
        }

        /// <summary>
        /// "Sliced" tier: one clean angled cut through the middle of the intact tower; the top half topples and comes to
        /// rest leaning on the bottom half with its low edge on the ground. Seeds are retried until it actually leans.
        /// </summary>
        private static bool BakeSliced(GameObject sourcePrefab, string source, int variant)
        {
            var interior = AssetDatabase.LoadAssetAtPath<Material>(InteriorMaterialPath);
            var merged = MergeSource(sourcePrefab, out var materials);
            if (merged == null) return false;
            var mats = materials.Concat(new[] { interior }).ToArray(); // + the cut cap submesh
            Bounds b = merged.bounds;

            for (int attempt = 0; attempt < 6; attempt++)
            {
                var rng = new System.Random(StableHash(source) ^ (variant * 131 + attempt * 7));
                float h = Mathf.Lerp(0.42f, 0.58f, (float)rng.NextDouble());
                float tilt = Mathf.Lerp(20f, 35f, (float)rng.NextDouble());
                float yaw = (float)rng.NextDouble() * 360f;
                Vector3 lowSide = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;   // the cut slopes down toward here
                Vector3 normal = Quaternion.AngleAxis(tilt, Vector3.Cross(Vector3.up, lowSide)) * Vector3.up;
                var plane = new Plane(normal, new Vector3(b.center.x, b.min.y + b.size.y * h, b.center.z));

                MeshSlicer.Slice(WorkMesh.FromMesh(merged), plane, 1f, CutSurfaceUVMode.FitToCut, out var top, out var bottom);
                if (top == null || bottom == null) continue;
                var topMesh = top.ToMesh($"{source}_SlicedTop");
                var bottomMesh = bottom.ToMesh($"{source}_SlicedBottom");

                var sim = EditorSceneManager.NewPreviewScene();
                try
                {
                    var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    SceneManager.MoveGameObjectToScene(ground, sim);
                    ground.transform.position = new Vector3(0f, -0.5f, 0f);
                    ground.transform.localScale = new Vector3(400f, 1f, 400f);

                    var space = new GameObject("Root");
                    SceneManager.MoveGameObjectToScene(space, sim);
                    var bottomRb = Piece(space.transform, "Bottom", bottomMesh, mats, false);
                    var topRb = Piece(space.transform, "Top", topMesh, mats, true);
                    topRb.mass = 400f;
                    // Start the top half as if it already slid off the low side of the cut: stood on the ground beside
                    // the stump, tipped back toward it, pushed out until it clears the stump. Physics then lets it
                    // fall against the stump and come to rest leaning there.
                    var t = topRb.transform;
                    var topCol = topRb.GetComponent<Collider>();
                    var botCol = bottomRb.GetComponent<Collider>();
                    float lean = Mathf.Lerp(16f, 28f, (float)rng.NextDouble());
                    var tb0 = topRb.GetComponent<Renderer>().bounds;
                    t.RotateAround(tb0.center, Vector3.Cross(Vector3.up, lowSide), -lean);
                    Physics.SyncTransforms();
                    t.position += Vector3.up * (0.02f - topRb.GetComponent<Renderer>().bounds.min.y);
                    for (int k = 0; k < 200; k++)
                    {
                        Physics.SyncTransforms();
                        if (!Physics.ComputePenetration(topCol, t.position, t.rotation, botCol, bottomRb.transform.position,
                                bottomRb.transform.rotation, out _, out _)) break;
                        t.position += lowSide * 0.2f;
                    }
                    t.position -= lowSide * 0.1f; // touching, so it rests against the stump instead of tipping away
                    topRb.position = t.position;
                    topRb.rotation = t.rotation;
                    SimulateUntilRest(sim.GetPhysicsScene(), new[] { topRb });

                    var tb = topRb.GetComponent<Renderer>().bounds;
                    var bb = bottomRb.GetComponent<Renderer>().bounds;
                    bb.Expand(0.4f);
                    bool onGround = tb.min.y < 0.6f;
                    bool leaning = tb.Intersects(bb) && tb.max.y > bb.max.y * 0.6f;
                    if (!onGround || !leaning)
                    {
                        Debug.Log($"[RuinBaker] {source} sliced attempt {attempt} rejected (onGround={onGround}, leaning={leaning}, top={tb.min.y:0.0}..{tb.max.y:0.0}).");
                        Object.DestroyImmediate(topMesh); Object.DestroyImmediate(bottomMesh); continue;
                    }

                    topRb.gameObject.name = LeaningName;
                    bool saved = SaveRuin(source, Tier.Sliced, variant, space.transform, new List<Rigidbody> { bottomRb }, new List<Rigidbody> { topRb });
                    Object.DestroyImmediate(topMesh);
                    Object.DestroyImmediate(bottomMesh);
                    return saved;
                }
                finally
                {
                    EditorSceneManager.ClosePreviewScene(sim);
                }
            }
            Debug.LogWarning($"[RuinBaker] {source} v{variant}: no Sliced seed came to rest leaning; skipped.");
            return false;
        }

        private static Rigidbody Piece(Transform parent, string name, Mesh mesh, Material[] mats, bool dynamic)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = mats.Take(mesh.subMeshCount).ToArray();
            var col = go.AddComponent<MeshCollider>();
            col.sharedMesh = mesh;
            col.convex = dynamic;
            var rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = !dynamic;
            rb.angularDamping = 0.3f;
            return rb;
        }

        /// <summary>All of the source prefab's meshes in prefab space, one submesh per material.</summary>
        private static Mesh MergeSource(GameObject prefab, out Material[] materials)
        {
            var byMaterial = new Dictionary<Material, List<CombineInstance>>();
            foreach (var mr in prefab.GetComponentsInChildren<MeshRenderer>())
            {
                var mf = mr.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var ms = mr.sharedMaterials;
                for (int s = 0; s < mf.sharedMesh.subMeshCount && s < ms.Length; s++)
                {
                    if (ms[s] == null) continue;
                    if (!byMaterial.TryGetValue(ms[s], out var list)) byMaterial[ms[s]] = list = new List<CombineInstance>();
                    list.Add(new CombineInstance { mesh = mf.sharedMesh, subMeshIndex = s, transform = prefab.transform.worldToLocalMatrix * mr.transform.localToWorldMatrix });
                }
            }
            materials = byMaterial.Keys.ToArray();
            if (byMaterial.Count == 0) return null;
            var parts = byMaterial.Values.Select(v =>
            {
                var m = new Mesh { indexFormat = IndexFormat.UInt32 };
                m.CombineMeshes(v.ToArray(), true, true);
                return new CombineInstance { mesh = m, transform = Matrix4x4.identity };
            }).ToArray();
            var merged = new Mesh { indexFormat = IndexFormat.UInt32 };
            merged.CombineMeshes(parts, false, false);
            foreach (var part in parts) Object.DestroyImmediate(part.mesh);
            merged.RecalculateBounds();
            return merged;
        }

        // ---------------------------------------------------------------- output

        private static bool SaveRuin(string source, Tier tier, int variant, Transform space, List<Rigidbody> standing, List<Rigidbody> fallen)
        {
            string name = $"RUIN_{source}_{tier}_v{variant}";
            var meshes = new List<Mesh>();
            var prefabRoot = new GameObject(name);
            try
            {
                var standingGo = new GameObject(StandingName);
                standingGo.transform.SetParent(prefabRoot.transform, false);
                if (standing.Count > 0) AddMerged(standingGo, standing, space, $"{name}_Standing", meshes);

                var fallenGo = new GameObject(FallenName);
                fallenGo.transform.SetParent(prefabRoot.transform, false);
                // Small clusters so each settles onto the terrain on its own (a big mixed cluster can hang off a slope).
                int clusters = Mathf.Clamp(fallen.Count / 4, fallen.Count > 0 ? 1 : 0, 12);
                foreach (var (cluster, i) in Cluster(fallen, clusters).Select((c, i) => (c, i)))
                {
                    if (cluster.Count == 0) continue;
                    bool leaning = cluster.Count == 1 && cluster[0].name == LeaningName;
                    var chunk = new GameObject(leaning ? LeaningName : $"Chunk_{i:00}");
                    chunk.transform.SetParent(fallenGo.transform, false);
                    AddMerged(chunk, cluster, space, $"{name}_Chunk_{i:00}", meshes);
                }
                if (meshes.Count == 0) return false;

                string meshPath = MeshPath(source, tier, variant);
                AssetDatabase.DeleteAsset(meshPath);
                AssetDatabase.CreateAsset(meshes[0], meshPath);
                for (int i = 1; i < meshes.Count; i++) AssetDatabase.AddObjectToAsset(meshes[i], meshPath);
                AssetDatabase.SaveAssets();

                GameObjectUtility.SetStaticEditorFlags(prefabRoot, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic
                    | StaticEditorFlags.OccluderStatic | StaticEditorFlags.ContributeGI | StaticEditorFlags.NavigationStatic);
                foreach (Transform t in prefabRoot.GetComponentsInChildren<Transform>())
                    GameObjectUtility.SetStaticEditorFlags(t.gameObject, GameObjectUtility.GetStaticEditorFlags(prefabRoot));
                PrefabUtility.SaveAsPrefabAsset(prefabRoot, PrefabPath(source, tier, variant));
                return true;
            }
            finally
            {
                Object.DestroyImmediate(prefabRoot);
            }
        }

        /// <summary>Merges fragment meshes (plus their interior scatter) into one mesh with a submesh per material.</summary>
        private static void AddMerged(GameObject host, List<Rigidbody> pieces, Transform space, string meshName, List<Mesh> meshes)
        {
            var byMaterial = new Dictionary<Material, List<CombineInstance>>();
            foreach (var rb in pieces)
            foreach (var mr in rb.GetComponentsInChildren<MeshRenderer>())
            {
                // QD's interior scatter (rebar/clutter inside cut faces) is ~90% of the triangles and unreadable at
                // gameplay distance; the cut caps in ruin concrete carry the look.
                if (mr.gameObject.name == "Interior Scatter") continue;
                var mf = mr.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var mats = mr.sharedMaterials;
                for (int s = 0; s < mf.sharedMesh.subMeshCount && s < mats.Length; s++)
                {
                    if (mats[s] == null) continue;
                    if (!byMaterial.TryGetValue(mats[s], out var list)) byMaterial[mats[s]] = list = new List<CombineInstance>();
                    list.Add(new CombineInstance { mesh = mf.sharedMesh, subMeshIndex = s, transform = space.worldToLocalMatrix * mr.transform.localToWorldMatrix });
                }
            }
            if (byMaterial.Count == 0) return;

            var parts = new List<CombineInstance>();
            var materials = new List<Material>();
            foreach (var kv in byMaterial)
            {
                var part = new Mesh { indexFormat = IndexFormat.UInt32 };
                part.CombineMeshes(kv.Value.ToArray(), true, true);
                parts.Add(new CombineInstance { mesh = part, transform = Matrix4x4.identity });
                materials.Add(kv.Key);
            }
            // Pick the index format before combining: switching it afterwards drops the index buffer.
            int vertexTotal = parts.Sum(p => p.mesh.vertexCount);
            var mesh = new Mesh { name = meshName, indexFormat = vertexTotal < 65000 ? IndexFormat.UInt16 : IndexFormat.UInt32 };
            mesh.CombineMeshes(parts.ToArray(), false, false);
            foreach (var p in parts) Object.DestroyImmediate(p.mesh);
            mesh.RecalculateBounds(); // (Mesh.Optimize() empties the mesh on 6000.6 — don't call it)
            meshes.Add(mesh);

            host.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = host.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = materials.ToArray();
            renderer.receiveGI = ReceiveGI.LightProbes; // merged fragments have no lightmap UVs: read APV instead
            host.AddComponent<MeshCollider>().sharedMesh = mesh;
        }

        /// <summary>Simple k-means on XZ so each fallen chunk can be snapped to the terrain on its own.</summary>
        private static List<List<Rigidbody>> Cluster(List<Rigidbody> pieces, int k)
        {
            var result = new List<List<Rigidbody>>();
            if (k <= 0 || pieces.Count == 0) return result;
            var centers = pieces.OrderBy(p => p.position.x + p.position.z * 0.37f)
                .Where((p, i) => i % Mathf.Max(1, pieces.Count / k) == 0).Take(k)
                .Select(p => new Vector2(p.position.x, p.position.z)).ToList();
            for (int iter = 0; iter < 8; iter++)
            {
                result = centers.Select(_ => new List<Rigidbody>()).ToList();
                foreach (var p in pieces)
                {
                    var xz = new Vector2(p.position.x, p.position.z);
                    int best = 0;
                    for (int c = 1; c < centers.Count; c++)
                        if ((centers[c] - xz).sqrMagnitude < (centers[best] - xz).sqrMagnitude) best = c;
                    result[best].Add(p);
                }
                for (int c = 0; c < centers.Count; c++)
                    if (result[c].Count > 0)
                        centers[c] = new Vector2(result[c].Average(p => p.position.x), result[c].Average(p => p.position.z));
            }
            return result;
        }

        private static DestructionProfile EnsureProfile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<DestructionProfile>(ProfilePath);
            if (profile == null)
            {
                WeatherVfxBuilder.EnsureFolder(System.IO.Path.GetDirectoryName(ProfilePath).Replace('\\', '/'));
                profile = ScriptableObject.CreateInstance<DestructionProfile>();
                AssetDatabase.CreateAsset(profile, ProfilePath);
            }
            profile.Pattern = FracturePattern.UniformConcrete;
            profile.InteriorUseSourceMaterial = false;
            profile.InteriorMaterial = AssetDatabase.LoadAssetAtPath<Material>(InteriorMaterialPath);
            profile.Colliders = ColliderStrategy.ConvexMesh;
            EditorUtility.SetDirty(profile);
            return profile;
        }

        private static MethodInfo FindBakeMethod()
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.GetName().Name != "RipVertices.QuickDestruction.Editor") continue;
                var bp = asm.GetTypes().FirstOrDefault(t => t.Name == "BakePipeline");
                var m = bp?.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(x => x.Name == "Bake" && x.GetParameters().Length > 3 && x.GetParameters()[0].ParameterType == typeof(GameObject));
                if (m != null) return m;
            }
            return null;
        }

        private static Bounds Encapsulate(IEnumerable<Renderer> renderers)
        {
            bool first = true;
            var b = new Bounds();
            foreach (var r in renderers)
            {
                if (r == null) continue;
                if (first) { b = r.bounds; first = false; }
                else b.Encapsulate(r.bounds);
            }
            return b;
        }

        private static int StableHash(string s)
        {
            unchecked
            {
                int h = 23;
                foreach (char c in s) h = h * 31 + c;
                return h;
            }
        }
    }
}
