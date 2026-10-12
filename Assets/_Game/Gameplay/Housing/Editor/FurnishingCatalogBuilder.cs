using System.Collections.Generic;
using System.IO;
using System.Linq;
using KryticalNova.Factions;
using KryticalNova.Housing;
using KryticalNova.Items;
using KryticalNova.World;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.EditorTools.Housing
{
    /// <summary>
    /// Makes the furnishings: for every row of the tables (<c>FurnishingCatalogBuilder.Tables.cs</c>), an item (so
    /// the piece is carried, stored and traded like any other), a furnishing definition measured from the
    /// vendor's prefab, and an icon that is a picture of the model. The vendor prefab is only read.
    /// <para>Each piece is held to the world's measure (<see cref="WorldScaleSettings"/>): its row names what kind
    /// of thing it is, and a model outside that kind's range is multiplied back into it. What of it stands in a
    /// walker's way is measured over the height a person takes up, so a tree stops nobody under its crown.</para>
    /// The Null's pieces from the Fantasy Kingdom pack are prefab variants in the Null's own colours
    /// (<see cref="NullTreatment"/>). Ids are the tables' and are never renumbered: 20000 Hegemony, 23000 Null,
    /// 26000 Covenant. Names are first picks and want the owner's eye.
    /// </summary>
    public static partial class FurnishingCatalogBuilder
    {
        public const string ItemFolder = HousingLotBuilder.DataFolder + "/Items";
        public const string FurnishingFolder = HousingLotBuilder.DataFolder + "/Furnishings";
        public const string IconFolder = HousingLotBuilder.DataFolder + "/Icons";
        public const string CatalogPath = HousingLotBuilder.DataFolder + "/FurnishingCatalog.asset";
        public const string SyntyRoot = "Assets/Synty";

        private struct Row
        {
            public ushort Id;
            public FactionId Faction;
            public string Pack, Prefab, Name, Category, Kind;
            public FurnishingMount? Mount;
            public FurnishingPlace? Place;
            public bool? HasTop, Blocks;
            public bool Flip;
        }

        private static Row R(ushort id, FactionId faction, string pack, string prefab, string name, string category, string kind,
            FurnishingMount? mount = null, FurnishingPlace? place = null, bool? hasTop = null, bool? blocks = null, bool flip = false) =>
            new Row { Id = id, Faction = faction, Pack = pack, Prefab = prefab, Name = name, Category = category, Kind = kind,
                Mount = mount, Place = place, HasTop = hasTop, Blocks = blocks, Flip = flip };

        private static readonly string[] TopKinds = { "table", "lowtable", "counter", "lowcase", "crate", "barrel", "chest" };
        private static readonly string[] LightKinds = { "candle", "tablelamp", "floorlamp", "lamppost", "chandelier", "brazier", "hearth" };
        private static readonly string[] LawnKinds = { "smalltree", "tree", "greattree", "hedge", "bush", "lamppost", "fountain", "well", "shelter", "rock", "monument" };
        private static readonly string[] WalkedThroughKinds = { "rug", "flowers", "shelter", "small", "picture", "banner", "candle" };

        [MenuItem("Krytical Nova/Housing/2. Build Furnishings")]
        public static void Build() => Build(captureIcons: true);

        [MenuItem("Krytical Nova/Housing/Build Furnishings (No Icons)")]
        public static void BuildWithoutIcons() => Build(captureIcons: false);

        public static void Build(bool captureIcons)
        {
            var items = AssetDatabase.LoadAssetAtPath<ItemCatalog>(SidekickGearGenerator.CatalogPath);
            var housing = AssetDatabase.LoadAssetAtPath<HousingSettings>(HousingLotBuilder.SettingsPath);
            if (items == null || housing == null)
            {
                Debug.LogError($"[Housing] No item catalog at {SidekickGearGenerator.CatalogPath}, or no housing settings (build the Lot first).");
                return;
            }
            WorldScaleSettings scale = WorldScaleBuilder.Load();
            WeatherVfxBuilder.EnsureFolder(ItemFolder);
            WeatherVfxBuilder.EnsureFolder(FurnishingFolder);
            WeatherVfxBuilder.EnsureFolder(IconFolder);

            var furnishings = new List<FurnishingDefinition>(Rows.Length);
            var made = new List<ItemDefinition>(Rows.Length);
            var pictures = new List<(GameObject model, string path)>();
            var pictured = new List<ItemDefinition>();
            var kept = new HashSet<string>();
            var report = new System.Text.StringBuilder();
            int missing = 0;

            foreach (Row row in Rows)
            {
                string prefabPath = FindPrefab(row.Pack, row.Prefab);
                var prefab = prefabPath != null ? AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) : null;
                if (prefab == null)
                {
                    Debug.LogWarning($"[Housing] {row.Id} {row.Name}: no prefab {row.Prefab} in {row.Pack}; skipped.");
                    missing++;
                    continue;
                }
                // The Null wear the Fantasy Kingdom's things in their own colours.
                if (row.Faction == FactionId.Null && row.Pack == Kingdom) prefab = NullTreatment.Variant(prefab);

                string asset = $"Furnishing_{row.Id}";
                string iconPath = $"{IconFolder}/{asset}_{row.Prefab.Replace("SM_Prop_", string.Empty).Replace("SM_Env_", string.Empty).Replace("SM_Bld_", string.Empty)}.png";
                // A furnishing is anyone's: its faction is the style it is made in, not a lock.
                ItemDefinition item = Item(row.Id, FactionId.None, row.Name, asset,
                    $"A furnishing in the {row.Faction} style. Set it down at your Hall with Arrange; it is kept in the Furnishing Crate.", "HOME");
                FurnishingDefinition furnishing = Furnishing(row, asset, item, prefab, scale, housing);
                made.Add(item);
                furnishings.Add(furnishing);
                kept.Add($"{ItemFolder}/{asset}.asset");
                kept.Add($"{FurnishingFolder}/{asset}.asset");
                kept.Add(iconPath);
                report.Append(row.Id).Append('\t').Append(row.Name).Append('\t').Append(row.Kind).Append('\t')
                    .Append(furnishing.modelScale.ToString("0.00")).Append('\t').Append(furnishing.boundsSize.x.ToString("0.00")).Append('\t')
                    .Append(furnishing.boundsSize.y.ToString("0.00")).Append('\t').Append(furnishing.boundsSize.z.ToString("0.00")).Append('\t')
                    .Append(furnishing.mount).Append('\t').Append(furnishing.blocks ? "blocks" : "-").Append('\n');

                // A picture is taken again when the model behind an id has changed.
                bool stale = item.icon == null || AssetDatabase.GetAssetPath(item.icon) != iconPath;
                if (captureIcons && stale)
                {
                    pictures.Add((prefab, iconPath));
                    pictured.Add(item);
                }
            }

            CaptureIcons(pictures, pictured);

            var catalog = AssetDatabase.LoadAssetAtPath<FurnishingCatalog>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<FurnishingCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }
            catalog.SetContents(furnishings.OrderBy(f => f.itemId).ToList());
            EditorUtility.SetDirty(catalog);

            int removed = RemoveStale(items, made, kept, new[] { ItemFolder, FurnishingFolder, IconFolder }, "Furnishing_");
            AssetDatabase.SaveAssets();
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "kn_furnishings.tsv"), report.ToString());
            Debug.Log($"[Housing] {furnishings.Count} furnishings built ({pictures.Count} icons taken, {missing} missing prefabs, {removed} old assets removed). Catalog: {CatalogPath}.");
        }

        /// <summary>
        /// Puts the made items into the item catalog and deletes what an earlier table made that this one no longer
        /// does: assets under the folders whose name starts with the prefix and that are not among the kept.
        /// </summary>
        public static int RemoveStale(ItemCatalog items, List<ItemDefinition> made, HashSet<string> kept, string[] folders, string prefix)
        {
            var gone = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets(string.Empty, folders))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetDatabase.IsValidFolder(path) || kept.Contains(path)) continue;
                if (Path.GetFileName(path).StartsWith(prefix)) gone.Add(path);
            }
            var goneItems = new HashSet<ItemDefinition>(gone.Select(AssetDatabase.LoadAssetAtPath<ItemDefinition>).Where(i => i != null));
            var all = items.Items.Where(i => i != null && !goneItems.Contains(i)).Concat(made).Distinct().OrderBy(i => i.itemId).ToList();
            items.SetContents(all, items.StarterEquipped.ToList(), items.StarterBag.ToList());
            EditorUtility.SetDirty(items);
            if (gone.Count > 0)
            {
                var failed = new List<string>();
                AssetDatabase.DeleteAssets(gone.ToArray(), failed);
            }
            return gone.Count;
        }

        /// <summary>Takes the pictures: of the model alone, since flames and glows are no part of an icon and the icon rig measures meshes only.</summary>
        public static void CaptureIcons(List<(GameObject model, string path)> pictures, List<ItemDefinition> pictured)
        {
            if (pictures.Count == 0) return;
            var models = new List<(GameObject model, string path)>(pictures.Count);
            foreach ((GameObject model, string path) in pictures) models.Add((MeshesOnly(model), path));
            try
            {
                List<Sprite> sprites = ItemIconCapture.CaptureModels(models);
                for (int n = 0; n < sprites.Count; n++)
                {
                    if (sprites[n] == null) continue;
                    pictured[n].icon = sprites[n];
                    EditorUtility.SetDirty(pictured[n]);
                }
            }
            finally
            {
                foreach ((GameObject model, string _) in models) Object.DestroyImmediate(model);
            }
        }

        // A copy of a prefab with nothing in it but its meshes, to be pictured. The prefab itself is not touched.
        private static GameObject MeshesOnly(GameObject prefab)
        {
            GameObject copy = Object.Instantiate(prefab);
            copy.name = prefab.name;
            copy.hideFlags = HideFlags.HideAndDontSave;
            copy.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            foreach (Renderer renderer in copy.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null || HousingLotBuilder.IsMesh(renderer)) continue;
                if (renderer.gameObject == copy) Object.DestroyImmediate(renderer);
                else Object.DestroyImmediate(renderer.gameObject);
            }
            foreach (Collider collider in copy.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(collider);
            return copy;
        }

        public static string FindPrefab(string pack, string name)
        {
            string folder = $"{SyntyRoot}/{pack}/Prefabs";
            if (!AssetDatabase.IsValidFolder(folder)) return null;
            foreach (string guid in AssetDatabase.FindAssets($"{name} t:Prefab", new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(path) == name) return path;
            }
            return null;
        }

        /// <summary>
        /// The item a furnishing or a Hall's look is carried as, never renumbered once made. <paramref name="faction"/>
        /// binds it to one faction (a Facade); None leaves it anyone's (a furnishing, a Finish).
        /// </summary>
        public static ItemDefinition Item(ushort id, FactionId faction, string displayName, string asset, string description, string glyph)
        {
            string path = $"{ItemFolder}/{asset}.asset";
            var item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
            bool created = item == null;
            if (created)
            {
                item = ScriptableObject.CreateInstance<ItemDefinition>();
                item.rarity = ItemRarity.Common;
            }
            // What the table owns on every run.
            item.itemId = id;
            item.displayName = displayName;
            item.description = description;
            item.maxStack = 99;
            item.equipSlot = EquipSlot.None;
            item.placeholderGlyph = glyph;
            item.boundFaction = faction;
            if (created) AssetDatabase.CreateAsset(item, path);
            else EditorUtility.SetDirty(item);
            return item;
        }

        private static FurnishingDefinition Furnishing(in Row row, string asset, ItemDefinition item, GameObject prefab,
            WorldScaleSettings scale, HousingSettings housing)
        {
            string path = $"{FurnishingFolder}/{asset}.asset";
            var furnishing = AssetDatabase.LoadAssetAtPath<FurnishingDefinition>(path);
            bool created = furnishing == null;
            if (created) furnishing = ScriptableObject.CreateInstance<FurnishingDefinition>();
            string kind = row.Kind;

            FurnishingMount mount = row.Mount ?? (kind == "rug" ? FurnishingMount.Rug
                : kind == "picture" || kind == "banner" ? FurnishingMount.Wall
                : kind == "chandelier" ? FurnishingMount.Ceiling
                : kind == "small" || kind == "candle" || kind == "tablelamp" ? FurnishingMount.Surface
                : FurnishingMount.Floor);

            // What hangs on a wall is turned so that its thin side is its depth and its face looks out from the wall (+z).
            Vector3 euler = Vector3.zero;
            if (mount == FurnishingMount.Wall)
            {
                Bounds asMade = Measure(prefab, Vector3.zero, 1f, float.NegativeInfinity, float.PositiveInfinity);
                if (asMade.size.x < asMade.size.z) euler.y = 90f;
            }
            if (row.Flip) euler.y += 180f;

            Bounds raw = Measure(prefab, euler, 1f, float.NegativeInfinity, float.PositiveInfinity);
            float factor = scale.Fit(kind, raw.size);
            Bounds box = new Bounds(raw.center * factor, raw.size * factor);
            // What of it is in a walker's way: its mesh between the ground and a person's height.
            Bounds solid = mount == FurnishingMount.Floor ? Measure(prefab, euler, factor, box.min.y - 0.01f, box.min.y + housing.walkBand) : box;
            float largest = Mathf.Max(box.size.x, box.size.y, box.size.z);

            furnishing.item = item;
            furnishing.itemId = row.Id;
            furnishing.faction = row.Faction;
            furnishing.category = row.Category;
            furnishing.source = FurnishingSource.Vendor;
            furnishing.prefab = prefab;
            furnishing.kind = kind;
            furnishing.modelScale = factor;
            furnishing.modelEuler = euler;
            furnishing.boundsCenter = box.center;
            furnishing.boundsSize = box.size;
            furnishing.solidCenter = solid.center;
            furnishing.solidSize = solid.size;
            furnishing.mount = mount;
            furnishing.hasTop = row.HasTop ?? (mount == FurnishingMount.Floor && TopKinds.Contains(kind));
            furnishing.lights = LightKinds.Contains(kind);
            furnishing.lightOffset = new Vector3(box.center.x, box.min.y + box.size.y * (mount == FurnishingMount.Ceiling ? 0.3f : 0.8f), box.center.z);
            furnishing.blocks = row.Blocks ?? (mount == FurnishingMount.Floor && !WalkedThroughKinds.Contains(kind) && solid.size.y >= housing.blockHeight);
            furnishing.castsShadow = largest >= 0.4f;
            furnishing.budgetCost = largest < 0.6f ? 1 : largest < 2.5f ? 2 : 4;
            furnishing.place = row.Place ?? (mount == FurnishingMount.Wall || mount == FurnishingMount.Ceiling ? FurnishingPlace.IndoorOnly
                : LawnKinds.Contains(kind) ? FurnishingPlace.LawnOnly : FurnishingPlace.Anywhere);

            if (created) AssetDatabase.CreateAsset(furnishing, path);
            else EditorUtility.SetDirty(furnishing);
            return furnishing;
        }

        /// <summary>
        /// A model's box as a piece holds it: turned and multiplied as it will be in the game, measured from the
        /// points of its meshes that lie between two heights (all of them, when the heights are infinite). Returns
        /// the whole box when no point lies between them.
        /// </summary>
        public static Bounds Measure(GameObject prefab, Vector3 euler, float factor, float fromHeight, float toHeight)
        {
            var holder = new GameObject("Measure") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                GameObject model = Object.Instantiate(prefab, holder.transform, false);
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = Quaternion.Euler(euler);
                model.transform.localScale = prefab.transform.localScale * factor;

                bool any = false, anyAtAll = false;
                Bounds within = default, whole = default;
                foreach (MeshFilter filter in model.GetComponentsInChildren<MeshFilter>())
                {
                    Mesh mesh = filter.sharedMesh;
                    if (mesh == null || !filter.TryGetComponent(out MeshRenderer _)) continue;
                    Matrix4x4 toWorld = filter.transform.localToWorldMatrix;
                    foreach (Vector3 vertex in mesh.vertices)
                    {
                        Vector3 point = toWorld.MultiplyPoint3x4(vertex);
                        if (anyAtAll) whole.Encapsulate(point); else whole = new Bounds(point, Vector3.zero);
                        anyAtAll = true;
                        if (point.y < fromHeight || point.y > toHeight) continue;
                        if (any) within.Encapsulate(point); else within = new Bounds(point, Vector3.zero);
                        any = true;
                    }
                }
                return any ? within : anyAtAll ? whole : new Bounds(Vector3.zero, Vector3.one);
            }
            finally
            {
                Object.DestroyImmediate(holder);
            }
        }
    }
}
