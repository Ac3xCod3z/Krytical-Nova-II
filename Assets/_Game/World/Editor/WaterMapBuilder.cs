using System.IO;
using KryticalNova.Player;
using KryticalNova.World;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Waterstride (owner, 22 Oct 2026): bakes the open zone's <see cref="WaterMap"/>, a one-metre grid over the terrain
    /// with the cells where the water is deep enough to run on and nothing stands in it, and points the zone's catalogue
    /// entry at it. Water is flat, so this is all it needs: a few kilobytes, where the walls' graph is megabytes.
    /// </summary>
    public static class WaterMapBuilder
    {
        public const string MapFolder = "Assets/_Game/World/Data/Assets/WaterMaps";
        public const string ReportPath = "Library/KryticalNova.WaterMap.txt";
        private const string SettingsPath = "Assets/_Game/Gameplay/Player/Data/Assets/WaterstrideSettings.asset";
        private const string ZoneCatalogPath = "Assets/_Game/World/Data/Assets/ZoneCatalog.asset";
        private const string WaterSurfaceName = "Water Surface";
        private const string WaterName = "Water";

        public static string MapPath(string sceneName) => $"{MapFolder}/{sceneName}_WaterMap.asset";

        [MenuItem("Krytical Nova/Zone/Water Map/Bake Zone (Open Scene)")]
        private static void Menu() => Debug.Log(BakeZone());

        /// <summary>Bakes the open zone's water map; returns the report.</summary>
        public static string BakeZone()
        {
            string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            Terrain terrain = Terrain.activeTerrain;
            if (terrain == null) return $"[Water map] {scene} has no terrain: nothing baked.";
            if (!TryWaterLevel(out float level)) return $"[Water map] {scene} has no water: nothing baked.";
            var settings = AssetDatabase.LoadAssetAtPath<WaterstrideSettings>(SettingsPath);
            float cell = settings != null ? settings.mapCellSize : 1f;
            float minDepth = settings != null ? settings.minDepth : 0.35f;
            float body = settings != null ? settings.bodyRadius : 0.3f;

            Vector3 corner = terrain.transform.position;
            Vector3 size = terrain.terrainData.size;
            int width = Mathf.CeilToInt(size.x / cell), depth = Mathf.CeilToInt(size.z / cell);
            var water = new bool[width * depth];
            int blockers = Physics.DefaultRaycastLayers & ~(1 << LayerMask.NameToLayer("Water")) & ~(1 << LayerMask.NameToLayer("Ground"));
            int deep = 0, blocked = 0;
            for (int z = 0; z < depth; z++)
            for (int x = 0; x < width; x++)
            {
                var centre = new Vector3(corner.x + (x + 0.5f) * cell, level, corner.z + (z + 0.5f) * cell);
                float ground = terrain.SampleHeight(centre) + corner.y;
                if (ground > level - minDepth) continue;
                deep++;
                // Nothing standing in the water here: a pier, a rock, a boat, a bridge's pillar.
                if (Physics.CheckCapsule(centre + Vector3.up * (body + 0.05f), centre + Vector3.up * 1.7f, body, blockers, QueryTriggerInteraction.Ignore))
                {
                    blocked++;
                    continue;
                }
                water[z * width + x] = true;
            }

            Directory.CreateDirectory(MapFolder);
            string path = MapPath(scene);
            var map = AssetDatabase.LoadAssetAtPath<WaterMap>(path);
            if (map == null)
            {
                map = ScriptableObject.CreateInstance<WaterMap>();
                AssetDatabase.CreateAsset(map, path);
            }
            map.waterLevel = level;
            map.origin = new Vector2(corner.x, corner.z);
            map.cellSize = cell;
            map.width = width;
            map.depth = depth;
            map.Set(water, $"Baked {System.DateTime.Now:yyyy-MM-dd HH:mm} from {scene}: water at {level:0.00} m, {cell} m cells, " +
                           $"at least {minDepth} m deep, a body of {body} m must fit.");
            EditorUtility.SetDirty(map);
            UseForZone(map, scene);
            AssetDatabase.SaveAssets();

            string report = $"[Water map] {scene}: {map.WaterCells} cells to run on ({deep} deep enough, {blocked} with something standing in them), " +
                            $"grid {width} x {depth}, {map.PackedBytes / 1024f:0.0} KB. Saved {path}.";
            File.WriteAllText(ReportPath, report);
            return report;
        }

        /// <summary>The zone's water surface: the shaded surface's height, else the top of the water's collider.</summary>
        private static bool TryWaterLevel(out float level)
        {
            level = 0f;
            foreach (GameObject root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            {
                Transform surface = Find(root.transform, WaterSurfaceName);
                if (surface != null) { level = surface.position.y; return true; }
            }
            foreach (GameObject root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            {
                Transform water = Find(root.transform, WaterName);
                if (water != null && water.TryGetComponent(out Collider collider)) { level = collider.bounds.max.y; return true; }
            }
            return false;
        }

        private static Transform Find(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform child in t)
            {
                Transform found = Find(child, name);
                if (found != null) return found;
            }
            return null;
        }

        private static void UseForZone(WaterMap map, string sceneName)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ZoneCatalog>(ZoneCatalogPath);
            ZoneEntry entry = catalog != null ? catalog.Find(sceneName) : null;
            if (entry == null) { Debug.LogWarning($"[Water map] {sceneName} is not in the zone catalogue: the game will not use this map."); return; }
            if (entry.waterMap == map) return;
            entry.waterMap = map;
            EditorUtility.SetDirty(catalog);
        }
    }
}
