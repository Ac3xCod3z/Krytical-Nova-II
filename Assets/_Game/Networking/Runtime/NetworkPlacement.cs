using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.AI;

namespace KryticalNova.Networking
{
    /// <summary>Moves a character's root in one step on the machine that owns its movement (the server, or offline).</summary>
    public static class NetworkPlacement
    {
        /// <summary>
        /// Puts <paramref name="root"/> at a root position (NavMeshAgent base offset included). Observers snap with it
        /// instead of interpolating across the map.
        /// </summary>
        public static void Place(Transform root, Vector3 position, Quaternion rotation)
        {
            if (root.TryGetComponent<NavMeshAgent>(out var agent) && agent.isActiveAndEnabled && agent.isOnNavMesh)
                agent.Warp(position);
            root.SetPositionAndRotation(position, rotation);

            if (root.TryGetComponent<NetworkTransform>(out var netTransform) && netTransform.IsSpawned && netTransform.CanCommitToTransform)
                netTransform.Teleport(position, rotation, root.localScale);
        }
    }
}
