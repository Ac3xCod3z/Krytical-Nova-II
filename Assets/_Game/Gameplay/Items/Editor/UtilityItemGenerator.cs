using System.Collections.Generic;
using System.Linq;
using KryticalNova.Items;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Creates every item that is not gear from one table: the Stims today, and whatever a later system needs
    /// (permits, rations, Recall Tags, Alloys, Skill Chips) as a new row. Registers them in the item catalog.
    /// An asset that already exists keeps its numbers and text, so hand tuning survives a re-run; only what the
    /// table owns outright (what kind of item it is) is refreshed. IDs are never renumbered: append.
    /// </summary>
    public static class UtilityItemGenerator
    {
        private const string ItemsRoot = "Assets/_Game/Gameplay/Items/Data/Assets";
        private const string Folder = ItemsRoot + "/Consumables";
        /// <summary>What the forge uses up: Alloys, Failsafes, the Fusion Catalyst, Tuning Chips.</summary>
        private const string MaterialFolder = ItemsRoot + "/GearMaterials";
        private const string CatalogPath = ItemsRoot + "/ItemCatalog.asset";

        private const ushort StimStack = 99;
        private const ushort StarterStims = 10;
        private const float StimAmount = 300f;
        /// <summary>The Dual Stim restores HP : Flux at 3 : 1.</summary>
        private const float DualFluxAmount = StimAmount / 3f;

        /// <summary>One utility item. <see cref="Starter"/> is how many a new character is handed (0 = none).</summary>
        private struct Row
        {
            public ushort Id;
            public string Asset, Name, Glyph, Description;
            public ItemRarity Rarity;
            public ushort Stack, Starter;
            public float Health, Flux;
            public bool Hotbar;
            public ItemUseEffect Effect;
            /// <summary>Null = the consumables folder.</summary>
            public string Folder;
        }

        // Stims keep their first asset names (Pill_*): saves, hotbars and GM commands are keyed on them.
        private static readonly Row[] Rows =
        {
            new Row { Id = 9001, Asset = "Pill_Vitality", Name = "HP Stim", Glyph = "HP", Rarity = ItemRarity.Common,
                Description = $"Restores {StimAmount:0} HP.", Stack = StimStack, Starter = StarterStims, Health = StimAmount, Hotbar = true },
            new Row { Id = 9002, Asset = "Pill_Chi", Name = "Flux Stim", Glyph = "FLUX", Rarity = ItemRarity.Common,
                Description = $"Restores {StimAmount:0} Flux.", Stack = StimStack, Starter = StarterStims, Flux = StimAmount, Hotbar = true },
            new Row { Id = 9003, Asset = "Pill_DualHarmony", Name = "Dual Stim", Glyph = "HP+FLUX", Rarity = ItemRarity.Uncommon,
                Description = $"Restores {StimAmount:0} HP and {DualFluxAmount:0} Flux.", Stack = StimStack,
                Health = StimAmount, Flux = DualFluxAmount, Hotbar = true },

            // Pets (design: _Docs/Design/12_pet_evolution_system.md).
            new Row { Id = PetRationId, Asset = "Pet_Ration", Name = "Pet Ration", Glyph = "FOOD", Rarity = ItemRarity.Common,
                Description = "Feeds the pet you have out: its hunger goes back to 100%. A starving pet gives no bonus and earns no XP.",
                Stack = 99, Effect = ItemUseEffect.FeedPet },

            // Kiosks: a permit is used up when a Kiosk is opened for its length. Sold in the Nova Store later.
            new Row { Id = KioskPermit14Id, Asset = "Permit_Kiosk14", Name = "Kiosk Permit (14 Days)", Glyph = "14D", Rarity = ItemRarity.Uncommon,
                Description = "Opens your Kiosk for 14 days instead of 3. Used up when the Kiosk opens.", Stack = 99 },
            new Row { Id = KioskPermit30Id, Asset = "Permit_Kiosk30", Name = "Kiosk Permit (30 Days)", Glyph = "30D", Rarity = ItemRarity.Rare,
                Description = "Opens your Kiosk for 30 days instead of 3. Used up when the Kiosk opens.", Stack = 99 },

            // The forge (_Docs/Reference/12Sky/02_Gear_System_Spec.md). Ids are named in GearMaterialIds, which the
            // settings asset reads; what each one does is decided there, not here.
            Material(GearMaterialIds.Alumite, "Alloy_Alumite", "Alumite", "+3%", ItemRarity.Common,
                "Alloy. Tempers a piece of gear by 3% on a success."),
            Material(GearMaterialIds.Iridion, "Alloy_Iridion", "Iridion", "+6%", ItemRarity.Common,
                "Alloy. Tempers a piece of gear by 6% on a success."),
            Material(GearMaterialIds.Orichalc, "Alloy_Orichalc", "Orichalc", "+9%", ItemRarity.Uncommon,
                "Alloy. Tempers a piece of gear by 9% on a success."),
            Material(GearMaterialIds.Eversteel, "Alloy_Eversteel", "Eversteel", "+12%", ItemRarity.Rare,
                "Alloy. Tempers a piece of gear by 12% on a success."),
            Material(GearMaterialIds.TrinityPlatinum, "Alloy_TrinityPlatinum", "Trinity Platinum", "+15%", ItemRarity.Epic,
                "Alloy. Tempers a piece of gear by 15% on a success."),
            Material(GearMaterialIds.PrimeAlloy, "Alloy_Prime", "Prime Alloy", "PRIME", ItemRarity.Epic,
                "The only Alloy that works from 120% Temper up. Adds 3% on a success."),
            Material(GearMaterialIds.Failsafe, "Failsafe", "Failsafe", "SAFE", ItemRarity.Rare,
                "Stops a failed Tempering from breaking the piece. Used up only when it does."),
            Material(GearMaterialIds.AdvancedFailsafe, "Failsafe_Advanced", "Advanced Failsafe", "SAFE+", ItemRarity.Epic,
                "A failed Tempering costs the piece nothing and never breaks it. Used up on a fail. Required from 133% Temper."),
            Material(GearMaterialIds.LuckyFailsafe, "Failsafe_Lucky", "Lucky Failsafe", "LUCK", ItemRarity.Rare,
                "Adds 5% to one Tempering attempt and stops that attempt from breaking the piece. Used up on the attempt."),
            Material(GearMaterialIds.FusionCatalyst, "Fusion_Catalyst", "Fusion Catalyst", "FS", ItemRarity.Rare,
                "Used up by every Fusion attempt."),
            Material(GearMaterialIds.TuningChip, "Tuning_Chip", "Tuning Chip", "TUNE", ItemRarity.Rare,
                "Used up by a Tuning attempt, stages 1 to 12."),
            Material(GearMaterialIds.VioletChip, "Tuning_VioletChip", "Violet Chip", "VIOLET", ItemRarity.Epic,
                "Used up by a Tuning attempt, stages 13 to 25."),
            Material(GearMaterialIds.ResetChip, "Tuning_ResetChip", "Reset Chip", "RESET", ItemRarity.Rare,
                "Returns a piece of gear to Tune 0. Nothing is refunded."),
        };

        private static Row Material(ushort id, string asset, string name, string glyph, ItemRarity rarity, string description) =>
            new Row { Id = id, Asset = asset, Name = name, Glyph = glyph, Rarity = rarity, Description = description, Stack = 999, Folder = MaterialFolder };

        // Ids other systems name. 9000–9999 belongs to this table.
        public const ushort PetRationId = 9010;
        public const ushort KioskPermit14Id = 9020;
        public const ushort KioskPermit30Id = 9021;

        [MenuItem("Krytical Nova/Items/5. Generate Utility Items (Stims, Permits, Rations, Forge Materials)")]
        public static void Generate()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>(CatalogPath);
            if (catalog == null)
            {
                Debug.LogError($"[Items] No item catalog at {CatalogPath}. Run 'Krytical Nova/Items/1. Generate Gear Looks From Synty' first.");
                return;
            }

            WeatherVfxBuilder.EnsureFolder(Folder);
            WeatherVfxBuilder.EnsureFolder(MaterialFolder);
            var made = new List<ItemDefinition>(Rows.Length);
            var bag = catalog.StarterBag.ToList();
            foreach (Row row in Rows)
            {
                ItemDefinition item = Item(row);
                made.Add(item);
                if (row.Starter > 0 && !bag.Any(s => s.item == item)) bag.Add(new StarterItem { item = item, count = row.Starter });
            }

            var items = catalog.Items.Where(i => i != null).Concat(made).Distinct().OrderBy(i => i.itemId).ToList();
            catalog.SetContents(items, catalog.StarterEquipped.ToList(), bag);
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Items] {made.Count} utility items registered in {CatalogPath}.");
        }

        private static ItemDefinition Item(in Row row)
        {
            string path = $"{row.Folder ?? Folder}/{row.Asset}.asset";
            var def = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
            bool created = def == null;
            if (created)
            {
                def = ScriptableObject.CreateInstance<ItemDefinition>();
                def.itemId = row.Id;
                def.displayName = row.Name;
                def.description = row.Description;
                def.rarity = row.Rarity;
                def.maxStack = row.Stack;
                def.restoreHealth = row.Health;
                def.restoreChi = row.Flux;
            }

            // What the table owns on every run: the kind of item this is, and the text it shows until it has an icon.
            def.placeholderGlyph = row.Glyph;
            def.equipSlot = EquipSlot.None;
            def.usableFromHotbar = row.Hotbar;
            def.useEffect = row.Effect;

            if (created) AssetDatabase.CreateAsset(def, path);
            else EditorUtility.SetDirty(def);
            return def;
        }
    }
}
