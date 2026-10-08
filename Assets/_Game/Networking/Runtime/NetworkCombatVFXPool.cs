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
        private readonly Dictionary<GameObject, string> prefabKeys = new Dictionary<GameObject, string>();
        // Instances currently riding on another transform (auras, loops), by their pool key.
        private readonly Dictionary<GameObject, string> attachedKeys = new Dictionary<GameObject, string>();
        // How long each prefab plays, worked out once (asking the prefab walks its whole hierarchy).
        private readonly Dictionary<string, float> durations = new Dictionary<string, float>();

        /// <summary>Copies made ahead of time by <see cref="Prewarm"/> or the predefined pools.</summary>
        public int CreatedAhead { get; private set; }
        /// <summary>
        /// Copies that had to be created because an effect was asked for while its pool was empty. Stays at 0 when
        /// every effect was made ready ahead: the number to watch for "nothing is created during a fight".
        /// </summary>
        public int CreatedOnDemand { get; private set; }
        /// <summary>The effect the last such copy was of: what to make ready ahead when the count is not 0.</summary>
        public string LastCreatedOnDemand { get; private set; }
        /// <summary>Effects that were not shown because their pool was empty and they may not be created (<see cref="TrySpawn"/>).</summary>
        public int Skipped { get; private set; }

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
                    CreatedAhead++;
                }

                pools[entry.poolKey] = queue;
            }
        }

        /// <summary>
        /// Makes sure <paramref name="count"/> copies of an effect lie ready in its pool, creating what is missing.
        /// Call it while nothing is happening (the game feel does, a few a frame, when a scene starts), so that no
        /// effect has to be created in the middle of a fight. Returns how many it created.
        /// </summary>
        public int Prewarm(GameObject prefab, int count)
        {
            if (prefab == null || count <= 0) return 0;
            string key = KeyFor(prefab);
            if (!pools.TryGetValue(key, out var queue))
            {
                queue = new Queue<GameObject>(count);
                pools[key] = queue;
            }
            int made = 0;
            while (queue.Count < count)
            {
                var instance = Instantiate(prefab, transform);
                instance.SetActive(false);
                queue.Enqueue(instance);
                made++;
            }
            CreatedAhead += made;
            return made;
        }

        /// <summary>How many copies of an effect lie ready right now.</summary>
        public int Ready(GameObject prefab) =>
            prefab != null && prefabKeys.TryGetValue(prefab, out string key) && pools.TryGetValue(key, out var queue) ? queue.Count : 0;

        /// <summary>
        /// Shows a pooled effect only if a copy lies ready: it is never created here. For effects that may be left
        /// out when too many play at once (comic words and impact marks). Returns null when none was ready.
        /// </summary>
        public GameObject TrySpawn(GameObject prefab, Vector3 position, Quaternion rotation, float lifetime = 0f)
        {
            if (prefab == null) return null;
            string key = KeyFor(prefab);
            if (!pools.TryGetValue(key, out var queue) || queue.Count == 0)
            {
                Skipped++;
                return null;
            }
            if (lifetime <= 0f) lifetime = DurationOf(key, prefab);
            return Spawn(key, position, rotation, lifetime);
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
                    RestartParticles(instance);
                    return instance;
                }
            }

            if (prefabMap.TryGetValue(poolKey, out var prefab) && prefab != null)
            {
                var newInstance = Instantiate(prefab, position, rotation, transform);
                CreatedOnDemand++;
                LastCreatedOnDemand = prefab.name;
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

        /// <summary>
        /// Spawns a pooled instance of <paramref name="prefab"/> without needing a predefined pool entry.
        /// The prefab is registered on first use. A non-positive <paramref name="lifetime"/> uses the
        /// prefab's own particle duration so effects are never cut off early.
        /// </summary>
        public GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation, float lifetime = 0f)
        {
            if (prefab == null) return null;
            string key = KeyFor(prefab);
            if (lifetime <= 0f) lifetime = DurationOf(key, prefab);
            return Spawn(key, position, rotation, lifetime);
        }

        /// <summary>
        /// Spawns a pooled instance that rides on <paramref name="parent"/> (buff auras, run dust, a one-shot that
        /// should follow the character). With a non-negative <paramref name="lifetime"/> it returns to the pool by
        /// itself (0 = the prefab's own duration); a negative one keeps it until <see cref="Release"/>.
        /// </summary>
        public GameObject SpawnAttached(GameObject prefab, Transform parent, Vector3 localPosition, Quaternion localRotation, float lifetime = -1f)
        {
            if (prefab == null || parent == null) return null;
            string key = KeyFor(prefab);
            var instance = Spawn(key, parent.position, parent.rotation);
            if (instance == null) return null;

            instance.transform.SetParent(parent, false);
            instance.transform.SetLocalPositionAndRotation(localPosition, localRotation);
            RestartParticles(instance); // the first Spawn of a fresh instance played it at the pool's origin
            attachedKeys[instance] = key;
            if (lifetime >= 0f)
            {
                pendingReturns.Add(new PendingReturn
                {
                    poolKey = key,
                    instance = instance,
                    returnTime = Time.time + (lifetime > 0f ? lifetime : DurationOf(key, prefab)),
                });
            }
            return instance;
        }

        /// <summary>As <see cref="SpawnAttached"/>, but only if a copy lies ready: it is never created here.</summary>
        public GameObject TrySpawnAttached(GameObject prefab, Transform parent, Vector3 localPosition, Quaternion localRotation, float lifetime = -1f)
        {
            if (prefab == null || parent == null) return null;
            if (Ready(prefab) == 0)
            {
                Skipped++;
                return null;
            }
            return SpawnAttached(prefab, parent, localPosition, localRotation, lifetime);
        }

        /// <summary>
        /// Ends an instance from <see cref="SpawnAttached"/>. Gracefully: it stops emitting, stays where it is in
        /// the world while its last particles fade, then returns to the pool. Otherwise it is recycled at once.
        /// </summary>
        public void Release(GameObject instance, bool graceful = true)
        {
            if (instance == null || !attachedKeys.TryGetValue(instance, out string key)) return;
            attachedKeys.Remove(instance);
            if (!graceful)
            {
                Despawn(key, instance);
                return;
            }

            float fade = 0f;
            foreach (var ps in instance.GetComponentsInChildren<ParticleSystem>())
            {
                ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
                fade = Mathf.Max(fade, ps.main.startLifetime.constantMax);
            }
            instance.transform.SetParent(transform, true);
            pendingReturns.Add(new PendingReturn { poolKey = key, instance = instance, returnTime = Time.time + fade });
        }

        /// <summary>
        /// Ends an attached instance whose parent is being deactivated or destroyed. Unity refuses to re-parent a child
        /// during that, so the instance only stops here and returns to the pool on the next Update, if it survived.
        /// </summary>
        public void ReleaseDeferred(GameObject instance)
        {
            if (ReferenceEquals(instance, null) || !attachedKeys.TryGetValue(instance, out string key)) return;
            attachedKeys.Remove(instance);
            if (instance == null) return;
            foreach (var ps in instance.GetComponentsInChildren<ParticleSystem>())
                ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            pendingReturns.Add(new PendingReturn { poolKey = key, instance = instance, returnTime = Time.time });
        }

        private string KeyFor(GameObject prefab)
        {
            if (!prefabKeys.TryGetValue(prefab, out var key))
            {
                key = "prefab:" + prefab.name + ":" + prefabKeys.Count;
                prefabKeys[prefab] = key;
                prefabMap[key] = prefab;
            }
            return key;
        }

        private float DurationOf(string key, GameObject prefab)
        {
            if (!durations.TryGetValue(key, out float seconds))
            {
                seconds = GetParticleDuration(prefab);
                durations[key] = seconds;
            }
            return seconds;
        }

        /// <summary>
        /// How long an effect needs to play out: for each of its systems its start delay, the time of its last
        /// emission and the life of a particle, and the longest of those. A system that only bursts is done
        /// emitting long before its run is over, and a copy held for the whole run is a copy the next hit cannot
        /// have. An effect with sub-emitters is given its systems' whole runs: when those emit hangs on other
        /// particles.
        /// </summary>
        public static float GetParticleDuration(GameObject prefab)
        {
            var systems = prefab.GetComponentsInChildren<ParticleSystem>(true);
            bool wholeRun = false;
            foreach (var ps in systems)
            {
                var sub = ps.subEmitters;
                if (sub.enabled && sub.subEmittersCount > 0) wholeRun = true;
            }
            float max = 0f;
            foreach (var ps in systems)
            {
                var main = ps.main;
                float emits = main.loop ? 0f : wholeRun ? main.duration : LastEmission(ps);
                float t = main.startDelay.constantMax + emits + main.startLifetime.constantMax;
                // A trail that outlives its particle is still on screen for its own time.
                var trails = ps.trails;
                if (trails.enabled && !trails.dieWithParticles) t += main.startLifetime.constantMax * Peak(trails.lifetime);
                if (t > max) max = t;
            }
            return max > 0f ? max : 2f;
        }

        /// <summary>Seconds into its run a system emits for the last time: its whole run when it emits at a rate, its last burst when it only bursts.</summary>
        private static float LastEmission(ParticleSystem ps)
        {
            var emission = ps.emission;
            if (!emission.enabled) return 0f;
            float duration = ps.main.duration;
            if (Peak(emission.rateOverTime) > 0f || Peak(emission.rateOverDistance) > 0f) return duration;
            float last = 0f;
            for (int i = 0; i < emission.burstCount; i++)
            {
                ParticleSystem.Burst burst = emission.GetBurst(i);
                if (burst.cycleCount <= 0) return duration; // repeats for as long as the system runs
                last = Mathf.Max(last, burst.time + (burst.cycleCount - 1) * burst.repeatInterval);
            }
            return Mathf.Min(last, duration);
        }

        private static float Peak(ParticleSystem.MinMaxCurve curve)
        {
            switch (curve.mode)
            {
                case ParticleSystemCurveMode.Constant: return curve.constant;
                case ParticleSystemCurveMode.TwoConstants: return curve.constantMax;
                default: return Mathf.Abs(curve.curveMultiplier);
            }
        }

        // Reused instances must replay from t=0; re-enabling alone can resume a half-finished system,
        // whose one-shot bursts have already fired, so the effect silently shows nothing.
        private static void RestartParticles(GameObject instance)
        {
            if (!instance.TryGetComponent<ParticleSystem>(out var root))
                root = instance.GetComponentInChildren<ParticleSystem>();
            if (root == null) return;

            root.Clear(true);
            root.Play(true);
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
            // An attached instance dies with its parent: forget it even though it now compares equal to null.
            if (!ReferenceEquals(instance, null)) attachedKeys.Remove(instance);
            if (instance == null) return;

            foreach (var ps in instance.GetComponentsInChildren<ParticleSystem>())
                ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
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
