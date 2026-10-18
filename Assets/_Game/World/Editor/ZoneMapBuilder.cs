using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Lays a map out from its <see cref="ZoneLayout"/> (World/Data/ZoneLayouts/&lt;scene&gt;.json, written from
    /// Design/Zone-Build-Kits.md): sizes and shapes the terrain, paints it, plants its trees, stands its set pieces
    /// and scatters its props, all from the place's kit (prefab variants already at world scale), then sets the
    /// markers, the concept-art camera shots, the NavMesh and the minimap. Everything it makes is under one root,
    /// "--- Zone Build ---", so the map's own pieces (network stack, HUD, sky) are never touched. A map whose root
    /// holds a child named <see cref="LockName"/> has been dressed by hand and is never laid out again.
    /// </summary>
    public static class ZoneMapBuilder
    {
        public const string LayoutsFolder = "Assets/_Game/World/Data/ZoneLayouts";
        public const string RootName = "--- Zone Build ---";
        public const string LockName = "Zone Build: dressed by hand, do not rebuild";
        public const string ShotsName = "Concept Shots";
        public const string MarkersName = "Markers";
        private const string ZonesRoot = ZoneKitBuilder.ZonesRoot;

        private static System.Random rng;
        private static ZoneLayout L;
        private static Terrain terrain;
        private static float[,] h; // world heights, [z, x]
        private static readonly List<(Vector2 p, float r)> keepOut = new List<(Vector2, float)>();
        private static readonly Dictionary<string, List<GameObject>> prefabCache = new Dictionary<string, List<GameObject>>();
        private static string kitFolder, regionKitFolder;
        private static StringBuilder log;

        [MenuItem("Krytical Nova/World/Lay Out Open Map From Its Zone Layout")]
        public static void BuildOpenMenu() => Debug.Log("[World] " + Build(SceneManager.GetActiveScene().path, false));

        /// <summary>Lays out the map at <paramref name="scenePath"/>. Returns a report.</summary>
        public static string Build(string scenePath, bool bakeNav = true)
        {
            log = new StringBuilder();
            prefabCache.Clear();
            keepOut.Clear();
            if (EditorApplication.isPlaying) return "Stop Play mode first.";
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != scenePath)
            {
                string unsaved = UnsavedScene();
                if (unsaved != null) return unsaved;
                scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            }
            string layoutPath = $"{LayoutsFolder}/{scene.name}.json";
            if (!File.Exists(layoutPath)) return $"No layout at {layoutPath}.";
            L = JsonUtility.FromJson<ZoneLayout>(File.ReadAllText(layoutPath));
            rng = new System.Random(L.seed);

            GameObject root = Root(scene);
            if (root != null && root.transform.Find(LockName) != null) return $"{scene.name} is dressed by hand: left alone.";
            if (root != null) UnityEngine.Object.DestroyImmediate(root);
            root = new GameObject(RootName);
            SceneManager.MoveGameObjectToScene(root, scene);

            FindKits(scene.name);
            terrain = UnityEngine.Object.FindAnyObjectByType<Terrain>();
            if (terrain == null) return "The map has no terrain.";
            ShapeTerrain(scene.name);
            Paint();
            foreach (ZoneLayout.Piece piece in L.objects) Place(root, piece);
            foreach (ZoneLayout.Ring ring in L.rings) PlaceRing(root, ring);
            foreach (ZoneLayout.Line line in L.lines) PlaceLine(root, line);
            foreach (ZoneLayout.Scatter s in L.scatter) ScatterObjects(root, s);
            PlantTrees();
            Water(root);
            Markers(root);
            Shots(root);

            if (bakeNav)
            {
                var surface = UnityEngine.Object.FindAnyObjectByType<NavMeshSurface>();
                if (surface != null)
                {
                    surface.BuildNavMesh();
                    string folder = $"{Path.GetDirectoryName(scene.path).Replace('\\', '/')}/{scene.name}";
                    WeatherVfxBuilder.EnsureFolder(folder);
                    string navPath = $"{folder}/NavMesh-Navigation.asset";
                    AssetDatabase.DeleteAsset(navPath);
                    AssetDatabase.CreateAsset(surface.navMeshData, navPath);
                    EditorUtility.SetDirty(surface);
                }
                MinimapBaker.Bake();
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            log.Insert(0, $"{scene.name}: laid out, {root.GetComponentsInChildren<Transform>().Length - 1} objects, {terrain.terrainData.treeInstanceCount} trees.\n");
            return log.ToString();
        }

        private static GameObject Root(Scene scene) => scene.GetRootGameObjects().FirstOrDefault(g => g.name == RootName);

        /// <summary>Never opens a scene over unsaved work: the caller is told to save first.</summary>
        private static string UnsavedScene()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) return $"{SceneManager.GetSceneAt(i).name} has unsaved changes: save it first.";
            return null;
        }


        /// <summary>
        /// The maps the World Atlas has and <see cref="WorldScenesBuilder"/> left out: the Ruptureworks and every
        /// Sunderpeak (Ronin) map, each copied from a blank map of its kind. They are not put in the build list or the
        /// zone catalog: the Ruptureworks waits for its portals, and the Ronin stay hidden until their expansion.
        /// </summary>
        private static readonly (string scene, string from)[] Missing =
        {
            ("Dungeons/TheScar/TheScar_Run_TheRuptureworks", "Dungeons/Cessation/Cessation_Run_TheStirringWell"),
            ("Towns/Sunderpeak_Kagero", "Towns/EldersReach_Aldhavn"),
            ("Overworld/Sunderpeak/Sunderpeak_Z1_TheEmberTerraces", "Overworld/EldersReach/EldersReach_Z1_BirchlightVale"),
            ("Overworld/Sunderpeak/Sunderpeak_Z2_TheCaneSea", "Overworld/EldersReach/EldersReach_Z2_Skeldfjord"),
            ("Overworld/Sunderpeak/Sunderpeak_Z3_BlackfallGorge", "Overworld/EldersReach/EldersReach_Z3_TheSylwood"),
            ("Overworld/Sunderpeak/Sunderpeak_Z4_TheVeilpass", "Overworld/EldersReach/EldersReach_Z4_Barrowfell"),
            ("Dungeons/Sunderpeak/Sunderpeak_Run_Steamhollow", "Dungeons/EldersReach/EldersReach_Run_Hearthbarrow"),
            ("Dungeons/Sunderpeak/Sunderpeak_Run_TheListeningHouse", "Dungeons/EldersReach/EldersReach_Run_WrecktideGrotto"),
            ("Dungeons/Sunderpeak/Sunderpeak_Run_TheLanternGallery", "Dungeons/EldersReach/EldersReach_Run_TheRootvault"),
            ("Dungeons/Sunderpeak/Sunderpeak_Run_TheQuietArchive", "Dungeons/EldersReach/EldersReach_Run_HowlstoneCrypts"),
        };

        [MenuItem("Krytical Nova/World/Make Missing Atlas Scenes (Ruptureworks, Sunderpeak)")]
        public static void MakeMissingScenesMenu() => Debug.Log("[World] " + MakeMissingScenes());

        public static string MakeMissingScenes()
        {
            var report = new StringBuilder();
            string unsaved = UnsavedScene();
            if (unsaved != null) return unsaved;
            foreach (var (scene, from) in Missing)
            {
                string path = $"Assets/_Game/Scenes/World/{scene}.unity", source = $"Assets/_Game/Scenes/World/{from}.unity";
                if (File.Exists(path)) { report.AppendLine($"{scene}: already there"); continue; }
                WeatherVfxBuilder.EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
                if (!AssetDatabase.CopyAsset(source, path)) { report.AppendLine($"{scene}: could not copy {source}"); continue; }
                Scene s = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                string id = Path.GetFileNameWithoutExtension(path);
                foreach (var session in UnityEngine.Object.FindObjectsByType<Networking.ZoneSessionManager>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    var so = new SerializedObject(session);
                    so.FindProperty("currentZoneId").stringValue = id;
                    so.FindProperty("currentZoneDisplayName").stringValue = id.Substring(id.LastIndexOf('_') + 1);
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
                EditorSceneManager.MarkSceneDirty(s);
                EditorSceneManager.SaveScene(s);
                report.AppendLine($"{scene}: made from {from}");
            }
            return report.ToString();
        }

        // ------------------------------------------------------------------ kits

        private static void FindKits(string sceneName)
        {
            string region = sceneName.Split('_')[0];
            string place = sceneName.Substring(region.Length).TrimStart('_');
            if (sceneName == "TheScar") { region = "TheScar"; place = "TheScar"; }
            // A Home City's folder is HomeCity_<Name>; zones and Runs keep their scene names.
            string folder = $"{ZonesRoot}/{region}/{place}";
            if (!AssetDatabase.IsValidFolder(folder)) folder = $"{ZonesRoot}/{region}/HomeCity_{place}";
            kitFolder = $"{folder}/Kit";
            regionKitFolder = $"{ZonesRoot}/{region}/_RegionKit";
        }

        /// <summary>
        /// The prefabs a name stands for: the kit's variants first (the place's own, then the region's), so what is
        /// placed is already at world scale; the vendor prefab only when no kit holds it. "Name_*" is the family.
        /// </summary>
        public static List<GameObject> Prefabs(string name)
        {
            if (prefabCache.TryGetValue(name, out var cached)) return cached;
            bool family = name.EndsWith("_*");
            string stem = family ? name.Substring(0, name.Length - 2) : name;
            // A family is its numbered pieces (Wall_01, Wall_02A), not its longer cousins (Wall_Tower_01); a stem with
            // no numbered pieces takes every name it begins.
            var numbered = new System.Text.RegularExpressions.Regex("^" + System.Text.RegularExpressions.Regex.Escape(stem) + @"_\d+[A-Za-z]?(_.*)?$");
            bool Match(string file, bool loose) => !family ? file == stem : loose ? file == stem || file.StartsWith(stem + "_", StringComparison.Ordinal) : numbered.IsMatch(file);

            var found = new List<GameObject>();
            foreach (bool loose in new[] { false, true })
            {
                foreach (string folder in new[] { kitFolder, regionKitFolder, "Assets/Synty" })
                {
                    if (!AssetDatabase.IsValidFolder(folder)) continue;
                    foreach (string guid in AssetDatabase.FindAssets(stem + " t:Prefab", new[] { folder }))
                    {
                        string path = AssetDatabase.GUIDToAssetPath(guid);
                        if (path.Contains("/Characters/") || path.Contains("/Demo")) continue;
                        if (!Match(Path.GetFileNameWithoutExtension(path), loose)) continue;
                        var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                        if (go != null) found.Add(go);
                    }
                    if (found.Count > 0) break;
                }
                if (found.Count > 0 || !family) break;
            }
            if (found.Count == 0) log.AppendLine($"no prefab for {name}");
            prefabCache[name] = found;
            return found;
        }

        private static GameObject Pick(List<string> names)
        {
            var all = new List<GameObject>();
            foreach (string n in names) all.AddRange(Prefabs(n));
            return all.Count == 0 ? null : all[rng.Next(all.Count)];
        }

        // ------------------------------------------------------------------ terrain

        private static void ShapeTerrain(string sceneName)
        {
            // A map copied from another holds that map's terrain: it gets its own before anything is changed.
            string region = sceneName == "TheScar" ? "TheScar" : sceneName.Split('_')[0];
            string own = $"{ZonesRoot}/{region}/{sceneName}_TerrainData.asset";
            if (terrain.terrainData == null || AssetDatabase.GetAssetPath(terrain.terrainData) != own)
            {
                var fresh = AssetDatabase.LoadAssetAtPath<TerrainData>(own);
                if (fresh == null)
                {
                    WeatherVfxBuilder.EnsureFolder($"{ZonesRoot}/{region}");
                    fresh = new TerrainData();
                    AssetDatabase.CreateAsset(fresh, own);
                }
                terrain.terrainData = fresh;
                if (terrain.TryGetComponent<TerrainCollider>(out var tc)) tc.terrainData = fresh;
                log.AppendLine($"terrain data of its own: {own}");
            }
            TerrainData data = terrain.terrainData;
            data.heightmapResolution = L.heightRes;
            data.alphamapResolution = L.alphaRes;
            data.baseMapResolution = 1024;
            data.size = new Vector3(L.sizeX, L.height, L.sizeZ);
            terrain.transform.position = new Vector3(-L.sizeX * 0.5f, -L.depth, -L.sizeZ * 0.5f);

            int n = data.heightmapResolution;
            h = new float[n, n];
            for (int z = 0; z < n; z++) for (int x = 0; x < n; x++) h[z, x] = 0f;
            foreach (ZoneLayout.Stamp s in L.stamps) Stamp(s, n);
            foreach (ZoneLayout.Road r in L.roads) if (r.flatten) FlattenRoad(r, n);

            var norm = new float[n, n];
            for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++)
                    norm[z, x] = Mathf.Clamp01((h[z, x] + L.depth) / L.height);
            data.SetHeights(0, 0, norm);
            data.SyncHeightmap();
            terrain.Flush();
            EditorUtility.SetDirty(data);
        }

        private static Vector2 World(int x, int z, int n) => new Vector2(x / (float)(n - 1) * L.sizeX - L.sizeX * 0.5f, z / (float)(n - 1) * L.sizeZ - L.sizeZ * 0.5f);

        private static float Smooth(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }

        private static void Stamp(ZoneLayout.Stamp s, int n)
        {
            var pts = Points(s.pts);
            float seedX = (float)rng.NextDouble() * 1000f, seedZ = (float)rng.NextDouble() * 1000f;
            for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++)
                {
                    Vector2 p = World(x, z, n);
                    float cur = h[z, x];
                    switch (s.type)
                    {
                        case "noise":
                        {
                            float v = 0f, amp = 1f, f = s.scale, sum = 0f;
                            for (int o = 0; o < 4; o++) { v += (Mathf.PerlinNoise(seedX + p.x * f, seedZ + p.y * f) - 0.5f) * amp; sum += amp; amp *= 0.5f; f *= 2.1f; }
                            float fade = 1f;
                            if (s.r > 0f) fade = Smooth((Vector2.Distance(p, new Vector2(s.x, s.z)) - s.r) / Mathf.Max(1f, s.r2 - s.r));
                            h[z, x] = cur + v / sum * 2f * s.amp * fade;
                            break;
                        }
                        case "slope":
                            // the land rises northward by h between z = r and z = r2 (a climb to a pass, not stairs)
                            h[z, x] = cur + s.h * Smooth((p.y - s.r) / Mathf.Max(1f, s.r2 - s.r));
                            break;
                        case "floor":
                            // the ground never dips below h (water only where a later stamp carves it)
                            h[z, x] = Mathf.Max(cur, s.h);
                            break;
                        case "hill":
                        {
                            float d = Vector2.Distance(p, new Vector2(s.x, s.z));
                            if (d < s.r) h[z, x] = cur + s.h * (0.5f + 0.5f * Mathf.Cos(Mathf.PI * d / s.r));
                            break;
                        }
                        case "plateau":
                        {
                            float d = Vector2.Distance(p, new Vector2(s.x, s.z));
                            float t = 1f - Smooth((d - s.r) / Mathf.Max(0.01f, s.falloff));
                            if (t > 0f) h[z, x] = Mathf.Lerp(cur, s.h, t);
                            break;
                        }
                        case "rect":
                        {
                            Vector2 q = Rotate(p - new Vector2(s.x, s.z), -s.rot);
                            float dx = Mathf.Abs(q.x) - s.w * 0.5f, dz = Mathf.Abs(q.y) - s.d * 0.5f;
                            float d = Mathf.Max(dx, dz);
                            float t = 1f - Smooth(d / Mathf.Max(0.01f, s.falloff));
                            if (t > 0f) h[z, x] = Mathf.Lerp(cur, s.h, t);
                            break;
                        }
                        case "crater":
                        {
                            float d = Vector2.Distance(p, new Vector2(s.x, s.z));
                            if (d < s.r) h[z, x] = cur - s.h * Smooth(1f - d / s.r) + s.rim * Mathf.Sin(Mathf.PI * Mathf.Clamp01(d / s.r));
                            else if (d < s.r * 1.4f) h[z, x] = cur + s.rim * (1f - Smooth((d - s.r) / (s.r * 0.4f)));
                            break;
                        }
                        case "line":
                        {
                            float d = DistanceToLine(pts, p);
                            float t = 1f - Smooth((d - s.w * 0.5f) / Mathf.Max(0.01f, s.falloff));
                            if (t <= 0f) break;
                            if (s.mode == "set") h[z, x] = Mathf.Lerp(cur, s.h, t);
                            else if (s.mode == "carve") h[z, x] = Mathf.Min(cur, Mathf.Lerp(cur, s.h, t));
                            else h[z, x] = cur + s.h * t;
                            break;
                        }
                        case "rim":
                        {
                            float edge = Mathf.Min(Mathf.Min(p.x + L.sizeX * 0.5f, L.sizeX * 0.5f - p.x), Mathf.Min(p.y + L.sizeZ * 0.5f, L.sizeZ * 0.5f - p.y));
                            float t = 1f - Smooth(edge / Mathf.Max(1f, s.w));
                            for (int g = 0; g + 2 < s.gaps.Count; g += 3)
                            {
                                float gd = Vector2.Distance(p, new Vector2(s.gaps[g], s.gaps[g + 1]));
                                t *= Smooth((gd - s.gaps[g + 2] * 0.5f) / Mathf.Max(1f, s.gaps[g + 2]));
                            }
                            float wobble = 0.75f + 0.5f * Mathf.PerlinNoise(seedX + p.x * 0.01f, seedZ + p.y * 0.01f);
                            h[z, x] = cur + s.h * t * wobble;
                            break;
                        }
                        case "terraces":
                        {
                            float d = Vector2.Distance(p, new Vector2(s.x, s.z));
                            if (d > s.r2) break;
                            if (d < s.r) { h[z, x] = Mathf.Min(cur, s.h - s.steps * s.d); break; }
                            float t = (d - s.r) / Mathf.Max(1f, s.r2 - s.r); // 0 at the inner edge, 1 at the outer
                            float step = Mathf.Floor(t * s.steps);
                            h[z, x] = s.h - (s.steps - step) * s.d + s.d * Smooth((t * s.steps - step - 0.85f) / 0.15f);
                            break;
                        }
                    }
                }
        }

        private static void FlattenRoad(ZoneLayout.Road r, int n)
        {
            var pts = Points(r.pts);
            for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++)
                {
                    Vector2 p = World(x, z, n);
                    float d = DistanceToLine(pts, p, out float along);
                    if (d > r.w * 0.5f + r.shoulder) continue;
                    float target = RoadHeight(pts, along);
                    float t = 1f - Smooth((d - r.w * 0.5f) / Mathf.Max(0.01f, r.shoulder));
                    h[z, x] = Mathf.Lerp(h[z, x], target, t);
                }
        }

        /// <summary>A road keeps the ground height of its own points, eased between them.</summary>
        private static float RoadHeight(List<Vector2> pts, float along)
        {
            float acc = 0f;
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                float len = Vector2.Distance(pts[i], pts[i + 1]);
                if (along <= acc + len || i + 2 == pts.Count)
                {
                    float t = len > 0f ? Mathf.Clamp01((along - acc) / len) : 0f;
                    return Mathf.Lerp(HeightAt(pts[i]), HeightAt(pts[i + 1]), t);
                }
                acc += len;
            }
            return pts.Count > 0 ? HeightAt(pts[0]) : 0f;
        }

        private static float HeightAt(Vector2 p)
        {
            int n = h.GetLength(0);
            float fx = (p.x + L.sizeX * 0.5f) / L.sizeX * (n - 1), fz = (p.y + L.sizeZ * 0.5f) / L.sizeZ * (n - 1);
            int x = Mathf.Clamp(Mathf.RoundToInt(fx), 0, n - 1), z = Mathf.Clamp(Mathf.RoundToInt(fz), 0, n - 1);
            return h[z, x];
        }

        private static void Paint()
        {
            TerrainData data = terrain.terrainData;
            var layers = new List<TerrainLayer>();
            foreach (string path in L.layers)
            {
                var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
                if (layer == null) log.AppendLine($"no terrain layer {path}");
                layers.Add(layer);
            }
            data.terrainLayers = layers.ToArray();
            int a = data.alphamapResolution, count = Mathf.Max(1, layers.Count);
            var alpha = new float[a, a, count];
            for (int z = 0; z < a; z++)
                for (int x = 0; x < a; x++)
                {
                    var w = new float[count];
                    w[0] = 1f;
                    Vector2 p = new Vector2(x / (float)(a - 1) * L.sizeX - L.sizeX * 0.5f, z / (float)(a - 1) * L.sizeZ - L.sizeZ * 0.5f);
                    float y = HeightAt(p);
                    foreach (ZoneLayout.Band b in L.bands)
                        Blend(w, b.layer, Smooth((y - b.from) / Mathf.Max(0.01f, b.blend)) * (1f - Smooth((y - b.to) / Mathf.Max(0.01f, b.blend))));
                    foreach (ZoneLayout.Shape s in L.paint) Blend(w, s.layer, Inside(s, p));
                    foreach (ZoneLayout.Road r in L.roads)
                        if (r.layer >= 0) Blend(w, r.layer, 1f - Smooth((DistanceToLine(Points(r.pts), p) - r.w * 0.5f) / 3f));
                    if (L.slopeLayer >= 0)
                    {
                        float slope = data.GetSteepness(x / (float)(a - 1), z / (float)(a - 1));
                        Blend(w, L.slopeLayer, Smooth((slope - L.slopeStart) / Mathf.Max(0.01f, L.slopeFull - L.slopeStart)));
                    }
                    float sum = w.Sum();
                    for (int i = 0; i < count; i++) alpha[z, x, i] = w[i] / Mathf.Max(0.0001f, sum);
                }
            data.SetAlphamaps(0, 0, alpha);
            EditorUtility.SetDirty(data);
        }

        private static void Blend(float[] w, int layer, float t)
        {
            if (t <= 0f || layer < 0 || layer >= w.Length) return;
            for (int i = 0; i < w.Length; i++) w[i] *= 1f - t;
            w[layer] += t;
        }

        /// <summary>How far inside a shape a point is: 1 inside, falling to 0 over the shape's falloff.</summary>
        private static float Inside(ZoneLayout.Shape s, Vector2 p)
        {
            float d;
            switch (s.shape)
            {
                case "all": return 1f;
                case "ring":
                {
                    float r = Vector2.Distance(p, new Vector2(s.x, s.z));
                    d = Mathf.Max(s.r - r, r - s.r2);
                    if (s.a1 > s.a0)
                    {
                        float ang = Mathf.Repeat(Mathf.Atan2(p.x - s.x, p.y - s.z) * Mathf.Rad2Deg - s.a0, 360f);
                        float span = s.a1 - s.a0;
                        float outside = ang <= span ? 0f : Mathf.Min(ang - span, 360f - ang);
                        d = Mathf.Max(d, outside * Mathf.Deg2Rad * r);
                    }
                    break;
                }
                case "rect":
                {
                    Vector2 q = Rotate(p - new Vector2(s.x, s.z), -s.rot);
                    d = Mathf.Max(Mathf.Abs(q.x) - s.w * 0.5f, Mathf.Abs(q.y) - s.d * 0.5f);
                    break;
                }
                case "line": d = DistanceToLine(Points(s.pts), p) - s.w * 0.5f; break;
                default: d = Vector2.Distance(p, new Vector2(s.x, s.z)) - s.r; break;
            }
            return 1f - Smooth(d / Mathf.Max(0.01f, s.falloff));
        }

        // ------------------------------------------------------------------ things on the ground

        private static Vector3 Ground(float x, float z, float dy = 0f) =>
            new Vector3(x, terrain.SampleHeight(new Vector3(x, 0f, z)) + terrain.transform.position.y + dy, z);

        private static Transform Group(GameObject root, string name)
        {
            Transform t = root.transform.Find(name);
            if (t == null) { t = new GameObject(name).transform; t.SetParent(root.transform, false); }
            return t;
        }

        private static GameObject Spawn(GameObject prefab, Transform parent, Vector3 pos, Quaternion rot, float scale, bool collide)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.transform.SetPositionAndRotation(pos, rot);
            go.transform.localScale = prefab.transform.localScale * scale;
            if (collide && go.GetComponentInChildren<Collider>() == null)
                foreach (MeshFilter mf in go.GetComponentsInChildren<MeshFilter>())
                    if (mf.sharedMesh != null && mf.GetComponent<Collider>() == null) mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.OccludeeStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.ReflectionProbeStatic | StaticEditorFlags.NavigationStatic);
            Bounds b = ZoneKitBuilderBounds(go);
            keepOut.Add((new Vector2(pos.x, pos.z), Mathf.Max(b.extents.x, b.extents.z)));
            return go;
        }

        private static Bounds ZoneKitBuilderBounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.one);
            Bounds b = rs[0].bounds;
            foreach (Renderer r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        /// <summary>The prefab's size as it stands (its kit scale included), from its meshes.</summary>
        public static Vector3 LocalSize(GameObject prefab)
        {
            bool any = false;
            Bounds b = default;
            Matrix4x4 toRoot = prefab.transform.worldToLocalMatrix;
            Matrix4x4 rootScale = Matrix4x4.Scale(prefab.transform.localScale);
            foreach (MeshFilter mf in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                Matrix4x4 m = rootScale * toRoot * mf.transform.localToWorldMatrix;
                Bounds mb = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 w = m.MultiplyPoint3x4(c);
                    if (!any) { b = new Bounds(w, Vector3.zero); any = true; } else b.Encapsulate(w);
                }
            }
            return any ? b.size : Vector3.one;
        }

        /// <summary>What to scale a piece by so it stands <paramref name="height"/> m high (1 when no height is asked).</summary>
        private static float Fit(GameObject prefab, float height) => height > 0f ? height / Mathf.Max(0.01f, LocalSize(prefab).y) : 1f;

        private static void Place(GameObject root, ZoneLayout.Piece piece)
        {
            List<GameObject> found = Prefabs(piece.prefab);
            if (found.Count == 0) return;
            GameObject prefab = found[rng.Next(found.Count)];
            Vector3 pos = float.IsNaN(piece.y) ? Ground(piece.x, piece.z, piece.dy) : new Vector3(piece.x, piece.y, piece.z);
            GameObject go = Spawn(prefab, Group(root, piece.group), pos, Quaternion.Euler(piece.tilt, piece.rot, piece.roll), piece.scale * Fit(prefab, piece.fitHeight), piece.collide);
            if (!string.IsNullOrEmpty(piece.name)) go.name = piece.name;
        }

        private static void PlaceRing(GameObject root, ZoneLayout.Ring ring)
        {
            Transform parent = Group(root, ring.group);
            GameObject first = Pick(ring.prefabs);
            if (first == null) return;
            float fit = Fit(first, ring.fitHeight) * ring.scale;
            int count = ring.count;
            bool full = ring.endAngle - ring.startAngle >= 359.9f;
            if (count <= 0)
            {
                float arc = Mathf.Deg2Rad * (ring.endAngle - ring.startAngle) * ring.r;
                count = Mathf.Max(1, Mathf.CeilToInt(arc / Mathf.Max(0.5f, LocalSize(first).x * fit)) + (full ? 0 : 1));
            }
            for (int i = 0; i < count; i++)
            {
                GameObject prefab = i == 0 ? first : Pick(ring.prefabs);
                float ang = Mathf.Lerp(ring.startAngle, ring.endAngle, full ? i / (float)count : i / Mathf.Max(1f, count - 1f));
                float rad = ang * Mathf.Deg2Rad;
                float x = ring.x + Mathf.Sin(rad) * ring.r, z = ring.z + Mathf.Cos(rad) * ring.r;
                float yaw = ang + (ring.faceOut ? 0f : 180f) + ring.rotOffset;
                Spawn(prefab, parent, Ground(x, z, ring.dy), Quaternion.Euler(0f, yaw, 0f), fit, ring.collide);
            }
        }

        private static void PlaceLine(GameObject root, ZoneLayout.Line line)
        {
            var pts = Points(line.pts);
            Transform parent = Group(root, line.group);
            GameObject first = Pick(line.prefabs);
            if (first == null) return;
            float fit = Fit(first, line.fitHeight) * line.scale;
            float spacing = line.spacing > 0f ? line.spacing : Mathf.Max(0.5f, Mathf.Max(LocalSize(first).x, LocalSize(first).z) * fit);
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                Vector2 a = pts[i], b = pts[i + 1];
                float len = Vector2.Distance(a, b);
                int steps = Mathf.Max(1, Mathf.RoundToInt(len / spacing));
                float yaw = Mathf.Atan2(b.x - a.x, b.y - a.y) * Mathf.Rad2Deg + line.rotOffset;
                Vector2 dir = (b - a).normalized, side = new Vector2(dir.y, -dir.x);
                for (int s = 0; s < steps; s++)
                {
                    GameObject prefab = Pick(line.prefabs);
                    if (prefab == null) return;
                    Vector2 p = Vector2.Lerp(a, b, (s + 0.5f) / steps) + side * line.offset;
                    if (line.jitter > 0f) p += new Vector2((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f) * line.jitter;
                    Spawn(prefab, parent, Ground(p.x, p.y, line.dy), Quaternion.Euler(0f, line.align ? yaw : (float)rng.NextDouble() * 360f, 0f), fit, line.collide);
                }
            }
        }

        private static bool Free(Vector2 p, float clearance, bool offRoads)
        {
            foreach (var k in keepOut) if (Vector2.Distance(p, k.p) < k.r + clearance) return false;
            if (offRoads) foreach (ZoneLayout.Road r in L.roads) if (DistanceToLine(Points(r.pts), p) < r.w * 0.5f + 2f) return false;
            return true;
        }

        /// <summary>Candidate points inside a shape, rejected by slope, height, roads and what already stands there.</summary>
        private static IEnumerable<Vector2> Points(ZoneLayout.Scatter s)
        {
            int made = 0, tries = 0;
            var placed = new List<Vector2>();
            while (made < s.count && tries < s.count * 30)
            {
                tries++;
                Vector2 p = RandomIn(s);
                if (Inside(s, p) < 0.5f) continue;
                if (Mathf.Abs(p.x) > L.sizeX * 0.5f - 4f || Mathf.Abs(p.y) > L.sizeZ * 0.5f - 4f) continue;
                Vector3 g = Ground(p.x, p.y);
                if (g.y < s.minY || g.y > s.maxY) continue;
                if (!float.IsNaN(L.waterLevel) && g.y < L.waterLevel + 0.3f) continue;
                float slope = terrain.terrainData.GetSteepness((p.x + L.sizeX * 0.5f) / L.sizeX, (p.y + L.sizeZ * 0.5f) / L.sizeZ);
                if (slope > s.maxSlope) continue;
                if (!Free(p, 1f, s.keepOffRoads)) continue;
                bool near = false;
                foreach (Vector2 q in placed) if ((q - p).sqrMagnitude < s.minDist * s.minDist) { near = true; break; }
                if (near) continue;
                placed.Add(p);
                made++;
                yield return p;
            }
        }

        private static Vector2 RandomIn(ZoneLayout.Shape s)
        {
            float R() => (float)rng.NextDouble();
            switch (s.shape)
            {
                case "all": return new Vector2((R() - 0.5f) * L.sizeX, (R() - 0.5f) * L.sizeZ);
                case "rect":
                    return new Vector2(s.x, s.z) + Rotate(new Vector2((R() - 0.5f) * (s.w + s.falloff), (R() - 0.5f) * (s.d + s.falloff)), s.rot);
                case "line":
                {
                    var pts = Points(s.pts);
                    int i = rng.Next(Mathf.Max(1, pts.Count - 1));
                    Vector2 a = pts[i], b = pts[Mathf.Min(i + 1, pts.Count - 1)];
                    Vector2 dir = (b - a).normalized;
                    return Vector2.Lerp(a, b, R()) + new Vector2(-dir.y, dir.x) * (R() - 0.5f) * s.w;
                }
                case "ring":
                {
                    float ang = s.a1 > s.a0 ? Mathf.Lerp(s.a0, s.a1, R()) * Mathf.Deg2Rad : R() * Mathf.PI * 2f;
                    float r = Mathf.Sqrt(Mathf.Lerp(s.r * s.r, s.r2 * s.r2, R()));
                    return new Vector2(s.x + Mathf.Sin(ang) * r, s.z + Mathf.Cos(ang) * r);
                }
                default:
                {
                    float ang = R() * Mathf.PI * 2f, r = Mathf.Sqrt(R()) * (s.r + s.falloff * 0.5f);
                    return new Vector2(s.x + Mathf.Sin(ang) * r, s.z + Mathf.Cos(ang) * r);
                }
            }
        }

        private static void ScatterObjects(GameObject root, ZoneLayout.Scatter s)
        {
            Transform parent = Group(root, s.group);
            foreach (Vector2 p in Points(s))
            {
                GameObject prefab = Pick(s.prefabs);
                if (prefab == null) return;
                float scale = s.fitMax > 0f
                    ? Fit(prefab, Mathf.Lerp(s.fitMin, s.fitMax, (float)rng.NextDouble()))
                    : Mathf.Lerp(s.scaleMin, s.scaleMax, (float)rng.NextDouble());
                float yaw = (float)rng.NextDouble() * 360f;
                if (s.faceCenter) yaw = Mathf.Round(Mathf.Atan2(s.x - p.x, s.z - p.y) * Mathf.Rad2Deg / 90f) * 90f;
                Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
                if (s.align) rot = Quaternion.FromToRotation(Vector3.up, terrain.terrainData.GetInterpolatedNormal((p.x + L.sizeX * 0.5f) / L.sizeX, (p.y + L.sizeZ * 0.5f) / L.sizeZ)) * rot;
                Spawn(prefab, parent, Ground(p.x, p.y, s.dy), rot, scale, s.collide);
            }
        }

        /// <summary>
        /// Trees go on the terrain (drawn instanced, solid only near players through TreeProxyField). The kit variant's
        /// root scale is its world-scale fix; the terrain does not read a prefab's root scale, so it goes on each tree.
        /// </summary>
        private static void PlantTrees()
        {
            TerrainData data = terrain.terrainData;
            var prototypes = new List<TreePrototype>();
            var index = new Dictionary<GameObject, int>();
            var instances = new List<TreeInstance>();
            foreach (ZoneLayout.Scatter s in L.trees)
                foreach (Vector2 p in Points(s))
                {
                    GameObject prefab = Pick(s.prefabs);
                    if (prefab == null) break;
                    if (prefab.GetComponent<MeshRenderer>() == null && prefab.GetComponent<LODGroup>() == null) { log.AppendLine($"{prefab.name} cannot be a terrain tree"); continue; }
                    if (!index.TryGetValue(prefab, out int i))
                    {
                        i = prototypes.Count;
                        prototypes.Add(new TreePrototype { prefab = prefab });
                        index[prefab] = i;
                    }
                    float fit = prefab.transform.localScale.y;
                    float scale = fit * Mathf.Lerp(s.scaleMin, s.scaleMax, (float)rng.NextDouble());
                    Vector3 g = Ground(p.x, p.y);
                    instances.Add(new TreeInstance
                    {
                        prototypeIndex = i,
                        position = new Vector3((p.x + L.sizeX * 0.5f) / L.sizeX, (g.y - terrain.transform.position.y) / L.height, (p.y + L.sizeZ * 0.5f) / L.sizeZ),
                        widthScale = scale, heightScale = scale,
                        rotation = (float)rng.NextDouble() * Mathf.PI * 2f,
                        color = Color.white, lightmapColor = Color.white,
                    });
                }
            data.treeInstances = Array.Empty<TreeInstance>();
            data.treePrototypes = prototypes.ToArray();
            data.RefreshPrototypes();
            data.SetTreeInstances(instances.ToArray(), true);
            if (terrain.TryGetComponent<TerrainCollider>(out var collider)) ZoneTerrainGenerator.DisableTreeColliders(collider);
            EditorUtility.SetDirty(data);
        }

        private static void Water(GameObject root)
        {
            if (float.IsNaN(L.waterLevel)) return;
            var plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
            UnityEngine.Object.DestroyImmediate(plane.GetComponent<Collider>());
            plane.name = "Water";
            plane.transform.SetParent(root.transform, false);
            if (L.waterW > 0f)
            {
                plane.transform.position = new Vector3(L.waterX, L.waterLevel, L.waterZ);
                plane.transform.rotation = Quaternion.Euler(0f, L.waterRot, 0f);
                plane.transform.localScale = new Vector3(L.waterW / 10f, 1f, L.waterD / 10f);
            }
            else
            {
                plane.transform.position = new Vector3(0f, L.waterLevel, 0f);
                plane.transform.localScale = new Vector3(L.sizeX / 10f, 1f, L.sizeZ / 10f);
            }
            var mat = AssetDatabase.LoadAssetAtPath<Material>(string.IsNullOrEmpty(L.waterMaterial) ? "Assets/_Game/World/Zones/Zone01/M_Water_Zone01.mat" : L.waterMaterial);
            if (mat != null) plane.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        private static void Markers(GameObject root)
        {
            Transform parent = Group(root, MarkersName);
            foreach (ZoneLayout.Marker m in L.markers)
            {
                var go = new GameObject("Marker: " + m.name);
                go.transform.SetParent(parent, false);
                go.transform.position = Ground(m.x, m.z, m.dy);
            }
        }

        private static void Shots(GameObject root)
        {
            Transform parent = Group(root, ShotsName);
            parent.gameObject.tag = "EditorOnly";
            foreach (ZoneLayout.Shot s in L.shots)
            {
                var go = new GameObject("Shot: " + s.name);
                go.transform.SetParent(parent, false);
                go.transform.position = new Vector3(s.x, s.y, s.z);
                go.transform.LookAt(new Vector3(s.tx, s.ty, s.tz));
                var cam = go.AddComponent<Camera>();
                cam.enabled = false;
                cam.fieldOfView = s.fov;
                cam.farClipPlane = 6000f;
                if (s.ortho > 0f) { cam.orthographic = true; cam.orthographicSize = s.ortho * 0.5f; }
            }
        }

        // ------------------------------------------------------------------ geometry

        private static List<Vector2> Points(List<float> flat)
        {
            var pts = new List<Vector2>();
            for (int i = 0; i + 1 < flat.Count; i += 2) pts.Add(new Vector2(flat[i], flat[i + 1]));
            return pts;
        }

        private static Vector2 Rotate(Vector2 v, float deg)
        {
            float r = deg * Mathf.Deg2Rad, c = Mathf.Cos(r), s = Mathf.Sin(r);
            return new Vector2(v.x * c + v.y * s, -v.x * s + v.y * c);
        }

        private static float DistanceToLine(List<Vector2> pts, Vector2 p) => DistanceToLine(pts, p, out _);

        private static float DistanceToLine(List<Vector2> pts, Vector2 p, out float along)
        {
            along = 0f;
            if (pts.Count == 0) return float.MaxValue;
            if (pts.Count == 1) return Vector2.Distance(pts[0], p);
            float best = float.MaxValue, acc = 0f;
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                Vector2 a = pts[i], b = pts[i + 1], ab = b - a;
                float len2 = ab.sqrMagnitude;
                float t = len2 > 0f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2) : 0f;
                float d = Vector2.Distance(p, a + ab * t);
                if (d < best) { best = d; along = acc + t * Mathf.Sqrt(len2); }
                acc += Mathf.Sqrt(len2);
            }
            return best;
        }
    }
}
