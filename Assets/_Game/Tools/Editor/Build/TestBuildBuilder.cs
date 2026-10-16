using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// The Windows build handed to testers. It takes the scenes of <see cref="BuildSceneOrder"/> and nothing else, so
    /// only what those scenes reference (and the Resources folders) ships. Not a development build. Next to the
    /// build it writes a size report, largest first, so anything that should not be in there shows up.
    /// <para>Two scripting backends: Mono (the scripts are compiled while the game runs, so the first use of
    /// anything hitches) and IL2CPP (compiled to machine code beforehand: faster, and no such hitches). IL2CPP
    /// needs Unity's "Windows Build Support (IL2CPP)" module and the Visual Studio C++ build tools with a Windows
    /// SDK; <see cref="Il2CppMissing"/> says what is not there. Neither build leaves the project on another backend.</para>
    /// </summary>
    public static class TestBuildBuilder
    {
        public const string BuildRoot = "Builds/Windows";
        public const string OutputFolder = BuildRoot + "/KryticalNovaII";
        public const string ReportPath = BuildRoot + "/BuildReport.txt";
        private const string ExeName = "KryticalNovaII.exe";
        /// <summary>Written by the game beside its exe (<c>PerformanceLogRecorder</c>). Not part of a hand-out.</summary>
        public const string LogsFolder = "PerformanceLogs";
        private const int ReportAssets = 80;
        private const int ReportFolders = 30;
        private const int FolderDepth = 3;

        [MenuItem("Krytical Nova/Build/Windows Test Build", priority = 50)]
        private static void Menu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Windows Test Build", "Stop Play first.", "OK");
                return;
            }
            Build();
        }

        [MenuItem("Krytical Nova/Build/Windows Test Build (IL2CPP)", priority = 51)]
        private static void MenuIl2Cpp()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Windows Test Build", "Stop Play first.", "OK");
                return;
            }
            string missing = Il2CppMissing();
            if (missing != null)
            {
                EditorUtility.DisplayDialog("Windows Test Build (IL2CPP)", missing, "OK");
                return;
            }
            Build(ScriptingImplementation.IL2CPP);
        }

        /// <summary>
        /// What this PC lacks for an IL2CPP build, in words for whoever has to install it; null when nothing is seen
        /// to be missing. The Unity module is certain (its player files are a folder of the Editor); the C++ tools
        /// are looked for where Visual Studio's installer registers them, so a build can still fail on those.
        /// </summary>
        public static string Il2CppMissing()
        {
            var missing = new List<string>();
            string variations = Path.Combine(EditorApplication.applicationContentsPath, "PlaybackEngines", "windowsstandalonesupport", "Variations");
            bool module = Directory.Exists(variations) && Directory.GetDirectories(variations, "*il2cpp*").Length > 0;
            if (!module) missing.Add("Unity's module \"Windows Build Support (IL2CPP)\" for " + Application.unityVersion + " (Unity Hub > Installs > the gear on this version > Add modules)");

            string programs = Environment.GetEnvironmentVariable("ProgramFiles(x86)") ?? string.Empty;
            bool studio = File.Exists(Path.Combine(programs, "Microsoft Visual Studio", "Installer", "vswhere.exe"));
            bool sdk = Directory.Exists(Path.Combine(programs, "Windows Kits", "10", "Include"));
            if (!studio) missing.Add("Visual Studio or its Build Tools with the workload \"Desktop development with C++\" (the Hub offers it with the module)");
            if (!sdk) missing.Add("a Windows 10 or 11 SDK (part of that workload)");
            return missing.Count == 0 ? null : "An IL2CPP build needs, and this PC does not have:\n\n- " + string.Join("\n- ", missing) + "\n\nRestart the Editor after installing.";
        }

        /// <summary>Builds without asking, with Mono. True when the build succeeded; the report is written either way.</summary>
        public static bool Build() => Build(ScriptingImplementation.Mono2x);

        /// <summary>Where a build goes, how it is packed and where its size report is written.</summary>
        public sealed class Output
        {
            public string Folder = OutputFolder;
            public string ReportPath = TestBuildBuilder.ReportPath;
            public string Title = "Windows test build";
            /// <summary>The test build packs every asset into one compressed file; a release keeps them apart for small patches.</summary>
            public BuildOptions Options = BuildOptions.CompressWithLz4HC;
        }

        /// <summary>The game's exe name in any of its builds.</summary>
        public const string GameExe = ExeName;

        /// <summary>
        /// Builds without asking, with the scripting backend named. A build with the project's own backend (Mono)
        /// touches no setting. For another one the backend, stripping level and compiler configuration are set for
        /// the build and put back afterwards: the values are the project's again, though ProjectSettings then names
        /// them where it left them unsaid before.
        /// </summary>
        public static bool Build(ScriptingImplementation backend, Output output = null)
        {
            output = output ?? new Output();
            if (backend == ScriptingImplementation.IL2CPP)
            {
                string missing = Il2CppMissing();
                if (missing != null)
                {
                    Debug.LogError("[Build] " + missing.Replace("\n\n", " ").Replace("\n", " "));
                    return false;
                }
            }

            NamedBuildTarget standalone = NamedBuildTarget.Standalone;
            ScriptingImplementation backendWas = PlayerSettings.GetScriptingBackend(standalone);
            // The project's own backend: nothing is set and nothing has to be put back, so ProjectSettings is not written.
            if (backendWas == backend && backend != ScriptingImplementation.IL2CPP) return BuildWith(backend, output);
            ManagedStrippingLevel strippingWas = PlayerSettings.GetManagedStrippingLevel(standalone);
            Il2CppCompilerConfiguration compilerWas = PlayerSettings.GetIl2CppCompilerConfiguration(standalone);
            try
            {
                PlayerSettings.SetScriptingBackend(standalone, backend);
                if (backend == ScriptingImplementation.IL2CPP)
                {
                    // Minimal: only Unity's and .NET's own libraries are trimmed. The game's assemblies and the
                    // packages' go in whole, because the GM console finds its commands, and Netcode its messages,
                    // by reflection, which trimming cannot see.
                    PlayerSettings.SetManagedStrippingLevel(standalone, ManagedStrippingLevel.Minimal);
                    PlayerSettings.SetIl2CppCompilerConfiguration(standalone, Il2CppCompilerConfiguration.Release);
                }
                return BuildWith(backend, output);
            }
            finally
            {
                PlayerSettings.SetScriptingBackend(standalone, backendWas);
                PlayerSettings.SetManagedStrippingLevel(standalone, strippingWas);
                PlayerSettings.SetIl2CppCompilerConfiguration(standalone, compilerWas);
            }
        }

        private static bool BuildWith(ScriptingImplementation backend, Output output)
        {
            BuildSceneOrder.Apply();
            string[] scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0)
            {
                Debug.LogError("[Build] There are no scenes to build.");
                return false;
            }

            // Only our own output folder: a file left from an older build must not be handed out with this one.
            // The performance logs of the sessions played from it are kept across the rebuild.
            string folder = output.Folder;
            string logs = folder + "/" + LogsFolder, keptLogs = Path.GetDirectoryName(folder) + "/" + LogsFolder + "_kept";
            if (Directory.Exists(logs) && !Directory.Exists(keptLogs)) Directory.Move(logs, keptLogs);
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
            Directory.CreateDirectory(folder);
            if (Directory.Exists(keptLogs)) Directory.Move(keptLogs, logs);

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = folder + "/" + ExeName,
                targetGroup = BuildTargetGroup.Standalone,
                target = BuildTarget.StandaloneWindows64,
                options = output.Options
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            WriteSizeReport(report, scenes, output.Title, "Scripting backend: " + BackendName(backend), output.ReportPath);

            bool ok = report.summary.result == BuildResult.Succeeded;
            if (ok) Debug.Log($"[Build] {output.Title} ({BackendName(backend)}) written to {folder} ({Megabytes(report.summary.totalSize)}). Sizes: {output.ReportPath}");
            else Debug.LogError($"[Build] {output.Title} {report.summary.result}: {report.summary.totalErrors} errors. See {output.ReportPath}");
            return ok;
        }

        private static string BackendName(ScriptingImplementation backend) => backend == ScriptingImplementation.IL2CPP ? "IL2CPP" : "Mono";

        /// <summary>
        /// Writes what a build weighs, largest first, by folder and by asset, with its scenes and any errors. Also used
        /// by <see cref="ServerBuildBuilder"/>.
        /// </summary>
        public static void WriteSizeReport(BuildReport report, string[] scenes, string title, string detailLine, string reportPath)
        {
            var text = new StringBuilder();
            BuildSummary summary = report.summary;
            text.AppendLine($"{title}: {summary.result}");
            text.AppendLine(detailLine);
            text.AppendLine($"Built {summary.buildEndedAt.ToLocalTime():yyyy-MM-dd HH:mm} in {summary.totalTime:hh\\:mm\\:ss}");
            text.AppendLine($"Size {Megabytes(summary.totalSize)}, errors {summary.totalErrors}, warnings {summary.totalWarnings}");
            text.AppendLine();
            text.AppendLine("Scenes:");
            foreach (string scene in scenes) text.AppendLine("  " + scene);

            if (summary.totalErrors > 0)
            {
                text.AppendLine();
                text.AppendLine("Errors:");
                foreach (BuildStep step in report.steps)
                    foreach (BuildStepMessage message in step.messages)
                        if (message.type == LogType.Error || message.type == LogType.Exception)
                            text.AppendLine("  [" + step.name + "] " + message.content);
            }

            // What each source asset weighs once packed (an asset can be split over several files).
            var byAsset = new Dictionary<string, ulong>(4096);
            foreach (PackedAssets packed in report.packedAssets)
                foreach (PackedAssetInfo info in packed.contents)
                {
                    string path = string.IsNullOrEmpty(info.sourceAssetPath) ? "(built-in or generated)" : info.sourceAssetPath;
                    byAsset.TryGetValue(path, out ulong size);
                    byAsset[path] = size + info.packedSize;
                }

            var byFolder = new Dictionary<string, ulong>(256);
            foreach (KeyValuePair<string, ulong> asset in byAsset)
            {
                string folder = FolderOf(asset.Key);
                byFolder.TryGetValue(folder, out ulong size);
                byFolder[folder] = size + asset.Value;
            }

            text.AppendLine();
            text.AppendLine($"Largest folders (first {FolderDepth} levels):");
            foreach (KeyValuePair<string, ulong> folder in byFolder.OrderByDescending(f => f.Value).Take(ReportFolders))
                text.AppendLine($"  {Megabytes(folder.Value),10}  {folder.Key}");

            text.AppendLine();
            text.AppendLine($"Largest assets (first {ReportAssets} of {byAsset.Count}):");
            foreach (KeyValuePair<string, ulong> asset in byAsset.OrderByDescending(a => a.Value).Take(ReportAssets))
                text.AppendLine($"  {Megabytes(asset.Value),10}  {asset.Key}");

            Directory.CreateDirectory(Path.GetDirectoryName(reportPath));
            File.WriteAllText(reportPath, text.ToString());
        }

        private static string FolderOf(string assetPath)
        {
            string[] parts = assetPath.Split('/');
            return parts.Length <= FolderDepth ? assetPath : string.Join("/", parts, 0, FolderDepth);
        }

        public static string Megabytes(ulong bytes) => (bytes / (1024.0 * 1024.0)).ToString("0.0") + " MB";
    }
}
