using System;
using System.Collections.Generic;
using System.IO;

namespace KryticalNova.EditorTools.AssetRestore
{
    /// <summary>Where the restore tool reads packages from and writes its catalog, log and caches.</summary>
    public static class AssetRestorePaths
    {
        /// <summary>The owner's whole Synty library as .unitypackage files.</summary>
        public static readonly string SyntyLibrary =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "SyntyPolygonForKN").Replace('\\', '/');

        /// <summary>Unity's Asset Store download cache: every package bought and downloaded, by publisher.</summary>
        public static readonly string AssetStoreCache =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Unity", "Asset Store-5.x").Replace('\\', '/');

        /// <summary>The vendor roots the catalog and the cleanup cover.</summary>
        public static readonly string[] VendorRoots = { "Assets/Synty", "Assets/_ThirdParty" };

        public const string DocsFolder = "Assets/_Game/_Docs/Cleanup";
        /// <summary>Unity skips folders ending in "~", so the long lists cost the Editor nothing; git still keeps them.</summary>
        public const string ListsFolder = DocsFolder + "/Lists~";
        public const string RemovalLog = DocsFolder + "/Removal-Log.md";
        public const string RemovedTable = ListsFolder + "/Removed-Assets.tsv";
        public const string CatalogTable = ListsFolder + "/Asset-Catalog.tsv";
        public const string CatalogPage = ListsFolder + "/Asset-Catalog.html";

        public const string IndexCache = "Library/KryticalNova.AssetRestore/PackageIndex.bin";
        public const string PreviewCache = "Library/KryticalNova.AssetRestore/Previews";

        public static IEnumerable<string> PackageFolders()
        {
            yield return SyntyLibrary;
            yield return AssetStoreCache;
        }

        /// <summary>"Assets/Synty/PolygonKnights/Prefabs/x.prefab" → "PolygonKnights"; "Assets/_ThirdParty/Feel/..." → "Feel".</summary>
        public static string PackOf(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return "";
            string[] parts = assetPath.Replace('\\', '/').Split('/');
            if (parts.Length >= 3 && parts[0] == "Assets" && (parts[1] == "Synty" || parts[1] == "_ThirdParty")) return parts[2];
            return parts.Length >= 2 ? parts[1] : "";
        }

        /// <summary>Plain kind of an asset from its extension, for the catalog's type column.</summary>
        public static string KindOf(string assetPath)
        {
            switch (Path.GetExtension(assetPath).ToLowerInvariant())
            {
                case ".prefab": return "Prefab";
                case ".fbx": case ".obj": case ".blend": case ".dae": return "Model";
                case ".mat": return "Material";
                case ".png": case ".tga": case ".psd": case ".jpg": case ".jpeg": case ".exr": case ".tif": case ".tiff": case ".hdr": case ".bmp": return "Texture";
                case ".anim": return "Animation";
                case ".controller": case ".overridecontroller": return "Animator";
                case ".mask": return "Avatar mask";
                case ".wav": case ".ogg": case ".mp3": case ".aif": case ".aiff": case ".flac": return "Audio";
                case ".shader": case ".shadergraph": case ".shadersubgraph": case ".hlsl": case ".cginc": return "Shader";
                case ".unity": return "Scene";
                case ".cs": return "Script";
                case ".dll": return "Plugin";
                case ".asmdef": case ".asmref": return "Assembly";
                case ".ttf": case ".otf": return "Font";
                case ".asset": return "Asset";
                case ".vfx": return "VFX graph";
                case ".physicmaterial": case ".physicsmaterial": return "Physics material";
                case ".spriteatlas": case ".spriteatlasv2": return "Sprite atlas";
                case ".txt": case ".md": case ".pdf": case ".json": case ".xml": return "Document";
                default: return "Other";
            }
        }
    }
}
