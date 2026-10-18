using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace KryticalNova.EditorTools.AssetRestore
{
    /// <summary>
    /// What every .unitypackage on this PC holds: per asset its GUID, the path it imports to, its size and the
    /// GUIDs it points at (from the asset's YAML or JSON and from its .meta), so a single asset can be restored
    /// with exactly what it needs. Built off the main thread, cached in Library and re-read only for packages that
    /// changed.
    /// </summary>
    public sealed class PackageIndex
    {
        public sealed class Package
        {
            public string Path;
            public long Length;
            public long Ticks;
            public string Source;   // "Synty library", "Asset Store (Kronnect)", ...
            public string Name => System.IO.Path.GetFileNameWithoutExtension(Path);
        }

        public struct Item
        {
            public string Guid;
            public string AssetPath;
            public int Package;
            public long Size;
            public bool IsFolder;    // a folder entry: only a pathname and a meta
            public string[] Deps;
        }

        private const string Magic = "KNAR";
        private const int Version = 1;
        private const int MaxTextAsset = 128 << 20;
        private static readonly Regex GuidRef = new Regex(@"guid\\?""?\s*:\s*\\?""?([0-9a-f]{32})", RegexOptions.Compiled);
        private static readonly HashSet<string> BuiltIn = new HashSet<string>
        {
            "0000000000000000e000000000000000", "0000000000000000f000000000000000", "0000000000000000d000000000000000",
        };

        public readonly List<Package> Packages = new List<Package>();
        public readonly List<Item> Items = new List<Item>();
        private Dictionary<string, List<int>> byGuid;

        public int Count => Items.Count;

        /// <summary>Every copy of a GUID across the packages (shared folders such as PolygonGeneric ship in many).</summary>
        public IReadOnlyList<int> Find(string guid)
        {
            if (byGuid == null) Rebuild();
            return byGuid.TryGetValue(guid, out var list) ? list : (IReadOnlyList<int>)Array.Empty<int>();
        }

        /// <summary>The best copy of a GUID: the one whose package path matches the asset's own pack, else the first.</summary>
        public bool TryGet(string guid, out Item item)
        {
            var copies = Find(guid);
            item = default;
            if (copies.Count == 0) return false;
            item = Items[copies[0]];
            foreach (int i in copies)
            {
                var candidate = Items[i];
                if (PackMatches(candidate.AssetPath, Packages[candidate.Package].Name)) { item = candidate; break; }
            }
            return true;
        }

        public Package PackageOf(Item item) => Packages[item.Package];

        private static bool PackMatches(string assetPath, string packageName)
        {
            string pack = AssetRestorePaths.PackOf(assetPath);
            string a = Simplify(pack), b = Simplify(packageName);
            return a.Length > 0 && (b.Contains(a) || a.Contains(b));
        }

        private static string Simplify(string s) =>
            new string(s.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant().Replace("polygon", "").Replace("interface", "").Replace("animation", "");

        private void Rebuild()
        {
            byGuid = new Dictionary<string, List<int>>(Items.Count);
            for (int i = 0; i < Items.Count; i++)
            {
                if (!byGuid.TryGetValue(Items[i].Guid, out var list)) byGuid[Items[i].Guid] = list = new List<int>(1);
                list.Add(i);
            }
        }

        // ---------------------------------------------------------------- building

        /// <summary>
        /// Indexes every package under <paramref name="folders"/>, reusing <paramref name="previous"/> for packages
        /// whose size and date are unchanged. Safe to call off the main thread.
        /// </summary>
        public static PackageIndex Build(IEnumerable<string> folders, PackageIndex previous, Action<string, float> progress,
            CancellationToken cancel)
        {
            var files = new List<(string path, string source)>();
            foreach (string folder in folders.Where(Directory.Exists))
            foreach (string file in Directory.EnumerateFiles(folder, "*.unitypackage", SearchOption.AllDirectories))
                files.Add((file.Replace('\\', '/'), SourceLabel(folder, file)));
            files = files.Distinct().OrderBy(f => f.path, StringComparer.OrdinalIgnoreCase).ToList();

            var index = new PackageIndex();
            long total = files.Sum(f => new FileInfo(f.path).Length), done = 0;
            foreach (var (path, source) in files)
            {
                cancel.ThrowIfCancellationRequested();
                var info = new FileInfo(path);
                var package = new Package { Path = path, Length = info.Length, Ticks = info.LastWriteTimeUtc.Ticks, Source = source };
                int packageIndex = index.Packages.Count;
                index.Packages.Add(package);

                int old = previous?.Packages.FindIndex(p => p.Path == path && p.Length == package.Length && p.Ticks == package.Ticks) ?? -1;
                if (old >= 0)
                {
                    foreach (var item in previous.Items.Where(i => i.Package == old))
                    {
                        var copy = item;
                        copy.Package = packageIndex;
                        index.Items.Add(copy);
                    }
                }
                else
                {
                    try { IndexPackage(path, packageIndex, index.Items, f => progress?.Invoke(package.Name, (done + f * package.Length) / Math.Max(1f, total)), cancel); }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception e) { UnityEngine.Debug.LogWarning($"[AssetRestore] Could not read {path}: {e.Message}"); }
                }
                done += package.Length;
                progress?.Invoke(package.Name, done / Math.Max(1f, total));
            }
            index.Rebuild();
            return index;
        }

        /// <summary>Reads one package into <paramref name="items"/>.</summary>
        public static void IndexPackage(string path, int packageIndex, List<Item> items, Action<float> progress, CancellationToken cancel)
        {
            var found = new Dictionary<string, (string path, long size, bool hasAsset, HashSet<string> deps)>();
            using (var reader = new UnityPackageReader(path))
            {
                int n = 0;
                while (reader.Next(out var entry))
                {
                    if ((++n & 255) == 0) { cancel.ThrowIfCancellationRequested(); progress?.Invoke(reader.Progress); }
                    found.TryGetValue(entry.Guid, out var rec);
                    rec.deps = rec.deps ?? new HashSet<string>();
                    switch (entry.Part)
                    {
                        case UnityPackageReader.Part.Pathname:
                            rec.path = UnityPackageReader.PathFrom(reader.ReadAll());
                            break;
                        case UnityPackageReader.Part.Asset:
                            rec.hasAsset = true;
                            rec.size = entry.Size;
                            if (entry.Size <= MaxTextAsset)
                            {
                                byte[] head = reader.ReadHead(16);
                                if (LooksLikeText(head)) AddDeps(Concat(head, reader.ReadAll()), entry.Guid, rec.deps);
                            }
                            break;
                        case UnityPackageReader.Part.Meta:
                            AddDeps(reader.ReadAll(), entry.Guid, rec.deps);
                            break;
                    }
                    found[entry.Guid] = rec;
                }
            }
            foreach (var pair in found)
            {
                if (string.IsNullOrEmpty(pair.Value.path)) continue;
                items.Add(new Item
                {
                    Guid = pair.Key,
                    AssetPath = pair.Value.path,
                    Package = packageIndex,
                    Size = pair.Value.size,
                    IsFolder = !pair.Value.hasAsset,
                    Deps = pair.Value.deps.Count == 0 ? Array.Empty<string>() : pair.Value.deps.ToArray(),
                });
            }
        }

        /// <summary>The GUIDs a YAML, JSON or .meta file points at, without its own and Unity's built-in ones.</summary>
        public static void AddDeps(byte[] data, string self, HashSet<string> into)
        {
            foreach (Match m in GuidRef.Matches(Encoding.UTF8.GetString(data)))
            {
                string guid = m.Groups[1].Value;
                if (guid != self && !BuiltIn.Contains(guid)) into.Add(guid);
            }
        }

        private static bool LooksLikeText(byte[] head)
        {
            string s = Encoding.ASCII.GetString(head).TrimStart();
            return s.StartsWith("%YAML", StringComparison.Ordinal) || s.StartsWith("{", StringComparison.Ordinal);
        }

        private static byte[] Concat(byte[] a, byte[] b)
        {
            var all = new byte[a.Length + b.Length];
            Buffer.BlockCopy(a, 0, all, 0, a.Length);
            Buffer.BlockCopy(b, 0, all, a.Length, b.Length);
            return all;
        }

        private static string SourceLabel(string folder, string file)
        {
            string f = folder.Replace('\\', '/').TrimEnd('/');
            if (f.EndsWith("Asset Store-5.x", StringComparison.OrdinalIgnoreCase))
            {
                string rel = file.Replace('\\', '/').Substring(f.Length + 1);
                int slash = rel.IndexOf('/');
                return "Asset Store (" + (slash > 0 ? rel.Substring(0, slash) : rel) + ")";
            }
            return f == AssetRestorePaths.SyntyLibrary.TrimEnd('/') ? "Synty library" : "Packages in " + f;
        }

        // ---------------------------------------------------------------- cache

        public void Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temp = path + ".tmp";
            using (var w = new BinaryWriter(File.Create(temp), Encoding.UTF8))
            {
                w.Write(Magic);
                w.Write(Version);
                w.Write(Packages.Count);
                foreach (var p in Packages) { w.Write(p.Path); w.Write(p.Length); w.Write(p.Ticks); w.Write(p.Source); }
                w.Write(Items.Count);
                foreach (var i in Items)
                {
                    w.Write(i.Guid); w.Write(i.AssetPath); w.Write(i.Package); w.Write(i.Size); w.Write(i.IsFolder);
                    w.Write(i.Deps.Length);
                    foreach (string d in i.Deps) w.Write(d);
                }
            }
            if (File.Exists(path)) File.Delete(path);
            File.Move(temp, path);
        }

        /// <summary>The cached index, or null when there is none or it is from another version.</summary>
        public static PackageIndex Load(string path)
        {
            if (!File.Exists(path)) return null;
            try
            {
                using (var r = new BinaryReader(File.OpenRead(path), Encoding.UTF8))
                {
                    if (r.ReadString() != Magic || r.ReadInt32() != Version) return null;
                    var index = new PackageIndex();
                    int packages = r.ReadInt32();
                    for (int i = 0; i < packages; i++)
                        index.Packages.Add(new Package { Path = r.ReadString(), Length = r.ReadInt64(), Ticks = r.ReadInt64(), Source = r.ReadString() });
                    int items = r.ReadInt32();
                    index.Items.Capacity = items;
                    for (int i = 0; i < items; i++)
                    {
                        var item = new Item { Guid = r.ReadString(), AssetPath = r.ReadString(), Package = r.ReadInt32(), Size = r.ReadInt64(), IsFolder = r.ReadBoolean() };
                        int deps = r.ReadInt32();
                        item.Deps = deps == 0 ? Array.Empty<string>() : new string[deps];
                        for (int d = 0; d < deps; d++) item.Deps[d] = r.ReadString();
                        index.Items.Add(item);
                    }
                    index.Rebuild();
                    return index;
                }
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning($"[AssetRestore] Package index cache unreadable, it will be rebuilt: {e.Message}");
                return null;
            }
        }
    }
}
