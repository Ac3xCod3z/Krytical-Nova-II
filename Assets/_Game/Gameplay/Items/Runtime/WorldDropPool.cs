using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace KryticalNova.Items
{
    /// <summary>
    /// Drops that are off the ground, kept to be used again: Creds, Stims and gear fall by the hundred while
    /// players hunt, and none of them creates or destroys anything once the pool is warm. On the network the
    /// drop prefab has a handler here, so a client also takes a pooled drop when the server spawns one and puts
    /// it back when the server despawns it.
    /// </summary>
    public static class WorldDropPool
    {
        private static readonly Dictionary<GameObject, Stack<WorldDrop>> free = new Dictionary<GameObject, Stack<WorldDrop>>();
        private static readonly Dictionary<WorldDrop, GameObject> prefabOf = new Dictionary<WorldDrop, GameObject>();

        /// <summary>Drops made since Play started. Does not grow once the pool is warm.</summary>
        public static int Created { get; private set; }

        /// <summary>Drops waiting in the pool.</summary>
        public static int Waiting
        {
            get
            {
                int count = 0;
                foreach (Stack<WorldDrop> stack in free.Values) count += stack.Count;
                return count;
            }
        }

        // Enter Play Mode runs without a domain reload, so statics must be reset by hand. Netcode clears its own
        // static event at SubsystemRegistration; this runs after that and before any scene object wakes.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ResetStatics()
        {
            free.Clear();
            prefabOf.Clear();
            Created = 0;
            NetworkManager.OnInstantiated -= RegisterHandlers;
            NetworkManager.OnInstantiated += RegisterHandlers;
        }

        private static void RegisterHandlers(NetworkManager manager)
        {
            foreach (NetworkPrefab entry in manager.NetworkConfig.Prefabs.Prefabs)
            {
                if (entry.Prefab == null || !entry.Prefab.TryGetComponent(out WorldDrop prefab)) continue;
                if (!manager.PrefabHandler.AddHandler(entry.Prefab, new Handler(prefab)))
                    Debug.LogWarning($"[Loot] {entry.Prefab.name} already has a prefab handler: drops will not be pooled on this peer.");
            }
        }

        /// <summary>A drop at this place, from the pool when one is waiting. It is active.</summary>
        public static WorldDrop Take(WorldDrop prefab, Vector3 position, Quaternion rotation)
        {
            Stack<WorldDrop> stack = StackOf(prefab.gameObject);
            while (stack.Count > 0)
            {
                WorldDrop waiting = stack.Pop();
                if (waiting == null) continue; // its scene was unloaded
                waiting.transform.SetPositionAndRotation(position, rotation);
                waiting.gameObject.SetActive(true);
                return waiting;
            }
            WorldDrop made = Object.Instantiate(prefab, position, rotation);
            prefabOf[made] = prefab.gameObject;
            Created++;
            return made;
        }

        /// <summary>Puts a drop away. One that did not come from the pool is destroyed.</summary>
        public static void Return(WorldDrop drop)
        {
            if (drop == null) return;
            if (!prefabOf.TryGetValue(drop, out GameObject prefab))
            {
                Object.Destroy(drop.gameObject);
                return;
            }
            if (!drop.gameObject.activeSelf) return; // already waiting
            drop.gameObject.SetActive(false);
            StackOf(prefab).Push(drop);
        }

        private static Stack<WorldDrop> StackOf(GameObject prefab)
        {
            if (!free.TryGetValue(prefab, out Stack<WorldDrop> stack))
            {
                stack = new Stack<WorldDrop>();
                free[prefab] = stack;
            }
            return stack;
        }

        /// <summary>What Netcode calls instead of Instantiate and Destroy for the drop prefab.</summary>
        private sealed class Handler : INetworkPrefabInstanceHandler
        {
            private readonly WorldDrop prefab;
            public Handler(WorldDrop prefab) => this.prefab = prefab;

            public NetworkObject Instantiate(ulong ownerClientId, Vector3 position, Quaternion rotation) =>
                Take(prefab, position, rotation).NetworkObject;

            public void Destroy(NetworkObject networkObject)
            {
                if (networkObject != null && networkObject.TryGetComponent(out WorldDrop drop)) Return(drop);
            }
        }
    }
}
