using System;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace KryticalNova.Networking.Entities
{
    /// <summary>
    /// The few dedicated-server settings the Entities bootstrap needs, read the way the Netcode for GameObjects server reads them
    /// (<c>server-settings.json</c> beside the exe, then the command line, which wins) but without naming any NGO type.
    /// Field names match the NGO file so one file serves both builds. Only reads: it never writes the file.
    /// </summary>
    [Serializable]
    public sealed class KnServerSettings
    {
        public const string FileName = "server-settings.json";
        public const int DefaultPort = 7777;

        // JsonUtility field names: the same keys as server-settings.json.
        public int port = DefaultPort;
        public int pagePort = 8090;
        public int maxPlayers = 100;
        public string map = "Zone01_StarterTown";
        public string saveService = "";
        /// <summary>The save service key file ("" = Documents\KryticalNova-Secrets\save-service-key.txt). Also the key of <c>POST /stop</c>.</summary>
        public string saveServiceKeyFile = "";
        /// <summary>DTLS certificate and private key (PEM). Both set = the server wants encryption (the driver constructor wires it: KnTransport).</summary>
        public string certificateFile = "";
        public string privateKeyFile = "";
        /// <summary>"direct" (players come to this machine's UDP port, DTLS when the certificate is there) or "relay" (through Unity Relay).</summary>
        public string connectionMode = "direct";
        /// <summary>
        /// A release server runs without encryption only when this says so. Without it, a missing or unreadable certificate keeps
        /// the server from starting (a development run only warns).
        /// </summary>
        public bool plainTransport;
        public string serverName = "Krytical Nova Server";
        /// <summary>Takes development bot tokens (thin-client soak). Ignored, with a warning, in a build that has no bots compiled in.</summary>
        public bool devBots;
        /// <summary>The Unity project the sign-in tokens must belong to; "" = this build's own (<c>Application.cloudProjectId</c>).</summary>
        public string cloudProjectId = "";
        /// <summary>The most seconds the stop waits for the players' connections to close before it force-closes them and quits (default <see cref="KnNetDefaults.StopGraceSeconds"/>).</summary>
        public float stopGraceSeconds = KnNetDefaults.StopGraceSeconds;

        /// <summary>"main" or "map" (<c>-role</c> on the command line only, as in the NGO server).</summary>
        [NonSerialized] public string role = "main";

        /// <summary>The status/control page port: <c>-pageport</c>, else the file's pagePort (0 = off).</summary>
        public int PagePort => pagePort;

        /// <summary>True when this server saves through the save service (the file names it); otherwise it saves to files.</summary>
        public bool UsesSaveService => !string.IsNullOrWhiteSpace(saveService);

        /// <summary>True when a certificate and a private key are both named.</summary>
        public bool WantsEncryption => !string.IsNullOrWhiteSpace(certificateFile) && !string.IsNullOrWhiteSpace(privateKeyFile);

        /// <summary>The key file in use: the named one, else the owner's secrets folder.</summary>
        public string SaveServiceKeyPath => !string.IsNullOrWhiteSpace(saveServiceKeyFile) ? saveServiceKeyFile
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "KryticalNova-Secrets", "save-service-key.txt");

        /// <summary>
        /// The save service key text (trimmed), or "" when this server has no save service or no key file. Never logged. The
        /// stop endpoint needs it: without a key nothing may ask the server to stop through the page.
        /// </summary>
        public string ReadStopKey()
        {
            try
            {
                if (UsesSaveService && File.Exists(SaveServiceKeyPath)) return File.ReadAllText(SaveServiceKeyPath).Trim();
            }
            catch (Exception) { /* no key: the stop endpoint stays shut */ }
            return string.Empty;
        }

        /// <summary>Defaults, then the file's text (null or empty = no file), then the arguments.</summary>
        public static KnServerSettings Parse(string json, string[] args)
        {
            var s = new KnServerSettings();
            if (!string.IsNullOrWhiteSpace(json))
            {
                try { JsonUtility.FromJsonOverwrite(json, s); }
                catch (Exception e) { Debug.LogWarning($"[KnServer] server-settings.json could not be read ({e.Message}); using defaults."); }
            }
            s.ApplyArguments(args);
            s.Clamp();
            return s;
        }

        /// <summary>The file beside the exe (the folder above <c>Application.dataPath</c>) and <c>Environment.GetCommandLineArgs()</c>.</summary>
        public static KnServerSettings LoadForThisServer()
        {
            string json = null;
            try
            {
                string file = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? string.Empty, FileName);
                if (File.Exists(file)) json = File.ReadAllText(file);
            }
            catch (Exception e) { Debug.LogWarning($"[KnServer] could not read {FileName} ({e.Message}); using defaults."); }
            return Parse(json, Environment.GetCommandLineArgs());
        }

        private void ApplyArguments(string[] args)
        {
            if (args == null) return;
            foreach (string flag in args)
            {
                if (string.Equals(flag, "-devbots", StringComparison.OrdinalIgnoreCase)) devBots = true;
                if (string.Equals(flag, "-plaintransport", StringComparison.OrdinalIgnoreCase)) plainTransport = true;
            }
            for (int i = 0; i < args.Length - 1; i++)
            {
                string value = args[i + 1];
                switch (args[i].ToLowerInvariant())
                {
                    case "-port": port = Int(value, port); break;
                    case "-pageport": pagePort = Int(value, pagePort); break;
                    case "-maxplayers": maxPlayers = Int(value, maxPlayers); break;
                    case "-map": map = value; break;
                    case "-savekeyfile": saveServiceKeyFile = value; break;
                    case "-certificate": certificateFile = value; break;
                    case "-privatekey": privateKeyFile = value; break;
                    case "-connectionmode": connectionMode = value; break;
                    case "-role": role = string.Equals(value.Trim(), "map", StringComparison.OrdinalIgnoreCase) ? "map" : "main"; break;
                }
            }
        }

        private static int Int(string text, int fallback) =>
            int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : fallback;

        /// <summary>The real server's ports (game, query, real save service, page): a test-lane server never touches them.</summary>
        public static readonly int[] RealLanePorts = { 7777, 7778, 7801, 8090 };

        /// <summary>
        /// Null when this server keeps off the real lane; else why not. Checked by a test-lane build (<c>KN_TEST_LANE</c>, the B3 Linux
        /// gate) before its world is made: the game port, the page port and the save service's port must all be other than
        /// <see cref="RealLanePorts"/> (the default game port is 7777, so a test server always names its <c>-port</c>).
        /// </summary>
        public string TestLaneProblem()
        {
            if (Array.IndexOf(RealLanePorts, port) >= 0) return "game port " + port + " belongs to the real server (use -port 7797)";
            if (Array.IndexOf(RealLanePorts, pagePort) >= 0) return "page port " + pagePort + " belongs to the real server (use -pageport 7798)";
            if (!string.IsNullOrWhiteSpace(saveService))
            {
                if (!Uri.TryCreate(saveService.Trim(), UriKind.Absolute, out Uri uri)) return "the save service address \"" + saveService + "\" cannot be read";
                if (Array.IndexOf(RealLanePorts, uri.Port) >= 0) return "save service port " + uri.Port + " is the real save service (the test lane is 7802)";
            }
            return null;
        }

        private void Clamp()
        {
            if (port < 1 || port > 65535) port = DefaultPort;
            if (pagePort < 0 || pagePort > 65535) pagePort = 0;
            maxPlayers = Mathf.Clamp(maxPlayers, 1, 1000);
            if (string.IsNullOrWhiteSpace(map)) map = "Zone01_StarterTown";
            connectionMode = string.Equals((connectionMode ?? string.Empty).Trim(), KnTransportPlanner.RelayMode, StringComparison.OrdinalIgnoreCase)
                ? KnTransportPlanner.RelayMode : KnTransportPlanner.DirectMode;
        }
    }
}
