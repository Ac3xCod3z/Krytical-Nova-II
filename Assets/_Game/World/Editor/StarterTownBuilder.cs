using System.Collections.Generic;
using KryticalNova.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Lays out the Zone01 walled city from code tables and seeded rules, using the POLYGON Sci-Fi City kit:
    /// a Synty-demo-style street grid (10 m two-lane streets, merged lots, market alleys, special blocks), the central
    /// plaza with the Elder hall and vendor stalls, themed districts of modular shopfronts, prefabs and stacked towers,
    /// facade billboards and street dressing, the wall, and the outskirts (ruins, skyline, bridges, north-island portal).
    /// Every root it owns is deleted and rebuilt on each run; the terrain is never touched. Origin = city centre, +Z =
    /// north (the portal side). Split by section: .Streets / .Plaza / .Districts / .Facades / .Outskirts.
    /// </summary>
    public static partial class StarterTownBuilder
    {
        public const string TownRootName = "--- Town ---";
        public const string NpcRootName = "--- NPCs ---";
        public const string PortalRootName = "--- Portals ---";
        public const string SpawnRootName = "--- Spawn ---";
        public const string OutskirtsRootName = "--- Outskirts ---";
        public const string NpcPrefabFolder = "Assets/_Game/Characters/NPC/Prefabs";

        // City bands, as distances from the centre (max(|x|, |z|)). All multiples of the 5 m tile.
        public const float WallHalf = 110f;      // wall centre line; inner face ≈ 108.5
        private const float GateHalf = 6f;       // gate openings match the 10 m avenues
        private const float PlazaHalf = 30f;
        private const float LoopIn = 30f, LoopOut = 40f;
        private const float MidIn = 60f, MidOut = 70f;
        private const float RingIn = 95f, RingOut = 105f;
        private const float LawnHalf = 20f;      // plaza lawns sit between the walkways (5 m) and the perimeter walk

        private const float Tile = 5f;
        private const int Cells = 44;            // 44 × 5 m = 220 m, wall to wall
        private const float PavingHeight = 0.03f;
        private const float BridgeDeckHeight = 0.32f;

        private static readonly string[] PrefabFolders =
        {
            "Assets/Synty/PolygonSciFiCity/Prefabs",
            "Assets/Synty/PolygonGeneric/Prefabs"
        };

        private static Dictionary<string, string> prefabIndex;
        private static readonly Dictionary<string, Bounds> footprintCache = new Dictionary<string, Bounds>();
        private static int groundLayer;
        private static System.Random rng;
        private static Terrain terrain;

        [MenuItem("Krytical Nova/Zone/Build Starter Town")]
        public static void BuildMenu() => Build();

        public static void Build()
        {
            groundLayer = LayerMask.NameToLayer("Ground");
            IndexPrefabs();
            footprintCache.Clear();
            var profile = ZoneTerrainGenerator.LoadOrCreateProfile();
            rng = new System.Random(profile.seed + 101);
            terrain = Object.FindAnyObjectByType<Terrain>();

            ClassifyTiles();
            ResetOccupancy();
            paletteUsed.Clear();
            buildings.Clear();
            buildingOcc = new bool[OccSize, OccSize];
            paletteOptions.Clear();
            ruinLibrary = RuinBaker.Library();
            ruinRng = new System.Random(profile.seed + 303);

            var town = FreshRoot(TownRootName);
            BuildWalls(Group(town, "Walls"));
            BuildStreets(Group(town, "Streets"));
            BuildPlaza(Group(town, "Central Plaza"));
            var npcs = FreshRoot(NpcRootName).transform;
            BuildStalls(Group(town, "Vendor Stalls"), npcs);
            BuildQuarters(Group(town, "Quarters"), npcs);
            BuildSpecialBlocks(Group(town, "Special Blocks"));
            BuildDistricts(Group(town, "Districts"));
            BuildAlleys(Group(town, "Market Alleys"));
            BuildStreetDressing(Group(town, "Street Dressing"));
            BuildFacadeBillboards(Group(town, "Facade Billboards"));
            BuildStreetSigns(Group(town, "Street Signs"));

            var outskirts = FreshRoot(OutskirtsRootName);
            BuildBridges(Group(outskirts, "Bridges"), profile);
            BuildWallBand(Group(outskirts, "Wall Ruins"), profile);
            BuildRuins(Group(outskirts, "Ruins"), profile);
            BuildSkyline(Group(outskirts, "Skyline"), profile);
            BuildPortal(FreshRoot(PortalRootName).transform, profile);
            BuildSpawns(FreshRoot(SpawnRootName).transform);
            BuildPointsOfInterest(Group(town, "Points of Interest"), Group(outskirts, "Points of Interest"), profile);
            PlaceExistingActors();

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log("[StarterTown] City, stalls, NPCs, outskirts, portal and spawn points built.");
        }

        // ---------------------------------------------------------------- occupancy (1 m grid over the walled city)

        private const int OccSize = (int)(WallHalf * 2f);
        private static bool[,] occupied;

        private static void ResetOccupancy()
        {
            occupied = new bool[OccSize, OccSize];
            for (int i = 0; i < Cells; i++)
            for (int j = 0; j < Cells; j++)
                if (tiles[i, j] != TileKind.Block)
                    MarkRect(new Rect(CellMin(i), CellMin(j), Tile, Tile));
        }

        // Rects are quantized to the 1 m grid with a small inward tolerance, so footprints that exactly touch (shops
        // standing shoulder to shoulder on the 5 m grid) don't claim each other's cells.
        private const float OccEpsilon = 0.15f;

        private static bool IsFree(Rect r)
        {
            int x0 = Mathf.FloorToInt(r.xMin + OccEpsilon + WallHalf), x1 = Mathf.CeilToInt(r.xMax - OccEpsilon + WallHalf);
            int z0 = Mathf.FloorToInt(r.yMin + OccEpsilon + WallHalf), z1 = Mathf.CeilToInt(r.yMax - OccEpsilon + WallHalf);
            if (x0 < 0 || z0 < 0 || x1 > OccSize || z1 > OccSize) return false;
            for (int x = x0; x < x1; x++)
            for (int z = z0; z < z1; z++)
                if (occupied[x, z]) return false;
            return true;
        }

        private static void MarkRect(Rect r)
        {
            int x0 = Mathf.Max(0, Mathf.FloorToInt(r.xMin + OccEpsilon + WallHalf)), x1 = Mathf.Min(OccSize, Mathf.CeilToInt(r.xMax - OccEpsilon + WallHalf));
            int z0 = Mathf.Max(0, Mathf.FloorToInt(r.yMin + OccEpsilon + WallHalf)), z1 = Mathf.Min(OccSize, Mathf.CeilToInt(r.yMax - OccEpsilon + WallHalf));
            for (int x = x0; x < x1; x++)
            for (int z = z0; z < z1; z++)
                occupied[x, z] = true;
        }

        private static Rect CenteredRect(Vector2 center, Vector2 size) => new Rect(center - size * 0.5f, size);

        // ---------------------------------------------------------------- placement helpers

        /// <summary>
        /// Instantiates a Synty prefab so its footprint is centred on (x, z) — most Sci-Fi City buildings pivot on a
        /// corner — and gives collider-less meshes a box so they block clicks and the NavMesh.
        /// </summary>
        private static GameObject Place(Transform parent, string prefabName, float x, float z, float yaw,
            float y = 0f, float scale = 1f, bool alignToBounds = true, bool collider = true)
        {
            return Spawn(parent, prefabName, new Vector3(x, y, z), Quaternion.Euler(0f, yaw, 0f), Vector3.one * scale, alignToBounds, collider);
        }

        /// <summary>Full-control placement: any rotation (tilted ruins) and non-uniform scale (wall spans).</summary>
        private static GameObject Spawn(Transform parent, string prefabName, Vector3 position, Quaternion rotation, Vector3 scale,
            bool alignToBounds = true, bool collider = true, bool castShadows = true)
        {
            if (!prefabIndex.TryGetValue(prefabName, out var path))
            {
                Debug.LogWarning($"[StarterTown] Prefab not found: {prefabName}");
                return null;
            }

            var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path), parent);
            go.transform.localScale = scale;
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            bool hasBounds = TryGetBounds(go, out Bounds local);

            go.transform.rotation = rotation;
            Vector3 offset = Vector3.zero;
            if (alignToBounds && TryGetBounds(go, out Bounds rotated)) offset = new Vector3(rotated.center.x, 0f, rotated.center.z);
            go.transform.position = position - offset;

            if (collider && hasBounds && go.GetComponentInChildren<Collider>() == null)
            {
                var box = go.AddComponent<BoxCollider>();
                box.center = Divide(local.center, scale);
                box.size = Divide(local.size, scale);
            }
            else if (!collider)
            {
                foreach (var c in go.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
            }

            if (!castShadows)
                foreach (var r in go.GetComponentsInChildren<Renderer>())
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.OccluderStatic);
            return go;
        }

        private static Vector3 Divide(Vector3 a, Vector3 b) => new Vector3(a.x / b.x, a.y / b.y, a.z / b.z);

        // ---------------------------------------------------------------- building palette (ColorBible)

        private const float PaletteNeighbourRadius = 22f;
        private static readonly List<(Vector2 Pos, string Family)> paletteUsed = new List<(Vector2, string)>();
        private static readonly Dictionary<string, List<Material>> paletteOptions = new Dictionary<string, List<Material>>();

        /// <summary>
        /// Recolours a building from the colour-bible palette (Synty atlas variants / tinted tower materials), picking a
        /// colour family no building within <see cref="PaletteNeighbourRadius"/> already uses, so neighbours never match
        /// (families, not materials: two pale towers with different window sets still read as the same colour).
        /// </summary>
        private static void ApplyPalette(GameObject go, Vector2 pos)
        {
            if (go == null) return;
            var chosen = new Dictionary<string, Material>(); // one pick per source material, shared by the object's renderers
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                if (r is ParticleSystemRenderer) continue;
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int k = 0; k < mats.Length; k++)
                {
                    var source = mats[k];
                    if (source == null) continue;
                    if (!chosen.TryGetValue(source.name, out var pick))
                    {
                        if (!paletteOptions.TryGetValue(source.name, out var options))
                            paletteOptions[source.name] = options = ColorBible.BuildingMaterials(source);
                        if (options.Count == 0) continue;
                        pick = PickUnusedNear(options, pos);
                        chosen[source.name] = pick;
                        paletteUsed.Add((pos, ColorBible.FamilyOf(pick)));
                    }
                    if (mats[k] != pick) { mats[k] = pick; changed = true; }
                }
                if (changed) r.sharedMaterials = mats;
            }
        }

        private static Material PickUnusedNear(List<Material> options, Vector2 pos)
        {
            int start = rng.Next(options.Count);
            for (int n = 0; n < options.Count; n++)
            {
                var m = options[(start + n) % options.Count];
                string family = ColorBible.FamilyOf(m);
                bool clash = false;
                foreach (var (p, used) in paletteUsed)
                    if (used == family && (p - pos).sqrMagnitude < PaletteNeighbourRadius * PaletteNeighbourRadius) { clash = true; break; }
                if (!clash) return m;
            }
            return options[start];
        }

        /// <summary>Places a prefab in a local frame (origin + yaw), e.g. the pieces of a vendor stall.</summary>
        private static GameObject PlaceLocal(Transform parent, string prefab, Vector2 origin, float frameYaw, float lx, float lz,
            float localYaw = 0f, float y = 0f, bool collider = true, bool align = true)
        {
            Vector2 p = origin + Rotate(new Vector2(lx, lz), frameYaw);
            return Place(parent, prefab, p.x, p.y, frameYaw + localYaw, y, 1f, align, collider);
        }

        /// <summary>Rotates an XZ vector by a Unity yaw (degrees, clockwise seen from above): local +Z → (sin, cos).</summary>
        private static Vector2 Rotate(Vector2 v, float yaw)
        {
            float r = yaw * Mathf.Deg2Rad, c = Mathf.Cos(r), s = Mathf.Sin(r);
            return new Vector2(v.x * c + v.y * s, -v.x * s + v.y * c);
        }

        private static float YawOf(Vector2 dir) => Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg;

        /// <summary>Prefab footprint at yaw 0 (cached), from an editor-only temporary instance.</summary>
        private static Bounds Footprint(string prefabName)
        {
            if (footprintCache.TryGetValue(prefabName, out var b)) return b;
            b = new Bounds(Vector3.zero, Vector3.one);
            if (prefabIndex.TryGetValue(prefabName, out var path))
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path));
                go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                if (TryGetBounds(go, out var found)) b = found;
                Object.DestroyImmediate(go);
            }
            footprintCache[prefabName] = b;
            return b;
        }

        private static bool TryGetBounds(GameObject go, out Bounds bounds)
        {
            bounds = default;
            bool found = false;
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                if (r is ParticleSystemRenderer) continue;
                if (!found) { bounds = r.bounds; found = true; }
                else bounds.Encapsulate(r.bounds);
            }
            return found;
        }

        private static float GroundHeight(float x, float z)
        {
            if (terrain == null) return 0f;
            return terrain.SampleHeight(new Vector3(x, 0f, z)) + terrain.transform.position.y;
        }

        private static GameObject Marker(Transform parent, string name, float x, float z, float y = 0f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(x, y, z);
            return go;
        }

        private static void Poi(Transform parent, string id, string displayName, float x, float z)
        {
            var go = Marker(parent, "POI " + displayName, x, z, GroundHeight(x, z));
            var poi = go.AddComponent<PointOfInterest>();
            var so = new SerializedObject(poi);
            so.FindProperty("poiId").stringValue = id;
            so.FindProperty("displayName").stringValue = displayName;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetLayerRecursively(GameObject go, int layer)
        {
            if (layer < 0) return;
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
        }

        private static GameObject FreshRoot(string name)
        {
            var scene = EditorSceneManager.GetActiveScene();
            foreach (var root in scene.GetRootGameObjects())
                if (root.name == name) Object.DestroyImmediate(root);
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "Create " + name);
            return go;
        }

        private static Transform Group(GameObject parent, string name) => Group(parent.transform, name);

        private static Transform Group(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        private static T Pick<T>(IReadOnlyList<T> list) => list[rng.Next(list.Count)];
        private static float Range(float min, float max) => min + (float)rng.NextDouble() * (max - min);
        private static bool Chance(double p) => rng.NextDouble() < p;

        private static void IndexPrefabs()
        {
            prefabIndex = new Dictionary<string, string>();
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", PrefabFolders))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string name = System.IO.Path.GetFileNameWithoutExtension(path);
                if (!prefabIndex.ContainsKey(name)) prefabIndex.Add(name, path);
            }
        }
    }
}
