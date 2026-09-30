using System.Collections.Generic;
using UnityEngine;

namespace KryticalNova.Networking
{
    /// <summary>
    /// Zero-allocation object pool for combat impact particles, slash VFX, and projectiles.
    /// Prevents GC spikes during high player-density battles in MMORPG zone instances.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NetworkCombatVFXPool : MonoBehaviour
    {
        public static NetworkCombatVFXPool Instance { get; private set; }

        [System.Serializable]
        public struct PoolEntry
        {
            public string poolKey;
            public GameObject prefab;
            public int initialCount;
        }

        [SerializeField] private PoolEntry[] predefinedPools;

        private readonly Dictionary<string, Queue<GameObject>> pools = new Dictionary<string, Queue<GameObject>>();
        private readonly Dictionary<string, GameObject> prefabMap = new Dictionary<string, GameObject>();

        private struct PendingReturn
        {
            public string poolKey;
            public GameObject instance;
            public float returnTime;
        }

        // Timed auto-returns processed in Update (no per-spawn coroutine/WaitForSeconds allocations).
        private readonly List<PendingReturn> pendingReturns = new List<PendingReturn>();

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            InitializePools();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void InitializePools()
        {
            if (predefinedPools == null) return;

            foreach (var entry in predefinedPools)
            {
                if (string.IsNullOrEmpty(entry.poolKey) || entry.prefab == null) continue;

                prefabMap[entry.poolKey] = entry.prefab;
                var queue = new Queue<GameObject>(entry.initialCount);

                for (int i = 0; i < entry.initialCount; i++)
                {
                    var instance = Instantiate(entry.prefab, transform);
                    instance.SetActive(false);
                    queue.Enqueue(instance);
                }

                pools[entry.poolKey] = queue;
            }
        }

        /// <summary>Retrieves an instance from the pool or instantiates if empty.</summary>
        public GameObject Spawn(string poolKey, Vector3 position, Quaternion rotation)
        {
            if (pools.TryGetValue(poolKey, out var queue) && queue.Count > 0)
            {
                var instance = queue.Dequeue();
                if (instance != null)
                {
                    instance.transform.SetPositionAndRotation(position, rotation);
                    instance.SetActive(true);
                    return instance;
                }
            }

            if (prefabMap.TryGetValue(poolKey, out var prefab) && prefab != null)
            {
                var newInstance = Instantiate(prefab, position, rotation, transform);
                return newInstance;
            }

            return null;
        }

        /// <summary>Spawns a pooled instance and automatically returns it to the pool after <paramref name="lifetime"/> seconds.</summary>
        public GameObject Spawn(string poolKey, Vector3 position, Quaternion rotation, float lifetime)
        {
            var instance = Spawn(poolKey, position, rotation);
            if (instance != null)
            {
                pendingReturns.Add(new PendingReturn
                {
                    poolKey = poolKey,
                    instance = instance,
                    returnTime = Time.time + Mathf.Max(0f, lifetime)
                });
            }
            return instance;
        }

        private void Update()
        {
            if (pendingReturns.Count == 0) return;

            float now = Time.time;
            for (int i = pendingReturns.Count - 1; i >= 0; i--)
            {
                var pending = pendingReturns[i];
                if (now < pending.returnTime) continue;

                pendingReturns.RemoveAt(i);
                Despawn(pending.poolKey, pending.instance);
            }
        }

        /// <summary>Recycles an instance back into the pool.</summary>
        public void Despawn(string poolKey, GameObject instance)
        {
            if (instance == null) return;

            instance.SetActive(false);
            instance.transform.SetParent(transform);

            if (!pools.TryGetValue(poolKey, out var queue))
            {
                queue = new Queue<GameObject>();
                pools[poolKey] = queue;
            }

            queue.Enqueue(instance);
        }
    }
}
