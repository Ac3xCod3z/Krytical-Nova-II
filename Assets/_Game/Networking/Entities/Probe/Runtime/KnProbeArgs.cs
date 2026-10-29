#if KN_PROBE || UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Globalization;
using System.Text;

namespace KryticalNova.Networking.Entities.Probe
{
    public enum KnProbeRole { None, Server, Client }

    public enum KnProbeAction { Connect, Roster, Idle }

    /// <summary>
    /// The command line of a probe process (M2c). Pure: no Unity call, so EditMode tests parse arguments without a player.
    /// <c>-knprobe server|client</c> switches the probe on; without it nothing here does anything.
    /// Client flags: <c>-knaddr</c> (127.0.0.1), <c>-knport</c> (7797), <c>-knpageport</c> (7798, the server page used to tell a kick from a
    /// server stop), <c>-knaction connect|roster|idle</c> (connect = join and stay; roster = ask the roster first, then join; idle = same as
    /// connect, named for the client that only waits to be kicked), <c>-knquit-after</c> seconds connected (0 = stay), <c>-kntimeout</c>
    /// seconds to connect (20), <c>-knname</c> (A, B, ...), <c>-knout</c> folder of the result file (default: beside the exe).
    /// The server takes its ports from the normal server flags (<c>-port</c>, <c>-pageport</c>, <c>server-settings.json</c>).
    /// </summary>
    public sealed class KnProbeArgs
    {
        public const int DefaultPort = 7797;
        public const int DefaultPagePort = 7798;
        /// <summary>The ports a probe may ever use. The game's own (7777, 7778, 8090) and the save service (7801, 7802) are never among them.</summary>
        public static readonly int[] AllowedPorts = { 7797, 7798, 8097 };

        public KnProbeRole Role = KnProbeRole.None;
        public string Address = "127.0.0.1";
        public int Port = DefaultPort;
        public int PagePort = DefaultPagePort;
        public KnProbeAction Action = KnProbeAction.Connect;
        public double QuitAfterSeconds;
        public double TimeoutSeconds = 20;
        public string Name = "";
        public string OutDir = "";
        /// <summary>Non-null when an argument could not be used (the probe then stops with exit code 1).</summary>
        public string Error;

        /// <summary>True when <c>-knprobe</c> was on the line at all (even with a value that is not understood).</summary>
        public bool Requested;

        public bool Enabled => Role != KnProbeRole.None;

        /// <summary>The 32-character save id this client plays under (the join check takes it as the made-up account of an unsigned game).</summary>
        public string Account => AccountFor(Name);

        public static KnProbeArgs Parse(string[] args)
        {
            var p = new KnProbeArgs();
            if (args == null) return p;
            for (int i = 0; i < args.Length; i++)
            {
                string flag = (args[i] ?? string.Empty).ToLowerInvariant();
                bool hasValue = i + 1 < args.Length;
                string value = hasValue ? args[i + 1] : null;
                switch (flag)
                {
                    case "-knprobe":
                        p.Requested = true;
                        if (!hasValue) { p.Error = "-knprobe needs server or client"; break; }
                        string role = value.Trim().ToLowerInvariant();
                        if (role == "server") p.Role = KnProbeRole.Server;
                        else if (role == "client") p.Role = KnProbeRole.Client;
                        else p.Error = "-knprobe " + value + " is not server or client";
                        i++;
                        break;
                    case "-knaddr":
                        if (hasValue) { p.Address = value; i++; } else p.Error = "-knaddr needs an address";
                        break;
                    case "-knport":
                        p.Port = Int(value, p.Port, "-knport", p);
                        if (hasValue) i++;
                        break;
                    case "-knpageport":
                        p.PagePort = Int(value, p.PagePort, "-knpageport", p);
                        if (hasValue) i++;
                        break;
                    case "-knaction":
                        if (!hasValue) { p.Error = "-knaction needs connect, roster or idle"; break; }
                        switch (value.Trim().ToLowerInvariant())
                        {
                            case "connect": p.Action = KnProbeAction.Connect; break;
                            case "roster": p.Action = KnProbeAction.Roster; break;
                            case "idle": p.Action = KnProbeAction.Idle; break;
                            default: p.Error = "-knaction " + value + " is not connect, roster or idle"; break;
                        }
                        i++;
                        break;
                    case "-knquit-after":
                        p.QuitAfterSeconds = Double(value, p.QuitAfterSeconds, "-knquit-after", p);
                        if (hasValue) i++;
                        break;
                    case "-kntimeout":
                        p.TimeoutSeconds = Double(value, p.TimeoutSeconds, "-kntimeout", p);
                        if (hasValue) i++;
                        break;
                    case "-knname":
                        if (hasValue) { p.Name = value; i++; } else p.Error = "-knname needs a name";
                        break;
                    case "-knout":
                        if (hasValue) { p.OutDir = value; i++; } else p.Error = "-knout needs a folder";
                        break;
                }
            }
            if (p.Enabled && p.Error == null) p.Error = ClientProblem(p);
            return p;
        }

        /// <summary>A client probe never touches a port that is not on the list; the reason in words, or null.</summary>
        public static string ClientProblem(KnProbeArgs p)
        {
            if (p.Role != KnProbeRole.Client) return null;
            if (Array.IndexOf(AllowedPorts, p.Port) < 0) return "port " + p.Port + " is not a probe port (7797, 7798, 8097)";
            if (p.PagePort != 0 && Array.IndexOf(AllowedPorts, p.PagePort) < 0) return "page port " + p.PagePort + " is not a probe port (7797, 7798, 8097)";
            if (p.TimeoutSeconds < 1) return "-kntimeout must be at least 1 second";
            return null;
        }

        /// <summary>The reason in words when the server settings name a port that is not a probe port; null when all are fine. The probe server refuses to listen otherwise.</summary>
        public static string ServerPortProblem(int port, int pagePort)
        {
            if (Array.IndexOf(AllowedPorts, port) < 0) return "the game port " + port + " is not a probe port (7797, 7798, 8097)";
            if (pagePort != 0 && Array.IndexOf(AllowedPorts, pagePort) < 0) return "the page port " + pagePort + " is not a probe port (7797, 7798, 8097)";
            return null;
        }

        /// <summary>Deterministic 32 lower-case hex characters for a client name ("A" always gets the same id).</summary>
        public static string AccountFor(string name)
        {
            ulong h1 = 14695981039346656037UL, h2 = 1099511628211UL;
            foreach (char c in "kn-probe:" + (name ?? string.Empty))
            {
                unchecked
                {
                    h1 = (h1 ^ c) * 1099511628211UL;
                    h2 = (h2 + c) * 6364136223846793005UL + 1442695040888963407UL;
                }
            }
            var sb = new StringBuilder(32);
            sb.Append(h1.ToString("x16", CultureInfo.InvariantCulture));
            sb.Append(h2.ToString("x16", CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        private static int Int(string text, int fallback, string flag, KnProbeArgs p)
        {
            if (text != null && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v)) return v;
            p.Error = flag + " needs a whole number";
            return fallback;
        }

        private static double Double(string text, double fallback, string flag, KnProbeArgs p)
        {
            if (text != null && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && v >= 0) return v;
            p.Error = flag + " needs a number of seconds";
            return fallback;
        }
    }
}
#endif
