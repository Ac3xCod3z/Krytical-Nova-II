using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace KryticalNova.EditorTools.World
{
    /// <summary>
    /// Concord 040 slim (owner approved 12 Nov 2026, 50-tri threshold + Mesh Compression): the zone's 3,605 grid-cell chunk meshes are
    /// replaced by the Blender slim (Drive: Art/Blender/Zones/Concord040, scripts/phase2): unique chunks, whole-chunk copies and part
    /// copies placed from one shared mesh each, and one simplified collision mesh per grid cell instead of a MeshCollider on every
    /// visible chunk. Glass, the Accord Hall door leaves and the thrusters are kept as they are; the prefab keeps its GUID, so Zone01's
    /// instance follows. Copies drop Batching Static and their materials get GPU instancing, so the copies batch instead of being
    /// merged. <see cref="Batch"/> also measures the change in Zone01: the NavMesh still standing on collision, the Sky Lift arrival,
    /// a capsule walk over the stairs and tiers, and render time before and after.
    /// </summary>
    public static class Concord040SlimBuilder
    {
        public const string CollisionGroup = "Collision (slim)";
        private const string Folder = "Assets/_Game/World/Zones/Concord040";
        private const string SlimFolder = Folder + "/Slim";
        private const string PrefabPath = Folder + "/Prefabs/Concord_040.prefab";
        private const string LayoutPath = SlimFolder + "/Concord040_SlimLayout.json";
        private const string ScenePath = "Assets/_Game/Scenes/World/Towns/Zone01_StarterTown.unity";
        private const string RootName = "Concord 040 (sky preview)";
        private const string PropsGroup = "Static Architecture and Props";
        private static readonly string[] SlimGroups = { "Chunks", "Chunk Copies", "Part Copies" };
        private const StaticEditorFlags ChunkFlags = StaticEditorFlags.OccluderStatic | StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic;
        private const StaticEditorFlags CopyFlags = StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic;

        [Serializable] private class Obj { public string name, mesh, kind; public int collider; public float[] m; }
        [Serializable] private class Layout { public Obj[] objects; public string[] collision; }

        private static readonly StringBuilder Report = new StringBuilder();
        private static void Log(string s) { Report.AppendLine(s); Debug.Log("[Concord040Slim] " + s); }

        [MenuItem("Krytical Nova/World/Concord 040/Build Slim Prefab")]
        public static void BuildMenu() => Build();

        /// <summary>Batch: Unity -batchmode -executeMethod KryticalNova.EditorTools.World.Concord040SlimBuilder.Batch (env KN_C40_OUT = report folder).</summary>
        public static void Batch()
        {
            int code = 0;
            string outDir = Environment.GetEnvironmentVariable("KN_C40_OUT") ?? "Logs/Concord040Slim";
            Directory.CreateDirectory(outDir);
            try
            {
                Measure("OLD", outDir, false);
                if (!Build()) code = 2;
                else
                {
                    Measure("NEW", outDir, true);
                    MeasureResidentDrawer(outDir);
                }
            }
            catch (Exception e) { Log("FAIL " + e); code = 1; }
            File.WriteAllText(Path.Combine(outDir, "report.txt"), Report.ToString());
            EditorApplication.Exit(code);
        }

        /// <summary>
        /// The NavMesh rebake on the slim collision (owner, 12 Nov 2026, before build 16): Zone01's Concord NavMeshSurface bakes again
        /// with its own settings, and the result is written over the existing Concord040_NavMesh asset, so the scene keeps its
        /// reference and is not saved. Measured before and after. Batch: -executeMethod ...Concord040SlimBuilder.RebakeBatch.
        /// </summary>
        public static void RebakeBatch()
        {
            int code = 0;
            string outDir = Environment.GetEnvironmentVariable("KN_C40_OUT") ?? "Logs/Concord040Slim";
            Directory.CreateDirectory(outDir);
            try
            {
                Measure("BEFORE", outDir, false);
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                var root = GameObject.Find(RootName);
                var surface = root != null ? root.GetComponent<Unity.AI.Navigation.NavMeshSurface>() : null;
                var asset = AssetDatabase.LoadAssetAtPath<NavMeshData>(Folder + "/Concord040_NavMesh.asset");
                if (surface == null || asset == null || surface.navMeshData != asset) { Log("FAIL no Concord NavMeshSurface using Concord040_NavMesh.asset"); code = 2; }
                else
                {
                    Physics.SyncTransforms();
                    surface.BuildNavMesh();                       // a new in-memory NavMeshData from the slim collision
                    var fresh = surface.navMeshData;
                    if (fresh == null || fresh == asset) { Log("FAIL bake produced nothing"); code = 2; }
                    else
                    {
                        surface.RemoveData();
                        EditorUtility.CopySerialized(fresh, asset);  // same asset, same GUID: the scene's reference stays
                        asset.name = "Concord040_NavMesh";
                        EditorUtility.SetDirty(asset);
                        AssetDatabase.SaveAssets();
                        Log("NavMesh rebaked into Concord040_NavMesh.asset (scene not saved)");
                        Measure("AFTER", outDir, true);
                    }
                }
            }
            catch (Exception e) { Log("FAIL " + e); code = 1; }
            File.WriteAllText(Path.Combine(outDir, "report.txt"), Report.ToString());
            EditorApplication.Exit(code);
        }

        // ---------------------------------------------------------------- build

        public static bool Build()
        {
            if (!File.Exists(LayoutPath)) { Log("FAIL no layout at " + LayoutPath); return false; }
            var layout = JsonUtility.FromJson<Layout>(File.ReadAllText(LayoutPath));
            // Imported first without compression, so the axis fix is found on exact positions; the compression level is then
            // chosen by measurement (run 1, 12 Nov: Medium moved large chunks' vertices by up to 13.5 cm).
            ConfigureImporters(ModelImporterMeshCompression.Off);
            var meshes = LoadMeshes();
            if (meshes == null) return false;
            var exact = new Dictionary<string, Vector3[]>();
            foreach (var kv in meshes) if (kv.Value.mesh != null && !kv.Key.StartsWith("COL_")) exact[kv.Key] = kv.Value.mesh.vertices;
            Log($"meshes loaded: {meshes.Count}");

            var contents = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var props = contents.transform.Find(PropsGroup);
                if (props == null) { Log("FAIL no " + PropsGroup); return false; }
                // world bounds of the old chunks, to find the axis fix and to prove every piece sits where it sat
                var oldBounds = new Dictionary<string, Bounds>();
                foreach (var mf in props.GetComponentsInChildren<MeshFilter>(true))
                    if (mf.sharedMesh != null && mf.name.StartsWith("Opaque_")) oldBounds[mf.name] = Xform(mf.transform.localToWorldMatrix, mf.sharedMesh.bounds);
                if (oldBounds.Count == 0 && props.Find("Chunks") == null) { Log("FAIL no old chunks and no slim groups"); return false; }

                Matrix4x4 fix = Matrix4x4.identity;
                if (oldBounds.Count > 0)
                {
                    fix = FindAxisFix(layout, meshes, oldBounds, out float err);
                    if (err > 0.02f) { Log($"FAIL no axis fix fits (best error {err:0.000} m)"); return false; }
                    Log($"axis fix {fix.rotation.eulerAngles} (worst chunk-copy bounds error {err * 1000:0.0} mm)");
                }

                // the strongest Mesh Compression that moves no vertex more than CompressionTolerance (the owner's pick, without visible loss)
                var chosen = ModelImporterMeshCompression.Off;
                foreach (var level in new[] { ModelImporterMeshCompression.Medium, ModelImporterMeshCompression.Low })
                {
                    ConfigureImporters(level);
                    var test = LoadMeshes();
                    float worstMove = 0f; string worstMesh = "";
                    foreach (var kv in exact)
                    {
                        if (!test.TryGetValue(kv.Key, out var t) || t.mesh == null) continue;
                        var v = t.mesh.vertices;
                        if (v.Length != kv.Value.Length) { worstMove = float.MaxValue; worstMesh = kv.Key + " (vertex count changed)"; break; }
                        for (int i = 0; i < v.Length; i++) { float d = (v[i] - kv.Value[i]).magnitude; if (d > worstMove) { worstMove = d; worstMesh = kv.Key; } }
                    }
                    Log($"Mesh Compression {level}: worst vertex move {(worstMove == float.MaxValue ? "n/a" : (worstMove * 1000).ToString("0.0") + " mm")} ({worstMesh})");
                    if (worstMove <= CompressionTolerance) { chosen = level; meshes = test; break; }
                }
                if (chosen == ModelImporterMeshCompression.Off) { ConfigureImporters(ModelImporterMeshCompression.Off); meshes = LoadMeshes(); }
                Log($"Mesh Compression kept: {chosen} (tolerance {CompressionTolerance * 1000:0} mm)");
                VertCache.Clear();   // the meshes were reimported: measure the kept ones

                for (int i = props.childCount - 1; i >= 0; i--)
                {
                    var c = props.GetChild(i);
                    if (c.name.StartsWith("Opaque_") || SlimGroups.Contains(c.name)) Object.DestroyImmediate(c.gameObject);
                }
                var oldCol = contents.transform.Find(CollisionGroup);
                if (oldCol != null) Object.DestroyImmediate(oldCol.gameObject);

                var groups = SlimGroups.ToDictionary(g => g, g => { var t = new GameObject(g).transform; t.SetParent(props, false); return t; });
                var instanced = new HashSet<Material>();
                int placed = 0, missing = 0, outside = 0;
                float worst = 0f;
                foreach (var o in layout.objects)
                {
                    if (o.kind == "passthrough") continue;
                    if (!meshes.TryGetValue(o.mesh, out var src) || src.mesh == null) { missing++; continue; }
                    Matrix4x4 m = Row(o.m) * fix;
                    string g = o.kind == "chunk" ? "Chunks" : o.kind == "chunk_instance" ? "Chunk Copies" : "Part Copies";
                    var go = new GameObject(o.name);
                    go.transform.SetParent(groups[g], false);
                    go.transform.localPosition = m.GetColumn(3);
                    go.transform.localRotation = m.rotation;
                    go.AddComponent<MeshFilter>().sharedMesh = src.mesh;
                    var r = go.AddComponent<MeshRenderer>();
                    r.sharedMaterials = src.mats;
                    GameObjectUtility.SetStaticEditorFlags(go, o.kind == "chunk" ? ChunkFlags : CopyFlags);
                    if (o.kind != "chunk") foreach (var mat in src.mats) if (mat != null) instanced.Add(mat);
                    // a unique chunk lies inside its old self (parts moved out only shrink it); a whole-chunk copy matches its old self
                    if (o.kind != "part_instance" && oldBounds.TryGetValue(o.name, out var ob))
                    {
                        var nb = VertBounds(m, src.mesh);   // copies turn by any angle: a turned bounding box would be too big (runs 1-2, 12 Nov)
                        float e = o.kind == "chunk_instance" ? BoundsError(nb, ob) : Outside(nb, ob);
                        worst = Mathf.Max(worst, e);
                        if (e > 0.02f) outside++;
                    }
                    placed++;
                }
                Log($"placed {placed} renderers (chunks {groups["Chunks"].childCount}, chunk copies {groups["Chunk Copies"].childCount}, part copies {groups["Part Copies"].childCount}); missing meshes {missing}; off their old place {outside} (worst {worst * 1000:0.0} mm)");
                if (missing > 0 || outside > 0) { Log("FAIL pieces missing or moved"); return false; }

                var col = new GameObject(CollisionGroup).transform;
                col.SetParent(contents.transform, false);
                int colTris = 0;
                foreach (var name in layout.collision)
                {
                    if (!meshes.TryGetValue(name, out var src) || src.mesh == null) continue;
                    var go = new GameObject(name);
                    go.transform.SetParent(col, false);
                    go.transform.localPosition = fix.GetColumn(3);
                    go.transform.localRotation = fix.rotation;
                    var mc = go.AddComponent<MeshCollider>();
                    mc.sharedMesh = src.mesh;
                    mc.cookingOptions = MeshColliderCookingOptions.CookForFasterSimulation | MeshColliderCookingOptions.UseFastMidphase |
                                        MeshColliderCookingOptions.EnableMeshCleaning | MeshColliderCookingOptions.WeldColocatedVertices;
                    colTris += Tris(src.mesh);
                }
                Log($"collision: {col.childCount} cell meshes, {colTris} tris");
                if (col.childCount != layout.collision.Length) { Log($"FAIL collision meshes found {col.childCount} of {layout.collision.Length}"); return false; }

                foreach (var mat in instanced)
                    if (!mat.enableInstancing && AssetDatabase.GetAssetPath(mat).StartsWith(Folder)) { mat.enableInstancing = true; EditorUtility.SetDirty(mat); }
                Log($"GPU instancing on {instanced.Count} materials used by copies");

                PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath);
                AssetDatabase.SaveAssets();
                Log("prefab saved: " + PrefabPath);
                return true;
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
        }

        private const float CompressionTolerance = 0.005f;

        private static Dictionary<string, (Mesh mesh, Material[] mats)> LoadMeshes()
        {
            var meshes = new Dictionary<string, (Mesh mesh, Material[] mats)>();
            foreach (var f in new[] { "Concord040_Chunks", "Concord040_Parts", "Concord040_Collision" })
            {
                var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{SlimFolder}/{f}.fbx");
                if (model == null) { Log("FAIL missing " + f + ".fbx"); return null; }
                foreach (var mf in model.GetComponentsInChildren<MeshFilter>(true))
                    // Blender adds .001 when an object of that name already exists in the .blend (run 3: every COL_ object)
                    meshes[System.Text.RegularExpressions.Regex.Replace(mf.name, @"\.\d{3}$", "")] = (mf.sharedMesh, mf.GetComponent<MeshRenderer>() != null ? mf.GetComponent<MeshRenderer>().sharedMaterials : null);
            }
            return meshes;
        }

        private static void ConfigureImporters(ModelImporterMeshCompression level)
        {
            foreach (var f in new[] { "Concord040_Chunks", "Concord040_Parts", "Concord040_Collision" })
            {
                string path = $"{SlimFolder}/{f}.fbx";
                var mi = AssetImporter.GetAtPath(path) as ModelImporter;
                if (mi == null) continue;
                bool collision = f.EndsWith("Collision");
                mi.meshCompression = collision ? ModelImporterMeshCompression.Off : level;   // collision stays exact
                mi.isReadable = collision;    // collision is cooked from the mesh, also in Addressable bundles
                mi.importNormals = ModelImporterNormals.Import;
                mi.importTangents = collision ? ModelImporterTangents.None : ModelImporterTangents.CalculateMikk;
                mi.importBlendShapes = false; mi.importCameras = false; mi.importLights = false; mi.importVisibility = false;
                mi.importAnimation = false; mi.animationType = ModelImporterAnimationType.None;
                mi.addCollider = false; mi.generateSecondaryUV = false; mi.keepQuads = false; mi.weldVertices = true;
                mi.materialImportMode = collision ? ModelImporterMaterialImportMode.None : ModelImporterMaterialImportMode.ImportStandard;
                mi.materialLocation = ModelImporterMaterialLocation.InPrefab;
                mi.SaveAndReimport();
                if (!collision)
                {
                    // the zone's own Material_xxx assets (Concord040/Materials) by name, never same-named materials elsewhere
                    mi.SearchAndRemapMaterials(ModelImporterMaterialName.BasedOnMaterialName, ModelImporterMaterialSearch.RecursiveUp);
                    mi.SaveAndReimport();
                    int bad = 0;
                    foreach (var r in AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponentsInChildren<MeshRenderer>(true))
                        foreach (var mat in r.sharedMaterials)
                            if (mat == null || !AssetDatabase.GetAssetPath(mat).StartsWith(Folder + "/Materials/")) bad++;
                    Log($"{f}: materials not mapped to Concord040/Materials: {bad}");
                }
            }
        }

        private static Matrix4x4 FindAxisFix(Layout layout, Dictionary<string, (Mesh mesh, Material[] mats)> meshes, Dictionary<string, Bounds> oldBounds, out float best)
        {
            var samples = layout.objects.Where(o => o.kind == "chunk_instance" && oldBounds.ContainsKey(o.name) && meshes.ContainsKey(o.mesh)).Take(80).ToList();
            best = float.MaxValue;
            Matrix4x4 bestFix = Matrix4x4.identity;
            foreach (var x in new[] { 0, 90, 180, 270 })
                foreach (var y in new[] { 0, 90, 180, 270 })
                    foreach (var z in new[] { 0, 90, 180, 270 })
                    {
                        var fix = Matrix4x4.Rotate(Quaternion.Euler(x, y, z));
                        float worst = 0f;
                        foreach (var o in samples)
                        {
                            worst = Mathf.Max(worst, BoundsError(VertBounds(Row(o.m) * fix, meshes[o.mesh].mesh), oldBounds[o.name]));
                            if (worst > best) break;
                        }
                        if (worst < best) { best = worst; bestFix = fix; }
                    }
            return bestFix;
        }

        private static Matrix4x4 Row(float[] m)
        {
            var r = new Matrix4x4();
            for (int i = 0; i < 4; i++) for (int j = 0; j < 4; j++) r[i, j] = m[i * 4 + j];
            return r;
        }

        private static Bounds Xform(Matrix4x4 m, Bounds b)
        {
            var o = new Bounds(m.MultiplyPoint3x4(b.center), Vector3.zero);
            for (int i = 0; i < 8; i++)
                o.Encapsulate(m.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
            return o;
        }

        private static readonly Dictionary<Mesh, Vector3[]> VertCache = new Dictionary<Mesh, Vector3[]>();

        /// <summary>The tight world box of a mesh's own vertices under m (exact for any rotation, unlike a turned bounding box).</summary>
        private static Bounds VertBounds(Matrix4x4 m, Mesh mesh)
        {
            if (!VertCache.TryGetValue(mesh, out var v)) VertCache[mesh] = v = mesh.vertices;
            var b = new Bounds(m.MultiplyPoint3x4(v[0]), Vector3.zero);
            for (int i = 1; i < v.Length; i++) b.Encapsulate(m.MultiplyPoint3x4(v[i]));
            return b;
        }

        private static float BoundsError(Bounds a, Bounds b) => Mathf.Max((a.min - b.min).magnitude, (a.max - b.max).magnitude);
        private static float Outside(Bounds inner, Bounds outer) =>
            Mathf.Max(0f, Mathf.Max(Mathf.Max(outer.min.x - inner.min.x, outer.min.y - inner.min.y, outer.min.z - inner.min.z),
                                    Mathf.Max(inner.max.x - outer.max.x, inner.max.y - outer.max.y, inner.max.z - outer.max.z)));

        // ---------------------------------------------------------------- measure (Zone01, scene never saved)

        // Concord frame (metres): the Sky Lift arrival by the portal, the civic stair onto the Hall terrace, the west market, the
        // Apothecary street, the east access stair up to the 7.25 m tier and stair 024 up to the 12.7 m tier.
        private static readonly Vector2[] Walk =
        {
            new Vector2(0f, -56f), new Vector2(0f, -20f), new Vector2(0f, 39f), new Vector2(-40f, 0f), new Vector2(-40f, -50f),
            new Vector2(55f, -82f), new Vector2(62f, -58f), new Vector2(66f, -44f), new Vector2(66f, -20f), new Vector2(66f, 10f),
        };
        private static readonly (string name, Vector3 pos, Vector3 look)[] Views =
        {
            ("overview", new Vector3(0f, 130f, -210f), new Vector3(0f, 5f, 0f)),
            ("plaza", new Vector3(0f, 1.8f, -40f), new Vector3(0f, 3f, 10f)),
            ("west_street", new Vector3(-22f, 2f, -6f), new Vector3(-60f, 2f, -4f)),
            ("east_tiers", new Vector3(30f, 9.5f, 8f), new Vector3(70f, 10f, 10f)),
        };

        private static void Measure(string tag, string outDir, bool frames)
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var rootGo = GameObject.Find(RootName);
            if (rootGo == null) { Log($"{tag}: FAIL no {RootName} in Zone01"); return; }
            var root = rootGo.transform;
            Physics.SyncTransforms();
            var renderers = root.GetComponentsInChildren<MeshRenderer>(true);
            var colliders = root.GetComponentsInChildren<Collider>(true);
            bool IsHelper(Collider c) => c.transform.IsChildOfName("Deck floors") || c.transform.IsChildOfName("Stair ramps") || c.transform.IsChildOfName("Door blockers");
            var geo = new HashSet<Collider>(colliders.Where(c => !IsHelper(c)));
            int tris = 0; var uniq = new HashSet<Mesh>();
            foreach (var r in renderers) { var mf = r.GetComponent<MeshFilter>(); if (mf && mf.sharedMesh) { uniq.Add(mf.sharedMesh); tris += Tris(mf.sharedMesh); } }
            Log($"{tag}: renderers {renderers.Length}, unique meshes {uniq.Count}, tris drawn {tris}, colliders {colliders.Length} (geometry {geo.Count})");

            // 1) the NavMesh still stands on the geometry's collision
            var tri = NavMesh.CalculateTriangulation();
            int n = 0, miss = 0, off = 0; double sum = 0;
            var hits = new RaycastHit[16];
            for (int i = 0; i + 2 < tri.indices.Length; i += 3)
            {
                Vector3 c = (tri.vertices[tri.indices[i]] + tri.vertices[tri.indices[i + 1]] + tri.vertices[tri.indices[i + 2]]) / 3f;
                Vector3 l = root.InverseTransformPoint(c);
                if (Mathf.Abs(l.x) > 100f || Mathf.Abs(l.z) > 100f || l.y < -5f || l.y > 30f) continue;
                n++;
                int k = Physics.RaycastNonAlloc(c + Vector3.up * 0.6f, Vector3.down, hits, 1.4f, ~0, QueryTriggerInteraction.Ignore);
                float bestD = float.MaxValue;
                for (int h = 0; h < k; h++) if (geo.Contains(hits[h].collider)) bestD = Mathf.Min(bestD, Mathf.Abs(hits[h].point.y - c.y));
                if (bestD == float.MaxValue) { miss++; continue; }
                sum += bestD; if (bestD > 0.25f) off++;
            }
            Log($"{tag}: NavMesh triangles on Concord {n}: no collision under {miss} ({100.0 * miss / Mathf.Max(1, n):0.0}%), more than 0.25 m off {off}, mean gap {sum / Mathf.Max(1, n - miss) * 100:0.0} cm");

            // 2) the Sky Lift arrival stands on ground
            var arrival = GameObject.Find("Concord040_Arrival");
            if (arrival != null)
            {
                Vector3 a = arrival.transform.position;
                bool hit = Physics.Raycast(a + Vector3.up * 1f, Vector3.down, out var ah, 3f, ~0, QueryTriggerInteraction.Ignore);
                bool onNav = NavMesh.SamplePosition(a, out var nh, 0.5f, NavMesh.AllAreas);
                Log($"{tag}: Sky Lift arrival {root.InverseTransformPoint(a)}: ground {(hit ? $"{(ah.point.y - a.y) * 100:0.0} cm off, on {ah.collider.name}" : "NONE")}; " +
                    $"NavMesh {(onNav ? $"{Vector3.Distance(nh.position, a) * 100:0.0} cm away" : "NONE within 0.5 m")}");
            }

            // 3) capsule walk along NavMesh paths (r 0.3, h 1.8, steps up to 0.45 m)
            var capsule = frames ? GameObject.CreatePrimitive(PrimitiveType.Capsule) : null;
            if (capsule != null) { Object.DestroyImmediate(capsule.GetComponent<Collider>()); capsule.transform.localScale = new Vector3(0.6f, 0.9f, 0.6f); }
            Camera cam = null; RenderTexture rt = null; int frame = 0; string frameDir = Path.Combine(outDir, "frames_" + tag.ToLower());
            if (frames) { Directory.CreateDirectory(frameDir); cam = new GameObject("WalkCam").AddComponent<Camera>(); cam.fieldOfView = 60f; rt = new RenderTexture(1280, 720, 24); cam.targetTexture = rt; }
            int steps = 0, blockedWall = 0, blockedStep = 0, noGround = 0;
            float pathLen = 0f;
            var path = new NavMeshPath();
            for (int w = 0; w + 1 < Walk.Length; w++)
            {
                if (!NavMesh.SamplePosition(root.TransformPoint(new Vector3(Walk[w].x, 1f, Walk[w].y)), out var s0, 4f, NavMesh.AllAreas) ||
                    !NavMesh.SamplePosition(root.TransformPoint(new Vector3(Walk[w + 1].x, 1f, Walk[w + 1].y)), out var s1, 4f, NavMesh.AllAreas) ||
                    !NavMesh.CalculatePath(s0.position, s1.position, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete)
                { Log($"{tag}: leg {w}: no NavMesh path"); continue; }
                float y = path.corners[0].y;
                for (int c = 0; c + 1 < path.corners.Length; c++)
                {
                    Vector3 a = path.corners[c], b = path.corners[c + 1];
                    float len = Vector3.Distance(new Vector3(a.x, 0, a.z), new Vector3(b.x, 0, b.z)); pathLen += len;
                    for (float t = 0; t < len; t += 0.2f)
                    {
                        Vector3 p = Vector3.Lerp(a, b, len > 0 ? t / len : 0);
                        steps++;
                        if (!Physics.Raycast(new Vector3(p.x, y + 0.6f, p.z), Vector3.down, out var g, 2.5f, ~0, QueryTriggerInteraction.Ignore)) { noGround++; continue; }
                        if (g.point.y - y > 0.45f) { blockedStep++; y = g.point.y; continue; }
                        y = g.point.y;
                        if (Physics.CheckCapsule(new Vector3(p.x, y + 0.3f + 0.46f, p.z), new Vector3(p.x, y + 1.8f - 0.3f, p.z), 0.28f, ~0, QueryTriggerInteraction.Ignore)) blockedWall++;
                        if (frames && steps % 25 == 0 && frame < 400)
                        {
                            Vector3 fwd = (b - a); fwd.y = 0; fwd = fwd.sqrMagnitude > 0.001f ? fwd.normalized : Vector3.forward;
                            capsule.transform.position = new Vector3(p.x, y + 0.9f, p.z);
                            cam.transform.position = capsule.transform.position - fwd * 5f + Vector3.up * 2.2f;
                            cam.transform.LookAt(capsule.transform.position + Vector3.up * 0.3f);
                            Save(cam, rt, Path.Combine(frameDir, $"f{frame++:000}.png"));
                        }
                    }
                }
            }
            Log($"{tag}: capsule walk {pathLen:0} m, {steps} steps: blocked by a wall {blockedWall}, step too high {blockedStep}, no ground {noGround}" + (frames ? $", {frame} frames in {frameDir}" : ""));
            if (capsule != null) Object.DestroyImmediate(capsule);

            // 4) render time, the same four views (GPU-synced: one pixel read back per frame)
            if (cam == null) { cam = new GameObject("TimeCam").AddComponent<Camera>(); }
            var timeRt = new RenderTexture(1920, 1080, 24); cam.targetTexture = timeRt; cam.fieldOfView = 60f; cam.farClipPlane = 3000f;
            var px = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            foreach (var v in Views)
            {
                cam.transform.position = root.TransformPoint(v.pos);
                cam.transform.LookAt(root.TransformPoint(v.look));
                for (int i = 0; i < 5; i++) RenderSync(cam, timeRt, px);
                var sw = Stopwatch.StartNew();
                for (int i = 0; i < 30; i++) RenderSync(cam, timeRt, px);
                Log($"{tag}: render {v.name}: {sw.Elapsed.TotalMilliseconds / 30:0.00} ms/frame (1920x1080, Editor batch)");
                if (frames || tag == "OLD") Save(cam, timeRt, Path.Combine(outDir, $"view_{tag.ToLower()}_{v.name}.png"));
            }
            Object.DestroyImmediate(cam.gameObject);
        }

        private static void MeasureResidentDrawer(string outDir)
        {
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urp == null) { Log("GRD: no URP asset"); return; }
            // set through reflection: the mode's type lives in the GPUDriven assembly, which the shared editor assembly does not reference
            var prop = urp.GetType().GetProperty("gpuResidentDrawerMode");
            if (prop == null || !prop.CanWrite) { Log("GRD: this URP has no settable gpuResidentDrawerMode"); return; }
            object before = prop.GetValue(urp);
            try
            {
                Log($"GRD: project setting is {before}; trying InstancedDrawing in memory only (not saved)");
                prop.SetValue(urp, Enum.Parse(prop.PropertyType, "InstancedDrawing"));
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                var root = GameObject.Find(RootName).transform;
                var cam = new GameObject("GrdCam").AddComponent<Camera>();
                var rt = new RenderTexture(1920, 1080, 24); cam.targetTexture = rt; cam.farClipPlane = 3000f;
                var px = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                foreach (var v in Views)
                {
                    cam.transform.position = root.TransformPoint(v.pos); cam.transform.LookAt(root.TransformPoint(v.look));
                    for (int i = 0; i < 5; i++) RenderSync(cam, rt, px);
                    var sw = Stopwatch.StartNew();
                    for (int i = 0; i < 30; i++) RenderSync(cam, rt, px);
                    Log($"GRD on: render {v.name}: {sw.Elapsed.TotalMilliseconds / 30:0.00} ms/frame");
                }
                Object.DestroyImmediate(cam.gameObject);
            }
            catch (Exception e) { Log("GRD: could not measure: " + e.Message); }
            finally { prop.SetValue(urp, before); }
        }

        private static void RenderSync(Camera cam, RenderTexture rt, Texture2D px)
        {
            cam.Render();
            var prev = RenderTexture.active; RenderTexture.active = rt;
            px.ReadPixels(new Rect(0, 0, 1, 1), 0, 0, false);
            RenderTexture.active = prev;
        }

        private static void Save(Camera cam, RenderTexture rt, string path)
        {
            cam.Render();
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            RenderTexture.active = prev;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }

        private static int Tris(Mesh m)
        {
            long n = 0;
            for (int i = 0; i < m.subMeshCount; i++) n += m.GetIndexCount(i);
            return (int)(n / 3);
        }

        private static bool IsChildOfName(this Transform t, string name)
        {
            for (var p = t; p != null; p = p.parent) if (p.name == name) return true;
            return false;
        }
    }
}
