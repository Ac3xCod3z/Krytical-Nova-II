using System;
using UnityEngine;

namespace KryticalNova.World
{
    /// <summary>
    /// Deterministic lightning: strike times and positions come from the shared weather seed and
    /// server time, so every client sees the same strike at the same moment with zero network traffic.
    /// Uses no realtime lights — the flash is fed to <see cref="EnvironmentPresenter"/> (key light boost
    /// + sky/cloud uniform). Strike VFX are pooled.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LightningController : MonoBehaviour
    {
        private const double SlotSeconds = 1.0;

        [Header("Strike VFX (pooled, must not contain lights)")]
        [SerializeField] private ParticleSystem strikePrefab;
        [Range(1, 4)] [SerializeField] private int poolSize = 2;

        [Header("Placement")]
        [Tooltip("Strikes land in a ring around the centre of the observer's grid cell, so nearby players agree.")]
        [SerializeField] private float cellSize = 500f;
        [SerializeField] private float minDistance = 120f;
        [SerializeField] private float maxDistance = 420f;

        [Header("Flash")]
        [SerializeField] private float flashDecay = 6f;
        [Tooltip("Flash strength for the farthest strikes (nearest = 1).")]
        [Range(0f, 1f)] [SerializeField] private float farFlash = 0.35f;
        [Tooltip("Faint sheet-lightning flickers between visible strikes, as a fraction of strikes.")]
        [Range(0f, 1f)] [SerializeField] private float sheetLightningRatio = 0.5f;

        [Header("Thunder")]
        [SerializeField] private AudioSource thunderSource;
        [SerializeField] private AudioClip[] thunderClips;
        [SerializeField] private float maxThunderDelay = 4f;
        [Tooltip("How loud the thunder of the farthest strikes is (nearest = 1).")]
        [Range(0f, 1f)] [SerializeField] private float farThunder = 0.45f;

        private ParticleSystem[] pool;
        private int poolIndex;
        private long lastSlot = long.MinValue;
        private double pendingStrikeTime = -1;
        private Vector3 pendingPosition;
        private float pendingAmplitude;
        private bool pendingVisible;
        private double flashStart = double.MinValue;
        private float flashAmplitude;
        private double thunderTime = -1;
        private int thunderClip;
        private float thunderLoudness = 1f;
        private Transform cam;

        /// <summary>Current flash intensity (0-1+), read by the presenter each frame.</summary>
        public float Flash { get; private set; }

        private void Awake()
        {
            if (Application.isBatchMode)
            {
                enabled = false;
                return;
            }

            if (strikePrefab == null) return;
            pool = new ParticleSystem[poolSize];
            for (int i = 0; i < poolSize; i++)
            {
                pool[i] = Instantiate(strikePrefab, transform);
                pool[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        private void Update()
        {
            var env = WorldEnvironmentNetwork.Instance;
            if (env == null)
            {
                Flash = 0f;
                return;
            }

            double now = env.Now;
            int seed = env.State.Seed;
            float perMinute = env.Weather.lightningPerMinute;

            long slot = (long)Math.Floor(now / SlotSeconds);
            if (slot != lastSlot)
            {
                lastSlot = slot;
                if (perMinute > 0.01f && WorldEnvironmentNetwork.Hash01(seed, slot * 8) < perMinute / 60f * (float)SlotSeconds)
                    ScheduleStrike(seed, slot);
            }

            if (pendingStrikeTime >= 0 && now >= pendingStrikeTime)
            {
                pendingStrikeTime = -1;
                Strike();
            }

            if (thunderTime >= 0 && now >= thunderTime)
            {
                thunderTime = -1;
                if (thunderSource != null && thunderClips != null && thunderClips.Length > 0)
                    thunderSource.PlayOneShot(thunderClips[thunderClip % thunderClips.Length], thunderLoudness);
            }

            Flash = Envelope((float)(now - flashStart)) * flashAmplitude;
        }

        /// <summary>GM/testing: a visible, full-strength strike right now near the camera (local only).</summary>
        public void ForceStrike()
        {
            var env = WorldEnvironmentNetwork.Instance;
            if (env == null || !enabled) return;
            ScheduleStrike(env.State.Seed, lastSlot ^ 0x5A5A);
            pendingVisible = true;
            pendingAmplitude = 1f;
            pendingStrikeTime = env.Now;
        }

        private void ScheduleStrike(int seed, long slot)
        {
            if (cam == null)
            {
                var main = Camera.main;
                if (main == null) return;
                cam = main.transform;
            }

            float angle = WorldEnvironmentNetwork.Hash01(seed, slot * 8 + 2) * Mathf.PI * 2f;
            float distance01 = WorldEnvironmentNetwork.Hash01(seed, slot * 8 + 3);
            Vector3 c = cam.position;
            Vector3 cellCentre = new Vector3((Mathf.Floor(c.x / cellSize) + 0.5f) * cellSize, 0f, (Mathf.Floor(c.z / cellSize) + 0.5f) * cellSize);
            Vector3 p = cellCentre + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * Mathf.Lerp(minDistance, maxDistance, distance01);
            var terrain = Terrain.activeTerrain;
            p.y = terrain != null ? terrain.SampleHeight(p) + terrain.transform.position.y : c.y;

            pendingStrikeTime = slot * SlotSeconds + WorldEnvironmentNetwork.Hash01(seed, slot * 8 + 1) * SlotSeconds;
            pendingPosition = p;
            pendingVisible = WorldEnvironmentNetwork.Hash01(seed, slot * 8 + 4) >= sheetLightningRatio;
            float realDistance = Vector3.Distance(c, p);
            pendingAmplitude = Mathf.Lerp(1f, farFlash, Mathf.InverseLerp(minDistance, maxDistance, realDistance)) * (pendingVisible ? 1f : 0.45f);
            thunderClip = (int)(WorldEnvironmentNetwork.Hash01(seed, slot * 8 + 5) * 1000f);
        }

        private void Strike()
        {
            flashStart = WorldEnvironmentNetwork.Instance != null ? WorldEnvironmentNetwork.Instance.Now : 0.0;
            flashAmplitude = pendingAmplitude;

            if (pendingVisible && pool != null && pool.Length > 0)
            {
                var ps = pool[poolIndex];
                poolIndex = (poolIndex + 1) % pool.Length;
                ps.transform.position = pendingPosition;
                ps.Play(true);
            }

            if (cam != null)
            {
                float distance = Vector3.Distance(cam.position, pendingPosition);
                thunderTime = flashStart + Mathf.Min(distance / 343f, maxThunderDelay);
                thunderLoudness = Mathf.Lerp(1f, farThunder, Mathf.InverseLerp(minDistance, maxDistance, distance));
            }
        }

        /// <summary>Bright stroke, brief dip, then a decaying return stroke.</summary>
        private float Envelope(float age)
        {
            if (age < 0f || age > 1.5f) return 0f;
            if (age < 0.08f) return 1f;
            if (age < 0.15f) return 0.2f;
            return Mathf.Exp(-(age - 0.15f) * flashDecay) * 0.85f;
        }
    }
}
