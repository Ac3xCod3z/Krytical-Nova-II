using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using UnityEngine.Rendering;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Every map is an Addressable (owner, 20 Oct 2026: "make sure with the newer maps they use addressables"): each map
    /// scene in a group of its own, <c>World_&lt;scene&gt;</c>, addressed by its scene name, packed into the game (local
    /// bundles, no download server) and built with the game. The game loads maps through <c>MapScenes</c> and ships no
    /// map in its build list; a dedicated server keeps them in its own (<see cref="BuildSceneOrder.ShippedForServer"/>).
    /// Runs by itself whenever the build's scene list is set (<see cref="BuildSceneOrder.Apply"/>), so a new map is never
    /// left out. Probe lighting (APV) is stored with the scenes rather than in Streaming Assets, as Addressable scenes need.
    /// </summary>
    public static class MapAddressables
    {
        /// <summary>The Addressables settings and groups: project-level config for a package (ProjectOrganization section 1).</summary>
        public const string SettingsFolder = "Assets/_Game/Settings/Addressables";
        public const string GroupPrefix = "World_";
        /// <summary>What two or more maps share, in one bundle (ProjectOrganization section 10's planned World_Common).</summary>
        public const string CommonGroup = "World_Common";
        private const string IsolationGroup = "Duplicate Asset Isolation"; // the name the package's own fix gives its group
        /// <summary>
        /// What <see cref="IsolateShared"/> last saw and moved, per checkout (Library is never committed): line 1 the
        /// fingerprint of the maps and the groups before it ran, line 2 after it ran, then the GUIDs it moved into
        /// <see cref="CommonGroup"/>.
        /// </summary>
        public const string IsolationStampPath = "Library/KryticalNova-MapIsolation.stamp";

        [MenuItem("Krytical Nova/World/Make Maps Addressable")]
        public static void MakeAll() => Debug.Log(Apply());

        /// <summary>The full search, whatever the stamp says (a build skips it while nothing it reads has changed).</summary>
        [MenuItem("Krytical Nova/World/Isolate Shared Map Assets (Full Search)")]
        public static void IsolateSharedMenu() => Debug.Log(IsolateShared());

        /// <summary>Every map scene that exists: Concord (Zone01) and the World Atlas maps.</summary>
        public static List<string> MapScenePaths()
        {
            var paths = new List<string> { BuildSceneOrder.WorldScenePath };
            paths.AddRange(WorldScenesBuilder.ScenePaths());
            return paths.Where(File.Exists).Distinct().ToList();
        }

        /// <summary>Puts every map in its group (made when missing) and sets the build to make the bundles. Returns a report.</summary>
        public static string Apply()
        {
            AddressableAssetSettings settings = EnsureSettings();
            bool changed = false;
            if (settings.BuildAddressablesWithPlayerBuild != AddressableAssetSettings.PlayerBuildOption.BuildWithPlayer)
            {
                settings.BuildAddressablesWithPlayerBuild = AddressableAssetSettings.PlayerBuildOption.BuildWithPlayer;
                changed = true;
            }
            int added = 0, kept = 0;
            foreach (string path in MapScenePaths())
            {
                string guid = AssetDatabase.AssetPathToGUID(path);
                string scene = Path.GetFileNameWithoutExtension(path);
                string groupName = GroupPrefix + scene;
                AddressableAssetGroup group = settings.FindGroup(groupName)
                    ?? settings.CreateGroup(groupName, false, false, false, settings.DefaultGroup.Schemas);
                AddressableAssetEntry entry = settings.FindAssetEntry(guid);
                if (entry != null && entry.parentGroup == group && entry.address == scene) { kept++; continue; }
                entry = settings.CreateOrMoveEntry(guid, group, false, false);
                entry.address = scene;
                added++;
                changed = true;
            }
            string zoneArt = ApplyZoneArt(settings, ref changed);
            bool lighting = StoreProbeLightingWithScenes();
            if (changed)
            {
                settings.SetDirty(AddressableAssetSettings.ModificationEvent.BatchModification, null, true, true);
                AssetDatabase.SaveAssets();
            }
            var report = new StringBuilder($"[Maps] {added + kept} map(s) Addressable ({added} added or moved, {kept} as they were), " +
                                           $"each in its {GroupPrefix}<scene> group, built with the game.");
            report.Append(' ').Append(zoneArt);
            if (lighting) report.Append(" Probe lighting (APV) is now stored with the scenes, not in Streaming Assets.");
            return report.ToString();
        }

        /// <summary>
        /// Art placed inside a map scene that gets a bundle of its own (owner's pick B, 13 Nov 2026): Concord 040, the sky city
        /// in Zone01's scene. Its group holds the assets of the zone's folder that the map scene uses (the slim FBX meshes,
        /// materials, textures, thrusters, NavMesh), never the prefab: the scene carries the prefab's objects itself, so they
        /// would be packed twice. The zone still loads and frees with its map; a change to the map no longer re-ships its art.
        /// </summary>
        public static readonly (string group, string folder, string mapScene)[] ZoneArt =
        {
            (GroupPrefix + "Concord040", "Assets/_Game/World/Zones/Concord040", BuildSceneOrder.WorldScenePath),
        };

        // Sets each zone-art group to exactly what its map scene uses from the zone's folder. A checkout without the folder
        // (it stays out of git) leaves the group as it is and says so.
        private static string ApplyZoneArt(AddressableAssetSettings settings, ref bool changed)
        {
            var report = new StringBuilder();
            foreach (var (groupName, folder, mapScene) in ZoneArt)
            {
                if (!AssetDatabase.IsValidFolder(folder) || !File.Exists(mapScene))
                {
                    report.Append($"{groupName}: {folder} is not in this checkout, group left as it is. ");
                    Debug.LogWarning($"[Maps] {folder} is missing in this checkout: {groupName} is left as it is, and the map ships without that zone's art.");
                    continue;
                }
                string prefix = folder + "/";
                var wanted = new HashSet<string>(AssetDatabase.GetDependencies(mapScene, true)
                    .Where(p => p.StartsWith(prefix, StringComparison.Ordinal)
                                && !p.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)
                                && !p.EndsWith(".unity", StringComparison.OrdinalIgnoreCase)
                                && !p.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                    .Select(AssetDatabase.AssetPathToGUID));
                AddressableAssetGroup group = settings.FindGroup(groupName)
                    ?? settings.CreateGroup(groupName, false, false, false, settings.DefaultGroup.Schemas);
                int added = 0, removed = 0;
                foreach (AddressableAssetEntry entry in new List<AddressableAssetEntry>(group.entries))
                    if (!wanted.Contains(entry.guid)) { settings.RemoveAssetEntry(entry.guid, false); removed++; }
                foreach (string guid in wanted)
                {
                    AddressableAssetEntry entry = settings.FindAssetEntry(guid);
                    if (entry != null && entry.parentGroup == group) continue;
                    settings.CreateOrMoveEntry(guid, group, false, false);
                    added++;
                }
                if (added + removed > 0) changed = true;
                report.Append($"{groupName}: {wanted.Count} asset(s) of {folder} that {Path.GetFileNameWithoutExtension(mapScene)} uses ({added} added, {removed} removed). ");
            }
            return report.ToString().TrimEnd();
        }

        /// <summary>
        /// Before a game build: every asset that two or more maps use (the player, the network prefabs, characters, shaders)
        /// is moved into <see cref="CommonGroup"/>, one bundle, so no map's bundle carries a copy of its own. Without it the
        /// first build was 16 GB, each of the 29 maps about 550 MB, blank ones too. The package's own rule finds them
        /// (Analyze, "Check Duplicate Bundle Dependencies"); what an earlier build moved stays, so the group only grows.
        /// </summary>
        public static string IsolateShared()
        {
            AddressableAssetSettings settings = EnsureSettings();
            return IsolateShared(settings, Fingerprint(settings));
        }

        // The search itself, given the fingerprint of what it is about to read (worked out once by the caller).
        private static string IsolateShared(AddressableAssetSettings settings, string fingerprint)
        {
            var rule = new UnityEditor.AddressableAssets.Build.AnalyzeRules.CheckBundleDupeDependencies();
            rule.FixIssues(settings);
            var movedGuids = new List<string>();
            AddressableAssetGroup found = settings.FindGroup(IsolationGroup);
            AddressableAssetGroup common = settings.FindGroup(CommonGroup);
            int moved = 0;
            if (found != null)
            {
                movedGuids.AddRange(found.entries.Select(e => e.guid));
                if (common == null)
                {
                    found.Name = CommonGroup;
                    moved = found.entries.Count;
                    common = found;
                }
                else
                {
                    foreach (AddressableAssetEntry entry in new List<AddressableAssetEntry>(found.entries))
                    {
                        settings.MoveEntry(entry, common, false, false);
                        moved++;
                    }
                    settings.RemoveGroup(found);
                }
                settings.SetDirty(AddressableAssetSettings.ModificationEvent.BatchModification, null, true, true);
                AssetDatabase.SaveAssets();
            }
            int total = common != null ? common.entries.Count : 0;
            // Nothing moved: the groups are as they were, so the fingerprint after is the one before (no second pass).
            WriteIsolationStamp(fingerprint, moved == 0 ? fingerprint : Fingerprint(settings), movedGuids);
            return $"[Maps] {moved} more asset(s) shared by two or more maps moved into {CommonGroup} ({total} in all): no map bundle carries its own copy.";
        }

        /// <summary>
        /// Before a game build (31 Oct 2026, owner's pick): <see cref="IsolateShared"/>'s full search took about 6 minutes of
        /// every publish and found nothing new when the maps had not changed, so it runs only when the maps, what they use or
        /// the Addressables groups differ from what the last search saw (<see cref="IsolationStampPath"/>). Two states count
        /// as seen: the groups as the search found them, which is what a build checkout has again after it is reset to the
        /// commit before each publish (the reset undoes moves that were never committed, so the assets that search moved are
        /// moved again here), and the groups as the search left them, which is what the project you work in keeps, so a test
        /// build there does not search again either. A wrong skip only leaves copies in map bundles (a bigger download);
        /// nothing breaks. The menu Isolate Shared Map Assets (Full Search) always searches.
        /// </summary>
        public static string IsolateSharedWhenChanged()
        {
            AddressableAssetSettings settings = EnsureSettings();
            string[] stamp = File.Exists(IsolationStampPath) ? File.ReadAllLines(IsolationStampPath) : Array.Empty<string>();
            string fingerprint = Fingerprint(settings);
            if (stamp.Length < 2 || (stamp[0] != fingerprint && stamp[1] != fingerprint)) return IsolateShared(settings, fingerprint);

            AddressableAssetGroup common = settings.FindGroup(CommonGroup);
            if (common == null) return IsolateShared(settings, fingerprint);
            int again = 0;
            foreach (string guid in stamp.Skip(2).Where(g => g.Length > 0))
            {
                if (string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(guid))) continue;
                AddressableAssetEntry entry = settings.FindAssetEntry(guid);
                if (entry != null && entry.parentGroup == common) continue;
                settings.CreateOrMoveEntry(guid, common, false, false);
                again++;
            }
            if (again > 0)
            {
                settings.SetDirty(AddressableAssetSettings.ModificationEvent.BatchModification, null, true, true);
                AssetDatabase.SaveAssets();
            }
            return $"[Maps] The maps and their groups are as the last search saw them: search skipped ({again} asset(s) it had moved moved into " +
                   $"{CommonGroup} again, {common.entries.Count} in all). Full search: Krytical Nova ▸ World ▸ Isolate Shared Map Assets (Full Search).";
        }

        // What the search reads: each map scene (its import hash), every asset the maps use (by path, so a newly shared one
        // counts), the Addressables settings and every group file. A change to any of them makes the next build search again.
        private static string Fingerprint(AddressableAssetSettings settings)
        {
            List<string> scenes = MapScenePaths();
            var text = new StringBuilder();
            foreach (string scene in scenes) text.Append(scene).Append('=').Append(AssetDatabase.GetAssetDependencyHash(scene)).Append('\n');
            foreach (string used in AssetDatabase.GetDependencies(scenes.ToArray(), true).OrderBy(p => p, StringComparer.Ordinal)) text.Append(used).Append('\n');
            var files = new List<string> { AssetDatabase.GetAssetPath(settings) };
            files.AddRange(settings.groups.Where(g => g != null).Select(g => AssetDatabase.GetAssetPath(g)));
            using (var sha = SHA256.Create())
            {
                foreach (string file in files.Where(File.Exists).OrderBy(p => p, StringComparer.Ordinal))
                    text.Append(file).Append('=').Append(Convert.ToBase64String(sha.ComputeHash(File.ReadAllBytes(file)))).Append('\n');
                return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(text.ToString())));
            }
        }

        // Before: a build checkout is reset to the same commit next time, so it matches again, and the moved GUIDs are put
        // back by IsolateSharedWhenChanged. After: the project you work in keeps the moves, so it matches there.
        private static void WriteIsolationStamp(string before, string after, List<string> movedGuids)
        {
            var guids = new List<string>();
            if (File.Exists(IsolationStampPath))
            {
                string[] was = File.ReadAllLines(IsolationStampPath);
                // Moves the last search made that this one did not need (they are in the groups now) are kept, in case the
                // checkout is reset to a commit without them.
                guids.AddRange(was.Skip(2).Where(g => g.Length > 0 && !movedGuids.Contains(g)));
            }
            guids.AddRange(movedGuids);
            Directory.CreateDirectory(Path.GetDirectoryName(IsolationStampPath));
            File.WriteAllLines(IsolationStampPath, new[] { before, after }.Concat(guids.Distinct()));
        }

        private static AddressableAssetSettings EnsureSettings()
        {
            if (AddressableAssetSettingsDefaultObject.SettingsExists) return AddressableAssetSettingsDefaultObject.Settings;
            Directory.CreateDirectory(SettingsFolder);
            // The package keeps a pointer to its settings in Assets/AddressableAssetsData unless one is registered already:
            // it is made here, beside the settings, so nothing lands at the Assets root.
            if (!EditorBuildSettings.TryGetConfigObject(AddressableAssetSettingsDefaultObject.kDefaultConfigObjectName, out AddressableAssetSettingsDefaultObject pointer))
            {
                pointer = ScriptableObject.CreateInstance<AddressableAssetSettingsDefaultObject>();
                AssetDatabase.CreateAsset(pointer, SettingsFolder + "/DefaultObject.asset");
                AssetDatabase.SaveAssets();
                EditorBuildSettings.AddConfigObject(AddressableAssetSettingsDefaultObject.kDefaultConfigObjectName, pointer, true);
            }
            AddressableAssetSettings settings = AssetDatabase.LoadAssetAtPath<AddressableAssetSettings>(SettingsFolder + "/AddressableAssetSettings.asset")
                ?? AddressableAssetSettings.Create(SettingsFolder, "AddressableAssetSettings", true, true);
            AddressableAssetSettingsDefaultObject.Settings = settings;
            return settings;
        }

        // An Addressable scene carries its APV data only when the data is kept as assets (the render pipeline's documentation,
        // "Load APV data from AssetBundles or Addressables"). True when it was switched now.
        private static bool StoreProbeLightingWithScenes()
        {
            // The setting's own class is internal to the render pipeline: it is set in the URP global settings asset.
            UnityEngine.Object global = GraphicsSettings.GetSettingsForRenderPipeline<UnityEngine.Rendering.Universal.UniversalRenderPipeline>();
            if (global == null) return false;
            var serialized = new SerializedObject(global);
            SerializedProperty property = serialized.GetIterator();
            while (property.Next(true))
            {
                if (property.name != "m_ProbeVolumeDisableStreamingAssets" || property.propertyType != SerializedPropertyType.Boolean) continue;
                if (property.boolValue) return false;
                property.boolValue = true;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.SaveAssets();
                return true;
            }
            Debug.LogWarning("[Maps] The APV setting \"Probe Volume Disable Streaming Assets\" was not found: set it by hand (Project Settings ▸ Graphics ▸ URP).");
            return false;
        }
    }
}
