using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using KryticalNova.EditorTools.AssetRestore;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Brings in only what the zones use from the Synty packs the Zone Build Kits name but the project did not have
    /// (Design/Zone-Build-Kits.md): every prefab a place's kit (<see cref="ZoneKitBuilder.KitsPath"/>) or a map's
    /// layout (<see cref="ZoneMapBuilder.LayoutsFolder"/>) asks for, with the meshes, materials and textures it needs,
    /// one pack at a time, through the restore tool (<see cref="AssetRestoreService"/>): files keep their original
    /// GUIDs and nothing already in the project is touched, so the shared Synty folders stay as they are. Then the
    /// kits are built again and every map is laid out again so the new pieces stand in place.
    /// </summary>
    public sealed class ZonePackImporter : EditorWindow
    {
        /// <summary>The packs, as their files in the Synty library and the folder each installs to.</summary>
        public static readonly (string file, string folder)[] Packs =
        {
            ("POLYGON_Apocalypse_Wasteland", "PolygonApocalypseWasteland"),
            ("POLYGON_Fantasy_Village", "PolygonFantasyVillage"),
            ("POLYGON_Dungeon", "PolygonDungeon"),
            ("POLYGON_Dungeon_Realms", "PolygonDungeonRealms"),
            ("POLYGON_Mech_Unity_2022_3_v1_3_2", "PolygonMech"),
            ("POLYGON_Kaiju", "PolygonKaiju"),
        };

        public const string ReportPath = "Library/KryticalNova.ZonePackImport.txt";

        private sealed class PackNeed
        {
            public string File, Folder;
            public int Package = -1;
            public readonly List<string> Prefabs = new List<string>();   // asset paths in the package
            public readonly List<string> Guids = new List<string>();
            public AssetRestorer.Plan Plan;
            public string State = "";
        }

        private List<PackNeed> needs;
        private Vector2 scroll;
        private static readonly Queue<int> queue = new Queue<int>();

        [MenuItem("Krytical Nova/World/Zone Pack Importer")]
        public static void Open() => GetWindow<ZonePackImporter>("Zone Packs").Refresh();

        // ---------------------------------------------------------------- what the zones ask for

        /// <summary>Every prefab name the layouts place, as written ("Name" or the family "Name_*").</summary>
        private static HashSet<string> LayoutNames()
        {
            var names = new HashSet<string>();
            if (!Directory.Exists(ZoneMapBuilder.LayoutsFolder)) return names;
            foreach (string file in Directory.GetFiles(ZoneMapBuilder.LayoutsFolder, "*.json"))
            {
                var l = JsonUtility.FromJson<ZoneLayout>(File.ReadAllText(file));
                foreach (var p in l.objects) names.Add(p.prefab);
                foreach (var r in l.rings) names.UnionWith(r.prefabs);
                foreach (var r in l.lines) names.UnionWith(r.prefabs);
                foreach (var s in l.scatter) names.UnionWith(s.prefabs);
                foreach (var s in l.trees) names.UnionWith(s.prefabs);
            }
            names.Remove(null);
            return names;
        }

        /// <summary>Whether the project already has a prefab for a layout name, the way the map builder looks for one.</summary>
        private static bool InProject(string name)
        {
            bool family = name.EndsWith("_*");
            string stem = family ? name.Substring(0, name.Length - 2) : name;
            foreach (string guid in AssetDatabase.FindAssets(stem + " t:Prefab", new[] { "Assets/Synty", ZoneKitBuilder.ZonesRoot }))
            {
                string file = Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(guid));
                if (family ? file == stem || file.StartsWith(stem + "_", StringComparison.Ordinal) : file == stem) return true;
            }
            return false;
        }

        /// <summary>Every Synty path the kit list names.</summary>
        private static HashSet<string> KitPaths()
        {
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(ZoneKitBuilder.KitsPath)) return paths;
            foreach (Match m in Regex.Matches(File.ReadAllText(ZoneKitBuilder.KitsPath), "\"path\": \"([^\"]+)\""))
                paths.Add(m.Groups[1].Value);
            return paths;
        }

        /// <summary>Works out, for each pack, which of its prefabs the zones use and what bringing them in takes.</summary>
        private static List<PackNeed> Work()
        {
            var index = AssetRestoreService.Index;
            var result = Packs.Select(p => new PackNeed { File = p.file, Folder = p.folder }).ToList();
            if (index == null) { foreach (var n in result) n.State = "no package index"; return result; }

            var kit = KitPaths();
            // A layout name counts only when nothing in the project can be placed for it yet (a family such as
            // SM_Env_Rock_* already has rocks from other packs, and the map builder takes those first).
            var names = new HashSet<string>(LayoutNames().Where(n => !InProject(n)));
            var exact = new HashSet<string>(names.Where(n => !n.EndsWith("_*")));
            var families = names.Where(n => n.EndsWith("_*")).Select(n => n.Substring(0, n.Length - 2)).ToList();

            foreach (var need in result)
            {
                need.Package = index.Packages.FindIndex(p => p.Name.Equals(need.File, StringComparison.OrdinalIgnoreCase));
                if (need.Package < 0) { need.State = "not in the Synty library"; continue; }
                string root = $"Assets/Synty/{need.Folder}/";
                var prefabs = index.Items.Where(i => i.Package == need.Package && !i.IsFolder && i.AssetPath.StartsWith(root, StringComparison.Ordinal)
                                                     && i.AssetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)).ToList();
                foreach (var item in prefabs)
                {
                    string name = Path.GetFileNameWithoutExtension(item.AssetPath);
                    bool wanted = kit.Contains(item.AssetPath) || exact.Contains(name)
                                  || families.Any(f => name == f || Regex.IsMatch(name, "^" + Regex.Escape(f) + @"_\d+[A-Za-z]?(_.*)?$"));
                    if (!wanted) continue;
                    need.Prefabs.Add(item.AssetPath);
                    need.Guids.Add(item.Guid);
                }
                need.Plan = AssetRestorer.PlanFor(index, need.Guids, AssetRestoreService.InProject);
                need.State = need.Guids.Count == 0 ? "nothing in it is used"
                    : need.Plan.Files == 0 ? "in the project"
                    : $"{need.Plan.Files:N0} files to bring in ({RemovalLog.Size(need.Plan.Bytes)})";
            }
            return result;
        }

        private void Refresh() { needs = Work(); Repaint(); }

        // ---------------------------------------------------------------- importing

        /// <summary>Imports every pack that still has something to bring in, one after another. Writes progress to <see cref="ReportPath"/>.</summary>
        public static string ImportAll()
        {
            var list = Work();
            queue.Clear();
            for (int i = 0; i < list.Count; i++) if (list[i].Plan != null && list[i].Plan.Files > 0) queue.Enqueue(i);
            File.WriteAllText(ReportPath, $"started {DateTime.Now:HH:mm:ss}: {queue.Count} pack(s)\n");
            if (queue.Count == 0) { Append("done: nothing to bring in"); return "Nothing to bring in."; }
            int packs = queue.Count;
            Next();
            return $"Importing {packs} pack(s) one at a time; progress in {ReportPath}.";
        }

        private static void Next()
        {
            if (queue.Count == 0) { Append($"done {DateTime.Now:HH:mm:ss}"); Debug.Log("[World] Zone packs: every needed asset is in."); return; }
            int i = queue.Dequeue();
            var need = Work()[i];   // planned again: an earlier pack may already have brought a shared piece
            if (need.Plan == null || need.Plan.Files == 0) { Append($"{need.File}: nothing left to bring in"); Next(); return; }
            Append($"{need.File}: {need.Prefabs.Count} prefabs, {need.Plan.Files} files ...");
            AssetRestoreService.Restore(need.Guids, false, result =>
            {
                Append($"{need.File}: {result.Written.Count} written, {result.Skipped.Count} skipped");
                Next();
            });
        }

        private static void Append(string line) => File.AppendAllText(ReportPath, line + "\n");

        /// <summary>Lays out every World Atlas map again from its layout and renders its concept shots (the maps take the new pieces).</summary>
        public static string LayOutEveryMap()
        {
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty) return "Save the open scene first.";
            var scenes = Directory.GetFiles(ZoneMapBuilder.LayoutsFolder, "*.json").Select(Path.GetFileNameWithoutExtension)
                .Select(n => AssetDatabase.FindAssets(n + " t:Scene", new[] { "Assets/_Game/Scenes/World" })
                    .Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault(p => Path.GetFileNameWithoutExtension(p) == n))
                .Where(p => p != null).ToList();
            var log = new System.Text.StringBuilder();
            try
            {
                for (int i = 0; i < scenes.Count; i++)
                {
                    EditorUtility.DisplayProgressBar("Laying out the maps", scenes[i], i / (float)scenes.Count);
                    log.AppendLine(ZoneMapBuilder.Build(scenes[i], true).Split('\n')[0]);
                    ZoneShotCapture.Capture();
                }
            }
            finally { EditorUtility.ClearProgressBar(); }
            return log.ToString();
        }

        // ---------------------------------------------------------------- window

        private void OnGUI()
        {
            if (needs == null) Refresh();
            EditorGUILayout.HelpBox("Brings in only the prefabs the zone kits and map layouts use from these packs, with what they need. " +
                                    "Nothing already in the project is touched. Then build the kits and lay the maps out again.", MessageType.Info);
            if (AssetRestoreService.Index == null && GUILayout.Button("Build Package Index first")) AssetRestoreService.BuildIndex(Refresh);
            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (var n in needs)
            {
                EditorGUILayout.BeginHorizontal("box");
                EditorGUILayout.LabelField(n.File, GUILayout.Width(260));
                EditorGUILayout.LabelField($"{n.Prefabs.Count} prefabs used", GUILayout.Width(110));
                EditorGUILayout.LabelField(n.State);
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
            using (new EditorGUI.DisabledScope(AssetRestoreService.Busy))
            {
                if (GUILayout.Button("1. Bring in what the zones use (one pack at a time)")) Debug.Log("[World] " + ImportAll());
                if (GUILayout.Button("2. Build Zone Kits")) ZoneKitBuilder.Start();
                if (GUILayout.Button("3. Lay out every map again (takes a few minutes)")) Debug.Log("[World] " + LayOutEveryMap());
                if (GUILayout.Button("Look again")) Refresh();
            }
            if (AssetRestoreService.Busy) EditorGUILayout.LabelField(AssetRestoreService.BusyLabel + $" {AssetRestoreService.BusyProgress:P0}");
        }

        private void OnInspectorUpdate() { if (AssetRestoreService.Busy) Repaint(); }
    }
}
