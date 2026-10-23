using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using KryticalNova.Items;
using KryticalNova.Items.Visuals;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Bakes the <see cref="SidekickPartCatalog"/> from every Sidekick part FBX on disk. Existing parts keep their
    /// IDs (they are saved in characters and items), new parts are appended. Run again after importing a new pack.
    /// </summary>
    public static class SidekickPartCatalogBaker
    {
        public const string CatalogPath = SidekickGearGenerator.ItemsRoot + "/SidekickPartCatalog.asset";
        public const string MeshRoot = "Assets/Synty/SidekickCharacters/Resources/Meshes";
        /// <summary>
        /// Our copies of Synty parts (same names, with our extra body keys, e.g. the bare bodies with kn_bust).
        /// Scanned after <see cref="MeshRoot"/>, so a copy takes over its original's entry and keeps the id.
        /// </summary>
        public const string OwnPartsRoot = "Assets/_Game/Characters/Shared/SidekickParts/Resources/Meshes";
        private static readonly string[] PartRoots = { MeshRoot, OwnPartsRoot };
        private const string ResourcesMarker = "/Resources/";

        // SK_<PACK>_<VV>_<NN><CODE>_<SSnn>, e.g. SK_SCFI_SOLD_01_10TORS_HU01
        public static readonly Regex PartName =
            new Regex(@"^SK_(?<pack>[A-Z]+_[A-Z]+)_(?<vv>\d{2})_(?<nn>\d{2})(?<code>[A-Z]{4})_(?<sp>[A-Z]{2})\d{2}$");

        private sealed class NameRow
        {
            public string Name { get; set; }
        }

        [MenuItem("Krytical Nova/Characters/1. Bake Sidekick Part Catalog")]
        public static SidekickPartCatalog Bake()
        {
            WeatherVfxBuilder.EnsureFolder(SidekickGearGenerator.ItemsRoot);
            var catalog = AssetDatabase.LoadAssetAtPath<SidekickPartCatalog>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<SidekickPartCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }

            var wrapTorsos = ReadWrapTorsos();
            int before = catalog.Parts.Count;
            // Root by root, in order: a later root's part replaces the earlier one of the same name.
            var paths = PartRoots.Where(Directory.Exists).SelectMany(root =>
                Directory.GetFiles(root, "SK_*.fbx", SearchOption.AllDirectories)
                    .Select(p => p.Replace('\\', '/')).OrderBy(p => p, StringComparer.Ordinal));
            int seen = 0, replaced = 0;
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var path in paths)
            {
                string partName = Path.GetFileNameWithoutExtension(path);
                var m = PartName.Match(partName);
                if (!m.Success) continue; // SK_BaseModel and anything that is not a part
                int at = path.IndexOf(ResourcesMarker, StringComparison.Ordinal);
                if (at < 0) continue;
                string resourcePath = path.Substring(at + ResourcesMarker.Length);
                resourcePath = resourcePath.Substring(0, resourcePath.Length - ".fbx".Length);

                catalog.Upsert(new SidekickPartCatalog.Entry
                {
                    name = partName,
                    resourcePath = resourcePath,
                    region = (BodyRegion)int.Parse(m.Groups["nn"].Value),
                    pack = m.Groups["pack"].Value,
                    variant = byte.Parse(m.Groups["vv"].Value),
                    species = m.Groups["sp"].Value,
                    usesWrap = wrapTorsos.Contains(partName)
                });
                if (names.Add(partName)) seen++;
                else replaced++;
            }

            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            PartLibrary.Clear();
            Debug.Log($"[Characters] Part catalog: {seen} parts on disk ({replaced} by our own copies), " +
                      $"{catalog.Parts.Count - before} new, {catalog.Parts.Count} total. " +
                      "Next: Krytical Nova/Items/1. Generate Test Gear From Synty.");
            return catalog;
        }

        /// <summary>Bare-chested torsos that need the chest wrap on a feminine body (Sidekick's uses_wrap flag).</summary>
        private static HashSet<string> ReadWrapTorsos()
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            using (var db = SidekickDatabase.OpenReadOnly())
            {
                if (db == null) return names;
                foreach (var row in db.Query<NameRow>("select name as Name from sk_part where uses_wrap = 1"))
                    if (!string.IsNullOrEmpty(row.Name)) names.Add(row.Name);
            }
            return names;
        }
    }
}
