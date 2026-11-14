using System;
using System.Collections.Generic;
using KryticalNova.Core;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;

namespace KryticalNova.World.Entities
{
    /// <summary>The season as the server sends it (owner's DAY list item 21): <see cref="SeasonState"/> field for field, 48 bytes.</summary>
    public struct SeasonStateRpc : IRpcCommand
    {
        public double BlendStart, SeasonStart, SnowAnchor;
        public float BlendSeconds, SeasonSeconds, SnowCover, SnowRate, SnowFloor;
        public byte FromSeason, ToSeason, CalendarSeason, Flags;

        public static SeasonStateRpc Of(in SeasonState s) => new SeasonStateRpc
        {
            BlendStart = s.BlendStart, SeasonStart = s.SeasonStart, SnowAnchor = s.SnowAnchor, BlendSeconds = s.BlendSeconds,
            SeasonSeconds = s.SeasonSeconds, SnowCover = s.SnowCover, SnowRate = s.SnowRate, SnowFloor = s.SnowFloor,
            FromSeason = s.FromSeason, ToSeason = s.ToSeason, CalendarSeason = s.CalendarSeason, Flags = s.Flags,
        };

        public SeasonState State => new SeasonState
        {
            BlendStart = BlendStart, SeasonStart = SeasonStart, SnowAnchor = SnowAnchor, BlendSeconds = BlendSeconds,
            SeasonSeconds = SeasonSeconds, SnowCover = SnowCover, SnowRate = SnowRate, SnowFloor = SnowFloor,
            FromSeason = FromSeason, ToSeason = ToSeason, CalendarSeason = CalendarSeason, Flags = Flags,
        };
    }

    /// <summary>What the snow needs to know of the weather now: what falls, how hard, and whether it is night (the warped sun).</summary>
    public struct SeasonWeatherNow
    {
        public PrecipitationType Falling;
        public float Intensity;
        /// <summary>Time of day on the world clock, 0-1 (the season warps it for the sun).</summary>
        public float Clock01;
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
        Func<WeatherProfile, float> m_Scale;
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
            m_Scale = w => m_WeatherProfile != null ? m_WeatherProfile.WeightOf(w) : 1f;
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

            // the weather pick gets the season's weights (an always-winter map takes Winter's)
            Season dominant = m_State.DominantAt(now);
            m_WeatherProfile = s.Profile(alwaysWinter ? Season.Winter : dominant);
            SeasonWeather.Scale = m_WeatherProfile != null ? m_Scale : null;
            SeasonWeather.Extra = m_WeatherProfile != null ? m_WeatherProfile.extraNext : null;

            // the snow: re-anchored only when its rate or floor changes (a new weather, sunrise, sunset, a GM)
            float rate = 0f;
            (bool ok, SeasonWeatherNow w) = WeatherSource != null ? WeatherSource() : DefaultWeather();
            if (ok)
            {
                bool night = SeasonRules.IsNight(w.Clock01, DaylightShare(s, now));
                rate = SeasonRules.SnowRate(s.Rates, w.Falling, w.Intensity, night, dominant, alwaysWinter);
            }
            if (!m_Started || !rate.Equals(m_State.SnowRate) || !floor.Equals(m_State.SnowFloor))
            {
                float cover = m_Started ? m_State.SnowAt(now) : 0f;
                m_State.SnowCover = Math.Clamp(Math.Max(cover, floor), 0f, 1f);
                m_State.SnowAnchor = now;
                m_State.SnowRate = rate;
                m_State.SnowFloor = floor;
            }
            m_Started = true;
            SeasonView.Server = m_State;
            SeasonView.HasServer = true;
            return true;
        }

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
            return (true, new SeasonWeatherNow
            {
                Falling = p != null ? p.precipitation : PrecipitationType.None,
                Intensity = p != null ? p.precipitationIntensity : 0f,
                Clock01 = env.TimeOfDay01,
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
            m_State.SnowAnchor = Now;
            SeasonView.Server = m_State;
            Send();
            return true;
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
