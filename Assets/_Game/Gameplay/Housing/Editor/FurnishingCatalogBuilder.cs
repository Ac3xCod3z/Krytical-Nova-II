using System.Collections.Generic;
using System.IO;
using System.Linq;
using KryticalNova.Factions;
using KryticalNova.Housing;
using KryticalNova.Items;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.EditorTools.Housing
{
    /// <summary>
    /// Makes the furnishings: for every row of the table, an item (so the piece is carried, stored and traded like
    /// any other), a furnishing definition measured from the vendor's prefab, and an icon that is a picture of the
    /// model. The vendor prefab is only read. How a piece mounts, whether things stand on it and whether it lights
    /// are worked out from its name; the table corrects what the name gets wrong.
    /// Ids are the table's and are never renumbered: 20000 Hegemony, 23000 Null, 26000 Covenant.
    /// Names are first picks and want the owner's eye.
    /// </summary>
    public static class FurnishingCatalogBuilder
    {
        public const string ItemFolder = HousingLotBuilder.DataFolder + "/Items";
        public const string FurnishingFolder = HousingLotBuilder.DataFolder + "/Furnishings";
        public const string IconFolder = HousingLotBuilder.DataFolder + "/Icons";
        public const string CatalogPath = HousingLotBuilder.DataFolder + "/FurnishingCatalog.asset";
        private const string SyntyRoot = "Assets/Synty";

        private struct Row
        {
            public ushort Id;
            public FactionId Faction;
            public string Pack, Prefab, Name, Category;
            public FurnishingMount? Mount;
            public FurnishingPlace? Place;
            public bool? HasTop;
        }

        private static Row R(ushort id, FactionId faction, string pack, string prefab, string name, string category,
            FurnishingMount? mount = null, FurnishingPlace? place = null, bool? hasTop = null) =>
            new Row { Id = id, Faction = faction, Pack = pack, Prefab = prefab, Name = name, Category = category, Mount = mount, Place = place, HasTop = hasTop };

        private const FactionId H = FactionId.Hegemony, N = FactionId.Null, C = FactionId.Covenant;
        private const string Space = "PolygonSciFiSpace", Worlds = "PolygonSciFiWorlds", City = "PolygonSciFiCity",
            Empire = "PolygonSamuraiEmpire", Samurai = "PolygonSamurai", Fortress = "PolygonDarkFortress", Dark = "PolygonDarkFantasy",
            Kingdom = "PolygonFantasyKingdom", Elven = "PolygonElvenRealm";

        // The starter set: enough of each kind, for each faction, to try the building with.
        private static readonly Row[] Rows =
        {
            // ---- Hegemony: clean corporate tech, with a garden from the old country
            R(20001, H, Space, "SM_Prop_Bed_01", "Crew Bunk", "Seating"),
            R(20002, H, Worlds, "SM_Prop_Bed_Double_01", "Executive Bed", "Seating"),
            R(20003, H, Worlds, "SM_Prop_Sofa_01", "Lounge Sofa", "Seating"),
            R(20004, H, Worlds, "SM_Prop_Chair_01", "Station Chair", "Seating"),
            R(20005, H, Space, "SM_Prop_Stool_01", "Deck Stool", "Seating"),
            R(20006, H, Space, "SM_Prop_CornerSeat_01", "Corner Booth", "Seating"),
            R(20007, H, City, "SM_Prop_Bench_01", "Plaza Bench", "Seating"),
            R(20010, H, Space, "SM_Prop_SquareTable_01", "Mess Table", "Tables"),
            R(20011, H, Worlds, "SM_Prop_Table_Large_01", "Briefing Table", "Tables"),
            R(20012, H, Space, "SM_Prop_Desk_Small_01", "Duty Desk", "Tables"),
            R(20013, H, Space, "SM_Prop_MapTable_01", "Map Table", "Tables"),
            R(20014, H, Worlds, "SM_Prop_Projection_Table_01", "Holo Table", "Tables"),
            R(20020, H, Worlds, "SM_Prop_Locker_01", "Kit Locker", "Storage"),
            R(20021, H, Worlds, "SM_Prop_Crate_01", "Supply Crate", "Storage"),
            R(20022, H, Worlds, "SM_Prop_Scav_Barrel_01", "Coolant Drum", "Storage"),
            R(20030, H, Space, "SM_Prop_Light_Panel_01", "Light Panel", "Lighting"),
            R(20031, H, City, "SM_Prop_Streetlight_01", "Street Lamp", "Lighting", place: FurnishingPlace.LawnOnly),
            R(20032, H, Empire, "SM_Prop_Lantern_01", "Stone Lantern", "Lighting"),
            R(20040, H, Space, "SM_Prop_Screen_01", "Wall Screen", "Decor"),
            R(20041, H, Worlds, "SM_Prop_Monitor_01", "Desk Monitor", "Decor", FurnishingMount.Surface),
            R(20042, H, Space, "SM_Prop_Sink_01", "Wash Basin", "Decor"),
            R(20043, H, City, "SM_Prop_VendingMachine_01", "Vending Unit", "Decor"),
            R(20050, H, Space, "SM_Prop_Plant_Vase_01", "Potted Fern", "Plants", FurnishingMount.Surface),
            R(20051, H, City, "SM_Prop_Plant_Vase_02", "Tall Planter", "Plants"),
            R(20052, H, City, "SM_Env_Hedge_01", "Clipped Hedge", "Plants", place: FurnishingPlace.LawnOnly),
            R(20053, H, City, "SM_Env_Tree_01", "Plaza Tree", "Plants", place: FurnishingPlace.LawnOnly),
            R(20054, H, Empire, "SM_Env_Tree_Maple_01", "Red Maple", "Plants", place: FurnishingPlace.LawnOnly),
            R(20055, H, Empire, "SM_Env_Bamboo_01", "Bamboo Stand", "Plants", place: FurnishingPlace.LawnOnly),
            R(20056, H, Samurai, "SM_Prop_Bonsai_01", "Bonsai", "Plants", FurnishingMount.Surface),

            // ---- Null: machines in old stone
            R(23001, N, Fortress, "SM_Prop_Bed_01", "Iron Cot", "Seating"),
            R(23002, N, Fortress, "SM_Prop_Hammock_01", "Hammock", "Seating"),
            R(23003, N, Fortress, "SM_Prop_Chair_01", "Hall Chair", "Seating"),
            R(23004, N, Fortress, "SM_Prop_Throne_01", "Iron Throne", "Seating"),
            R(23005, N, Dark, "SM_Prop_Pew_01", "Chapel Pew", "Seating"),
            R(23010, N, Fortress, "SM_Prop_Table_01", "Trestle Table", "Tables"),
            R(23011, N, Fortress, "SM_Prop_Desk_01", "Scribe's Desk", "Tables"),
            R(23020, N, Fortress, "SM_Prop_Bookshelf_01", "Archive Shelf", "Storage"),
            R(23021, N, Fortress, "SM_Prop_Cupboard_01", "Cupboard", "Storage"),
            R(23022, N, Dark, "SM_Prop_Bookshelf_01", "Crypt Shelf", "Storage"),
            R(23023, N, Fortress, "SM_Prop_Barrel_01", "Tar Barrel", "Storage"),
            R(23024, N, Fortress, "SM_Prop_Crate_01", "Iron-bound Crate", "Storage"),
            R(23030, N, Fortress, "SM_Prop_Brazier_01", "Brazier", "Lighting"),
            R(23031, N, Dark, "SM_Prop_Candelabra_01", "Candelabra", "Lighting"),
            R(23032, N, Dark, "SM_Prop_Lantern_01", "Grave Lantern", "Lighting", FurnishingMount.Surface),
            R(23033, N, Fortress, "SM_Prop_Chandelier_01", "Iron Chandelier", "Lighting", FurnishingMount.Ceiling),
            R(23040, N, Fortress, "SM_Prop_Fireplace_01", "Hearth", "Decor"),
            R(23041, N, Fortress, "SM_Prop_Statue_01", "Sentinel Statue", "Decor"),
            R(23042, N, Dark, "SM_Prop_Gargoyle_01", "Gargoyle", "Decor"),
            R(23043, N, Dark, "SM_Prop_Organ_01", "Pipe Organ", "Decor"),
            R(23044, N, Fortress, "SM_Prop_Fountain_01", "Black Fountain", "Decor", place: FurnishingPlace.LawnOnly),
            R(23045, N, Dark, "SM_Prop_Well_01", "Old Well", "Decor", place: FurnishingPlace.LawnOnly),
            R(23050, N, Fortress, "SM_Prop_Rug_01", "Hall Rug", "Rugs", FurnishingMount.Rug),
            R(23051, N, Fortress, "SM_Prop_Banner_01", "War Banner", "Walls", FurnishingMount.Wall),
            R(23060, N, Fortress, "SM_Prop_Planter_01", "Stone Planter", "Plants"),
            R(23061, N, Fortress, "SM_Env_Thorns_01", "Thorn Bush", "Plants", place: FurnishingPlace.LawnOnly),
            R(23062, N, Dark, "SM_Env_Tree_Dead_01", "Dead Tree", "Plants", place: FurnishingPlace.LawnOnly),
            R(23063, N, Dark, "SM_Env_Rock_01", "Standing Stone", "Plants", place: FurnishingPlace.LawnOnly),

            // ---- Covenant: clans, timber and hearth
            R(26001, C, Kingdom, "SM_Prop_Bed_01", "Clan Bed", "Seating"),
            R(26002, C, Kingdom, "SM_Prop_Chair_Wood_01", "Oak Chair", "Seating"),
            R(26003, C, Kingdom, "SM_Prop_Chair_Fancy_01", "Carved Chair", "Seating"),
            R(26004, C, Kingdom, "SM_Prop_Throne_01", "Chieftain's Seat", "Seating"),
            R(26005, C, Elven, "SM_Prop_Couch_01", "Elven Couch", "Seating"),
            R(26010, C, Kingdom, "SM_Prop_Table_Wood_01", "Oak Table", "Tables"),
            R(26011, C, Elven, "SM_Prop_Table_01", "Elven Table", "Tables"),
            R(26012, C, Kingdom, "SM_Prop_Bar_01", "Tavern Bar", "Tables"),
            R(26020, C, Kingdom, "SM_Prop_Wardrobe_01", "Wardrobe", "Storage"),
            R(26021, C, Kingdom, "SM_Prop_Dresser_01", "Dresser", "Storage"),
            R(26022, C, Kingdom, "SM_Prop_Cabinet_01", "Cabinet", "Storage"),
            R(26023, C, Kingdom, "SM_Prop_Chest_01", "Sea Chest", "Storage"),
            R(26024, C, Kingdom, "SM_Prop_Barrel_01", "Ale Barrel", "Storage"),
            R(26030, C, Kingdom, "SM_Prop_Street_Lamp_01", "Lamp Post", "Lighting", place: FurnishingPlace.LawnOnly),
            R(26031, C, Elven, "SM_Prop_Lamp_01", "Elven Lamp", "Lighting"),
            R(26032, C, Elven, "SM_Prop_Lantern_01", "Moon Lantern", "Lighting", FurnishingMount.Surface),
            R(26033, C, Kingdom, "SM_Prop_Chandelier_01", "Antler Chandelier", "Lighting", FurnishingMount.Ceiling),
            R(26040, C, Kingdom, "SM_Prop_Stove_01", "Cook Stove", "Decor"),
            R(26041, C, Elven, "SM_Prop_Statue_01", "Ancestor Statue", "Decor"),
            R(26042, C, Elven, "SM_Prop_Vase_01", "Glazed Vase", "Decor", FurnishingMount.Surface),
            R(26043, C, Kingdom, "SM_Env_Fountain_01", "Village Fountain", "Decor", place: FurnishingPlace.LawnOnly),
            R(26044, C, Elven, "SM_Prop_Fountain_01", "Spring Fountain", "Decor", place: FurnishingPlace.LawnOnly),
            R(26045, C, Kingdom, "SM_Prop_Well_01", "Stone Well", "Decor", place: FurnishingPlace.LawnOnly),
            R(26046, C, Kingdom, "SM_Prop_Gazebo_01", "Gazebo", "Decor", place: FurnishingPlace.LawnOnly, hasTop: false),
            R(26047, C, Elven, "SM_Prop_BirdBath_01", "Bird Bath", "Decor", place: FurnishingPlace.LawnOnly),
            R(26050, C, Kingdom, "SM_Prop_Rug_01", "Woven Rug", "Rugs", FurnishingMount.Rug),
            R(26051, C, Kingdom, "SM_Prop_Painting_01", "Painting", "Walls", FurnishingMount.Wall),
            R(26052, C, Kingdom, "SM_Prop_Banner_01", "Clan Banner", "Walls", FurnishingMount.Wall),
            R(26060, C, Kingdom, "SM_Prop_PlanterBox_01", "Planter Box", "Plants"),
            R(26061, C, Kingdom, "SM_Env_Hedge_01", "Hedgerow", "Plants", place: FurnishingPlace.LawnOnly),
            R(26062, C, Kingdom, "SM_Env_Bush_01", "Bush", "Plants", place: FurnishingPlace.LawnOnly),
            R(26063, C, Kingdom, "SM_Env_Flowers_01", "Wildflowers", "Plants", place: FurnishingPlace.LawnOnly),
            R(26064, C, Kingdom, "SM_Env_Big_Tree_01", "Great Oak", "Plants", place: FurnishingPlace.LawnOnly),
        };

        private static readonly string[] TopWords = { "Table", "Desk", "Cabinet", "Shelf", "Bar", "Counter", "Crate", "Barrel", "Chest", "Dresser", "Cupboard", "Stove", "Locker", "Organ" };
        private static readonly string[] LightWords = { "Light", "Lamp", "Lantern", "Candle", "Candelabra", "Torch", "Chandelier", "Brazier", "Fireplace" };

        [MenuItem("Krytical Nova/Housing/2. Build Furnishings")]
        public static void Build() => Build(captureIcons: true);

        [MenuItem("Krytical Nova/Housing/Build Furnishings (No Icons)")]
        public static void BuildWithoutIcons() => Build(captureIcons: false);

        public static void Build(bool captureIcons)
        {
            var items = AssetDatabase.LoadAssetAtPath<ItemCatalog>(SidekickGearGenerator.CatalogPath);
            if (items == null)
            {
                Debug.LogError($"[Housing] No item catalog at {SidekickGearGenerator.CatalogPath}.");
                return;
            }
            WeatherVfxBuilder.EnsureFolder(ItemFolder);
            WeatherVfxBuilder.EnsureFolder(FurnishingFolder);
            WeatherVfxBuilder.EnsureFolder(IconFolder);

            var furnishings = new List<FurnishingDefinition>(Rows.Length);
            var made = new List<ItemDefinition>(Rows.Length);
            var pictures = new List<(GameObject model, string path)>();
            var pictured = new List<ItemDefinition>();
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

                string asset = $"Furnishing_{row.Faction}_{row.Id}_{row.Prefab.Replace("SM_Prop_", string.Empty).Replace("SM_Env_", string.Empty)}";
                ItemDefinition item = Item(row, asset);
                FurnishingDefinition furnishing = Furnishing(row, asset, item, prefab);
                made.Add(item);
                furnishings.Add(furnishing);
                if (captureIcons && item.icon == null)
                {
                    pictures.Add((prefab, $"{IconFolder}/{asset}.png"));
                    pictured.Add(item);
                }
            }

            if (pictures.Count > 0)
            {
                // The picture is of the model alone: flames, glows and other effects are not part of the icon, and
                // the icon rig measures meshes only.
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

            var catalog = AssetDatabase.LoadAssetAtPath<FurnishingCatalog>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<FurnishingCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }
            catalog.SetContents(furnishings.OrderBy(f => f.itemId).ToList());
            EditorUtility.SetDirty(catalog);

            var all = items.Items.Where(i => i != null).Concat(made).Distinct().OrderBy(i => i.itemId).ToList();
            items.SetContents(all, items.StarterEquipped.ToList(), items.StarterBag.ToList());
            EditorUtility.SetDirty(items);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Housing] {furnishings.Count} furnishings built ({pictures.Count} icons taken, {missing} missing prefabs). Catalog: {CatalogPath}.");
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

        private static string FindPrefab(string pack, string name)
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

        private static ItemDefinition Item(in Row row, string asset)
        {
            string path = $"{ItemFolder}/{asset}.asset";
            var item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
            bool created = item == null;
            if (created)
            {
                item = ScriptableObject.CreateInstance<ItemDefinition>();
                item.itemId = row.Id; // never renumbered once made
                item.rarity = ItemRarity.Common;
            }
            // What the table owns on every run.
            item.displayName = row.Name;
            item.description = $"A furnishing of the {row.Faction}. Set it down at your Hall with Arrange; it is kept in the Furnishing Crate.";
            item.maxStack = 99;
            item.equipSlot = EquipSlot.None;
            item.placeholderGlyph = "HOME";
            item.boundFaction = row.Faction;
            if (created) AssetDatabase.CreateAsset(item, path);
            else EditorUtility.SetDirty(item);
            return item;
        }

        private static FurnishingDefinition Furnishing(in Row row, string asset, ItemDefinition item, GameObject prefab)
        {
            string path = $"{FurnishingFolder}/{asset}.asset";
            var furnishing = AssetDatabase.LoadAssetAtPath<FurnishingDefinition>(path);
            bool created = furnishing == null;
            if (created) furnishing = ScriptableObject.CreateInstance<FurnishingDefinition>();

            Bounds bounds = HousingLotBuilder.PrefabBounds(prefab);
            float largest = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            string name = row.Prefab;

            furnishing.item = item;
            furnishing.itemId = row.Id;
            furnishing.faction = row.Faction;
            furnishing.category = row.Category;
            furnishing.source = FurnishingSource.Vendor;
            furnishing.prefab = prefab;
            furnishing.boundsCenter = bounds.center;
            furnishing.boundsSize = bounds.size;
            furnishing.mount = row.Mount ?? GuessMount(name, largest);
            furnishing.hasTop = row.HasTop ?? (furnishing.mount == FurnishingMount.Floor && TopWords.Any(name.Contains));
            furnishing.lights = LightWords.Any(name.Contains);
            furnishing.castsShadow = largest >= 0.4f;
            furnishing.budgetCost = largest < 0.6f ? 1 : largest < 2.5f ? 2 : 4;
            // Until Halls have an inside, what hangs on a wall or from a ceiling has nowhere to go on a lawn.
            furnishing.place = row.Place ?? (furnishing.mount == FurnishingMount.Wall || furnishing.mount == FurnishingMount.Ceiling
                ? FurnishingPlace.IndoorOnly : FurnishingPlace.Anywhere);

            if (created) AssetDatabase.CreateAsset(furnishing, path);
            else EditorUtility.SetDirty(furnishing);
            return furnishing;
        }

        private static FurnishingMount GuessMount(string name, float largest)
        {
            if (name.Contains("Rug") || name.Contains("Carpet")) return FurnishingMount.Rug;
            if (name.Contains("Painting") || name.Contains("Banner") || name.Contains("Mirror") || name.Contains("Poster")) return FurnishingMount.Wall;
            if (name.Contains("Chandelier") || name.Contains("Hanging")) return FurnishingMount.Ceiling;
            return largest < 0.6f ? FurnishingMount.Surface : FurnishingMount.Floor;
        }
    }
}
