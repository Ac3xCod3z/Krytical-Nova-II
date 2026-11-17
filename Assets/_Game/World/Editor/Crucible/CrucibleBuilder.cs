using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using KryticalNova.Crossfire;
using KryticalNova.Factions;
using Newtonsoft.Json.Linq;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

namespace KryticalNova.EditorTools.Crucible
{
    /// <summary>
    /// Builds The Crucible (Crossfire war map, levels 90-112) from Fable's checkpoint 022b into one self-contained prefab and puts it
    /// under the "Crucible" root far out in Zone01 (owner's stop-gap for build 19: same server, the port teleports; build 20 moves the
    /// same prefab to its own map server). Art carries no colliders; collision is Fable's proxies (1,059 boxes, 8 convex hulls,
    /// 16 ground meshes), 37 player blockers, 16 stair ramps in Concord's form (Concord040Setup.GiveStairRamps). Rules table:
    /// side-session notes CRUCIBLE-UNITY-RULES-TABLE.md. Reruns replace the prefab and the root; nothing else in Zone01 is touched.
    /// </summary>
    public static class CrucibleBuilder
    {
        public const string RootName = "Crucible";
        public const string Zone01Path = "Assets/_Game/Scenes/World/Towns/Zone01_StarterTown.unity";
        /// <summary>Where the map's heart sits in Zone01 (about 3 km out, past the town's far clip and fog).</summary>
        public static readonly Vector3 Zone01Offset = new Vector3(0f, 0f, 3000f);
        /// <summary>The player-only blocker layer; Default when the project has no such layer.</summary>
        public const string BlockerLayer = "Blocker";
        public const string ReportPath = "Library/KryticalNova.Crucible.txt";
        const float RampThickness = 0.6f;

        static readonly string[] ArtSkipCollections = { "Colliders_For_Unity", "Blockers_For_Unity" };

        [MenuItem("Krytical Nova/World/Crucible/Build Prefab")]
        public static void BuildPrefabMenu() => Debug.Log(BuildPrefab());

        [MenuItem("Krytical Nova/World/Crucible/Build Prefab + Place In Zone01")]
        public static void BuildAllMenu() => Debug.Log(BuildAll());

        /// <summary>Batch entry (-executeMethod): materials, prefab, Zone01 placement and NavMesh; exits 0 on success.</summary>
        public static void BuildAllBatch()
        {
            string report;
            try { report = BuildAll(); }
            catch (System.Exception e) { Debug.LogError("[Crucible] " + e); EditorApplication.Exit(1); return; }
            Debug.Log(report);
            EditorApplication.Exit(report.Contains("FAIL") ? 1 : 0);
        }

        public static string BuildAll()
        {
            string prefab = BuildPrefab();
            string own = MakeOwnScene();
            string place = PlaceInZone01();
            string text = prefab + "\n" + own + "\n" + place;
            File.WriteAllText(ReportPath, text);
            return text;
        }

        public static string BuildPrefab()
        {
            var log = new StringBuilder("[Crucible] build from checkpoint 022b\n");
            log.AppendLine(CrucibleTextureImport.EnsureFloorGroup());
            CrucibleData data = CrucibleData.Load();
            Dictionary<string, Material> mats = CrucibleMaterials.Build(data, log);
            Dictionary<string, Mesh> meshes = LoadMeshes(log);

            var root = new GameObject(RootName);
            try
            {
                Transform art = Child(root.transform, "Art");
                var placed = new Dictionary<string, GameObject>();
                ChooseCustomMeshFix(data, meshes, log);
                PlaceArt(data, art, mats, meshes, placed, log);
                PlaceColliders(data, Child(root.transform, "Colliders"), meshes, placed, log);
                PlaceBlockers(data, Child(root.transform, "Blockers"), log);
                PlaceStairRamps(data, Child(root.transform, "Stair Ramps"), log);
                PlaceSeamStrips(Child(root.transform.Find("Colliders"), "Seam Strips"), log);
                PlaceCrossfire(data, Child(root.transform, "Crossfire"), placed, log);
                WireGates(data, root.transform, placed, log);
                root.AddComponent<KryticalNova.World.CrucibleNight>();
                PlaceLights(data, Child(root.transform, "Lights"), log);
                WireHeat(data, root.transform, placed, log);
                AddNavMeshSurface(root);
                var probes = Child(root.transform, "Probe Volume (Crucible)");
                probes.localPosition = CrucibleLighting.VolumeCentre;
                var pv = probes.gameObject.AddComponent<UnityEngine.Rendering.ProbeVolume>();
                pv.mode = UnityEngine.Rendering.ProbeVolume.Mode.Local;
                pv.size = CrucibleLighting.VolumeSize;
                CheckSizes(data, placed, log);
                MeshLods(root, log);

                Directory.CreateDirectory(Path.GetDirectoryName(CrucibleData.PrefabPath));
                PrefabUtility.SaveAsPrefabAsset(root, CrucibleData.PrefabPath, out bool ok);
                log.AppendLine(ok ? $"Prefab saved: {CrucibleData.PrefabPath}" : "FAIL: prefab not saved");
            }
            finally { Object.DestroyImmediate(root); }
            return log.ToString();
        }

        // ---------------------------------------------------------------------------------------------------------------- art

        static Dictionary<string, Mesh> LoadMeshes(StringBuilder log)
        {
            var meshes = new Dictionary<string, Mesh>();
            foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(CrucibleData.MeshPath))
                if (o is Mesh m)
                {
                    meshes[m.name] = m;
                    string bare = StripSuffix(m.name);
                    if (!meshes.ContainsKey(bare)) meshes[bare] = m;
                }
            log.AppendLine($"Custom meshes in the FBX: {meshes.Values.Distinct().Count()}.");
            return meshes;
        }

        static string StripSuffix(string n)
        {
            int dot = n.LastIndexOf('.');
            return dot > 0 && n.Length - dot == 4 && char.IsDigit(n[n.Length - 1]) ? n.Substring(0, dot) : n;
        }

        static void PlaceArt(CrucibleData data, Transform art, Dictionary<string, Material> mats, Dictionary<string, Mesh> meshes,
            Dictionary<string, GameObject> placed, StringBuilder log)
        {
            var prefabCache = new Dictionary<string, GameObject>();
            var groups = new Dictionary<string, Transform>();
            int synty = 0, custom = 0, missing = 0, matMisses = 0;
            var missingNames = new List<string>();
            foreach (JObject o in data.Objects)
            {
                string kind = (string)o["kind"], name = (string)o["name"], coll = (string)o["collection"];
                if (kind != "synty" && kind != "custom" && kind != "layout") continue;
                if (ArtSkipCollections.Contains(coll) || name.StartsWith("UCX_") || name.StartsWith("Blocker_") || name.StartsWith("ScaleFigure")) continue;
                if (!groups.TryGetValue(coll, out Transform group)) groups[coll] = group = Child(art, coll);

                GameObject go;
                Vector3 pivot = Vector3.zero;
                if (kind == "synty")
                {
                    GameObject prefab = SyntyPrefab((string)o["synty_pack"], (string)o["synty_prefab"], prefabCache);
                    if (prefab == null) { missing++; missingNames.Add(name); continue; }
                    go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, group);
                    pivot = -CrucibleData.V(o["pivot"]);
                    foreach (Collider c in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
                    matMisses += SwapMaterials(go, o, mats, data);
                    synty++;
                }
                else
                {
                    if (!meshes.TryGetValue((string)o["mesh"], out Mesh mesh) && !meshes.TryGetValue(StripSuffix((string)o["mesh"]), out mesh))
                    { missing++; missingNames.Add(name); continue; }
                    go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
                    go.transform.SetParent(group, false);
                    go.GetComponent<MeshFilter>().sharedMesh = mesh;
                    var list = ((JArray)o["materials"]).Select(t => mats.TryGetValue((string)t, out Material m) ? m : null).ToArray();
                    if (list.Any(m => m == null)) matMisses++;
                    go.GetComponent<MeshRenderer>().sharedMaterials = list;
                    custom++;
                }
                go.name = name;
                if (!data.TryUnityMatrix(name, pivot, out Matrix4x4 u)) { log.AppendLine("No matrix for " + name); continue; }
                if (kind != "synty") u *= CrucibleData.CustomMeshFix;
                CrucibleData.Apply(go.transform, u);
                MakeStatic(go, coll == "SkyDome");
                placed[name] = go;
            }
            log.AppendLine($"Art: {synty} Synty placements, {custom} custom/layout pieces, {missing} missing, {matMisses} with a material not found.");
            if (missingNames.Count > 0) log.AppendLine("  missing: " + string.Join(", ", missingNames.Take(20)));
        }


        /// <summary>
        /// Which turn (and mirror) takes the FBX's imported vertices into the converted frame: every candidate is scored against each
        /// custom piece's Blender world box (centre and size); the best wins. Runs 4-5 proved the importer's frame is not the textbook
        /// one, so it is measured, not assumed. A mirrored winner cannot be placed with a plain transform and is reported as FAIL.
        /// </summary>
        static void ChooseCustomMeshFix(CrucibleData data, Dictionary<string, Mesh> meshes, StringBuilder log)
        {
            var candidates = new System.Collections.Generic.List<(string name, Matrix4x4 m)>();
            foreach (var r in new[] { (-90f, 0f), (90f, 0f), (-90f, 180f), (90f, 180f), (0f, 0f), (0f, 180f) })
            {
                Matrix4x4 rot = Matrix4x4.Rotate(Quaternion.Euler(r.Item1, r.Item2, 0f));
                candidates.Add(($"rot({r.Item1},{r.Item2},0)", rot));
                candidates.Add(($"rot({r.Item1},{r.Item2},0)+mirrorX", rot * Matrix4x4.Scale(new Vector3(-1f, 1f, 1f))));
            }
            double best = double.MaxValue; string bestName = null; Matrix4x4 bestM = Matrix4x4.identity;
            foreach (var c in candidates)
            {
                double err = 0; int n = 0;
                foreach (JObject o in data.Objects)
                {
                    string kind = (string)o["kind"], name = (string)o["name"];
                    if ((kind != "custom" && kind != "layout") || name.StartsWith("UCX_") || !data.Centres.TryGetValue(name, out float[] ce)) continue;
                    if (!meshes.TryGetValue((string)o["mesh"], out Mesh mesh) && !meshes.TryGetValue(StripSuffix((string)o["mesh"]), out mesh)) continue;
                    if (!data.TryUnityMatrix(name, Vector3.zero, out Matrix4x4 u)) continue;
                    Bounds wb = WorldBounds(u * c.m, mesh.bounds);
                    Vector3 wantC = CrucibleData.ToUnity(new Vector3(ce[0], ce[1], ce[2]));
                    Vector3 wantS = CrucibleData.SizeToUnity(CrucibleData.V(o["size_m"]));
                    err += (wb.center - wantC).magnitude + (wb.size - wantS).magnitude;
                    n++;
                }
                if (n > 0 && err < best) { best = err; bestName = c.name; bestM = c.m; }
            }
            CrucibleData.CustomMeshFix = bestM;
            bool mirrored = bestM.determinant < 0f;
            log.AppendLine($"Custom mesh frame: {bestName} (mean error {best / Mathf.Max(1, data.Objects.Count(x => (string)x["kind"] == "custom")):F3} m){(mirrored ? " FAIL: mirrored" : "")}.");
        }

        static Bounds WorldBounds(Matrix4x4 m, Bounds local)
        {
            var b = new Bounds(m.MultiplyPoint3x4(local.center), Vector3.zero);
            Vector3 e = local.extents;
            for (int i = 0; i < 8; i++)
                b.Encapsulate(m.MultiplyPoint3x4(local.center + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z)));
            return b;
        }

        static GameObject SyntyPrefab(string pack, string prefab, Dictionary<string, GameObject> cache)
        {
            string key = pack + "/" + prefab;
            if (cache.TryGetValue(key, out GameObject g)) return g;
            g = null;
            foreach (string guid in AssetDatabase.FindAssets(prefab + " t:Prefab", new[] { $"Assets/Synty/{pack}/Prefabs" }))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(p) == prefab) { g = AssetDatabase.LoadAssetAtPath<GameObject>(p); break; }
            }
            return cache[key] = g;
        }

        /// <summary>Puts Fable's material (variant, tint, relief) on each slot whose pack material it was made from.</summary>
        static int SwapMaterials(GameObject go, JObject o, Dictionary<string, Material> mats, CrucibleData data)
        {
            var wanted = ((JArray)o["materials"]).Select(t => (string)t).ToList();
            var bySource = new Dictionary<string, Material>();
            foreach (string w in wanted)
            {
                if (!mats.TryGetValue(w, out Material m)) continue;
                JToken entry = data.Materials.FirstOrDefault(x => (string)x["name"] == w);
                string src = (string)entry?["synty_material"];
                if (src != null && !bySource.ContainsKey(src)) bySource[src] = m;
            }
            int misses = 0;
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
            {
                Material[] slots = r.sharedMaterials;
                for (int i = 0; i < slots.Length; i++)
                {
                    if (slots[i] != null && bySource.TryGetValue(slots[i].name, out Material m)) slots[i] = m;
                    else if (wanted.Count == 1 && mats.TryGetValue(wanted[0], out Material only)) slots[i] = only;
                    else misses++;
                }
                r.sharedMaterials = slots;
            }
            return misses > 0 ? 1 : 0;
        }

        static void MakeStatic(GameObject go, bool sky)
        {
            // static batching stays off on Synty (Mesh LOD + SRP Batcher, LOD memory note); GI and occlusion on
            var flags = sky ? 0 : StaticEditorFlags.ContributeGI | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic |
                                 StaticEditorFlags.ReflectionProbeStatic;
            foreach (Transform t in go.GetComponentsInChildren<Transform>(true)) GameObjectUtility.SetStaticEditorFlags(t.gameObject, flags);
            if (sky)
                foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
                {
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    r.receiveShadows = false;
                    r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                }
        }

        // ---------------------------------------------------------------------------------------------------------- collision

        static void PlaceColliders(CrucibleData data, Transform holder, Dictionary<string, Mesh> meshes, Dictionary<string, GameObject> placed,
            StringBuilder log)
        {
            Transform boxes = Child(holder, "Boxes"), hulls = Child(holder, "Convex Hulls"), ground = Child(holder, "Ground");
            // The three Rally Gates' leaves: solid while shut, but left out of the NavMesh bake so the walk runs through the gateway
            // once it opens (MusterGate's carved obstacle blocks it until then). The sealed north leaf stays in the bake.
            Transform leaves = Child(holder, "Gate Leaves");
            leaves.gameObject.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
            int ground_ = LayerMask.NameToLayer("Ground");
            foreach (JObject b in data.Boxes)
            {
                string bn = (string)b["name"];
                var go = new GameObject(bn);
                go.transform.SetParent(bn.StartsWith("UCX_GateLeaf_") && !bn.Contains("Sealed") ? leaves : boxes, false);
                go.transform.localPosition = CrucibleData.V(b["centre_unity"]);
                go.transform.localRotation = Quaternion.Euler(CrucibleData.V(b["unity_euler"]));
                go.AddComponent<BoxCollider>().size = CrucibleData.V(b["size_m_unity"]);
            }
            // Fable's gate leaf colliders are unit boxes placed by matrix (Colliders_For_Unity), not in COLLIDERS_022.json's list
            foreach (JObject o in data.Objects)
            {
                string on = (string)o["name"];
                if (!on.StartsWith("UCX_GateLeaf_") || !data.TryUnityMatrix(on, Vector3.zero, out Matrix4x4 lu)) continue;
                var go = new GameObject(on);
                go.transform.SetParent(on.Contains("Sealed") ? boxes : leaves, false);
                CrucibleData.Apply(go.transform, lu);
                go.AddComponent<BoxCollider>().size = Vector3.one;
            }
            int hullCount = 0;
            foreach (JToken h in data.ConvexHulls)
            {
                string n = (string)h;
                if (!meshes.TryGetValue(n, out Mesh mesh)) { log.AppendLine("FAIL: no hull mesh " + n); continue; }
                var go = new GameObject(n);
                go.transform.SetParent(hulls, false);
                if (data.TryUnityMatrix(n, Vector3.zero, out Matrix4x4 u)) CrucibleData.Apply(go.transform, u * CrucibleData.CustomMeshFix);
                var mc = go.AddComponent<MeshCollider>();
                mc.sharedMesh = mesh;
                mc.convex = true;
                hullCount++;
            }
            int groundCount = 0;
            foreach (JToken g in data.GroundMeshes)
            {
                string n = (string)g;
                if (!placed.TryGetValue(n, out GameObject artGo)) { log.AppendLine("FAIL: no ground art " + n); continue; }
                var go = new GameObject(n + " (ground)");
                go.layer = ground_;
                go.transform.SetParent(ground, false);
                go.transform.localPosition = artGo.transform.localPosition;
                go.transform.localRotation = artGo.transform.localRotation;
                go.transform.localScale = artGo.transform.localScale;
                go.AddComponent<MeshCollider>().sharedMesh = artGo.GetComponent<MeshFilter>()?.sharedMesh;
                groundCount++;
            }
            foreach (Transform t in holder.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.NavigationStatic);
            log.AppendLine($"Colliders: {data.Boxes.Count} boxes, {hullCount} convex hulls, {groundCount} ground meshes (Ground layer).");
        }

        static void PlaceBlockers(CrucibleData data, Transform holder, StringBuilder log)
        {
            int layer = LayerMask.NameToLayer(BlockerLayer);
            if (layer < 0) layer = 0;
            holder.gameObject.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
            foreach (JObject b in data.Blockers)
            {
                var go = new GameObject((string)b["name"]) { layer = layer };
                go.transform.SetParent(holder, false);
                go.transform.localPosition = CrucibleData.V(b["centre_unity"]);
                go.transform.localRotation = Quaternion.Euler(CrucibleData.V(b["unity_euler"]));
                go.AddComponent<BoxCollider>().size = CrucibleData.V(b["size_m"]);
            }
            log.AppendLine($"Blockers: {data.Blockers.Count} on layer {LayerMask.LayerToName(layer)} (left out of the NavMesh bake).");
        }


        /// <summary>
        /// Fable's floor and rim quarters meet on the map's two axes, exactly where the grand stairs and gate thresholds run; a ray down
        /// that edge can slip between the meshes (walk check, run 11: Hegemony and north feet). A 0.4 m Ground strip 1 cm under each
        /// seam closes it (Concord's deck-floor way: the click still lands on the visible floor).
        /// </summary>
        static void PlaceSeamStrips(Transform holder, StringBuilder log)
        {
            int ground = LayerMask.NameToLayer("Ground");
            var spans = new[] { (from: 12.4f, to: 82.4f, top: -7.5f), (from: 103.6f, to: 118.2f, top: 0f) };
            int n = 0;
            foreach (var sp in spans)
                foreach (float angle in new[] { 0f, 90f, 180f, 270f })
                {
                    var go = new GameObject($"Seam {angle:0} r{sp.from:0}-{sp.to:0}") { layer = ground };
                    go.transform.SetParent(holder, false);
                    float mid = (sp.from + sp.to) * 0.5f;
                    go.transform.localRotation = Quaternion.Euler(0f, angle, 0f);
                    go.transform.localPosition = go.transform.localRotation * new Vector3(0f, sp.top - 0.01f - 0.25f, mid);
                    go.AddComponent<BoxCollider>().size = new Vector3(0.4f, 0.5f, sp.to - sp.from);
                    GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.NavigationStatic);
                    n++;
                }
            log.AppendLine($"Seam strips: {n} (Ground, under the quarter seams).");
        }

        /// <summary>Concord's ramps: one invisible Ground box per flight, top through the nosings, 0.6 m thick.</summary>
        static void PlaceStairRamps(CrucibleData data, Transform holder, StringBuilder log)
        {
            int ground = LayerMask.NameToLayer("Ground");
            foreach (JObject f in data.Flights)
            {
                JObject c = (JObject)f["concord_form"];
                Vector3 a, b; float width;
                if ((string)c["axis"] == "x")
                {
                    float z = ((float)c["z_min"] + (float)c["z_max"]) * 0.5f;
                    a = new Vector3((float)c["x_start"], (float)c["y_start"], z); b = new Vector3((float)c["x_end"], (float)c["y_end"], z);
                    width = (float)c["z_max"] - (float)c["z_min"];
                }
                else
                {
                    float x = ((float)c["x_min"] + (float)c["x_max"]) * 0.5f;
                    a = new Vector3(x, (float)c["y_start"], (float)c["z_start"]); b = new Vector3(x, (float)c["y_end"], (float)c["z_end"]);
                    width = (float)c["x_max"] - (float)c["x_min"];
                }
                var go = new GameObject("Ramp " + (string)f["name"]) { layer = ground };
                go.transform.SetParent(holder, false);
                go.transform.localPosition = (a + b) * 0.5f;
                go.transform.localRotation = Quaternion.LookRotation((b - a).normalized, Vector3.up);
                var box = go.AddComponent<BoxCollider>();
                box.size = new Vector3(width, RampThickness, (b - a).magnitude);
                box.center = new Vector3(0f, -RampThickness * 0.5f, 0f);
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.NavigationStatic);
            }
            log.AppendLine($"Stair ramps: {data.Flights.Count} (Ground layer, {RampThickness} m).");
        }

        // ----------------------------------------------------------------------------------------------------------- crossfire

        static void PlaceCrossfire(CrucibleData data, Transform holder, Dictionary<string, GameObject> placed, StringBuilder log)
        {
            var landings = new Dictionary<string, Transform>();
            foreach (JObject o in data.Objects.Where(x => (string)x["kind"] == "landing"))
            {
                var t = Marker(data, holder, (string)o["name"], out _);
                if (t != null) landings[(string)o["faction"]] = t;
            }
            int areas = 0, gates = 0;
            foreach (JObject o in data.Objects)
            {
                string kind = (string)o["kind"], faction = (string)o["faction"], name = (string)o["name"];
                if (kind == "CrossfireArea")
                {
                    Transform t = Marker(data, holder, name, out Vector3 halfExtent);
                    if (t == null) continue;
                    var area = t.gameObject.AddComponent<CrossfireArea>();
                    area.size = halfExtent * 2f;
                    if (faction == "Battle") area.kind = CrossfireArea.AreaKind.Field;
                    else
                    {
                        area.kind = CrossfireArea.AreaKind.MusterYard;
                        area.faction = Faction(faction);
                        landings.TryGetValue(faction, out area.landing);
                    }
                    areas++;
                }
                else if (kind == "MusterGate")
                {
                    Transform t = Marker(data, holder, name, out Vector3 halfExtent);
                    if (t == null) continue;
                    var obstacle = t.gameObject.AddComponent<NavMeshObstacle>();
                    obstacle.shape = NavMeshObstacleShape.Box;
                    obstacle.size = halfExtent * 2f;
                    obstacle.carving = true;
                    obstacle.carveOnlyStationary = false;
                    var gate = t.gameObject.AddComponent<MusterGate>();
                    gate.blocker = obstacle;
                    t.name = "Rally Gate " + faction;
                    gates++;
                }
                else if (kind == "respawn_shrine" || kind == "heart") Marker(data, holder, name, out _);
            }
            // The sealed north gate never opens: a carved obstacle that is always on, so nobody is walked in (Ronin stay hidden).
            if (placed.TryGetValue("Yard_Sealed_GateLeaf", out GameObject sealedLeaf) || TryFindByPrefix(placed, "GateLeaf_Sealed", out sealedLeaf))
            {
                var seal = new GameObject("Sealed North (never opens)").transform;
                seal.SetParent(holder, false);
                seal.localPosition = sealedLeaf.transform.localPosition + Vector3.up * 4f;
                var obstacle = seal.gameObject.AddComponent<NavMeshObstacle>();
                obstacle.shape = NavMeshObstacleShape.Box;
                obstacle.size = new Vector3(14f, 9f, 3f);
                obstacle.carving = true;
            }
            log.AppendLine($"Crossfire: {areas} areas (3 yards + field), {gates} Rally Gates (carved blockers), {landings.Count} landings.");
        }


        /// <summary>
        /// Each Rally Gate: its leaf colliders become the gate's shut look (off once open, on the server and every client alike) and a
        /// client presenter slides its art leaves by Fable's vectors (GATES_022.json). The sealed north gate has neither: it never opens.
        /// </summary>
        static void WireGates(CrucibleData data, Transform root, Dictionary<string, GameObject> placed, StringBuilder log)
        {
            Transform leafColliders = root.Find("Colliders/Gate Leaves");
            int wired = 0;
            foreach (string faction in new[] { "Covenant", "Hegemony", "Null" })
            {
                Transform gateT = root.Find("Crossfire/Rally Gate " + faction);
                var gate = gateT != null ? gateT.GetComponent<MusterGate>() : null;
                if (gate == null) { log.AppendLine("FAIL: no Rally Gate " + faction); continue; }
                var shut = new GameObject(faction + " leaves (shut)").transform;
                shut.SetParent(leafColliders, false);
                foreach (Transform box in leafColliders.Cast<Transform>().ToArray())
                    if (box.name.StartsWith("UCX_GateLeaf_" + faction)) box.SetParent(shut, false);
                gate.shutLook = shut.gameObject;

                var leaves = new List<Transform>();
                var offsets = new List<Vector3>();
                foreach (JObject g in data.Gates)
                {
                    if ((string)g["faction"] != faction || !placed.TryGetValue((string)g["name"], out GameObject leaf)) continue;
                    leaves.Add(leaf.transform);
                    offsets.Add(CrucibleData.V(g["open_pos_unity"]) - CrucibleData.V(g["closed_pos_unity"]));
                }
                var presenter = gateT.gameObject.AddComponent<RallyGatePresenter>();
                presenter.gate = gate;
                presenter.leaves = leaves.ToArray();
                presenter.openOffsets = offsets.ToArray();
                foreach (Transform l in leaves) GameObjectUtility.SetStaticEditorFlags(l.gameObject, 0);   // moving art is not static
                wired++;
                log.AppendLine($"  {faction} gate: {leaves.Count} leaf/leaves, {shut.childCount} leaf colliders.");
            }
            log.AppendLine($"Rally Gates wired: {wired} of 3.");
        }


        static readonly string[] LavaMaterials = { "M_Crucible_ArenaFloor", "M_Crucible_Dais", "M_Crucible_OuterGround_LavaCracked" };

        /// <summary>Lava breathing, fire flicker, crack haze, embers and smoke: one client presenter with Fable's points.</summary>
        static void WireHeat(CrucibleData data, Transform root, Dictionary<string, GameObject> placed, StringBuilder log)
        {
            var heat = root.gameObject.AddComponent<KryticalNova.World.CrucibleHeat>();
            var lava = new List<Renderer>();
            foreach (JObject o in data.Objects)
            {
                if (!placed.TryGetValue((string)o["name"], out GameObject go) || o["materials"] == null) continue;
                if (((JArray)o["materials"]).Any(m => LavaMaterials.Contains((string)m)))
                    lava.AddRange(go.GetComponentsInChildren<Renderer>());
            }
            heat.lavaRenderers = lava.ToArray();
            heat.flickerLights = root.Find("Lights").GetComponentsInChildren<Light>().Where(l => l.lightmapBakeType == LightmapBakeType.Realtime).ToArray();
            heat.hazeBoxes = data.HazeBoxes.Select(b =>
            {
                Vector3 c = CrucibleData.V(b["centre_unity"]), sz = CrucibleData.V(b["size_unity"]);
                return new Vector4(c.x, c.y - sz.y * 0.5f, c.z, Mathf.Max(sz.x, sz.z) * 0.5f);
            }).ToArray();
            heat.emberPoints = data.EmberPoints.Select(CrucibleData.V).ToArray();
            heat.smokePoints = data.SmokePoints.Select(CrucibleData.V).ToArray();
            heat.embers = MakeParticles(root, "Embers", true);
            heat.smoke = MakeParticles(root, "Smoke", false);
            log.AppendLine($"Heat: {lava.Count} lava renderers breathe, {heat.flickerLights.Length} lights flicker, {heat.hazeBoxes.Length} haze cells, {heat.emberPoints.Length} ember and {heat.smokePoints.Length} smoke points.");
        }

        static ParticleSystem MakeParticles(Transform root, string name, bool ember)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.playOnAwake = true;
            main.loop = true;
            main.maxParticles = ember ? 400 : 120;
            main.startLifetime = ember ? new ParticleSystem.MinMaxCurve(1.5f, 3.5f) : new ParticleSystem.MinMaxCurve(8f, 14f);
            main.startSpeed = ember ? new ParticleSystem.MinMaxCurve(0.6f, 2.2f) : new ParticleSystem.MinMaxCurve(0.4f, 1.0f);
            main.startSize = ember ? new ParticleSystem.MinMaxCurve(0.04f, 0.11f) : new ParticleSystem.MinMaxCurve(3f, 7f);
            main.startColor = ember ? new ParticleSystem.MinMaxGradient(new Color(4f, 1.1f, 0.25f), new Color(3f, 0.45f, 0.08f))
                                    : new ParticleSystem.MinMaxGradient(new Color(0.08f, 0.06f, 0.08f, 0.35f), new Color(0.14f, 0.09f, 0.1f, 0.25f));
            main.gravityModifier = ember ? -0.08f : -0.01f;
            var emission = ps.emission; emission.rateOverTime = 0f;
            var shape = ps.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = ember ? 12f : 25f; shape.radius = 0.1f;
            shape.rotation = new Vector3(-90f, 0f, 0f);
            var noise = ps.noise; noise.enabled = true; noise.strength = ember ? 0.6f : 0.25f; noise.frequency = 0.6f;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var size = ps.sizeOverLifetime; size.enabled = !ember; size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.6f));
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = ParticleMaterial(ember);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return ps;
        }

        static Material ParticleMaterial(bool ember)
        {
            string path = $"{CrucibleData.MaterialFolder}/M_Crucible_{(ember ? "Ember" : "Smoke")}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetFloat("_Surface", 1f);                          // transparent
            mat.SetFloat("_Blend", ember ? 2f : 0f);               // additive embers, alpha smoke
            mat.SetTexture("_BaseMap", AssetDatabase.GetBuiltinExtraResource<Texture2D>("Default-Particle.psd"));
            mat.SetColor("_BaseColor", Color.white);
            mat.renderQueue = 3000;
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)(ember ? UnityEngine.Rendering.BlendMode.One : UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha));
            mat.SetInt("_ZWrite", 0);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            if (ember) mat.EnableKeyword("_BLENDMODE_ADD");
            EditorUtility.SetDirty(mat);
            return mat;
        }


        /// <summary>
        /// Mesh LOD (the project's LOD rule, owner 17 Oct 2026: import settings, never static batching) on every model the map draws,
        /// Fable's heavy props among them (Brazier_03/05, the crates, Barrel_01, the crenel tops), at Zone01's maximum level.
        /// </summary>
        static void MeshLods(GameObject root, StringBuilder log)
        {
            int maxLods = EnvironmentLodBuilder.LoadSettings().maxMeshLods;
            var models = new HashSet<string>();
            foreach (MeshFilter f in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (f.sharedMesh == null) continue;
                string path = AssetDatabase.GetAssetPath(f.sharedMesh);
                if (AssetImporter.GetAtPath(path) is ModelImporter) models.Add(path);
            }
            int on = 0;
            foreach (string path in models)
            {
                var importer = (ModelImporter)AssetImporter.GetAtPath(path);
                if (importer.generateMeshLods && importer.maximumMeshLod == maxLods) continue;
                importer.generateMeshLods = true;
                importer.maximumMeshLod = maxLods;
                importer.SaveAndReimport();
                on++;
            }
            log.AppendLine($"Mesh LOD: {models.Count} models drawn, {on} switched on now (max level {maxLods}).");
        }

        static bool TryFindByPrefix(Dictionary<string, GameObject> placed, string part, out GameObject go)
        {
            go = placed.FirstOrDefault(kv => kv.Key.Contains(part)).Value;
            return go != null;
        }

        static FactionId Faction(string f) =>
            f == "Hegemony" ? FactionId.Hegemony : f == "Covenant" ? FactionId.Covenant : f == "Null" ? FactionId.Null : FactionId.None;

        /// <summary>An empty at a marker's place and turn; its scale (Fable's half extents) comes back separately and the empty keeps 1.</summary>
        static Transform Marker(CrucibleData data, Transform holder, string name, out Vector3 halfExtent)
        {
            halfExtent = Vector3.one;
            if (!data.TryUnityMatrix(name, Vector3.zero, out Matrix4x4 u)) return null;
            var t = new GameObject(name).transform;
            t.SetParent(holder, false);
            CrucibleData.Apply(t, u);
            halfExtent = t.localScale;
            t.localScale = Vector3.one;
            return t;
        }

        // -------------------------------------------------------------------------------------------------------------- lights

        static void PlaceLights(CrucibleData data, Transform holder, StringBuilder log)
        {
            int realtime = 0, baked = 0, mixed = 0;
            foreach (JObject fx in data.Lights)
            {
                JObject l = (JObject)fx["light"];
                string type = (string)l["type"], mode = (string)l["mode"];
                if (type == "Directional" || mode == "None" || type == null || type.StartsWith("None")) continue;   // the moon is Zone01's own light
                var go = new GameObject((string)fx["name"]);
                go.transform.SetParent(holder, false);
                go.transform.localPosition = CrucibleData.V(fx["unity_pos"]);
                var light = go.AddComponent<Light>();
                light.type = type.StartsWith("Spot") ? LightType.Spot : LightType.Point;
                light.color = CrucibleData.Colour(fx["colour_linear"]).gamma;   // Fable gives linear colours; Light.color is sRGB
                light.intensity = (float)l["intensity"];
                light.range = (float)l["range_m"];
                light.shadows = (string)l["shadows"] == "Soft" ? LightShadows.Soft : LightShadows.None;
                if (light.shadows != LightShadows.None) light.shadowCustomResolution = 256;
                if (light.type == LightType.Spot) { go.transform.localRotation = Quaternion.LookRotation(Vector3.up); light.spotAngle = 40f; }
                light.lightmapBakeType = mode == "Realtime" ? LightmapBakeType.Realtime : mode == "Mixed" ? LightmapBakeType.Mixed : LightmapBakeType.Baked;
                if (mode == "Realtime") realtime++; else if (mode == "Mixed") mixed++; else baked++;
            }
            log.AppendLine($"Lights: {realtime} realtime (flicker), {mixed} mixed (shrines, heart), {baked} baked.");
        }

        // ------------------------------------------------------------------------------------------------------- nav and checks

        static void AddNavMeshSurface(GameObject root)
        {
            var surface = root.AddComponent<NavMeshSurface>();
            // The playable square (perimeter +-118 m, yards to +-163 m) from the arena floor to above the gate tops; outside it no
            // NavMesh, so no walk or jump can land beyond the walls.
            surface.collectObjects = CollectObjects.Volume;
            surface.center = new Vector3(0f, 8f, 0f);
            surface.size = new Vector3(340f, 36f, 340f);
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.agentTypeID = 0;
            int blocker = LayerMask.NameToLayer(BlockerLayer);
            surface.layerMask = blocker >= 0 ? ~(1 << blocker) : ~0;
        }

        /// <summary>Every placed piece's world box against Blender's (axes swapped): catches a wrong axis, pivot or scale per pack.</summary>
        static void CheckSizes(CrucibleData data, Dictionary<string, GameObject> placed, StringBuilder log)
        {
            var badByPrefab = new Dictionary<string, int>();
            int checkedCount = 0, bad = 0;
            var examples = new List<string>();
            foreach (JObject o in data.Objects)
            {
                if (o["size_m"] == null || !placed.TryGetValue((string)o["name"], out GameObject go)) continue;
                var rs = go.GetComponentsInChildren<Renderer>().Where(r => !(r is ParticleSystemRenderer)).ToArray();
                if (rs.Length == 0) continue;
                // LOD groups: measure LOD0 only
                var lod = go.GetComponentInChildren<LODGroup>();
                if (lod != null && lod.GetLODs().Length > 0) rs = lod.GetLODs()[0].renderers.Where(r => r != null).ToArray();
                if (rs.Length == 0) continue;
                Bounds b = rs[0].bounds;
                foreach (Renderer r in rs) b.Encapsulate(r.bounds);
                Vector3 want = CrucibleData.SizeToUnity(CrucibleData.V(o["size_m"]));
                Vector3 d = b.size - want;
                if (data.Centres.TryGetValue((string)o["name"], out float[] ce))
                {
                    Vector3 off = b.center - CrucibleData.ToUnity(new Vector3(ce[0], ce[1], ce[2]));
                    if (off.magnitude > 0.05f * want.magnitude + 0.1f) d += off * 10f;   // a flipped or shifted piece fails like a wrong size
                }
                float tol = 0.05f * want.magnitude + 0.05f;
                checkedCount++;
                if (d.magnitude > tol)
                {
                    bad++;
                    string key = (string)o["synty_prefab"] ?? (string)o["piece"] ?? (string)o["mesh"];
                    badByPrefab[key] = badByPrefab.TryGetValue(key, out int n) ? n + 1 : 1;
                    if (examples.Count < 8) examples.Add($"{o["name"]}: unity {b.size:F2} vs blender {want:F2}");
                }
            }
            log.AppendLine($"Size check: {checkedCount} pieces, {bad} off by more than 5 % ({(bad == 0 ? "PASS" : "CHECK")}).");
            foreach (var kv in badByPrefab.OrderByDescending(k => k.Value).Take(15)) log.AppendLine($"  {kv.Key}: {kv.Value}");
            foreach (string e in examples) log.AppendLine("  e.g. " + e);
        }

        // ----------------------------------------------------------------------------------------------------------- Zone01


        public const string OwnScenePath = "Assets/_Game/Scenes/World/Crossfire/Crucible.unity";

        /// <summary>
        /// The Crucible's own scene for build 20's map server (Master's pick, 17 Nov 2026): the same prefab at the origin, nothing else.
        /// Not in the build list or Addressables yet; Zone01's copy is the one build 19 plays.
        /// </summary>
        public static string MakeOwnScene()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CrucibleData.PrefabPath);
            if (prefab == null) return "FAIL: no Crucible prefab for its own scene";
            Directory.CreateDirectory(Path.GetDirectoryName(OwnScenePath));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            PrefabUtility.InstantiatePrefab(prefab, scene);
            EditorSceneManager.SaveScene(scene, OwnScenePath);
            return $"Own scene saved: {OwnScenePath} (prefab at the origin; build 20).";
        }

        public static string PlaceInZone01()
        {
            var log = new StringBuilder();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CrucibleData.PrefabPath);
            if (prefab == null) return "FAIL: no Crucible prefab";
            var scene = EditorSceneManager.OpenScene(Zone01Path, OpenSceneMode.Single);
            foreach (GameObject r in scene.GetRootGameObjects())
                if (r.name == RootName) Object.DestroyImmediate(r);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            go.transform.SetPositionAndRotation(Zone01Offset, Quaternion.identity);
            Physics.SyncTransforms();

            var surface = go.GetComponent<NavMeshSurface>();
            surface.BuildNavMesh();
            if (surface.navMeshData == null) log.AppendLine("FAIL: the Crucible NavMesh bake produced nothing");
            else
            {
                AssetDatabase.DeleteAsset(CrucibleData.NavMeshPath);
                AssetDatabase.CreateAsset(surface.navMeshData, CrucibleData.NavMeshPath);
                PrefabUtility.RecordPrefabInstancePropertyModifications(surface);
                log.AppendLine($"NavMesh baked: {CrucibleData.NavMeshPath}.");
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            log.AppendLine($"Placed under '{RootName}' in Zone01 at {Zone01Offset}.");
            return log.ToString();
        }


        /// <summary>
        /// What the Wallstride bake needs from a placed Crucible: its region (the playable square, with headroom), one exclusion box
        /// per stair flight (the ramp widened 1.5 m each side for the side walls, 4 m tall), and the objects to hide while baking
        /// (blockers and stair ramps, so neither becomes a wall). False when the open scene has no Crucible.
        /// </summary>
        public static bool SurfaceGraphParts(out Bounds region, out Matrix4x4[] excluded, List<GameObject> hide)
        {
            region = default; excluded = null;
            GameObject root = GameObject.Find(RootName);
            if (root == null) return false;
            region = new Bounds(root.transform.TransformPoint(new Vector3(0f, 10f, 0f)), new Vector3(340f, 60f, 340f));
            var boxes = new List<Matrix4x4>();
            Transform ramps = root.transform.Find("Stair Ramps"), blockers = root.transform.Find("Blockers");
            if (ramps != null)
                foreach (BoxCollider b in ramps.GetComponentsInChildren<BoxCollider>(true))
                {
                    Transform t = b.transform;
                    Vector3 size = new Vector3(b.size.x + 3f, 4f, b.size.z + 1f);
                    Matrix4x4 world = Matrix4x4.TRS(t.TransformPoint(new Vector3(0f, 1.6f, 0f)), t.rotation, size);
                    boxes.Add(world.inverse);
                }
            if (ramps != null) hide.Add(ramps.gameObject);
            if (blockers != null) hide.Add(blockers.gameObject);
            excluded = boxes.ToArray();
            return true;
        }


        /// <summary>
        /// Proves the Crucible's spot in Zone01 is empty (Master's condition, 17 Nov 2026): nothing of Zone01 (terrain, renderer,
        /// collider, NavMesh, spawn or hostile zone) within the sky dome (500 m round the heart, any height). Sky Concord sits 300 m
        /// over the town, 3 km away. Writes Library/KryticalNova.CrucibleSpot.txt; batch: exits 1 when something is there.
        /// </summary>
        public static string SpotCheck()
        {
            var scene = EditorSceneManager.OpenScene(Zone01Path, OpenSceneMode.Single);
            var log = new StringBuilder($"[Crucible] spot check: Zone01 within 500 m of {Zone01Offset} (the Crucible root left out)\n");
            Vector2 c = new Vector2(Zone01Offset.x, Zone01Offset.z);
            bool Near(Bounds b)
            {
                float dx = Mathf.Max(0f, Mathf.Abs(c.x - b.center.x) - b.extents.x), dz = Mathf.Max(0f, Mathf.Abs(c.y - b.center.z) - b.extents.z);
                return dx * dx + dz * dz <= 500f * 500f;
            }
            int hits = 0;
            var farthest = new Bounds();
            bool any = false;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == RootName) continue;
                foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
                {
                    if (!any) { farthest = r.bounds; any = true; } else farthest.Encapsulate(r.bounds);
                    if (Near(r.bounds) && hits++ < 20) log.AppendLine($"  renderer {PathOf(r.transform)} at {r.bounds.center}");
                }
                foreach (Collider col in root.GetComponentsInChildren<Collider>(true))
                    if (Near(col.bounds) && hits++ < 20) log.AppendLine($"  collider {PathOf(col.transform)} at {col.bounds.center}");
                foreach (Terrain t in root.GetComponentsInChildren<Terrain>(true))
                {
                    var b = new Bounds(t.transform.position + t.terrainData.size * 0.5f, t.terrainData.size);
                    log.AppendLine($"  terrain {t.name}: {b.min} .. {b.max}");
                    if (Near(b)) { hits++; log.AppendLine("    INSIDE the dome"); }
                }
                foreach (NavMeshSurface s in root.GetComponentsInChildren<NavMeshSurface>(true))
                    if (s.navMeshData != null && Near(s.navMeshData.sourceBounds) && hits++ < 20) log.AppendLine($"  NavMesh {PathOf(s.transform)}");
                foreach (MonoBehaviour mb in root.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (mb == null) continue;
                    string tn = mb.GetType().Name;
                    if ((tn.Contains("Spawn") || tn.Contains("HostileZone") || tn.Contains("Portal")) && Near(new Bounds(mb.transform.position, Vector3.one)) && hits++ < 20)
                        log.AppendLine($"  {tn} {PathOf(mb.transform)} at {mb.transform.position}");
                }
            }
            log.AppendLine($"  everything else in Zone01 spans {farthest.min} .. {farthest.max}");
            log.AppendLine(hits == 0 ? "Spot check: PASS (nothing within 500 m)" : $"Spot check: FAIL ({hits} things within 500 m)");
            File.WriteAllText("Library/KryticalNova.CrucibleSpot.txt", log.ToString());
            return log.ToString();
        }

        public static void SpotCheckBatch() { string r = SpotCheck(); Debug.Log(r); EditorApplication.Exit(r.Contains("FAIL") ? 1 : 0); }

        static string PathOf(Transform t) => t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name;

        static Transform Child(Transform parent, string name)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            return t;
        }
    }
}
