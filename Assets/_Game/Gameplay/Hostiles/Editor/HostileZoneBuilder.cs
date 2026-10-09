using System.Collections.Generic;
using KryticalNova.Hostiles;
using KryticalNova.World;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

namespace KryticalNova.EditorTools.Hostiles
{
    /// <summary>
    /// The Hostile Zones of Zone01: one definition asset per zone and one object per zone in the scene, from the
    /// table below, and the rules every zone must keep. A zone holds one species at one level and stands clear
    /// of the town, its roads, the portal and the other zones. The corner and North Gate positions are exact;
    /// the three beyond the river are settled against the ground that is actually there.
    /// </summary>
    public static class HostileZoneBuilder
    {
        private const string ZoneFolder = "Assets/_Game/World/Zones/Zone01/HostileZones";
        private const string TerrainProfilePath = "Assets/_Game/World/Zones/Zone01/ZTP_Zone01.asset";
        private const string SceneName = "Zone01_StarterTown";
        private const string RootName = "Hostile Zones";
        private const string GameplayHeader = "--- Gameplay ---";

        // The rules (owner's plan, section 6). Metres.
        private const float WallClearance = 20f;
        private const float RoadClearance = 12f;
        private const float PortalClearance = 25f;
        private const float ZoneGap = 35f;
        private const float RoadLength = 96f;        // a gate road runs this far out from the wall before it meets water or open ground
        private const float MinWalkableShare = 0.7f;
        private const int BandLow = 1, BandHigh = 22;
        private const int MaxBruiserLevelsAbove = 2;
        private const float SettleReach = 70f, SettleStep = 10f;

        private struct Row
        {
            public string Id, Name, Species, Bruiser;
            public Vector2 Centre;
            public float Radius;
            public int MaxAlive;
            /// <summary>True where the position came from a map estimate and may be moved to fit the ground.</summary>
            public bool Settle;
        }

        // Out of the north gate into level 1, round the town anticlockwise, over the bridge, and up to the portal.
        private static readonly Row[] Zones =
        {
            new Row { Id = "north-gate", Name = "Hostile Zone North Gate", Species = "Bonewalker", Centre = new Vector2(-50f, 157f), Radius = 25f, MaxAlive = 10 },
            new Row { Id = "mob-nw", Name = "Hostile Zone NW", Species = "Blightborn", Centre = new Vector2(-150f, 150f), Radius = 30f, MaxAlive = 12 },
            new Row { Id = "mob-sw", Name = "Hostile Zone SW", Species = "BlightReaver", Centre = new Vector2(-150f, -150f), Radius = 30f, MaxAlive = 12 },
            new Row { Id = "mob-se", Name = "Hostile Zone SE", Species = "Gargoyle", Bruiser = "Hellion", Centre = new Vector2(150f, -150f), Radius = 30f, MaxAlive = 12 },
            new Row { Id = "mob-ne", Name = "Hostile Zone NE", Species = "Wraith", Centre = new Vector2(150f, 150f), Radius = 30f, MaxAlive = 12 },
            new Row { Id = "island-west", Name = "Hostile Zone Island West", Species = "SeraphUnit", Centre = new Vector2(-120f, 310f), Radius = 30f, MaxAlive = 14, Settle = true },
            new Row { Id = "island-north", Name = "Hostile Zone Island North", Species = "CarapaceRaider", Bruiser = "Colossus", Centre = new Vector2(40f, 380f), Radius = 30f, MaxAlive = 14, Settle = true },
            new Row { Id = "portal-approach", Name = "Hostile Zone Portal Approach", Species = "WarFrame", Centre = new Vector2(330f, 300f), Radius = 30f, MaxAlive = 14, Settle = true },
        };

        [MenuItem("Krytical Nova/Hostiles/Build Hostile Zones (Zone01 Open)")]
        public static void BuildOpenScene()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.name != SceneName) { Debug.LogError($"[Hostile Zones] Open {SceneName} first."); return; }
            if (Terrain.activeTerrain == null) { Debug.LogError("[Hostile Zones] The scene has no terrain."); return; }
            HostileSettings settings = HostileAssetBuilder.EnsureSettings();
            WeatherVfxBuilder.EnsureFolder(ZoneFolder);

            GameObject root = GameObject.Find(RootName);
            if (root == null)
            {
                root = new GameObject(RootName);
                GameObject header = GameObject.Find(GameplayHeader);
                if (header != null) root.transform.SetParent(header.transform, false);
            }

            var placed = new List<HostileZoneDefinition>();
            foreach (Row row in Zones)
            {
                HostileNet species = HostileAssetBuilder.PrefabOf(row.Species);
                if (species == null) { Debug.LogError($"[Hostile Zones] {row.Id}: species {row.Species} is not built. Run Build Hostiles first."); continue; }
                HostileNet bruiser = string.IsNullOrEmpty(row.Bruiser) ? null : HostileAssetBuilder.PrefabOf(row.Bruiser);

                string path = $"{ZoneFolder}/HZ_{row.Id}.asset";
                var definition = AssetDatabase.LoadAssetAtPath<HostileZoneDefinition>(path);
                bool isNew = definition == null;
                if (isNew)
                {
                    definition = ScriptableObject.CreateInstance<HostileZoneDefinition>();
                    AssetDatabase.CreateAsset(definition, path);
                }
                definition.id = row.Id;
                definition.displayName = row.Name;
                definition.radius = row.Radius;
                definition.maxAlive = row.MaxAlive;
                definition.hostile = species.Definition;
                definition.bruiser = bruiser != null ? bruiser.Definition : null;
                // A position that was settled, or moved by hand in the asset, is kept on a rebuild.
                if (isNew) definition.centre = row.Settle ? Settle(row, placed) : row.Centre;
                EditorUtility.SetDirty(definition);
                placed.Add(definition);

                Transform existing = root.transform.Find(row.Id);
                GameObject go = existing != null ? existing.gameObject : new GameObject(row.Id);
                go.transform.SetParent(root.transform, false);
                go.transform.position = World(definition.centre);
                HostileZone zone = go.TryGetComponent(out HostileZone had) ? had : go.AddComponent<HostileZone>();
                var so = new SerializedObject(zone);
                so.FindProperty("definition").objectReferenceValue = definition;
                so.FindProperty("settings").objectReferenceValue = settings;
                so.FindProperty("hostilePrefab").objectReferenceValue = species;
                so.FindProperty("bruiserPrefab").objectReferenceValue = bruiser;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);

            List<string> problems = Validate();
            if (problems.Count == 0) Debug.Log($"[Hostile Zones] {placed.Count} zones placed; every rule holds. Save the scene to keep them.");
            else Debug.LogError($"[Hostile Zones] {placed.Count} zones placed, {problems.Count} problem(s):\n" + string.Join("\n", problems));
        }

        [MenuItem("Krytical Nova/World/Validate Hostile Zones")]
        public static void ValidateMenu()
        {
            List<string> problems = Validate();
            if (problems.Count == 0) Debug.Log("[Hostile Zones] Every rule holds.");
            else Debug.LogError($"[Hostile Zones] {problems.Count} problem(s):\n" + string.Join("\n", problems));
        }

        // ------------------------------------------------------------------ ground

        private static Vector3 World(Vector2 map)
        {
            Terrain terrain = Terrain.activeTerrain;
            float y = terrain != null ? terrain.SampleHeight(new Vector3(map.x, 0f, map.y)) + terrain.transform.position.y : 0f;
            return new Vector3(map.x, y, map.y);
        }

        private static ZoneTerrainProfile Profile() => AssetDatabase.LoadAssetAtPath<ZoneTerrainProfile>(TerrainProfilePath);

        /// <summary>The share of a zone's ground (centre and two rings) that is on the NavMesh and out of the water.</summary>
        private static float WalkableShare(Vector2 centre, float radius)
        {
            ZoneTerrainProfile profile = Profile();
            float water = profile != null ? profile.waterLevel + 0.2f : -1f;
            int good = 0, total = 0;
            for (int ring = 0; ring <= 2; ring++)
            {
                int steps = ring == 0 ? 1 : 12;
                for (int i = 0; i < steps; i++)
                {
                    float a = i * Mathf.PI * 2f / steps;
                    Vector3 point = World(centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (radius * ring * 0.5f));
                    total++;
                    if (point.y > water && NavMesh.SamplePosition(point, out NavMeshHit hit, 2f, NavMesh.AllAreas) && hit.position.y > water) good++;
                }
            }
            return total > 0 ? good / (float)total : 0f;
        }

        /// <summary>The nearest spot to a row's estimate that keeps every rule against the zones placed so far.</summary>
        private static Vector2 Settle(Row row, List<HostileZoneDefinition> placed)
        {
            Vector2 best = row.Centre;
            float bestDistance = float.MaxValue;
            for (float dx = -SettleReach; dx <= SettleReach; dx += SettleStep)
            {
                for (float dy = -SettleReach; dy <= SettleReach; dy += SettleStep)
                {
                    Vector2 candidate = row.Centre + new Vector2(dx, dy);
                    float distance = new Vector2(dx, dy).magnitude;
                    if (distance >= bestDistance) continue;
                    if (PlacementProblem(candidate, row.Radius, placed, null) != null) continue;
                    best = candidate;
                    bestDistance = distance;
                }
            }
            if (bestDistance > 0.5f && bestDistance < float.MaxValue)
                Debug.Log($"[Hostile Zones] {row.Id} moved {bestDistance:0} m from its estimate to ({best.x:0}, {best.y:0}) to fit the ground.");
            if (bestDistance == float.MaxValue)
                Debug.LogWarning($"[Hostile Zones] {row.Id}: no spot within {SettleReach:0} m of ({row.Centre.x:0}, {row.Centre.y:0}) keeps the rules. Left at the estimate.");
            return best;
        }

        // ------------------------------------------------------------------ rules

        /// <summary>The first placement rule a circle breaks, or null. <paramref name="self"/> is left out of the gap check.</summary>
        private static string PlacementProblem(Vector2 centre, float radius, List<HostileZoneDefinition> others, HostileZoneDefinition self)
        {
            float wall = StarterTownBuilder.WallHalf;
            // Distance from the circle's edge to the walled square.
            float outsideX = Mathf.Max(0f, Mathf.Abs(centre.x) - wall), outsideY = Mathf.Max(0f, Mathf.Abs(centre.y) - wall);
            float toWall = Mathf.Sqrt(outsideX * outsideX + outsideY * outsideY) - radius;
            if (Mathf.Abs(centre.x) < wall && Mathf.Abs(centre.y) < wall) return "is inside the town (neutral ground)";
            if (toWall < WallClearance) return $"is {toWall:0} m from the town wall (needs {WallClearance:0})";

            // The four gate roads run straight out from the middle of each wall.
            foreach (Vector2 dir in new[] { Vector2.up, Vector2.down, Vector2.left, Vector2.right })
            {
                float along = Mathf.Clamp(Vector2.Dot(centre, dir), wall, wall + RoadLength);
                float toRoad = (centre - dir * along).magnitude - radius;
                if (toRoad < RoadClearance) return $"is {toRoad:0} m from a gate road (needs {RoadClearance:0})";
            }

            ZoneTerrainProfile profile = Profile();
            if (profile != null)
            {
                float toPortal = (centre - profile.portalCenter).magnitude - radius - profile.portalPadRadius;
                if (toPortal < PortalClearance) return $"is {toPortal:0} m from the portal pad (needs {PortalClearance:0})";
            }

            foreach (HostileZoneDefinition other in others)
            {
                if (other == null || other == self) continue;
                float gap = (centre - other.centre).magnitude - radius - other.radius;
                if (gap < ZoneGap) return $"is {gap:0} m from {other.displayName} (needs {ZoneGap:0})";
            }

            float share = WalkableShare(centre, radius);
            if (share < MinWalkableShare) return $"has only {share * 100f:0}% of its ground walkable and dry (needs {MinWalkableShare * 100f:0}%)";
            return null;
        }

        /// <summary>Every broken rule of the zones in the open scene, one line each. Empty when all hold.</summary>
        public static List<string> Validate()
        {
            var problems = new List<string>();
            HostileZone[] zones = Object.FindObjectsByType<HostileZone>(FindObjectsInactive.Include);
            if (zones.Length == 0) { problems.Add("The open scene has no Hostile Zones."); return problems; }

            var definitions = new List<HostileZoneDefinition>();
            foreach (HostileZone zone in zones)
                if (zone.Definition != null) definitions.Add(zone.Definition);
            var networkPrefabs = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(GameplayPrefabsSetup.NetworkPrefabsListPath);

            foreach (HostileZone zone in zones)
            {
                HostileZoneDefinition d = zone.Definition;
                if (d == null) { problems.Add($"{zone.name}: no zone definition."); continue; }
                string name = d.displayName;

                if (d.hostile == null) problems.Add($"{name}: no species.");
                else
                {
                    if (d.hostile.tier != HostileTier.Normal) problems.Add($"{name}: its species {d.hostile.displayName} is a {d.hostile.tier}, not a Normal Hostile.");
                    if (d.hostile.isBossCandidate) problems.Add($"{name}: {d.hostile.displayName} is a boss candidate.");
                    if (d.hostile.level < BandLow || d.hostile.level > BandHigh) problems.Add($"{name}: level {d.hostile.level} is outside the map's band {BandLow} to {BandHigh}.");
                    CheckPrefab(name, d.hostile, networkPrefabs, problems);
                }
                if (d.bruiser != null)
                {
                    if (d.bruiser.tier != HostileTier.Bruiser) problems.Add($"{name}: {d.bruiser.displayName} is not a Bruiser.");
                    if (d.bruiser.isBossCandidate) problems.Add($"{name}: {d.bruiser.displayName} is a boss candidate.");
                    if (d.hostile != null && d.bruiser.level > d.hostile.level + MaxBruiserLevelsAbove)
                        problems.Add($"{name}: its Bruiser is {d.bruiser.level - d.hostile.level} levels above the zone (at most {MaxBruiserLevelsAbove}).");
                    CheckPrefab(name, d.bruiser, networkPrefabs, problems);
                }

                Vector3 at = zone.transform.position;
                if (Mathf.Abs(at.x - d.centre.x) > 0.5f || Mathf.Abs(at.z - d.centre.y) > 0.5f)
                    problems.Add($"{name}: the scene object is at ({at.x:0}, {at.z:0}) but the zone says ({d.centre.x:0}, {d.centre.y:0}). Build the zones again.");

                string placement = PlacementProblem(d.centre, d.radius, definitions, d);
                if (placement != null) problems.Add($"{name} {placement}.");
            }
            return problems;
        }

        private static void CheckPrefab(string zone, HostileDefinition species, NetworkPrefabsList networkPrefabs, List<string> problems)
        {
            if (species.prefab == null) problems.Add($"{zone}: {species.displayName} has no prefab. Run Build Hostiles.");
            else if (networkPrefabs == null || !networkPrefabs.Contains(species.prefab))
                problems.Add($"{zone}: the prefab of {species.displayName} is not a network prefab, so clients would never see it.");
        }
    }
}
