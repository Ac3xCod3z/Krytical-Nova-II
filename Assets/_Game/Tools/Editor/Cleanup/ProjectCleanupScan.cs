using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using CodeStage.Maintainer.Cleaner;
using CodeStage.Maintainer.Core;
using KryticalNova.EditorTools.AssetRestore;
using UnityEditor;
using UnityEngine;
using MaintainerSettings = CodeStage.Maintainer.Settings.ProjectSettings;

namespace KryticalNova.EditorTools.Cleanup
{
    /// <summary>
    /// The project cleanup on top of Code Stage Maintainer's Project Cleaner (owner, 18 Oct 2026).
    /// <para><b>Scan (report only)</b>: Maintainer lists what nothing uses, counting as used the build scenes, every
    /// Resources folder, the settings and all of <c>Assets/_Game</c>; this class then also keeps what our editor
    /// builders load by path or by name (Maintainer cannot see those) with everything they depend on, and code and
    /// shaders. The result goes to <c>Library/KryticalNova.Cleanup</c>, nothing is removed.</para>
    /// <para><b>Remove Unused</b>: scans afresh and removes every file that passed all three checks and has a
    /// restore source (owner, 18 Oct 2026: no picking by hand), writing the Removal Log first, then restores at once
    /// anything still pointed at.</para>
    /// </summary>
    public static class ProjectCleanupScan
    {
        public const string ReportFolder = "Library/KryticalNova.Cleanup";
        public const string ScanTable = ReportFolder + "/Scan.tsv";
        public const string ScanSummary = ReportFolder + "/Scan-Summary.md";

        /// <summary>Only these can be listed for removal.</summary>
        public static readonly string[] Candidates =
        {
            "Assets/Synty/", "Assets/_ThirdParty/", "Assets/_Recovery/", "Assets/QuickDestruction/Demo/", "Assets/TextMesh Pro/Examples & Extras/",
        };

        /// <summary>Counted as used, with everything they reference.</summary>
        public static readonly string[] Roots =
        {
            "Assets/_Game/", "Assets/Synty/SidekickCharacters/", "Assets/Synty/Tools/", "Assets/Synty/SyntyPackageHelper/",
        };

        /// <summary>Folders inside the Interface packs our HUD, faction and plate builders read by path.</summary>
        private static readonly string[] InterfaceKeep = { "/Prefabs/", "/Sprites/", "/Fonts/" };

        /// <summary>Code, shaders and assembly files: they go only with a whole package, never one by one.</summary>
        private static readonly HashSet<string> CodeKinds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".cs", ".dll", ".asmdef", ".asmref", ".rsp", ".shader", ".shadergraph", ".shadersubgraph", ".hlsl", ".cginc", ".cg", ".glslinc", ".compute", ".raytrace", ".cfxrshader", ".so", ".bundle", ".jslib", ".uss", ".uxml", ".tss",
        };

        private static readonly Regex VendorLiteral = new Regex(@"""(Assets/(?:Synty|_ThirdParty|QuickDestruction)/[^""]+)""", RegexOptions.Compiled);
        private static readonly Regex QuotedName = new Regex(@"""([A-Za-z0-9_\-\.\(\) ]{4,96})""", RegexOptions.Compiled);

        // ---------------------------------------------------------------- scan

        [MenuItem("Krytical Nova/Project/Cleanup/Scan For Unused Vendor Assets (report only)", priority = 150)]
        private static void ScanMenu()
        {
            if (!EditorUtility.DisplayDialog("Project cleanup scan",
                    "Maintainer will read every asset in the project to find the vendor files nothing uses. The first scan builds its assets map and " +
                    "can keep the Editor busy for several minutes.\n\nNothing is removed: the result is a report in Library/KryticalNova.Cleanup.",
                    "Scan", "Cancel"))
                return;
            Scan();
        }

        /// <summary>Runs the scan and writes the report. Returns the number of removable files, or -1 if cancelled.</summary>
        public static int Scan()
        {
            // The written-reference check reads files in its own process while Maintainer works.
            var written = UsageGuard.StartWrittenCheck("scan");
            if (written == null) return -1;

            ApplyMaintainerFilters();
            var records = ProjectCleaner.StartSearch(false);
            if (records == null) { TryKill(written); return -1; }

            var listed = records.OfType<CodeStage.Maintainer.Cleaner.AssetRecord>()
                .Where(r => r.Type == RecordType.UnreferencedAsset)
                .Select(r => r.AssetPath.Replace('\\', '/'))
                .Where(p => Candidates.Any(c => p.StartsWith(c, StringComparison.Ordinal)))
                .Distinct().ToList();

            HashSet<string> keep;
            try
            {
                EditorUtility.DisplayProgressBar("Project cleanup", "Keeping what our builders load by path or name…", 0.05f);
                keep = PathLoadedKeepSet(listed);
            }
            finally { EditorUtility.ClearProgressBar(); }

            var unityUsed = UsageGuard.UnityClosure(IsCandidate, keep, out int rootCount);
            if (unityUsed == null) { TryKill(written); Debug.LogWarning("[Cleanup] Scan cancelled."); return -1; }
            if (!UsageGuard.Wait(written, "Project cleanup: written references")) { Debug.LogWarning("[Cleanup] Scan stopped: the written-reference check did not finish."); return -1; }
            var writtenUsed = UsageGuard.ReadWrittenUsed();

            var index = AssetRestoreService.Index;
            var rows = new List<string[]>();
            var why = new Dictionary<string, int> { { "loaded by path or name", 0 }, { "code or shader", 0 }, { "Unity dependency", 0 }, { "written reference", 0 }, { "no restore source", 0 } };
            foreach (string path in listed)
            {
                if (keep.Contains(path)) { why["loaded by path or name"]++; continue; }
                if (CodeKinds.Contains(Path.GetExtension(path))) { why["code or shader"]++; continue; }
                if (unityUsed.Contains(path)) { why["Unity dependency"]++; continue; }
                if (writtenUsed.Contains(path)) { why["written reference"]++; continue; }
                string guid = AssetDatabase.AssetPathToGUID(path);
                string source = AssetCatalog.SourceOf(index, guid);
                if (source.Length == 0) { why["no restore source"]++; continue; }
                long bytes = File.Exists(path) ? new FileInfo(path).Length : 0;
                rows.Add(new[] { path, guid, bytes.ToString(CultureInfo.InvariantCulture), AssetRestorePaths.PackOf(path), AssetRestorePaths.KindOf(path), source });
            }
            int kept = why.Values.Sum();

            Directory.CreateDirectory(ReportFolder);
            using (var w = new StreamWriter(ScanTable, false, new UTF8Encoding(false)))
            {
                w.NewLine = "\n";
                w.WriteLine("path\tguid\tbytes\tpack\tkind\tsource");
                foreach (var r in rows.OrderBy(r => r[0], StringComparer.OrdinalIgnoreCase)) w.WriteLine(string.Join("\t", r));
            }
            WriteSummary(rows, listed.Count, why, rootCount, index != null);
            Debug.Log($"[Cleanup] Scan: Maintainer listed {listed.Count:N0}; {rows.Count:N0} pass all three checks " +
                      $"({RemovalLog.Size(rows.Sum(r => long.Parse(r[2], CultureInfo.InvariantCulture)))}); {kept:N0} kept. Report: {ScanSummary}");
            return rows.Count;
        }

        private static void TryKill(System.Diagnostics.Process process)
        {
            try { if (process != null && !process.HasExited) process.Kill(); } catch (Exception) { }
        }

        /// <summary>A file the cleanup may touch: under a candidate folder and not in a kept one.</summary>
        public static bool IsCandidate(string path)
        {
            if (!Candidates.Any(c => path.StartsWith(c, StringComparison.Ordinal))) return false;
            if (Roots.Any(r => path.StartsWith(r, StringComparison.Ordinal))) return false;
            if (path.StartsWith("Assets/Synty/Interface", StringComparison.Ordinal) && InterfaceKeep.Any(k => path.IndexOf(k, StringComparison.Ordinal) > 0)) return false;
            if (path.IndexOf("/Resources/", StringComparison.Ordinal) >= 0 || path.IndexOf("/Plugins/", StringComparison.Ordinal) >= 0
                || path.IndexOf("/Editor/", StringComparison.Ordinal) >= 0 || path.IndexOf("/StreamingAssets/", StringComparison.Ordinal) >= 0) return false;
            return true;
        }

        /// <summary>Points Maintainer's Project Cleaner at our rules and saves them, so its own window shows the same.</summary>
        public static void ApplyMaintainerFilters()
        {
            var cleaner = MaintainerSettings.Cleaner;
            var ignores = new List<FilterItem>(ProjectCleanerSettingsDefaults());
            ignores.AddRange(Roots.Select(r => FilterItem.Create(r, FilterKind.Path, true)));
            foreach (string pack in Directory.GetDirectories("Assets/Synty", "Interface*"))
            foreach (string keep in InterfaceKeep)
                ignores.Add(FilterItem.Create(pack.Replace('\\', '/') + keep, FilterKind.Path, true));
            cleaner.pathIgnoresFilters = ignores.ToArray();
            cleaner.pathIncludesFilters = Candidates.Select(c => FilterItem.Create(c, FilterKind.Path, true)).ToArray();
            cleaner.ignoreScenesInBuild = true;
            cleaner.ignoreOnlyEnabledScenesInBuild = true;
            cleaner.scenesSelection = CodeStage.Maintainer.Settings.ProjectCleanerSettings.ScenesSelection.ScenesInFilters;
            cleaner.findUnreferencedAssets = true;
            cleaner.findUnreferencedScripts = false;
            cleaner.findEmptyFolders = false;
            cleaner.ignoreEditorAssets = true;
            MaintainerSettings.Save();
        }

        private static IEnumerable<FilterItem> ProjectCleanerSettingsDefaults() =>
            CodeStage.Maintainer.Settings.ProjectCleanerSettings.GetDefaultFilters();

        /// <summary>
        /// Vendor files our code reaches without a reference Maintainer can follow: literal "Assets/Synty/…" paths
        /// (an interpolated part matches any one folder), and files whose name appears quoted in _Game code or data
        /// (builders find prefabs by name inside a pack). Returned with everything they depend on.
        /// </summary>
        public static HashSet<string> PathLoadedKeepSet(IReadOnlyCollection<string> candidates)
        {
            var literals = new List<Regex>();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string file in Directory.EnumerateFiles("Assets/_Game", "*.*", SearchOption.AllDirectories))
            {
                string ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext != ".cs" && ext != ".json") continue;
                string text = File.ReadAllText(file);
                foreach (Match m in VendorLiteral.Matches(text))
                {
                    string literal = m.Groups[1].Value;
                    string pattern = "^" + string.Join("[^/]+", Regex.Split(literal, @"\{[^}]*\}").Select(Regex.Escape));
                    if (pattern.Length - 1 > "Assets/_ThirdParty/".Length) literals.Add(new Regex(pattern, RegexOptions.IgnoreCase));
                }
                foreach (Match m in QuotedName.Matches(text)) names.Add(m.Groups[1].Value.Trim());
            }

            var protect = candidates.Where(p =>
                names.Contains(Path.GetFileNameWithoutExtension(p)) || names.Contains(Path.GetFileName(p)) || literals.Any(r => r.IsMatch(p))).ToArray();
            var keep = new HashSet<string>(protect, StringComparer.Ordinal);
            if (protect.Length > 0)
                foreach (string dep in AssetDatabase.GetDependencies(protect, true)) keep.Add(dep.Replace('\\', '/'));
            return keep;
        }

        private static void WriteSummary(List<string[]> rows, int listed, Dictionary<string, int> why, int roots, bool haveIndex)
        {
            var sb = new StringBuilder();
            sb.Append("# Cleanup scan ").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)).Append("\n\n");
            sb.Append($"Maintainer listed {listed:N0} vendor files as unused. {rows.Count:N0} of them pass every check and can go, ");
            sb.Append($"{RemovalLog.Size(rows.Sum(r => long.Parse(r[2], CultureInfo.InvariantCulture)))}. Checks ran from {roots:N0} roots ");
            sb.Append("(everything outside the vendor folders, all of `Assets/_Game`, the build scenes, ProjectSettings and embedded packages).\n\n");
            sb.Append("Kept, first reason that applied:\n\n");
            foreach (var pair in why) sb.Append($"- {pair.Key}: {pair.Value:N0}\n");
            if (!haveIndex) sb.Append("\n**No package index yet**, so every file counts as having no restore source and is kept; build the index and scan again.\n");
            sb.Append("\n| Pack | Files | Size | Restorable from a package | Git history only |\n|---|---:|---:|---:|---:|\n");
            foreach (var pack in rows.GroupBy(r => r[0].StartsWith("Assets/Synty/") || r[0].StartsWith("Assets/_ThirdParty/") ? r[3] : r[0].Split('/')[1])
                                     .OrderByDescending(g => g.Sum(r => long.Parse(r[2], CultureInfo.InvariantCulture))))
            {
                long bytes = pack.Sum(r => long.Parse(r[2], CultureInfo.InvariantCulture));
                int restorable = pack.Count(r => r[5].Length > 0);
                sb.Append($"| {pack.Key} | {pack.Count():N0} | {RemovalLog.Size(bytes)} | {restorable:N0} | {pack.Count() - restorable:N0} |\n");
            }
            File.WriteAllText(ScanSummary, sb.ToString(), new UTF8Encoding(false));
        }

        // ---------------------------------------------------------------- remove

        [MenuItem("Krytical Nova/Project/Cleanup/Remove Unused Vendor Assets…", priority = 151)]
        private static void RemoveMenu() => RemoveUnused(true, !(ScanIsFresh() && EditorUtility.DisplayDialog("Remove unused vendor assets",
            "Use the scan from " + File.GetLastWriteTime(ScanTable).ToString("HH:mm") + ", or scan again first?", "Use that scan", "Scan again")));

        /// <summary>A scan from the last three hours can be reused; the after-check still runs over the whole project.</summary>
        public static bool ScanIsFresh() => File.Exists(ScanTable) && (DateTime.Now - File.GetLastWriteTime(ScanTable)).TotalHours < 3;

        /// <summary>
        /// Scans afresh, then removes every file that passed all three checks and has a restore source, one Removal
        /// Log batch per pack (the log is written before the files go). Files go to the Recycle Bin; git keeps them
        /// too. Afterwards anything still in the project that writes a reference to a removed file gets it back at
        /// once. Returns the number of files removed.
        /// </summary>
        public static int RemoveUnused(bool confirm, bool rescan = true)
        {
            if (AssetRestoreService.Index == null) { Debug.LogWarning("[Cleanup] Build the package index first: nothing goes without a way back."); return 0; }
            if (rescan || !ScanIsFresh()) { if (Scan() <= 0) return 0; }
            else
            {
                // A reused scan: the written-reference check runs again (about a minute, outside Unity), so files a keep
                // rule added since the scan bring what they point at with them (18 Oct 2026: the Prop Bone Tool's
                // settings kept after the scan, and the five rigs they point at removed, then restored by the after-check).
                if (!UsageGuard.Wait(UsageGuard.StartWrittenCheck("scan"), "Project cleanup: written references")) return 0;
            }
            var stillUsed = UsageGuard.ReadWrittenUsed();
            // The keep rules are applied again, so a rule added since the scan still holds.
            var scan = File.ReadAllLines(ScanTable).Skip(1).Select(l => l.Split('\t'))
                .Where(c => c.Length >= 6 && c[5].Length > 0 && IsCandidate(c[0]) && !CodeKinds.Contains(Path.GetExtension(c[0]))
                            && !stillUsed.Contains(c[0]) && File.Exists(c[0])).ToList();
            var batches = scan.GroupBy(c => BatchName(c[0])).OrderBy(g => g.Key).ToList();
            int total = scan.Count;
            long bytes = scan.Sum(r => long.Parse(r[2], CultureInfo.InvariantCulture));
            if (total == 0) { Debug.Log("[Cleanup] Nothing to remove."); return 0; }
            if (confirm && !EditorUtility.DisplayDialog("Remove unused vendor assets",
                    $"Move {total:N0} files ({RemovalLog.Size(bytes)}) from {batches.Count} pack(s) to the Recycle Bin?\n\n" +
                    "Each passed Maintainer, Unity's dependencies and the written-reference check, and each can be restored from its package. " +
                    "The Removal Log is written first.", "Remove", "Cancel"))
                return 0;

            string root = AssetRestoreService.ProjectRoot;
            string commit = RemovalLog.HeadCommit(root);
            int removed = 0;
            try
            {
                foreach (var batch in batches)
                {
                    var rows = batch.ToList();
                    RemovalLog.RecordBatch(root, batch.Key,
                        "Vendor files nothing uses: not the build scenes, not any scene, zone or asset in `Assets/_Game`, not Resources, settings or plugins. " +
                        "Three checks agreed (Maintainer, Unity's dependencies, written references) and none is loaded by path or name.",
                        rows.Select(r => new RemovalLog.Removal { Path = r[0], Guid = r[1], Bytes = long.Parse(r[2], CultureInfo.InvariantCulture) }).ToList(),
                        AssetRestoreService.Index, commit);

                    // One pack at a time with importing paused, so a variant is never re-imported in the moment between
                    // its parent going and itself going (the first run logged 1,429 such passing errors).
                    if (EditorUtility.DisplayCancelableProgressBar("Removing " + batch.Key, $"{removed:N0} / {total:N0}", (float)removed / total))
                        throw new OperationCanceledException();
                    var failed = new List<string>();
                    AssetDatabase.StartAssetEditing();
                    try { AssetDatabase.MoveAssetsToTrash(rows.Select(r => r[0]).ToArray(), failed); }
                    finally { AssetDatabase.StopAssetEditing(); }
                    removed += rows.Count - failed.Count;
                    foreach (string f in failed) Debug.LogWarning("[Cleanup] Could not remove " + f);
                    RemoveEmptyFolders(BatchFolder(rows[0][0]));
                }
            }
            catch (OperationCanceledException) { Debug.LogWarning("[Cleanup] Stopped part way; what was removed is in the log."); }
            finally { EditorUtility.ClearProgressBar(); }
            AssetDatabase.Refresh();
            Debug.Log($"[Cleanup] Removed {removed:N0} files. Log: {AssetRestorePaths.RemovalLog}");

            RestoreAnythingStillReferenced();
            return removed;
        }

        /// <summary>The safety net: anything a file still in the project points at that was removed comes straight back.</summary>
        public static int RestoreAnythingStillReferenced()
        {
            // The script reads the whole Removal Log, so earlier batches are checked again too.
            if (!UsageGuard.Wait(UsageGuard.StartWrittenCheck("after"), "Project cleanup: after-check"))
            {
                Debug.LogError("[Cleanup] The after-check did not finish; run Krytical Nova ▸ Project ▸ Cleanup ▸ Check Nothing Points At A Removed File.");
                return -1;
            }
            var broken = UsageGuard.ReadStillReferenced();
            if (broken.Count == 0) { Debug.Log("[Cleanup] After-check: nothing in the project points at a removed file."); return 0; }
            Debug.LogWarning($"[Cleanup] After-check: {broken.Count} removed file(s) are still referenced; restoring them now.");
            AssetRestoreService.Restore(broken, false);
            return broken.Count;
        }

        [MenuItem("Krytical Nova/Project/Cleanup/Check Nothing Points At A Removed File", priority = 152)]
        private static void AfterCheckMenu() => RestoreAnythingStillReferenced();

        private static string BatchName(string path)
        {
            string folder = BatchFolder(path);
            return folder.StartsWith("Assets/Synty/") || folder.StartsWith("Assets/_ThirdParty/") ? folder.Substring(folder.IndexOf('/', 7) + 1) : folder.Substring(7);
        }

        /// <summary>"Assets/Synty/PolygonKnights/…" → "Assets/Synty/PolygonKnights"; otherwise the candidate folder itself.</summary>
        private static string BatchFolder(string path)
        {
            string[] parts = path.Split('/');
            if (parts.Length > 3 && (parts[1] == "Synty" || parts[1] == "_ThirdParty")) return string.Join("/", parts.Take(3));
            string candidate = Candidates.FirstOrDefault(c => path.StartsWith(c, StringComparison.Ordinal));
            return candidate != null ? candidate.TrimEnd('/') : Path.GetDirectoryName(path).Replace('\\', '/');
        }

        private static void RemoveEmptyFolders(string folder)
        {
            if (!AssetDatabase.IsValidFolder(folder)) return;
            foreach (string sub in AssetDatabase.GetSubFolders(folder)) RemoveEmptyFolders(sub);
            bool hasFiles = Directory.EnumerateFiles(folder).Any(f => !f.EndsWith(".meta", StringComparison.OrdinalIgnoreCase));
            if (!hasFiles && !Directory.EnumerateDirectories(folder).Any()) AssetDatabase.MoveAssetToTrash(folder);
        }
    }
}
