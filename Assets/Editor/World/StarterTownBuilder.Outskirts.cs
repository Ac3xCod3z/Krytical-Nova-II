using System.Collections.Generic;
using KryticalNova.Networking;
using KryticalNova.World;
using KryticalNova.World.Portals;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Everything outside the walls: bridges where paths cross rivers, the ruined outskirts (tilted, half-sunk
    /// towers, rubble and wrecks, with four empty mob-area anchors), the skyline (a ring of intact towers in the
    /// ruins band and a far ring on the mountain rim, so the map edge reads as more city from street level, like
    /// the Synty demo), the north-island road and portal, spawn points and teleport POIs.
    /// </summary>
    public static partial class StarterTownBuilder
    {
        private static readonly string[] RuinTowers =
        {
            "SM_Bld_Background_Lrg_01", "SM_Bld_Background_Lrg_02", "SM_Bld_Background_Lrg_03", "SM_Bld_Background_Med_01",
            "SM_Bld_Background_Med_03", "SM_Bld_Background_Med_05", "SM_Bld_Background_Med_06", "SM_Bld_Background_Med_09",
            "SM_Bld_Large_01", "SM_Bld_Large_03", "SM_Bld_Large_06", "SM_Bld_Large_Part_06", "SM_Bld_Large_Part_03"
        };
        private static readonly string[] SkylineTowers =
        {
            "SM_Bld_Background_Lrg_01", "SM_Bld_Background_Lrg_02", "SM_Bld_Background_Lrg_02", "SM_Bld_Background_Lrg_03",
            "SM_Bld_Background_Lrg_03", "SM_Bld_Large_01", "SM_Bld_Large_02", "SM_Bld_Large_06", "SM_Bld_Background_Med_09"
        };
        private static readonly string[] Rubble =
        {
            "SM_Gen_Env_Rock_01", "SM_Gen_Env_Rock_02", "SM_Gen_Env_Rock_03", "SM_Gen_Env_Rock_05", "SM_Prop_RubbishPile_01",
            "SM_Prop_RubbishPile_03", "SM_Prop_Beam_01", "SM_Prop_Beam_02", "SM_Prop_Beams_01", "SM_Prop_Crate_Large_01",
            "SM_Prop_Barrier_03", "SM_Prop_Fence_01", "SM_Prop_Cylinder_01", "SM_Prop_Pipes_02"
        };
        private static readonly string[] Wrecks =
        {
            "SM_Veh_Classic_01", "SM_Veh_Retro_01", "SM_Veh_Garbage_01", "SM_Veh_Future_01", "SM_Veh_Future_Taxi_01", "SM_Veh_Armored_Truck_02"
        };

        public static readonly Vector2[] MobAreas =
        {
            new Vector2(150f, 150f), new Vector2(-150f, 150f), new Vector2(-150f, -150f), new Vector2(150f, -150f)
        };
        private static readonly string[] MobAreaIds = { "mob-ne", "mob-nw", "mob-sw", "mob-se" };

        private static readonly List<(Vector2 Pos, float Radius)> outskirtsTaken = new List<(Vector2, float)>();
        private static Dictionary<string, List<GameObject>> ruinLibrary = new Dictionary<string, List<GameObject>>();
        private const double DemolishedShare = 0.6; // share of ruin towers that use a baked Mesh Demolisher variant

        // ---------------------------------------------------------------- bridges

        private static readonly List<Vector2> bridgeCenters = new List<Vector2>();

        private static void BuildBridges(Transform parent, ZoneTerrainProfile profile)
        {
            bridgeCenters.Clear();
            outskirtsTaken.Clear();
            float halfSpan = profile.riverWidth * 0.5f + profile.riverBankWidth + 4f;
            int halfTiles = Mathf.CeilToInt(halfSpan / Tile);
            foreach (var path in profile.paths)
            foreach (var river in profile.rivers)
                for (int a = 0; a + 1 < path.points.Count; a++)
                for (int b = 0; b + 1 < river.points.Count; b++)
                {
                    if (!SegmentIntersect(path.points[a], path.points[a + 1], river.points[b], river.points[b + 1], out Vector2 hit)) continue;
                    if (bridgeCenters.Exists(c => Vector2.Distance(c, hit) < 10f)) continue;
                    bridgeCenters.Add(hit);
                    Vector2 dir = (path.points[a + 1] - path.points[a]).normalized;
                    Vector2 side = new Vector2(dir.y, -dir.x);
                    float yaw = YawOf(dir);
                    var bridge = Group(parent, $"Bridge ({hit.x:0}, {hit.y:0})");
                    float deckY = profile.pathHeight + BridgeDeckHeight;
                    for (int k = -halfTiles; k <= halfTiles; k++)
                    {
                        Vector2 c = hit + dir * (k * Tile);
                        for (int s = -1; s <= 1; s += 2)
                        {
                            Vector2 p = c + side * (s * Tile * 0.5f);
                            DeckTile(bridge, p.x, p.y, deckY, yaw);
                            Vector2 rail = c + side * (s * (Tile + 0.2f));
                            Place(bridge, "SM_Env_Railing_01", rail.x, rail.y, yaw + 90f, deckY);
                        }
                    }
                    for (int s = -1; s <= 1; s += 2)
                    {
                        Vector2 lamp = hit + side * (s * (Tile + 0.6f));
                        Place(bridge, "SM_Prop_Streetlight_01", lamp.x, lamp.y, YawOf(-side * s), deckY, 1f, alignToBounds: false);
                    }
                    outskirtsTaken.Add((hit, halfSpan + 6f));
                }
        }

        /// <summary>The "Base" tiles are only an underside; a flat tile on top gives the walkable surface.</summary>
        private static void DeckTile(Transform parent, float x, float z, float y, float yaw, string basePrefab = "SM_Env_Ground_Tile_Base_01")
        {
            Place(parent, basePrefab, x, z, yaw, y);
            var top = Place(parent, "SM_Env_Ground_Tile_01", x, z, yaw, y + 0.01f);
            if (top != null) SetLayerRecursively(top, groundLayer);
        }

        private static bool SegmentIntersect(Vector2 p1, Vector2 p2, Vector2 q1, Vector2 q2, out Vector2 hit)
        {
            hit = default;
            Vector2 r = p2 - p1, s = q2 - q1;
            float denom = r.x * s.y - r.y * s.x;
            if (Mathf.Abs(denom) < 1e-5f) return false;
            Vector2 qp = q1 - p1;
            float t = (qp.x * s.y - qp.y * s.x) / denom;
            float u = (qp.x * r.y - qp.y * r.x) / denom;
            if (t < 0f || t > 1f || u < 0f || u > 1f) return false;
            hit = p1 + r * t;
            return true;
        }

        // ---------------------------------------------------------------- ruins

        private static void BuildRuins(Transform parent, ZoneTerrainProfile profile)
        {
            float half = profile.size * 0.5f;
            int placed = 0;

            // Mob areas first, so each gets a dense ruin field around its anchor.
            var mobRoot = Group(parent, "Mob Areas");
            for (int m = 0; m < MobAreas.Length; m++)
            {
                Vector2 c = MobAreas[m];
                // Empty anchor for a future mob spawner (no collider: it must not catch click raycasts).
                Marker(mobRoot, $"MobArea_{MobAreaIds[m]}", c.x, c.y, GroundHeight(c.x, c.y));
                for (int k = 0; k < 7; k++)
                {
                    Vector2 p = c + Random2(22f);
                    if (OutskirtsClear(profile, p, 7f)) RuinCluster(Group(parent, $"Ruin {++placed}"), p);
                }
            }

            // The ruins band around the walls, the north island and the east bank, then sparse wrecks further out.
            Scatter(parent, profile, 70, 6f, p => Square(p) < profile.ruinsHalfSize, ref placed);
            Scatter(parent, profile, 40, 6f, p => p.y > 240f && Square(p) < half - profile.rimWidth, ref placed);
            Scatter(parent, profile, 12, 6f, p => p.x > 255f && p.y < 190f && Square(p) < half - profile.rimWidth, ref placed);
            Scatter(parent, profile, 30, 7f, p => Square(p) < half - profile.rimWidth, ref placed);

            var wrecks = Group(parent, "Field Wrecks");
            for (int attempt = 0, n = 0; attempt < 400 && n < 24; attempt++)
            {
                Vector2 p = new Vector2(Range(-half, half), Range(-half, half));
                if (!OutskirtsClear(profile, p, 3f)) continue;
                Wreck(wrecks, p);
                outskirtsTaken.Add((p, 4f));
                n++;
            }

            // Island road: broken streetlights lean along the path to the portal.
            var road = Group(parent, "Island Road Lights");
            foreach (var path in profile.paths)
                for (int i = 0; i + 1 < path.points.Count; i++)
                {
                    Vector2 a = path.points[i], b = path.points[i + 1];
                    float len = Vector2.Distance(a, b);
                    Vector2 dir = (b - a) / len, side = new Vector2(dir.y, -dir.x);
                    for (float t = 14f; t < len; t += 28f)
                    {
                        Vector2 c = a + dir * t;
                        if (c.y < 240f || Vector2.Distance(c, profile.portalCenter) < 30f) continue;
                        for (int s = -1; s <= 1; s += 2)
                        {
                            Vector2 p = c + side * (s * (profile.pathWidth * 0.5f + 1.5f));
                            var rot = Quaternion.Euler(Range(-10f, 10f), YawOf(-side * s), Range(-12f, 12f));
                            var go = Spawn(road, "SM_Prop_Streetlight_01", new Vector3(p.x, GroundHeight(p.x, p.y), p.y), rot, Vector3.one, false);
                            SnapBottom(go, GroundHeight(p.x, p.y) - 0.4f);
                        }
                    }
                }
        }

        private static void Scatter(Transform parent, ZoneTerrainProfile profile, int count, float radius, System.Func<Vector2, bool> region, ref int index)
        {
            float half = profile.size * 0.5f;
            int made = 0;
            for (int attempt = 0; attempt < count * 60 && made < count; attempt++)
            {
                Vector2 p = new Vector2(Range(-half, half), Range(-half, half));
                if (!region(p) || !OutskirtsClear(profile, p, radius)) continue;
                RuinCluster(Group(parent, $"Ruin {++index}"), p);
                made++;
            }
        }

        /// <summary>One ruined tower (tilted, sunk), a ring of rubble and sometimes a wreck.</summary>
        private static void RuinCluster(Transform parent, Vector2 c)
        {
            outskirtsTaken.Add((c, 9f));
            string tower = Pick(RuinTowers);
            float scale = Range(0.7f, 1.05f);
            float ground = GroundHeight(c.x, c.y);
            if (ruinLibrary.TryGetValue(tower, out var variants) && variants.Count > 0 && Chance(DemolishedShare))
            {
                // A baked broken tower: jagged stump plus toppled chunks, each chunk settled onto the terrain.
                var ruin = (GameObject)PrefabUtility.InstantiatePrefab(Pick(variants), parent);
                ruin.transform.SetPositionAndRotation(new Vector3(c.x, ground - 0.3f, c.y),
                    Quaternion.Euler(Range(-3f, 3f), Range(0f, 360f), Range(-3f, 3f)));
                ruin.transform.localScale = Vector3.one * scale;
                var fallenRoot = ruin.transform.Find(RuinDemolisher.FallenName);
                if (fallenRoot != null)
                    foreach (Transform piece in fallenRoot)
                        SnapBottom(piece.gameObject, GroundHeight(piece.position.x, piece.position.z) - 0.25f * scale);
                GameObjectUtility.SetStaticEditorFlags(ruin, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.OccluderStatic);
                ApplyPalette(ruin, c);
            }
            else
            {
                var rot = Quaternion.Euler(Range(-13f, 13f), Range(0f, 360f), Range(-13f, 13f));
                var go = Spawn(parent, tower, new Vector3(c.x, ground, c.y), rot, Vector3.one * scale);
                float height = Footprint(tower).size.y * scale;
                SnapBottom(go, ground - height * Range(0.06f, 0.22f));
                ApplyPalette(go, c);
            }

            int rubble = rng.Next(3, 7);
            for (int k = 0; k < rubble; k++)
            {
                Vector2 p = c + Random2(Range(4f, 9f), true);
                float g = GroundHeight(p.x, p.y);
                string prefab = Pick(Rubble);
                bool lying = prefab.Contains("Beam") || prefab.Contains("Fence") || prefab.Contains("Pipes");
                var r = Quaternion.Euler(lying ? Range(60f, 90f) : Range(-8f, 8f), Range(0f, 360f), Range(-8f, 8f));
                var piece = Spawn(parent, prefab, new Vector3(p.x, g, p.y), r, Vector3.one * Range(0.8f, 1.4f));
                SnapBottom(piece, g - 0.25f);
            }
            for (int k = 0; k < 3; k++)
            {
                Vector2 p = c + Random2(7f, true);
                Spawn(parent, "SM_Prop_Sidewalk_Rubble_01", new Vector3(p.x, GroundHeight(p.x, p.y) + 0.02f, p.y),
                      Quaternion.Euler(0f, Range(0f, 360f), 0f), Vector3.one * Range(1.5f, 3f), true, false, false);
            }
            if (Chance(0.4)) Wreck(parent, c + Random2(Range(6f, 10f), true));
        }

        private static void Wreck(Transform parent, Vector2 p)
        {
            float g = GroundHeight(p.x, p.y);
            float roll = Chance(0.5) ? 90f : (Chance(0.5) ? 180f : Range(-15f, 15f));
            var go = Spawn(parent, Pick(Wrecks), new Vector3(p.x, g, p.y), Quaternion.Euler(Range(-6f, 6f), Range(0f, 360f), roll), Vector3.one);
            SnapBottom(go, g - 0.15f);
        }

        /// <summary>Moves an object vertically so its lowest renderer point sits at <paramref name="y"/>.</summary>
        private static void SnapBottom(GameObject go, float y)
        {
            if (go == null || !TryGetBounds(go, out var b)) return;
            go.transform.position += new Vector3(0f, y - b.min.y, 0f);
        }

        private static bool OutskirtsClear(ZoneTerrainProfile p, Vector2 pos, float radius)
        {
            float square = Square(pos);
            if (square < WallHalf + 10f + radius) return false;
            if (square > p.size * 0.5f - p.rimWidth * 0.55f) return false;
            if (ZoneTerrainGenerator.DistanceToLines(p.paths, pos) < p.pathWidth * 0.5f + 4f + radius) return false;
            if (ZoneTerrainGenerator.DistanceToLines(p.rivers, pos) < p.riverWidth * 0.5f + p.riverBankWidth + 3f + radius) return false;
            foreach (var lake in p.lakes)
                if (Vector2.Distance(pos, lake.center) < lake.radius * 1.35f + p.lakeShoreWidth + radius) return false;
            if (Vector2.Distance(pos, p.portalCenter) < p.portalPadRadius + 16f + radius) return false;
            if (GroundHeight(pos.x, pos.y) < p.waterLevel + 0.6f) return false;
            foreach (var (other, r) in outskirtsTaken)
                if (Vector2.Distance(pos, other) < r + radius) return false;
            return true;
        }

        private static float Square(Vector2 p) => Mathf.Max(Mathf.Abs(p.x), Mathf.Abs(p.y));

        private static Vector2 Random2(float radius, bool exact = false)
        {
            float a = Range(0f, Mathf.PI * 2f);
            float r = exact ? radius : radius * Mathf.Sqrt((float)rng.NextDouble());
            return new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
        }

        // ---------------------------------------------------------------- skyline

        private static void BuildSkyline(Transform parent, ZoneTerrainProfile profile)
        {
            // Inner ring: intact towers standing among the ruins, the first layer of "city beyond the wall".
            var inner = Group(parent, "Inner Towers");
            for (int attempt = 0, n = 0; attempt < 3000 && n < 36; attempt++)
            {
                float half = profile.ruinsHalfSize;
                Vector2 p = new Vector2(Range(-half, half), Range(-half, half));
                if (Square(p) < 150f || !OutskirtsClear(profile, p, 6f)) continue;
                var go = Place(inner, Pick(SkylineTowers), p.x, p.y, rng.Next(4) * 90f + Range(-8f, 8f), GroundHeight(p.x, p.y) - 0.5f, Range(1f, 1.3f));
                if (go != null) { ApplyPalette(go, p); outskirtsTaken.Add((p, 8f)); n++; }
            }

            // Far ring on the mountain rim: tall silhouettes with no colliders or shadows, so the map edge reads as
            // more city from street level (below the rim line the towers fill the horizon).
            var far = Group(parent, "Far Skyline");
            float halfSize = profile.size * 0.5f;
            float[] rows = { halfSize - profile.rimWidth * 0.8f, halfSize - profile.rimWidth * 0.3f };
            for (int row = 0; row < rows.Length; row++)
            {
                float s = rows[row];
                const float spacing = 24f;
                for (int side = 0; side < 4; side++)
                for (float t = -s + (row == 0 ? 0f : spacing * 0.5f); t <= s; t += spacing)
                {
                    Vector2 p = side switch
                    {
                        0 => new Vector2(t, s),
                        1 => new Vector2(t, -s),
                        2 => new Vector2(s, t),
                        _ => new Vector2(-s, t),
                    };
                    p += new Vector2(Range(-5f, 5f), Range(-5f, 5f));
                    p = Vector2.Max(Vector2.one * -(halfSize - 4f), Vector2.Min(Vector2.one * (halfSize - 4f), p));
                    float g = GroundHeight(p.x, p.y);
                    if (g < profile.waterLevel + 0.5f) continue;
                    var tower = Spawn(far, Pick(SkylineTowers), new Vector3(p.x, g - 3f, p.y), Quaternion.Euler(0f, rng.Next(4) * 90f + Range(-10f, 10f), 0f),
                          Vector3.one * Range(1.4f, 2.3f), true, false, false);
                    ApplyPalette(tower, p);
                }
            }
        }

        // ---------------------------------------------------------------- portal

        private static void BuildPortal(Transform parent, ZoneTerrainProfile profile)
        {
            Vector2 c = profile.portalCenter;
            float padTop = profile.portalPadHeight + 0.12f;

            for (int ix = -1; ix <= 2; ix++)
            for (int iz = -1; iz <= 2; iz++)
                DeckTile(parent, c.x + (ix - 0.5f) * Tile, c.y + (iz - 0.5f) * Tile, padTop, 0f, "SM_Env_Ground_Tile_Base_02");

            // Approach direction: the last path segment into the portal.
            Vector2 approach = new Vector2(-1f, -0.4f).normalized;
            foreach (var path in profile.paths)
            {
                int n = path.points.Count;
                if (n >= 2 && Vector2.Distance(path.points[n - 1], c) < 1f) approach = (path.points[n - 2] - c).normalized;
            }
            float approachYaw = YawOf(approach);

            // Power pylons ring the pad, leaving the approach open.
            for (int i = 1; i < 8; i++)
            {
                float angle = approachYaw + i * 45f;
                float rad = angle * Mathf.Deg2Rad;
                Place(parent, "SM_Bld_Power_04", c.x + Mathf.Sin(rad) * 9.5f, c.y + Mathf.Cos(rad) * 9.5f, angle, padTop);
            }

            var portal = new GameObject("Zone Portal (North Island)");
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

        // ---------------------------------------------------------------- spawns, POIs, actors

        private static void BuildSpawns(Transform parent)
        {
            parent.gameObject.AddComponent<PlayerSpawnService>();
            var points = new[] { new Vector2(0f, -12f), new Vector2(-2.5f, -15f), new Vector2(2.5f, -15f), new Vector2(-2.5f, -19f), new Vector2(2.5f, -19f), new Vector2(0f, -8f) };
            for (int i = 0; i < points.Length; i++)
            {
                var go = new GameObject($"Spawn Point {i + 1}");
                go.transform.SetParent(parent, false);
                go.transform.SetPositionAndRotation(new Vector3(points[i].x, 0.05f, points[i].y), Quaternion.identity);
                go.AddComponent<PlayerSpawnPoint>();
            }
        }

        private static void BuildPointsOfInterest(Transform town, Transform outside, ZoneTerrainProfile profile)
        {
            Poi(town, "plaza", "Central Plaza", 0f, -9f);
            Poi(town, "elder", "Elder Hall", 0f, 8f);
            Poi(town, "yard", "Training Yard", TrainingYard.center.x, TrainingYard.yMin + 2f);
            Poi(town, "exchange", "Exchange Square", ExchangeSquare.center.x, ExchangeSquare.yMax - 2f);
            Poi(town, "gate-n", "North Gate", 0f, RingIn);
            Poi(town, "gate-s", "South Gate", 0f, -RingIn);
            Poi(town, "gate-e", "East Gate", RingIn, 0f);
            Poi(town, "gate-w", "West Gate", -RingIn, 0f);
            for (int i = 0; i < bridgeCenters.Count; i++)
                Poi(outside, $"bridge-{i + 1}", $"Bridge {i + 1}", bridgeCenters[i].x, bridgeCenters[i].y);
            Poi(outside, "island", "North Island", 100f, 300f);
            for (int m = 0; m < MobAreas.Length; m++)
                Poi(outside, MobAreaIds[m], "Mob Area " + MobAreaIds[m].Substring(4).ToUpperInvariant(), MobAreas[m].x, MobAreas[m].y - 26f);
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
                    root.transform.SetPositionAndRotation(new Vector3(TrainingYard.center.x, 1f, TrainingYard.center.y - 1f), Quaternion.Euler(0f, 180f, 0f));
                }
            }
        }
    }
}
