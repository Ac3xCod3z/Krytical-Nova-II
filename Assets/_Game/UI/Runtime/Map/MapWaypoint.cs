using UnityEngine;
using UnityEngine.SceneManagement;

namespace KryticalNova.UI
{
    /// <summary>
    /// The local player's own map marker: one per zone, placed on the world map and shown on the minimap as well.
    /// Purely local presentation (never replicated), remembered per zone in PlayerPrefs. The map controllers poll it
    /// every frame, like the other marker sources.
    /// </summary>
    public static class MapWaypoint
    {
        /// <summary>Walking this close (metres, flat) to the marker clears it.</summary>
        public const float ArrivalRadius = 3f;

        private static string loadedZone;
        // The scene the marker was read for. A scene's name is a new string every time it is asked for, and the
        // maps ask every frame: the scene is told apart as a scene, and its name read once.
        private static Scene loadedScene;
        private static bool hasWaypoint;
        private static Vector3 position;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            loadedZone = null;
            loadedScene = default;
        }

        public static bool TryGet(out Vector3 world)
        {
            EnsureLoaded();
            world = position;
            return hasWaypoint;
        }

        public static void Set(Vector3 world)
        {
            EnsureLoaded();
            hasWaypoint = true;
            position = world;
            HudPreferences.SetWaypoint(loadedZone, world);
        }

        public static void Clear()
        {
            EnsureLoaded();
            if (!hasWaypoint) return;
            hasWaypoint = false;
            HudPreferences.SetWaypoint(loadedZone, null);
        }

        /// <summary>Clears the marker once the player stands on it.</summary>
        public static void ClearIfReached(Vector3 playerPosition)
        {
            if (!TryGet(out Vector3 world)) return;
            Vector2 offset = new Vector2(world.x - playerPosition.x, world.z - playerPosition.z);
            if (offset.sqrMagnitude <= ArrivalRadius * ArrivalRadius) Clear();
        }

        private static void EnsureLoaded()
        {
            Scene active = SceneManager.GetActiveScene();
            if (loadedZone != null && active == loadedScene) return;
            loadedScene = active;
            string zone = active.name;
            if (zone == loadedZone) return;
            loadedZone = zone;
            hasWaypoint = HudPreferences.TryGetWaypoint(zone, out position);
        }
    }
}
