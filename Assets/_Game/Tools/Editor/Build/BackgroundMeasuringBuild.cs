using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// A Windows build for measuring only, made by a second Unity in the build checkout (as Publish to Test is, through
    /// the Unity Accelerator) so the Editor you work in stays free; nothing is uploaded. IL2CPP, with graphics jobs in
    /// Split mode set in the checkout only (DX12 uses it; DX11 falls back), so one build compares DX11 and DX12 with
    /// <c>-force-d3d11</c> / <c>-force-d3d12</c> (batching pass, 18 Oct 2026). Output: Builds/Windows/KryticalNovaII_Measure.
    /// </summary>
    public static class BackgroundMeasuringBuild
    {
        public const string OutputFolder = TestBuildBuilder.BuildRoot + "/KryticalNovaII_Measure";
        public const string ResultPath = TestBuildBuilder.BuildRoot + "/measure-build.result";
        public const string BatchLogPath = TestBuildBuilder.BuildRoot + "/measure-build.log";

        [MenuItem("Krytical Nova/Build/Measuring Build (Background, IL2CPP)", priority = 52)]
        private static void Menu()
        {
            string problem = Start();
            if (problem != null) Debug.LogError("[Build] Measuring build not started: " + problem);
        }

        /// <summary>Starts the build of this folder's HEAD commit; null when started, else why not.</summary>
        public static string Start()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "Stop Play first.";
            string problem = BackgroundPublisher.AcceleratorProblem();
            if (problem != null) return problem;
            string checkout = BackgroundPublisher.CheckoutFolder;
            if (Path.GetFullPath(".").TrimEnd('\\', '/').Equals(checkout.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
                return "This Editor is the build checkout.";
            if (!Run("git", "rev-parse HEAD", Path.GetFullPath("."), out string commit)) return "Could not read this folder's commit: " + commit;
            commit = commit.Trim();
            if (!Run("git", "checkout --force --detach " + commit, checkout, out string moved)) return "The build checkout could not move to " + commit + ":\n" + moved;

            string builds = Path.Combine(checkout, "Builds");
            if (!Directory.Exists(builds))
            {
                if (!Run("cmd.exe", $"/c mklink /J \"{builds}\" \"{Path.GetFullPath("Builds")}\"", checkout, out string linked)) return "Could not link the Builds folder:\n" + linked;
            }
            else if ((new DirectoryInfo(builds).Attributes & FileAttributes.ReparsePoint) == 0)
                return builds + " is a folder of its own, not a link to this project's Builds.";

            Directory.CreateDirectory(TestBuildBuilder.BuildRoot);
            if (File.Exists(ResultPath)) File.Delete(ResultPath);
            string arguments = string.Join(" ",
                "-batchmode", "-projectPath", Quote(checkout), "-buildTarget", "Win64",
                "-executeMethod", "KryticalNova.EditorTools.BackgroundMeasuringBuild.RunInBatch",
                "-logFile", Quote(Path.GetFullPath(BatchLogPath)),
                "-EnableCacheServer", "-cacheServerEndpoint", BackgroundPublisher.AcceleratorEndpoint,
                "-cacheServerEnableDownload", "true", "-cacheServerEnableUpload", "true", "-cacheServerWaitForConnection", "10000",
                "-cacheServerEnableImportResultCaching");
            var info = new ProcessStartInfo(EditorApplication.applicationPath, arguments) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = checkout };
            foreach (string name in new[] { "APPDATA", "LOCALAPPDATA", "USERPROFILE", "HOMEDRIVE", "HOMEPATH", "USERNAME", "TEMP", "TMP" })
            {
                string value = Environment.GetEnvironmentVariable(name);
                if (!string.IsNullOrEmpty(value)) info.EnvironmentVariables[name] = value;
            }
            try
            {
                Process process = Process.Start(info);
                process.PriorityClass = ProcessPriorityClass.BelowNormal;
                Debug.Log($"[Build] Measuring build of {commit.Substring(0, 9)} started in the background (Unity process {process.Id}). Log: {BatchLogPath}");
            }
            catch (Exception e)
            {
                return "Unity did not start: " + e.Message;
            }
            return null;
        }

        /// <summary>-executeMethod of the batch Unity: graphics jobs in Split mode, an IL2CPP build, the result in a file, quit.</summary>
        public static void RunInBatch()
        {
            int code = 1;
            var clock = Stopwatch.StartNew();
            try
            {
                string missing = TestBuildBuilder.Il2CppMissing();
                if (missing != null) { Finish("FAILED: " + missing); return; }
                // The checkout's own settings, thrown away by its next checkout: the project you work in is not touched.
                PlayerSettings.graphicsJobs = true;
                PlayerSettings.graphicsJobMode = GraphicsJobMode.Split;
                bool ok = TestBuildBuilder.Build(ScriptingImplementation.IL2CPP, new TestBuildBuilder.Output
                {
                    Folder = OutputFolder,
                    ReportPath = TestBuildBuilder.BuildRoot + "/measure-build-size.txt",
                    Title = "Windows measuring build",
                });
                Finish((ok ? "OK" : "FAILED: see " + BatchLogPath) + $" after {clock.Elapsed.TotalMinutes:0.0} min");
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
            File.WriteAllText(ResultPath, result + Environment.NewLine);
            Debug.Log("[Build] Measuring build: " + result);
        }

        private static string Quote(string path) => "\"" + path + "\"";

        private static bool Run(string exe, string arguments, string folder, out string output)
        {
            try
            {
                var info = new ProcessStartInfo(exe, arguments)
                {
                    UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = folder,
                    RedirectStandardOutput = true, RedirectStandardError = true,
                };
                using Process process = Process.Start(info);
                output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
                process.WaitForExit();
                return process.ExitCode == 0;
            }
            catch (Exception e)
            {
                output = e.Message;
                return false;
            }
        }
    }
}
