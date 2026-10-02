using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace KryticalNova.Networking
{
    /// <summary>
    /// Server only: moves each newly connected player (the host's own included) to the next
    /// <see cref="PlayerSpawnPoint"/> before its first Start, so it never tries to attach to the NavMesh at the
    /// prefab's stored position. <see cref="OfflinePlayerHandoff"/> then moves the host's player onto the offline avatar.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerSpawnService : MonoBehaviour
    {
        private const float NavMeshSampleRadius = 3f;

        private NetworkManager subscribedManager;
        private int nextIndex;

        private void Start()
        {
            subscribedManager = NetworkManager.Singleton;
            if (subscribedManager != null)
                subscribedManager.OnClientConnectedCallback += OnClientConnected;
        }

        private void OnDestroy()
        {
            if (subscribedManager != null)
                subscribedManager.OnClientConnectedCallback -= OnClientConnected;
        }

        private void OnClientConnected(ulong clientId)
        {
            var manager = subscribedManager;
            if (manager == null || !manager.IsServer) return;
            if (!manager.ConnectedClients.TryGetValue(clientId, out var client) || client.PlayerObject == null) return;

            var points = PlayerSpawnPoint.Active;
            if (points.Count == 0) return;
            Transform point = points[nextIndex++ % points.Count].transform;
            Place(client.PlayerObject.transform, point.position, point.rotation);
        }

        private static void Place(Transform player, Vector3 position, Quaternion rotation)
        {
            if (NavMesh.SamplePosition(position, out NavMeshHit hit, NavMeshSampleRadius, NavMesh.AllAreas))
                position = hit.position;

            if (player.TryGetComponent<NavMeshAgent>(out var agent))
            {
                position += Vector3.up * agent.baseOffset;
                if (agent.isActiveAndEnabled && agent.isOnNavMesh)
                    agent.Warp(position);
            }
            player.SetPositionAndRotation(position, rotation);
        }
    }
}
