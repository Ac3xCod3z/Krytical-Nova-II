using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.EditorTools.Organization
{
    /// <summary>
    /// Keeps the Assets root clean when a .unitypackage / Asset Store package is imported.
    /// It snapshots the root when an import starts; any new root folder the package created is moved
    /// into Assets/_ThirdParty/&lt;Name&gt; once the import finishes. If that vendor already lives in
    /// _ThirdParty (an update re-importing new files at its original path), the contents are merged in.
    /// GUIDs are preserved, so nothing loses its references.
    ///
    /// Unity-reserved folders (Plugins, Resources, ...) and pinned vendors (Synty) are never touched.
    /// Toggle: Krytical Nova/Project/Auto-Quarantine Imported Packages.
    /// </summary>
    [InitializeOnLoad]
    public static class ThirdPartyImportEnforcer
    {
        private const string MenuPath = "Krytical Nova/Project/Auto-Quarantine Imported Packages";
        private const string EnabledPref = "KryticalNova.Organization.AutoQuarantine";
        private const string SnapshotKey = "KryticalNova.Organization.RootSnapshot";

        public static bool Enabled
        {
            get => EditorPrefs.GetBool(EnabledPref, true);
            set => EditorPrefs.SetBool(EnabledPref, value);
        }

        static ThirdPartyImportEnforcer()
        {
            AssetDatabase.importPackageStarted += OnImportStarted;
            AssetDatabase.importPackageCompleted += OnImportCompleted;
            AssetDatabase.importPackageCancelled += OnImportAborted;
            AssetDatabase.importPackageFailed += (name, error) => OnImportAborted(name);
        }

        // Package imports can trigger a domain reload between "started" and "completed" (when the package
        // contains scripts), so the snapshot is kept in SessionState rather than a static field.
        private static void OnImportStarted(string packageName)
        {
            SessionState.SetString(SnapshotKey, string.Join("\n", ProjectOrganizationRules.RootEntries()));
        }

        private static void OnImportAborted(string packageName) => SessionState.EraseString(SnapshotKey);

        private static void OnImportCompleted(string packageName)
        {
            string snapshot = SessionState.GetString(SnapshotKey, null);
            SessionState.EraseString(SnapshotKey);
            if (!Enabled || snapshot == null) return;

            var before = new HashSet<string>(snapshot.Split('\n'));
            var created = ProjectOrganizationRules.RootEntries().Where(p => !before.Contains(p)).ToList();
            var toMove = ProjectOrganizationRules.QuarantineCandidates(created);

            foreach (var path in created.Except(toMove)) WarnAboutRootEntry(path, packageName);
            if (toMove.Count == 0) return;

            // Moving inside the import callback can race the importer; wait one editor tick.
            EditorApplication.delayCall += () => Quarantine(toMove, packageName);
        }

        public static void Quarantine(IList<string> rootFolders, string reason)
        {
            var conflicts = new List<string>();
            foreach (var folder in rootFolders)
            {
                if (!AssetDatabase.IsValidFolder(folder)) continue;
                string destination = $"{ProjectOrganizationRules.ThirdPartyRoot}/{Path.GetFileName(folder)}";
                conflicts.AddRange(ProjectOrganizationRules.MoveMerging(folder, destination));
                Debug.Log($"[Organization] '{reason}': moved {folder} → {destination}",
                    AssetDatabase.LoadAssetAtPath<Object>(destination));
            }
            AssetDatabase.SaveAssets();

            foreach (var conflict in conflicts)
                Debug.LogWarning($"[Organization] Left in place (name clash, resolve by hand): {conflict}");
        }

        private static void WarnAboutRootEntry(string path, string packageName)
        {
            string name = Path.GetFileName(path);
            switch (ProjectOrganizationRules.Classify(name, AssetDatabase.IsValidFolder(path)))
            {
                case ProjectOrganizationRules.RootKind.LegacyBucket:
                    Debug.LogWarning($"[Organization] '{packageName}' created {path}. Generic buckets aren't allowed at the root; " +
                                     $"move it under {ProjectOrganizationRules.ThirdPartyRoot}/<Vendor>/ (or _Game/<System>/ if it's ours).");
                    break;
                case ProjectOrganizationRules.RootKind.Unsorted: // a loose file
                    Debug.LogWarning($"[Organization] '{packageName}' dropped a loose file at {path}. " +
                                     $"Move it next to its vendor in {ProjectOrganizationRules.ThirdPartyRoot}/ unless a tool needs it at the root.");
                    break;
            }
        }

        [MenuItem(MenuPath, priority = 100)]
        private static void Toggle() => Enabled = !Enabled;

        [MenuItem(MenuPath, true)]
        private static bool ToggleValidate()
        {
            Menu.SetChecked(MenuPath, Enabled);
            return true;
        }

        /// <summary>For folders dropped in by hand (Explorer / Finder) rather than a package import.</summary>
        [MenuItem("Krytical Nova/Project/Quarantine Root Folders Into _ThirdParty...", priority = 101)]
        private static void QuarantineNow()
        {
            var candidates = ProjectOrganizationRules.QuarantineCandidates(ProjectOrganizationRules.RootEntries());
            if (candidates.Count == 0)
            {
                EditorUtility.DisplayDialog("Project Organization", "Nothing to move: the Assets root is clean.", "OK");
                return;
            }

            string list = string.Join("\n", candidates.Select(c => "  • " + c));
            if (EditorUtility.DisplayDialog("Project Organization",
                    $"Move these root folders into {ProjectOrganizationRules.ThirdPartyRoot}/?\n\n{list}\n\n" +
                    "Only do this for vendor content. Our own content belongs in _Game/<System>/.",
                    "Move", "Cancel"))
                Quarantine(candidates, "manual");
        }
    }
}
