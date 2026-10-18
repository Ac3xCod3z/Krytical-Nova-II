using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KryticalNova.EditorTools.Skin;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.EditorTools.AssetRestore
{
    /// <summary>
    /// Krytical Nova ▸ Project ▸ Restore Assets: find any vendor asset the project has had (Catalog), everything the
    /// cleanup removed (Removed) or anything in the packages on this PC (Packages), and bring one back with exactly
    /// what it needs, never the whole pack.
    /// </summary>
    public sealed class AssetRestoreWindow : EditorWindow
    {
        private enum Tab { Catalog, Removed, Packages }

        private struct Row
        {
            public string Guid, Path, Kind, Source, Note;
            public long Bytes;
        }

        private const float RowHeight = 20f;
        private const int MaxShown = 20000;
        private static readonly string[] TabNames = { "Catalog", "Removed", "Packages" };

        private Tab tab;
        private string search = "";
        private int packChoice, kindChoice;
        private string[] packs = { "All packs" }, kinds = { "All kinds" };
        private List<Row> all = new List<Row>(), shown = new List<Row>();
        private readonly HashSet<string> ticked = new HashSet<string>();
        private string selected;
        private Vector2 scroll;
        private bool dirty = true;
        private AssetRestorer.Plan selectedPlan;
        private Texture2D preview;
        private string previewFor;

        [MenuItem("Krytical Nova/Project/Restore Assets", priority = 140)]
        public static void Open() => GetWindow<AssetRestoreWindow>("Restore Assets").minSize = new Vector2(720, 420);

        private void OnEnable() => dirty = true;

        private void OnInspectorUpdate()
        {
            if (AssetRestoreService.Busy || (previewFor != null && preview == null)) Repaint();
        }

        private void OnGUI()
        {
            KnSkinScope.Backdrop(new Rect(0f, 0f, position.width, position.height));
            using var comic = new KnSkinScope();

            DrawToolbar();
            if (dirty) Reload();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.BeginVertical(GUILayout.Width(position.width * 0.62f));
            DrawFilters();
            DrawList();
            EditorGUILayout.EndVertical();
            EditorGUILayout.BeginVertical();
            DrawDetail();
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
        }

        // ---------------------------------------------------------------- top bar

        private void DrawToolbar()
        {
            var index = AssetRestoreService.Index;
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            int newTab = GUILayout.Toolbar((int)tab, TabNames, EditorStyles.toolbarButton, GUILayout.Width(260));
            if (newTab != (int)tab) { tab = (Tab)newTab; dirty = true; }
            GUILayout.FlexibleSpace();
            using (new EditorGUI.DisabledScope(AssetRestoreService.Busy))
            {
                if (GUILayout.Button(index == null ? "Build package index" : "Refresh index", EditorStyles.toolbarButton))
                    AssetRestoreService.BuildIndex(() => { dirty = true; Repaint(); });
                if (GUILayout.Button("Take catalog snapshot", EditorStyles.toolbarButton))
                    AssetRestoreService.TakeSnapshot(() => { dirty = true; Repaint(); });
            }
            EditorGUILayout.EndHorizontal();

            if (AssetRestoreService.Busy)
            {
                EditorGUILayout.BeginHorizontal();
                Rect bar = GUILayoutUtility.GetRect(100, 18, GUILayout.ExpandWidth(true));
                EditorGUI.ProgressBar(bar, AssetRestoreService.BusyProgress, AssetRestoreService.BusyLabel);
                if (GUILayout.Button("Cancel", GUILayout.Width(70))) AssetRestoreService.Cancel();
                EditorGUILayout.EndHorizontal();
            }
            else
            {
                string status = index == null
                    ? "No package index yet: build it once so assets can be restored (reads every .unitypackage on this PC, a few minutes)."
                    : $"{index.Packages.Count} packages, {index.Count:N0} assets indexed.  {AssetRestoreService.LastMessage}";
                EditorGUILayout.LabelField(status, EditorStyles.miniLabel);
            }
        }

        private void DrawFilters()
        {
            EditorGUILayout.BeginHorizontal();
            string s = EditorGUILayout.TextField(search, EditorStyles.toolbarSearchField);
            int p = EditorGUILayout.Popup(packChoice, packs, GUILayout.Width(150));
            int k = EditorGUILayout.Popup(kindChoice, kinds, GUILayout.Width(100));
            if (s != search || p != packChoice || k != kindChoice) { search = s; packChoice = p; kindChoice = k; Filter(); }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"{shown.Count:N0} of {all.Count:N0}" + (shown.Count >= MaxShown ? " (narrow the search)" : ""), EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();
            using (new EditorGUI.DisabledScope(ticked.Count == 0 || AssetRestoreService.Busy))
                if (GUILayout.Button($"Restore ticked ({ticked.Count})", GUILayout.Width(150)))
                    AssetRestoreService.Restore(ticked.ToList(), true, _ => { ticked.Clear(); dirty = true; Repaint(); });
            EditorGUILayout.EndHorizontal();
        }

        // ---------------------------------------------------------------- list

        private void DrawList()
        {
            Rect area = GUILayoutUtility.GetRect(10, 10, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            Rect content = new Rect(0, 0, area.width - 16, shown.Count * RowHeight);
            scroll = GUI.BeginScrollView(area, scroll, content);
            int first = Mathf.Max(0, (int)(scroll.y / RowHeight));
            int last = Mathf.Min(shown.Count, first + (int)(area.height / RowHeight) + 2);
            for (int i = first; i < last; i++)
            {
                var row = shown[i];
                Rect r = new Rect(0, i * RowHeight, content.width, RowHeight);
                if (row.Guid == selected) EditorGUI.DrawRect(r, new Color(0.3f, 0.6f, 1f, 0.25f));
                else if ((i & 1) == 1) EditorGUI.DrawRect(r, new Color(0f, 0f, 0f, 0.08f));

                bool tick = ticked.Contains(row.Guid);
                bool newTick = GUI.Toggle(new Rect(r.x + 2, r.y + 2, 16, 16), tick, GUIContent.none);
                if (newTick != tick) { if (newTick) ticked.Add(row.Guid); else ticked.Remove(row.Guid); }

                float w = r.width - 20;
                GUI.Label(new Rect(r.x + 20, r.y, w * 0.42f, r.height), Path.GetFileName(row.Path));
                GUI.Label(new Rect(r.x + 20 + w * 0.42f, r.y, w * 0.22f, r.height), AssetRestorePaths.PackOf(row.Path), EditorStyles.miniLabel);
                GUI.Label(new Rect(r.x + 20 + w * 0.64f, r.y, w * 0.13f, r.height), row.Kind, EditorStyles.miniLabel);
                GUI.Label(new Rect(r.x + 20 + w * 0.77f, r.y, w * 0.23f, r.height), row.Note, EditorStyles.miniLabel);

                if (Event.current.type == EventType.MouseDown && r.Contains(Event.current.mousePosition) && Event.current.mousePosition.x > r.x + 20)
                {
                    Select(row.Guid);
                    Event.current.Use();
                }
            }
            GUI.EndScrollView();
        }

        private void Select(string guid)
        {
            selected = guid;
            selectedPlan = null;
            preview = null;
            previewFor = null;
            var index = AssetRestoreService.Index;
            if (index != null && !AssetRestoreService.InProject(guid))
                selectedPlan = AssetRestorer.PlanFor(index, new[] { guid }, AssetRestoreService.InProject, OldPaths());
            Repaint();
        }

        // ---------------------------------------------------------------- detail

        private void DrawDetail()
        {
            if (selected == null) { EditorGUILayout.HelpBox("Search for an asset by name, pick it, and restore it. Tick several to restore them together.", MessageType.None); return; }
            var row = all.FirstOrDefault(r => r.Guid == selected);
            bool inProject = AssetRestoreService.InProject(selected);

            EditorGUILayout.LabelField(Path.GetFileName(row.Path), EditorStyles.boldLabel);
            EditorGUILayout.SelectableLabel(row.Path, EditorStyles.wordWrappedMiniLabel, GUILayout.Height(30));
            EditorGUILayout.LabelField("Kind", row.Kind);
            EditorGUILayout.LabelField("Size", RemovalLog.Size(row.Bytes));
            EditorGUILayout.LabelField("GUID", selected);
            EditorGUILayout.LabelField("Status", inProject ? "In the project" : row.Note);
            EditorGUILayout.LabelField("From", string.IsNullOrEmpty(row.Source) ? "in no package on this PC (git history only)" : row.Source, EditorStyles.wordWrappedLabel);

            if (previewFor != selected && AssetRestoreService.Index != null && !inProject)
            {
                if (GUILayout.Button("Show the package's preview")) { previewFor = selected; preview = AssetRestoreService.Preview(selected); }
            }
            else if (previewFor == selected)
            {
                if (preview == null && !AssetRestoreService.Busy) preview = AssetRestoreService.Preview(selected);
                if (preview != null) GUILayout.Label(preview, GUILayout.Width(128), GUILayout.Height(128));
                else EditorGUILayout.LabelField(AssetRestoreService.Busy ? "Reading the package…" : "The package has no preview for it.", EditorStyles.miniLabel);
            }

            GUILayout.Space(6);
            if (inProject)
            {
                if (GUILayout.Button("Show in the project")) EditorGUIUtility.PingObject(AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(selected)));
                return;
            }
            if (selectedPlan == null) { EditorGUILayout.HelpBox("Build the package index to restore it.", MessageType.Info); return; }
            if (selectedPlan.Items.Count == 0) { EditorGUILayout.HelpBox("No package on this PC has it. It can still come back from git (the Removal Log names the commit).", MessageType.Warning); return; }

            EditorGUILayout.HelpBox($"Restoring copies in {selectedPlan.Files:N0} file(s), {RemovalLog.Size(selectedPlan.Bytes)}: this asset and what it needs that the project lacks. " +
                                    "Files already in the project are left as they are.", MessageType.None);
            using (new EditorGUI.DisabledScope(AssetRestoreService.Busy))
                if (GUILayout.Button("Restore", GUILayout.Height(28)))
                    AssetRestoreService.Restore(new[] { selected }, true, _ => { dirty = true; Select(selected); });
        }

        // ---------------------------------------------------------------- data

        private void Reload()
        {
            dirty = false;
            all = new List<Row>();
            string root = AssetRestoreService.ProjectRoot;
            var index = AssetRestoreService.Index;
            switch (tab)
            {
                case Tab.Catalog:
                    foreach (var r in AssetCatalog.Load(Path.Combine(root, AssetRestorePaths.CatalogTable)))
                        all.Add(new Row { Guid = r.Guid, Path = r.Path, Kind = r.Kind, Bytes = r.Bytes, Source = r.Source,
                                          Note = r.Status == AssetCatalog.Removed ? "Removed " + r.RemovedOn : "In project" });
                    break;
                case Tab.Removed:
                    foreach (var e in RemovalLog.LoadTable(root))
                        all.Add(new Row { Guid = e.Guid, Path = e.Path, Kind = e.Kind, Bytes = e.Bytes, Source = e.Source, Note = "Removed " + e.Date });
                    break;
                case Tab.Packages:
                    if (index == null) break;
                    var seen = new HashSet<string>();
                    foreach (var item in index.Items)
                    {
                        if (item.IsFolder || !seen.Add(item.Guid)) continue;
                        var package = index.PackageOf(item);
                        all.Add(new Row { Guid = item.Guid, Path = item.AssetPath, Kind = AssetRestorePaths.KindOf(item.AssetPath), Bytes = item.Size,
                                          Source = Path.GetFileName(package.Path) + " (" + package.Source + ")", Note = package.Name });
                    }
                    break;
            }
            packs = new[] { "All packs" }.Concat(all.Select(r => AssetRestorePaths.PackOf(r.Path)).Distinct().OrderBy(x => x)).ToArray();
            kinds = new[] { "All kinds" }.Concat(all.Select(r => r.Kind).Distinct().OrderBy(x => x)).ToArray();
            packChoice = Mathf.Clamp(packChoice, 0, packs.Length - 1);
            kindChoice = Mathf.Clamp(kindChoice, 0, kinds.Length - 1);
            Filter();
        }

        private void Filter()
        {
            string q = search.Trim();
            string pack = packChoice > 0 ? packs[packChoice] : null;
            string kind = kindChoice > 0 ? kinds[kindChoice] : null;
            shown = all.Where(r => (pack == null || AssetRestorePaths.PackOf(r.Path) == pack)
                                   && (kind == null || r.Kind == kind)
                                   && (q.Length == 0 || r.Path.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0))
                       .Take(MaxShown).ToList();
            scroll = Vector2.zero;
        }

        private static Dictionary<string, string> OldPaths()
        {
            var map = new Dictionary<string, string>();
            foreach (var e in RemovalLog.LoadTable(AssetRestoreService.ProjectRoot)) map[e.Guid] = e.Path;
            return map;
        }
    }
}
