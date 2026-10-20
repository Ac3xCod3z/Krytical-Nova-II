using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KryticalNova.Networking
{
    /// <summary>
    /// Keeps the map's own networked objects when a game joins (owner, 20 Oct 2026: the maps are Addressables, so
    /// Netcode's scene syncing is off). Without scene syncing Netcode destroys every networked object placed in the
    /// game's scenes as it joins and rebuilds each from its registered prefab: a placed one that is no prefab (a map's
    /// World Environment, the Combat Dummy, a Zone Portal) would be lost, and the scene's own settings on it with it. So
    /// before a game joins, each of those gets a handler under its own id: Netcode keeps the object instead of destroying
    /// it, and when the server spawns it, it is that very object that comes to life. Instances of registered prefabs (the
    /// offline avatar) are left to Netcode. The server and a host place their own and need none.
    /// </summary>
    public static class SceneObjectKeeper
    {
        private static readonly List<uint> Kept = new List<uint>();

        private sealed class Keep : INetworkPrefabInstanceHandler
        {
            private readonly NetworkObject placed;

            public Keep(NetworkObject placed) => this.placed = placed;

            public NetworkObject Instantiate(ulong ownerClientId, Vector3 position, Quaternion rotation)
            {
                if (placed == null) return null;
                placed.transform.SetPositionAndRotation(position, rotation);
                if (!placed.gameObject.activeSelf) placed.gameObject.SetActive(true);
                return placed;
            }

            // The map's object stays where the map put it: it goes with its map, never with the session.
            public void Destroy(NetworkObject networkObject) { }
        }

        /// <summary>Before a game joins: a handler for each networked object its loaded scenes placed that is no prefab.</summary>
        public static int KeepPlacedObjects(NetworkManager network)
        {
            Forget(network);
            if (network == null) return 0;
            var prefabs = new HashSet<uint>();
            foreach (NetworkPrefab prefab in network.NetworkConfig.Prefabs.Prefabs)
            {
                prefabs.Add(prefab.SourcePrefabGlobalObjectIdHash);
                prefabs.Add(prefab.TargetPrefabGlobalObjectIdHash);
            }
            foreach (NetworkObject placed in Object.FindObjectsByType<NetworkObject>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                Scene scene = placed.gameObject.scene;
                if (!scene.IsValid() || !scene.isLoaded || scene.buildIndex == -1 && scene.name == "DontDestroyOnLoad") continue;
                if (placed.IsSpawned || placed.GetComponent<NetworkManager>() != null) continue;
                uint id = placed.PrefabIdHash;
                if (id == 0 || prefabs.Contains(id) || Kept.Contains(id)) continue;
                if (network.PrefabHandler.AddHandler(id, new Keep(placed))) Kept.Add(id);
            }
            if (Kept.Count > 0) Debug.Log($"[Network] Joining with the map's own {Kept.Count} networked object(s) kept for the server to bring to life.");
            return Kept.Count;
        }

        /// <summary>Lets go of the handlers (a map is left, or the next join makes its own).</summary>
        public static void Forget(NetworkManager network)
        {
            if (network != null)
                foreach (uint id in Kept) network.PrefabHandler.RemoveHandler(id);
            Kept.Clear();
        }
    }
}
