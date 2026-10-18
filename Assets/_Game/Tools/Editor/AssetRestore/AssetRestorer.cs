using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace KryticalNova.EditorTools.AssetRestore
{
    /// <summary>
    /// Brings single assets back from the packages on this PC: the asset, everything it points at that the project
    /// no longer has (materials, textures, meshes, sub-prefabs) and the folders they sit in, each with its original
    /// .meta so its GUID is the one scenes and prefabs still reference. Files already in the project are never
    /// touched, so upgraded shared materials stay as they are.
    /// </summary>
    public static class AssetRestorer
    {
        public sealed class Plan
        {
            public readonly List<PackageIndex.Item> Items = new List<PackageIndex.Item>();
            public readonly Dictionary<string, string> Targets = new Dictionary<string, string>();   // guid → path to write
            public readonly List<string> AlreadyInProject = new List<string>();
            public readonly List<string> NotInAnyPackage = new List<string>();
            public long Bytes;
            public int Files => Items.Count(i => !i.IsFolder);
        }

        public sealed class Result
        {
            public readonly List<string> Written = new List<string>();
            public readonly List<string> Skipped = new List<string>();   // "path: reason"
        }

        /// <summary>
        /// Works out what restoring <paramref name="guids"/> takes. <paramref name="inProject"/> says whether a GUID
        /// is already in the project (asset or package); <paramref name="oldPaths"/> sends an asset back to where the
        /// cleanup removed it from when that differs from the package's path.
        /// </summary>
        public static Plan PlanFor(PackageIndex index, IEnumerable<string> guids, Func<string, bool> inProject,
            IReadOnlyDictionary<string, string> oldPaths = null, Func<string, bool> pathExists = null)
        {
            pathExists = pathExists ?? (p => File.Exists(p) || Directory.Exists(p));
            var plan = new Plan();
            var seen = new HashSet<string>();
            var queue = new Queue<string>(guids.Distinct());
            var roots = new HashSet<string>(queue);
            var folderNeeds = new List<string>();

            while (queue.Count > 0)
            {
                string guid = queue.Dequeue();
                if (!seen.Add(guid)) continue;
                if (inProject(guid)) { if (roots.Contains(guid)) plan.AlreadyInProject.Add(guid); continue; }
                if (!index.TryGet(guid, out var item)) { plan.NotInAnyPackage.Add(guid); continue; }

                string target = oldPaths != null && oldPaths.TryGetValue(guid, out string old) ? old : item.AssetPath;
                if (!IsSafeTarget(target)) { plan.NotInAnyPackage.Add(guid); continue; }

                plan.Items.Add(item);
                plan.Targets[guid] = target;
                plan.Bytes += item.Size;
                folderNeeds.Add(target);
                foreach (string dep in item.Deps) queue.Enqueue(dep);
            }

            // The folders an asset goes into, with their own metas when the package has them.
            var folderItems = new Dictionary<string, PackageIndex.Item>(StringComparer.OrdinalIgnoreCase);
            var byPackage = new HashSet<int>(plan.Items.Select(i => i.Package));
            foreach (var folder in index.Items.Where(i => i.IsFolder && byPackage.Contains(i.Package)))
                if (!folderItems.ContainsKey(folder.AssetPath)) folderItems[folder.AssetPath] = folder;

            var addedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string target in folderNeeds)
            {
                string dir = Parent(target);
                while (!string.IsNullOrEmpty(dir) && dir != "Assets" && !pathExists(dir))
                {
                    if (addedFolders.Add(dir) && folderItems.TryGetValue(dir, out var folder) && !inProject(folder.Guid) && !plan.Targets.ContainsKey(folder.Guid))
                    {
                        plan.Items.Add(folder);
                        plan.Targets[folder.Guid] = dir;
                    }
                    dir = Parent(dir);
                }
            }
            return plan;
        }

        /// <summary>Writes the plan's files. Pure file IO (no Unity API), so it may run off the main thread; refresh the AssetDatabase afterwards.</summary>
        public static Result Execute(PackageIndex index, Plan plan, string projectRoot, Action<string, float> progress, CancellationToken cancel)
        {
            var result = new Result();
            var groups = plan.Items.GroupBy(i => i.Package).ToList();
            int g = 0;
            foreach (var group in groups)
            {
                var package = index.Packages[group.Key];
                var wanted = group.ToDictionary(i => i.Guid, i => i);
                var gotAsset = new HashSet<string>();
                var gotMeta = new HashSet<string>();
                var wroteAsset = new HashSet<string>();
                // A file already at the target is the project's own: neither it nor a meta beside it is touched.
                var occupied = new HashSet<string>(group.Where(i => !i.IsFolder && File.Exists(Path.Combine(projectRoot, plan.Targets[i.Guid]))).Select(i => i.Guid));
                int remaining = wanted.Count * 2 - wanted.Values.Count(i => i.IsFolder);

                using (var reader = new UnityPackageReader(package.Path))
                {
                    while (remaining > 0 && reader.Next(out var entry))
                    {
                        cancel.ThrowIfCancellationRequested();
                        progress?.Invoke(package.Name, (g + reader.Progress) / groups.Count);
                        if (!wanted.TryGetValue(entry.Guid, out var item)) continue;
                        string target = Path.Combine(projectRoot, plan.Targets[item.Guid]).Replace('\\', '/');

                        if (entry.Part == UnityPackageReader.Part.Asset && !item.IsFolder && gotAsset.Add(item.Guid))
                        {
                            remaining--;
                            if (occupied.Contains(item.Guid) || File.Exists(target)) { result.Skipped.Add(plan.Targets[item.Guid] + ": a file is already there"); continue; }
                            Directory.CreateDirectory(Path.GetDirectoryName(target));
                            string temp = target + ".restoring";
                            using (var file = File.Create(temp)) reader.CopyTo(file);
                            File.Move(temp, target);
                            wroteAsset.Add(item.Guid);
                            result.Written.Add(plan.Targets[item.Guid]);
                        }
                        else if (entry.Part == UnityPackageReader.Part.Meta && gotMeta.Add(item.Guid))
                        {
                            remaining--;
                            if (occupied.Contains(item.Guid)) continue;
                            string meta = target + ".meta";
                            if (item.IsFolder) Directory.CreateDirectory(target);
                            else Directory.CreateDirectory(Path.GetDirectoryName(target));
                            if (!File.Exists(meta)) File.WriteAllBytes(meta, reader.ReadAll());
                            if (item.IsFolder) result.Written.Add(plan.Targets[item.Guid] + "/");
                        }
                    }
                }
                foreach (var item in wanted.Values)
                    if ((!item.IsFolder && !gotAsset.Contains(item.Guid)) || !gotMeta.Contains(item.Guid))
                        result.Skipped.Add(plan.Targets[item.Guid] + $": not found in {package.Name}");
                g++;
            }
            return result;
        }

        /// <summary>Only paths inside Assets/, never climbing out of it.</summary>
        public static bool IsSafeTarget(string path) =>
            !string.IsNullOrEmpty(path) && path.StartsWith("Assets/", StringComparison.Ordinal) && !path.Contains("..") && !Path.IsPathRooted(path);

        private static string Parent(string path)
        {
            int slash = path.LastIndexOf('/');
            return slash > 0 ? path.Substring(0, slash) : null;
        }
    }
}
