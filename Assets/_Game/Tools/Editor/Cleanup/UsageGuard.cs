using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using Debug = UnityEngine.Debug;

namespace KryticalNova.EditorTools.Cleanup
{
    /// <summary>
    /// The two checks the cleanup runs besides Maintainer's, so a file goes only when all three say nothing uses it
    /// (owner, 18 Oct 2026: "make sure your system's foolproof"). Both start from the same roots: every asset outside
    /// the folders the cleanup may touch (all of <c>Assets/_Game</c>, every zone and scene in it, Resources, plugins,
    /// tools), every scene in the build list, the files <c>ProjectSettings</c> and the embedded packages point at, and
    /// the vendor files our code loads by path or by name.
    /// <list type="bullet">
    /// <item><b>Unity's dependencies</b>: each reached file's direct dependencies (<c>GetDependencies(path, false)</c>),
    /// walked once per file. (Asking for whole subtrees of thousands of roots at once kept the Editor busy for over an
    /// hour on 18 Oct 2026.) It can be cancelled.</item>
    /// <item><b>Written references</b>: <c>Scripts~/written_refs.py</c>, outside Unity, reading the GUIDs written in
    /// YAML, JSON and .meta files without asking Unity. It runs while Maintainer works, so it costs no extra time.</item>
    /// </list>
    /// </summary>
    public static class UsageGuard
    {
        public const string Script = "Assets/_Game/Tools/Editor/Cleanup/Scripts~/written_refs.py";
        public const string WrittenUsedFile = ProjectCleanupScan.ReportFolder + "/WrittenUsed.txt";
        public const string StillReferencedFile = ProjectCleanupScan.ReportFolder + "/StillReferenced.txt";
        public const string ScriptLog = ProjectCleanupScan.ReportFolder + "/written_refs.log";

        /// <summary>Starts the written-reference check in its own process ("scan" or "after"). Returns null if Python is missing.</summary>
        public static Process StartWrittenCheck(string mode)
        {
            Directory.CreateDirectory(ProjectCleanupScan.ReportFolder);
            try
            {
                var info = new ProcessStartInfo("python", $"-u \"{Script}\" {mode}")
                {
                    WorkingDirectory = Path.GetDirectoryName(UnityEngine.Application.dataPath),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                var process = new Process { StartInfo = info, EnableRaisingEvents = true };
                var log = new StreamWriter(ScriptLog, false) { AutoFlush = true };
                process.OutputDataReceived += (_, e) => { if (e.Data != null) lock (log) log.WriteLine(e.Data); };
                process.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (log) log.WriteLine(e.Data); };
                process.Exited += (_, __) => { lock (log) log.Dispose(); };
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                return process;
            }
            catch (Exception e)
            {
                Debug.LogError("[Cleanup] Could not start the written-reference check (is Python on PATH?): " + e.Message);
                return null;
            }
        }

        /// <summary>Waits for the check with a progress bar that can be cancelled. False when cancelled or it failed.</summary>
        public static bool Wait(Process process, string title)
        {
            if (process == null) return false;
            var started = DateTime.Now;
            while (!process.WaitForExit(250))
            {
                string last = LastLine();
                if (EditorUtility.DisplayCancelableProgressBar(title, $"Reading the project's files outside Unity ({(DateTime.Now - started).TotalSeconds:0} s)  {last}", 0.5f))
                {
                    try { process.Kill(); } catch (Exception) { }
                    return false;
                }
            }
            EditorUtility.ClearProgressBar();
            if (process.ExitCode != 0) { Debug.LogError("[Cleanup] The written-reference check failed; see " + ScriptLog); return false; }
            return true;
        }

        public static HashSet<string> ReadWrittenUsed() =>
            new HashSet<string>(File.ReadAllLines(WrittenUsedFile).Where(l => l.Length > 0 && l[0] != '#'), StringComparer.Ordinal);

        public static HashSet<string> ReadStillReferenced() =>
            new HashSet<string>(File.Exists(StillReferencedFile) ? File.ReadAllLines(StillReferencedFile).Where(l => l.Length == 32) : Array.Empty<string>());

        /// <summary>
        /// Every Assets file reached from the roots through Unity's dependencies, each file asked once.
        /// Null if cancelled.
        /// </summary>
        public static HashSet<string> UnityClosure(Func<string, bool> isCandidate, IEnumerable<string> extraRoots, out int rootCount)
        {
            var reached = new HashSet<string>(StringComparer.Ordinal);
            var queue = new Queue<string>();
            void Add(string p) { if (reached.Add(p)) queue.Enqueue(p); }

            foreach (string path in AssetDatabase.GetAllAssetPaths())
                if (path.StartsWith("Assets/", StringComparison.Ordinal) && !isCandidate(path) && !AssetDatabase.IsValidFolder(path)) Add(path);
            foreach (var scene in EditorBuildSettings.scenes) if (!string.IsNullOrEmpty(scene.path)) Add(scene.path);
            foreach (string path in extraRoots) Add(path);
            rootCount = reached.Count;

            int done = 0;
            try
            {
                while (queue.Count > 0)
                {
                    string path = queue.Dequeue();
                    if ((++done & 255) == 0 && EditorUtility.DisplayCancelableProgressBar("Project cleanup: Unity's dependencies",
                            $"{done:N0} files asked, {reached.Count:N0} reached", Math.Min(0.99f, (float)done / Math.Max(1, reached.Count))))
                        return null;
                    foreach (string dep in AssetDatabase.GetDependencies(path, false))
                        if (dep.StartsWith("Assets/", StringComparison.Ordinal)) Add(dep.Replace('\\', '/'));
                }
            }
            finally { EditorUtility.ClearProgressBar(); }
            return reached;
        }

        private static string LastLine()
        {
            try
            {
                using (var s = new FileStream(ScriptLog, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var r = new StreamReader(s))
                {
                    string last = "", line;
                    while ((line = r.ReadLine()) != null) last = line;
                    return last;
                }
            }
            catch (IOException) { return ""; }
        }
    }
}
