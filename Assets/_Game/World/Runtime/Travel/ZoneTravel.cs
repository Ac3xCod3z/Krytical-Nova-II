using System;
using System.Collections;
using KryticalNova.Networking;
using KryticalNova.Player;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace KryticalNova.World
{
    /// <summary>
    /// Moves the game from one map to another. In a session the host moves everyone at once (Netcode's scene
    /// manager loads the map on every client; the players come along and the server puts them on its spawn points);
    /// offline the map is simply loaded and its own avatar picks up the save. Every machine raises
    /// <see cref="Departing"/> when a trip starts (the travel screen shows) and <see cref="Ended"/> when the new
    /// map's <see cref="ZoneRules"/> has started. Only the host or offline player starts a trip (gm.travel).
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
            if (!Application.CanStreamedLevelBeLoaded(zone.sceneName))
            {
                message = $"{zone.sceneName} is not in the build list (Krytical Nova ▸ World ▸ Build World Scenes adds it).";
                return false;
            }

            var nm = NetworkManager.Singleton;
            bool online = nm != null && nm.IsListening;
            if (online && !nm.IsServer) { message = "Only the host can move the session to another map."; return false; }
            if (online && nm.SceneManager == null) { message = "Scene management is off on the NetworkManager."; return false; }

            Begin(zone, catalog);
            EnsureRunner().StartCoroutine(Load(zone, online));
            message = online ? $"Taking everyone to {zone.displayName}." : $"Travelling to {zone.displayName}.";
            return true;
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
            if (online)
            {
                if (nm == null || !nm.IsListening || nm.SceneManager == null) { Fail("The session ended before the trip."); yield break; }
                SceneEventProgressStatus status = nm.SceneManager.LoadScene(zone.sceneName, LoadSceneMode.Single);
                if (status != SceneEventProgressStatus.Started) Fail($"Netcode refused to load {zone.sceneName}: {status}.");
                yield break;
            }
            if (SceneManager.LoadSceneAsync(zone.sceneName, LoadSceneMode.Single) == null)
                Fail($"{zone.sceneName} could not be loaded.");
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
            foreach (PointAndClickPlayer player in PointAndClickPlayer.Active)
                if (player != null && player.IsSpawned && player.IsOwner) player.RebindToScene();
        }

        private static Runner EnsureRunner()
        {
            if (runner != null) return runner;
            var go = new GameObject("Zone Travel") { hideFlags = HideFlags.HideInHierarchy };
            UnityEngine.Object.DontDestroyOnLoad(go);
            runner = go.AddComponent<Runner>();
            return runner;
        }

        /// <summary>Runs the trips' coroutines across scene loads and hears the host start one (on clients).</summary>
        private sealed class Runner : MonoBehaviour
        {
            private NetworkSceneManager watched;

            private void Update()
            {
                var nm = NetworkManager.Singleton;
                NetworkSceneManager current = nm != null && nm.IsListening ? nm.SceneManager : null;
                if (current == watched) return;
                if (watched != null) watched.OnSceneEvent -= OnSceneEvent;
                watched = current;
                if (watched != null) watched.OnSceneEvent += OnSceneEvent;
            }

            private void OnDestroy()
            {
                if (watched != null) watched.OnSceneEvent -= OnSceneEvent;
                if (runner == this) runner = null;
            }

            private static void OnSceneEvent(SceneEvent e)
            {
                // The host started this trip itself; clients learn of it here.
                var nm = NetworkManager.Singleton;
                if (IsTraveling || nm == null || e.SceneEventType != SceneEventType.Load || e.ClientId != nm.LocalClientId) return;
                ZoneCatalog catalog = ZoneRules.Current != null ? ZoneRules.Current.Catalog : null;
                ZoneEntry zone = catalog != null ? catalog.Find(e.SceneName) : null;
                if (zone != null) Begin(zone, catalog);
            }
        }
    }
}
