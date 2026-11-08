#if UNITY_SERVER
using KryticalNova.Networking;
using KryticalNova.Networking.Entities;
using UnityEngine;

namespace KryticalNova.Persistence.Entities
{
    /// <summary>
    /// The dedicated entity server's routine chores that NGO's ServerLifetime did (DOTS builds, 8 Nov 2026, asked by dots-ports):
    /// <see cref="ServerReach"/> (this PC's public address, Relay and auto-list notes for the control page; it publishes only when
    /// the list has an "auto" entry) and the save backups through the page's own <see cref="KnPageBrowserHost.Backup"/>: one a minute
    /// after the start, then every <c>backupHours</c>, and one when the server quits (after the stop path has saved everyone). Lives
    /// here, not in KnServerEntry, because Networking.Entities may not use Persistence or NGO's Networking (dependency rules).
    /// </summary>
    public sealed class KnServerRoutine : MonoBehaviour
    {
        const float FirstBackupSeconds = 60f;

        static KnServerRoutine s_Instance;
        ServerLaunchOptions m_Options;
        float m_NextBackup;

        /// <summary>Starts once per process (the server world's setup calls it).</summary>
        public static void Begin()
        {
            if (s_Instance != null) return;
            var host = new GameObject("KN Server Routine");
            DontDestroyOnLoad(host);
            s_Instance = host.AddComponent<KnServerRoutine>();
            s_Instance.m_Options = ServerLaunchOptions.LoadForThisServer();
            ServerReach.Begin(s_Instance.m_Options);
            s_Instance.m_NextBackup = Time.unscaledTime + FirstBackupSeconds;
            Application.quitting += () => Backup("at stop");
            Debug.Log($"[KnServer] Routine: backups a minute after the start, then every {s_Instance.m_Options.backupHours:0.##} h and at stop; reach notes on.");
        }

        void Update()
        {
            if (Time.unscaledTime < m_NextBackup) return;
            float hours = m_Options != null && m_Options.backupHours > 0f ? m_Options.backupHours : 6f;
            m_NextBackup = Time.unscaledTime + hours * 3600f;
            Backup("timed");
        }

        static void Backup(string why)
        {
            if (!(KnPageBrowser.Host is KnPageBrowserHost host)) return;
            try { Debug.Log("[KnServer] Backup (" + why + "): " + host.Backup()); }
            catch (System.Exception e) { Debug.LogWarning("[KnServer] Backup (" + why + ") failed: " + e.Message); }
        }
    }
}
#endif
