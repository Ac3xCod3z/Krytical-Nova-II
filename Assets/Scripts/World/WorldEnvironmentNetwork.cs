using System;
using Unity.Netcode;
using UnityEngine;

namespace KryticalNova.World
{
    /// <summary>
    /// Server-authoritative clock + weather scheduler. Replicates one small <see cref="WorldEnvironmentState"/>
    /// that changes only when the weather changes (minutes apart); every peer evaluates time of day,
    /// blend progress, coverage drift and lightning locally from server time and a shared seed.
    /// Runs the same logic offline (before a session starts) on <see cref="Time.timeAsDouble"/>.
    /// Has no rendering cost — presentation lives in <see cref="EnvironmentPresenter"/>.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-50)]
    public sealed class WorldEnvironmentNetwork : NetworkBehaviour
    {
        public static WorldEnvironmentNetwork Instance { get; private set; }

        [Header("Profiles")]
        [SerializeField] private DayCycleProfile dayCycle;
        [SerializeField] private WeatherCatalog weatherCatalog;
        [SerializeField] private WeatherProfile startWeather;

        [Header("Scheduling (server)")]
        [Tooltip("Pick new weather automatically from each profile's Next list.")]
        [SerializeField] private bool autoWeather = true;
        [Tooltip("Seconds for cloud coverage to drift from one random point in a profile's range to the next.")]
        [Min(10f)] [SerializeField] private float coverageDriftPeriod = 300f;
        [Tooltip("Average seconds between gust peaks (shared noise, so every client feels the same gust).")]
        [Range(1f, 20f)] [SerializeField] private float gustPeriod = 5f;
        [Tooltip("Extra wind speed at a full gust in a gustiness-1 weather (0.8 = +80%).")]
        [Range(0f, 2f)] [SerializeField] private float maxGustBoost = 0.8f;
        [Tooltip("Seconds per slow lull/surge swing of the wind, so even one weather state breathes.")]
        [Range(20f, 600f)] [SerializeField] private float windSwellPeriod = 90f;
        [Tooltip("How far the slow lull/surge scales wind speed (0.25 = ±25%).")]
        [Range(0f, 0.6f)] [SerializeField] private float windSwellAmount = 0.25f;

        [Header("Debug (host / offline only)")]
        [SerializeField] [Range(0f, 24f)] private float debugHours = 12f;
        [SerializeField] private WeatherProfile debugWeather;
        [SerializeField] [Min(0f)] private float debugTransitionSeconds = 10f;

        private readonly NetworkVariable<WorldEnvironmentState> netState = new NetworkVariable<WorldEnvironmentState>(
            default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private WorldEnvironmentState state;
        private double nextWeatherChangeTime;
        private System.Random rng;
        private bool weatherLocked;
        private int lastHour = -1;
        private double lastNow;
        private double lastLocalNow;

        public DayCycleProfile DayCycle => dayCycle;
        public WeatherCatalog Catalog => weatherCatalog;
        public WorldEnvironmentState State => state;

        /// <summary>True offline, on the server and on a host. Only these may change the world.</summary>
        public bool HasWorldAuthority => !IsSpawned || IsServer;
        public bool WeatherLocked => weatherLocked;

        /// <summary>Shared clock: server time when networked, local time offline.</summary>
        public double Now => IsSpawned && NetworkManager != null ? NetworkManager.ServerTime.Time : Time.timeAsDouble;

        // Per-frame evaluation (read by WorldClock and presentation).
        public float TimeOfDay01 { get; private set; }
        public WeatherBlend Weather { get; private set; } = WeatherBlend.Clear();
        public WeatherProfile FromWeather { get; private set; }
        public WeatherProfile ToWeather { get; private set; }
        /// <summary>Eased transition progress 0-1 from <see cref="FromWeather"/> to <see cref="ToWeather"/>.</summary>
        public float WeatherBlendT { get; private set; } = 1f;

        public event Action<int> HourChanged;
        public event Action<WeatherProfile> WeatherChanged;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            InitLocalState(Time.timeAsDouble);
            Evaluate(Time.timeAsDouble);
        }

        private void OnEnable()
        {
            // Survives an Editor domain reload during Play Mode (statics and private state are cleared).
            if (Instance == null) Instance = this;
            if (Instance == this && state.DayLengthSeconds <= 0f) InitLocalState(Time.timeAsDouble);
        }

        public override void OnDestroy()
        {
            if (Instance == this) Instance = null;
            base.OnDestroy();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            double serverNow = NetworkManager.ServerTime.Time;

            if (IsServer)
            {
                // Keep the offline sky continuous when a host/server session starts.
                double delta = serverNow - lastLocalNow;
                state.Rebase(lastLocalNow, serverNow);
                nextWeatherChangeTime += delta;
                netState.Value = state;
            }
            else
            {
                ApplyReplicatedState(netState.Value);
                netState.OnValueChanged += OnNetStateChanged;
            }
        }

        public override void OnNetworkDespawn()
        {
            if (!IsServer) netState.OnValueChanged -= OnNetStateChanged;

            // Fall back to the local clock without a visible jump.
            double localNow = Time.timeAsDouble;
            state.Rebase(lastNow, localNow);
            nextWeatherChangeTime += localNow - lastNow;
            base.OnNetworkDespawn();
        }

        private void OnNetStateChanged(WorldEnvironmentState previous, WorldEnvironmentState current)
        {
            ApplyReplicatedState(current);
        }

        private void ApplyReplicatedState(WorldEnvironmentState replicated)
        {
            if (replicated.DayLengthSeconds <= 0f) return; // not initialised yet
            state = replicated;
        }

        private void Update()
        {
            double now = Now;
            lastNow = now;
            lastLocalNow = Time.timeAsDouble;

            if (HasWorldAuthority && autoWeather && !weatherLocked && now >= nextWeatherChangeTime)
                AdvanceWeather(now);

            Evaluate(now);
        }

        // ---------------------------------------------------------------- evaluation

        private void Evaluate(double now)
        {
            TimeOfDay01 = state.TimeOfDay01(now);

            int hour = Mathf.FloorToInt(TimeOfDay01 * 24f) % 24;
            if (hour != lastHour)
            {
                lastHour = hour;
                HourChanged?.Invoke(hour);
            }

            var to = weatherCatalog != null ? weatherCatalog.Get(state.ToWeather) : null;
            var from = weatherCatalog != null ? weatherCatalog.Get(state.FromWeather) : null;
            if (to != ToWeather && ToWeather != null) WeatherChanged?.Invoke(to);
            FromWeather = from;
            ToWeather = to;

            float p = state.TransitionProgress(now);
            WeatherBlendT = p * p * (3f - 2f * p);
            var blend = to == null ? WeatherBlend.Clear()
                : WeatherBlend.Evaluate(from, to, WeatherBlendT, CoverageNoise(now), state.FromWindHeading, state.ToWindHeading,
                    state.FromWindSpeed, state.ToWindSpeed);

            // Slow lulls and surges on top of the per-cycle speed (shared noise, so every client agrees).
            blend.wind *= 1f + (SwellNoise(now) * 2f - 1f) * windSwellAmount;

            // Gusts: shared smooth noise, squared so calm stretches are long and surges are punchy.
            float g = GustNoise(now);
            blend.gust = blend.gustiness * g * g;
            blend.wind *= 1f + blend.gust * maxGustBoost;
            Weather = blend;
        }

        private float GustNoise(double now)
        {
            double x = now / gustPeriod;
            long i = (long)Math.Floor(x);
            float f = (float)(x - i);
            float a = Hash01(state.Seed ^ 0x2F6B3A1, i);
            float b = Hash01(state.Seed ^ 0x2F6B3A1, i + 1);
            return Mathf.Lerp(a, b, f * f * (3f - 2f * f));
        }

        private float SwellNoise(double now)
        {
            double x = now / windSwellPeriod;
            long i = (long)Math.Floor(x);
            float f = (float)(x - i);
            float a = Hash01(state.Seed ^ 0x51A7E11, i);
            float b = Hash01(state.Seed ^ 0x51A7E11, i + 1);
            return Mathf.Lerp(a, b, f * f * (3f - 2f * f));
        }

        /// <summary>
        /// Server: the wind speed for one cycle of a weather. Some cycles are calm; the rest roll inside the profile's
        /// range biased toward the low end (r²), so strong-wind days are rarer than breezy ones.
        /// </summary>
        private float RollWindSpeed(WeatherProfile profile)
        {
            if (profile == null) return 3f;
            if (!profile.HasWindRange || rng == null) return profile.windSpeed;
            if (rng.NextDouble() < profile.calmChance) return (float)rng.NextDouble();
            float r = (float)rng.NextDouble();
            return Mathf.Lerp(profile.windSpeedMin, profile.windSpeedMax, r * r);
        }

        /// <summary>Server: the heading a weather cycle blows toward — random per cycle, or the profile's fixed heading.</summary>
        private float RollWindHeading(WeatherProfile profile)
        {
            if (profile == null) return 60f;
            return profile.randomWindDirection && rng != null ? (float)(rng.NextDouble() * 360.0) : profile.windHeading;
        }

        /// <summary>Smooth 1D value noise in [0,1], identical on every peer for the same seed and time.</summary>
        private float CoverageNoise(double now)
        {
            double x = now / coverageDriftPeriod;
            long i = (long)Math.Floor(x);
            float f = (float)(x - i);
            float a = Hash01(state.Seed, i);
            float b = Hash01(state.Seed, i + 1);
            return Mathf.Lerp(a, b, f * f * (3f - 2f * f));
        }

        /// <summary>Deterministic hash → [0,1). Shared with lightning so all clients agree.</summary>
        public static float Hash01(int seed, long n)
        {
            unchecked
            {
                ulong h = (ulong)n * 0x9E3779B97F4A7C15UL ^ (ulong)(uint)seed * 0xC2B2AE3D27D4EB4FUL;
                h ^= h >> 33; h *= 0xFF51AFD7ED558CCDUL;
                h ^= h >> 33; h *= 0xC4CEB9FE1A85EC53UL;
                h ^= h >> 33;
                return (h >> 40) / (float)(1UL << 24);
            }
        }

        // ---------------------------------------------------------------- scheduling (authority only)

        private void InitLocalState(double now)
        {
            int seed = Environment.TickCount ^ 0x5F3759DF;
            rng = new System.Random(seed);
            byte start = (byte)Mathf.Max(0, weatherCatalog != null ? weatherCatalog.IndexOf(startWeather) : 0);
            state = new WorldEnvironmentState
            {
                CycleEpoch = now,
                DayLengthSeconds = dayCycle != null ? dayCycle.dayLengthSeconds : 1440f,
                TimeOffset01 = dayCycle != null ? dayCycle.startTimeOfDay01 : 0.3f,
                TimeScale = 1f,
                FromWeather = start,
                ToWeather = start,
                TransitionStart = now,
                TransitionSeconds = 0f,
                Seed = seed,
            };
            var startProfile = weatherCatalog != null ? weatherCatalog.Get(start) : null;
            state.ToWindHeading = state.FromWindHeading = RollWindHeading(startProfile);
            state.ToWindSpeed = state.FromWindSpeed = RollWindSpeed(startProfile);
            lastNow = lastLocalNow = now;
            ScheduleNext(now, weatherCatalog != null ? weatherCatalog.Get(start) : null, 0f);
        }

        private void AdvanceWeather(double now)
        {
            var current = weatherCatalog != null ? weatherCatalog.Get(state.ToWeather) : null;
            if (current == null || current.next.Count == 0)
            {
                ScheduleNext(now, current, 0f);
                return;
            }

            float total = 0f;
            foreach (var n in current.next) if (n.profile != null) total += n.weight;
            float pick = (float)rng.NextDouble() * total;
            WeatherProfile chosen = current;
            foreach (var n in current.next)
            {
                if (n.profile == null) continue;
                pick -= n.weight;
                if (pick <= 0f) { chosen = n.profile; break; }
            }

            int index = weatherCatalog.IndexOf(chosen);
            if (index < 0 || index == state.ToWeather)
            {
                ScheduleNext(now, current, 0f);
                return;
            }
            BeginTransition((byte)index, chosen.transitionSeconds, now);
        }

        private void BeginTransition(byte to, float seconds, double now)
        {
            // Interrupting a running blend: start from whichever side currently dominates.
            if (state.TransitionProgress(now) < 0.5f && state.TransitionSeconds > 0f)
            {
                state.ToWeather = state.FromWeather;
                state.ToWindHeading = state.FromWindHeading;
                state.ToWindSpeed = state.FromWindSpeed;
            }

            state.FromWeather = state.ToWeather;
            state.ToWeather = to;
            // The outgoing weather keeps its heading; the new cycle rolls one and holds it until the next change.
            state.FromWindHeading = state.ToWindHeading;
            state.ToWindHeading = RollWindHeading(weatherCatalog.Get(to));
            state.FromWindSpeed = state.ToWindSpeed;
            state.ToWindSpeed = RollWindSpeed(weatherCatalog.Get(to));
            state.TransitionStart = now;
            state.TransitionSeconds = Mathf.Max(0f, seconds);
            Commit();
            ScheduleNext(now, weatherCatalog.Get(to), seconds);
        }

        private void ScheduleNext(double now, WeatherProfile profile, float transitionSeconds)
        {
            float min = profile != null ? profile.durationMin : 300f;
            float max = profile != null ? profile.durationMax : 600f;
            nextWeatherChangeTime = now + transitionSeconds + Mathf.Lerp(min, max, (float)rng.NextDouble());
        }

        private void Commit()
        {
            if (IsSpawned && IsServer) netState.Value = state;
        }

        // ---------------------------------------------------------------- authority API
        // TODO(GM): wrap these with GM permission checks in the upcoming GM mod.
        // Dev console stand-ins live in Assets/Scripts/DevTools/WorldEnvironmentCommands.cs.

        /// <summary>Jumps the clock to <paramref name="hours"/> (0-24). Authority only.</summary>
        public bool SetTimeOfDay(float hours)
        {
            if (!HasWorldAuthority) return false;
            double now = Now;
            state.TimeOffset01 = Mathf.Repeat(hours / 24f, 1f);
            state.CycleEpoch = now;
            Commit();
            return true;
        }

        /// <summary>Changes real seconds per day while keeping the current time of day. Authority only.</summary>
        public bool SetDayLength(float seconds)
        {
            if (!HasWorldAuthority || seconds < 10f) return false;
            RebaseClock();
            state.DayLengthSeconds = seconds;
            Commit();
            return true;
        }

        /// <summary>1 = normal speed, 0 = pause time, &gt;1 = fast-forward. Authority only.</summary>
        public bool SetTimeScale(float scale)
        {
            if (!HasWorldAuthority || scale < 0f) return false;
            RebaseClock();
            state.TimeScale = scale;
            Commit();
            return true;
        }

        /// <summary>Starts blending to the catalog entry at <paramref name="index"/>. Authority only.</summary>
        public bool ForceWeather(int index, float transitionSeconds)
        {
            if (!HasWorldAuthority || weatherCatalog == null || index < 0 || index >= weatherCatalog.Count) return false;
            BeginTransition((byte)index, transitionSeconds, Now);
            return true;
        }

        /// <summary>
        /// GM override: holds this wind speed (and optionally heading, NaN = keep) for the current weather cycle,
        /// easing in over <paramref name="blendSeconds"/>. The next weather change rolls fresh wind. Authority only.
        /// </summary>
        public bool SetWind(float speed, float heading = float.NaN, float blendSeconds = 5f)
        {
            if (!HasWorldAuthority || speed < 0f) return false;
            double now = Now;
            // Re-anchor on the current weather so the override blends from what is blowing right now.
            Vector2 current = Weather.wind;
            state.FromWeather = state.ToWeather;
            state.FromWindSpeed = current.magnitude;
            state.FromWindHeading = current.sqrMagnitude > 1e-4f
                ? Mathf.Repeat(Mathf.Atan2(current.x, current.y) * Mathf.Rad2Deg, 360f) : state.ToWindHeading;
            state.ToWindSpeed = speed;
            if (!float.IsNaN(heading)) state.ToWindHeading = Mathf.Repeat(heading, 360f);
            state.TransitionStart = now;
            state.TransitionSeconds = Mathf.Max(0f, blendSeconds);
            Commit();
            return true;
        }

        /// <summary>Drops a GM wind override: re-rolls wind for the current weather. Authority only.</summary>
        public bool ClearWindOverride(float blendSeconds = 5f)
        {
            if (!HasWorldAuthority || weatherCatalog == null) return false;
            var profile = weatherCatalog.Get(state.ToWeather);
            return SetWind(RollWindSpeed(profile), RollWindHeading(profile), blendSeconds);
        }

        /// <summary>Stops (or resumes) automatic weather changes. Authority only.</summary>
        public bool SetWeatherLocked(bool locked)
        {
            if (!HasWorldAuthority) return false;
            weatherLocked = locked;
            if (!locked) ScheduleNext(Now, ToWeather, 0f);
            return true;
        }

        private void RebaseClock()
        {
            double now = Now;
            state.TimeOffset01 = state.TimeOfDay01(now);
            state.CycleEpoch = now;
        }

        [ContextMenu("Debug/Apply Debug Hours")]
        private void ApplyDebugHours() => SetTimeOfDay(debugHours);

        [ContextMenu("Debug/Apply Debug Weather")]
        private void ApplyDebugWeather()
        {
            if (weatherCatalog != null && debugWeather != null)
                ForceWeather(weatherCatalog.IndexOf(debugWeather), debugTransitionSeconds);
        }
    }
}
