using System.Collections.Generic;
using KryticalNova.Networking;
using KryticalNova.World;
using KryticalNova.World.Portals;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Lays out the Zone01 starter town from a code table using only small POLYGON Sci-Fi City buildings:
    /// walls with four gates, a paved plaza and streets, vendor buildings, NPCs, the river bridge, the
    /// portal pad and the player spawn points. Each root it owns is deleted and rebuilt on every run;
    /// the terrain is never touched. Origin = town centre, +Z = north (the portal side).
    /// </summary>
    public static class StarterTownBuilder
    {
        public const string TownRootName = "--- Town ---";
        public const string NpcRootName = "--- NPCs ---";
        public const string PortalRootName = "--- Portals ---";
        public const string SpawnRootName = "--- Spawn ---";
        public const string NpcPrefabFolder = "Assets/Prefabs/NPC";

        private const float WallHalf = 50f;      // wall line; inner face ≈ 48.5
        private const float GateHalf = 6f;       // half width of each gate opening
        private const float WallSegment = 11f;   // SM_Bld_City_Wall_01 is 11.2 m long
        private const float PlazaHalf = 17.5f;
        private const float StreetHalf = 5f;
        private const float TileSize = 5f;
        private const float PavingHeight = 0.03f;
        private const float BridgeDeckHeight = 0.32f;

        private static readonly string[] PrefabFolders =
        {
            "Assets/Synty/PolygonSciFiCity/Prefabs",
            "Assets/Synty/PolygonGeneric/Prefabs"
        };

        private static Dictionary<string, string> prefabIndex;
        private static int groundLayer;

        [MenuItem("Krytical Nova/Zone/Build Starter Town")]
        public static void BuildMenu() => Build();

        public static void Build()
        {
            groundLayer = LayerMask.NameToLayer("Ground");
            IndexPrefabs();
            var profile = ZoneTerrainGenerator.LoadOrCreateProfile();

            var town = FreshRoot(TownRootName);
            BuildWalls(Group(town, "Walls"));
            BuildPaving(Group(town, "Paving"));
            BuildPlaza(Group(town, "Plaza"));
            BuildDistricts(Group(town, "Districts"));
            BuildBridge(Group(town, "North Bridge"), profile);
            BuildNpcs(FreshRoot(NpcRootName).transform);
            BuildPortal(FreshRoot(PortalRootName).transform, profile);
            BuildSpawns(FreshRoot(SpawnRootName).transform);
            PlaceExistingActors();

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log("[StarterTown] Town, NPCs, bridge, portal and spawn points built.");
        }

        // ---------------------------------------------------------------- walls

        private static void BuildWalls(Transform parent)
        {
            for (int side = 0; side < 4; side++)
            {
                // 0 = north, 1 = south, 2 = east, 3 = west
                bool alongX = side < 2;
                float line = side == 0 || side == 2 ? WallHalf : -WallHalf;
                float yaw = alongX ? 0f : 90f;
                for (int k = 0; k < 4; k++)
                {
                    float s = GateHalf + WallSegment * 0.5f + WallSegment * k;
                    foreach (float along in new[] { s, -s })
                    {
                        float x = alongX ? along : line;
                        float z = alongX ? line : along;
                        Place(parent, "SM_Bld_City_Wall_01", x, z, yaw);
                    }
                }

                // Gate towers flank every opening.
                for (int sign = -1; sign <= 1; sign += 2)
                {
                    float along = sign * (GateHalf + 2f);
                    Place(parent, "SM_Bld_City_Wall_Tower_01", alongX ? along : line, alongX ? line : along, yaw);
                }
            }

            for (int sx = -1; sx <= 1; sx += 2)
            for (int sz = -1; sz <= 1; sz += 2)
                Place(parent, "SM_Bld_City_Wall_Tower_01", sx * WallHalf, sz * WallHalf, 0f);

            // Main (north) gate beacons, echoing the light beam over the gate in the reference.
            var beacon = AssetDatabase.LoadAssetAtPath<GameObject>(PortalVfxBuilder.GateBeaconPath);
            if (beacon != null)
                for (int sign = -1; sign <= 1; sign += 2)
                {
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(beacon, parent);
                    go.transform.position = new Vector3(sign * (GateHalf + 2f), 13.4f, WallHalf);
                    go.transform.localScale = Vector3.one * 2f;
                }
        }

        // ---------------------------------------------------------------- ground

        private static void BuildPaving(Transform parent)
        {
            // Plaza: 8 × 8 tiles.
            for (float x = -PlazaHalf + TileSize * 0.5f; x < PlazaHalf; x += TileSize)
            for (float z = -PlazaHalf + TileSize * 0.5f; z < PlazaHalf; z += TileSize)
                Ground(parent, "SM_Env_Ground_Tile_01", x, z);

            // Four streets, two tiles wide, from the plaza out through each gate.
            for (float along = PlazaHalf + TileSize * 0.5f; along < WallHalf + 6f; along += TileSize)
            for (float across = -StreetHalf + TileSize * 0.5f; across < StreetHalf; across += TileSize)
            {
                Ground(parent, "SM_Env_Ground_Tile_02", across, along);
                Ground(parent, "SM_Env_Ground_Tile_02", across, -along);
                Ground(parent, "SM_Env_Ground_Tile_02", along, across);
                Ground(parent, "SM_Env_Ground_Tile_02", -along, across);
            }
        }

        private static void Ground(Transform parent, string prefab, float x, float z)
        {
            var go = Place(parent, prefab, x, z, 0f, PavingHeight);
            if (go != null) SetLayerRecursively(go, groundLayer);
        }

        // ---------------------------------------------------------------- plaza

        private static void BuildPlaza(Transform parent)
        {
            // Centre podium + hologram beacon (the reference's yellow centre square).
            Place(parent, "SM_Bld_LandingPad_01", 0f, 0f, 45f);
            var beacon = AssetDatabase.LoadAssetAtPath<GameObject>(PortalVfxBuilder.TownBeaconPath);
            if (beacon != null)
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(beacon, parent);
                go.transform.position = new Vector3(0f, 1.25f, 0f);
                go.transform.localScale = Vector3.one * 1.5f;
            }

            for (int sx = -1; sx <= 1; sx += 2)
            for (int sz = -1; sz <= 1; sz += 2)
            {
                // Streetlights on the plaza corners, arms toward the centre.
                float yaw = Mathf.Atan2(-sx, -sz) * Mathf.Rad2Deg;
                Place(parent, "SM_Prop_Streetlight_01", sx * 16f, sz * 16f, yaw, 0f, 1f, alignToBounds: false);

                // Hedges frame each corner, leaving the four street mouths open.
                Place(parent, "SM_Env_Hedge_01", sx * 11f, sz * 17.2f, 0f);
                Place(parent, "SM_Env_Hedge_01", sx * 17.2f, sz * 11f, 90f);

                // Benches face the podium.
                Place(parent, "SM_Prop_Bench_01", sx * 8f, sz * 13f, sz > 0 ? 180f : 0f);
                Place(parent, "SM_Prop_TrashCan_01", sx * 11f, sz * 13.5f, 0f);
            }
        }

        // ---------------------------------------------------------------- districts

        private static void BuildDistricts(Transform parent)
        {
            // NE — Herb Master's market.
            var ne = Group(parent, "NE Market (Herb Master)");
            Place(ne, "SM_Bld_Shop_Open_01", 27f, 26f, 180f);
            for (int i = 0; i < 3; i++)
            {
                float x = 34f + i * 5f;
                Place(ne, "SM_Prop_MarketCover_01", x, 23f, 0f);
                Place(ne, "SM_Prop_MarketTable_0" + (i + 1), x, 23.2f, 0f);
            }
            Place(ne, "SM_Prop_MarketLights_01", 39f, 25.5f, 0f);
            Place(ne, "SM_Prop_Crate_01", 46f, 28f, 20f);
            Place(ne, "SM_Prop_Crate_03", 45.5f, 31f, 0f);
            Place(ne, "SM_Bld_Background_Small_01", 44.5f, 44.5f, 0f);
            Place(ne, "SM_Bld_Background_Small_02", 37f, 46f, 0f);
            Place(ne, "SM_Building_Shack_01", 45.5f, 37f, 270f);
            Place(ne, "SM_Building_Shack_02", 24f, 45f, 180f);
            Place(ne, "SM_Bld_Background_Small_03", 30.5f, 46f, 0f);

            // NW — training yard.
            var nw = Group(parent, "NW Training Yard");
            for (int i = 0; i < 4; i++)
            {
                Place(nw, "SM_Prop_Barrier_01", -22.5f, 26f + i * 5f, 0f);
                Place(nw, "SM_Prop_Barrier_01", -44f, 26f + i * 5f, 0f);
                Place(nw, "SM_Prop_Barrier_01", -26f - i * 5f, 44.5f, 90f);
            }
            Place(nw, "SM_Prop_Crate_04", -41f, 41f, 90f);
            Place(nw, "SM_Prop_Crate_Military_01", -40f, 38f, 15f);
            Place(nw, "SM_Bld_Background_Small_04", -46.5f, 46.5f, 0f);
            Place(nw, "SM_Bld_Raised_01", -45f, 13f, 90f);

            // W — Mall Manager's food stand and vending row.
            var w = Group(parent, "W Mall Stand (Mall Manager)");
            Place(w, "SM_Bld_FoodHole_01", -26f, 10.5f, 180f);
            Place(w, "SM_Prop_VendingMachine_Soda_01", -30.5f, 10.5f, 180f);
            Place(w, "SM_Prop_VendingMachine_01", -33.5f, 9.5f, 180f);
            Place(w, "SM_Prop_VendingMachine_01", -34.7f, 9.5f, 180f);
            Place(w, "SM_Prop_Hologram_Noodles_01", -21.5f, 12.5f, 180f);
            Place(w, "SM_Bld_Background_Small_01", -45f, -12f, 0f);

            // SW — Blacksmith's chop shop.
            var sw = Group(parent, "SW Smithy (Blacksmith)");
            Place(sw, "SM_Bld_Chopshop_01", -36f, -29f, 90f);
            Place(sw, "SM_Prop_Crate_03", -25.5f, -22f, 10f);
            Place(sw, "SM_Prop_Crate_04", -25f, -35f, 90f);
            Place(sw, "SM_Prop_Crate_Military_02", -27f, -37.5f, 0f);
            Place(sw, "SM_Prop_Dumpster_01", -45f, -38f, 90f);
            Place(sw, "SM_Building_Shack_02", -45.5f, -45.5f, 45f);
            Place(sw, "SM_Bld_Background_Small_02", -27f, -46f, 0f);
            Place(sw, "SM_Building_Shack_01", -36f, -46f, 0f);

            // SE — Trader's bank and the Agent's kiosk.
            var se = Group(parent, "SE Exchange (Trader, Agent)");
            Place(se, "SM_Bld_Bank_01", 38.5f, -36.5f, 270f);
            Place(se, "SM_Prop_Bank_Booth_01", 26f, -23f, 180f);
            Place(se, "SM_Building_Shack_01", 21.5f, -46f, 0f);
            Place(se, "SM_Prop_Atm_01", 26f, -43f, 270f);
            Place(se, "SM_Prop_Atm_01", 26f, -40.5f, 270f);
            Place(se, "SM_Bld_Background_Small_01", 45f, 12f, 0f);
            Place(se, "SM_Bld_Background_Small_02", 45.5f, -13f, 90f);

            // N gate — Guard Captain's posts.
            var gate = Group(parent, "N Gate (Guard Captain)");
            Place(gate, "SM_Bld_Security_WatchStation_01", -12.5f, 44.5f, 180f);
            Place(gate, "SM_Bld_Security_WatchStation_01", 12.5f, 44.5f, 180f);
            Place(gate, "SM_Prop_Barrier_Plastic_01", -5f, 54f, 90f);
            Place(gate, "SM_Prop_Barrier_Plastic_01", 5f, 54f, 90f);

            // Reserved spots from the reference map for NPCs that come later.
            var slots = Group(parent, "Future NPC Slots");
            Marker(slots, "Slot_Elder", -12f, 33f);
            Marker(slots, "Slot_Tailor", -22f, -12f);
            Marker(slots, "Slot_GuildDirector", -20f, 20f);
            Marker(slots, "Slot_Collector", 12f, -32f);
            Marker(slots, "Slot_GateMaster", 22f, -12f);
        }

        // ---------------------------------------------------------------- bridge

        private static void BuildBridge(Transform parent, ZoneTerrainProfile profile)
        {
            // The north path crosses the river at x = 0; find the crossing from the river line.
            float riverZ = RiverZAtX(profile, 0f, 108f);
            const int length = 7;
            float start = riverZ - TileSize * (length - 1) * 0.5f;
            for (int i = 0; i < length; i++)
            {
                float z = start + i * TileSize;
                for (int sx = -1; sx <= 1; sx += 2)
                {
                    DeckTile(parent, sx * TileSize * 0.5f, z, BridgeDeckHeight, "SM_Env_Ground_Tile_Base_01");
                    Place(parent, "SM_Env_Railing_01", sx * (TileSize + 0.2f), z, 90f, BridgeDeckHeight);
                }
            }
        }

        /// <summary>The "Base" tiles are only an underside; a flat tile on top gives the walkable surface.</summary>
        private static void DeckTile(Transform parent, float x, float z, float y, string basePrefab)
        {
            Place(parent, basePrefab, x, z, 0f, y);
            var top = Place(parent, "SM_Env_Ground_Tile_01", x, z, 0f, y + 0.01f);
            if (top != null) SetLayerRecursively(top, groundLayer);
        }

        private static float RiverZAtX(ZoneTerrainProfile profile, float x, float fallback)
        {
            foreach (var river in profile.rivers)
                for (int i = 0; i + 1 < river.points.Count; i++)
                {
                    Vector2 a = river.points[i], b = river.points[i + 1];
                    if ((a.x - x) * (b.x - x) > 0f || Mathf.Approximately(a.x, b.x)) continue;
                    return Mathf.Lerp(a.y, b.y, (x - a.x) / (b.x - a.x));
                }
            return fallback;
        }

        // ---------------------------------------------------------------- NPCs

        private struct NpcSpot
        {
            public string Id;
            public float X, Z;
            public float FaceX, FaceZ;
            public NpcSpot(string id, float x, float z, float faceX, float faceZ) { Id = id; X = x; Z = z; FaceX = faceX; FaceZ = faceZ; }
        }

        private static readonly NpcSpot[] NpcSpots =
        {
            new NpcSpot("HerbMaster", 27f, 21.5f, 0f, 0f),
            new NpcSpot("Blacksmith", -29.5f, -25.5f, 0f, 0f),
            new NpcSpot("TrainerBlade", -28f, 29f, 0f, 0f),
            new NpcSpot("TrainerFist", -38f, 29f, -33f, 0f),
            new NpcSpot("Trader", 27f, -27.5f, 0f, 0f),
            new NpcSpot("MallManager", -25f, 7.5f, 0f, 0f),
            new NpcSpot("GuardCaptain", 8.5f, 40f, 0f, 25f),
            new NpcSpot("Agent", 22.5f, -40f, 0f, 0f),
        };

        private static void BuildNpcs(Transform parent)
        {
            foreach (var spot in NpcSpots)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{NpcPrefabFolder}/NPC_{spot.Id}.prefab");
                if (prefab == null) { Debug.LogWarning($"[StarterTown] Missing NPC prefab NPC_{spot.Id}; run Zone01 setup."); continue; }
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
                var position = new Vector3(spot.X, 0f, spot.Z);
                Vector3 look = new Vector3(spot.FaceX, 0f, spot.FaceZ) - position;
                go.transform.SetPositionAndRotation(position, Quaternion.LookRotation(look.sqrMagnitude > 0.01f ? look : Vector3.back));
            }
        }

        // ---------------------------------------------------------------- portal

        private static void BuildPortal(Transform parent, ZoneTerrainProfile profile)
        {
            Vector2 c = profile.portalCenter;
            float padTop = profile.portalPadHeight + 0.12f;

            for (int ix = -1; ix <= 2; ix++)
            for (int iz = -1; iz <= 2; iz++)
            {
                DeckTile(parent, c.x + (ix - 0.5f) * TileSize, c.y + (iz - 0.5f) * TileSize, padTop, "SM_Env_Ground_Tile_Base_02");
            }

            // Approach direction: the last path segment into the portal.
            Vector2 approach = new Vector2(-1f, -0.4f).normalized;
            foreach (var path in profile.paths)
            {
                int n = path.points.Count;
                if (n >= 2 && Vector2.Distance(path.points[n - 1], c) < 1f) approach = (path.points[n - 2] - c).normalized;
            }
            float approachYaw = Mathf.Atan2(approach.x, approach.y) * Mathf.Rad2Deg;

            // Power pylons ring the pad, leaving the approach open.
            for (int i = 1; i < 8; i++)
            {
                float angle = approachYaw + i * 45f;
                float rad = angle * Mathf.Deg2Rad;
                Place(parent, "SM_Bld_Power_04", c.x + Mathf.Sin(rad) * 9.5f, c.y + Mathf.Cos(rad) * 9.5f, angle, padTop);
            }

            var portal = new GameObject("Zone Portal (Northeast)");
            portal.transform.SetParent(parent, false);
            portal.transform.SetPositionAndRotation(new Vector3(c.x, padTop, c.y), Quaternion.Euler(0f, approachYaw, 0f));
            portal.AddComponent<NetworkObject>();
            var trigger = portal.AddComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.radius = 3f;
            trigger.center = new Vector3(0f, 1.5f, 0f);
            portal.AddComponent<ZonePortal>();

            var vfx = AssetDatabase.LoadAssetAtPath<GameObject>(PortalVfxBuilder.PortalPath);
            if (vfx != null)
            {
                var fx = (GameObject)PrefabUtility.InstantiatePrefab(vfx, portal.transform);
                fx.transform.localPosition = new Vector3(0f, 2.6f, 0f);
                fx.transform.localRotation = Quaternion.identity;
                fx.transform.localScale = Vector3.one * 2.2f;
            }
        }

        // ---------------------------------------------------------------- spawns

        private static void BuildSpawns(Transform parent)
        {
            parent.gameObject.AddComponent<PlayerSpawnService>();
            var points = new[] { new Vector2(0f, -12f), new Vector2(-5f, -11f), new Vector2(5f, -11f), new Vector2(-9f, -8f), new Vector2(9f, -8f), new Vector2(0f, -16f) };
            for (int i = 0; i < points.Length; i++)
            {
                var go = new GameObject($"Spawn Point {i + 1}");
                go.transform.SetParent(parent, false);
                go.transform.SetPositionAndRotation(new Vector3(points[i].x, 0.05f, points[i].y), Quaternion.identity);
                go.AddComponent<PlayerSpawnPoint>();
            }
        }

        /// <summary>Moves the offline avatar to the plaza and the combat dummy into the training yard.</summary>
        private static void PlaceExistingActors()
        {
            var scene = EditorSceneManager.GetActiveScene();
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == "Player")
                {
                    Undo.RecordObject(root.transform, "Move player");
                    root.transform.SetPositionAndRotation(new Vector3(0f, 1f, -12f), Quaternion.identity);
                }
                else if (root.name == "Combat Dummy")
                {
                    Undo.RecordObject(root.transform, "Move dummy");
                    root.transform.SetPositionAndRotation(new Vector3(-33f, 1f, 36f), Quaternion.Euler(0f, 135f, 0f));
                }
            }
        }

        // ---------------------------------------------------------------- placement helpers

        /// <summary>
        /// Instantiates a Synty prefab so its footprint is centred on (x, z) — most Sci-Fi City buildings
        /// pivot on a corner — and gives collider-less meshes a box so they block clicks and the NavMesh.
        /// </summary>
        private static GameObject Place(Transform parent, string prefabName, float x, float z, float yaw,
            float y = 0f, float scale = 1f, bool alignToBounds = true)
        {
            if (!prefabIndex.TryGetValue(prefabName, out var path))
            {
                Debug.LogWarning($"[StarterTown] Prefab not found: {prefabName}");
                return null;
            }

            var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path), parent);
            go.transform.localScale = Vector3.one * scale;
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            bool hasBounds = TryGetBounds(go, out Bounds local);

            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            Vector3 offset = Vector3.zero;
            if (alignToBounds && TryGetBounds(go, out Bounds rotated)) offset = new Vector3(rotated.center.x, 0f, rotated.center.z);
            go.transform.position = new Vector3(x, y, z) - offset;

            if (hasBounds && go.GetComponentInChildren<Collider>() == null)
            {
                var box = go.AddComponent<BoxCollider>();
                box.center = local.center / scale;
                box.size = local.size / scale;
            }

            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.OccluderStatic);
            return go;
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

        private static void Marker(Transform parent, string name, float x, float z)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(x, 0f, z);
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
