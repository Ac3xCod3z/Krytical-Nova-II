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

        /// <summary>"main" or "map" (<c>-role</c> on the command line only, as in the NGO server).</summary>
        [NonSerialized] public string role = "main";

        /// <summary>The status/control page port: <c>-pageport</c>, else the file's pagePort (0 = off).</summary>
        public int PagePort => pagePort;

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
            for (int i = 0; i < args.Length - 1; i++)
            {
                string value = args[i + 1];
                switch (args[i].ToLowerInvariant())
                {
                    case "-port": port = Int(value, port); break;
                    case "-pageport": pagePort = Int(value, pagePort); break;
                    case "-maxplayers": maxPlayers = Int(value, maxPlayers); break;
                    case "-map": map = value; break;
                    case "-role": role = string.Equals(value.Trim(), "map", StringComparison.OrdinalIgnoreCase) ? "map" : "main"; break;
                }
            }
        }

        private static int Int(string text, int fallback) =>
            int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : fallback;

        private void Clamp()
        {
            if (port < 1 || port > 65535) port = DefaultPort;
            if (pagePort < 0 || pagePort > 65535) pagePort = 0;
            maxPlayers = Mathf.Clamp(maxPlayers, 1, 1000);
            if (string.IsNullOrWhiteSpace(map)) map = "Zone01_StarterTown";
        }
    }
}
