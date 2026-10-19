using System;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// The release pipeline for players (Krytical Nova ▸ Release). A release build of the game goes into
    /// <c>Builds/Release/Game</c> with its files kept apart (no single packed data file), so a patch carries only what
    /// changed. The release tool (<c>Release/Tools~/ReleaseTool</c>) hashes it, compresses new files into
    /// <c>Builds/Release/Store</c> and signs the update list with the owner's key, which lives in this Windows account
    /// only. The store is then synced to Unity Cloud Content Delivery with the UGS command line: a new release with the
    /// <c>test</c> badge, moved to <c>live</c> by Promote. Launchers read the release under their badge
    /// (<c>Release/Tools~/Launcher</c>).
    /// </summary>
    public static class ReleasePublisher
    {
        public const string Root = "Builds/Release";
        public const string GameFolder = Root + "/Game";
        public const string StoreFolder = Root + "/Store";
        public const string ToolsFolder = Root + "/Tools";
        public const string ForPlayersFolder = Root + "/For Players";
        public const string LogPath = Root + "/release-log.txt";
        public const string SourceFolder = "Assets/_Game/Release/Tools~";
        public const string SettingsPath = SourceFolder + "/release-settings.json";
        /// <summary>
        /// The launcher's news for the next release (owner, 18 Oct 2026): up to four headlines with what changed, written
        /// before a release meant for LIVE from everything since the last LIVE one, and committed (a publish builds a commit).
        /// </summary>
        public const string NewsPath = SourceFolder + "/launcher-news.json";
        public const string LauncherName = "Krytical Nova Launcher.exe";

        private const string Menu = "Krytical Nova/Release/";

        /// <summary>The CCD side, filled in once the bucket exists. Nothing in it is secret: the bucket is public to read.</summary>
        [Serializable]
        public sealed class Settings
        {
            public string projectId = "";
            public string environment = "production";
            public string environmentId = "";
            public string bucketName = "";
            public string bucketId = "";

            public bool HasBucket => !string.IsNullOrWhiteSpace(bucketId) && !string.IsNullOrWhiteSpace(projectId) && !string.IsNullOrWhiteSpace(environmentId);

            /// <summary>Where launchers fetch from; {channel} and {path} are filled in by the launcher.</summary>
            public string UpdateSource => $"https://{projectId}.client-api.unity3dusercontent.com/client_api/v1/environments/{environmentId}/buckets/{bucketId}/release_by_badge/{{channel}}/entry_by_path/content/?path={{path}}";

            public static Settings Load()
            {
                var settings = new Settings { projectId = CloudProjectSettings.projectId };
                if (File.Exists(SettingsPath)) JsonUtility.FromJsonOverwrite(File.ReadAllText(SettingsPath), settings);
                if (string.IsNullOrWhiteSpace(settings.projectId)) settings.projectId = CloudProjectSettings.projectId;
                return settings;
            }

            public void Save() => File.WriteAllText(SettingsPath, JsonUtility.ToJson(this, true));
        }

        // ------------------------------------------------------------------ menus

        // Publish to Test itself runs in the background, in the build checkout (BackgroundPublisher). This one locks the Editor.
        [MenuItem(Menu + "Publish to Test (in This Editor)", priority = 72)]
        private static void MenuPublish()
        {
            if (!ReadyToBuild()) return;
            Settings settings = Settings.Load();
            string where = settings.HasBucket ? "uploaded to Cloud Content Delivery as a new release on the TEST line" : "staged in Builds/Release/Store only (Cloud Content Delivery is not set up yet)";
            if (!EditorUtility.DisplayDialog("Publish to Test",
                    $"Builds the game for release (IL2CPP, about 10 minutes), packs what changed, signs the update list, and has it {where}.\n\nPlayers on LIVE see nothing until you Promote.{NewsSummary()}",
                    "Publish", "Cancel")) return;
            Publish();
        }

        [MenuItem(Menu + "Promote Test to Live", priority = 71)]
        private static void MenuPromote()
        {
            Settings settings = Settings.Load();
            if (!settings.HasBucket) { EditorUtility.DisplayDialog("Promote Test to Live", "Cloud Content Delivery is not set up yet.", "OK"); return; }
            if (!EditorUtility.DisplayDialog("Promote Test to Live", "Every launcher on LIVE gets the release that TEST has now, the next time it opens.", "Promote", "Cancel")) return;
            EditorUtility.DisplayDialog("Promote Test to Live", Promote(), "OK");
        }

        [MenuItem(Menu + "Make Launcher for Players", priority = 82)]
        private static void MenuForPlayers()
        {
            string zip = MakeLauncherForPlayers();
            if (zip != null) EditorUtility.RevealInFinder(zip);
        }

        [MenuItem(Menu + "Make My Test Launcher", priority = 83)]
        private static void MenuTestLauncher()
        {
            string folder = MakeTestLauncher();
            if (folder != null) EditorUtility.RevealInFinder(folder);
        }

        [MenuItem(Menu + "Back Up the Signing Key...", priority = 95)]
        private static void MenuBackUpKey()
        {
            string file = EditorUtility.SaveFilePanel("Back up the update signing key (keep it on a USB stick, offline)", "", "KryticalNova-update-signing-key-BACKUP", "key");
            if (string.IsNullOrEmpty(file)) return;
            EditorUtility.DisplayDialog("Back Up the Signing Key", RunTool("export-key \"" + file + "\"", out _) ? "Saved. Keep it offline: anyone with it can sign updates." : "Failed: see " + LogPath, "OK");
        }

        private static bool ReadyToBuild()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { EditorUtility.DisplayDialog("Release", "Stop Play first.", "OK"); return false; }
            // Every publish goes through the Unity Accelerator (owner, 18 Oct 2026).
            string accelerator = BackgroundPublisher.AcceleratorProblem();
            if (accelerator != null) { EditorUtility.DisplayDialog("Release", accelerator, "OK"); return false; }
            string missing = TestBuildBuilder.Il2CppMissing();
            if (missing != null) { EditorUtility.DisplayDialog("Release", missing, "OK"); return false; }
            return true;
        }

        // ------------------------------------------------------------------ the pipeline

        // ------------------------------------------------------------------ build numbers (online step 4)

        /// <summary>The build number of the release staged last (Builds/Release/Store), or 0 when none was.</summary>
        public static int LastReleaseBuild()
        {
            string manifest = StoreFolder + "/manifest.json";
            if (!File.Exists(manifest)) return 0;
            Match match = Regex.Match(File.ReadAllText(manifest), "\"build\"\\s*:\\s*(\\d+)");
            return match.Success ? int.Parse(match.Groups[1].Value) : 0;
        }

        /// <summary>The project's version without a build number ("0.1.0").</summary>
        public static string BaseVersion() => Regex.Replace(PlayerSettings.bundleVersion, @"\s*build \d+$", string.Empty);

        /// <summary>
        /// Runs a build as build <paramref name="build"/>: the version (Application.version in the build) reads
        /// "0.1.0 build N", which the game and the server compare when a player joins. 0 leaves a development build.
        /// The project's version is put back afterwards.
        /// </summary>
        public static T WithVersion<T>(int build, Func<T> work)
        {
            string was = PlayerSettings.bundleVersion;
            if (build > 0) PlayerSettings.bundleVersion = BaseVersion() + " build " + build;
            try
            {
                return work();
            }
            finally
            {
                PlayerSettings.bundleVersion = was;
            }
        }

        /// <summary>
        /// Build, stage, sign and (when CCD is set up) upload to the test line, then build the server of the same
        /// number. A line per step in the release log. <paramref name="buildGame"/> false re-stages the game already
        /// in Builds/Release/Game (for tests: its version is the one it was built with).
        /// </summary>
        public static bool Publish(bool buildGame = true)
        {
            Directory.CreateDirectory(Root);
            Log("--- Publish to Test " + DateTime.Now);
            Settings settings = Settings.Load();
            if (!BuildTools(settings)) return Fail("the launcher or the release tool did not build");
            int build = LastReleaseBuild() + 1;
            if (buildGame)
            {
                var output = new TestBuildBuilder.Output { Folder = GameFolder, ReportPath = Root + "/BuildReport.txt", Title = "Release build", Options = BuildOptions.None };
                if (!WithVersion(build, () => TestBuildBuilder.Build(ScriptingImplementation.IL2CPP, output)))
                    return Fail("the game did not build (see " + output.ReportPath + ")");
            }
            if (!Directory.Exists(GameFolder)) return Fail("there is no release build in " + GameFolder);

            string launcher = Path.GetFullPath(ToolsFolder + "/Launcher/" + LauncherName);
            string news = File.Exists(NewsPath) ? $" --news \"{Path.GetFullPath(NewsPath)}\"" : "";
            if (!RunTool($"stage --game \"{Path.GetFullPath(GameFolder)}\" --launcher \"{launcher}\" --out \"{Path.GetFullPath(StoreFolder)}\" --version \"v{BaseVersion()}\" --build {build}{news}", out string staged))
                return Fail("staging failed");
            Log(staged.Trim());
            if (!settings.HasBucket)
            {
                Log("Cloud Content Delivery is not set up: the release is staged in " + StoreFolder + " only.");
                return true;
            }

            string version = Regex.Match(staged, @"build=(\d+)").Groups[1].Value;
            if (!RunUgs($"ccd entries sync \"{Path.GetFullPath(StoreFolder)}\" -b {settings.bucketName} -x -r -u test -n \"build {version}\" -e {settings.environment} -p {settings.projectId}", out string synced))
                return Fail("the upload to Cloud Content Delivery failed");
            Log("Uploaded build " + version + " to the TEST line.");
            Debug.Log($"[Release] Build {version} is on the TEST line. Check it with your test launcher, then Krytical Nova ▸ Release ▸ Promote Test to Live.");
            BuildMatchingServer();
            return true;
        }

        // A game joins only a server of its own build: the server is rebuilt with the release's number, unless it is running.
        private static void BuildMatchingServer()
        {
            string busy = ServerBuildBuilder.FolderBusy(ServerBuildBuilder.OutputFolder(ServerBuildBuilder.Os.Windows));
            if (busy != null)
            {
                Log("Server not rebuilt: it is running. Stop it, then Krytical Nova > Build > Dedicated Server (Windows).");
                Debug.LogWarning("[Release] Your server is running, so it was not rebuilt for this release. Stop it and use Krytical Nova > Build > Dedicated Server (Windows): until then players on the new build are told the server runs another build.");
                return;
            }
            Log(ServerBuildBuilder.Build(ServerBuildBuilder.Os.Windows) ? "Server rebuilt for build " + LastReleaseBuild() + "." : "The server did not build: see Builds/Server/BuildReport-Windows.txt.");
        }

        /// <summary>The news headlines the next release carries, for the publish dialogs (the committed file is the one built).</summary>
        public static string NewsSummary()
        {
            if (!File.Exists(NewsPath)) return "\n\nNo launcher news: " + NewsPath + " is missing.";
            MatchCollection titles = Regex.Matches(File.ReadAllText(NewsPath), "\"title\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
            if (titles.Count == 0) return "\n\nNo launcher news (" + NewsPath + " has no items).";
            var text = new StringBuilder("\n\nLauncher news:");
            foreach (Match title in titles) text.Append("\n  • ").Append(Regex.Unescape(title.Groups[1].Value));
            return text.ToString();
        }

        /// <summary>Moves the live badge to the release the test badge is on.</summary>
        public static string Promote()
        {
            Settings settings = Settings.Load();
            if (!RunUgs($"ccd badges list -b {settings.bucketName} -n test -j -e {settings.environment} -p {settings.projectId}", out string list))
                return "Could not read the TEST line: see " + LogPath;
            Match release = Regex.Match(list, "\"(?:releasenum|releaseNum|ReleaseNum)\"\\s*:\\s*(\\d+)");
            if (!release.Success) return "The TEST line has no release yet. Publish to Test first.";
            if (!RunUgs($"ccd badges create {release.Groups[1].Value} live -b {settings.bucketName} -e {settings.environment} -p {settings.projectId}", out _))
                return "Promoting failed: see " + LogPath;
            Log("Promoted release " + release.Groups[1].Value + " to LIVE.");
            return $"Release {release.Groups[1].Value} is LIVE. Every launcher gets it the next time it opens.";
        }

        /// <summary>The launcher and the release tool, from source, with the update source and public key built in.</summary>
        public static bool BuildTools(Settings settings)
        {
            string launcherSource = SourceFolder + "/Launcher";
            if (!File.Exists(launcherSource + "/UpdateKey.pub")) { Log("No public key: run ReleaseTool keygen first (see the Release section of the Server Guide)."); return false; }
            File.WriteAllText(launcherSource + "/UpdateSource.txt", settings.HasBucket ? settings.UpdateSource : "");
            return Dotnet($"build \"{Path.GetFullPath(SourceFolder + "/ReleaseTool/ReleaseTool.csproj")}\" -c Release -o \"{Path.GetFullPath(ToolsFolder + "/ReleaseTool")}\" --nologo")
                && Dotnet($"build \"{Path.GetFullPath(launcherSource + "/Launcher.csproj")}\" -c Release -o \"{Path.GetFullPath(ToolsFolder + "/Launcher")}\" --nologo");
        }

        /// <summary>A zip with the launcher in a "Krytical Nova" folder: what a player is given, once.</summary>
        public static string MakeLauncherForPlayers()
        {
            Settings settings = Settings.Load();
            if (!settings.HasBucket) { EditorUtility.DisplayDialog("Make Launcher for Players", "Cloud Content Delivery is not set up yet, so this launcher would have nowhere to look.", "OK"); return null; }
            if (!BuildTools(settings)) return null;
            string folder = ForPlayersFolder + "/Krytical Nova";
            if (Directory.Exists(ForPlayersFolder)) Directory.Delete(ForPlayersFolder, true);
            Directory.CreateDirectory(folder);
            File.Copy(ToolsFolder + "/Launcher/" + LauncherName, folder + "/" + LauncherName);
            string zip = Root + "/Krytical Nova Launcher.zip";
            if (File.Exists(zip)) File.Delete(zip);
            ZipFile.CreateFromDirectory(ForPlayersFolder, zip);
            Log("Launcher for players: " + zip);
            return zip;
        }

        /// <summary>The owner's own launcher, on the TEST line, in Builds/Release/My Test Launcher.</summary>
        public static string MakeTestLauncher(string source = null)
        {
            Settings settings = Settings.Load();
            if (!BuildTools(settings)) return null;
            string folder = Root + "/My Test Launcher";
            Directory.CreateDirectory(folder);
            File.Copy(ToolsFolder + "/Launcher/" + LauncherName, folder + "/" + LauncherName, true);
            string json = source == null ? "{\"channel\":\"test\"}" : "{\"channel\":\"test\",\"source\":\"" + source + "\"}";
            File.WriteAllText(folder + "/launcher.json", json);
            return folder;
        }

        // ------------------------------------------------------------------ processes

        private static bool Fail(string why)
        {
            Log("FAILED: " + why);
            Debug.LogError("[Release] Publish failed: " + why + ". Details: " + LogPath);
            return false;
        }

        private static void Log(string line)
        {
            Directory.CreateDirectory(Root);
            File.AppendAllText(LogPath, line + Environment.NewLine);
        }

        public static bool RunTool(string arguments, out string output) =>
            Run(Path.GetFullPath(ToolsFolder + "/ReleaseTool/ReleaseTool.exe"), arguments, 3600, out output);

        private static bool Dotnet(string arguments) => Run(DotnetPath(), arguments, 600, out _);

        public static bool RunUgs(string arguments, out string output) => Run(UgsPath(), arguments, 3600, out output);

        private static string DotnetPath()
        {
            string installed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "dotnet.exe");
            return File.Exists(installed) ? installed : "dotnet";
        }

        /// <summary>The UGS command line: Unity's single-file release kept in the owner's tools folder, or on the PATH.</summary>
        public static string UgsPath()
        {
            string kept = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KryticalNova", "tools", "ugs.exe");
            return File.Exists(kept) ? kept : "ugs";
        }

        private static bool Run(string exe, string arguments, int timeoutSeconds, out string output)
        {
            var text = new StringBuilder();
            try
            {
                var info = new ProcessStartInfo(exe, arguments)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = Path.GetFullPath("."),
                };
                // The Editor's process launcher drops the user's folders, so the release tool could not find the signing
                // key in the profile, nor UGS its login: hand them over by name.
                foreach (string name in new[] { "APPDATA", "LOCALAPPDATA", "USERPROFILE", "HOMEDRIVE", "HOMEPATH", "USERNAME", "TEMP", "TMP" })
                {
                    string value = Environment.GetEnvironmentVariable(name);
                    if (!string.IsNullOrEmpty(value)) info.EnvironmentVariables[name] = value;
                }
                using (var process = new Process { StartInfo = info })
                {
                    process.OutputDataReceived += (s, e) => { if (e.Data != null) lock (text) text.AppendLine(e.Data); };
                    process.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (text) text.AppendLine(e.Data); };
                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                    if (!process.WaitForExit(timeoutSeconds * 1000))
                    {
                        process.Kill();
                        text.AppendLine("(stopped after " + timeoutSeconds + " s)");
                    }
                    process.WaitForExit();
                    output = text.ToString();
                    Log($"> {Path.GetFileName(exe)} {arguments}{Environment.NewLine}{output.TrimEnd()}");
                    return process.HasExited && process.ExitCode == 0;
                }
            }
            catch (Exception e)
            {
                output = e.Message;
                Log($"> {Path.GetFileName(exe)} {arguments}{Environment.NewLine}could not run: {e.Message}");
                return false;
            }
        }
    }
}
