#if UNITY_SERVER
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;
using Debug = UnityEngine.Debug;

namespace KryticalNova.Networking
{
    /// <summary>
    /// How hard the server works (online phase 1, chunk 7: measuring how many players a server holds). The server runs
    /// 30 frames a second, one game tick each; a frame's time is mostly waiting, so what is measured is the busy part
    /// of each frame (from the first update to the last). Every five seconds a line goes into
    /// <c>PerformanceLogs/server_*.csv</c> beside the server (or the settings' <c>performanceLogFolder</c>): players,
    /// frames, busy milliseconds (average, 95th percentile, worst), frames that overran a tick (more than 33.3 ms) and
    /// memory. The control page shows the last line. A server keeps up while its busy time stays under a tick; the player count where it stops is its cap.
    /// A server that runs for days starts a new file each day, and only the newest <see cref="KeepFiles"/> of its own map
    /// are kept (Linux plan item 6: a box never fills its disk with logs).
    /// Only in a server build.
    /// </summary>
    public static class ServerPerformanceLog
    {
        public const float WindowSeconds = 5f;
        public const float TickMilliseconds = 1000f / 30f;
        public const int KeepFiles = 30;

        private static readonly Stopwatch Clock = new Stopwatch();
        private static readonly List<float> Busy = new List<float>(256);
        private static float windowStart = -1f;
        private static int frames;
        private static StreamWriter file;
        private static DateTime fileDay;

        /// <summary>The last window, for the control page ("30 fps · 12.3 ms average · 21.0 ms worst · 0 slow").</summary>
        public static string Summary { get; private set; } = "measuring...";
        /// <summary>The last window's numbers, for the page's status.</summary>
        public static float LastAverageMs { get; private set; }
        public static float LastWorstMs { get; private set; }
        public static float LastFps { get; private set; }
        public static int LastSlowFrames { get; private set; }

        private struct FrameStart { }
        private struct FrameEnd { }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (ServerSingleCopy.AnotherCopyRuns) return; // a second copy on this PC writes no log of its own
            PlayerLoopSystem loop = PlayerLoop.GetCurrentPlayerLoop();
            for (int i = 0; i < loop.subSystemList.Length; i++)
            {
                Type phase = loop.subSystemList[i].type;
                if (phase == typeof(PreUpdate)) loop.subSystemList[i] = With(loop.subSystemList[i], typeof(FrameStart), OnFrameStart, first: true);
                else if (phase == typeof(PostLateUpdate)) loop.subSystemList[i] = With(loop.subSystemList[i], typeof(FrameEnd), OnFrameEnd, first: false);
            }
            PlayerLoop.SetPlayerLoop(loop);
            Application.quitting += Close;
        }

        private static PlayerLoopSystem With(PlayerLoopSystem phase, Type type, PlayerLoopSystem.UpdateFunction call, bool first)
        {
            var list = new List<PlayerLoopSystem>(phase.subSystemList ?? new PlayerLoopSystem[0]);
            var system = new PlayerLoopSystem { type = type, updateDelegate = call };
            if (first) list.Insert(0, system);
            else list.Add(system);
            phase.subSystemList = list.ToArray();
            return phase;
        }

        private static void OnFrameStart() => Clock.Restart();

        private static void OnFrameEnd()
        {
            if (!Clock.IsRunning) return;
            Busy.Add((float)Clock.Elapsed.TotalMilliseconds);
            frames++;
            float now = Time.realtimeSinceStartup;
            if (windowStart < 0f) windowStart = now;
            if (now - windowStart < WindowSeconds) return;
            Write(now - windowStart);
            windowStart = now;
            frames = 0;
            Busy.Clear();
        }

        private static void Write(float seconds)
        {
            Busy.Sort();
            float sum = 0f;
            int slow = 0;
            foreach (float ms in Busy)
            {
                sum += ms;
                if (ms > TickMilliseconds) slow++;
            }
            float average = Busy.Count > 0 ? sum / Busy.Count : 0f;
            float p95 = Busy.Count > 0 ? Busy[Mathf.Min(Busy.Count - 1, (int)(Busy.Count * 0.95f))] : 0f;
            float worst = Busy.Count > 0 ? Busy[Busy.Count - 1] : 0f;
            float fps = seconds > 0f ? frames / seconds : 0f;
            NetworkManager network = NetworkManager.Singleton;
            int players = network != null && network.IsServer ? network.ConnectedClientsIds.Count : 0;
            long memory = GC.GetTotalMemory(false) / (1024 * 1024);
            long reserved = UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong() / (1024 * 1024);
            // Lag beside the load (owner, 18 Oct 2026): late hits from everyone at once point at the server, from one player at their line.
            NetworkHealth.TakeTotals(out int lateHits, out int goneTargets, out int worstPing);

            LastAverageMs = average;
            LastWorstMs = worst;
            LastFps = fps;
            LastSlowFrames = slow;
            Summary = $"{players} players · {fps:0} fps · {average:0.0} ms average · {p95:0.0} ms 95% · {worst:0.0} ms worst · {slow} slow";

            try
            {
                if (file != null && DateTime.Now.Date != fileDay) Close(); // a new file each day
                if (file == null) Open();
                file?.WriteLine(string.Join(",",
                    DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture), players.ToString(CultureInfo.InvariantCulture),
                    fps.ToString("0.0", CultureInfo.InvariantCulture), average.ToString("0.00", CultureInfo.InvariantCulture),
                    p95.ToString("0.00", CultureInfo.InvariantCulture), worst.ToString("0.00", CultureInfo.InvariantCulture),
                    slow.ToString(CultureInfo.InvariantCulture), Busy.Count.ToString(CultureInfo.InvariantCulture),
                    memory.ToString(CultureInfo.InvariantCulture), reserved.ToString(CultureInfo.InvariantCulture),
                    lateHits.ToString(CultureInfo.InvariantCulture), goneTargets.ToString(CultureInfo.InvariantCulture),
                    worstPing.ToString(CultureInfo.InvariantCulture)));
                file?.Flush();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Server] The performance log could not be written: " + e.Message);
                file = null;
            }
        }

        private static void Open()
        {
            ServerLaunchOptions options = ServerLaunchOptions.LoadForThisServer();
            string folder = options.PerformanceLogFolderPath;
            Directory.CreateDirectory(folder);
            // Map servers share the folder (online phase 5): each file says its map.
            string map = options.IsMapServer ? "_" + options.map : string.Empty;
            foreach (string old in ToDelete(Directory.GetFiles(folder, "server_*.csv"), map, KeepFiles - 1))
                try { File.Delete(old); } catch (Exception) { /* in use or gone: the next start tries again */ }
            fileDay = DateTime.Now.Date;
            string path = Path.Combine(folder, $"server_{DateTime.Now:yyyyMMdd_HHmmss}{map}.csv");
            file = new StreamWriter(path, false);
            file.WriteLine("time,players,fps,busy_avg_ms,busy_p95_ms,busy_worst_ms,slow_frames,frames,managed_mb,reserved_mb,late_hits,gone_targets,worst_ping_ms");
            Debug.Log("[Server] Performance log: " + path);
        }

        /// <summary>
        /// The oldest logs of one server (its map suffix, empty for the listed server) beyond the newest <paramref name="keep"/>.
        /// Another map's logs in the same folder are never counted or touched.
        /// </summary>
        public static List<string> ToDelete(IEnumerable<string> files, string map, int keep)
        {
            var own = new List<string>();
            foreach (string path in files)
            {
                string name = Path.GetFileNameWithoutExtension(path); // server_yyyyMMdd_HHmmss[_map]
                if (name.Length == "server_yyyyMMdd_HHmmss".Length + map.Length && name.EndsWith(map, StringComparison.Ordinal)) own.Add(path);
            }
            own.Sort(StringComparer.Ordinal); // the name carries the start time
            return own.GetRange(0, Math.Max(0, own.Count - Math.Max(0, keep)));
        }

        private static void Close()
        {
            try { file?.Dispose(); } catch (Exception) { /* closing anyway */ }
            file = null;
        }
    }
}
#endif
