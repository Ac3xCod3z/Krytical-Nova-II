using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        [MenuItem("Krytical Nova/World/Make Maps Addressable")]
        public static void MakeAll() => Debug.Log(Apply());

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
            bool lighting = StoreProbeLightingWithScenes();
            if (changed)
            {
                settings.SetDirty(AddressableAssetSettings.ModificationEvent.BatchModification, null, true, true);
                AssetDatabase.SaveAssets();
            }
            var report = new StringBuilder($"[Maps] {added + kept} map(s) Addressable ({added} added or moved, {kept} as they were), " +
                                           $"each in its {GroupPrefix}<scene> group, built with the game.");
            if (lighting) report.Append(" Probe lighting (APV) is now stored with the scenes, not in Streaming Assets.");
            return report.ToString();
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
            var rule = new UnityEditor.AddressableAssets.Build.AnalyzeRules.CheckBundleDupeDependencies();
            rule.FixIssues(settings);
            AddressableAssetGroup found = settings.FindGroup(IsolationGroup);
            AddressableAssetGroup common = settings.FindGroup(CommonGroup);
            int moved = 0;
            if (found != null)
            {
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
            return $"[Maps] {moved} more asset(s) shared by two or more maps moved into {CommonGroup} ({total} in all): no map bundle carries its own copy.";
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
            Object global = GraphicsSettings.GetSettingsForRenderPipeline<UnityEngine.Rendering.Universal.UniversalRenderPipeline>();
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
