#if UNITY_SERVER || UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace KryticalNova.Networking
{
    /// <summary>
    /// How a dedicated server runs: <c>server-settings.json</c> beside its exe, then the command line, which wins.
    /// The first start writes the file with every setting at its default, for the owner to edit with Notepad. Nothing
    /// about a machine is in code or scenes (online roadmap, rule 2). Only in a server build (and the Editor, for tests):
    /// a player's game never carries it, and the control page's password lives only in the file.
    /// </summary>
    [Serializable]
    public sealed class ServerLaunchOptions
    {
        public const string FileName = "server-settings.json";
        public const string DefaultSaveFolderName = "KryticalNovaServer-Save";

        [Tooltip("The name players see in the server list.")]
        public string serverName = "Krytical Nova Server";
        [Tooltip("The UDP port players connect to. The router forwards this port to the server PC.")]
        public int port = 7777;
        [Tooltip("Players at once; one more is turned away with \"The server is full\".")]
        public int maxPlayers = 100;
        [Tooltip("Where the saves are kept. Empty: a folder named KryticalNovaServer-Save beside the server's own folder, so a new build never touches it. A relative path is from the server's folder.")]
        public string saveFolder = "";
        [Tooltip("The map the server opens with (a scene in the build).")]
        public string map = "Zone01_StarterTown";
        [Tooltip("direct: players connect to this PC's address and port (port forwarding). relay: through Unity Relay (not built yet).")]
        public string connectionMode = "direct";
        [Tooltip("The control page's port, on the home network only. 0 switches the page off.")]
        public int pagePort = 8090;
        [Tooltip("The control page's password. Only ever in this file; when empty the server makes one and writes it here.")]
        public string pagePassword = "";
        [Tooltip("Hours between backups of the save folder; 0 backs up only at start and stop.")]
        public float backupHours = 6f;
        [Tooltip("How many backups are kept; the oldest go first.")]
        public int backupsKept = 10;

        /// <summary>The options of this server, read at boot (<see cref="LoadForThisServer"/>). Null before.</summary>
        public static ServerLaunchOptions Current { get; private set; }

        /// <summary>The folder the server's exe is in, where the settings file lives.</summary>
        [NonSerialized] public string ServerFolder;

        /// <summary>The save folder as a full path.</summary>
        public string SaveFolderPath
        {
            get
            {
                if (string.IsNullOrWhiteSpace(saveFolder))
                {
                    string parent = Path.GetDirectoryName(ServerFolder.TrimEnd('/', '\\'));
                    return Path.GetFullPath(Path.Combine(parent ?? ServerFolder, DefaultSaveFolderName));
                }
                return Path.GetFullPath(Path.IsPathRooted(saveFolder) ? saveFolder : Path.Combine(ServerFolder, saveFolder));
            }
        }

        /// <summary>Where backups of the save folder go: beside it, its name with "-Backups".</summary>
        public string BackupFolderPath
        {
            get
            {
                string save = SaveFolderPath.TrimEnd('/', '\\');
                return Path.Combine(Path.GetDirectoryName(save) ?? save, Path.GetFileName(save) + "-Backups");
            }
        }

        /// <summary>Reads this server's options once, logs them, and keeps them as <see cref="Current"/>.</summary>
        public static ServerLaunchOptions LoadForThisServer()
        {
            if (Current != null) return Current;
            string folder = Path.GetDirectoryName(Application.dataPath);
            Current = Load(folder, Environment.GetCommandLineArgs(), out string report);
            Debug.Log(report);
            return Current;
        }

        /// <summary>
        /// The options from <paramref name="serverFolder"/>'s settings file (written with the defaults when there is
        /// none) and then the arguments. A file that cannot be read is left as it is and the defaults are used.
        /// </summary>
        public static ServerLaunchOptions Load(string serverFolder, string[] args, out string report)
        {
            var notes = new List<string>();
            var options = new ServerLaunchOptions { ServerFolder = serverFolder };
            string file = Path.Combine(serverFolder, FileName);
            try
            {
                if (File.Exists(file))
                {
                    JsonUtility.FromJsonOverwrite(File.ReadAllText(file), options);
                    options.ServerFolder = serverFolder;
                    notes.Add("settings from " + file);
                }
                else
                {
                    File.WriteAllText(file, JsonUtility.ToJson(options, true));
                    notes.Add("no settings file: wrote one with the defaults at " + file);
                }
            }
            catch (Exception e)
            {
                options = new ServerLaunchOptions { ServerFolder = serverFolder };
                notes.Add($"could not read {file} ({e.Message}); using the defaults, the file is left as it is");
            }
            options.ApplyArguments(args, notes);
            options.Clamp(notes);
            report = $"[Server] {options.serverName}: port {options.port}, up to {options.maxPlayers} players, map {options.map}, " +
                     $"{options.connectionMode} connections, saves in {options.SaveFolderPath}, backups every {options.backupHours:0.##} h " +
                     $"(last {options.backupsKept} kept) in {options.BackupFolderPath}. " + string.Join("; ", notes) + ".";
            return options;
        }

        /// <summary>Command-line overrides: -name, -port, -maxPlayers, -saveFolder, -map, -mode, -pagePort, -backupHours, -backupsKept.</summary>
        public void ApplyArguments(string[] args, List<string> notes)
        {
            if (args == null) return;
            for (int i = 0; i < args.Length - 1; i++)
            {
                string key = args[i], value = args[i + 1];
                switch (key.ToLowerInvariant())
                {
                    case "-name": serverName = value; break;
                    case "-port": port = Int(value, port, key, notes); break;
                    case "-maxplayers": maxPlayers = Int(value, maxPlayers, key, notes); break;
                    case "-savefolder": saveFolder = value; break;
                    case "-map": map = value; break;
                    case "-mode": connectionMode = value; break;
                    case "-pageport": pagePort = Int(value, pagePort, key, notes); break;
                    case "-backuphours":
                        if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float hours)) backupHours = hours;
                        else notes.Add($"{key} \"{value}\" is not a number");
                        break;
                    case "-backupskept": backupsKept = Int(value, backupsKept, key, notes); break;
                    default: continue;
                }
                notes.Add($"{key} from the command line");
            }
        }

        private void Clamp(List<string> notes)
        {
            if (port < 1 || port > 65535) { notes.Add($"port {port} is not a port: 7777"); port = 7777; }
            maxPlayers = Mathf.Clamp(maxPlayers, 1, 1000);
            if (backupHours < 0f) backupHours = 0f;
            backupsKept = Mathf.Max(1, backupsKept);
            if (string.IsNullOrWhiteSpace(serverName)) serverName = "Krytical Nova Server";
            if (string.IsNullOrWhiteSpace(map)) map = "Zone01_StarterTown";
            connectionMode = string.IsNullOrWhiteSpace(connectionMode) ? "direct" : connectionMode.Trim().ToLowerInvariant();
            if (connectionMode != "direct") { notes.Add($"connection mode \"{connectionMode}\" is not built yet: direct"); connectionMode = "direct"; }
        }

        private static int Int(string value, int fallback, string key, List<string> notes)
        {
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)) return parsed;
            notes.Add($"{key} \"{value}\" is not a whole number");
            return fallback;
        }

        /// <summary>
        /// The control page's password. When the settings have none, one is made, written into the settings file (and
        /// nowhere else) and returned with <paramref name="isNew"/> set, for the server's own window to show once.
        /// </summary>
        public string EnsurePagePassword(out bool isNew)
        {
            isNew = false;
            if (!string.IsNullOrWhiteSpace(pagePassword)) return pagePassword;
            pagePassword = ControlPageRules.NewPassword();
            isNew = true;
            string file = Path.Combine(ServerFolder, FileName);
            try
            {
                // Written into the file as the owner left it: command-line overrides of this run are not saved.
                var onDisk = new ServerLaunchOptions { ServerFolder = ServerFolder };
                if (File.Exists(file)) JsonUtility.FromJsonOverwrite(File.ReadAllText(file), onDisk);
                onDisk.pagePassword = pagePassword;
                File.WriteAllText(file, JsonUtility.ToJson(onDisk, true));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Server] The new control page password could not be written into {file}: {e.Message}. It works until the server stops.");
            }
            return pagePassword;
        }

#if UNITY_EDITOR
        /// <summary>Tests only: forget the options read.</summary>
        public static void ResetForTests() => Current = null;
#endif
    }
}
#endif
