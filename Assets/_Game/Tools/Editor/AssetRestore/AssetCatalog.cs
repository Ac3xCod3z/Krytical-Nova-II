using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace KryticalNova.EditorTools.AssetRestore
{
    /// <summary>
    /// The Asset Catalog: one row for every vendor asset the project has had (Synty and _ThirdParty), with its
    /// pack, kind, size, GUID and the package it can be restored from. A snapshot is taken before anything is
    /// removed; later snapshots keep the rows of removed assets and mark them, so "where did that lamp go?" is a
    /// search away. Written as a tab-separated table (greppable, diffs well) plus a searchable HTML page.
    /// </summary>
    public static class AssetCatalog
    {
        public const string InProject = "In project";
        public const string Removed = "Removed";

        public sealed class Row
        {
            public string Guid, Path, Kind, Source, Status, RemovedOn;
            public long Bytes;
            public string Pack => AssetRestorePaths.PackOf(Path);
            public string Name => System.IO.Path.GetFileName(Path);
        }

        private static readonly Regex MetaGuid = new Regex(@"^guid:\s*([0-9a-f]{32})", RegexOptions.Multiline | RegexOptions.Compiled);
        private const string Header = "guid\tpath\tkind\tbytes\tsource\tstatus\tremoved_on";

        /// <summary>
        /// Walks the vendor roots on disk (no Unity API, so it may run off the main thread) and merges with the
        /// previous catalog so removed rows stay. <paramref name="removedOn"/> maps a GUID to the date it was removed.
        /// </summary>
        public static List<Row> Snapshot(string projectRoot, PackageIndex index, IReadOnlyList<Row> previous,
            IReadOnlyDictionary<string, string> removedOn, Action<float> progress = null)
        {
            var rows = new Dictionary<string, Row>();
            var files = new List<string>();
            foreach (string root in AssetRestorePaths.VendorRoots)
            {
                string full = Path.Combine(projectRoot, root);
                if (Directory.Exists(full)) files.AddRange(Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories));
            }

            for (int i = 0; i < files.Count; i++)
            {
                if ((i & 1023) == 0) progress?.Invoke((float)i / files.Count);
                string file = files[i];
                if (file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
                string meta = file + ".meta";
                if (!File.Exists(meta)) continue;
                string guid = GuidOfMeta(meta);
                if (guid == null) continue;
                string path = file.Substring(projectRoot.Length).TrimStart('/', '\\').Replace('\\', '/');
                rows[guid] = new Row
                {
                    Guid = guid, Path = path, Kind = AssetRestorePaths.KindOf(path), Bytes = new FileInfo(file).Length,
                    Source = SourceOf(index, guid), Status = InProject, RemovedOn = "",
                };
            }

            if (previous != null)
                foreach (var old in previous)
                {
                    if (rows.ContainsKey(old.Guid)) continue;
                    rows[old.Guid] = new Row
                    {
                        Guid = old.Guid, Path = old.Path, Kind = old.Kind, Bytes = old.Bytes,
                        Source = string.IsNullOrEmpty(old.Source) ? SourceOf(index, old.Guid) : old.Source,
                        Status = Removed,
                        RemovedOn = removedOn != null && removedOn.TryGetValue(old.Guid, out string d) ? d
                            : string.IsNullOrEmpty(old.RemovedOn) ? DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : old.RemovedOn,
                    };
                }
            progress?.Invoke(1f);
            return rows.Values.OrderBy(r => r.Path, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static string SourceOf(PackageIndex index, string guid)
        {
            if (index == null || !index.TryGet(guid, out var item)) return "";
            var package = index.PackageOf(item);
            return Path.GetFileName(package.Path) + " (" + package.Source + ")";
        }

        public static string GuidOfMeta(string metaPath)
        {
            using (var reader = new StreamReader(metaPath))
            {
                for (int i = 0; i < 4; i++)
                {
                    string line = reader.ReadLine();
                    if (line == null) break;
                    var m = MetaGuid.Match(line);
                    if (m.Success) return m.Groups[1].Value;
                }
            }
            return null;
        }

        // ---------------------------------------------------------------- files

        public static List<Row> Load(string tablePath)
        {
            var rows = new List<Row>();
            if (!File.Exists(tablePath)) return rows;
            foreach (string line in File.ReadLines(tablePath))
            {
                if (line.Length == 0 || line[0] == '#' || line.StartsWith("guid\t", StringComparison.Ordinal)) continue;
                string[] c = line.Split('\t');
                if (c.Length < 6) continue;
                rows.Add(new Row
                {
                    Guid = c[0], Path = c[1], Kind = c[2], Bytes = long.TryParse(c[3], out long b) ? b : 0,
                    Source = c[4], Status = c[5], RemovedOn = c.Length > 6 ? c[6] : "",
                });
            }
            return rows;
        }

        public static void Save(string tablePath, IReadOnlyList<Row> rows, string takenOn)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(tablePath));
            using (var w = new StreamWriter(tablePath, false, new UTF8Encoding(false)))
            {
                w.NewLine = "\n";
                w.WriteLine($"# Krytical Nova Asset Catalog, snapshot {takenOn}: every Synty and _ThirdParty asset the project has had. " +
                            "Restore one with Krytical Nova > Project > Restore Assets (Catalog tab).");
                w.WriteLine(Header);
                foreach (var r in rows)
                    w.WriteLine(string.Join("\t", r.Guid, r.Path, r.Kind, r.Bytes.ToString(CultureInfo.InvariantCulture), r.Source, r.Status, r.RemovedOn));
            }
        }

        /// <summary>A self-contained page to search the catalog outside Unity (names, pack, kind, status, where to get it back).</summary>
        public static void SavePage(string pagePath, IReadOnlyList<Row> rows, string takenOn)
        {
            var data = new StringBuilder(rows.Count * 120);
            foreach (var r in rows)
                data.Append(Clean(r.Path)).Append('\t').Append(r.Kind).Append('\t').Append(r.Bytes.ToString(CultureInfo.InvariantCulture)).Append('\t')
                    .Append(Clean(r.Source)).Append('\t').Append(r.Status).Append('\t').Append(r.RemovedOn).Append('\n');
            int removed = rows.Count(r => r.Status == Removed);
            string html = PageTemplate
                .Replace("{{TAKEN}}", takenOn)
                .Replace("{{COUNT}}", rows.Count.ToString("N0", CultureInfo.InvariantCulture))
                .Replace("{{REMOVED}}", removed.ToString("N0", CultureInfo.InvariantCulture))
                .Replace("{{DATA}}", data.ToString().Replace("</", "<\\/"));
            Directory.CreateDirectory(Path.GetDirectoryName(pagePath));
            File.WriteAllText(pagePath, html, new UTF8Encoding(false));
        }

        private static string Clean(string s) => (s ?? "").Replace('\t', ' ').Replace('\n', ' ').Replace('<', '‹');

        private const string PageTemplate = @"<!doctype html>
<html lang=""en""><head><meta charset=""utf-8""><meta name=""viewport"" content=""width=device-width,initial-scale=1"">
<title>Asset Catalog</title>
<style>
:root{--bg:#f6f4ef;--panel:#fff;--ink:#1d1d1f;--muted:#6b6b70;--line:#e2ded5;--accent:#2f6fde;--removed:#b4441f}
@media (prefers-color-scheme:dark){:root{--bg:#16171a;--panel:#1f2024;--ink:#ececee;--muted:#9a9aa2;--line:#2e3036;--accent:#7aa7ff;--removed:#ff8a5c}}
*{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--ink);font:14px/1.45 system-ui,Segoe UI,sans-serif}
header{padding:20px 16px 8px;max-width:1200px;margin:auto}h1{margin:0 0 4px;font-size:22px}p{margin:0;color:var(--muted)}
.bar{display:flex;flex-wrap:wrap;gap:8px;padding:12px 16px;max-width:1200px;margin:auto;position:sticky;top:0;background:var(--bg)}
input,select{font:inherit;padding:7px 10px;border:1px solid var(--line);border-radius:8px;background:var(--panel);color:var(--ink)}
input{flex:1 1 260px}main{max-width:1200px;margin:auto;padding:0 16px 40px}
table{width:100%;border-collapse:collapse;background:var(--panel);border:1px solid var(--line);border-radius:10px;overflow:hidden}
th,td{text-align:left;padding:6px 10px;border-bottom:1px solid var(--line);vertical-align:top}th{font-size:12px;color:var(--muted);font-weight:600}
td.path{word-break:break-all;color:var(--muted);font-size:12px}td.name{font-weight:600}.removed{color:var(--removed);font-weight:600}
.count{padding:8px 0;color:var(--muted)}@media (max-width:700px){.hide-sm{display:none}}
</style></head><body>
<header><h1>Asset Catalog</h1><p>Snapshot {{TAKEN}} · {{COUNT}} assets · {{REMOVED}} removed in cleanup. To bring one back: Unity ▸ Krytical Nova ▸ Project ▸ Restore Assets, Catalog tab, search the name, Restore.</p></header>
<div class=""bar""><input id=""q"" placeholder=""Search name or path (e.g. lamp, SM_Prop_Barrel)"" autofocus>
<select id=""pack""><option value="""">All packs</option></select><select id=""kind""><option value="""">All kinds</option></select>
<select id=""status""><option value="""">Any status</option><option>In project</option><option>Removed</option></select></div>
<main><div class=""count"" id=""count""></div><table><thead><tr><th>Name</th><th>Pack</th><th>Kind</th><th class=""hide-sm"">Size</th><th>Status</th><th class=""hide-sm"">Get it back from</th></tr></thead><tbody id=""rows""></tbody></table></main>
<script id=""data"" type=""text/plain"">{{DATA}}</script>
<script>
const rows=document.getElementById('data').textContent.split('\n').filter(Boolean).map(l=>{const c=l.split('\t');const p=c[0].split('/');
return{path:c[0],name:p[p.length-1],pack:(p[1]==='Synty'||p[1]==='_ThirdParty')?p[2]:p[1],kind:c[1],bytes:+c[2],source:c[3],status:c[4],removed:c[5]}});
const fill=(id,vals)=>{const s=document.getElementById(id);[...new Set(vals)].sort().forEach(v=>{const o=document.createElement('option');o.textContent=v;s.appendChild(o)})};
fill('pack',rows.map(r=>r.pack));fill('kind',rows.map(r=>r.kind));
const size=b=>b>1048576?(b/1048576).toFixed(1)+' MB':b>1024?Math.round(b/1024)+' KB':b+' B';
const esc=s=>s.replace(/&/g,'&amp;').replace(/</g,'&lt;');
function draw(){const q=document.getElementById('q').value.toLowerCase().trim(),pk=document.getElementById('pack').value,kd=document.getElementById('kind').value,st=document.getElementById('status').value;
const hit=rows.filter(r=>(!pk||r.pack===pk)&&(!kd||r.kind===kd)&&(!st||r.status===st)&&(!q||r.path.toLowerCase().includes(q)));
document.getElementById('count').textContent=hit.length.toLocaleString()+' match'+(hit.length===1?'':'es')+(hit.length>500?' (first 500 shown)':'');
document.getElementById('rows').innerHTML=hit.slice(0,500).map(r=>`<tr><td><div class=""name"">${esc(r.name)}</div><div class=""path"">${esc(r.path)}</div></td><td>${esc(r.pack)}</td><td>${r.kind}</td><td class=""hide-sm"">${size(r.bytes)}</td><td class=""${r.status==='Removed'?'removed':''}"">${r.status}${r.removed?' '+r.removed:''}</td><td class=""hide-sm"">${esc(r.source||'git history only')}</td></tr>`).join('')}
['q','pack','kind','status'].forEach(id=>document.getElementById(id).addEventListener('input',draw));draw();
</script></body></html>";
    }
}
