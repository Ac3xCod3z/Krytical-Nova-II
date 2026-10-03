using System.Collections.Generic;
using KryticalNova.Items.Visuals;
using KryticalNova.Networking;
using Unity.Netcode;
using UnityEngine;

namespace KryticalNova.GameFeel
{
    /// <summary>
    /// The lasting effects on one character: buff auras (one per skill, for the buff's whole duration), the run and
    /// airborne loops, and the weapon trail. Added on demand by <see cref="GameFeelPresenter"/>, like HitReactor.
    /// Everything is pooled and purely local; when the character is disabled or despawned its effects go with it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ActorVfx : MonoBehaviour
    {
        private const float ExpiryWarningSeconds = 3f;
        private const float ExpiryPulseHz = 3f;
        private const float ExpiryPulseDepth = 0.22f;
        private const float DefaultTrailSeconds = 0.25f;

        private struct Aura
        {
            public GameObject Instance;
            public Vector3 BaseScale;
            public float EndTime;
        }

        private readonly Dictionary<string, Aura> auras = new Dictionary<string, Aura>();
        private readonly List<string> auraIds = new List<string>(); // iteration order without allocating
        private GameObject runLoop, airLoop;
        private GameObject runPrefab;
        private bool running, airborne;
        private TrailRenderer trail;
        private float trailUntil;

        /// <summary>The character's effects, created on first use. Moments reported from a child (the visuals) land on the root.</summary>
        public static ActorVfx For(Transform actor)
        {
            if (actor == null) return null;
            actor = Root(actor);
            return actor.TryGetComponent(out ActorVfx vfx) ? vfx : actor.gameObject.AddComponent<ActorVfx>();
        }

        /// <summary>The character's effects if it has any yet, otherwise null (for moments that only end something).</summary>
        public static ActorVfx Find(Transform actor) =>
            actor != null && Root(actor).TryGetComponent(out ActorVfx vfx) ? vfx : null;

        private static Transform Root(Transform actor)
        {
            var net = actor.GetComponentInParent<NetworkObject>();
            return net != null ? net.transform : actor;
        }

        // ------------------------------------------------------------------ buff auras

        public void SetAura(string skillId, GameObject prefab, Vector3 localOffset, float duration)
        {
            ClearAura(skillId, false);
            var pool = NetworkCombatVFXPool.Instance;
            if (pool == null || prefab == null) return;
            var instance = pool.SpawnAttached(prefab, transform, localOffset, Quaternion.identity);
            if (instance == null) return;
            auras[skillId] = new Aura { Instance = instance, BaseScale = instance.transform.localScale, EndTime = Time.time + duration };
            auraIds.Add(skillId);
        }

        /// <summary>Ends the skill's aura. Returns false when it had none.</summary>
        public bool ClearAura(string skillId, bool graceful = true)
        {
            if (!auras.TryGetValue(skillId, out Aura aura)) return false;
            auras.Remove(skillId);
            auraIds.Remove(skillId);
            if (aura.Instance != null)
            {
                aura.Instance.transform.localScale = aura.BaseScale;
                NetworkCombatVFXPool.Instance?.Release(aura.Instance, graceful);
            }
            return true;
        }

        // ------------------------------------------------------------------ locomotion loops

        public void SetRunning(bool value, GameObject loopPrefab)
        {
            running = value;
            runPrefab = loopPrefab;
            RefreshRunLoop();
        }

        public void SetAirborne(bool value, GameObject loopPrefab)
        {
            airborne = value;
            SetLoop(ref airLoop, airborne, loopPrefab, new Vector3(0f, 1f, 0f));
            RefreshRunLoop(); // no dust while the feet are off the ground
        }

        private void RefreshRunLoop() => SetLoop(ref runLoop, running && !airborne, runPrefab, new Vector3(0f, 0.05f, 0f));

        private void SetLoop(ref GameObject loop, bool on, GameObject prefab, Vector3 localOffset)
        {
            var pool = NetworkCombatVFXPool.Instance;
            if (on == (loop != null) || pool == null) return;
            if (on)
            {
                if (prefab != null) loop = pool.SpawnAttached(prefab, transform, localOffset, Quaternion.identity);
            }
            else
            {
                pool.Release(loop);
                loop = null;
            }
        }

        // ------------------------------------------------------------------ weapon trail

        /// <summary>Draws the blade's trail for <paramref name="seconds"/> (the cutting part of a swing).</summary>
        public void Trail(TrailRenderer prefab, float seconds)
        {
            if (prefab == null) return;
            var mount = GetComponentInChildren<WeaponVisualMount>();
            if (mount == null) return;

            if (trail == null || trail.transform.parent != mount.transform)
            {
                if (trail != null) Destroy(trail.gameObject);
                trail = Instantiate(prefab, mount.transform, false);
                trail.name = prefab.name;
                trail.gameObject.layer = mount.gameObject.layer;
                trail.emitting = false;
            }

            // The blade runs along the mount's +Z; ride the outer half so the ribbon follows the tip's arc.
            if (mount.TryGetComponent(out BoxCollider blade))
            {
                trail.transform.localPosition = blade.center + Vector3.forward * (blade.size.z * 0.2f);
                trail.widthMultiplier = Mathf.Max(0.3f, blade.size.z * 0.6f);
            }
            if (!trail.emitting) trail.Clear();
            trail.emitting = true;
            trailUntil = Time.time + (seconds > 0f ? seconds : DefaultTrailSeconds);
        }

        // ------------------------------------------------------------------ lifecycle

        private void Update()
        {
            if (trail != null && trail.emitting && Time.time >= trailUntil) trail.emitting = false;

            // A buff about to run out breathes faster and smaller, so its end never comes as a surprise.
            for (int i = 0; i < auraIds.Count; i++)
            {
                Aura aura = auras[auraIds[i]];
                if (aura.Instance == null) continue;
                float left = aura.EndTime - Time.time;
                float pulse = left < ExpiryWarningSeconds
                    ? 1f - ExpiryPulseDepth * (0.5f + 0.5f * Mathf.Sin(Time.time * ExpiryPulseHz * Mathf.PI * 2f))
                    : 1f;
                aura.Instance.transform.localScale = aura.BaseScale * pulse;
            }
        }

        private void OnDisable()
        {
            var pool = NetworkCombatVFXPool.Instance;
            foreach (var aura in auras.Values)
            {
                if (aura.Instance == null) continue;
                aura.Instance.transform.localScale = aura.BaseScale;
                if (pool != null) pool.Release(aura.Instance, false);
            }
            auras.Clear();
            auraIds.Clear();
            if (pool != null)
            {
                pool.Release(runLoop, false);
                pool.Release(airLoop, false);
            }
            runLoop = airLoop = null;
            running = airborne = false;
            if (trail != null) trail.emitting = false;
        }
    }
}
