using System.IO;
using System.Linq;
using KryticalNova.Hostiles;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// The test arena for Cadence (design 17): a zone to try a fight-only mechanic in without touching the starter
    /// town. It is the town's scene copied as it is now, so everything a zone needs to be played comes along
    /// (network, player, cameras, HUD, sky and weather, GM console), with the town itself, its NPCs, traffic and
    /// portals taken out, one Hostile Zone moved beside the spawn, and a NavMesh of its own. Built again from the
    /// town each time: nothing in the arena is edited by hand.
    /// </summary>
    public static class TestArenaBuilder
    {
        public const string ScenePath = "Assets/_Game/Scenes/Development/Test_Cadence.unity";
        private const string DataFolder = "Assets/_Game/Scenes/Development/Test_Cadence";

        // What the arena has no use for. Roots of the town scene, by name.
        private static readonly string[] Removed =
        {
            "--- Town ---", "--- Outskirts ---", "--- Traffic ---", "--- NPCs ---", "--- Portals ---",
            "Trade Zone - Central Plaza", "Neutral Ground - Starter Town (for testing)",
        };

        // The Hostile Zone that stays is set down this far north of the spawn: near enough to walk to, far enough
        // (with its radius) that a player who has just arrived is left alone.
        private const float HostileZoneDistance = 60f;

        [MenuItem("Krytical Nova/Zone/Build Cadence Test Arena")]
        private static void Menu() => Build();

        /// <summary>Builds the arena and puts the scene that was open back. False when it could not start.</summary>
        public static bool Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("[Arena] Leave Play first.");
                return false;
            }
            Scene open = SceneManager.GetActiveScene();
            if (open.isDirty)
            {
                Debug.LogError($"[Arena] Save or discard the changes in {open.name} first: the arena is built in its place and it is opened again after.");
                return false;
            }
            string back = open.path;

            if (File.Exists(ScenePath)) AssetDatabase.DeleteAsset(ScenePath);
            if (!AssetDatabase.CopyAsset(Zone01Setup.ScenePath, ScenePath))
            {
                Debug.LogError($"[Arena] Could not copy {Zone01Setup.ScenePath} to {ScenePath}.");
                return false;
            }

            Scene arena = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            int removed = RemoveTown(arena);
            RemoveBakedProbes(arena);
            string zone = KeepOneHostileZone();
            bool walkable = BakeNavMesh();

            EditorSceneManager.SaveScene(arena);
            BuildSceneOrder.Apply();
            AssetDatabase.SaveAssets();
            Debug.Log($"[Arena] {ScenePath} built from the town: {removed} root(s) taken out, Hostile Zone '{zone}' beside the spawn, " +
                      $"NavMesh {(walkable ? "baked" : "NOT baked")}.");

            if (!string.IsNullOrEmpty(back) && back != ScenePath) EditorSceneManager.OpenScene(back, OpenSceneMode.Single);
            return true;
        }

        private static int RemoveTown(Scene arena)
        {
            int removed = 0;
            foreach (GameObject root in arena.GetRootGameObjects())
            {
                if (!Removed.Contains(root.name)) continue;
                Object.DestroyImmediate(root);
                removed++;
            }
            return removed;
        }

        /// <summary>
        /// The town's baked light probes know its buildings, which are gone here, and belong to the town's scene.
        /// The arena is lit by the sun and the sky alone.
        /// </summary>
        private static void RemoveBakedProbes(Scene arena)
        {
            foreach (GameObject root in arena.GetRootGameObjects())
                foreach (var data in root.GetComponentsInChildren<UnityEngine.Rendering.ProbeVolumePerSceneData>(true))
                    Object.DestroyImmediate(data.gameObject);
        }

        /// <summary>Keeps the Hostile Zone of the lowest level and sets it down north of the first spawn point.</summary>
        private static string KeepOneHostileZone()
        {
            HostileZone[] zones = Object.FindObjectsByType<HostileZone>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            HostileZone kept = zones
                .Where(z => z.Definition != null && z.Definition.hostile != null)
                .OrderBy(z => z.Definition.hostile.level).ThenBy(z => z.Definition.id)
                .FirstOrDefault();
            foreach (HostileZone zone in zones)
                if (zone != kept) Object.DestroyImmediate(zone.gameObject);
            if (kept == null) return "none";

            Vector3 spawn = SpawnPoint();
            float reach = HostileZoneDistance + kept.Radius;
            Vector3 place = spawn + Vector3.forward * reach;
            Terrain terrain = Terrain.activeTerrain;
            if (terrain != null) place.y = terrain.SampleHeight(place) + terrain.transform.position.y;
            kept.transform.position = place;
            return kept.Definition.id;
        }

        private static Vector3 SpawnPoint()
        {
            GameObject header = SceneManager.GetActiveScene().GetRootGameObjects().FirstOrDefault(r => r.name == "--- Spawn ---");
            return header != null && header.transform.childCount > 0 ? header.transform.GetChild(0).position : Vector3.zero;
        }

        /// <summary>The arena's own NavMesh, in its own folder: the town's is a shared asset and is left as it is.</summary>
        private static bool BakeNavMesh()
        {
            var surface = Object.FindAnyObjectByType<NavMeshSurface>();
            if (surface == null) return false;

            surface.RemoveData();
            surface.navMeshData = null;
            surface.BuildNavMesh();
            if (surface.navMeshData == null) return false;

            WeatherVfxBuilder.EnsureFolder(DataFolder);
            string path = $"{DataFolder}/NavMesh-{surface.name}.asset";
            if (AssetDatabase.LoadMainAssetAtPath(path) != null) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(surface.navMeshData, path);
            EditorUtility.SetDirty(surface);
            return true;
        }
    }
}
