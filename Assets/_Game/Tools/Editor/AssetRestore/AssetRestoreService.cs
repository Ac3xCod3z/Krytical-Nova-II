using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.EditorTools.AssetRestore
{
    /// <summary>
    /// The jobs behind the Restore Assets window and the cleanup: building the package index, taking the catalog
    /// snapshot and restoring assets. The slow parts (reading packages, walking folders) run on a worker thread so
    /// the Editor stays usable; the AssetDatabase is touched only on the main thread.
    /// </summary>
    public static class AssetRestoreService
    {
        private static PackageIndex index;
        private static Task running;
        private static CancellationTokenSource cancel;
        private static Action onMainThread;

        public static string ProjectRoot => Path.GetDirectoryName(Application.dataPath).Replace('\\', '/');
        public static bool Busy => running != null && !running.IsCompleted;
        public static string BusyLabel { get; private set; } = "";
        public static float BusyProgress { get; private set; }
        public static string LastMessage { get; private set; } = "";

        /// <summary>The package index, loaded from Library on first use (null until one has been built).</summary>
        public static PackageIndex Index => index ?? (index = PackageIndex.Load(Path.Combine(ProjectRoot, AssetRestorePaths.IndexCache)));

        public static bool InProject(string guid)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            return !string.IsNullOrEmpty(path) && (File.Exists(path) || Directory.Exists(path) || path.StartsWith("Packages/", StringComparison.Ordinal));
        }

        public static void Cancel() => cancel?.Cancel();

        // ---------------------------------------------------------------- index

        [MenuItem("Krytical Nova/Project/Cleanup/Build Package Index", priority = 141)]
        private static void BuildIndexMenu() => BuildIndex(null);

        /// <summary>Reads every package on this PC (only the ones that changed since the last time).</summary>
        public static void BuildIndex(Action done)
        {
            var folders = AssetRestorePaths.PackageFolders().ToList();
            var previous = Index;
            string cachePath = Path.Combine(ProjectRoot, AssetRestorePaths.IndexCache);
            Run("Reading packages", token =>
            {
                var built = PackageIndex.Build(folders, previous, (name, p) => { BusyLabel = "Reading " + name; BusyProgress = p; }, token);
                built.Save(cachePath);
                return () =>
                {
                    index = built;
                    LastMessage = $"Package index: {built.Packages.Count} packages, {built.Count:N0} assets.";
                    Debug.Log("[AssetRestore] " + LastMessage);
                    done?.Invoke();
                };
            });
        }

        // ---------------------------------------------------------------- catalog

        [MenuItem("Krytical Nova/Project/Cleanup/Take Catalog Snapshot", priority = 142)]
        private static void SnapshotMenu() => TakeSnapshot(null);

        /// <summary>Writes the Asset Catalog (table + page) from what is on disk now, keeping earlier rows of removed assets.</summary>
        public static void TakeSnapshot(Action done)
        {
            string root = ProjectRoot;
            var idx = Index;
            Run("Taking the catalog snapshot", token =>
            {
                string table = Path.Combine(root, AssetRestorePaths.CatalogTable);
                var previous = AssetCatalog.Load(table);
                var removedOn = new Dictionary<string, string>();
                foreach (var e in RemovalLog.LoadTable(root)) removedOn[e.Guid] = e.Date;
                var rows = AssetCatalog.Snapshot(root, idx, previous, removedOn, p => { BusyLabel = "Walking vendor folders"; BusyProgress = p; });
                string taken = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
                AssetCatalog.Save(table, rows, taken);
                AssetCatalog.SavePage(Path.Combine(root, AssetRestorePaths.CatalogPage), rows, taken);
                int removed = rows.Count(r => r.Status == AssetCatalog.Removed);
                return () =>
                {
                    LastMessage = $"Asset Catalog: {rows.Count:N0} assets ({removed:N0} removed), written to {AssetRestorePaths.CatalogTable}.";
                    Debug.Log("[AssetRestore] " + LastMessage);
                    done?.Invoke();
                };
            });
        }

        // ---------------------------------------------------------------- restore

        /// <summary>
        /// Restores <paramref name="guids"/> and what they need. With <paramref name="confirm"/> the owner is shown
        /// how many files it takes first. Old paths from the Removal Log win over the package's own path.
        /// </summary>
        public static void Restore(IEnumerable<string> guids, bool confirm, Action<AssetRestorer.Result> done = null)
        {
            var idx = Index;
            if (idx == null) { EditorUtility.DisplayDialog("Restore Assets", "Build the package index first (Krytical Nova ▸ Project ▸ Cleanup ▸ Build Package Index).", "OK"); return; }

            var oldPaths = new Dictionary<string, string>();
            foreach (var e in RemovalLog.LoadTable(ProjectRoot)) oldPaths[e.Guid] = e.Path;
            var plan = AssetRestorer.PlanFor(idx, guids, InProject, oldPaths);

            if (plan.Items.Count == 0)
            {
                LastMessage = plan.AlreadyInProject.Count > 0 ? "Already in the project." : "Not found in any package on this PC.";
                if (confirm) EditorUtility.DisplayDialog("Restore Assets", LastMessage, "OK");
                return;
            }
            if (confirm && !EditorUtility.DisplayDialog("Restore Assets",
                    $"Bring back {plan.Files:N0} file(s), {RemovalLog.Size(plan.Bytes)}, from {plan.Items.Select(i => i.Package).Distinct().Count()} package(s)?\n\n" +
                    string.Join("\n", plan.Targets.Values.Take(12)) + (plan.Targets.Count > 12 ? $"\n… and {plan.Targets.Count - 12} more" : "") +
                    (plan.NotInAnyPackage.Count > 0 ? $"\n\n{plan.NotInAnyPackage.Count} reference(s) are in no package (scripts or built-ins); they are left as they are." : ""),
                    "Restore", "Cancel"))
                return;

            string root = ProjectRoot;
            Run("Restoring", token =>
            {
                var result = AssetRestorer.Execute(idx, plan, root, (name, p) => { BusyLabel = "Restoring from " + name; BusyProgress = p; }, token);
                return () =>
                {
                    AssetDatabase.Refresh();
                    LastMessage = $"Restored {result.Written.Count} file(s)" + (result.Skipped.Count > 0 ? $", skipped {result.Skipped.Count}" : "") + ".";
                    Debug.Log("[AssetRestore] " + LastMessage + "\n" + string.Join("\n", result.Written.Concat(result.Skipped.Select(s => "skipped " + s))));
                    var first = result.Written.FirstOrDefault(p => !p.EndsWith("/"));
                    if (first != null) EditorGUIUtility.PingObject(AssetDatabase.LoadMainAssetAtPath(first));
                    done?.Invoke(result);
                };
            });
        }

        /// <summary>The preview picture a package carries for an asset, cached in Library (null if it has none).</summary>
        public static Texture2D Preview(string guid)
        {
            string cached = Path.Combine(ProjectRoot, AssetRestorePaths.PreviewCache, guid + ".png");
            if (File.Exists(cached)) return Load(cached);
            if (Busy || Index == null || !Index.TryGet(guid, out var item)) return null;
            string package = Index.PackageOf(item).Path;
            Run("Reading the preview", token =>
            {
                byte[] png = null;
                using (var reader = new UnityPackageReader(package))
                    while (reader.Next(out var entry))
                    {
                        token.ThrowIfCancellationRequested();
                        if (entry.Guid == guid && entry.Part == UnityPackageReader.Part.Preview) { png = reader.ReadAll(); break; }
                        BusyProgress = reader.Progress;
                    }
                Directory.CreateDirectory(Path.GetDirectoryName(cached));
                File.WriteAllBytes(cached, png ?? Array.Empty<byte>());
                return null;
            });
            return null;
        }

        private static Texture2D Load(string file)
        {
            byte[] data = File.ReadAllBytes(file);
            if (data.Length == 0) return null;
            var tex = new Texture2D(2, 2) { hideFlags = HideFlags.HideAndDontSave };
            return tex.LoadImage(data) ? tex : null;
        }

        // ---------------------------------------------------------------- running

        private static void Run(string label, Func<CancellationToken, Action> work)
        {
            if (Busy) { Debug.LogWarning("[AssetRestore] Still busy with " + BusyLabel); return; }
            cancel = new CancellationTokenSource();
            var token = cancel.Token;
            BusyLabel = label;
            BusyProgress = 0f;
            running = Task.Run(() =>
            {
                try { onMainThread = work(token); }
                catch (OperationCanceledException) { onMainThread = () => LastMessage = label + ": cancelled."; }
                catch (Exception e) { onMainThread = () => { LastMessage = label + " failed: " + e.Message; Debug.LogException(e); }; }
            });
            EditorApplication.update -= Pump;
            EditorApplication.update += Pump;
        }

        private static void Pump()
        {
            if (Busy) return;
            EditorApplication.update -= Pump;
            var then = onMainThread;
            onMainThread = null;
            BusyLabel = "";
            then?.Invoke();
        }
    }
}
