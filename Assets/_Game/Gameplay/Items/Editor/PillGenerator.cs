using System.Collections.Generic;
using System.Linq;
using KryticalNova.Items;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Creates the regeneration pills (consumables) and registers them in the item catalog and starter bag.
    /// Existing pill assets are left alone, so hand-tuned amounts survive a re-run.
    /// </summary>
    public static class PillGenerator
    {
        private const string ItemsRoot = "Assets/_Game/Gameplay/Items/Data/Assets";
        private const string PillFolder = ItemsRoot + "/Consumables";
        private const string CatalogPath = ItemsRoot + "/ItemCatalog.asset";

        // Consumable ID range. Never renumber, only append.
        private const ushort VitalityPillId = 9001;
        private const ushort ChiPillId = 9002;
        private const ushort DualPillId = 9003;

        private const ushort PillStack = 99;
        private const ushort StarterCount = 10;
        private const float PillAmount = 300f;
        /// <summary>The dual pill restores HP : Chi at 3 : 1.</summary>
        private const float DualChiAmount = PillAmount / 3f;

        [MenuItem("Krytical Nova/Items/5. Generate Pills")]
        public static void Generate()
        {
            WeatherVfxBuilder.EnsureFolder(PillFolder);
            var pills = new List<ItemDefinition>
            {
                Pill(VitalityPillId, "Pill_Vitality", "Vitality Pill", ItemRarity.Common, "HP",
                    $"Restores {PillAmount:0} HP.", PillAmount, 0f),
                Pill(ChiPillId, "Pill_Chi", "Chi Pill", ItemRarity.Common, "CHI",
                    $"Restores {PillAmount:0} Chi.", 0f, PillAmount),
                Pill(DualPillId, "Pill_DualHarmony", "Dual Harmony Pill", ItemRarity.Uncommon, "HP+CHI",
                    $"Restores {PillAmount:0} HP and {DualChiAmount:0} Chi.", PillAmount, DualChiAmount),
            };

            var catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>(CatalogPath);
            if (catalog == null)
            {
                Debug.LogError($"[Pills] No item catalog at {CatalogPath}. Run 'Krytical Nova/Items/1. Generate Test Gear From Synty' first.");
                return;
            }

            var items = catalog.Items.Where(i => i != null).Concat(pills).Distinct().OrderBy(i => i.itemId).ToList();
            var bag = catalog.StarterBag.ToList();
            foreach (var pill in pills)
                if (!bag.Any(s => s.item == pill)) bag.Add(new StarterItem { item = pill, count = StarterCount });

            catalog.SetContents(items, catalog.StarterEquipped.ToList(), bag);
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Pills] {pills.Count} pills registered in {CatalogPath} (starter bag: {StarterCount} of each).");
        }

        private static ItemDefinition Pill(ushort id, string asset, string displayName, ItemRarity rarity, string glyph,
            string description, float health, float chi)
        {
            string path = $"{PillFolder}/{asset}.asset";
            var def = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
            if (def != null) return def;

            def = ScriptableObject.CreateInstance<ItemDefinition>();
            def.itemId = id;
            def.displayName = displayName;
            def.description = description;
            def.rarity = rarity;
            def.equipSlot = EquipSlot.None;
            def.maxStack = PillStack;
            def.restoreHealth = health;
            def.restoreChi = chi;
            def.placeholderGlyph = glyph;
            AssetDatabase.CreateAsset(def, path);
            return def;
        }
    }
}
