using System;
using System.Collections.Generic;
using KryticalNova.Progression;
using UnityEngine;

namespace KryticalNova.Items
{
    /// <summary>
    /// Every number behind gear stats: the level bands, the base formula (item level x slot slope x rarity
    /// multiplier), the ladder of secondary stats, and explicit rows that override the formula (the owner's Epic
    /// 100-112 tables are stored that way, word for word). The baker reads this once per item at edit time; nothing
    /// here is evaluated while the game runs. See _Docs/Reference/12Sky/02_Gear_System_Spec.md section 3.
    /// </summary>
    [CreateAssetMenu(menuName = "Krytical Nova/Items/Gear Stat Table")]
    public sealed class GearStatTable : ScriptableObject
    {
        /// <summary>A run of level tiers of one rarity that share one look per line.</summary>
        [Serializable]
        public struct Band
        {
            public string name;
            [Tooltip("One letter used in baked asset names (C, U, R, E).")]
            public string code;
            public ItemRarity rarity;
            [Tooltip("The item levels of this band, lowest first. One item is baked per level per line.")]
            public byte[] levels;
        }

        /// <summary>A secondary stat that grows linearly between two levels of one rarity on one slot.</summary>
        [Serializable]
        public struct LadderRow
        {
            public ItemRarity rarity;
            public EquipSlot slot;
            public StatType stat;
            public byte fromLevel;
            public float fromValue;
            public byte toLevel;
            public float toValue;
        }

        /// <summary>An exact value for one stat of one tier. Wins over the formula and the ladder.</summary>
        [Serializable]
        public struct Override
        {
            public ItemRarity rarity;
            public EquipSlot slot;
            public byte level;
            public StatType stat;
            public float value;
        }

        /// <summary>Bonus skill levels on a slot over a level range of one rarity.</summary>
        [Serializable]
        public struct SkillRow
        {
            public ItemRarity rarity;
            public EquipSlot slot;
            public byte fromLevel;
            public byte toLevel;
            public byte levels;
        }

        [Header("Bands (index = GearDictionary line band)")]
        public Band[] bands = Array.Empty<Band>();

        [Header("Rarity multipliers (Common, Uncommon, Rare, Epic, Legendary)")]
        public float[] rarityMultipliers = { 1f, 1.2f, 1.45f, 1.7f, 2f };

        [Header("Slopes per item level")]
        [Tooltip("Weapon Attack Power per level (x rarity).")]
        public float weaponAttackPerLevel = 5.2f;
        [Tooltip("Armor Defense per level (x rarity).")]
        public float armorDefensePerLevel = 2.1f;
        [Tooltip("Gloves, Boots, Ring, Amulet, Cape Defense per level (x rarity).")]
        public float secondaryDefensePerLevel = 0.55f;
        [Tooltip("Armor and Boots Evasion per level (not scaled by rarity).")]
        public float evasionPerLevel = 0.35f;
        [Tooltip("Evasion appears on Armor and Boots from this level.")]
        public byte evasionFromLevel = 5;
        [Tooltip("Weapon Hit Rate per level (not scaled by rarity).")]
        public float hitRatePerLevel = 2.5f;
        [Tooltip("Gloves carry this share of the weapon's Hit Rate.")]
        [Range(0f, 1f)] public float glovesHitShare = 0.4f;

        [Header("Secondary stats")]
        public LadderRow[] ladder = Array.Empty<LadderRow>();
        public SkillRow[] skillLevels = Array.Empty<SkillRow>();

        [Header("Exact values (owner's tables)")]
        public Override[] overrides = Array.Empty<Override>();

        public float MultiplierOf(ItemRarity rarity)
        {
            int index = (int)rarity;
            return rarityMultipliers != null && index >= 0 && index < rarityMultipliers.Length ? rarityMultipliers[index] : 1f;
        }

        public bool TryGetBand(int index, out Band band)
        {
            band = default;
            if (bands == null || index < 0 || index >= bands.Length) return false;
            band = bands[index];
            return band.levels != null && band.levels.Length > 0;
        }

        /// <summary>The highest item level any band reaches.</summary>
        public int MaxItemLevel
        {
            get
            {
                int max = 0;
                if (bands == null) return 0;
                foreach (var band in bands)
                    if (band.levels != null)
                        foreach (byte level in band.levels) max = Mathf.Max(max, level);
                return max;
            }
        }

        /// <summary>
        /// Fills the primary and secondary stats of one tier. Primary = what Tempering scales later: Attack Power on
        /// a weapon, Defense on everything else. Lists are cleared first.
        /// </summary>
        public void Resolve(EquipSlot slot, ItemRarity rarity, int level, List<ItemStatModifier> primary,
            List<ItemStatModifier> secondary, out byte skillBonus)
        {
            primary.Clear();
            secondary.Clear();
            skillBonus = 0;
            float multiplier = MultiplierOf(rarity);

            switch (slot)
            {
                case EquipSlot.Weapon:
                    Add(primary, slot, rarity, level, StatType.AttackPower, level * weaponAttackPerLevel * multiplier);
                    Add(secondary, slot, rarity, level, StatType.HitRate, level * hitRatePerLevel);
                    break;
                case EquipSlot.Armor:
                    Add(primary, slot, rarity, level, StatType.DefensePower, level * armorDefensePerLevel * multiplier);
                    AddEvasion(secondary, slot, rarity, level);
                    break;
                case EquipSlot.Gloves:
                    Add(primary, slot, rarity, level, StatType.DefensePower, level * secondaryDefensePerLevel * multiplier);
                    Add(secondary, slot, rarity, level, StatType.HitRate, level * hitRatePerLevel * glovesHitShare);
                    break;
                case EquipSlot.Boots:
                    Add(primary, slot, rarity, level, StatType.DefensePower, level * secondaryDefensePerLevel * multiplier);
                    AddEvasion(secondary, slot, rarity, level);
                    break;
                case EquipSlot.Ring:
                case EquipSlot.Amulet:
                case EquipSlot.Cape:
                    Add(primary, slot, rarity, level, StatType.DefensePower, level * secondaryDefensePerLevel * multiplier);
                    break;
                default:
                    return;
            }

            if (ladder != null)
            {
                foreach (var row in ladder)
                {
                    if (row.rarity != rarity || row.slot != slot || level < row.fromLevel || level > row.toLevel) continue;
                    if (Contains(primary, row.stat) || Contains(secondary, row.stat)) continue;
                    float t = row.toLevel > row.fromLevel ? (level - row.fromLevel) / (float)(row.toLevel - row.fromLevel) : 0f;
                    Add(secondary, slot, rarity, level, row.stat, Mathf.Lerp(row.fromValue, row.toValue, t));
                }
            }

            // Exact rows for stats neither the formula nor the ladder produced.
            if (overrides != null)
            {
                foreach (var row in overrides)
                {
                    if (row.rarity != rarity || row.slot != slot || row.level != level) continue;
                    if (Contains(primary, row.stat) || Contains(secondary, row.stat)) continue;
                    secondary.Add(new ItemStatModifier { stat = row.stat, modType = ModifierType.Flat, value = row.value });
                }
            }

            if (skillLevels != null)
            {
                foreach (var row in skillLevels)
                    if (row.rarity == rarity && row.slot == slot && level >= row.fromLevel && level <= row.toLevel)
                        skillBonus = (byte)Mathf.Max(skillBonus, row.levels);
            }
        }

        private void AddEvasion(List<ItemStatModifier> list, EquipSlot slot, ItemRarity rarity, int level)
        {
            if (level < evasionFromLevel && !HasOverride(slot, rarity, level, StatType.EvasionRate)) return;
            Add(list, slot, rarity, level, StatType.EvasionRate, level * evasionPerLevel);
        }

        private void Add(List<ItemStatModifier> list, EquipSlot slot, ItemRarity rarity, int level, StatType stat, float formula)
        {
            float value = TryOverride(slot, rarity, level, stat, out float exact) ? exact : Round(formula);
            if (value <= 0f) return;
            list.Add(new ItemStatModifier { stat = stat, modType = ModifierType.Flat, value = value });
        }

        private bool HasOverride(EquipSlot slot, ItemRarity rarity, int level, StatType stat) =>
            TryOverride(slot, rarity, level, stat, out _);

        private bool TryOverride(EquipSlot slot, ItemRarity rarity, int level, StatType stat, out float value)
        {
            value = 0f;
            if (overrides == null) return false;
            foreach (var row in overrides)
            {
                if (row.rarity != rarity || row.slot != slot || row.level != level || row.stat != stat) continue;
                value = row.value;
                return true;
            }
            return false;
        }

        private static bool Contains(List<ItemStatModifier> list, StatType stat)
        {
            foreach (var m in list) if (m.stat == stat) return true;
            return false;
        }

        /// <summary>Whole numbers, halves rounded up (257.5 becomes 258), so a tooltip never shows a fraction.</summary>
        private static float Round(float value) => Mathf.Floor(value + 0.5f);
    }
}
