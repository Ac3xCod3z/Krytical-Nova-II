using System;
using KryticalNova.Progression;
using UnityEngine;

namespace KryticalNova.Items
{
    /// <summary>How a progression bonus is worked out from its number.</summary>
    public enum GearEffectKind : byte
    {
        /// <summary>A percentage of what the item itself gives to that stat (its baked value).</summary>
        PercentOfItemStat,
        /// <summary>The number as it is.</summary>
        Flat,
        /// <summary>The number times the item's rarity multiplier.</summary>
        FlatTimesRarity,
        /// <summary>A percentage of the wearer's own stat (Max HP +1.5 %).</summary>
        PercentOfWearer,
    }

    /// <summary>One bonus a progression step gives a slot. What "per step" means is up to the table it sits in.</summary>
    [Serializable]
    public struct GearSlotEffect
    {
        public EquipSlot slot;
        public StatType stat;
        public GearEffectKind kind;
        public float value;

        public GearSlotEffect(EquipSlot slot, StatType stat, GearEffectKind kind, float value)
        {
            this.slot = slot; this.stat = stat; this.kind = kind; this.value = value;
        }
    }

    /// <summary>An Alloy: the item spent on a Tempering attempt, and the Temper % it adds when the attempt succeeds.</summary>
    [Serializable]
    public struct AlloyGrade
    {
        public ushort itemId;
        [Min(1)] public int step;
        [Tooltip("The only kind that works at or above the soft cap.")]
        public bool prime;
    }

    /// <summary>How likely a failed attempt is to break the item, across one stretch of Temper %.</summary>
    [Serializable]
    public struct TemperBreakBand
    {
        [Tooltip("Applies when the item's Temper % before the attempt is within these two (inclusive).")]
        public int from, to;
        [Tooltip("Break chance in percent at the start and at the end of the stretch; in between it rises in a line.")]
        [Range(0f, 100f)] public float chanceFrom, chanceTo;
    }

    /// <summary>The glow colour an item takes from this Temper % up to the next band.</summary>
    [Serializable]
    public struct TemperGlowBand
    {
        public int from;
        public string name;
        [ColorUsage(false, false)] public Color color;
    }

    /// <summary>How strongly a rarity shows its glow. The colour is the band's; only the strength differs.</summary>
    [Serializable]
    public struct TemperGlowLook
    {
        [Tooltip("Brightness of the rim. Above 1 it blooms.")]
        [Min(0f)] public float intensity;
        [Tooltip("How tight the glow hugs the silhouette (higher = thinner rim).")]
        [Range(0.5f, 8f)] public float rimPower;
        [Tooltip("How much the brightness breathes, as a share of the intensity.")]
        [Range(0f, 1f)] public float pulse;
        [Tooltip("Breaths per second.")]
        [Min(0f)] public float pulseSpeed;
        [Tooltip("Sparkles ride on the weapon.")]
        public bool sparkles;
    }

    [Serializable]
    public sealed class TemperSettings
    {
        [Header("Materials")]
        public AlloyGrade[] alloys =
        {
            new AlloyGrade { itemId = GearMaterialIds.Alumite, step = 3 },
            new AlloyGrade { itemId = GearMaterialIds.Iridion, step = 6 },
            new AlloyGrade { itemId = GearMaterialIds.Orichalc, step = 9 },
            new AlloyGrade { itemId = GearMaterialIds.Eversteel, step = 12 },
            new AlloyGrade { itemId = GearMaterialIds.TrinityPlatinum, step = 15 },
            new AlloyGrade { itemId = GearMaterialIds.PrimeAlloy, step = 3, prime = true },
        };
        [Tooltip("Stops one break. Only used up when it does.")]
        public ushort failsafeItemId = GearMaterialIds.Failsafe;
        [Tooltip("A fail costs nothing and never breaks. Used up on a fail.")]
        public ushort advancedFailsafeItemId = GearMaterialIds.AdvancedFailsafe;
        [Tooltip("Adds to the success chance of one attempt and stops a break on it. Used up on the attempt.")]
        public ushort luckyFailsafeItemId = GearMaterialIds.LuckyFailsafe;
        [Range(0f, 50f)] public float luckyFailsafeBonus = 5f;

        [Header("Success chance = base - (current % + alloy step) + Luck bonus, clamped")]
        public float baseChance = 103f;
        [Range(0f, 100f)] public float minChance = 5f;
        [Range(0f, 100f)] public float maxChance = 100f;
        [Tooltip("Luck needed for one extra percent of success. 0 = Luck does nothing.")]
        [Min(0f)] public float luckPerPercent = 100f;
        [Tooltip("The most Luck can add, in percent.")]
        [Range(0f, 100f)] public float maxLuckBonus = 25f;

        [Header("Limits")]
        [Tooltip("Temper % lost on a failed attempt.")]
        [Min(0)] public int failLoss = 3;
        [Tooltip("At or above this only Prime Alloy works.")]
        [Min(1)] public int softCap = 120;
        [Min(1)] public int hardCap = 150;
        [Tooltip("At or above this an attempt needs an Advanced Failsafe.")]
        [Min(1)] public int advancedFailsafeFrom = 133;

        [Header("Break chance on a fail")]
        public TemperBreakBand[] breakBands =
        {
            new TemperBreakBand { from = 60, to = 89, chanceFrom = 5f, chanceTo = 15f },
            new TemperBreakBand { from = 90, to = 119, chanceFrom = 15f, chanceTo = 30f },
            new TemperBreakBand { from = 120, to = 132, chanceFrom = 35f, chanceTo = 35f },
            new TemperBreakBand { from = 133, to = 150, chanceFrom = 50f, chanceTo = 50f },
        };

        [Header("Cost per attempt = item level x this x rarity multiplier")]
        [Min(0)] public long credsPerItemLevel = 500;
        [Tooltip("Applied at or above the soft cap.")]
        [Min(1f)] public float softCapCostMultiplier = 2f;

        [Header("What 1 % of Temper gives, by slot")]
        public GearSlotEffect[] effects =
        {
            new GearSlotEffect(EquipSlot.Weapon, StatType.AttackPower, GearEffectKind.PercentOfItemStat, 1f),
            new GearSlotEffect(EquipSlot.Armor, StatType.DefensePower, GearEffectKind.PercentOfItemStat, 1f),
            new GearSlotEffect(EquipSlot.Cape, StatType.DefensePower, GearEffectKind.PercentOfItemStat, 1f),
            new GearSlotEffect(EquipSlot.Gloves, StatType.HitRate, GearEffectKind.PercentOfItemStat, 1f),
            new GearSlotEffect(EquipSlot.Boots, StatType.EvasionRate, GearEffectKind.PercentOfItemStat, 1f),
            new GearSlotEffect(EquipSlot.Ring, StatType.CriticalRate, GearEffectKind.Flat, 0.05f),
            new GearSlotEffect(EquipSlot.Amulet, StatType.Luck, GearEffectKind.FlatTimesRarity, 4f),
        };

        [Header("Glow")]
        public TemperGlowBand[] glowBands =
        {
            new TemperGlowBand { from = 1, name = "Green", color = new Color(0.30f, 1.00f, 0.42f) },
            new TemperGlowBand { from = 30, name = "Yellow", color = new Color(1.00f, 0.86f, 0.24f) },
            new TemperGlowBand { from = 60, name = "Blue", color = new Color(0.28f, 0.62f, 1.00f) },
            new TemperGlowBand { from = 90, name = "Purple", color = new Color(0.72f, 0.36f, 1.00f) },
            new TemperGlowBand { from = 120, name = "Orange", color = new Color(1.00f, 0.52f, 0.12f) },
        };
        [Tooltip("By rarity: Common, Uncommon, Rare, Epic, Legendary. Same colours; an Epic shows them brighter.")]
        public TemperGlowLook[] glowLooks =
        {
            new TemperGlowLook { intensity = 0.55f, rimPower = 3.2f, pulse = 0.10f, pulseSpeed = 0.5f },
            new TemperGlowLook { intensity = 0.65f, rimPower = 3.0f, pulse = 0.12f, pulseSpeed = 0.5f },
            new TemperGlowLook { intensity = 0.80f, rimPower = 2.8f, pulse = 0.15f, pulseSpeed = 0.6f },
            new TemperGlowLook { intensity = 2.60f, rimPower = 1.9f, pulse = 0.35f, pulseSpeed = 1.1f, sparkles = true },
            new TemperGlowLook { intensity = 3.00f, rimPower = 1.8f, pulse = 0.40f, pulseSpeed = 1.2f, sparkles = true },
        };
    }

    [Serializable]
    public sealed class FusionSettings
    {
        [Range(1, 15)] public int maxStage = 12;
        public ushort catalystItemId = GearMaterialIds.FusionCatalyst;

        [Header("What the target needs")]
        [Tooltip("Temper % a Rare must have to be fused.")]
        [Min(0)] public int minTemperRare = 12;
        [Tooltip("Temper % an Epic must have to be fused.")]
        [Min(0)] public int minTemperEpic = 132;
        [Tooltip("Temper % a successful Fusion takes off the target.")]
        [Min(0)] public int temperCostOnSuccess = 12;

        [Header("Cost = first + per stage x (stage - 1)")]
        [Min(0)] public long firstStageCreds = 100_000;
        [Min(0)] public long credsPerStage = 50_000;

        [Header("Success chance")]
        [Tooltip("Stages that never fail.")]
        [Min(0)] public int certainStages = 3;
        [Tooltip("Percent lost per stage after those.")]
        [Range(0f, 50f)] public float chanceLossPerStage = 7f;
        [Range(0f, 100f)] public float minChance = 5f;
        [Min(0f)] public float luckPerPercent = 100f;
        [Range(0f, 100f)] public float maxLuckBonus = 25f;
        [Tooltip("A failed Fusion destroys the material. Off: a fail costs only the Catalyst and the Creds.")]
        public bool failConsumesMaterial = true;

        [Header("What one stage gives, by slot")]
        public GearSlotEffect[] effects =
        {
            new GearSlotEffect(EquipSlot.Weapon, StatType.AttackPower, GearEffectKind.PercentOfItemStat, 3f),
            new GearSlotEffect(EquipSlot.Weapon, StatType.HitRate, GearEffectKind.PercentOfItemStat, 2f),
            new GearSlotEffect(EquipSlot.Armor, StatType.DefensePower, GearEffectKind.PercentOfItemStat, 3f),
            new GearSlotEffect(EquipSlot.Armor, StatType.EvasionRate, GearEffectKind.PercentOfItemStat, 2f),
            new GearSlotEffect(EquipSlot.Armor, StatType.MaxHP, GearEffectKind.PercentOfWearer, 1.5f),
            new GearSlotEffect(EquipSlot.Gloves, StatType.DefensePower, GearEffectKind.PercentOfItemStat, 3f),
            new GearSlotEffect(EquipSlot.Gloves, StatType.HitRate, GearEffectKind.PercentOfItemStat, 2f),
            new GearSlotEffect(EquipSlot.Boots, StatType.DefensePower, GearEffectKind.PercentOfItemStat, 3f),
            new GearSlotEffect(EquipSlot.Boots, StatType.EvasionRate, GearEffectKind.PercentOfItemStat, 2f),
            new GearSlotEffect(EquipSlot.Cape, StatType.DefensePower, GearEffectKind.PercentOfItemStat, 3f),
            new GearSlotEffect(EquipSlot.Ring, StatType.ElementalAttack, GearEffectKind.FlatTimesRarity, 8f),
            new GearSlotEffect(EquipSlot.Amulet, StatType.ElementalDefense, GearEffectKind.FlatTimesRarity, 8f),
        };
    }

    /// <summary>What an attempt costs over one run of Tuning stages.</summary>
    [Serializable]
    public struct TuningBracket
    {
        [Tooltip("The stages being tuned to (inclusive).")]
        public int fromStage, toStage;
        public ushort chipItemId;
        [Min(1)] public int chips;
        [Min(0)] public int renown;
        [Min(0)] public long creds;
    }

    [Serializable]
    public sealed class TuningSettings
    {
        [Range(1, 31)] public int maxStage = 25;
        public TuningBracket[] brackets =
        {
            new TuningBracket { fromStage = 1, toStage = 12, chipItemId = GearMaterialIds.TuningChip, chips = 1, renown = 10, creds = 50_000 },
            new TuningBracket { fromStage = 13, toStage = 20, chipItemId = GearMaterialIds.VioletChip, chips = 1, renown = 25, creds = 150_000 },
            new TuningBracket { fromStage = 21, toStage = 25, chipItemId = GearMaterialIds.VioletChip, chips = 2, renown = 50, creds = 400_000 },
        };
        [Tooltip("Returns an item to stage 0. Nothing is refunded.")]
        public ushort resetChipItemId = GearMaterialIds.ResetChip;

        [Header("Success chance")]
        [Tooltip("Stages that never fail.")]
        [Min(0)] public int certainStages = 10;
        [Range(0f, 50f)] public float chanceLossPerStage = 5f;
        [Range(0f, 100f)] public float minChance = 5f;
        [Tooltip("From this stage on a failed attempt drops the item one stage.")]
        [Min(1)] public int failDropsFromStage = 13;

        [Header("What one stage gives, by slot")]
        public GearSlotEffect[] effects =
        {
            new GearSlotEffect(EquipSlot.Weapon, StatType.CriticalRate, GearEffectKind.Flat, 1f),
            new GearSlotEffect(EquipSlot.Weapon, StatType.Spirit, GearEffectKind.Flat, 5f),
            new GearSlotEffect(EquipSlot.Armor, StatType.MaxHP, GearEffectKind.Flat, 400f),
            new GearSlotEffect(EquipSlot.Armor, StatType.Spirit, GearEffectKind.Flat, 5f),
            new GearSlotEffect(EquipSlot.Gloves, StatType.AttackSpeedPercent, GearEffectKind.Flat, 0.5f),
            new GearSlotEffect(EquipSlot.Gloves, StatType.Agility, GearEffectKind.Flat, 5f),
            new GearSlotEffect(EquipSlot.Boots, StatType.MoveSpeedPercent, GearEffectKind.Flat, 0.5f),
            new GearSlotEffect(EquipSlot.Boots, StatType.Agility, GearEffectKind.Flat, 5f),
            new GearSlotEffect(EquipSlot.Ring, StatType.CoreResistancePercent, GearEffectKind.Flat, 0.1f),
            new GearSlotEffect(EquipSlot.Ring, StatType.Vitality, GearEffectKind.Flat, 5f),
            new GearSlotEffect(EquipSlot.Amulet, StatType.KnockbackResistancePercent, GearEffectKind.Flat, 1f),
            new GearSlotEffect(EquipSlot.Amulet, StatType.Strength, GearEffectKind.Flat, 5f),
        };
        [Tooltip("Added once, at the last stage.")]
        public GearSlotEffect[] finalStageBonus =
        {
            new GearSlotEffect(EquipSlot.Weapon, StatType.Spirit, GearEffectKind.Flat, 5f),
            new GearSlotEffect(EquipSlot.Armor, StatType.Spirit, GearEffectKind.Flat, 5f),
            new GearSlotEffect(EquipSlot.Gloves, StatType.Agility, GearEffectKind.Flat, 5f),
            new GearSlotEffect(EquipSlot.Boots, StatType.Agility, GearEffectKind.Flat, 5f),
            new GearSlotEffect(EquipSlot.Ring, StatType.Vitality, GearEffectKind.Flat, 5f),
            new GearSlotEffect(EquipSlot.Amulet, StatType.Strength, GearEffectKind.Flat, 5f),
        };
    }

    [Serializable]
    public sealed class ReforgeSettings
    {
        [Header("Cost = base + per ten levels x (item level - first level) / 10")]
        [Min(0)] public long baseCreds = 1_000_000;
        [Tooltip("The item level the base cost is for.")]
        [Min(1)] public int firstLevel = 45;
        [Min(0)] public long credsPerTenLevels = 200_000;

        [Header("Success chance")]
        [Range(0f, 100f)] public float chance = 50f;
        [Min(0f)] public float luckPerPercent = 100f;
        [Range(0f, 100f)] public float maxLuckBonus = 25f;
    }

    /// <summary>
    /// Every number of gear progression: Tempering, Fusion, Tuning and Reforge. One asset, four sections, read by
    /// the rules (<see cref="GearProgressionRules"/>), by the stat resolver and by the forge window. Numbers marked
    /// proposed in _Docs/Reference/12Sky/02_Gear_System_Spec.md are meant to be tuned here; nothing in code may
    /// hard-code one. The stat table supplies the rarity multipliers and the level tiers.
    /// </summary>
    [CreateAssetMenu(menuName = "Krytical Nova/Items/Gear Progression Settings")]
    public sealed class GearProgressionSettings : ScriptableObject
    {
        [Tooltip("Rarity multipliers and the level tiers of every band.")]
        public GearStatTable statTable;
        public TemperSettings temper = new TemperSettings();
        public FusionSettings fusion = new FusionSettings();
        public TuningSettings tuning = new TuningSettings();
        public ReforgeSettings reforge = new ReforgeSettings();

        public float MultiplierOf(ItemRarity rarity) => statTable != null ? statTable.MultiplierOf(rarity) : 1f;
    }

    /// <summary>
    /// Item ids of the progression materials. 9100-9199 of the utility range (see UtilityItemGenerator, which makes
    /// the items). Named here so the settings' defaults and the generator agree.
    /// </summary>
    public static class GearMaterialIds
    {
        public const ushort Alumite = 9100;
        public const ushort Iridion = 9101;
        public const ushort Orichalc = 9102;
        public const ushort Eversteel = 9103;
        public const ushort TrinityPlatinum = 9104;
        public const ushort PrimeAlloy = 9105;
        public const ushort Failsafe = 9110;
        public const ushort AdvancedFailsafe = 9111;
        public const ushort LuckyFailsafe = 9112;
        public const ushort FusionCatalyst = 9120;
        public const ushort TuningChip = 9130;
        public const ushort VioletChip = 9131;
        public const ushort ResetChip = 9132;
    }
}
