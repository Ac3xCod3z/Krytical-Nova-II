using System.Collections.Generic;
using System.Linq;
using KryticalNova.NPC;
using KryticalNova.World;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KryticalNova.EditorTools.World
{
    /// <summary>
    /// Concord 040 preview (owner, 9 Nov 2026): the ChatGPT-built Concord environment floats above Zone01's Concord,
    /// so it shares Zone01's sun, sky and weather. Moves the imported package into World/Zones/Concord040, places the
    /// prefab high above the town, wires the Accord Hall's automatic glass doors, stands the vendors at their district
    /// spots, bakes a NavMesh for it, and gives the Marshal the temporary SKY LIFT up and down. Safe to run again:
    /// the old preview is replaced. Zone01 itself is not changed otherwise.
    /// </summary>
    public static class Concord040Setup
    {
        private const string ImportedFolder = "Assets/Concord";
        private const string Folder = "Assets/_Game/World/Zones/Concord040";
        private const string PrefabPath = Folder + "/Prefabs/Concord_040.prefab";
        private const string NavMeshPath = Folder + "/Concord040_NavMesh.asset";
        private const string ScenePath = "Assets/_Game/Scenes/World/Towns/Zone01_StarterTown.unity";
        private const string NpcPrefabFolder = "Assets/_Game/Characters/NPC/Prefabs";
        private const string MarshalDefinition = "Assets/_Game/Gameplay/NPC/Data/Assets/NPC_GuardCaptain.asset";
        private const string RootName = "Concord 040 (sky preview)";
        private const float HeightAboveTown = 300f;

        // Where each vendor stands, in Concord 040's own frame (metres; +Z north, from the Blender master's district
        // aprons and service booths). Yaw null = face the middle of the platform.
        private static readonly (string npc, float x, float z, float? yaw)[] Vendors =
        {
            ("GuildDirector", -57f, 52.2f, 180f), // Vaultkeeper, northwest: outside the vault doors (they stay shut)
            ("Trader", -64f, 24.5f, 90f),          // Broker, west
            ("HerbMaster", -58.6f, -60.3f, 180f),  // Apothecary: beside the east end of his counter, in front of the blocked doorway, facing the street (owner 9 Nov)
            ("Fabricator", 57.05f, -23.76f, 270f),      // east shops: 0.8 m behind the counter by the shop sign (Blender "exterior workstation"), facing the street (west)
            ("Agent", 57.65f, -2.76f, 270f),             // Fixer
            ("Blacksmith", 57.05f, 18.24f, 270f),       // Armorer
            ("Collector", 57.85f, 52.9f, 270f),        // Herald, northeast
            ("Holoweaver", 57.05f, -60.37f, 270f),      // southeast
            ("MallManager", 57.85f, -48.87f, 270f),     // Store Clerk
            ("GateMaster", 17f, -66.5f, 180f),    // Wayfinder booth: behind the desk (the booth faces south), facing its front
            ("GuardCaptain", 25.3f, -67.4f, 180f),      // Marshal (also the sky lift): behind his desk, which MoveMarshalBooth pushed 3 m north, clear of the tree planter
        };

        [MenuItem("Krytical Nova/World/Concord 040/Place Above Zone01")]
        public static void Run()
        {
            if (AssetDatabase.IsValidFolder(ImportedFolder) && !AssetDatabase.IsValidFolder(Folder))
            {
                string error = AssetDatabase.MoveAsset(ImportedFolder, Folder);
                if (!string.IsNullOrEmpty(error)) { Debug.LogError("[Concord040] Move failed: " + error); return; }
            }
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null) { Debug.LogError("[Concord040] No prefab at " + PrefabPath + " (import Concord_040_Unity6_URP.unitypackage first)."); return; }

            GiveRealColliders();
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var old = GameObject.Find(RootName);
            if (old != null) Object.DestroyImmediate(old);
            var oldReturn = GameObject.Find("Concord040_Return");
            if (oldReturn != null) Object.DestroyImmediate(oldReturn);

            // Above the town: over the middle of Zone01's vendors, 300 m up.
            var townNpcs = Object.FindObjectsByType<NpcInteractable>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (townNpcs.Length == 0) { Debug.LogError("[Concord040] No town NPCs in Zone01."); return; }
            Vector3 town = Vector3.zero;
            foreach (var n in townNpcs) town += n.transform.position;
            town /= townNpcs.Length;

            var root = new GameObject(RootName).transform;
            root.position = new Vector3(town.x, town.y + HeightAboveTown, town.z);
            var concord = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root);
            concord.transform.localPosition = Vector3.zero;
            concord.transform.localRotation = Quaternion.identity;
            Physics.SyncTransforms();
            GiveStairRamps(root);
            GiveDeckFloors(root);
            BlockDoorways(root);

            // The Accord Hall's glass doors slide open as a player walks up.
            Transform glass = Find(concord.transform, "Accord Hall Sliding Glass - unanimated");
            Transform left = glass != null ? Find(glass, "DoorLeft") : null, right = glass != null ? Find(glass, "DoorRight") : null;
            if (left != null && right != null)
            {
                glass.gameObject.AddComponent<ProximitySlidingDoors>().Setup(left, right);
                Debug.Log($"[Concord040] Hall doors wired: left {Local(root, left.position)}, right {Local(root, right.position)}.");
            }
            else Debug.LogWarning("[Concord040] Accord Hall door leaves not found.");

            // Vendors at their district spots (copies of the town's NPCs; the town keeps its own).
            var vendors = new GameObject("Vendors (preview)").transform;
            vendors.SetParent(root, false);
            int placed = 0;
            foreach (var v in Vendors)
            {
                var npcPrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{NpcPrefabFolder}/NPC_{v.npc}.prefab");
                if (npcPrefab == null) { Debug.LogWarning("[Concord040] No NPC prefab NPC_" + v.npc); continue; }
                var go = (GameObject)PrefabUtility.InstantiatePrefab(npcPrefab, vendors);
                float yaw = v.yaw ?? Mathf.Atan2(-v.x, -v.z) * Mathf.Rad2Deg;
                go.transform.position = Ground(root, v.x, v.z);
                go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                placed++;
            }

            // Sky lift markers: arrival up by the shared portal, return down beside the town's Marshal.
            var arrival = new GameObject("Concord040_Arrival").transform;
            arrival.SetParent(root, false);
            arrival.position = Ground(root, 0f, -56f);
            var townMarshal = townNpcs.FirstOrDefault(n => n.Definition != null && n.Definition.name == "NPC_GuardCaptain");
            Vector3 back = townMarshal != null ? townMarshal.transform.position + townMarshal.transform.forward * 2.5f : town;
            new GameObject("Concord040_Return").transform.position = back;

            // A NavMesh of its own (Zone01's surface is left as it is).
            var surface = root.gameObject.AddComponent<NavMeshSurface>();
            // Only the platform itself (deck up to the top tier): the hull and its inside below the deck stay out, so nobody is ever put under the floor.
            surface.collectObjects = CollectObjects.Volume;
            surface.center = new Vector3(0f, 12f, 0f);
            surface.size = new Vector3(220f, 25f, 220f);
            surface.useGeometry = UnityEngine.AI.NavMeshCollectGeometry.PhysicsColliders;
            surface.agentTypeID = 0;
            surface.BuildNavMesh();
            if (surface.navMeshData != null)
            {
                AssetDatabase.DeleteAsset(NavMeshPath);
                AssetDatabase.CreateAsset(surface.navMeshData, NavMeshPath);
            }
            else Debug.LogWarning("[Concord040] NavMesh bake produced nothing.");

            // The Marshal offers the sky lift (temporary; the real Marshal service replaces it).
            var marshal = AssetDatabase.LoadAssetAtPath<NpcDefinition>(MarshalDefinition);
            if (marshal != null && (marshal.offers & NpcOffer.SkyLift) == 0)
            {
                marshal.offers |= NpcOffer.SkyLift;
                EditorUtility.SetDirty(marshal);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Concord040] Placed at {root.position} (town centre {town}); {placed} vendors; arrival {arrival.position}; return {back}; " +
                      $"NavMesh {(surface.navMeshData != null ? "baked" : "MISSING")}.");
        }

        /// <summary>Batch entry: Unity -batchmode -executeMethod KryticalNova.EditorTools.World.Concord040Setup.Batch</summary>
        public static void Batch()
        {
            Run();
            EditorApplication.Exit(0);
        }


        // Stair flights (Concord frame, from the Blender master: "024 stair supported tread", "North/South second-floor access tread",
        // "038 broad civic stair"): x min, x max, z start, y start, z end, y end. The ramp's top face runs through the step nosings, so a
        // click on a flight lands on the ramp (Ground layer: a walk, never "low enough to vault or walk") and the NavMesh path goes over it.
        private static readonly (string name, float x0, float x1, float zA, float yA, float zB, float yB)[] Flights =
        {
            ("Stair 024 south", 64.5f, 68f, -38.4f, 7.27f, -26.39f, 12.72f),
            ("Stair 024 north", 64.5f, 68f, 29.6f, 7.27f, 41.61f, 12.72f),
            ("Access south", 60.4f, 63.6f, -76.4f, 0.18f, -62.35f, 7.2f),
            ("Access north", 60.4f, 63.6f, 84.4f, 0.18f, 70.36f, 7.2f),
            ("Civic stair", -13f, 13f, 33.35f, 0.2f, 36.05f, 1.1f),
        };

        private static void GiveStairRamps(Transform root)
        {
            var holder = new GameObject("Stair ramps").transform;
            holder.SetParent(root, false);
            int ground = LayerMask.NameToLayer("Ground");
            foreach (var f in Flights)
            {
                var go = new GameObject("Ramp " + f.name);
                go.layer = ground;
                go.transform.SetParent(holder, false);
                Vector3 a = new Vector3((f.x0 + f.x1) * 0.5f, f.yA, f.zA), b = new Vector3(a.x, f.yB, f.zB);
                Vector3 dir = (b - a).normalized;
                go.transform.localPosition = (a + b) * 0.5f;
                go.transform.localRotation = Quaternion.LookRotation(dir, Vector3.up);
                var box = go.AddComponent<BoxCollider>();
                box.size = new Vector3(f.x1 - f.x0, 0.6f, (b - a).magnitude);
                box.center = new Vector3(0f, -0.3f, 0f);
            }
            Physics.SyncTransforms();
        }


        // The camera stop (ClampPitchAboveGround) looks for the Ground layer under the camera. The deck's colliders are all merged chunk
        // meshes on Default (floors, walls and props in the same mesh), so on Concord the stop found nothing and the camera sank into the
        // floor (owner, 10 Nov 2026). Invisible Ground boxes go under the walkable floors, as the ramps do: the visible top surface is
        // measured by raycast on a grid, only large flat areas are kept (so props, rails and wall tops are not), equal-height cells are merged
        // into rectangles, and each box's top lies DeckFloorSink below the visible floor, so a click still lands on the mesh, not the box.
        private const float DeckCell = 1f, DeckFloorSink = 0.02f, DeckFlatTolerance = 0.08f, DeckBoxThickness = 0.5f;

        [MenuItem("Krytical Nova/World/Concord 040/Give Deck Floors Only")]
        public static void DeckFloorsOnly()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var rootGo = GameObject.Find(RootName);
            if (rootGo == null) { Debug.LogError("[Concord040] No " + RootName + " in the scene: run Place Above Zone01 first."); return; }
            Physics.SyncTransforms();
            GiveDeckFloors(rootGo.transform);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        private static void GiveDeckFloors(Transform root)
        {
            var old = root.Find("Deck floors");
            if (old != null) Object.DestroyImmediate(old.gameObject);
            Physics.SyncTransforms();
            var renderers = root.GetComponentsInChildren<MeshRenderer>(true);
            if (renderers.Length == 0) return;
            Bounds b = renderers[0].bounds;
            foreach (var r in renderers) if (r.bounds.max.y > 0f) b.Encapsulate(r.bounds);
            int xMin = Mathf.FloorToInt(b.min.x / DeckCell), xMax = Mathf.CeilToInt(b.max.x / DeckCell);
            int zMin = Mathf.FloorToInt(b.min.z / DeckCell), zMax = Mathf.CeilToInt(b.max.z / DeckCell);
            int nx = xMax - xMin + 1, nz = zMax - zMin + 1;
            var height = new float[nx, nz];
            var up = new bool[nx, nz];
            int mask = 1 << LayerMask.NameToLayer("Default");
            float startY = b.max.y + 5f, reach = b.size.y + 10f;
            var hits = new RaycastHit[32];
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < nz; j++)
                {
                    height[i, j] = float.NaN;
                    var from = new Vector3((xMin + i + 0.5f) * DeckCell, startY, (zMin + j + 0.5f) * DeckCell);
                    int n = Physics.RaycastNonAlloc(from, Vector3.down, hits, reach, mask, QueryTriggerInteraction.Ignore);
                    float best = float.NegativeInfinity; Vector3 normal = Vector3.up;
                    for (int k = 0; k < n; k++)
                        if (hits[k].collider.transform.IsChildOf(root) && hits[k].point.y > best) { best = hits[k].point.y; normal = hits[k].normal; }
                    if (float.IsNegativeInfinity(best) || normal.y < 0.9f) continue;
                    // Only where the NavMesh says a player can stand: roofs and ledges nobody can reach get no floor box.
                    if (!UnityEngine.AI.NavMesh.SamplePosition(new Vector3(from.x, best + 0.05f, from.z), out _, 0.5f, UnityEngine.AI.NavMesh.AllAreas)) continue;
                    height[i, j] = best; up[i, j] = true;
                }
            // Keep a cell only if most of its neighbours are about as high: a floor or a roof, not a prop, a rail or a wall top.
            var keep = new bool[nx, nz];
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < nz; j++)
                {
                    if (!up[i, j]) continue;
                    int same = 0;
                    for (int di = -1; di <= 1; di++)
                        for (int dj = -1; dj <= 1; dj++)
                        {
                            if (di == 0 && dj == 0) continue;
                            int a = i + di, c = j + dj;
                            if (a >= 0 && c >= 0 && a < nx && c < nz && up[a, c] && Mathf.Abs(height[a, c] - height[i, j]) <= DeckFlatTolerance) same++;
                        }
                    keep[i, j] = same >= 6;
                }
            var holder = new GameObject("Deck floors").transform;
            holder.SetParent(root, false);
            int ground = LayerMask.NameToLayer("Ground");
            var used = new bool[nx, nz];
            int boxes = 0; double area = 0;
            for (int j = 0; j < nz; j++)
                for (int i = 0; i < nx; i++)
                {
                    if (!keep[i, j] || used[i, j]) continue;
                    float h = height[i, j];
                    int i2 = i;
                    while (i2 + 1 < nx && keep[i2 + 1, j] && !used[i2 + 1, j] && Mathf.Abs(height[i2 + 1, j] - h) <= DeckFlatTolerance) i2++;
                    int j2 = j;
                    bool grow = true;
                    while (grow && j2 + 1 < nz)
                    {
                        for (int a = i; a <= i2; a++)
                            if (!keep[a, j2 + 1] || used[a, j2 + 1] || Mathf.Abs(height[a, j2 + 1] - h) > DeckFlatTolerance) { grow = false; break; }
                        if (grow) j2++;
                    }
                    float top = 1e9f;
                    for (int a = i; a <= i2; a++) for (int c = j; c <= j2; c++) { used[a, c] = true; top = Mathf.Min(top, height[a, c]); }
                    top -= DeckFloorSink;
                    float x0 = (xMin + i) * DeckCell, x1 = (xMin + i2 + 1) * DeckCell, z0 = (zMin + j) * DeckCell, z1 = (zMin + j2 + 1) * DeckCell;
                    var go = new GameObject("Floor " + boxes.ToString("000"));
                    go.layer = ground;
                    go.transform.SetParent(holder, false);
                    go.transform.position = new Vector3((x0 + x1) * 0.5f, top - DeckBoxThickness * 0.5f, (z0 + z1) * 0.5f);
                    go.AddComponent<BoxCollider>().size = new Vector3(x1 - x0, DeckBoxThickness, z1 - z0);
                    boxes++; area += (x1 - x0) * (z1 - z0);
                }
            Physics.SyncTransforms();
            Debug.Log($"[Concord040] Deck floors: {boxes} Ground boxes, {area:0} m2, from a {nx}x{nz} grid.");
        }

        private static Vector3 Ground(Transform root, float x, float z)
        {
            // From just under a shop roof, so a spot inside a shop finds its floor and not the roof above it.
            Vector3 top = root.TransformPoint(new Vector3(x, 2.2f, z));
            float best = float.NegativeInfinity;
            foreach (var hit in Physics.RaycastAll(top, Vector3.down, 6f, ~0, QueryTriggerInteraction.Ignore))
                if (hit.collider.transform.IsChildOf(root) && hit.point.y > best) best = hit.point.y;
            Vector3 p = root.TransformPoint(new Vector3(x, 0.2f, z));
            if (!float.IsNegativeInfinity(best)) p.y = best;
            return p;
        }

        /// <summary>
        /// The package's colliders cover only a few floors and walls (and one of them sat under the visible tiles). Every opaque piece gets
        /// a MeshCollider of its own visible mesh instead, so the floor you stand on is the floor you see, stairs climb, and planters, benches,
        /// walls and props block. Pieces wholly below the deck (the hull) and glass get none. Rerun-safe.
        /// </summary>
        private static void GiveRealColliders()
        {
            ShortenBooths();
            MoveMarshalBooth();
            ShrinkLamps();
            var contents = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                Concord040Thrusters.Apply(contents);
                foreach (var c in contents.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
                int added = 0;
                foreach (var mr in contents.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (mr.transform.parent != null && mr.transform.parent.name.StartsWith("Architectural Glass")) continue;
                    if (mr.GetComponentInParent<ProximitySlidingDoors>(true) != null || mr.transform.parent != null && mr.transform.parent.name.StartsWith("Accord Hall Sliding Glass")) continue;
                    var mf = mr.GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null || mr.bounds.max.y < 0f) continue;
                    var mc = mr.gameObject.AddComponent<MeshCollider>();
                    mc.sharedMesh = mf.sharedMesh;
                    mc.cookingOptions = MeshColliderCookingOptions.CookForFasterSimulation | MeshColliderCookingOptions.UseFastMidphase;
                    added++;
                }
                PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath);
                Debug.Log($"[Concord040] {added} mesh colliders on the visible geometry.");
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
        }

        // The service booths (Wayfinder, Marshal) are baked into the package's chunk meshes: their desk stands 1.2 m above the deck and
        // the sign board 2.75 m, taller than the people. Every vertex of the booth's box is pressed down to 0.8 of its height above the
        // deck (desk about 1.0 m). Done once: it skips a booth already shorter than 2.5 m.

        // Doorways players must not walk into (owner, 9 Nov 2026): the Apothecary's door. Invisible blockers the NavMesh bake carves around.
        private static readonly (string name, Vector3 centre, Vector3 size)[] DoorBlocks =
        {
            ("Apothecary door", new Vector3(-57.7f, 1.6f, -59.35f), new Vector3(2.0f, 3.2f, 0.7f)),
            // its inside was still reached through a gap on the west side: the whole building (Blender "APOTHECARY owned Synty building"
            // x -65.5..-56.5, z -59.18..-48.42) is filled up to 5 m, under its 5.6 m roof, so no NavMesh is left inside
            ("Apothecary building", new Vector3(-61f, 2.5f, -53.8f), new Vector3(9f, 5f, 10.76f)),
        };

        private static void BlockDoorways(Transform root)
        {
            var holder = new GameObject("Door blockers").transform;
            holder.SetParent(root, false);
            foreach (var d in DoorBlocks)
            {
                var go = new GameObject("Block " + d.name);
                go.transform.SetParent(holder, false);
                go.transform.localPosition = d.centre;
                go.AddComponent<BoxCollider>().size = d.size;
            }
            Physics.SyncTransforms();
        }

        // The Marshal's booth sat right against the tree planter (owner, 9 Nov 2026: move him and his booth back, the planter stays). Every
        // booth vertex (not the deck's) is moved 3 m north. Done once: it skips when nothing is left at the old spot.
        private static void MoveMarshalBooth()
        {
            var box = new Bounds(new Vector3(26f, 2f, -71.99f), new Vector3(3.3f, 4f, 1.78f));
            var offset = new Vector3(0f, 0f, 3f);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var filters = new List<MeshFilter>();
            foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>(true))
                if (mf.sharedMesh != null) filters.Add(mf);
            bool InBooth(Vector3 w) => box.Contains(w) && w.y > 0.16f && !(w.y > 0.19f && w.y < 0.21f);
            int left = 0;
            foreach (var mf in filters)
                foreach (var v in mf.sharedMesh.vertices)
                    if (InBooth(mf.transform.TransformPoint(v)) && mf.transform.TransformPoint(v).y > 0.5f) left++;
            if (left == 0) return;
            int moved = 0;
            foreach (var mf in filters)
            {
                Transform t = mf.transform;
                Mesh m = mf.sharedMesh;
                var vs = m.vertices;
                bool changed = false;
                for (int i = 0; i < vs.Length; i++)
                {
                    Vector3 w = t.TransformPoint(vs[i]);
                    if (!InBooth(w)) continue;
                    vs[i] = t.InverseTransformPoint(w + offset);
                    changed = true;
                    moved++;
                }
                if (!changed) continue;
                m.vertices = vs;
                m.RecalculateBounds();
                EditorUtility.SetDirty(m);
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[Concord040] Marshal booth moved 3 m north ({moved} vertices).");
        }


        // The deck lamps (owner, 10 Nov 2026: much smaller). Every lamp is a post and a head baked into the chunk meshes, so each one is
        // scaled about its own foot, vertex by vertex. Done once: it skips a lamp whose top is already lower; the meshes it touches are copied
        // to Documentation/LampBackup~ first (restore by copying them back over Meshes/).
        public static readonly Vector2[] LampAxes =
        {
            new Vector2(6.405f, -61f), new Vector2(-6.405f, -61f), new Vector2(6.405f, -69f), new Vector2(-6.405f, -69f),   // the gate row (cyan)
            new Vector2(-13.7f, -7.3f), new Vector2(-8.3f, -7.3f), new Vector2(-13.7f, 7.3f), new Vector2(-8.3f, 7.3f),     // the plaza rings
            new Vector2(8.3f, -7.3f), new Vector2(13.7f, -7.3f), new Vector2(8.3f, 7.3f), new Vector2(13.7f, 7.3f),
            new Vector2(-8f, -4.1f), new Vector2(-5.1f, -7f), new Vector2(-8f, 4.1f), new Vector2(-5.1f, 7f),
            new Vector2(8f, -4.1f), new Vector2(5.1f, -7f), new Vector2(8f, 4.1f), new Vector2(5.1f, 7f),
        };
        public const float LampShrink = 0.5f, LampRadius = 0.5f, LampFoot = 0.2f, LampTop = 3.2f;

        private static void ShrinkLamps()
        {
            if (System.IO.File.Exists(Folder + "/Documentation/LampBackup~/.done")) return;   // once only
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var filters = new List<MeshFilter>();
            foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>(true))
                if (mf.sharedMesh != null) filters.Add(mf);
            string backup = Folder + "/Documentation/LampBackup~";
            int lamps = 0, moved = 0;
            var touched = new HashSet<Mesh>();
            foreach (var axis in LampAxes)
            {
                // top of this lamp = highest vertex in its cylinder; skip when already shrunk (top below foot + shrink x height)
                float top = float.NegativeInfinity;
                foreach (var mf in filters)
                    foreach (var v in mf.sharedMesh.vertices)
                    {
                        Vector3 w = mf.transform.TransformPoint(v);
                        if (w.y > LampFoot + 0.05f && w.y < LampTop && (new Vector2(w.x, w.z) - axis).sqrMagnitude < LampRadius * LampRadius && w.y > top) top = w.y;
                    }
                if (top < 0.9f) continue;   // none there, or already shrunk
                lamps++;
                foreach (var mf in filters)
                {
                    Transform t = mf.transform;
                    Mesh m = mf.sharedMesh;
                    var vs = m.vertices;
                    bool changed = false;
                    for (int i = 0; i < vs.Length; i++)
                    {
                        Vector3 w = t.TransformPoint(vs[i]);
                        if (w.y <= LampFoot + 0.05f || w.y >= LampTop || (new Vector2(w.x, w.z) - axis).sqrMagnitude >= LampRadius * LampRadius) continue;
                        if (!touched.Contains(m))
                        {
                            string src = AssetDatabase.GetAssetPath(m);
                            if (!string.IsNullOrEmpty(src))
                            {
                                System.IO.Directory.CreateDirectory(backup);
                                string dst = backup + "/" + System.IO.Path.GetFileName(src);
                                if (!System.IO.File.Exists(dst)) System.IO.File.Copy(src, dst);
                            }
                            touched.Add(m);
                        }
                        w.x = axis.x + (w.x - axis.x) * LampShrink;
                        w.z = axis.y + (w.z - axis.y) * LampShrink;
                        w.y = LampFoot + (w.y - LampFoot) * LampShrink;
                        vs[i] = t.InverseTransformPoint(w);
                        changed = true;
                        moved++;
                    }
                    if (!changed) continue;
                    m.vertices = vs;
                    m.RecalculateBounds();
                    EditorUtility.SetDirty(m);
                }
            }
            if (lamps > 0) { System.IO.File.WriteAllText(backup + "/.done", "lamps shrunk once"); AssetDatabase.SaveAssets(); Debug.Log($"[Concord040] {lamps} deck lamps shrunk to {LampShrink:0.##} ({moved} vertices, {touched.Count} meshes backed up to {backup})."); }
        }

        private static void ShortenBooths()
        {
            const float deck = 0.2f, k = 0.8f;
            var boxes = new[] { new Bounds(new Vector3(17f, 2f, -68f), new Vector3(3.8f, 4f, 2.2f)), new Bounds(new Vector3(26f, 2f, -72.2f), new Vector3(3.3f, 4f, 2.4f)), new Bounds(new Vector3(24f, 2f, -58f), new Vector3(3.6f, 4f, 2.2f)) };
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var filters = new List<MeshFilter>();
            foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>(true))
                if (mf.sharedMesh != null) filters.Add(mf);
            foreach (var box in boxes)
            {
                float top = float.NegativeInfinity;
                foreach (var mf in filters)
                {
                    Transform t = mf.transform;
                    foreach (var v in mf.sharedMesh.vertices)
                    {
                        Vector3 w = t.TransformPoint(v);
                        if (w.y > 0.3f && w.y < 4f && box.Contains(w) && w.y > top) top = w.y;
                    }
                }
                if (top < 2.5f) continue;   // none there, or already shortened
                int moved = 0;
                foreach (var mf in filters)
                {
                    Transform t = mf.transform;
                    Mesh m = mf.sharedMesh;
                    var vs = m.vertices;
                    bool changed = false;
                    for (int i = 0; i < vs.Length; i++)
                    {
                        Vector3 w = t.TransformPoint(vs[i]);
                        if (w.y <= 0.3f || w.y >= 4f || !box.Contains(w)) continue;
                        w.y = deck + (w.y - deck) * k;
                        vs[i] = t.InverseTransformPoint(w);
                        changed = true;
                        moved++;
                    }
                    if (!changed) continue;
                    m.vertices = vs;
                    m.RecalculateBounds();
                    EditorUtility.SetDirty(m);
                }
                Debug.Log($"[Concord040] Booth at {box.center.x:0},{box.center.z:0} shortened ({moved} vertices, top was {top:0.00} m).");
            }
            AssetDatabase.SaveAssets();
        }

        private static Vector3 Local(Transform root, Vector3 world) => root.InverseTransformPoint(world);

        private static Transform Find(Transform parent, string name)
        {
            var stack = new Stack<Transform>();
            stack.Push(parent);
            while (stack.Count > 0)
            {
                var t = stack.Pop();
                if (t.name == name) return t;
                for (int i = 0; i < t.childCount; i++) stack.Push(t.GetChild(i));
            }
            return null;
        }
    }
}
