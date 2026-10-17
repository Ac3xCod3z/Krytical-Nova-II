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
    /// <c>PerformanceLogs/server_*.csv</c> beside the server: players, frames, busy milliseconds (average, 95th
    /// percentile, worst), frames that overran a tick (more than 33.3 ms) and memory. The control page shows the last
    /// line. A server keeps up while its busy time stays under a tick; the player count where it stops is its cap.
    /// Only in a server build.
    /// </summary>
    public static class ServerPerformanceLog
    {
        public const float WindowSeconds = 5f;
        public const float TickMilliseconds = 1000f / 30f;
        private const string FolderName = "PerformanceLogs";

        private static readonly Stopwatch Clock = new Stopwatch();
        private static readonly List<float> Busy = new List<float>(256);
        private static float windowStart = -1f;
        private static int frames;
        private static StreamWriter file;

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

            LastAverageMs = average;
            LastWorstMs = worst;
            LastFps = fps;
            LastSlowFrames = slow;
            Summary = $"{players} players · {fps:0} fps · {average:0.0} ms average · {p95:0.0} ms 95% · {worst:0.0} ms worst · {slow} slow";

            try
            {
                if (file == null) Open();
                file?.WriteLine(string.Join(",",
                    DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture), players.ToString(CultureInfo.InvariantCulture),
                    fps.ToString("0.0", CultureInfo.InvariantCulture), average.ToString("0.00", CultureInfo.InvariantCulture),
                    p95.ToString("0.00", CultureInfo.InvariantCulture), worst.ToString("0.00", CultureInfo.InvariantCulture),
                    slow.ToString(CultureInfo.InvariantCulture), Busy.Count.ToString(CultureInfo.InvariantCulture),
                    memory.ToString(CultureInfo.InvariantCulture), reserved.ToString(CultureInfo.InvariantCulture)));
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
            string folder = Path.Combine(options.ServerFolder, FolderName);
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, $"server_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
            file = new StreamWriter(path, false);
            file.WriteLine("time,players,fps,busy_avg_ms,busy_p95_ms,busy_worst_ms,slow_frames,frames,managed_mb,reserved_mb");
            Debug.Log("[Server] Performance log: " + path);
        }

        private static void Close()
        {
            try { file?.Dispose(); } catch (Exception) { /* closing anyway */ }
            file = null;
        }
    }
}
#endif
