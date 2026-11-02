using System.Text;
using KryticalNova.Player;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KryticalNova.Tests
{
    /// <summary>
    /// A probe, not a rule (Explicit: run by name only). The wall walk's prop check did not see the air-conditioner box on Zone01's test
    /// wall (GUI run gui-wall-4, 1 Nov): this opens the zone and lists what is at that spot, and asks the check with and without the
    /// face's own collider left out.
    /// </summary>
    public sealed class Zone01WallPropProbe
    {
        const string Scene = "Assets/_Game/Scenes/World/Towns/Zone01_StarterTown.unity";
        // the node "along" ended at in gui-wall-2..4 (feet on the face), the face's normal +z; and the clean "onto" node
        static readonly Vector3 AcFeet = new Vector3(-13.2f, 4.35f, -45.3f), OntoFeet = new Vector3(-10.1f, 5.18f, -45.3f), Normal = Vector3.forward;

        [Test, Explicit]
        public void ListWhatIsAtTheAirConditioner()
        {
            EditorSceneManager.OpenScene(Scene, OpenSceneMode.Single);
            Physics.SyncTransforms();
            var report = new StringBuilder();
            foreach (var (name, feet) in new[] { ("A/C", AcFeet), ("onto", OntoFeet) })
            {
                report.AppendLine($"[probe] {name} feet {feet}");
                foreach (Collider c in Physics.OverlapSphere(feet + Normal * 0.6f, 0.8f, ~0, QueryTriggerInteraction.Collide))
                    report.AppendLine($"[probe]   {Path(c.transform)} {c.GetType().Name} layer {LayerMask.LayerToName(c.gameObject.layer)} trigger {c.isTrigger} " +
                        $"enabled {c.enabled} bounds {c.bounds.center} size {c.bounds.size}");
                Collider face = Physics.Raycast(feet + Normal * 0.4f, -Normal, out RaycastHit hit, 1f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) ? hit.collider : null;
                report.AppendLine($"[probe]   face along the normal: {(face != null ? Path(face.transform) + " at " + hit.point : "none")}");
                report.AppendLine($"[probe]   check, face left out: {WallWalkLayout.PropOnFace(feet, Normal, face, null, Physics.DefaultRaycastLayers)}; " +
                    $"face counted: {WallWalkLayout.PropOnFace(feet, Normal, null, null, Physics.DefaultRaycastLayers)}");
                foreach (Renderer r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                    if (r.bounds.Intersects(new Bounds(feet + Normal * 0.6f, Vector3.one * 1.2f)))
                        report.AppendLine($"[probe]   renderer {Path(r.transform)} bounds {r.bounds.center} size {r.bounds.size} collider on it: {r.GetComponent<Collider>() != null}");
            }
            Debug.Log(report.ToString());
            Assert.Pass(report.ToString());
        }

        // gui-climb-1's test face (the owner saw a tree), facing -z
        [Test, Explicit]
        public void ListWhatIsAtTheClimbFace()
        {
            EditorSceneManager.OpenScene(Scene, OpenSceneMode.Single);
            Physics.SyncTransforms();
            var report = new StringBuilder();
            Vector3 face = new Vector3(12.6f, 1.2f, -13.1f);
            foreach (Collider c in Physics.OverlapSphere(face, 1.5f, ~0, QueryTriggerInteraction.Collide))
                report.AppendLine($"[probe] collider {Path(c.transform)} {c.GetType().Name} layer {LayerMask.LayerToName(c.gameObject.layer)} " +
                    $"bounds {c.bounds.center} size {c.bounds.size} marker {(c.GetComponentInParent<ClimbableSurface>() is ClimbableSurface m ? m.kind.ToString() : "none")}");
            foreach (Renderer r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                if (r.bounds.size.magnitude < 40f && r.bounds.Intersects(new Bounds(face, Vector3.one * 3f)))
                    report.AppendLine($"[probe] renderer {Path(r.transform)} bounds {r.bounds.center} size {r.bounds.size}");
            foreach (Terrain t in Terrain.activeTerrains)
                report.AppendLine($"[probe] terrain {t.name}: {t.terrainData.treeInstanceCount} trees");
            Debug.Log(report.ToString());
            Assert.Pass(report.ToString());
        }

        // gui-pop-1 (1 Nov): "proxies live 0" under the entity netcode although the terrain has 1600 trees. Which trees TreeProxyField keeps
        // (a prototype with a capsule, at least 3 m tall) and how far the nearest are from the tree runs' start and the origin.
        [Test, Explicit]
        public void ListTheTerrainTreesTheProxiesWouldKeep()
        {
            EditorSceneManager.OpenScene(Scene, OpenSceneMode.Single);
            var report = new StringBuilder();
            var starts = new[] { ("tree runs' start", new Vector3(-6.8f, 0f, -7.5f)), ("origin", Vector3.zero) };
            foreach (Terrain t in Terrain.activeTerrains)
            {
                TerrainData data = t.terrainData;
                report.AppendLine($"[probe] terrain {t.name} scene {t.gameObject.scene.name} at {t.GetPosition()} size {data.size}: {data.treeInstanceCount} trees, " +
                    $"{data.treePrototypes.Length} prototypes; field on it in the saved scene: {t.GetComponent<TreeProxyField>() != null}");
                var ok = new bool[data.treePrototypes.Length];
                var height = new float[data.treePrototypes.Length];
                for (int i = 0; i < data.treePrototypes.Length; i++)
                {
                    GameObject prefab = data.treePrototypes[i].prefab;
                    CapsuleCollider capsule = prefab != null ? prefab.GetComponentInChildren<CapsuleCollider>(true) : null;
                    ok[i] = capsule != null;
                    height[i] = capsule != null ? capsule.height * capsule.transform.lossyScale.y : 0f;
                    report.AppendLine($"[probe]   prototype {i} {(prefab != null ? prefab.name : "none")}: capsule {(capsule != null ? $"height {height[i]:F1} m" : "NONE")}");
                }
                int kept = 0;
                var near = new System.Collections.Generic.List<(float d, Vector3 foot, string start)>();
                foreach (TreeInstance tree in data.treeInstances)
                {
                    int p = tree.prototypeIndex;
                    if (p < 0 || p >= ok.Length || !ok[p] || height[p] * tree.heightScale < 3f) continue;
                    kept++;
                    Vector3 foot = t.GetPosition() + Vector3.Scale(tree.position, data.size);
                    foreach (var (name, at) in starts)
                        near.Add((Vector2.Distance(new Vector2(foot.x, foot.z), new Vector2(at.x, at.z)), foot, name));
                }
                report.AppendLine($"[probe]   kept as climbable: {kept}");
                near.Sort((a, b) => a.d.CompareTo(b.d));
                foreach (var (name, _) in starts)
                {
                    int shown = 0;
                    foreach (var n in near)
                        if (n.start == name && shown++ < 3) report.AppendLine($"[probe]   nearest to the {name}: {n.d:F1} m, foot {n.foot}");
                }
            }
            Debug.Log(report.ToString());
            Assert.Pass(report.ToString());
        }

        // gui-catch-2 (1 Nov): NGO's Decide refused the 7 m terrain tree at 186.7,-82.1 ("There is no room at the top.") at every side. Suspect: the
        // TerrainCollider's own tree colliders are on in Zone01 (m_EnableTreeColliders 1), so the perch overlaps the terrain's copy of the tree
        // (BodyBlocked ignores only the proxy). The tree's proxy made as TreeProxyField makes it, then NGO's Decide asked with them on and off.
        [Test, Explicit]
        public void TheTerrainTree_IsRefused_WithTheTerrainsTreeColliders_AndClimbableWithout()
        {
            EditorSceneManager.OpenScene(Scene, OpenSceneMode.Single);
            var report = new StringBuilder();
            Terrain terrain = Terrain.activeTerrains[0];
            TerrainData data = terrain.terrainData;
            var collider = terrain.GetComponent<TerrainCollider>();
            // the setting has no script property in this Unity: the serialized field the scene file holds
            var setting = new UnityEditor.SerializedObject(collider);
            report.AppendLine($"[probe] terrain {terrain.name}: tree colliders in the saved scene {setting.FindProperty("m_EnableTreeColliders").boolValue}");
            // the climbable tree nearest the one gui-catch-2 tried
            Vector3 wanted = new Vector3(186.7f, 0f, -82.1f);
            float best = float.MaxValue, radius = 0f, height = 0f;
            Vector3 foot = default;
            foreach (TreeInstance tree in data.treeInstances)
            {
                GameObject prefab = data.treePrototypes[tree.prototypeIndex].prefab;
                CapsuleCollider capsule = prefab != null ? prefab.GetComponentInChildren<CapsuleCollider>(true) : null;
                if (capsule == null) continue;
                float h = capsule.height * capsule.transform.lossyScale.y * tree.heightScale;
                if (h < 3f) continue;
                Vector3 at = terrain.GetPosition() + Vector3.Scale(tree.position, data.size);
                float d = Vector2.Distance(new Vector2(at.x, at.z), new Vector2(wanted.x, wanted.z));
                if (d >= best) continue;
                best = d; foot = at; height = h;
                Vector3 s = capsule.transform.lossyScale;
                radius = capsule.radius * Mathf.Max(s.x, s.z) * tree.widthScale;
            }
            report.AppendLine($"[probe] the tree: foot {foot}, {height:F1} m tall, trunk radius {radius:F2} ({best:F1} m from the one tried)");
            // its proxy, as TreeProxyField.Place makes it
            var proxy = new GameObject("Tree Proxy (probe)");
            proxy.transform.position = foot;
            var body = proxy.AddComponent<CapsuleCollider>();
            body.direction = 1; body.radius = radius; body.height = height; body.center = new Vector3(0f, height * 0.5f, 0f);
            var marker = proxy.AddComponent<ClimbableSurface>();
            marker.kind = ClimbableKind.Tree; marker.trunkRadius = radius; marker.height = height; marker.perchHeight = height * 0.6f;
            // a player body to ask NGO's own Decide with (its motor's Awake runs as in the game)
            var playerPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Gameplay/Player/Prefabs/Player.prefab");
            var player = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(playerPrefab);
            try
            {
                var motor = player.GetComponent<PlayerWallRunMotor>();
                typeof(PlayerWallRunMotor).GetMethod("Awake", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.Invoke(motor, null);
                var ask = new PlayerWallRunMotor.GhostWallAsk { Open = true, Alive = true, Run = true, Flux = 999f, HasFluxForOne = true,
                    Up = Vector3.up, Forward = Vector3.forward, GroundSpeed = 28f };
                foreach (bool on in new[] { true, false })
                {
                    setting.FindProperty("m_EnableTreeColliders").boolValue = on;
                    setting.ApplyModifiedPropertiesWithoutUndo();   // in memory only: the scene is not saved
                    Physics.SyncTransforms();
                    string why = motor.FindTestTreeClimb(foot, ask, out Vector3 stand, out Vector3 point, out _);
                    report.AppendLine($"[probe] terrain tree colliders {(on ? "ON" : "OFF")}: {(why == null ? $"Decide ACCEPTS (face at {point}, start {stand})" : why)}");
                    Vector3 perch = marker.PerchFor(Vector3.forward, 0.6f, 0.5f);
                    foreach (Collider c in Physics.OverlapCapsule(perch + Vector3.up * 0.6f, perch + Vector3.up * 1.5f, 0.45f, ~0, QueryTriggerInteraction.Ignore))
                        report.AppendLine($"[probe]   at the perch {perch}: {Path(c.transform)} {c.GetType().Name}");
                }
            }
            finally
            {
                Object.DestroyImmediate(player);
                Object.DestroyImmediate(proxy);
            }
            Debug.Log(report.ToString());
            Assert.Pass(report.ToString());
        }

        // Owner rule (1 Nov): every test's places are planned before the run and proved here, never searched for near the start. Part 4's two:
        // the wall walk's test graph wall (in front of it: the point the scripted run teleports to) and a terrain tree out in the open. The run
        // (KnPlayHere.TestGraphWall / TestTerrainTree) uses exactly these.
        static readonly Vector3 TestGraphWall = new Vector3(-10.1f, 0f, -40f), TestTerrainTree = new Vector3(31.85f, 1.06f, -215.76f);

        [Test, Explicit]
        public void ThePlannedTestPlaces_StandInZone01()
        {
            EditorSceneManager.OpenScene(Scene, OpenSceneMode.Single);
            var report = new StringBuilder();
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            // the zone's rules (its surface graph) and a player body, set up as in the game
            var rules = Object.FindFirstObjectByType<KryticalNova.World.ZoneRules>();
            typeof(KryticalNova.World.ZoneRules).GetMethod("Awake", flags)?.Invoke(rules, null);
            var playerPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Gameplay/Player/Prefabs/Player.prefab");
            var player = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(playerPrefab);
            var proxy = new GameObject("Tree Proxy (probe)");
            try
            {
                var motor = player.GetComponent<PlayerWallRunMotor>();
                typeof(PlayerWallRunMotor).GetMethod("Awake", flags)?.Invoke(motor, null);
                Physics.SyncTransforms();
                string why = motor.FindTestWall(TestGraphWall, out PlayerWallRunMotor.TestWall wall);
                report.AppendLine(why == null
                    ? $"[probe] graph wall near {TestGraphWall}: FOUND; foot {wall.Foot}, front {wall.Front}, onto (4 m) {wall.Onto}, lower (2.5 m) {wall.Lower}, top {wall.Top}, normal {wall.Normal}"
                    : $"[probe] graph wall near {TestGraphWall}: NOT FOUND; {why}");
                var jump = player.GetComponent<PlayerJumpMotor>();
                float reach = jump != null ? jump.GhostJumpReach : 5f;
                report.AppendLine($"[probe] Jump reach {reach:F1} m (the prefab's own ranks); the run's three clicks: {Mathf.Min(2.5f, reach - 1f):F1}, {reach - 0.2f:F1}, {reach + 2f:F1} m up; the wall's top {(why == null ? (wall.Top.y - wall.Foot.y).ToString("F1") : "?")} m up");

                // the terrain tree: its proxy as TreeProxyField makes it, NGO's Decide asked through the aimed ray, the scene as saved
                Terrain terrain = Terrain.activeTerrains[0];
                var setting = new UnityEditor.SerializedObject(terrain.GetComponent<TerrainCollider>());
                TerrainData data = terrain.terrainData;
                float best = float.MaxValue, radius = 0f, height = 0f;
                Vector3 foot = default;
                foreach (TreeInstance tree in data.treeInstances)
                {
                    GameObject prefab = data.treePrototypes[tree.prototypeIndex].prefab;
                    CapsuleCollider capsule = prefab != null ? prefab.GetComponentInChildren<CapsuleCollider>(true) : null;
                    if (capsule == null) continue;
                    Vector3 at = terrain.GetPosition() + Vector3.Scale(tree.position, data.size);
                    float d = Vector2.Distance(new Vector2(at.x, at.z), new Vector2(TestTerrainTree.x, TestTerrainTree.z));
                    if (d >= best) continue;
                    best = d; foot = at;
                    height = capsule.height * capsule.transform.lossyScale.y * tree.heightScale;
                    radius = capsule.radius * Mathf.Max(capsule.transform.lossyScale.x, capsule.transform.lossyScale.z) * tree.widthScale;
                }
                proxy.transform.position = foot;
                var body = proxy.AddComponent<CapsuleCollider>();
                body.direction = 1; body.radius = radius; body.height = height; body.center = new Vector3(0f, height * 0.5f, 0f);
                var marker = proxy.AddComponent<ClimbableSurface>();
                marker.kind = ClimbableKind.Tree; marker.trunkRadius = radius; marker.height = height; marker.perchHeight = height * 0.6f;
                Physics.SyncTransforms();
                var ask = new PlayerWallRunMotor.GhostWallAsk { Open = true, Alive = true, Run = true, Flux = 999f, HasFluxForOne = true,
                    Up = Vector3.up, Forward = Vector3.forward, GroundSpeed = 28f };
                string treeWhy = motor.FindTestTreeClimb(foot, ask, out Vector3 stand, out Vector3 point, out Vector3 faceNormal);
                report.AppendLine($"[probe] terrain tree at {foot} ({height:F1} m, {best:F2} m from the planned point), the scene's tree colliders " +
                    $"{(setting.FindProperty("m_EnableTreeColliders").boolValue ? "ON" : "off")}: {(treeWhy == null ? $"Decide ACCEPTS (face {point}, start {stand})" : treeWhy)}");
                // gui-catch-4: the click on this tree went to the wall walk ("onto the wall"). Is the face a wall-walk click, and which graph node?
                if (treeWhy == null)
                {
                    var graph = rules != null && rules.Entry != null ? rules.Entry.surfaceGraph : null;
                    graph?.EnsureLoaded();
                    int node = graph != null ? graph.Nearest(point + faceNormal * 0.38f, 1.2f) : -1;
                    report.AppendLine($"[probe] the tree's face as a click: wall walk's? {motor.WantsWallWalkClick(point, faceNormal, false)}; nearest graph node " +
                        (node >= 0 ? $"{node} at {graph.points[node]} normal {graph.normals[node]} ({Vector3.Distance(graph.points[node], point):F2} m from the face point)" : "none within 1.2 m"));
                    // what the baker met there (it samples colliders and the terrain's height map): every collider by that node, the probe's proxy aside
                    if (node >= 0)
                        foreach (Collider c in Physics.OverlapSphere(graph.points[node], 1f, ~0, QueryTriggerInteraction.Collide))
                            if (c.gameObject != proxy)
                                report.AppendLine($"[probe]   by the node: {Path(c.transform)} ({c.GetType().Name}, layer {LayerMask.LayerToName(c.gameObject.layer)}, " +
                                    $"enabled {c.enabled}, static {c.gameObject.isStatic}, bounds {c.bounds.center} size {c.bounds.size})");
                }
            }
            finally
            {
                Object.DestroyImmediate(player);
                Object.DestroyImmediate(proxy);
            }
            Debug.Log(report.ToString());
            Assert.Pass(report.ToString());
        }

        // Part 5's places (owner rule 1 Nov; design 6.2): the self-starting run asked exactly as the ghost planner asks it
        // (PlanAutoForGhost), at the wall walk's test graph wall (its roof and the street in front) and at a straight-climb face by the spawn.
        static readonly Vector3 Spawn = new Vector3(-6.8f, 0f, -7.5f);

        [Test, Explicit]
        public void ThePart5TestPlaces_StandInZone01()
        {
            EditorSceneManager.OpenScene(Scene, OpenSceneMode.Single);
            var report = new StringBuilder();
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var rules = Object.FindFirstObjectByType<KryticalNova.World.ZoneRules>();
            typeof(KryticalNova.World.ZoneRules).GetMethod("Awake", flags)?.Invoke(rules, null);
            var playerPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Gameplay/Player/Prefabs/Player.prefab");
            var player = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(playerPrefab);
            try
            {
                var motor = player.GetComponent<PlayerWallRunMotor>();
                typeof(PlayerWallRunMotor).GetMethod("Awake", flags)?.Invoke(motor, null);
                Physics.SyncTransforms();
                PlayerWallRunMotor.GhostWallAsk Ask(bool run) => new PlayerWallRunMotor.GhostWallAsk { Open = true, Alive = true, Run = run,
                    Flux = 999f, HasFluxForOne = true, Up = Vector3.up, Forward = Vector3.forward, GroundSpeed = run ? 28f : 6f };
                string Way(Vector3 from, Vector3 to, bool run)
                {
                    var ask = Ask(run);
                    WallWalkLayout layout = motor.PlanAutoForGhost(from, to, in ask, out PlayerWallRunMotor.AutoWay way);
                    var walk = new UnityEngine.AI.NavMeshPath();
                    bool walkable = UnityEngine.AI.NavMesh.CalculatePath(from, to, UnityEngine.AI.NavMesh.AllAreas, walk)
                        && walk.status == UnityEngine.AI.NavMeshPathStatus.PathComplete;
                    float length = 0f;
                    for (int i = 1; walkable && i < walk.corners.Length; i++) length += Vector3.Distance(walk.corners[i - 1], walk.corners[i]);
                    return $"{way} ({(layout != null ? $"{layout.Corners.Count} corners, ends {layout.LastFeet}" : "no layout")}); the walk there " +
                        (walkable ? $"{length:F1} m" : "NONE");
                }

                string why = motor.FindTestWall(TestGraphWall, out PlayerWallRunMotor.TestWall wall);
                if (why != null) report.AppendLine($"[probe] graph wall near {TestGraphWall}: NOT FOUND; {why}");
                else
                {
                    report.AppendLine($"[probe] graph wall: foot {wall.Foot}, front {wall.Front}, far {wall.FrontFar}, roof {wall.Top}");
                    report.AppendLine($"[probe] A1 roof -> street, walking: {Way(wall.Top, wall.FrontFar, false)}");
                    report.AppendLine($"[probe] A1 street -> roof, running: {Way(wall.FrontFar, wall.Top, true)}");
                    report.AppendLine($"[probe] A1 street -> roof, walking (NGO: only with no walk there): {Way(wall.FrontFar, wall.Top, false)}");
                    report.AppendLine($"[probe] control: street -> street, running: {Way(wall.FrontFar, wall.Front, true)}");
                }

                var climbAsk = Ask(true);
                string climbWhy = motor.FindTestClimb(Spawn, climbAsk, out Vector3 stand, out Vector3 point, out Vector3 normal);
                if (climbWhy != null) report.AppendLine($"[probe] straight-climb face near the spawn: NOT FOUND; {climbWhy}");
                else
                {
                    motor.PlanClimbForGhost(stand, point, normal, in climbAsk, out WallWalkLayout climb);
                    Vector3 top = climb != null ? climb.LastFeet : point;
                    report.AppendLine($"[probe] straight-climb face {point} normal {normal}, start {stand}, its top {top}");
                    report.AppendLine($"[probe] A3 start -> the top, running: {Way(stand, top, true)}");
                    report.AppendLine($"[probe] A3 start -> the top, walking (NGO: never): {Way(stand, top, false)}");
                }
            }
            finally { Object.DestroyImmediate(player); }
            Debug.Log(report.ToString());
            Assert.Pass(report.ToString());
        }

        // Part 5's straight climb by itself (A3) needs a face the graph does not cover whose top is walkable, with no walk up or one twice as
        // long: none by the spawn (the probe above). This looks over the whole terrain on a 50 m grid and lists the first five that NGO's rule
        // takes (PlanAutoForGhost answers UpClimb from the face's start to its top).
        [Test, Explicit]
        public void FindPart5ClimbFaces()
        {
            EditorSceneManager.OpenScene(Scene, OpenSceneMode.Single);
            var report = new StringBuilder();
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var rules = Object.FindFirstObjectByType<KryticalNova.World.ZoneRules>();
            typeof(KryticalNova.World.ZoneRules).GetMethod("Awake", flags)?.Invoke(rules, null);
            var playerPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Gameplay/Player/Prefabs/Player.prefab");
            var player = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(playerPrefab);
            try
            {
                var motor = player.GetComponent<PlayerWallRunMotor>();
                typeof(PlayerWallRunMotor).GetMethod("Awake", flags)?.Invoke(motor, null);
                Physics.SyncTransforms();
                var ask = new PlayerWallRunMotor.GhostWallAsk { Open = true, Alive = true, Run = true, Flux = 999f, HasFluxForOne = true,
                    Up = Vector3.up, Forward = Vector3.forward, GroundSpeed = 28f };
                Terrain terrain = Terrain.activeTerrains[0];
                Vector3 min = terrain.GetPosition(), size = terrain.terrainData.size;
                int centres = 0, faces = 0, found = 0;
                var seen = new System.Collections.Generic.List<Vector3>();
                for (float x = min.x + 25f; x < min.x + size.x && found < 5; x += 50f)
                for (float z = min.z + 25f; z < min.z + size.z && found < 5; z += 50f)
                {
                    centres++;
                    if (motor.FindTestClimb(new Vector3(x, 0f, z), ask, out Vector3 stand, out Vector3 point, out Vector3 normal) != null) continue;
                    if (seen.Exists(p => Vector3.Distance(p, point) < 5f)) continue;
                    seen.Add(point);
                    faces++;
                    motor.PlanClimbForGhost(stand, point, normal, in ask, out WallWalkLayout climb);
                    if (climb == null) continue;
                    Vector3 top = climb.LastFeet;
                    WallWalkLayout auto = motor.PlanAutoForGhost(stand, top, in ask, out PlayerWallRunMotor.AutoWay way);
                    report.AppendLine($"[probe] face {point.x:F1},{point.y:F2},{point.z:F1} normal {normal.x:F2},{normal.z:F2}, start {stand.x:F1},{stand.y:F2},{stand.z:F1}, " +
                        $"top {top.x:F1},{top.y:F2},{top.z:F1} ({top.y - stand.y:F1} m up): by itself {way}");
                    if (way == PlayerWallRunMotor.AutoWay.UpClimb) found++;
                }
                report.AppendLine($"[probe] {centres} grid centres, {faces} distinct climbable faces, {found} taken by the straight climb by itself (A3)");
            }
            finally { Object.DestroyImmediate(player); }
            Debug.Log(report.ToString());
            Assert.Pass(report.ToString());
        }

        // gui-catch-4 (1 Nov): the planned terrain tree stood against Ruin 26's rubble, so its trunk was a wall-walk click. A tree truly in the
        // open: 6 m and up, nothing but the ground within 4 m, NGO's Decide accepts it (its proxy made as TreeProxyField makes it) and its face is
        // not the wall walk's. The first three nearest the spawn, for the plan.
        [Test, Explicit]
        public void FindTerrainTreesInTheOpen()
        {
            EditorSceneManager.OpenScene(Scene, OpenSceneMode.Single);
            var report = new StringBuilder();
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var rules = Object.FindFirstObjectByType<KryticalNova.World.ZoneRules>();
            typeof(KryticalNova.World.ZoneRules).GetMethod("Awake", flags)?.Invoke(rules, null);
            var playerPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Gameplay/Player/Prefabs/Player.prefab");
            var player = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(playerPrefab);
            var proxy = new GameObject("Tree Proxy (probe)");
            var body = proxy.AddComponent<CapsuleCollider>();
            body.direction = 1;
            var marker = proxy.AddComponent<ClimbableSurface>();
            marker.kind = ClimbableKind.Tree;
            try
            {
                var motor = player.GetComponent<PlayerWallRunMotor>();
                typeof(PlayerWallRunMotor).GetMethod("Awake", flags)?.Invoke(motor, null);
                var ask = new PlayerWallRunMotor.GhostWallAsk { Open = true, Alive = true, Run = true, Flux = 999f, HasFluxForOne = true,
                    Up = Vector3.up, Forward = Vector3.forward, GroundSpeed = 28f };
                Terrain terrain = Terrain.activeTerrains[0];
                TerrainData data = terrain.terrainData;
                var trees = new System.Collections.Generic.List<(float d, Vector3 foot, float h, float r)>();
                foreach (TreeInstance tree in data.treeInstances)
                {
                    GameObject prefab = data.treePrototypes[tree.prototypeIndex].prefab;
                    CapsuleCollider capsule = prefab != null ? prefab.GetComponentInChildren<CapsuleCollider>(true) : null;
                    if (capsule == null) continue;
                    float h = capsule.height * capsule.transform.lossyScale.y * tree.heightScale;
                    if (h < 6f) continue;
                    Vector3 at = terrain.GetPosition() + Vector3.Scale(tree.position, data.size);
                    float r = capsule.radius * Mathf.Max(capsule.transform.lossyScale.x, capsule.transform.lossyScale.z) * tree.widthScale;
                    trees.Add((new Vector2(at.x + 3f, at.z + 15f).magnitude, at, h, r));
                }
                trees.Sort((x, y) => x.d.CompareTo(y.d));
                int found = 0, tried = 0, crowded = 0, refused = 0, wallWalk = 0;
                foreach (var (d, foot, h, r) in trees)
                {
                    if (found >= 3 || tried >= 400) break;
                    tried++;
                    bool clear = true;
                    foreach (Collider c in Physics.OverlapSphere(foot + Vector3.up * 2f, 4f, ~0, QueryTriggerInteraction.Ignore))
                        if (!(c is TerrainCollider) && c.gameObject != proxy) { clear = false; break; }
                    if (!clear) { crowded++; continue; }
                    proxy.transform.position = foot;
                    body.radius = r; body.height = h; body.center = new Vector3(0f, h * 0.5f, 0f);
                    marker.trunkRadius = r; marker.height = h; marker.perchHeight = h * 0.6f;
                    Physics.SyncTransforms();
                    string why = motor.FindTestTreeClimb(foot, ask, out Vector3 stand, out Vector3 point, out Vector3 normal);
                    if (why != null) { refused++; continue; }
                    if (motor.WantsWallWalkClick(point, normal, false)) { wallWalk++; continue; }
                    found++;
                    report.AppendLine($"[probe] OPEN terrain tree {found}: foot {foot.x:F2},{foot.y:F2},{foot.z:F2} ({h:F1} m, {d:F0} m from the spawn); face {point}, start {stand}");
                }
                report.AppendLine($"[probe] {trees.Count} terrain trees 6 m and up; tried {tried}: {crowded} crowded within 4 m, {refused} refused by Decide, {wallWalk} the wall walk's, {found} open");
            }
            finally
            {
                Object.DestroyImmediate(player);
                Object.DestroyImmediate(proxy);
            }
            Debug.Log(report.ToString());
            Assert.Pass(report.ToString());
        }

        static string Path(Transform t)
        {
            string p = t.name;
            for (Transform u = t.parent; u != null; u = u.parent) p = u.name + "/" + p;
            return p;
        }
    }
}
