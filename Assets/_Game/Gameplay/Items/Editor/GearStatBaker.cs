using System.Collections.Generic;
using System.Linq;
using KryticalNova.Items;
using KryticalNova.Items.Visuals;
using KryticalNova.Progression;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Bakes the gear ladder: every line of the gear dictionary becomes one item per level tier of its band, with
    /// stats from the <see cref="GearStatTable"/> and the look of the line's template. Also rebuilds the dictionary's
    /// item rows and the item catalog. Re-runnable: item IDs are derived from the line ID and never change, baked
    /// items are overwritten, and baked items whose line or tier is gone are removed.
    /// </summary>
    public static class GearStatBaker
    {
        public const string GearRoot = SidekickGearGenerator.ItemsRoot + "/Gear";
        public const string StatTablePath = SidekickGearGenerator.ItemsRoot + "/GearStatTable.asset";
        private const string ConsumableFolder = SidekickGearGenerator.ItemsRoot + "/Consumables";
        private const int StarterStims = 5; // of each kind: ten Stims in a new character's Pack

        [MenuItem("Krytical Nova/Items/6. Bake Gear Ladder")]
        public static void Bake()
        {
            var dictionary = AssetDatabase.LoadAssetAtPath<GearDictionary>(SidekickGearGenerator.DictionaryPath);
            if (dictionary == null || dictionary.Lines.Count == 0)
            {
                Debug.LogError("[Items] The gear dictionary has no lines. Run 'Krytical Nova/Items/1. Generate Gear Looks From Synty' first.");
                return;
            }
            var table = LoadOrCreateTable();
            EnsureStatDefinitions();
            var partCatalog = dictionary.Parts != null
                ? dictionary.Parts
                : AssetDatabase.LoadAssetAtPath<SidekickPartCatalog>(SidekickPartCatalogBaker.CatalogPath);

            var entries = new List<GearDictionary.Entry>();
            var baked = new List<ItemDefinition>();
            var written = new HashSet<string>();
            var ids = new HashSet<ushort>();
            var primary = new List<ItemStatModifier>();
            var secondary = new List<ItemStatModifier>();
            int createdCount = 0, skipped = 0;

            // Folders first: inside an asset-editing batch a folder made a moment ago is not visible yet.
            WeatherVfxBuilder.EnsureFolder(GearRoot);
            foreach (var line in dictionary.Lines)
                if (line.IsValid && !string.IsNullOrEmpty(line.tag)) WeatherVfxBuilder.EnsureFolder($"{GearRoot}/{line.tag}");

            // Over a thousand small assets: one import pass for all of them instead of one each.
            AssetDatabase.StartAssetEditing();
            try
            {
            foreach (var line in dictionary.Lines)
            {
                if (!line.IsValid) continue;
                if (line.look == null) { Debug.LogWarning($"[Items] Line {line.lineId} ({line.displayName}) has no look: skipped.", dictionary); skipped++; continue; }
                if (!table.TryGetBand(line.band, out var band)) { Debug.LogWarning($"[Items] Line {line.lineId} ({line.displayName}) names band {line.band}, which the stat table does not have: skipped.", dictionary); skipped++; continue; }
                if (band.levels.Length > GearDictionary.IdsPerLine) { Debug.LogError($"[Items] Band '{band.name}' has more than {GearDictionary.IdsPerLine} levels.", table); skipped++; continue; }

                var look = line.look;
                string folder = $"{GearRoot}/{line.tag}";
                byte variant = 0;
                if (partCatalog != null && look.appearancePartIds != null)
                    foreach (ushort partId in look.appearancePartIds)
                        if (partCatalog.TryGet(partId, out var part)) { variant = part.variant; break; }

                for (int tier = 0; tier < band.levels.Length; tier++)
                {
                    byte level = band.levels[tier];
                    ushort itemId = (ushort)(line.FirstItemId + tier);
                    if (!ids.Add(itemId)) { Debug.LogError($"[Items] Item ID {itemId} is produced twice (line {line.lineId}). Two lines share an ID.", dictionary); continue; }

                    string path = $"{folder}/{SidekickGearGenerator.AssetPrefix(line.slot)}_{line.tag}_{band.code}{level:00}.asset";
                    var def = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
                    if (def == null)
                    {
                        def = ScriptableObject.CreateInstance<ItemDefinition>();
                        AssetDatabase.CreateAsset(def, path);
                        createdCount++;
                    }
                    written.Add(path);

                    table.Resolve(line.slot, line.rarity, level, primary, secondary, out byte skillBonus);

                    def.itemId = itemId;
                    def.displayName = line.displayName;
                    def.description = line.description;
                    def.icon = look.icon;
                    def.rarity = line.rarity;
                    def.equipSlot = line.slot;
                    def.maxStack = 1;
                    def.itemLevel = level;
                    def.lineId = line.lineId;
                    def.weaponClass = line.slot == EquipSlot.Weapon ? line.weaponClass : WeaponClassId.None;
                    def.baseStats = primary.ToArray();
                    def.secondaryStats = secondary.ToArray();
                    def.skillModifiers = skillBonus > 0
                        ? new[] { new ItemSkillModifier { line = def.weaponClass, levels = skillBonus } }
                        : System.Array.Empty<ItemSkillModifier>();
                    def.statModifiers = new List<ItemStatModifier>();
                    def.restoreHealth = 0f;
                    def.restoreChi = 0f;
                    def.usableFromHotbar = false;
                    // The look: copied, so the game never follows a reference to a template.
                    def.partCatalog = look.partCatalog;
                    def.appearancePartIds = look.appearancePartIds != null ? (ushort[])look.appearancePartIds.Clone() : System.Array.Empty<ushort>();
                    def.appearanceParts = new List<SidekickPartRef>();
                    def.weaponPrefab = look.weaponPrefab;
                    def.weaponLocalPosition = look.weaponLocalPosition;
                    def.weaponLocalEuler = look.weaponLocalEuler;
                    def.weaponLocalScale = look.weaponLocalScale;
                    def.hitboxCenter = look.hitboxCenter;
                    def.hitboxSize = look.hitboxSize;
                    def.petPrefab = null;
                    EditorUtility.SetDirty(def);
                    baked.Add(def);

                    entries.Add(new GearDictionary.Entry
                    {
                        itemId = itemId, name = line.displayName, slot = line.slot, rarity = line.rarity,
                        factions = line.factions, bodies = line.bodies, pack = line.pack ?? string.Empty, variant = variant,
                        partIds = def.appearancePartIds, itemLevel = level, lineId = line.lineId, weaponClass = def.weaponClass
                    });
                }
            }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            entries.Sort((a, b) => a.itemId.CompareTo(b.itemId));
            dictionary.SetContents(partCatalog, entries);
            EditorUtility.SetDirty(dictionary);

            int removed = RemoveStale(written);
            BuildCatalog(baked);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Items] Gear ladder baked: {baked.Count} items from {dictionary.Lines.Count} lines ({createdCount} new, {removed} removed" +
                      $"{(skipped > 0 ? $", {skipped} lines skipped" : string.Empty)}). Highest item level {table.MaxItemLevel}.");
        }

        /// <summary>Baked items whose line or tier no longer exists. They are generated output, so they go.</summary>
        private static int RemoveStale(HashSet<string> written)
        {
            if (!AssetDatabase.IsValidFolder(GearRoot)) return 0;
            int removed = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:ItemDefinition", new[] { GearRoot }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (written.Contains(path)) continue;
                if (AssetDatabase.DeleteAsset(path)) removed++;
            }
            return removed;
        }

        /// <summary>
        /// The catalog holds what a player can own: the baked ladder plus every item that is not a look template
        /// (consumables, pets, anything hand-made). The starter list is the Stims; gear comes from the starter kit.
        /// </summary>
        private static void BuildCatalog(List<ItemDefinition> baked)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>(SidekickGearGenerator.CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<ItemCatalog>();
                AssetDatabase.CreateAsset(catalog, SidekickGearGenerator.CatalogPath);
            }

            var items = new List<ItemDefinition>(baked);
            var seen = new HashSet<ItemDefinition>(baked);
            foreach (string guid in AssetDatabase.FindAssets("t:ItemDefinition", new[] { SidekickGearGenerator.ItemsRoot }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.StartsWith(GearRoot + "/") || SidekickGearGenerator.IsLookTemplatePath(path)) continue;
                var def = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
                if (def != null && def.itemId != 0 && seen.Add(def)) items.Add(def);
            }
            // Hand-made items kept elsewhere in the project stay in the catalog too.
            foreach (var def in catalog.Items)
            {
                if (def == null || def.itemId == 0 || seen.Contains(def)) continue;
                string path = AssetDatabase.GetAssetPath(def);
                if (path.StartsWith(GearRoot + "/") || SidekickGearGenerator.IsLookTemplatePath(path)) continue;
                if (seen.Add(def)) items.Add(def);
            }
            items = items.OrderBy(i => i.itemId).ToList();

            var bag = new List<StarterItem>();
            foreach (string asset in new[] { "Pill_Vitality", "Pill_Chi" })
            {
                var stim = AssetDatabase.LoadAssetAtPath<ItemDefinition>($"{ConsumableFolder}/{asset}.asset");
                if (stim != null) bag.Add(new StarterItem { item = stim, count = StarterStims });
            }
            catalog.SetContents(items, new List<ItemDefinition>(), bag);
            EditorUtility.SetDirty(catalog);
        }

        /// <summary>The look templates in use (one per look, whatever the number of tiers), for the icon capture.</summary>
        public static List<ItemDefinition> LookTemplates()
        {
            var result = new List<ItemDefinition>();
            var dictionary = AssetDatabase.LoadAssetAtPath<GearDictionary>(SidekickGearGenerator.DictionaryPath);
            if (dictionary == null) return result;
            var seen = new HashSet<ItemDefinition>();
            foreach (var line in dictionary.Lines)
                if (line.look != null && seen.Add(line.look)) result.Add(line.look);
            return result;
        }

        // ---------------------------------------------------------------- stat definitions

        private const string StatDefinitionFolder = "Assets/_Game/Gameplay/Progression/Data/Assets/StatDefinitions";

        /// <summary>The designer metadata asset of each stat only gear grants. Existing assets are left as they are.</summary>
        private static void EnsureStatDefinitions()
        {
            Define(StatType.AttackSpeedPercent, "ASPD", "Percentage added to attack speed. Granted by Tuning.");
            Define(StatType.CoreResistancePercent, "CORE RES", "Reduces the elemental damage an enemy's Cores add.");
            Define(StatType.KnockbackResistancePercent, "KB RES", "Chance to stand firm against a knockback.");
            Define(StatType.CriticalDefensePercent, "CRIT DEF", "Reduces the bonus damage of an incoming critical hit.");
            Define(StatType.KillExpRatePercent, "XP RATE", "Bonus to the rate XP is earned from kills.");
            Define(StatType.KillExpAmountPercent, "XP AMT", "Bonus to the XP a kill is worth.");
        }

        private static void Define(StatType stat, string abbreviation, string description)
        {
            string path = $"{StatDefinitionFolder}/Stat_{stat}.asset";
            if (AssetDatabase.LoadAssetAtPath<StatDefinitionData>(path) != null) return;
            WeatherVfxBuilder.EnsureFolder(StatDefinitionFolder);
            var definition = ScriptableObject.CreateInstance<StatDefinitionData>();
            definition.statType = stat;
            definition.statName = StatDisplay.Name(stat);
            definition.abbreviation = abbreviation;
            definition.description = description;
            definition.isPercentage = true;
            definition.maxValue = 1000f;
            AssetDatabase.CreateAsset(definition, path);
        }

        // ---------------------------------------------------------------- the stat table

        public static GearStatTable LoadOrCreateTable()
        {
            var table = AssetDatabase.LoadAssetAtPath<GearStatTable>(StatTablePath);
            if (table != null) return table;
            table = ScriptableObject.CreateInstance<GearStatTable>();
            SeedDefaults(table);
            AssetDatabase.CreateAsset(table, StatTablePath);
            return table;
        }

        [MenuItem("Krytical Nova/Items/Reset Gear Stat Table To Defaults")]
        public static void ResetTable()
        {
            if (!EditorUtility.DisplayDialog("Gear Stat Table", "Replace every value in GearStatTable.asset with the defaults from the gear spec?", "Reset", "Cancel")) return;
            var table = LoadOrCreateTable();
            SeedDefaults(table);
            EditorUtility.SetDirty(table);
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// The gear spec's numbers (_Docs/Reference/12Sky/02_Gear_System_Spec.md sections 2 and 3). The Epic rows are
        /// the owner's tables, word for word; the Uncommon and Rare secondaries are proposals from the spec's milestones.
        /// </summary>
        public static void SeedDefaults(GearStatTable table)
        {
            table.bands = new[]
            {
                new GearStatTable.Band { name = "Common", code = "C", rarity = ItemRarity.Common, levels = new byte[] { 1, 4, 7, 10, 13, 16, 19, 22, 25, 28 } },
                new GearStatTable.Band { name = "Uncommon", code = "U", rarity = ItemRarity.Uncommon, levels = new byte[] { 20, 25, 30, 35, 40 } },
                new GearStatTable.Band { name = "Rare I", code = "R", rarity = ItemRarity.Rare, levels = new byte[] { 45, 55, 65, 75 } },
                new GearStatTable.Band { name = "Rare II", code = "R", rarity = ItemRarity.Rare, levels = new byte[] { 85, 95, 105 } },
                new GearStatTable.Band { name = "Epic", code = "E", rarity = ItemRarity.Epic, levels = new byte[] { 100, 103, 106, 109, 112 } },
            };
            table.rarityMultipliers = new[] { 1f, 1.2f, 1.45f, 1.7f, 2f };
            table.weaponAttackPerLevel = 5.2f;
            table.armorDefensePerLevel = 2.1f;
            table.secondaryDefensePerLevel = 0.55f;
            table.evasionPerLevel = 0.35f;
            table.evasionFromLevel = 5;
            table.hitRatePerLevel = 2.5f;
            table.glovesHitShare = 0.4f;

            var ladder = new List<GearStatTable.LadderRow>();
            void Row(ItemRarity rarity, EquipSlot slot, StatType stat, int from, float fromValue, int to, float toValue) =>
                ladder.Add(new GearStatTable.LadderRow
                {
                    rarity = rarity, slot = slot, stat = stat, fromLevel = (byte)from, fromValue = fromValue, toLevel = (byte)to, toValue = toValue
                });

            const ItemRarity U = ItemRarity.Uncommon, R = ItemRarity.Rare, E = ItemRarity.Epic;
            // Uncommon 20-40: the first elemental stat, Health on armor, +5 attributes and the first utility % on accessories.
            Row(U, EquipSlot.Weapon, StatType.ElementalAttack, 20, 20, 40, 40);
            Row(U, EquipSlot.Armor, StatType.MaxHP, 20, 10, 40, 25);
            Row(U, EquipSlot.Armor, StatType.ElementalDefense, 20, 8, 40, 16);
            Row(U, EquipSlot.Gloves, StatType.ElementalAttack, 20, 5, 40, 10);
            Row(U, EquipSlot.Ring, StatType.Agility, 20, 5, 40, 8);
            Row(U, EquipSlot.Ring, StatType.CriticalDamagePercent, 20, 13, 40, 13);
            Row(U, EquipSlot.Amulet, StatType.Spirit, 20, 5, 40, 8);
            Row(U, EquipSlot.Amulet, StatType.KillExpRatePercent, 20, 40, 40, 40);
            // Rare 45-105: from the spec's level-45 and level-55 milestones up to just under the Epic tables.
            Row(R, EquipSlot.Weapon, StatType.ElementalAttack, 45, 67, 105, 115);
            Row(R, EquipSlot.Armor, StatType.MaxHP, 45, 32, 105, 76);
            Row(R, EquipSlot.Armor, StatType.ElementalDefense, 45, 22, 105, 48);
            Row(R, EquipSlot.Gloves, StatType.ElementalAttack, 45, 15, 105, 28);
            Row(R, EquipSlot.Boots, StatType.MoveSpeedPercent, 45, 10, 105, 28);
            Row(R, EquipSlot.Ring, StatType.Agility, 45, 11, 105, 23);
            Row(R, EquipSlot.Ring, StatType.ElementalAttack, 45, 55, 105, 95);
            Row(R, EquipSlot.Ring, StatType.CriticalDamagePercent, 45, 15, 105, 17);
            Row(R, EquipSlot.Amulet, StatType.Spirit, 45, 11, 105, 23);
            Row(R, EquipSlot.Amulet, StatType.ElementalDefense, 45, 40, 105, 72);
            Row(R, EquipSlot.Amulet, StatType.KillExpRatePercent, 45, 44, 105, 49);
            Row(R, EquipSlot.Amulet, StatType.KillExpAmountPercent, 45, 80, 55, 100);
            Row(R, EquipSlot.Amulet, StatType.KillExpAmountPercent, 55, 100, 105, 100);
            Row(R, EquipSlot.Cape, StatType.CriticalDefensePercent, 45, 10, 105, 24);
            // Epic: what the owner's tables do not list.
            Row(E, EquipSlot.Boots, StatType.MoveSpeedPercent, 100, 30, 112, 40);
            Row(E, EquipSlot.Cape, StatType.CriticalDefensePercent, 100, 26, 112, 28);
            table.ladder = ladder.ToArray();

            var skills = new List<GearStatTable.SkillRow>();
            void Skill(ItemRarity rarity, EquipSlot slot, int from, int to, int levels) =>
                skills.Add(new GearStatTable.SkillRow { rarity = rarity, slot = slot, fromLevel = (byte)from, toLevel = (byte)to, levels = (byte)levels });
            Skill(U, EquipSlot.Weapon, 20, 40, 1);
            foreach (var slot in new[] { EquipSlot.Weapon, EquipSlot.Gloves, EquipSlot.Boots, EquipSlot.Ring })
            {
                Skill(R, slot, 45, 45, 2);
                Skill(R, slot, 55, 75, 3);
                Skill(R, slot, 85, 105, 4);
                Skill(E, slot, 100, 106, 4);
                Skill(E, slot, 109, 112, 5);
            }
            table.skillLevels = skills.ToArray();

            var overrides = new List<GearStatTable.Override>();
            int[] epicLevels = { 100, 103, 106, 109, 112 };
            void Epic(EquipSlot slot, StatType stat, params float[] values)
            {
                for (int i = 0; i < epicLevels.Length && i < values.Length; i++)
                    overrides.Add(new GearStatTable.Override { rarity = E, slot = slot, level = (byte)epicLevels[i], stat = stat, value = values[i] });
            }
            Epic(EquipSlot.Weapon, StatType.AttackPower, 884, 910, 937, 963, 990);
            Epic(EquipSlot.Weapon, StatType.HitRate, 250, 258, 265, 273, 280);
            Epic(EquipSlot.Weapon, StatType.ElementalAttack, 120, 128, 135, 143, 150);
            Epic(EquipSlot.Armor, StatType.DefensePower, 357, 368, 378, 389, 400);
            Epic(EquipSlot.Armor, StatType.EvasionRate, 35, 36, 37, 38, 39);
            Epic(EquipSlot.Armor, StatType.MaxHP, 80, 85, 90, 95, 100);
            Epic(EquipSlot.Armor, StatType.ElementalDefense, 50, 52, 55, 58, 60);
            Epic(EquipSlot.Gloves, StatType.DefensePower, 94, 97, 100, 102, 105);
            Epic(EquipSlot.Gloves, StatType.HitRate, 100, 105, 110, 115, 120);
            Epic(EquipSlot.Gloves, StatType.ElementalAttack, 30, 32, 35, 37, 40);
            Epic(EquipSlot.Boots, StatType.DefensePower, 94, 97, 100, 102, 105);
            Epic(EquipSlot.Boots, StatType.EvasionRate, 35, 36, 37, 38, 39);
            Epic(EquipSlot.Ring, StatType.Agility, 24, 25, 26, 27, 28);
            Epic(EquipSlot.Ring, StatType.ElementalAttack, 100, 106, 112, 118, 125);
            Epic(EquipSlot.Ring, StatType.CriticalDamagePercent, 18, 18, 19, 19, 20);
            Epic(EquipSlot.Amulet, StatType.Spirit, 24, 25, 26, 27, 28);
            Epic(EquipSlot.Amulet, StatType.ElementalDefense, 75, 78, 82, 86, 90);
            Epic(EquipSlot.Amulet, StatType.KillExpRatePercent, 50, 52, 54, 56, 60);
            Epic(EquipSlot.Amulet, StatType.KillExpAmountPercent, 100, 105, 110, 115, 120);
            table.overrides = overrides.ToArray();
        }
    }
}
