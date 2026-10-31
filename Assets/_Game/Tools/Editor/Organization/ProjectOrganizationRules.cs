using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;

namespace KryticalNova.EditorTools.Organization
{
    /// <summary>
    /// Single source of truth for what may live where in Assets/. Human-readable version:
    /// Assets/_Game/_Docs/ProjectOrganization.md — keep the two in sync. The lists are copied into
    /// Tools/Editor/Architecture/Scripts~/organization_check.py for kn_check; change both.
    /// </summary>
    public static class ProjectOrganizationRules
    {
        public const string GameRoot = "Assets/_Game";
        public const string ThirdPartyRoot = "Assets/_ThirdParty";
        public const string PluginsRoot = "Assets/_Plugins";
        public const string ArtSourceRoot = "Assets/_ArtSource";

        /// <summary>The four project-owned top-level folders.</summary>
        public static readonly HashSet<string> OwnedRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "_Game", "_ThirdParty", "_Plugins", "_ArtSource",
        };

        /// <summary>Folders Unity (or a Unity package) gives special meaning at the Assets root. Never moved.</summary>
        public static readonly HashSet<string> UnityReservedRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Plugins", "Resources", "StreamingAssets", "Editor Default Resources", "Gizmos", "WebGLTemplates",
            "AddressableAssetsData", "Samples", "XR", "_Recovery", "TextMesh Pro",
        };

        /// <summary>
        /// Vendor folders that hard-code their own install path and break if moved. Never moved.
        /// Synty: SidekickCharacters reads "Assets/Synty/SidekickCharacters/Database/Side_Kick_Data.db".
        /// DownloadCache: Synty's ToolDownloader caches its .unitypackage files there.
        /// QuickDestruction: BakePipeline/QuickDestructionWindow/QDUI hard-code "Assets/QuickDestruction/...".
        /// FastScriptReload: cannot be moved (Unity refuses the move; the user confirmed it has to stay at the root).
        /// </summary>
        public static readonly HashSet<string> PinnedVendorRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Synty", "DownloadCache", "QuickDestruction", "FastScriptReload",
        };

        /// <summary>Root-level files that tools expect at a fixed path.</summary>
        public static readonly HashSet<string> PinnedRootFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "DefaultNetworkPrefabs.asset", // Netcode for GameObjects regenerates it at this path.
            "csc.rsp", "mcs.rsp", "link.xml",
        };

        /// <summary>
        /// Type-bucket names that signal first-party content dumped at the root (the old layout).
        /// These are flagged, never auto-quarantined — they belong inside _Game/&lt;System&gt;/.
        /// </summary>
        public static readonly HashSet<string> LegacyBucketNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Scripts", "Prefabs", "Materials", "Textures", "Models", "Meshes", "Shaders", "Scenes", "Animations",
            "Audio", "Art", "UI", "GameData", "Data", "Editor", "Tests", "Settings", "Fonts", "Sprites", "VFX",
            "ScriptableObjects",
        };

        /// <summary>Folder names under which C# scripts may live inside _Game.</summary>
        public static readonly HashSet<string> ScriptFolderNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Runtime", "Editor", "Data", "Tests", "Debug", "Profiling", "Importers",
        };

        public enum RootKind { Owned, UnityReserved, PinnedVendor, PinnedFile, LegacyBucket, Unsorted }

        public static RootKind Classify(string rootName, bool isFolder)
        {
            if (OwnedRoots.Contains(rootName)) return RootKind.Owned;
            if (UnityReservedRoots.Contains(rootName)) return RootKind.UnityReserved;
            if (PinnedVendorRoots.Contains(rootName)) return RootKind.PinnedVendor;
            if (!isFolder && PinnedRootFiles.Contains(rootName)) return RootKind.PinnedFile;
            if (isFolder && LegacyBucketNames.Contains(rootName)) return RootKind.LegacyBucket;
            return RootKind.Unsorted;
        }

        /// <summary>Current non-meta entries directly under Assets/, as asset paths.</summary>
        public static List<string> RootEntries()
        {
            return Directory.GetFileSystemEntries("Assets")
                .Where(p => !p.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                .Select(p => p.Replace('\\', '/'))
                .Where(p => !Path.GetFileName(p).StartsWith(".")) // hidden folders are ignored by Unity
                .ToList();
        }

        /// <summary>Root entries that break the rules and are safe to quarantine into _ThirdParty.</summary>
        public static List<string> QuarantineCandidates(IEnumerable<string> rootPaths)
        {
            return rootPaths
                .Where(AssetDatabase.IsValidFolder)
                .Where(p => Classify(Path.GetFileName(p), true) == RootKind.Unsorted)
                .ToList();
        }

        /// <summary>
        /// Moves an asset or folder to <paramref name="destination"/>, merging folder contents when the
        /// destination already exists (vendor updates re-import new files at the vendor's original path).
        /// GUIDs are preserved, so references survive. Returns conflicts that could not be moved.
        /// </summary>
        public static List<string> MoveMerging(string source, string destination)
        {
            var conflicts = new List<string>();
            MoveMerging(source, destination, conflicts);
            return conflicts;
        }

        private static void MoveMerging(string source, string destination, List<string> conflicts)
        {
            bool sourceIsFolder = AssetDatabase.IsValidFolder(source);
            if (sourceIsFolder && AssetDatabase.IsValidFolder(destination))
            {
                foreach (var child in Directory.GetFileSystemEntries(source))
                {
                    if (child.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
                    string name = Path.GetFileName(child);
                    MoveMerging($"{source}/{name}", $"{destination}/{name}", conflicts);
                }
                if (Directory.GetFileSystemEntries(source).Length == 0) AssetDatabase.DeleteAsset(source);
                return;
            }

            if (File.Exists(destination) || Directory.Exists(destination))
            {
                conflicts.Add($"{source} (already exists at {destination})");
                return;
            }

            EnsureFolder(Path.GetDirectoryName(destination)?.Replace('\\', '/'));
            string error = AssetDatabase.MoveAsset(source, destination);
            if (!string.IsNullOrEmpty(error)) conflicts.Add($"{source}: {error}");
        }

        public static void EnsureFolder(string path)
        {
            if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
