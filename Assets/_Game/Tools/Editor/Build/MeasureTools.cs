using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Measuring how many players a server holds (online phase 1, chunk 7). Everything runs apart from the owner's own
    /// server and save: a copy of the server build in <c>Builds/Bots/MeasureServer</c> on the test ports (game 7790,
    /// control page 8091) with its own save, and bot players: copies of a development build of the game started with no
    /// window (KryticalNova.DevTools.MeasureBot). The bots are given a level, their skills and Autopilot time
    /// through the measure server's own GM commands, from its control page, so the game's rules are not bent for them.
    /// The server writes its load into <c>Builds/Bots/MeasureServer/PerformanceLogs</c>.
    /// </summary>
    public static class MeasureTools
    {
        private const string Menu = "Krytical Nova/Online/Measure/";
        public const string Root = "Builds/Bots";
        public const string ClientFolder = Root + "/Client";
        public const string ServerFolder = Root + "/MeasureServer";
        public const string SaveFolder = Root + "/MeasureSave";
        public const string LogFolder = Root + "/Logs";
        public const int GamePort = 7790;
        public const int PagePort = 8091;
        private const string ClientExe = "KryticalNovaII.exe";
        private const int BotsPerStep = 5;
        private static readonly string[] Equip = { "gm.level 60", "gm.skills.max", "gm.autopilot.time 600" };

        // ------------------------------------------------------------------ the bot client

        [MenuItem(Menu + "1. Build Bot Client", priority = 60)]
        public static void BuildBotClient()
        {
            int build = ReleasePublisher.LastReleaseBuild();
            var output = new TestBuildBuilder.Output
            {
                Folder = ClientFolder,
                ReportPath = Root + "/BuildReport-Bots.txt",
                Title = "Bot client (development, build " + build + ")",
                Options = BuildOptions.Development | BuildOptions.CompressWithLz4HC,
            };
            // The same build number as the server the bots join, or it turns them away.
            bool ok = ReleasePublisher.WithVersion(build, () => TestBuildBuilder.Build(ScriptingImplementation.Mono2x, output));
            Debug.Log(ok ? $"[Measure] Bot client built in {ClientFolder} as build {build}." : "[Measure] The bot client did not build: see " + output.ReportPath);
        }

        // ------------------------------------------------------------------ the measure server

        [MenuItem(Menu + "2. Build Measure Server", priority = 61)]
        public static void BuildMeasureServer()
        {
            if (PageAnswers()) { Debug.LogError("[Measure] Stop the measure server first."); return; }
            bool ok = ServerBuildBuilder.Build(ServerBuildBuilder.Os.Windows, ServerFolder);
            Debug.Log(ok ? "[Measure] Measure server built in " + ServerFolder + "." : "[Measure] The measure server did not build.");
        }

        [MenuItem(Menu + "3. Start Measure Server", priority = 62)]
        public static void StartMeasureServer()
        {
            if (!File.Exists(Path.Combine(ServerFolder, "KryticalNovaServer.exe"))) { Debug.LogError("[Measure] Build the measure server first."); return; }
            if (PageAnswers()) { Debug.Log("[Measure] The measure server is already running."); return; }
            Directory.CreateDirectory(SaveFolder);
            string settingsFile = Path.Combine(ServerFolder, "server-settings.json");
            if (!File.Exists(settingsFile))
            {
                // Its own name, ports and save; no GMs and no server list, so it never publishes anything.
                var settings = new KryticalNova.Networking.ServerLaunchOptions
                {
                    serverName = "Measure server",
                    port = GamePort,
                    maxPlayers = 200,
                    saveFolder = Path.GetFullPath(SaveFolder).Replace('\\', '/'),
                    pagePort = PagePort,
                    pagePassword = NewPassword(),
                    backupHours = 0f,
                    backupsKept = 2,
                    // Online phase 4: the tests' save service and database, never the real saves.
                    saveService = "http://127.0.0.1:7802",
                };
                File.WriteAllText(settingsFile, JsonUtility.ToJson(settings, true));
            }
            Process.Start(new ProcessStartInfo("cmd.exe", "/c \"\"Start Server.bat\" --close\"")
            {
                WorkingDirectory = Path.GetFullPath(ServerFolder),
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Minimized,
            });
            Debug.Log($"[Measure] Measure server starting on port {GamePort} (control page http://localhost:{PagePort}).");
        }

        [MenuItem(Menu + "7. Stop Measure Server", priority = 66)]
        public static void StopMeasureServer()
        {
            string answer = Page("/api/stop", "{\"seconds\":0,\"reason\":\"Measuring done.\",\"restart\":false}");
            Debug.Log("[Measure] " + (answer ?? "The measure server did not answer (already stopped?)."));
        }

        // ------------------------------------------------------------------ bots

        /// <summary>Bots running now.</summary>
        public static int Running => BotProcesses().Count;

        [MenuItem(Menu + "4. Add 5 Bots", priority = 63)]
        public static void AddFive() => AddBots(BotsPerStep);

        /// <summary>Starts more bots, numbered on from the ones running, a second apart.</summary>
        public static int AddBots(int count)
        {
            string exe = Path.GetFullPath(Path.Combine(ClientFolder, ClientExe));
            if (!File.Exists(exe)) { Debug.LogError("[Measure] Build the bot client first."); return 0; }
            Directory.CreateDirectory(LogFolder);
            var taken = new HashSet<int>();
            foreach (Process bot in BotProcesses()) taken.Add(BotNumber(bot));
            int started = 0;
            for (int n = 0; started < count && n < 100; n++)
            {
                if (taken.Contains(n)) continue;
                string log = Path.GetFullPath(Path.Combine(LogFolder, $"bot{n:00}.log"));
                Process process = Process.Start(new ProcessStartInfo(exe,
                    $"-batchmode -nographics -bot {n} -botServer 127.0.0.1:{GamePort} -profile bot{n:00} -logFile \"{log}\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetFullPath(ClientFolder),
                });
                if (process != null) File.AppendAllText(BotListFile, process.Id + " " + n + Environment.NewLine);
                started++;
                System.Threading.Thread.Sleep(1000);
            }
            Debug.Log($"[Measure] Started {started} bot(s); {Running} running.");
            return started;
        }

        [MenuItem(Menu + "5. Equip Bots (level, skills, Autopilot time)", priority = 64)]
        public static void EquipBots()
        {
            string status = Page("/api/status", null);
            if (status == null) { Debug.LogError("[Measure] The measure server's page does not answer."); return; }
            int equipped = 0;
            foreach (Match player in Regex.Matches(status, "\\{\"id\":(\\d+),\"name\":\"(Bot[a-z]+[0-9]?)\""))
            {
                string id = player.Groups[1].Value;
                if (Page("/api/command", "{\"line\":\"gm.target " + id + "\"}") == null) continue;
                foreach (string line in Equip) Page("/api/command", "{\"line\":\"" + line + "\"}");
                equipped++;
            }
            Debug.Log($"[Measure] Equipped {equipped} bot(s): {string.Join(", ", Equip)}.");
        }

        [MenuItem(Menu + "6. Stop All Bots", priority = 65)]
        public static void StopBots()
        {
            int stopped = 0;
            foreach (Process bot in BotProcesses())
            {
                try { bot.Kill(); stopped++; } catch (Exception) { /* already gone */ }
            }
            if (File.Exists(BotListFile)) File.Delete(BotListFile);
            Debug.Log($"[Measure] Stopped {stopped} bot(s).");
        }

        private static List<Process> BotProcesses()
        {
            var found = new List<Process>();
            string folder = Path.GetFullPath(ClientFolder);
            foreach (Process process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(ClientExe)))
            {
                try
                {
                    if (process.MainModule != null && process.MainModule.FileName.StartsWith(folder, StringComparison.OrdinalIgnoreCase)) found.Add(process);
                }
                catch (Exception) { /* exiting, or not ours to read */ }
            }
            return found;
        }

        // Which number each running bot was started with: one line "pid number" per bot, written when it starts.
        private const string BotListFile = Root + "/bots.txt";

        private static int BotNumber(Process bot)
        {
            if (!File.Exists(BotListFile)) return -1;
            foreach (string line in File.ReadAllLines(BotListFile))
            {
                string[] parts = line.Split(' ');
                if (parts.Length == 2 && parts[0] == bot.Id.ToString() && int.TryParse(parts[1], out int n)) return n;
            }
            return -1;
        }

        // ------------------------------------------------------------------ the measure server's page

        private static string pagePassword;

        private static bool PageAnswers() => Page("/api/status", null) != null;

        /// <summary>Calls the measure server's control page (logged in with its password from its settings); null on failure.</summary>
        public static string Page(string path, string body)
        {
            try
            {
                string settings = Path.Combine(ServerFolder, "server-settings.json");
                if (!File.Exists(settings)) return null;
                pagePassword = Regex.Match(File.ReadAllText(settings), "\"pagePassword\"\\s*:\\s*\"([^\"]*)\"").Groups[1].Value;
                string page = "http://localhost:" + PagePort;
                var cookies = new CookieContainer();
                string login = Send(page, "/api/login", "{\"password\":\"" + pagePassword + "\"}", cookies, null);
                string check = Regex.Match(login, "\"check\"\\s*:\\s*\"([^\"]*)\"").Groups[1].Value;
                return Send(page, path, body, cookies, check);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string Send(string page, string path, string body, CookieContainer cookies, string check)
        {
            var request = (HttpWebRequest)WebRequest.Create(page + path);
            request.Method = body == null ? "GET" : "POST";
            request.CookieContainer = cookies;
            request.Timeout = 8000;
            request.Headers["Origin"] = page;
            if (check != null) request.Headers["X-KN-Check"] = check;
            if (body != null)
            {
                request.ContentType = "application/json";
                byte[] data = Encoding.UTF8.GetBytes(body);
                using (Stream stream = request.GetRequestStream()) stream.Write(data, 0, data.Length);
            }
            using (var response = (HttpWebResponse)request.GetResponse())
            using (var reader = new StreamReader(response.GetResponseStream())) return reader.ReadToEnd();
        }

        private static string NewPassword()
        {
            var bytes = new byte[18];
            using (var random = System.Security.Cryptography.RandomNumberGenerator.Create()) random.GetBytes(bytes);
            return Convert.ToBase64String(bytes).Replace('+', 'x').Replace('/', 'y').TrimEnd('=');
        }
    }
}
