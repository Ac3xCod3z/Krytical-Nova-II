#if KN_PROBE || UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.IO;
using UnityEngine;

namespace KryticalNova.Networking.Entities.Probe
{
    /// <summary>Why a probe process ended; <see cref="KnProbeExit.Code"/> turns it into the process exit code.</summary>
    public enum KnProbeExitReason
    {
        /// <summary>Still running (never written as a final result).</summary>
        None = 0,
        /// <summary>A client finished its scripted job (<c>-knquit-after</c> reached while connected).</summary>
        QuitAfter,
        /// <summary>The server ended the connection because it is stopping (or is gone): not a kick.</summary>
        ServerClosed,
        /// <summary>The server stopped on a stop request, after saving.</summary>
        Stopped,
        /// <summary>The server turned the join or the roster question away.</summary>
        Refused,
        /// <summary>No NetworkId or no roster answer in time.</summary>
        Timeout,
        /// <summary>Connected, then disconnected by a healthy server: a kick.</summary>
        Kicked,
        /// <summary>Bad arguments, a port that is not a probe port, or an exception.</summary>
        Error,
    }

    /// <summary>The process exit codes of a probe: 0 ok, 1 error, 2 refused, 3 timeout, 4 kicked.</summary>
    public static class KnProbeExit
    {
        public static int Code(KnProbeExitReason reason)
        {
            switch (reason)
            {
                case KnProbeExitReason.QuitAfter:
                case KnProbeExitReason.ServerClosed:
                case KnProbeExitReason.Stopped:
                    return 0;
                case KnProbeExitReason.Refused: return 2;
                case KnProbeExitReason.Timeout: return 3;
                case KnProbeExitReason.Kicked: return 4;
                default: return 1;
            }
        }

        public static string Name(KnProbeExitReason reason)
        {
            switch (reason)
            {
                case KnProbeExitReason.QuitAfter: return "quit-after";
                case KnProbeExitReason.ServerClosed: return "server-closed";
                case KnProbeExitReason.Stopped: return "stopped";
                case KnProbeExitReason.Refused: return "refused";
                case KnProbeExitReason.Timeout: return "timeout";
                case KnProbeExitReason.Kicked: return "kicked";
                case KnProbeExitReason.Error: return "error";
                default: return "running";
            }
        }
    }

    /// <summary>
    /// What a probe process writes to <c>kn-probe-&lt;role&gt;[-&lt;name&gt;].json</c> (machine-readable, one object). A flat class for JsonUtility;
    /// the PowerShell driver reads it with ConvertFrom-Json. Times are UTC milliseconds since 1970 so server and client lines can be ordered.
    /// Server-only and client-only fields stay at their zero values in the other role.
    /// </summary>
    [Serializable]
    public sealed class KnProbeResult
    {
        public string role = "";
        public string name = "";
        public string build = "";
        public long startedUtcMs;
        public long endedUtcMs;
        public string exitReason = "running";
        public int exitCode = -1;
        public string error = "";

        // client
        public bool connected;
        public int networkId = -1;
        public long connectedUtcMs;
        public bool rosterAsked;
        public bool rosterOk;
        public string rosterAnswer = "";
        public string rosterError = "";
        public string refusalReason = "";
        public string refusalDetail = "";
        public bool kicked;
        public long droppedUtcMs;

        // server
        public int listenPort;
        public int pagePort;
        public int joins;
        public string[] playerKeys = new string[0];
        public int[] playerNetworkIds = new int[0];
        public int rosterAnswers;
        public int saveCalls;
        public long saveBeginUtcMs;
        public long saveEndUtcMs;

        public string ToJson() => JsonUtility.ToJson(this, true);

        public static KnProbeResult FromJson(string json) => JsonUtility.FromJson<KnProbeResult>(json);

        public static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        public void AddPlayer(ulong key, int networkId)
        {
            Array.Resize(ref playerKeys, playerKeys.Length + 1);
            playerKeys[playerKeys.Length - 1] = key.ToString(System.Globalization.CultureInfo.InvariantCulture);
            Array.Resize(ref playerNetworkIds, playerNetworkIds.Length + 1);
            playerNetworkIds[playerNetworkIds.Length - 1] = networkId;
            joins++;
        }
    }

    /// <summary>Where the result file goes and how it is written.</summary>
    public static class KnProbeFiles
    {
        /// <summary><c>kn-probe-server.json</c>, <c>kn-probe-client-A.json</c>, <c>kn-probe-client.json</c> (no name).</summary>
        public static string FileName(KnProbeRole role, string name)
        {
            string r = role == KnProbeRole.Server ? "server" : "client";
            return string.IsNullOrEmpty(name) ? "kn-probe-" + r + ".json" : "kn-probe-" + r + "-" + Sanitize(name) + ".json";
        }

        /// <summary>Letters, digits, dash and underscore only: a name from the command line never reaches a path as it is.</summary>
        public static string Sanitize(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            var chars = new char[text.Length];
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                chars[i] = char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_';
            }
            return new string(chars);
        }

        /// <summary>Writes the file whole (a temporary file moved over the old one), so a reader never sees half a result. Returns false when the folder cannot be written.</summary>
        public static bool Write(string folder, KnProbeRole role, string name, KnProbeResult result)
        {
            try
            {
                if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
                string path = Path.Combine(folder ?? string.Empty, FileName(role, name));
                string temp = path + ".tmp";
                File.WriteAllText(temp, result.ToJson());
                if (File.Exists(path)) File.Delete(path);
                File.Move(temp, path);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[KnProbe] could not write the result file: " + e.GetType().Name + ": " + e.Message);
                return false;
            }
        }
    }
}
#endif
