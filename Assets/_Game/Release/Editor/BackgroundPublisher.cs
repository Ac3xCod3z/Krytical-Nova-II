using System;
using System.Diagnostics;
using System.IO;
using System.Text;
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
    /// <para>The matching server is built afterwards in a checkout of its own (<see cref="ServerCheckoutFolder"/>, 31 Oct
    /// 2026), opened on the Server subtarget and never switched: the game's Library keeps the Player subtarget, its
    /// scripts and its map-bundle cache, and the server's keeps its own. Two Unity never run at once: the game's Unity queues
    /// the server before it quits (<see cref="QueueServerAfterExit"/>), so the server is built even when the Editor that
    /// started the publish has been closed.</para>
    /// </summary>
    [InitializeOnLoad]
    public static class BackgroundPublisher
    {
        public const string AcceleratorEndpoint = "127.0.0.1:10443";
        public const string BatchLogPath = ReleasePublisher.Root + "/background-publish.log";
        private const string LockPath = ReleasePublisher.Root + "/background-publish.lock";
        private const string ResultPath = ReleasePublisher.Root + "/background-publish.result";
        private const string RestartedMarker = ReleasePublisher.Root + "/background-publish.restarted";
        private const string RestartResult = "RESTART";
        /// <summary>In the result of a publish that reached the test line: its server is built next.</summary>
        private const string UploadedResult = "uploaded to TEST";
        /// <summary>The third line of the lock file while the server step runs.</summary>
        private const string ServerPhase = "server";
        public const string ServerLogPath = ReleasePublisher.Root + "/server-only.log";
        /// <summary>On the game's batch Unity of a script that builds the server itself (publish-test.ps1): nothing is queued.</summary>
        public const string ServerByScriptFlag = "-knServerStepByScript";
        private const string Menu = "Krytical Nova/Release/";

        private static double s_NextCheck;

        static BackgroundPublisher()
        {
            if (!Application.isBatchMode) EditorApplication.update += WatchRunningPublish;
        }

        /// <summary>The build checkout: never the folder you work in.</summary>
        public static string CheckoutFolder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "KN-worktrees", "build");

        /// <summary>The server checkout: builds the dedicated server only, with a Library of its own on the Server subtarget.</summary>
        public static string ServerCheckoutFolder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "KN-worktrees", "build-server");

        private static string ProjectFolder => Path.GetFullPath(".");

        /// <summary>True in the server checkout (ServerBuildBuilder leaves it on the Server subtarget).</summary>
        public static bool InServerCheckout => SameFolder(ProjectFolder, ServerCheckoutFolder);

        private static bool SameFolder(string a, string b) =>
            string.Equals(Path.GetFullPath(a).TrimEnd('\\', '/'), Path.GetFullPath(b).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);

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
                    $"rebuilds the server. This Editor stays free; the console says when it is done.{warning}\n\nPlayers on LIVE see nothing until you Promote.{ReleasePublisher.NewsSummary()}",
                    "Publish", "Cancel")) return;
            string started = Start(head);
            if (started != null) EditorUtility.DisplayDialog("Publish to Test", started, "OK");
        }

        private static string CheckoutProblem()
        {
            if (SameFolder(ProjectFolder, CheckoutFolder) || InServerCheckout)
                return "This Editor is a build checkout. Publish from the folder you work in.";
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
            string ready = PrepareCheckout(CheckoutFolder, commit);
            if (ready != null) return ready;
            if (File.Exists(ResultPath)) File.Delete(ResultPath);
            string started = StartUnity(CheckoutFolder, commit, "KryticalNova.EditorTools.BackgroundPublisher.RunInBatch", BatchLogPath, "", null);
            if (started == null) Debug.Log($"[Release] Publishing commit {commit.Substring(0, 9)} in the background. Keep working: the console says when it is done. Log: {BatchLogPath}");
            return started;
        }

        /// <summary>
        /// The server step, queued by the game's batch Unity just before it quits, so it runs whether or not the Editor that
        /// started the publish is still open. The server checkout (made the first time) is moved to <paramref name="commit"/>,
        /// then a hidden PowerShell waits for this Unity to exit and runs a batch Unity there, opened on the Server subtarget
        /// (ServerBuildBatch.BuildWindows), below normal priority. The lock names that PowerShell while it waits and builds,
        /// so no second publish starts meanwhile, and the Editor, when open, reports the server when it ends. Null when it
        /// was queued, otherwise why not.
        /// </summary>
        public static string QueueServerAfterExit(string commit)
        {
            string busy = ServerBuildBuilder.FolderBusy(ServerBuildBuilder.OutputFolder(ServerBuildBuilder.Os.Windows));
            if (busy != null) return busy;
            if (!Directory.Exists(Path.Combine(ServerCheckoutFolder, "Assets")))
            {
                // A git worktree of this project: the first server build imports its Library (through the Accelerator).
                Directory.CreateDirectory(Path.GetDirectoryName(ServerCheckoutFolder));
                if (!Git(ProjectFolder, $"worktree add --detach {Quote(ServerCheckoutFolder)} {commit}", out string made))
                    return "The server checkout could not be made at " + ServerCheckoutFolder + ":\n" + made;
            }
            string ready = PrepareCheckout(ServerCheckoutFolder, commit);
            if (ready != null) return ready;
            if (File.Exists(ServerBuildBatch.ResultPath)) File.Delete(ServerBuildBatch.ResultPath);

            int me = Process.GetCurrentProcess().Id;
            string unity = UnityArguments(ServerCheckoutFolder, "KryticalNova.EditorTools.ServerBuildBatch.BuildWindows", ServerLogPath, "-standaloneBuildSubtarget Server");
            string script = $"Wait-Process -Id {me} -ErrorAction SilentlyContinue; " +
                            $"$p = Start-Process -FilePath {PsQuote(EditorApplication.applicationPath)} -ArgumentList {PsQuote(unity)} " +
                            $"-WorkingDirectory {PsQuote(ServerCheckoutFolder)} -WindowStyle Hidden -PassThru; " +
                            "try { $p.PriorityClass = 'BelowNormal' } catch {}; $p.WaitForExit()";
            var info = new ProcessStartInfo("powershell.exe",
                    "-NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(script)))
                { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = ServerCheckoutFolder };
            Process waiter;
            try
            {
                waiter = Process.Start(info);
            }
            catch (Exception e)
            {
                return "PowerShell did not start: " + e.Message;
            }
            File.WriteAllText(LockPath, waiter.Id + "\n" + commit + "\n" + ServerPhase + "\n" + DateTime.Now.ToString("s"));
            File.AppendAllText(ReleasePublisher.LogPath, $"Background server of {commit} queued in {ServerCheckoutFolder}: PowerShell process {waiter.Id} waits for Unity process {me} to quit, then builds it {DateTime.Now}{Environment.NewLine}");
            return null;
        }

        private static string PsQuote(string text) => "'" + text.Replace("'", "''") + "'";

        // The checkout is for building only: whatever a build left changed in it is thrown away. Its Builds folder is this project's.
        private static string PrepareCheckout(string checkout, string commit)
        {
            if (!Git(checkout, "checkout --force --detach " + commit, out string moved)) return "The checkout " + checkout + " could not move to " + commit + ":\n" + moved;

            string builds = Path.Combine(checkout, "Builds");
            if (!Directory.Exists(builds))
            {
                // To the main project's Builds by its real path (never a link to a link: Norton's exclusions name real paths).
                if (!Run("cmd.exe", $"/c mklink /J \"{builds}\" \"{ServerBuildBuilder.RealBuildsFolder()}\"", checkout, out string linked)) return "Could not link the Builds folder:\n" + linked;
            }
            else if ((new DirectoryInfo(builds).Attributes & FileAttributes.ReparsePoint) == 0)
                return builds + " is a folder of its own, not a link to " + ServerBuildBuilder.RealBuildsFolder() + ". Move it away, then publish again.";
            return null;
        }

        private static string UnityArguments(string checkout, string method, string logPath, string extra) =>
            string.Join(" ",
                "-batchmode", "-projectPath", Quote(checkout), "-buildTarget", "Win64", extra,
                "-executeMethod", method,
                "-logFile", Quote(Path.GetFullPath(logPath)),
                "-EnableCacheServer", "-cacheServerEndpoint", AcceleratorEndpoint,
                "-cacheServerEnableDownload", "true", "-cacheServerEnableUpload", "true", "-cacheServerWaitForConnection", "10000",
                // Since Unity 6.5 only textures and shaders are shared unless this is on: with it the build checkout gets
                // every import result another Editor already made. No importer of ours reads files it does not declare.
                "-cacheServerEnableImportResultCaching");

        private static string StartUnity(string checkout, string commit, string method, string logPath, string extra, string phase)
        {
            Directory.CreateDirectory(ReleasePublisher.Root);
            string arguments = UnityArguments(checkout, method, logPath, extra);
            var info = new ProcessStartInfo(EditorApplication.applicationPath, arguments) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = checkout };
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
            File.WriteAllText(LockPath, process.Id + "\n" + commit + "\n" + (phase ?? "") + "\n" + DateTime.Now.ToString("s"));
            File.AppendAllText(ReleasePublisher.LogPath, $"Background {phase ?? "publish"} of {commit} started in {checkout} (Unity process {process.Id}) {DateTime.Now}{Environment.NewLine}");
            return null;
        }

        private static Process RunningPublish()
        {
            if (!File.Exists(LockPath)) return null;
            if (!int.TryParse(File.ReadAllLines(LockPath)[0], out int id)) return null;
            try
            {
                Process process = Process.GetProcessById(id);
                // The game's Unity, or the PowerShell that waits for it and then runs the server's (QueueServerAfterExit).
                bool ours = process.ProcessName.StartsWith("Unity", StringComparison.OrdinalIgnoreCase) ||
                            process.ProcessName.StartsWith("powershell", StringComparison.OrdinalIgnoreCase);
                return process.HasExited || !ours ? null : process;
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
            string[] lockLines = File.ReadAllLines(LockPath);
            File.Delete(LockPath);
            if (lockLines.Length > 2 && lockLines[2] == ServerPhase)
            {
                // The game's Unity queued the server before it quit: its own result first, then the server's.
                if (File.Exists(ResultPath)) Debug.Log("[Release] Background publish done: " + File.ReadAllText(ResultPath).Trim() + ". Check it with your test launcher, then Krytical Nova ▸ Release ▸ Promote Test to Live.");
                if (File.Exists(RestartedMarker)) File.Delete(RestartedMarker);
                string built = File.Exists(ServerBuildBatch.ResultPath) ? File.ReadAllText(ServerBuildBatch.ResultPath).Trim() : "FAILED: Unity quit without saying how it went. See " + ServerLogPath;
                if (built.StartsWith("OK")) Debug.Log("[Release] The matching server is built for build " + ReleasePublisher.LastReleaseBuild() + ".");
                else Debug.LogError("[Release] The matching server did not build (" + built + "). Until it is rebuilt (Krytical Nova ▸ Build ▸ Dedicated Server (Windows)), players on the new build are told the server runs another build.");
                return;
            }
            string result = File.Exists(ResultPath) ? File.ReadAllText(ResultPath).Trim() : "FAILED: Unity quit without saying how it went. See " + BatchLogPath;
            if (result.StartsWith(RestartResult) && lockLines.Length > 1 && !File.Exists(RestartedMarker))
            {
                // Once only: the marker is removed when a publish ends any other way.
                File.WriteAllText(RestartedMarker, lockLines[1]);
                string again = Start(lockLines[1]);
                if (again == null) { Debug.Log("[Release] The checkout's subtarget was corrected: publishing again in a fresh Unity."); return; }
                result = "FAILED: " + again;
            }
            if (File.Exists(RestartedMarker)) File.Delete(RestartedMarker);
            if (result.StartsWith("OK"))
            {
                Debug.Log("[Release] Background publish done: " + result + ". Check it with your test launcher, then Krytical Nova ▸ Release ▸ Promote Test to Live.");
                // A publish that reached TEST queued its server before it quit; reaching this means it could not (the result says why).
                if (result.Contains(UploadedResult)) Debug.LogWarning("[Release] The matching server was not built: " + result + " Use Krytical Nova ▸ Build ▸ Dedicated Server (Windows).");
            }
            else Debug.LogError("[Release] Background publish " + result + " Details: " + ReleasePublisher.LogPath);
        }

        // ------------------------------------------------------------------ the batch side (runs in the build checkout)

        /// <summary>-executeMethod of the batch Unity: the Accelerator check, then Publish to Test and the server, then quit.</summary>
        public static void RunInBatch()
        {
            int code = 1;
            try
            {
                // A game is a Player build, and this Editor's scripts must be compiled as the game's. If the subtarget had to be
                // corrected (a server build left it on Server) this run stops here, before anything is built, and the Editor that
                // started it runs it again in a freshly compiled Unity (WatchRunningPublish); two Unity cannot share the project.
                if (ClientBuildGuard.EnsurePlayer("Publish to Test"))
                {
                    Finish(RestartResult + ": the subtarget was corrected; the publish starts again in a fresh Unity.");
                    code = ClientBuildGuard.RestartExitCode;
                    return;
                }
                string problem = AcceleratorProblem();
                if (problem != null)
                {
                    Finish("FAILED: " + problem);
                    code = 2;
                    return;
                }
                // The server is built afterwards in its own checkout, never in this Library: queued here, to run when this
                // Unity has quit (QueueServerAfterExit), unless a script builds it itself.
                bool ok = ReleasePublisher.Publish(buildServer: false);
                bool uploaded = ok && ReleasePublisher.Settings.Load().HasBucket;
                string result = ok ? "OK, build " + ReleasePublisher.LastReleaseBuild() + (uploaded ? ", " + UploadedResult : ", staged only") : "FAILED: see " + ReleasePublisher.LogPath + ".";
                if (uploaded && Array.IndexOf(Environment.GetCommandLineArgs(), ServerByScriptFlag) < 0)
                {
                    string queued = Git(ProjectFolder, "rev-parse HEAD", out string head) ? QueueServerAfterExit(head.Trim()) : "the commit could not be read: " + head;
                    result += queued == null ? "; the server is built next in " + ServerCheckoutFolder : "; the server was NOT queued: " + queued;
                }
                Finish(result);
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
