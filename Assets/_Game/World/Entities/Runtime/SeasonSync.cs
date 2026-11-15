using System;
using System.Collections.Generic;
using KryticalNova.Core;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;

namespace KryticalNova.World.Entities
{
    /// <summary>The season as the server sends it (owner's DAY list item 21): <see cref="SeasonState"/> field for field, 63 bytes.</summary>
    public struct SeasonStateRpc : IRpcCommand
    {
        public double BlendStart, SeasonStart, SnowAnchor;
        public float BlendSeconds, SeasonSeconds, SnowCover, SnowRate, SnowFloor, SnowCeiling, Progress, Temperature;
        public byte FromSeason, ToSeason, CalendarSeason, Flags, Spell, SpellSeason, DryDays;

        public static SeasonStateRpc Of(in SeasonState s) => new SeasonStateRpc
        {
            BlendStart = s.BlendStart, SeasonStart = s.SeasonStart, SnowAnchor = s.SnowAnchor, BlendSeconds = s.BlendSeconds,
            SeasonSeconds = s.SeasonSeconds, SnowCover = s.SnowCover, SnowRate = s.SnowRate, SnowFloor = s.SnowFloor,
            SnowCeiling = s.SnowCeiling, Progress = s.Progress, FromSeason = s.FromSeason, ToSeason = s.ToSeason, CalendarSeason = s.CalendarSeason, Flags = s.Flags,
            Temperature = s.Temperature, Spell = s.Spell, SpellSeason = s.SpellSeason, DryDays = s.DryDays,
        };

        public SeasonState State => new SeasonState
        {
            BlendStart = BlendStart, SeasonStart = SeasonStart, SnowAnchor = SnowAnchor, BlendSeconds = BlendSeconds,
            SeasonSeconds = SeasonSeconds, SnowCover = SnowCover, SnowRate = SnowRate, SnowFloor = SnowFloor,
            SnowCeiling = SnowCeiling, Progress = Progress, FromSeason = FromSeason, ToSeason = ToSeason, CalendarSeason = CalendarSeason, Flags = Flags,
            Temperature = Temperature, Spell = Spell, SpellSeason = SpellSeason, DryDays = DryDays,
        };
    }

    /// <summary>What the snow needs to know of the weather now: what falls, how hard, and whether it is night (the warped sun).</summary>
    public struct SeasonWeatherNow
    {
        public PrecipitationType Falling;
        public float Intensity;
        /// <summary>Time of day on the world clock, 0-1 (the season warps it for the sun).</summary>
        public float Clock01;
        /// <summary>The world clock's day number (weather spells run over days).</summary>
        public long Day;
        /// <summary>Degrees the weather now adds to the air (its profiles' offsets, blended).</summary>
        public float TemperatureOffset;
    }

    /// <summary>How a GM set this map's climate (gm.season climate).</summary>
    public enum SeasonClimate : byte { Auto = 0, Temperate = 1, AlwaysWinter = 2 }

    /// <summary>
    /// Server: the season clock (owner's DAY list item 21; design note <c>seasons-plan.md</c>). Real UTC time decides the season from
    /// <see cref="SeasonSettings"/> (two real weeks each, turning Mondays 00:00 UTC); a GM may hold a season (gm.season, the GM window, the
    /// control page) until "auto". It keeps the snow cover (piles up while it snows, melts after), gives the weather pick the season's
    /// weights (<see cref="SeasonWeather"/>), and sends the state to every player in the game when it changes and to each player as they
    /// come in, as <c>WorldStateServerSystem</c> does the world clock. Looked at four times a second. The server only decides numbers: it
    /// never touches client presentation (the build 12 black-sky rule).
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class SeasonServerSystem : SystemBase
    {
        /// <summary>The weather now (the scene's <c>WorldEnvironmentNetwork</c>; tests put their own). ok false = no weather here.</summary>
        public static Func<(bool ok, SeasonWeatherNow weather)> WeatherSource;
        /// <summary>This map's region and scene (the scene's <c>ZoneRules</c>; tests put their own).</summary>
        public static Func<(string region, string scene)> ZoneSource;

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { WeatherSource = null; ZoneSource = null; }

        /// <summary>The settings (tests put their own); the Resources asset otherwise.</summary>
        public SeasonSettings Settings { get => m_Settings; set { m_Settings = value; m_Started = false; } }
        /// <summary>The shared clock (tests put their own); <see cref="NetClock.Now"/> otherwise.</summary>
        public Func<double> Clock { get => m_Clock; set { m_Clock = value; m_Started = false; } }
        /// <summary>Real UTC time in Unix seconds (tests put their own); the PC's clock otherwise. Read on the server only.</summary>
        public Func<double> UtcNow { get => m_Utc; set { m_Utc = value; m_Started = false; } }
        public float Interval = 0.25f;

        SeasonSettings m_Settings;
        Func<double> m_Clock, m_Utc;
        bool m_Started, m_Hold;
        double m_UtcToClock, m_Next;
        SeasonClimate m_Climate;
        SeasonState m_State;
        SeasonProfile m_WeatherProfile;
        WeatherSpell m_Spell;
        Func<WeatherProfile, float> m_Scale;
        readonly List<WeatherProfile.NextWeather> m_Extra = new List<WeatherProfile.NextWeather>();
        float m_TemperatureOverride = float.NaN;
        int m_SpellOverride = -1;
        long m_SpellDayKey = long.MinValue;
        Season m_SpellSeasonKey;
        SpellDay m_SpellDay;
        int m_DryDays;
        readonly Dictionary<Entity, SeasonState> m_Sent = new Dictionary<Entity, SeasonState>();
        readonly List<Entity> m_Gone = new List<Entity>();
        EntityQuery m_InGame;

        /// <summary>The state now (tests, GM status).</summary>
        public SeasonState Current => m_State;
        /// <summary>True once the settings were found and a state was worked out.</summary>
        public bool Running => m_Started;
        /// <summary>States sent (tests).</summary>
        public int Sends { get; private set; }
        /// <summary>The climate a GM set for this map (Auto = from the settings' list).</summary>
        public SeasonClimate Climate => m_Climate;

        /// <summary>The server world's season system, null when this process runs no server.</summary>
        public static SeasonServerSystem Find()
        {
            foreach (Unity.Entities.World w in Unity.Entities.World.All)
                if (w.IsCreated && w.IsServer() && w.GetExistingSystemManaged<SeasonServerSystem>() is { } system) return system;
            return null;
        }

        protected override void OnCreate()
        {
            m_InGame = GetEntityQuery(ComponentType.ReadOnly<NetworkId>(), ComponentType.ReadOnly<NetworkStreamInGame>());
            m_Scale = w => (m_WeatherProfile != null ? m_WeatherProfile.WeightOf(w) : 1f) * (m_Spell != null ? m_Spell.WeightOf(w) : 1f);
        }

        protected override void OnDestroy()
        {
            if (SeasonWeather.Scale == m_Scale) { SeasonWeather.Scale = null; SeasonWeather.Extra = null; }
            SeasonView.HasServer = false;
        }

        SeasonSettings Config => m_Settings != null ? m_Settings : SeasonSettings.Instance;
        /// <summary>The shared clock's time now.</summary>
        public double Now => m_Clock != null ? m_Clock() : NetClock.Now;
        double Utc => m_Utc != null ? m_Utc() : (DateTime.UtcNow - DateTime.UnixEpoch).TotalSeconds;
        /// <summary>The shared clock's time of a UTC moment (fixed when the clock starts, so the season's times never jitter).</summary>
        public double ClockOf(double unix) => unix + m_UtcToClock;
        /// <summary>The UTC moment of a shared clock time.</summary>
        public double UnixOf(double clock) => clock - m_UtcToClock;

        protected override void OnUpdate()
        {
            double elapsed = SystemAPI.Time.ElapsedTime;
            if (elapsed < m_Next) return;
            m_Next = elapsed + Interval;
            if (!Step()) return;
            Send();
        }

        /// <summary>Works the state out now (main thread). False with no settings.</summary>
        public bool Step()
        {
            SeasonSettings s = Config;
            if (s == null) return false;
            double now = Now;
            SeasonCalendar calendar = s.Calendar;
            SeasonNow cal = SeasonRules.At(calendar, Utc);
            if (!m_Started)
            {
                m_UtcToClock = now - Utc;
                m_State = default;
                m_State.FromSeason = (byte)cal.Previous;
                m_State.ToSeason = (byte)cal.Season;
                m_State.BlendStart = ClockOf(cal.StartUnix);
                m_State.BlendSeconds = (float)calendar.TransitionSeconds;
                m_State.SnowAnchor = now;
                m_State.SnowRate = float.NaN;   // anchored below
                m_State.SnowCeiling = 1f;
                m_State.Progress = -1f;         // the calendar's share of the season
                m_Hold = false;
            }
            else if (!m_Hold && m_State.To != cal.Season)
            {
                // the calendar turned: blend from what shows now into the new season from its start
                m_State.FromSeason = (byte)m_State.DominantAt(now);
                m_State.ToSeason = (byte)cal.Season;
                m_State.BlendStart = Math.Min(now, ClockOf(cal.StartUnix));
                m_State.BlendSeconds = (float)calendar.TransitionSeconds;
            }
            m_State.CalendarSeason = (byte)cal.Season;
            m_State.SeasonStart = ClockOf(cal.StartUnix);
            m_State.SeasonSeconds = (float)calendar.SeasonSeconds;

            bool alwaysWinter = m_Climate == SeasonClimate.AlwaysWinter || (m_Climate == SeasonClimate.Auto && ZoneIsAlwaysWinter(s));
            float floor = alwaysWinter ? s.alwaysWinterFloor : 0f;
            m_State.Flags = (byte)((m_Hold ? SeasonState.FlagGmHold : 0) | (alwaysWinter ? SeasonState.FlagAlwaysWinter : 0));

            // the weather pick gets the season's weights and today's spell's (an always-winter map takes Winter's)
            Season dominant = m_State.DominantAt(now);
            Season weatherSeason = alwaysWinter ? Season.Winter : dominant;
            SeasonProfile profile = s.Profile(weatherSeason);
            (bool ok, SeasonWeatherNow w) = WeatherSource != null ? WeatherSource() : DefaultWeather();
            UpdateSpell(s, profile, weatherSeason, ok ? w.Day : (long)Math.Floor(now / 3600.0));
            m_WeatherProfile = profile;
            SeasonWeather.Scale = profile != null ? m_Scale : null;
            SeasonWeather.Extra = profile != null ? m_Extra : null;

            // the air: the season's mean and day swing, the spell, the weather, the day's own wobble (owner 14 Nov)
            float share = DaylightShare(s, now);
            float temperature = TemperatureNow(s, now, ok ? w.Clock01 : 0.5f, share, ok ? w.TemperatureOffset : 0f, ok ? w.Day : 0, alwaysWinter);
            m_State.Temperature = temperature;

            // the snow: re-anchored only when its rate or floor changes (a new weather, a temperature step, sunrise, sunset, a GM)
            float coverNow = m_Started ? m_State.SnowAt(now) : 0f;
            var trend = new SnowTrend { Rate = 0f, Ceiling = 1f };
            if (ok)
            {
                bool sunUp = !SeasonRules.IsNight(w.Clock01, share);
                trend = SeasonRules.SnowTrendOf(s.Rates, w.Falling, w.Intensity, temperature, sunUp, coverNow);
            }
            if (!m_Started || !trend.Rate.Equals(m_State.SnowRate) || !floor.Equals(m_State.SnowFloor) || !trend.Ceiling.Equals(m_State.SnowCeiling))
            {
                m_State.SnowCover = Math.Clamp(Math.Max(coverNow, floor), 0f, 1f);
                m_State.SnowAnchor = now;
                m_State.SnowRate = trend.Rate;
                m_State.SnowFloor = floor;
                m_State.SnowCeiling = trend.Ceiling;
            }
            m_State.Flags |= (byte)((float.IsNaN(m_TemperatureOverride) ? 0 : SeasonState.FlagGmTemperature)
                                    | (m_SpellOverride >= 0 ? SeasonState.FlagGmSpell : 0));
            m_Started = true;
            SeasonView.Server = m_State;
            SeasonView.HasServer = true;
            return true;
        }

        /// <summary>
        /// Today's weather spell (owner, 14 Nov 2026: runs of wet, dry, cold or stormy days), rolled from the world's seed and the map's
        /// name; worked out once a day (or when the season or a GM changes it). A new spell asks the weather to roll again at once.
        /// </summary>
        void UpdateSpell(SeasonSettings s, SeasonProfile profile, Season season, long day)
        {
            long key = day * 16 + (m_SpellOverride + 1);
            if (key == m_SpellDayKey && season == m_SpellSeasonKey && m_Started) return;
            bool changed = m_Started;
            m_SpellDayKey = key;
            m_SpellSeasonKey = season;
            m_Spell = null;
            m_DryDays = 0;
            m_SpellDay = new SpellDay { Spell = -1, Length = 1 };
            if (profile != null && profile.spells.Count > 0)
            {
                int seed = WeatherSeed(s);
                IReadOnlyList<SpellChoice> table = profile.SpellTable;
                m_SpellDay = m_SpellOverride >= 0
                    ? new SpellDay { Spell = Math.Min(m_SpellOverride, profile.spells.Count - 1), DayInSpell = 0, Length = 1 }
                    : SeasonRules.SpellAt(table, seed, season, day, s.spellBlockDays);
                m_Spell = profile.Spell(m_SpellDay.Spell);
                m_DryDays = m_Spell != null && m_Spell.dry
                    ? SeasonRules.RunOfDays(table, seed, season, day, i => profile.Spell(i) is { dry: true }, 14, s.spellBlockDays)
                    : 0;
                if (m_SpellOverride >= 0 && m_Spell != null && m_Spell.dry) m_DryDays = Math.Max(m_DryDays, 7);
            }
            m_Extra.Clear();
            if (profile != null) m_Extra.AddRange(profile.extraNext);
            if (m_Spell != null) m_Extra.AddRange(m_Spell.extraNext);
            m_State.Spell = m_Spell != null ? (byte)m_SpellDay.Spell : SeasonState.NoSpell;
            m_State.SpellSeason = (byte)season;
            m_State.DryDays = (byte)Math.Clamp(m_DryDays, 0, 255);
            if (changed) SeasonWeather.Reroll = true;
        }

        /// <summary>The weather seed for this map: the settings' seed mixed with the scene's name (each map its own weather).</summary>
        int WeatherSeed(SeasonSettings s)
        {
            var (_, scene) = ZoneSource != null ? ZoneSource() : DefaultZone();
            int h = s.weatherSeed;
            if (!string.IsNullOrEmpty(scene))
                foreach (char c in scene) h = unchecked(h * 31 + char.ToLowerInvariant(c));
            return h;
        }

        /// <summary>The air temperature now (deg C, in the settings' steps), or a GM's.</summary>
        float TemperatureNow(SeasonSettings s, double now, float clock01, float share, float weatherOffset, long day, bool alwaysWinter)
        {
            if (!float.IsNaN(m_TemperatureOverride)) return m_TemperatureOverride;
            SeasonProfile from = s.Profile(alwaysWinter ? Season.Winter : m_State.From), to = s.Profile(alwaysWinter ? Season.Winter : m_State.To);
            TemperatureCurve a = from != null ? from.Temperature : new TemperatureCurve { BaseC = 12f, SwingC = 8f };
            TemperatureCurve b = to != null ? to.Temperature : a;
            TemperatureCurve curve = TemperatureCurve.Lerp(a, b, m_State.BlendAt(now));
            float spell = m_Spell != null ? m_Spell.temperatureOffset : 0f;
            float wobble = SeasonRules.Hash01(WeatherSeed(s) ^ 0x2F6B, day);
            float t = SeasonRules.TemperatureAt(curve, clock01, share, s.warmestClock, spell, weatherOffset, wobble, s.dayWobble);
            return SeasonRules.Quantize(t, s.temperatureStep);
        }

        /// <summary>Today's spell (null = none) and the day within it (tests, GM status).</summary>
        public WeatherSpell SpellNow => m_Spell;
        public SpellDay SpellDayNow => m_SpellDay;

        /// <summary>The share of the day the sun is up at <paramref name="now"/>: the calendar's seasons, blended (never the climate's).</summary>
        public float DaylightShare(SeasonSettings s, double now)
        {
            SeasonProfile from = s.Profile(m_State.From), to = s.Profile(m_State.To);
            float a = from != null ? from.daylightShare : 0.5f, b = to != null ? to.daylightShare : 0.5f;
            return a + (b - a) * m_State.BlendAt(now);
        }

        bool ZoneIsAlwaysWinter(SeasonSettings s)
        {
            (string region, string scene) = ZoneSource != null ? ZoneSource() : DefaultZone();
            return s.IsAlwaysWinter(region, scene);
        }

        static (string, string) DefaultZone()
        {
            ZoneRules z = ZoneRules.Current;
            return z != null && z.Entry != null ? (z.Entry.region, z.Entry.sceneName) : (null, z != null ? z.gameObject.scene.name : null);
        }

        static (bool, SeasonWeatherNow) DefaultWeather()
        {
            WorldEnvironmentNetwork env = WorldEnvironmentNetwork.Instance;
            if (env == null) return (false, default);
            WeatherProfile p = env.WeatherBlendT >= 0.5f ? env.ToWeather : env.FromWeather;
            if (p == null) p = env.ToWeather;
            float t = env.WeatherBlendT;
            float offset = (env.FromWeather != null ? env.FromWeather.temperatureOffset : 0f) * (1f - t)
                           + (env.ToWeather != null ? env.ToWeather.temperatureOffset : 0f) * t;
            return (true, new SeasonWeatherNow
            {
                Falling = p != null ? p.precipitation : PrecipitationType.None,
                Intensity = p != null ? p.precipitationIntensity : 0f,
                Clock01 = env.TimeOfDay01,
                Day = env.CalendarDay,
                TemperatureOffset = offset,
            });
        }

        void Send()
        {
            var em = EntityManager;
            m_Gone.Clear();
            foreach (Entity c in m_Sent.Keys) if (!em.Exists(c)) m_Gone.Add(c);
            foreach (Entity c in m_Gone) m_Sent.Remove(c);
            using var connections = m_InGame.ToEntityArray(Allocator.Temp);
            foreach (Entity connection in connections)
            {
                if (m_Sent.TryGetValue(connection, out SeasonState had) && had.Equals(m_State)) continue;
                m_Sent[connection] = m_State;
                Entity rpc = em.CreateEntity();
                em.AddComponentData(rpc, SeasonStateRpc.Of(m_State));
                em.AddComponentData(rpc, new SendRpcCommandRequest { TargetConnection = connection });
                Sends++;
            }
        }

        // ---------------------------------------------------------------- GM (main thread: QC, the GM window, the control page)

        /// <summary>Holds a season (blends in over <paramref name="blendSeconds"/>, the settings' GM blend when negative). False with no settings.</summary>
        public bool Hold(Season season, float blendSeconds = -1f)
        {
            if (!Step()) return false;
            double now = Now;
            m_State.FromSeason = (byte)m_State.DominantAt(now);
            m_State.ToSeason = (byte)season;
            m_State.BlendStart = now;
            m_State.BlendSeconds = blendSeconds >= 0f ? blendSeconds : Config.gmBlendSeconds;
            m_Hold = true;
            Step();
            Send();
            return true;
        }

        /// <summary>Back to the calendar (blends over the GM blend). False with no settings.</summary>
        public bool Auto(float blendSeconds = -1f)
        {
            if (!Step()) return false;
            double now = Now;
            m_State.FromSeason = (byte)m_State.DominantAt(now);
            m_State.ToSeason = m_State.CalendarSeason;
            m_State.BlendStart = now;
            m_State.BlendSeconds = blendSeconds >= 0f ? blendSeconds : Config.gmBlendSeconds;
            m_Hold = false;
            Step();
            Send();
            return true;
        }

        /// <summary>Sets the snow cover now; it builds or melts from there. False with no settings.</summary>
        public bool SetSnow(float cover)
        {
            if (!Step()) return false;
            m_State.SnowCover = Math.Clamp(Math.Max(cover, m_State.SnowFloor), 0f, 1f);
            m_State.SnowCeiling = Math.Max(m_State.SnowCeiling, m_State.SnowCover);
            m_State.SnowAnchor = Now;
            SeasonView.Server = m_State;
            Send();
            return true;
        }

        /// <summary>
        /// Fixes how far into its season the world looks (0-1: early Autumn green turning, late Autumn bare), for GM shows and tests;
        /// below 0 = back to the calendar's. False with no settings.
        /// </summary>
        public bool SetProgress(float progress)
        {
            if (!Step()) return false;
            m_State.Progress = progress < 0f ? -1f : Math.Clamp(progress, 0f, 1f);
            SeasonView.Server = m_State;
            Send();
            return true;
        }

        /// <summary>Holds the air at a temperature (deg C) for GM shows and tests (NaN = back to the weather's). False with no settings.</summary>
        public bool SetTemperature(float celsius)
        {
            m_TemperatureOverride = float.IsNaN(celsius) ? float.NaN : Math.Clamp(celsius, -40f, 50f);
            if (!Step()) return false;
            Send();
            return true;
        }

        /// <summary>Holds today's spell (its index in the showing season's list; below 0 = the seed's). The weather rolls again at once.</summary>
        public bool SetSpell(int index)
        {
            m_SpellOverride = index < 0 ? -1 : index;
            if (!Step()) return false;
            Send();
            return true;
        }

        /// <summary>The spells of the coming days from today (the seed's, not a GM hold), for gm.season week.</summary>
        public void Forecast(List<(long day, WeatherSpell spell)> into, int days)
        {
            into.Clear();
            SeasonSettings s = Config;
            if (s == null || !m_Started) return;
            bool alwaysWinter = m_State.AlwaysWinter;
            Season season = alwaysWinter ? Season.Winter : m_State.DominantAt(Now);
            SeasonProfile profile = s.Profile(season);
            if (profile == null) return;
            (bool ok, SeasonWeatherNow w) = WeatherSource != null ? WeatherSource() : DefaultWeather();
            long today = ok ? w.Day : (long)Math.Floor(Now / 3600.0);
            int seed = WeatherSeed(s);
            for (int i = 0; i < days; i++)
                into.Add((today + i, profile.Spell(SeasonRules.SpellAt(profile.SpellTable, seed, season, today + i, s.spellBlockDays).Spell)));
        }

        /// <summary>Sets this map's climate until the server restarts (Auto = from the settings' always-winter list).</summary>
        public bool SetClimate(SeasonClimate climate)
        {
            m_Climate = climate;
            if (!Step()) return false;
            Send();
            return true;
        }
    }

    /// <summary>Client: takes the server's season (the look, the daylight and the snow every client draws the same).</summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class SeasonClientSystem : SystemBase
    {
        /// <summary>Where a received state goes (<see cref="SeasonView.Client"/>; tests put their own).</summary>
        public static Action<SeasonState> Sink;

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Sink = null;

        EntityQuery m_States;

        /// <summary>States taken, and the last one (tests).</summary>
        public int Applied { get; private set; }
        public SeasonState Last { get; private set; }

        protected override void OnCreate() =>
            m_States = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<SeasonStateRpc>());

        protected override void OnDestroy() => SeasonView.HasClient = false;

        protected override void OnUpdate()
        {
            if (m_States.IsEmpty) return;
            using var states = m_States.ToComponentDataArray<SeasonStateRpc>(Allocator.Temp);
            EntityManager.DestroyEntity(m_States);
            foreach (SeasonStateRpc rpc in states)
            {
                Last = rpc.State;
                Applied++;
                if (Sink != null) Sink(Last);
                else { SeasonView.Client = Last; SeasonView.HasClient = true; }
            }
        }
    }
}
