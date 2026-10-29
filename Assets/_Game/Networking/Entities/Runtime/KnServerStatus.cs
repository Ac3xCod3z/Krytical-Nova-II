using System;
using System.Globalization;
using System.Text;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

namespace KryticalNova.Networking.Entities
{
    /// <summary>
    /// What the status page and the UDP status answer say, copied out of the world once a second by <see cref="KnStatusSystem"/>.
    /// Immutable and plain, so another thread reading it never touches the entity world (the HTTP thread must not).
    /// </summary>
    public sealed class KnStatusSnapshot
    {
        public string Name = string.Empty;
        public string Build = string.Empty;
        public string Map = string.Empty;
        public string Role = string.Empty;
        public int Players;
        public int MaxPlayers;
        /// <summary>The server tick number, 0 before the first tick is known.</summary>
        public uint Tick;
        public bool Stopping;
        /// <summary>Seconds the world has run.</summary>
        public double UptimeSeconds;
        /// <summary>UTC ticks when the snapshot was taken.</summary>
        public long TakenUtcTicks;

        /// <summary>The page's JSON answer: no account, no address, nothing a stranger should not read.</summary>
        public string ToJson()
        {
            var sb = new StringBuilder(256);
            sb.Append("{\"name\":").Append(KnJson.Quote(Name))
              .Append(",\"build\":").Append(KnJson.Quote(Build))
              .Append(",\"map\":").Append(KnJson.Quote(Map))
              .Append(",\"role\":").Append(KnJson.Quote(Role))
              .Append(",\"players\":").Append(Players.ToString(CultureInfo.InvariantCulture))
              .Append(",\"max\":").Append(MaxPlayers.ToString(CultureInfo.InvariantCulture))
              .Append(",\"tick\":").Append(Tick.ToString(CultureInfo.InvariantCulture))
              .Append(",\"stopping\":").Append(Stopping ? "true" : "false")
              .Append(",\"uptime\":").Append(UptimeSeconds.ToString("0.0", CultureInfo.InvariantCulture))
              .Append('}');
            return sb.ToString();
        }
    }

    /// <summary>Minimal JSON string quoting for the status page.</summary>
    public static class KnJson
    {
        public static string Quote(string text)
        {
            if (text == null) return "null";
            var sb = new StringBuilder(text.Length + 2);
            sb.Append('"');
            foreach (char c in text)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20 || c == '<' || c == '>' || c == '&') sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }
    }

    /// <summary>The one latest <see cref="KnStatusSnapshot"/>: written by the main thread, read by any.</summary>
    public static class KnServerStatus
    {
        private static volatile KnStatusSnapshot s_Current = new KnStatusSnapshot();

        /// <summary>What does not change while the server runs; set at start by <see cref="KnServerEntry"/>.</summary>
        public static string Name = "Krytical Nova Server";
        public static string Build = string.Empty;
        public static string Map = string.Empty;
        public static string Role = "main";
        public static int MaxPlayers = 100;

        public static KnStatusSnapshot Current => s_Current;

        public static void Publish(KnStatusSnapshot snapshot) => s_Current = snapshot ?? new KnStatusSnapshot();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Reset()
        {
            s_Current = new KnStatusSnapshot();
            Name = "Krytical Nova Server";
            Build = Map = string.Empty;
            Role = "main";
            MaxPlayers = 100;
        }
    }

    /// <summary>
    /// Server, main thread: once a second copies players, tick and stop state into <see cref="KnServerStatus"/>. This is the only
    /// place the status page's numbers come from; the HTTP and UDP threads read the finished snapshot.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    public partial class KnStatusSystem : SystemBase
    {
        /// <summary>Seconds between snapshots (world time).</summary>
        public const double Interval = 1.0;

        private EntityQuery m_Players;
        private EntityQuery m_Time;
        private double m_Next;

        protected override void OnCreate()
        {
            m_Players = GetEntityQuery(ComponentType.ReadOnly<NetworkId>(), ComponentType.ReadOnly<ConnectionPlayerKey>());
            m_Time = GetEntityQuery(ComponentType.ReadOnly<NetworkTime>());
        }

        protected override void OnUpdate()
        {
            double now = World.Time.ElapsedTime;
            if (now < m_Next) return;
            m_Next = now + Interval;
            uint tick = 0;
            if (m_Time.TryGetSingleton(out NetworkTime time) && time.ServerTick.IsValid) tick = time.ServerTick.TickIndexForValidTick;
            KnServerStatus.Publish(new KnStatusSnapshot
            {
                Name = KnServerStatus.Name,
                Build = KnServerStatus.Build,
                Map = KnServerStatus.Map,
                Role = KnServerStatus.Role,
                Players = m_Players.CalculateEntityCount(),
                MaxPlayers = KnServerStatus.MaxPlayers,
                Tick = tick,
                Stopping = KnStopRequests.Stopping,
                UptimeSeconds = now,
                TakenUtcTicks = DateTime.UtcNow.Ticks,
            });
        }
    }
}
