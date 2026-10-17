#if UNITY_SERVER
using System.Runtime.InteropServices;
using System.Threading;
using KryticalNova.Persistence;
using Unity.Netcode;
using UnityEngine;

namespace KryticalNova.Networking
{
    /// <summary>
    /// A dedicated server's life: its settings at boot (<see cref="ServerLaunchOptions"/>), a backup at start and every
    /// few hours (<see cref="ServerBackups"/>), and a safe stop. Closing the console window, Ctrl+C, logging off, a
    /// Windows shutdown and <c>gm.shutdown</c> all save every player and the world, back up, tell the players why, and
    /// only then quit. A restart quits the same way with exit code <see cref="RestartExitCode"/>, which
    /// <c>Start Server.bat</c> answers by starting the server again. Only in a server build.
    /// </summary>
    public sealed class ServerLifetime : MonoBehaviour
    {
        // Windows gives a closing console window about five seconds before it ends the program.
        private const int ConsoleWaitMilliseconds = 4500;
        private const float QuitDelaySeconds = 1f; // the players' last messages go out first
        /// <summary>The exit code of a restart: the start script starts the server again.</summary>
        public const int RestartExitCode = 3;
        private static readonly int[] AnnounceAt = { 300, 120, 60, 30, 10, 5, 4, 3, 2, 1 };

        private static ServerLifetime instance;
        private static int consoleStop;  // set by the console's thread, read by the game's
        private static readonly ManualResetEventSlim Stopped = new ManualResetEventSlim(false);
        private static ConsoleHandler handler; // held, or the garbage collector frees the callback Windows calls

        private ServerLaunchOptions options;
        private float nextBackupAt = float.PositiveInfinity;
        private float shutdownAt = -1f;
        private string shutdownReason;
        private bool shutdownRestarts;
        private int nextAnnouncement;
        private bool stopping;
        private bool restarting;
        private float startedAt;

        /// <summary>The newest backup's log line, for the control page.</summary>
        public static string LastBackup { get; private set; } = "none yet";
        /// <summary>Seconds the server has been running.</summary>
        public static float UptimeSeconds => instance != null ? Time.realtimeSinceStartup - instance.startedAt : 0f;
        /// <summary>Seconds to the next timed backup, or -1 when there is none.</summary>
        public static float NextBackupSeconds => instance != null && !float.IsInfinity(instance.nextBackupAt) ? instance.nextBackupAt - Time.realtimeSinceStartup : -1f;
        /// <summary>Seconds left on a shutdown countdown, or -1; and whether it restarts.</summary>
        public static float ShutdownSeconds => instance != null && instance.shutdownAt >= 0f ? instance.shutdownAt - Time.realtimeSinceStartup : -1f;
        public static bool ShutdownRestarts => instance != null && instance.shutdownRestarts;

        /// <summary>True while a stop is under way (saves and backups are being written).</summary>
        public static bool Stopping => instance != null && instance.stopping;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Boot()
        {
            // A second copy on this PC quits before its first backup (ServerSingleCopy).
            if (instance != null || ServerSingleCopy.AnotherCopyRuns) return;
            var host = new GameObject("Server Lifetime");
            DontDestroyOnLoad(host);
            instance = host.AddComponent<ServerLifetime>();
            instance.options = ServerLaunchOptions.LoadForThisServer();
            InstallConsoleHandler();
        }

        private void Start()
        {
            startedAt = Time.realtimeSinceStartup;
            Backup("start");
            if (options.backupHours > 0f) nextBackupAt = Time.realtimeSinceStartup + options.backupHours * 3600f;
        }

        private void Update()
        {
            if (Interlocked.Exchange(ref consoleStop, 0) == 1 && !stopping)
            {
                StopNow("The server was closed.");
                return;
            }
            if (stopping) return;
            if (Time.realtimeSinceStartup >= nextBackupAt)
            {
                Backup("timed");
                nextBackupAt = Time.realtimeSinceStartup + options.backupHours * 3600f;
            }
            if (shutdownAt >= 0f) TickShutdown();
        }

        /// <summary>Makes a backup now (after saving everyone). Returns the log line.</summary>
        public static string Backup(string reason)
        {
            ServerLaunchOptions o = ServerLaunchOptions.LoadForThisServer();
            string line = ServerBackups.MakeNow(ServerSave.DefaultRoot(), o.BackupFolderPath, o.backupsKept, reason);
            Debug.Log(line);
            if (!line.StartsWith("[Backup] Nothing", System.StringComparison.Ordinal)) LastBackup = line.Replace("[Backup] ", string.Empty);
            return line;
        }

        /// <summary>
        /// Stops the server after <paramref name="seconds"/>, telling the players as it counts down; with
        /// <paramref name="restart"/> it then starts again (through Start Server.bat).
        /// </summary>
        public static string ScheduleShutdown(float seconds, string reason, bool restart = false)
        {
            if (instance == null) return "No server is running.";
            if (instance.stopping) return "The server is already stopping.";
            seconds = Mathf.Max(0f, seconds);
            instance.shutdownAt = Time.realtimeSinceStartup + seconds;
            instance.shutdownRestarts = restart;
            instance.shutdownReason = string.IsNullOrWhiteSpace(reason)
                ? (restart ? "The server is restarting. Join again in a minute." : "The server is shutting down for maintenance.")
                : reason.Trim();
            instance.nextAnnouncement = 0;
            // The first notice is this one: the countdown's next comes at the first mark below it.
            while (instance.nextAnnouncement < AnnounceAt.Length && AnnounceAt[instance.nextAnnouncement] >= seconds) instance.nextAnnouncement++;
            string what = restart ? "restarts" : "shuts down";
            PlayerAnnouncement($"The server {what} in {Describe(seconds)}. {instance.shutdownReason}");
            return $"{(restart ? "Restart" : "Shutdown")} in {Describe(seconds)}: {instance.shutdownReason} (gm.shutdown cancel stops it).";
        }

        /// <summary>Calls off a shutdown that is counting down.</summary>
        public static string CancelShutdown()
        {
            if (instance == null || instance.shutdownAt < 0f) return "No shutdown is counting down.";
            instance.shutdownAt = -1f;
            PlayerAnnouncement("The shutdown was called off.");
            return "Shutdown called off.";
        }

        private void TickShutdown()
        {
            float left = shutdownAt - Time.realtimeSinceStartup;
            if (left <= 0f)
            {
                shutdownAt = -1f;
                StopNow(shutdownReason, shutdownRestarts);
                return;
            }
            if (nextAnnouncement < AnnounceAt.Length && left <= AnnounceAt[nextAnnouncement])
            {
                PlayerAnnouncement($"The server {(shutdownRestarts ? "restarts" : "shuts down")} in {Describe(AnnounceAt[nextAnnouncement])}.");
                nextAnnouncement++;
            }
        }

        /// <summary>Saves everyone, backs up, sends the players away with <paramref name="reason"/> and quits (or restarts).</summary>
        public static void StopNow(string reason, bool restart = false)
        {
            if (instance == null || instance.stopping) return;
            instance.stopping = true;
            instance.restarting = restart;
            Debug.Log("[Server] Stopping: " + reason + " Saving every player and the world...");
            ServerSave.FlushAll();
            Backup("stop");
            NetworkManager network = NetworkManager.Singleton;
            if (network != null && network.IsServer)
                foreach (ulong client in new System.Collections.Generic.List<ulong>(network.ConnectedClientsIds))
                    if (client != NetworkManager.ServerClientId) network.DisconnectClient(client, reason);
            Debug.Log(restart ? "[Server] Saved. Restarting." : "[Server] Saved. Stopping.");
            instance.Invoke(nameof(Quit), QuitDelaySeconds);
        }

        // A closing console window is held until here (Windows ends the program as soon as it is let go), so the
        // players' "server closed" messages have had their second to go out.
        private void Quit()
        {
            Stopped.Set();
            Application.Quit(restarting ? RestartExitCode : 0);
        }

        private static void PlayerAnnouncement(string text)
        {
            Debug.Log("[Server] " + text);
            KryticalNova.Social.PlayerChat.ServerBroadcastGm(text, "Server");
        }

        private static string Describe(float seconds)
        {
            int s = Mathf.CeilToInt(seconds);
            if (s >= 120 && s % 60 == 0) return (s / 60) + " minutes";
            if (s == 60) return "1 minute";
            return s == 1 ? "1 second" : s + " seconds";
        }

        // ------------------------------------------------------------------ the console window (Windows)

        private delegate bool ConsoleHandler(int eventType);

#if UNITY_STANDALONE_WIN
        [DllImport("kernel32.dll")]
        private static extern bool SetConsoleCtrlHandler(ConsoleHandler routine, bool add);
#endif

        private static void InstallConsoleHandler()
        {
#if UNITY_STANDALONE_WIN
            handler = OnConsoleEvent;
            if (!SetConsoleCtrlHandler(handler, true)) Debug.LogWarning("[Server] Could not watch the console window: closing it will not save first.");
#endif
        }

        // Ctrl+C, Ctrl+Break, the window's close button, logging off or shutting Windows down. Called on a thread of
        // Windows' own: the game's thread saves, and this one waits for it (Windows ends the program once it returns).
        private static bool OnConsoleEvent(int eventType)
        {
            Interlocked.Exchange(ref consoleStop, 1);
            Stopped.Wait(ConsoleWaitMilliseconds);
            return true;
        }
    }
}
#endif
