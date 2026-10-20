using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace KryticalNova.EditorTools.AssetRestore
{
    /// <summary>
    /// The record of everything the cleanup removed, written before the files go:
    /// <list type="bullet">
    /// <item><c>_Docs/Cleanup/Removal-Log.md</c>: one section per batch (what, why, how much, from which packages, the commit that still has it).</item>
    /// <item><c>Lists~/Removed-&lt;date&gt;-&lt;batch&gt;.md</c>: the batch's items one per line, grouped by pack and folder, each with where to get it back.</item>
    /// <item><c>Lists~/Removed-Assets.tsv</c>: the same as a table, read by the Restore window's Removed tab.</item>
    /// </list>
    /// </summary>
    public static class RemovalLog
    {
        public struct Removal
        {
            public string Guid;
            public string Path;
            public long Bytes;
        }

        public sealed class Entry
        {
            public string Date, Batch, Guid, Path, Kind, Source;
            public long Bytes;
        }

        private const string TableHeader = "date\tbatch\tguid\tpath\tkind\tbytes\tsource";

        /// <summary>Logs one batch of removed assets. Call before deleting, so a crash mid-delete still leaves the log.</summary>
        public static string RecordBatch(string projectRoot, string batch, string reason, IReadOnlyList<Removal> items, PackageIndex index, string commit)
        {
            string date = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            string slug = Slug(batch);
            string listPath = $"{AssetRestorePaths.ListsFolder}/Removed-{date}-{slug}.md";
            var rows = items.Select(i => new Entry
            {
                Date = date, Batch = batch, Guid = i.Guid, Path = i.Path, Kind = AssetRestorePaths.KindOf(i.Path), Bytes = i.Bytes,
                Source = AssetCatalog.SourceOf(index, i.Guid),
            }).OrderBy(e => e.Path, StringComparer.OrdinalIgnoreCase).ToList();

            AppendTable(projectRoot, rows);
            WriteList(Path.Combine(projectRoot, listPath), batch, reason, date, commit, rows);
            AppendSection(projectRoot, batch, reason, date, commit, rows, listPath);
            return listPath;
        }

        /// <summary>Logs something that is not a file under Assets: a Unity package, a setting, a folder outside the project.</summary>
        public static void RecordNote(string projectRoot, string title, string reason, IEnumerable<string> lines)
        {
            string date = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var sb = new StringBuilder();
            sb.Append("\n## ").Append(date).Append(": ").Append(title).Append("\n\n").Append(reason).Append("\n\n");
            foreach (string line in lines) sb.Append("- ").Append(line).Append('\n');
            AppendToLog(projectRoot, sb.ToString());
        }

        public static List<Entry> LoadTable(string projectRoot)
        {
            string path = Path.Combine(projectRoot, AssetRestorePaths.RemovedTable);
            var list = new List<Entry>();
            if (!File.Exists(path)) return list;
            foreach (string line in File.ReadLines(path))
            {
                if (line.Length == 0 || line[0] == '#' || line.StartsWith("date\t", StringComparison.Ordinal)) continue;
                string[] c = line.Split('\t');
                if (c.Length < 7) continue;
                list.Add(new Entry { Date = c[0], Batch = c[1], Guid = c[2], Path = c[3], Kind = c[4], Bytes = long.TryParse(c[5], out long b) ? b : 0, Source = c[6] });
            }
            return list;
        }

        // ---------------------------------------------------------------- writing

        private static void AppendTable(string projectRoot, List<Entry> rows)
        {
            string path = Path.Combine(projectRoot, AssetRestorePaths.RemovedTable);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            bool fresh = !File.Exists(path);
            using (var w = new StreamWriter(path, true, new UTF8Encoding(false)))
            {
                w.NewLine = "\n";
                if (fresh)
                {
                    w.WriteLine("# Every asset the project cleanup removed. Restore with Krytical Nova > Project > Restore Assets (Removed tab).");
                    w.WriteLine(TableHeader);
                }
                foreach (var e in rows)
                    w.WriteLine(string.Join("\t", e.Date, e.Batch, e.Guid, e.Path, e.Kind, e.Bytes.ToString(CultureInfo.InvariantCulture), e.Source));
            }
        }

        private static void WriteList(string path, string batch, string reason, string date, string commit, List<Entry> rows)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var sb = new StringBuilder();
            sb.Append("# Removed ").Append(date).Append(": ").Append(batch).Append("\n\n");
            sb.Append(reason).Append("\n\n");
            sb.Append($"{rows.Count:N0} files, {Size(rows.Sum(r => r.Bytes))}. Last commit that still has them: `{commit}`.\n\n");
            sb.Append("To bring one back: Unity ▸ **Krytical Nova ▸ Project ▸ Restore Assets**, *Removed* tab, search its name, **Restore** ");
            sb.Append("(it comes back with the materials, textures and meshes it needs, under its old path and GUID). ");
            sb.Append($"Without the tool: `git checkout {commit} -- \"<path>\" \"<path>.meta\"`.\n");

            foreach (var pack in rows.GroupBy(r => AssetRestorePaths.PackOf(r.Path)).OrderBy(g => g.Key))
            {
                var sources = pack.Select(r => SourceFile(r.Source)).Where(s => s.Length > 0).Distinct().ToList();
                sb.Append("\n## ").Append(pack.Key).Append(" (").Append(pack.Count().ToString("N0")).Append(" files, ").Append(Size(pack.Sum(r => r.Bytes))).Append(")\n\n");
                sb.Append("Get it back from: ").Append(sources.Count > 0 ? string.Join(", ", sources) : "git history only").Append("\n");
                foreach (var folder in pack.GroupBy(r => Path.GetDirectoryName(r.Path).Replace('\\', '/')).OrderBy(g => g.Key))
                {
                    sb.Append("\n**").Append(folder.Key).Append("/**\n\n");
                    foreach (var r in folder)
                        sb.Append("- ").Append(Path.GetFileName(r.Path)).Append(" · ").Append(r.Kind).Append(" · ").Append(Size(r.Bytes))
                          .Append(r.Source.Length == 0 ? " · git only" : "").Append('\n');
                }
            }
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        private static void AppendSection(string projectRoot, string batch, string reason, string date, string commit, List<Entry> rows, string listPath)
        {
            var sb = new StringBuilder();
            sb.Append("\n## ").Append(date).Append(": ").Append(batch).Append("\n\n").Append(reason).Append("\n\n");
            sb.Append($"{rows.Count:N0} files, {Size(rows.Sum(r => r.Bytes))}. Last commit that still has them: `{commit}`. ");
            sb.Append("Every file, one per line: `").Append(listPath.Substring(AssetRestorePaths.DocsFolder.Length + 1)).Append("`.\n\n");
            sb.Append("| Pack | Files | Size | Get it back from |\n|---|---:|---:|---|\n");
            foreach (var pack in rows.GroupBy(r => AssetRestorePaths.PackOf(r.Path)).OrderByDescending(g => g.Sum(r => r.Bytes)))
            {
                var sources = pack.Select(r => SourceFile(r.Source)).Where(s => s.Length > 0).Distinct().ToList();
                int gitOnly = pack.Count(r => r.Source.Length == 0);
                string from = sources.Count > 0 ? string.Join(", ", sources) : "";
                if (gitOnly > 0) from += (from.Length > 0 ? "; " : "") + $"{gitOnly:N0} from git history only";
                sb.Append("| ").Append(pack.Key).Append(" | ").Append(pack.Count().ToString("N0")).Append(" | ").Append(Size(pack.Sum(r => r.Bytes)))
                  .Append(" | ").Append(from).Append(" |\n");
            }
            AppendToLog(projectRoot, sb.ToString());
        }

        private static void AppendToLog(string projectRoot, string section)
        {
            string path = Path.Combine(projectRoot, AssetRestorePaths.RemovalLog);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            if (!File.Exists(path)) File.WriteAllText(path, LogIntro, new UTF8Encoding(false));
            File.AppendAllText(path, section, new UTF8Encoding(false));
        }

        private const string LogIntro =
            "# Removal Log\n\n" +
            "Everything the project cleanup took out of the project, newest at the bottom: what went, why, and where to get it back. " +
            "The cleanup keeps whatever the build scenes or anything in `Assets/_Game` uses; it removes only vendor files nothing uses (see `README.md`).\n\n" +
            "**Bring one thing back:** Unity ▸ **Krytical Nova ▸ Project ▸ Restore Assets**. The *Removed* tab lists every file below; " +
            "search a name and press **Restore**. It comes back from the pack on this PC (the Synty library in Google Drive, `My Drive/KryticalNova/SyntyPacks`, or Unity's Asset Store cache) " +
            "with the materials, textures and meshes it needs, at its old path and with its old GUID, so anything that pointed at it finds it again. " +
            "Only that asset and what it needs are copied in, never the whole pack. Every batch also names the last commit that still has its files.\n";

        private static string SourceFile(string source)
        {
            if (string.IsNullOrEmpty(source)) return "";
            int paren = source.IndexOf(" (", StringComparison.Ordinal);
            return paren > 0 ? source.Substring(0, paren) : source;
        }

        public static string Size(long bytes) =>
            bytes >= 1L << 30 ? (bytes / (double)(1L << 30)).ToString("0.0", CultureInfo.InvariantCulture) + " GB"
            : bytes >= 1 << 20 ? (bytes / (double)(1 << 20)).ToString("0.0", CultureInfo.InvariantCulture) + " MB"
            : bytes >= 1 << 10 ? (bytes / 1024) + " KB"
            : bytes + " B";

        private static string Slug(string s) =>
            new string(s.Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-').Replace("--", "-");

        /// <summary>The commit HEAD points at, read from .git without running git.</summary>
        public static string HeadCommit(string projectRoot)
        {
            try
            {
                string git = Path.Combine(projectRoot, ".git");
                if (File.Exists(git))   // a worktree: "gitdir: <path>"
                {
                    string line = File.ReadAllText(git).Trim();
                    if (line.StartsWith("gitdir:", StringComparison.Ordinal)) git = line.Substring(7).Trim();
                }
                string head = File.ReadAllText(Path.Combine(git, "HEAD")).Trim();
                if (!head.StartsWith("ref:", StringComparison.Ordinal)) return head.Substring(0, Math.Min(12, head.Length));
                string reference = head.Substring(4).Trim();
                string refFile = Path.Combine(git, reference);
                if (File.Exists(refFile)) return File.ReadAllText(refFile).Trim().Substring(0, 12);
                string commonDir = File.Exists(Path.Combine(git, "commondir")) ? Path.Combine(git, File.ReadAllText(Path.Combine(git, "commondir")).Trim()) : git;
                refFile = Path.Combine(commonDir, reference);
                if (File.Exists(refFile)) return File.ReadAllText(refFile).Trim().Substring(0, 12);
                foreach (string line in File.ReadLines(Path.Combine(commonDir, "packed-refs")))
                    if (line.EndsWith(" " + reference, StringComparison.Ordinal)) return line.Substring(0, 12);
            }
            catch (Exception) { }
            return "unknown";
        }
    }
}
