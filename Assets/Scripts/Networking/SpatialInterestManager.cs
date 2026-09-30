using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace KryticalNova.Networking
{
    /// <summary>
    /// Implements spatial Area of Interest (AOI) culling to scale MMORPG zones to large concurrent populations.
    /// Distant entities (> interestRadius) are hidden from observing clients to eliminate O(N^2) packet flooding.
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

        private float nextUpdateTime;
        private readonly List<NetworkObject> registeredEntities = new List<NetworkObject>();

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
            if (netObj == null || registeredEntities.Contains(netObj)) return;
            registeredEntities.Add(netObj);

            // Set initial visibility delegate
            netObj.CheckObjectVisibility = (clientId) => IsEntityVisibleToClient(netObj, clientId);
        }

        /// <summary>Unregisters a despawned network entity.</summary>
        public void UnregisterEntity(NetworkObject netObj)
        {
            if (netObj != null)
            {
                registeredEntities.Remove(netObj);
            }
        }

        private void Update()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsServer || Time.time < nextUpdateTime) return;
            nextUpdateTime = Time.time + updateInterval;

            UpdateAllVisibility(nm);
        }

        private void UpdateAllVisibility(NetworkManager nm)
        {
            float sqrRadius = interestRadius * interestRadius;

            // Iterate over all connected clients
            foreach (var client in nm.ConnectedClientsList)
            {
                ulong clientId = client.ClientId;
                var playerObj = client.PlayerObject;
                if (playerObj == null) continue;

                Vector3 observerPos = playerObj.transform.position;

                for (int i = registeredEntities.Count - 1; i >= 0; i--)
                {
                    var entity = registeredEntities[i];
                    if (entity == null || !entity.IsSpawned)
                    {
                        registeredEntities.RemoveAt(i);
                        continue;
                    }

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
                    }
                    else if (!inRange && isCurrentlyVisible)
                    {
                        entity.NetworkHide(clientId);
                    }
                }
            }
        }

        private bool IsEntityVisibleToClient(NetworkObject targetEntity, ulong observerClientId)
        {
            if (targetEntity.OwnerClientId == observerClientId) return true;

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
