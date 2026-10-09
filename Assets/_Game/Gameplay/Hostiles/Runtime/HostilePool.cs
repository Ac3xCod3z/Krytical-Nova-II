using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace KryticalNova.Hostiles
{
    /// <summary>
    /// Hostiles that are out of the world, kept to be used again: a kill and a respawn create nothing. One pool
    /// per species, on every peer. On the network each Hostile prefab has a handler here, so a client also takes
    /// a pooled body when the server spawns one and puts it back when the server despawns it.
    /// </summary>
    public static class HostilePool
    {
        private static readonly Dictionary<GameObject, Stack<HostileNet>> free = new Dictionary<GameObject, Stack<HostileNet>>();
        private static readonly Dictionary<HostileNet, GameObject> prefabOf = new Dictionary<HostileNet, GameObject>();

        /// <summary>Hostiles made since Play started. Does not grow once the pools are warm.</summary>
        public static int Created { get; private set; }

        /// <summary>Hostiles waiting in the pools.</summary>
        public static int Waiting
        {
            get
            {
                int count = 0;
                foreach (Stack<HostileNet> stack in free.Values) count += stack.Count;
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
                if (entry.Prefab == null || !entry.Prefab.TryGetComponent(out HostileNet prefab)) continue;
                if (!manager.PrefabHandler.AddHandler(entry.Prefab, new Handler(prefab)))
                    Debug.LogWarning($"[Hostiles] {entry.Prefab.name} already has a prefab handler: it will not be pooled on this peer.");
            }
        }

        /// <summary>A Hostile of this species at this place, from the pool when one is waiting. It is active.</summary>
        public static HostileNet Take(HostileNet prefab, Vector3 position, Quaternion rotation)
        {
            Stack<HostileNet> stack = StackOf(prefab.gameObject);
            while (stack.Count > 0)
            {
                HostileNet waiting = stack.Pop();
                if (waiting == null) continue; // its scene was unloaded
                waiting.transform.SetPositionAndRotation(position, rotation);
                waiting.gameObject.SetActive(true);
                return waiting;
            }
            HostileNet made = Object.Instantiate(prefab, position, rotation);
            made.name = prefab.name;
            prefabOf[made] = prefab.gameObject;
            Created++;
            return made;
        }

        /// <summary>Puts a Hostile away. One that did not come from the pool is destroyed.</summary>
        public static void Return(HostileNet hostile)
        {
            if (hostile == null) return;
            if (!prefabOf.TryGetValue(hostile, out GameObject prefab))
            {
                Object.Destroy(hostile.gameObject);
                return;
            }
            if (!hostile.gameObject.activeSelf) return; // already waiting
            hostile.gameObject.SetActive(false);
            StackOf(prefab).Push(hostile);
        }

        /// <summary>Makes sure this many of a species are waiting, so the first fight creates nothing.</summary>
        public static void Prewarm(HostileNet prefab, int count)
        {
            if (prefab == null) return;
            Stack<HostileNet> stack = StackOf(prefab.gameObject);
            for (int i = stack.Count; i < count; i++)
            {
                HostileNet made = Object.Instantiate(prefab);
                made.gameObject.SetActive(false); // its brain is off in the prefab, so nothing has started
                made.name = prefab.name;
                prefabOf[made] = prefab.gameObject;
                Created++;
                stack.Push(made);
            }
        }

        private static Stack<HostileNet> StackOf(GameObject prefab)
        {
            if (!free.TryGetValue(prefab, out Stack<HostileNet> stack))
            {
                stack = new Stack<HostileNet>();
                free[prefab] = stack;
            }
            return stack;
        }

        /// <summary>What Netcode calls instead of Instantiate and Destroy for one Hostile prefab.</summary>
        private sealed class Handler : INetworkPrefabInstanceHandler
        {
            private readonly HostileNet prefab;
            public Handler(HostileNet prefab) => this.prefab = prefab;

            public NetworkObject Instantiate(ulong ownerClientId, Vector3 position, Quaternion rotation) =>
                Take(prefab, position, rotation).NetworkObject;

            public void Destroy(NetworkObject networkObject)
            {
                if (networkObject != null && networkObject.TryGetComponent(out HostileNet hostile)) Return(hostile);
            }
        }
    }
}
