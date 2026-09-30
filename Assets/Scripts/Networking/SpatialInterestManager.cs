using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace KryticalNova.Networking
{
    /// <summary>
    /// Implements spatial Area of Interest (AOI) culling to scale MMORPG zones to large concurrent populations.
    /// Distant entities (> interestRadius) are hidden from observing clients to eliminate O(N^2) packet flooding.
    /// Registration is self-reconciling: every server tick the spawned-object list is scanned, so any
    /// NetworkObject (player, monster, NPC) is culled automatically without per-script registration calls.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SpatialInterestManager : MonoBehaviour
    {
        public static SpatialInterestManager Instance { get; private set; }

        [Header("Interest Settings")]
        [Tooltip("Entities beyond this distance are completely culled from client network replication.")]
        [SerializeField] private float interestRadius = 75f;
        [Tooltip("How frequently in seconds the spatial interest grid updates visibility.")]
        [SerializeField] private float updateInterval = 0.5f;

        [Header("Diagnostics")]
        [Tooltip("Log entity registration and every visibility change (server only).")]
        [SerializeField] private bool logCulling;

        private float nextUpdateTime;
        private readonly List<NetworkObject> registeredEntities = new List<NetworkObject>();
        private readonly HashSet<NetworkObject> registeredSet = new HashSet<NetworkObject>();

        public int RegisteredCount => registeredEntities.Count;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>Registers a spawned dynamic network entity (Player, Monster, NPC) for spatial culling.</summary>
        public void RegisterEntity(NetworkObject netObj)
        {
            if (netObj == null || !registeredSet.Add(netObj)) return;
            registeredEntities.Add(netObj);

            // Initial visibility for newly connecting observers
            netObj.CheckObjectVisibility = (clientId) => IsEntityVisibleToClient(netObj, clientId);

            if (logCulling)
                Debug.Log($"[AOI] Registered '{netObj.name}' (NetId {netObj.NetworkObjectId}, owner {netObj.OwnerClientId}).", netObj);
        }

        /// <summary>Unregisters a despawned network entity.</summary>
        public void UnregisterEntity(NetworkObject netObj)
        {
            if (netObj == null || !registeredSet.Remove(netObj)) return;
            registeredEntities.Remove(netObj);
        }

        private void Update()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsServer || Time.time < nextUpdateTime) return;
            nextUpdateTime = Time.time + updateInterval;

            ReconcileRegistrations(nm);
            UpdateAllVisibility(nm);
        }

        /// <summary>Registers any spawned NetworkObject not yet tracked. Despawned entries are pruned in UpdateAllVisibility.</summary>
        private void ReconcileRegistrations(NetworkManager nm)
        {
            var spawnManager = nm.SpawnManager;
            if (spawnManager == null) return;

            foreach (var netObj in spawnManager.SpawnedObjectsList)
            {
                if (netObj == null || registeredSet.Contains(netObj) || !IsCullable(netObj)) continue;
                RegisterEntity(netObj);
            }
        }

        /// <summary>Nested NetworkObjects follow their root's visibility, so only root objects are culled.</summary>
        private static bool IsCullable(NetworkObject netObj)
        {
            Transform parent = netObj.transform.parent;
            return parent == null || parent.GetComponentInParent<NetworkObject>() == null;
        }

        private void UpdateAllVisibility(NetworkManager nm)
        {
            float sqrRadius = interestRadius * interestRadius;

            // Prune despawned/destroyed entities once per tick
            for (int i = registeredEntities.Count - 1; i >= 0; i--)
            {
                var entity = registeredEntities[i];
                if (entity == null || !entity.IsSpawned)
                {
                    registeredEntities.RemoveAt(i);
                    registeredSet.Remove(entity);
                }
            }

            // Iterate over all connected clients
            foreach (var client in nm.ConnectedClientsList)
            {
                ulong clientId = client.ClientId;

                // The server/host always observes everything; NetworkHide cannot target it.
                if (clientId == NetworkManager.ServerClientId) continue;

                var playerObj = client.PlayerObject;
                if (playerObj == null) continue;

                Vector3 observerPos = playerObj.transform.position;

                for (int i = 0; i < registeredEntities.Count; i++)
                {
                    var entity = registeredEntities[i];

                    // Always keep the client's own object visible to itself
                    if (entity.OwnerClientId == clientId)
                    {
                        if (!entity.IsNetworkVisibleTo(clientId))
                        {
                            entity.NetworkShow(clientId);
                        }
                        continue;
                    }

                    float sqrDist = (entity.transform.position - observerPos).sqrMagnitude;
                    bool inRange = sqrDist <= sqrRadius;
                    bool isCurrentlyVisible = entity.IsNetworkVisibleTo(clientId);

                    if (inRange && !isCurrentlyVisible)
                    {
                        entity.NetworkShow(clientId);
                        if (logCulling) Debug.Log($"[AOI] SHOW '{entity.name}' to client {clientId} ({Mathf.Sqrt(sqrDist):0.0}m).", entity);
                    }
                    else if (!inRange && isCurrentlyVisible)
                    {
                        entity.NetworkHide(clientId);
                        if (logCulling) Debug.Log($"[AOI] HIDE '{entity.name}' from client {clientId} ({Mathf.Sqrt(sqrDist):0.0}m).", entity);
                    }
                }
            }
        }

        private bool IsEntityVisibleToClient(NetworkObject targetEntity, ulong observerClientId)
        {
            if (targetEntity == null || targetEntity.OwnerClientId == observerClientId) return true;
            if (observerClientId == NetworkManager.ServerClientId) return true;

            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.ConnectedClients.TryGetValue(observerClientId, out var client) || client.PlayerObject == null)
            {
                return true;
            }

            float sqrDist = (targetEntity.transform.position - client.PlayerObject.transform.position).sqrMagnitude;
            return sqrDist <= (interestRadius * interestRadius);
        }
    }
}
