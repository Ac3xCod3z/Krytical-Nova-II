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

        /// <summary>
        /// Server (or offline) only: puts a player who died back on one of this map's spawn points. Each map has its
        /// own service and points, so where a death leads is decided by the scene. False when the map has none (the
        /// player then gets up where they fell).
        /// </summary>
        public static bool Respawn(Transform player)
        {
            if (instance == null || player == null || PlayerSpawnPoint.Active.Count == 0) return false;
            PlaceSpawnedPlayer(player);
            return true;
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

        /// <summary>
        /// Server of the entity netcode (M3a-2b): a spot for a joining player's body, from this map's spawn points in turn, skipping
        /// any point one of <paramref name="occupied"/> stands on (body roots; compared on the ground plane). The position is a body
        /// root: the NavMesh point plus <paramref name="baseOffset"/> (the agent's). False when the map has no service or no points.
        /// The Netcode for GameObjects path (<see cref="PlaceSpawnedPlayer"/>) is unchanged.
        /// </summary>
        public static bool PickPoint(IReadOnlyList<Vector3> occupied, float baseOffset, out Vector3 position, out Quaternion rotation)
        {
            position = default;
            rotation = Quaternion.identity;
            if (instance == null) return false;
            var points = PlayerSpawnPoint.Active;
            if (points.Count == 0) return false;
            int index = NextFreeIndex(points.Count, i => points[i].transform.position, ref instance.nextIndex, occupied, OccupiedRadius);
            Transform point = points[index].transform;
            position = point.position;
            if (NavMesh.SamplePosition(position, out NavMeshHit hit, NavMeshSampleRadius, NavMesh.AllAreas))
                position = hit.position;
            position += Vector3.up * baseOffset;
            rotation = point.rotation;
            return true;
        }

        /// <summary>
        /// Pure part of <see cref="PickPoint"/>: the index of the next point in turn (counting on from <paramref name="next"/>) with
        /// nobody of <paramref name="occupied"/> within <paramref name="radius"/> on the ground plane; the first one tried when all are
        /// taken; -1 when there are no points.
        /// </summary>
        public static int NextFreeIndex(int count, System.Func<int, Vector3> pointAt, ref int next, IReadOnlyList<Vector3> occupied, float radius)
        {
            if (count <= 0) return -1;
            if (next < 0) next = 0;
            int fallback = next % count;
            for (int tried = 0; tried < count; tried++)
            {
                int index = next++ % count;
                if (!IsNear(pointAt(index), occupied, radius)) return index;
            }
            return fallback;
        }

        private static bool IsNear(Vector3 point, IReadOnlyList<Vector3> occupied, float radius)
        {
            if (occupied == null) return false;
            for (int i = 0; i < occupied.Count; i++)
            {
                Vector3 offset = occupied[i] - point;
                offset.y = 0f;
                if (offset.sqrMagnitude < radius * radius) return true;
            }
            return false;
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
