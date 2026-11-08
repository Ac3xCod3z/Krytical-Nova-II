#if UNITY_SERVER || UNITY_EDITOR
using System;
using Unity.Entities;
using UnityEngine;

namespace KryticalNova.Networking.Entities
{
    /// <summary>
    /// The page's "stop in N seconds" and "restart in N seconds" (8 Nov 2026 port of NGO's ServerLifetime.ScheduleShutdown / CancelShutdown /
    /// TickShutdown, ServerLifetime.cs:~186-240). The count and the notices (5 min, 2 min, 1 min, 30, 10, 5, 4, 3, 2, 1 s) are pure, so they are
    /// tested without a world; <see cref="KnShutdownSystem"/> ticks them on the game's thread and, at zero, asks the normal stop
    /// (<see cref="KnStopRequests"/>, which saves everyone and quits). A restart quits with exit code 3, which the start script answers by starting again.
    /// </summary>
    public static class KnShutdown
    {
        public const int RestartExitCode = 3;
        public static readonly int[] AnnounceAt = { 300, 120, 60, 30, 10, 5, 4, 3, 2, 1 };

        public sealed class Countdown
        {
            double m_At = -1;
            int m_Next;

            public string Reason { get; private set; } = string.Empty;
            public bool Restarts { get; private set; }
            public bool Active => m_At >= 0;

            /// <summary>Seconds left (rounded up), or -1 when nothing counts down.</summary>
            public int SecondsLeft(double now) => m_At < 0 ? -1 : (int)Math.Ceiling(Math.Max(0, m_At - now));

            public string Schedule(double now, float seconds, string reason, bool restart, Action<string> announce)
            {
                seconds = Mathf.Clamp(seconds, 0f, 3600f);
                m_At = now + seconds;
                Restarts = restart;
                Reason = string.IsNullOrWhiteSpace(reason)
                    ? (restart ? "The server is restarting. Join again in a minute." : "The server is shutting down for maintenance.")
                    : reason.Trim();
                m_Next = 0;
                // the first notice is this one: the next comes at the first mark below it
                while (m_Next < AnnounceAt.Length && AnnounceAt[m_Next] >= seconds) m_Next++;
                string what = restart ? "restarts" : "shuts down";
                announce?.Invoke($"The server {what} in {Describe(seconds)}. {Reason}");
                return $"{(restart ? "Restart" : "Shutdown")} in {Describe(seconds)}: {Reason}";
            }

            public string Cancel(Action<string> announce)
            {
                if (m_At < 0) return "No shutdown is counting down.";
                m_At = -1;
                announce?.Invoke("The shutdown was called off.");
                return "Shutdown called off.";
            }

            /// <summary>True once, when the time is up (the count is then over).</summary>
            public bool Tick(double now, Action<string> announce)
            {
                if (m_At < 0) return false;
                double left = m_At - now;
                if (left <= 0)
                {
                    m_At = -1;
                    return true;
                }
                if (m_Next < AnnounceAt.Length && left <= AnnounceAt[m_Next])
                {
                    announce?.Invoke($"The server {(Restarts ? "restarts" : "shuts down")} in {Describe(AnnounceAt[m_Next])}.");
                    m_Next++;
                }
                return false;
            }

            public void Reset()
            {
                m_At = -1;
                m_Next = 0;
                Restarts = false;
                Reason = string.Empty;
            }
        }

        public static readonly Countdown Current = new Countdown();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Current.Reset();

        public static double Now => Time.realtimeSinceStartupAsDouble;

        /// <summary>Tells every player (the server's own words) and the log.</summary>
        public static void Announce(string text)
        {
            Debug.Log("[Server] " + text);
            KnPageDesk.Desk?.Announce(text);
        }

        public static string Schedule(float seconds, string reason, bool restart)
        {
            if (KnStopRequests.Stopping) return "The server is already stopping.";
            return Current.Schedule(Now, seconds, reason, restart, Announce) + " (Cancel on the page stops it.)";
        }

        public static string Cancel() => Current.Cancel(Announce);

        static string Describe(float seconds)
        {
            int s = Mathf.CeilToInt(seconds);
            if (s >= 120 && s % 60 == 0) return (s / 60) + " minutes";
            if (s == 60) return "1 minute";
            return s == 1 ? "1 second" : s + " seconds";
        }

        /// <summary>Time is up: a restart quits with exit code 3 (the stop's normal quit says 0), then the normal stop runs.</summary>
        public static void Fire(bool restart)
        {
            Debug.Log("[Server] Stopping at the page's countdown: " + Current.Reason);
            if (restart && KnStopSystem.QuitOverride == null)
                KnStopSystem.QuitOverride = _ => Application.Quit(RestartExitCode);
            KnStopRequests.Request(KnStopSource.Code);
        }
    }

    /// <summary>Server, main thread: counts the page's shutdown down.</summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    public partial class KnShutdownSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            KnShutdown.Countdown count = KnShutdown.Current;
            if (!count.Active) return;
            bool restart = count.Restarts;
            if (count.Tick(KnShutdown.Now, KnShutdown.Announce)) KnShutdown.Fire(restart);
        }
    }
}
#endif
