using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// The Windows build handed to testers. It takes the scenes of <see cref="BuildSceneOrder"/> and nothing else, so
    /// only what those scenes reference (and the Resources folders) ships. Not a development build. Next to the
    /// build it writes a size report, largest first, so anything that should not be in there shows up.
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

        /// <summary>Builds without asking. True when the build succeeded; the report is written either way.</summary>
        public static bool Build()
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
            string logs = OutputFolder + "/" + LogsFolder, keptLogs = BuildRoot + "/" + LogsFolder + "_kept";
            if (Directory.Exists(logs) && !Directory.Exists(keptLogs)) Directory.Move(logs, keptLogs);
            if (Directory.Exists(OutputFolder)) Directory.Delete(OutputFolder, true);
            Directory.CreateDirectory(OutputFolder);
            if (Directory.Exists(keptLogs)) Directory.Move(keptLogs, logs);

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = OutputFolder + "/" + ExeName,
                targetGroup = BuildTargetGroup.Standalone,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.CompressWithLz4HC
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            WriteReport(report, scenes);

            bool ok = report.summary.result == BuildResult.Succeeded;
            if (ok) Debug.Log($"[Build] Windows test build written to {OutputFolder} ({Megabytes(report.summary.totalSize)}). Sizes: {ReportPath}");
            else Debug.LogError($"[Build] Windows test build {report.summary.result}: {report.summary.totalErrors} errors. See {ReportPath}");
            return ok;
        }

        private static void WriteReport(BuildReport report, string[] scenes)
        {
            var text = new StringBuilder();
            BuildSummary summary = report.summary;
            text.AppendLine($"Windows test build: {summary.result}");
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

            Directory.CreateDirectory(BuildRoot);
            File.WriteAllText(ReportPath, text.ToString());
        }

        private static string FolderOf(string assetPath)
        {
            string[] parts = assetPath.Split('/');
            return parts.Length <= FolderDepth ? assetPath : string.Join("/", parts, 0, FolderDepth);
        }

        private static string Megabytes(ulong bytes) => (bytes / (1024.0 * 1024.0)).ToString("0.0") + " MB";
    }
}
