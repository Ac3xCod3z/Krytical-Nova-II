using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Publish to Test without locking the Editor you work in (owner, 18 Oct 2026). The release is built by a second Unity,
    /// in batch mode, in the build checkout (a git worktree at <c>%USERPROFILE%/KN-worktrees/build</c> with its own Library
    /// and its <c>Builds</c> folder linked to this project's, so build numbers, the store and the server folder stay one).
    /// That Unity is started from this Editor, so it runs as the owner and finds the signing key and the UGS login. It
    /// publishes the commit this folder is on: work that is not committed is not in it. Every publish needs the Unity
    /// Accelerator (the local cache server): without it the publish does not start.
    /// </summary>
    [InitializeOnLoad]
    public static class BackgroundPublisher
    {
        public const string AcceleratorEndpoint = "127.0.0.1:10443";
        public const string BatchLogPath = ReleasePublisher.Root + "/background-publish.log";
        private const string LockPath = ReleasePublisher.Root + "/background-publish.lock";
        private const string ResultPath = ReleasePublisher.Root + "/background-publish.result";
        private const string Menu = "Krytical Nova/Release/";

        private static double s_NextCheck;

        static BackgroundPublisher()
        {
            if (!Application.isBatchMode) EditorApplication.update += WatchRunningPublish;
        }

        /// <summary>The build checkout: never the folder you work in.</summary>
        public static string CheckoutFolder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "KN-worktrees", "build");

        private static string ProjectFolder => Path.GetFullPath(".");

        // ------------------------------------------------------------------ the Accelerator

        /// <summary>Why this Editor cannot use the Unity Accelerator now, or null when it is connected.</summary>
        public static string AcceleratorProblem()
        {
            if (!AssetDatabase.IsCacheServerEnabled())
                return "The Unity Accelerator is switched off for this Editor. Turn it on in Edit ▸ Preferences ▸ Asset Pipeline ▸ " +
                       "Cache Server (Enabled, " + AcceleratorEndpoint + "), then publish again.";
            if (AssetDatabase.IsConnectedToCacheServer()) return null;
            string address = AssetDatabase.GetCacheServerAddress();
            ushort port = AssetDatabase.GetCacheServerPort();
            if (AssetDatabase.CanConnectToCacheServer(address, port)) return null;
            return $"The Unity Accelerator does not answer at {address}:{port}. Start it (Windows Services ▸ Unity Accelerator ▸ Start), " +
                   "then publish again.";
        }

        // ------------------------------------------------------------------ the menu

        [MenuItem(Menu + "Publish to Test", priority = 70)]
        private static void MenuPublish()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { EditorUtility.DisplayDialog("Publish to Test", "Stop Play first.", "OK"); return; }
            string problem = AcceleratorProblem() ?? CheckoutProblem() ?? RunningProblem();
            if (problem != null) { EditorUtility.DisplayDialog("Publish to Test", problem, "OK"); return; }

            if (!Git(ProjectFolder, "rev-parse HEAD", out string head)) { EditorUtility.DisplayDialog("Publish to Test", "Could not read this folder's commit: " + head, "OK"); return; }
            head = head.Trim();
            Git(ProjectFolder, "rev-parse --abbrev-ref HEAD", out string branch);
            Git(ProjectFolder, "status --porcelain -- Assets ProjectSettings Packages", out string changes);
            int notCommitted = changes.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries).Length;
            string warning = notCommitted == 0 ? "" : $"\n\n{notCommitted} changed files in this folder are not committed and will NOT be in it.";
            ReleasePublisher.Settings settings = ReleasePublisher.Settings.Load();
            string where = settings.HasBucket ? "uploads it to the TEST line" : "stages it in Builds/Release/Store";
            if (!EditorUtility.DisplayDialog("Publish to Test",
                    $"Builds commit {head.Substring(0, 9)} ({branch.Trim()}) in the build checkout, in the background, then {where} and " +
                    $"rebuilds the server. This Editor stays free; the console says when it is done.{warning}\n\nPlayers on LIVE see nothing until you Promote.",
                    "Publish", "Cancel")) return;
            string started = Start(head);
            if (started != null) EditorUtility.DisplayDialog("Publish to Test", started, "OK");
        }

        private static string CheckoutProblem()
        {
            if (string.Equals(ProjectFolder.TrimEnd('\\', '/'), CheckoutFolder, StringComparison.OrdinalIgnoreCase))
                return "This Editor is the build checkout. Publish from the folder you work in.";
            if (!Directory.Exists(Path.Combine(CheckoutFolder, "Assets")))
                return "There is no build checkout at " + CheckoutFolder + ". The Release Guide (section 1a) says how to make it.";
            return null;
        }

        private static string RunningProblem()
        {
            Process running = RunningPublish();
            return running == null ? null : $"A background publish is already running (Unity process {running.Id}). Its log: {BatchLogPath}";
        }

        // ------------------------------------------------------------------ starting it

        /// <summary>Moves the build checkout to <paramref name="commit"/> and starts the batch Unity there. Null when it started, otherwise why not.</summary>
        public static string Start(string commit)
        {
            // The checkout is for building only: whatever a build left changed in it is thrown away.
            if (!Git(CheckoutFolder, "checkout --force --detach " + commit, out string moved)) return "The build checkout could not move to " + commit + ":\n" + moved;

            string builds = Path.Combine(CheckoutFolder, "Builds");
            if (!Directory.Exists(builds))
            {
                if (!Run("cmd.exe", $"/c mklink /J \"{builds}\" \"{Path.GetFullPath("Builds")}\"", CheckoutFolder, out string linked)) return "Could not link the Builds folder:\n" + linked;
            }
            else if ((new DirectoryInfo(builds).Attributes & FileAttributes.ReparsePoint) == 0)
                return builds + " is a folder of its own, not a link to " + Path.GetFullPath("Builds") + ". Move it away, then publish again.";

            Directory.CreateDirectory(ReleasePublisher.Root);
            if (File.Exists(ResultPath)) File.Delete(ResultPath);
            string arguments = string.Join(" ",
                "-batchmode", "-projectPath", Quote(CheckoutFolder), "-buildTarget", "Win64",
                "-executeMethod", "KryticalNova.EditorTools.BackgroundPublisher.RunInBatch",
                "-logFile", Quote(Path.GetFullPath(BatchLogPath)),
                "-EnableCacheServer", "-cacheServerEndpoint", AcceleratorEndpoint,
                "-cacheServerEnableDownload", "true", "-cacheServerEnableUpload", "true", "-cacheServerWaitForConnection", "10000",
                // Since Unity 6.5 only textures and shaders are shared unless this is on: with it the build checkout gets
                // every import result another Editor already made. No importer of ours reads files it does not declare.
                "-cacheServerEnableImportResultCaching");
            var info = new ProcessStartInfo(EditorApplication.applicationPath, arguments) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = CheckoutFolder };
            // As ReleasePublisher.Run: the user's folders are handed over by name, for the signing key and the UGS login.
            foreach (string name in new[] { "APPDATA", "LOCALAPPDATA", "USERPROFILE", "HOMEDRIVE", "HOMEPATH", "USERNAME", "TEMP", "TMP" })
            {
                string value = Environment.GetEnvironmentVariable(name);
                if (!string.IsNullOrEmpty(value)) info.EnvironmentVariables[name] = value;
            }
            Process process;
            try
            {
                process = Process.Start(info);
                // Below normal, so the Editor you work in keeps the first call on the processor.
                process.PriorityClass = ProcessPriorityClass.BelowNormal;
            }
            catch (Exception e)
            {
                return "Unity did not start: " + e.Message;
            }
            File.WriteAllText(LockPath, process.Id + "\n" + commit + "\n" + DateTime.Now.ToString("s"));
            File.AppendAllText(ReleasePublisher.LogPath, $"Background publish of {commit} started in {CheckoutFolder} (Unity process {process.Id}) {DateTime.Now}{Environment.NewLine}");
            Debug.Log($"[Release] Publishing commit {commit.Substring(0, 9)} in the background (Unity process {process.Id}). Keep working: the console says when it is done. Log: {BatchLogPath}");
            return null;
        }

        private static Process RunningPublish()
        {
            if (!File.Exists(LockPath)) return null;
            if (!int.TryParse(File.ReadAllLines(LockPath)[0], out int id)) return null;
            try
            {
                Process process = Process.GetProcessById(id);
                return process.HasExited || !process.ProcessName.StartsWith("Unity", StringComparison.OrdinalIgnoreCase) ? null : process;
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        // When the batch Unity has gone, the console says how it went (it writes the result file before it quits).
        private static void WatchRunningPublish()
        {
            if (EditorApplication.timeSinceStartup < s_NextCheck) return;
            s_NextCheck = EditorApplication.timeSinceStartup + 5;
            if (!File.Exists(LockPath) || RunningPublish() != null) return;
            string result = File.Exists(ResultPath) ? File.ReadAllText(ResultPath).Trim() : "FAILED: Unity quit without saying how it went. See " + BatchLogPath;
            File.Delete(LockPath);
            if (result.StartsWith("OK")) Debug.Log("[Release] Background publish done: " + result + ". Check it with your test launcher, then Krytical Nova ▸ Release ▸ Promote Test to Live.");
            else Debug.LogError("[Release] Background publish " + result + " Details: " + ReleasePublisher.LogPath);
        }

        // ------------------------------------------------------------------ the batch side (runs in the build checkout)

        /// <summary>-executeMethod of the batch Unity: the Accelerator check, then Publish to Test and the server, then quit.</summary>
        public static void RunInBatch()
        {
            int code = 1;
            try
            {
                string problem = AcceleratorProblem();
                if (problem != null)
                {
                    Finish("FAILED: " + problem);
                    code = 2;
                    return;
                }
                bool ok = ReleasePublisher.Publish();
                Finish(ok ? "OK, build " + ReleasePublisher.LastReleaseBuild() : "FAILED: see " + ReleasePublisher.LogPath + ".");
                code = ok ? 0 : 1;
            }
            catch (Exception e)
            {
                Finish("FAILED: " + e.Message);
                Debug.LogException(e);
            }
            finally
            {
                EditorApplication.Exit(code);
            }
        }

        private static void Finish(string result)
        {
            Directory.CreateDirectory(ReleasePublisher.Root);
            File.WriteAllText(ResultPath, result);
            File.AppendAllText(ReleasePublisher.LogPath, "Background publish: " + result + Environment.NewLine);
            Debug.Log("[Release] " + result);
        }

        // ------------------------------------------------------------------ processes

        private static string Quote(string path) => "\"" + path + "\"";

        private static bool Git(string folder, string arguments, out string output) => Run("git", arguments, folder, out output);

        private static bool Run(string exe, string arguments, string folder, out string output)
        {
            try
            {
                var info = new ProcessStartInfo(exe, arguments)
                {
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = folder,
                };
                using (Process process = Process.Start(info))
                {
                    // Both read at once: git writes its progress to the error stream, and a full pipe would stall it.
                    var error = process.StandardError.ReadToEndAsync();
                    string text = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();
                    output = text + error.Result;
                    return process.ExitCode == 0;
                }
            }
            catch (Exception e)
            {
                output = e.Message;
                return false;
            }
        }
    }
}
