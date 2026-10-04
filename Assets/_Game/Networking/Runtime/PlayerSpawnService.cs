using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace KryticalNova.Networking
{
    /// <summary>
    /// Server only: puts each player (the host's own included) on the next <see cref="PlayerSpawnPoint"/> as it
    /// spawns, so it never tries to attach to the NavMesh at the prefab's stored position and every client is told
    /// the real position from the start. <see cref="OfflinePlayerHandoff"/> then moves the host's player onto the
    /// offline avatar.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerSpawnService : MonoBehaviour
    {
        private const float NavMeshSampleRadius = 3f;
        private const float OccupiedRadius = 1.5f;

        private static PlayerSpawnService instance;
        private int nextIndex;

        private void Awake()
        {
            instance = this;
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        /// <summary>Called on the server by a player from its OnNetworkSpawn, before its first Start.</summary>
        public static void PlaceSpawnedPlayer(Transform player)
        {
            if (instance == null || player == null) return;

            var points = PlayerSpawnPoint.Active;
            if (points.Count == 0) return;
            Transform point = instance.NextFreePoint(points, player);

            Vector3 position = point.position;
            if (NavMesh.SamplePosition(position, out NavMeshHit hit, NavMeshSampleRadius, NavMesh.AllAreas))
                position = hit.position;
            if (player.TryGetComponent<NavMeshAgent>(out var agent))
                position += Vector3.up * agent.baseOffset;

            NetworkPlacement.Place(player, position, point.rotation);
        }

        /// <summary>The next point in turn that no other player is standing on (the host starts on one of them).</summary>
        private Transform NextFreePoint(IReadOnlyList<PlayerSpawnPoint> points, Transform player)
        {
            Transform fallback = points[nextIndex % points.Count].transform;
            var nm = NetworkManager.Singleton;
            for (int tried = 0; tried < points.Count; tried++)
            {
                Transform point = points[nextIndex++ % points.Count].transform;
                if (nm == null || !IsOccupied(nm, point.position, player)) return point;
            }
            return fallback;
        }

        private static bool IsOccupied(NetworkManager nm, Vector3 position, Transform player)
        {
            foreach (var client in nm.ConnectedClientsList)
            {
                var other = client.PlayerObject;
                if (other == null || other.transform == player) continue;
                Vector3 offset = other.transform.position - position;
                offset.y = 0f;
                if (offset.sqrMagnitude < OccupiedRadius * OccupiedRadius) return true;
            }
            return false;
        }
    }
}
