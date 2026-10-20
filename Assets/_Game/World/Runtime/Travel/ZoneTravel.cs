using System;
using System.Collections;
using KryticalNova.Networking;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using KryticalNova.Core;

namespace KryticalNova.World
{
    /// <summary>
    /// Moves the game from one map to another (gm.travel), through <see cref="MapScenes"/>: the maps are Addressables
    /// and load alone (owner, 20 Oct 2026). Offline the map is simply loaded and its own avatar picks up the save. A
    /// host saves everyone, closes its session, loads the map and opens the session again there; games that had joined
    /// it are told to join again (Netcode's scene syncing is off: one map per server, online phase 5, and travelling
    /// between map servers is milestone 3). A dedicated server runs one map and never travels. <see cref="Departing"/>
    /// is raised when a trip starts (the travel screen shows) and <see cref="Ended"/> when the new map's
    /// <see cref="ZoneRules"/> has started.
    /// </summary>
    public static class ZoneTravel
    {
        /// <summary>Seconds between the travel screen showing and the old map unloading.</summary>
        public const float LeadSeconds = 0.6f;

        /// <summary>A trip starts: the map it goes to and the catalog it is from.</summary>
        public static event Action<ZoneEntry, ZoneCatalog> Departing;
        /// <summary>A trip is over: the map arrived at, or null when it failed.</summary>
        public static event Action<ZoneEntry> Ended;

        public static bool IsTraveling { get; private set; }
        public static ZoneEntry Destination { get; private set; }

        private static Runner runner;

        // Enter Play Mode runs without a domain reload, so statics must be reset by hand.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Departing = null;
            Ended = null;
            IsTraveling = false;
            Destination = null;
            runner = null;
        }

        /// <summary>Host or offline: starts a trip to a map named by scene or by its player-facing name.</summary>
        public static bool TryTravel(string query, out string message)
        {
            ZoneRules rules = ZoneRules.Current;
            ZoneCatalog catalog = rules != null ? rules.Catalog : null;
            if (catalog == null) { message = "This scene has no zone catalog (no ZoneRules)."; return false; }
            ZoneEntry zone = catalog.Resolve(query, out string error);
            if (zone == null) { message = error; return false; }
            if (IsTraveling) { message = $"Already travelling to {Destination?.displayName}."; return false; }
            if (string.Equals(rules.gameObject.scene.name, zone.sceneName, StringComparison.OrdinalIgnoreCase))
            {
                message = $"Already in {zone.displayName}.";
                return false;
            }
            if (!MapScenes.Exists(zone.sceneName))
            {
                message = $"{zone.sceneName} is not a map of this game (Krytical Nova ▸ World ▸ Make Maps Addressable adds it).";
                return false;
            }

            var nm = NetworkManager.Singleton;
            bool online = nm != null && nm.IsListening;
            if (online && !nm.IsServer) { message = "Only the host can move the session to another map."; return false; }
#if UNITY_SERVER
            message = "A dedicated server runs one map (its -map); players reach another map on that map's server.";
            return false;
#else

            Begin(zone, catalog);
            EnsureRunner().StartCoroutine(Load(zone, online));
            message = online ? $"Moving the session to {zone.displayName}: joined players are asked to join again." : $"Travelling to {zone.displayName}.";
            return true;
#endif
        }

        /// <summary>Called by the new map's <see cref="ZoneRules"/> once its objects have started.</summary>
        internal static void SceneReady(ZoneRules rules)
        {
            EnsureRunner();
            var nm = NetworkManager.Singleton;
            if (nm != null && nm.IsListening)
            {
                // A map loaded under a running session: the players came along from the last one.
                if (nm.IsServer) runner.StartCoroutine(PlacePlayers(nm));
                RebindLocalPlayer();
            }
            if (!IsTraveling) return;
            IsTraveling = false;
            Destination = null;
            Ended?.Invoke(rules.Entry);
        }

        private static void Begin(ZoneEntry zone, ZoneCatalog catalog)
        {
            IsTraveling = true;
            Destination = zone;
            Departing?.Invoke(zone, catalog);
        }

        private static void Fail(string why)
        {
            Debug.LogError($"[ZoneTravel] {why}");
            IsTraveling = false;
            Destination = null;
            Ended?.Invoke(null);
        }

        private static IEnumerator Load(ZoneEntry zone, bool online)
        {
            yield return new WaitForSecondsRealtime(LeadSeconds);
            var nm = NetworkManager.Singleton;
            bool hosting = false, serving = false;
            if (online)
            {
                if (nm == null || !nm.IsListening || !nm.IsServer) { Fail("The session ended before the trip."); yield break; }
                // Everyone saved, the joined games told, then the session closes: the map it opens on next is loaded alone.
                hosting = nm.IsHost;
                serving = !hosting;
                ServerHooks.Saves?.FlushAll();
                foreach (ulong client in new System.Collections.Generic.List<ulong>(nm.ConnectedClientsIds))
                    if (client != NetworkManager.ServerClientId)
                        nm.DisconnectClient(client, $"The host moved the session to {zone.displayName}. Join again to follow.");
                nm.Shutdown();
                while (nm != null && nm.ShutdownInProgress) yield return null;
            }
            MapScenes.Load load = MapScenes.Begin(zone.sceneName);
            while (!load.Ready) yield return null;
            if (load.Error != null) { Fail(load.Error); yield break; }
            if (!hosting && !serving) yield break;
            // The new map has started (its objects placed, its offline avatar there): the session opens again on it.
            while (SceneManager.GetActiveScene().name != zone.sceneName) yield return null;
            yield return null;
            var session = KryticalNova.Networking.NetworkConnectionManager.Instance;
            bool started = session != null && (hosting ? session.StartLocalHost() : session.StartLocalServer());
            if (!started) Debug.LogError($"[ZoneTravel] The session could not open again on {zone.displayName}.");
        }

        /// <summary>Server: every player onto the new map's spawn points, and onto its NavMesh.</summary>
        private static IEnumerator PlacePlayers(NetworkManager nm)
        {
            // One frame, so the map's NavMesh is in place.
            yield return null;
            if (nm == null || !nm.IsServer) yield break;
            foreach (NetworkClient client in nm.ConnectedClientsList)
            {
                NetworkObject player = client.PlayerObject;
                if (player == null) continue;
                PlayerSpawnService.PlaceSpawnedPlayer(player.transform);
                PutOnNavMesh(player.transform);
            }
        }

        private static void PutOnNavMesh(Transform player)
        {
            if (!player.TryGetComponent<NavMeshAgent>(out var agent) || !agent.isActiveAndEnabled || agent.isOnNavMesh) return;
            agent.Warp(player.position);
        }

        /// <summary>This machine's networked player: camera, occluder fade and NavMesh of the new map.</summary>
        private static void RebindLocalPlayer()
        {
            foreach (IPlayerBody player in PlayerBodies.All)
                if (player != null && player.IsLocal) player.RebindToScene();
        }

        private static Runner EnsureRunner()
        {
            if (runner != null) return runner;
            var go = new GameObject("Zone Travel") { hideFlags = HideFlags.HideInHierarchy };
            UnityEngine.Object.DontDestroyOnLoad(go);
            runner = go.AddComponent<Runner>();
            return runner;
        }

        /// <summary>Runs the trips' coroutines across scene loads.</summary>
        private sealed class Runner : MonoBehaviour
        {
            private void OnDestroy()
            {
                if (runner == this) runner = null;
            }
        }
    }
}
