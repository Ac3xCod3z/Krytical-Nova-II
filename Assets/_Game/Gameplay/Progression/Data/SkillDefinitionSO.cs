using System;
using System.Collections.Generic;
using UnityEngine;

namespace KryticalNova.Progression
{
    public enum SkillTargetType
    {
        Self,
        SingleTarget,
        AreaOfEffect,
        Party
    }

    public enum SkillExecutionType
    {
        Instant,
        Channeled,
        Passive
    }

    public enum SkillCategory
    {
        GeneralUtility,
        RareMasterSkill,
        WeaponStance,
        Support,
        Combat
    }

    public enum SkillAcquisitionMethod
    {
        FactionTrainer,
        DropItem,
        CraftableArchive,
        CraftableOrDrop
    }

    [Serializable]
    public struct StatModifierPayload
    {
        public StatType statType;
        public ModifierType modifierType;
        public float value;

        public StatModifierPayload(StatType stat, ModifierType modType, float val)
        {
            statType = stat;
            modifierType = modType;
            value = val;
        }
    }

    /// <summary>Designer-configurable ScriptableObject defining skills, costs, targets, rank scaling, and effect payloads.</summary>
    [CreateAssetMenu(menuName = "Krytical Nova/Skill Definition", fileName = "Skill_NewSkill")]
    public sealed class SkillDefinitionSO : ScriptableObject
    {
        [Header("Identity & Categorization")]
        public string skillId = "SkillId";
        public string displayName = "Skill Name";
        [TextArea(2, 4)] public string description = "Skill description.";
        public SkillCategory category = SkillCategory.GeneralUtility;

        [Header("Targeting & Execution")]
        public SkillTargetType targetType = SkillTargetType.Self;
        public SkillExecutionType executionType = SkillExecutionType.Instant;

        [Header("Acquisition & Crafting")]
        public SkillAcquisitionMethod acquisitionMethod = SkillAcquisitionMethod.FactionTrainer;
        [Tooltip("When true, this skill cannot be purchased via trainer skill points; it requires acquiring a rare skill book drop or crafting.")]
        public bool requiresSkillBook;
        [Tooltip("Note shown in the skill UI describing how this skill is obtained.")]
        public string acquisitionNote = "Faction Skill Trainer";
        [Tooltip("List of required archives to craft this skill (e.g. for HR, RF, HD).")]
        public string[] requiredCraftingArchives = Array.Empty<string>();
        [Min(1)] public int requiredCharacterLevel = 1;

        [Header("Rank Progression & Skill Point Costs")]
        [Min(1)] public int minRank = 1;
        [Min(1)] public int maxRank = 20;
        [Tooltip("Skill points required to unlock Rank 1.")]
        [Min(0)] public int skillPointsToLearn = 1;
        [Tooltip("Total skill points required to achieve maximum rank.")]
        [Min(1)] public int totalSkillPointsToMax = 20;

        [Header("Resource & Timing Scaling")]
        [Min(0f)] public float minChiCost = 15f;
        [Min(0f)] public float maxChiCost = 15f;
        [Tooltip("Cooldown time in seconds before this skill can be activated again.")]
        [Min(0f)] public float cooldown = 1.0f;
        [Min(0f)] public float minDuration = 180f;
        [Min(0f)] public float maxDuration = 180f;

        [Header("Channeled Chi Recovery (e.g. Heavenly Serenity)")]
        [Tooltip("Interval in seconds between channeled ticks.")]
        [Min(0.1f)] public float channelTickInterval = 1.0f;
        [Tooltip("Percent of Max Chi restored per channel tick.")]
        [Min(0f)] public float channelChiRecoveryPercent = 5.0f;

        [Header("Absorption Shield (e.g. Heavenly Shield)")]
        public bool isAbsorptionShield;
        [Tooltip("Minimum shield intensity percentage of Max HP (e.g. 111 = 111% Max HP).")]
        [Min(0f)] public float minShieldPercent = 111f;
        [Tooltip("Maximum shield intensity percentage of Max HP (e.g. 150 = 150% Max HP).")]
        [Min(0f)] public float maxShieldPercent = 150f;

        [Header("Deadly Blow / Critical Multiplier (e.g. Heavenly Blow)")]
        public bool hasDeadlyBlowBoost;
        [Tooltip("Minimum percentage increase to existing deadly blow chance (e.g. 12 = +12%). Multiplicative.")]
        [Min(0f)] public float minDeadlyBlowPercent = 12f;
        [Tooltip("Maximum percentage increase to existing deadly blow chance (e.g. 75 = +75%). Multiplicative.")]
        [Min(0f)] public float maxDeadlyBlowPercent = 75f;

        [Header("Luck Multiplier (e.g. Garuda's Prayer)")]
        public bool hasLuckBoost;
        [Tooltip("Minimum percentage increase to luck (e.g. 13 = +13%). Multiplicative.")]
        [Min(0f)] public float minLuckPercent = 13f;
        [Tooltip("Maximum percentage increase to luck (e.g. 100 = +100%). Multiplicative.")]
        [Min(0f)] public float maxLuckPercent = 100f;

        [Header("Acupressure Stun Resistance (e.g. Reverse Flow)")]
        public bool hasAcupressureResistance;
        [Tooltip("Minimum stun resistance percentage increase (e.g. 1 = +1%).")]
        [Min(0f)] public float minAcupressureResistance = 1f;
        [Tooltip("Maximum stun resistance percentage increase (e.g. 20 = +20%).")]
        [Min(0f)] public float maxAcupressureResistance = 20f;

        [Header("Defensive Reflection Proc (e.g. Heavenly Repulsion)")]
        public bool hasReflection;
        [Range(0f, 100f)] public float minReflectionChance = 1f;
        [Range(0f, 100f)] public float maxReflectionChance = 10f;
        [Min(0f)] public float reflectionMultiplier = 1.0f;

        [Header("Offensive Proc / Dispel (e.g. Heavenly Destruction)")]
        public bool hasShieldShatter;
        [Range(0f, 100f)] public float minDestructionChance = 1f;
        [Range(0f, 100f)] public float maxDestructionChance = 20f;
        [Range(0f, 100f)] public float dispelBuffChance = 50f;

        [Header("Static Modifiers (Fallback / General)")]
        public StatModifierPayload[] statModifiers = Array.Empty<StatModifierPayload>();

        // Backward compatibility properties
        public float chiCost => minChiCost;
        public float duration => maxDuration;
        public int skillPointCost => skillPointsToLearn;

        public float GetRankProgress(int rank)
        {
            if (maxRank <= minRank) return 1f;
            return Mathf.Clamp01((rank - (float)minRank) / (maxRank - minRank));
        }

        public float GetChiCost(int rank)
        {
            return Mathf.Round(Mathf.Lerp(minChiCost, maxChiCost, GetRankProgress(rank)));
        }

        public float GetDuration(int rank)
        {
            return Mathf.Lerp(minDuration, maxDuration, GetRankProgress(rank));
        }

        public float GetShieldMultiplier(int rank)
        {
            if (!isAbsorptionShield) return 0f;
            return Mathf.Lerp(minShieldPercent, maxShieldPercent, GetRankProgress(rank)) / 100f;
        }

        public float GetDeadlyBlowBonus(int rank)
        {
            if (!hasDeadlyBlowBoost) return 0f;
            return Mathf.Lerp(minDeadlyBlowPercent, maxDeadlyBlowPercent, GetRankProgress(rank));
        }

        public float GetLuckBonus(int rank)
        {
            if (!hasLuckBoost) return 0f;
            return Mathf.Lerp(minLuckPercent, maxLuckPercent, GetRankProgress(rank));
        }

        public float GetAcupressureResistance(int rank)
        {
            if (!hasAcupressureResistance) return 0f;
            return Mathf.Lerp(minAcupressureResistance, maxAcupressureResistance, GetRankProgress(rank));
        }

        public float GetReflectionChance(int rank)
        {
            if (!hasReflection) return 0f;
            return Mathf.Lerp(minReflectionChance, maxReflectionChance, GetRankProgress(rank));
        }

        public float GetDestructionChance(int rank)
        {
            if (!hasShieldShatter) return 0f;
            return Mathf.Lerp(minDestructionChance, maxDestructionChance, GetRankProgress(rank));
        }

        public int GetSkillPointCostForRank(int currentRank)
        {
            if (currentRank <= 0) return Mathf.Max(1, skillPointsToLearn);
            if (currentRank >= maxRank) return 0;

            int remainingPoints = Mathf.Max(0, totalSkillPointsToMax - skillPointsToLearn);
            int remainingRanks = Mathf.Max(1, maxRank - 1);

            int pointsAtNext = Mathf.RoundToInt((float)currentRank / remainingRanks * remainingPoints);
            int pointsAtCur = Mathf.RoundToInt((float)(currentRank - 1) / remainingRanks * remainingPoints);
            return Mathf.Max(1, pointsAtNext - pointsAtCur);
        }

        public StatModifierPayload[] GetStatModifiers(int rank)
        {
            var list = new List<StatModifierPayload>();

            if (hasDeadlyBlowBoost)
            {
                list.Add(new StatModifierPayload(StatType.CriticalRate, ModifierType.PercentMult, GetDeadlyBlowBonus(rank)));
            }

            if (hasLuckBoost)
            {
                list.Add(new StatModifierPayload(StatType.Luck, ModifierType.PercentMult, GetLuckBonus(rank)));
            }

            if (hasAcupressureResistance)
            {
                list.Add(new StatModifierPayload(StatType.AcupressureStunResistance, ModifierType.Flat, GetAcupressureResistance(rank)));
            }

            if (statModifiers != null && statModifiers.Length > 0)
            {
                list.AddRange(statModifiers);
            }

            return list.ToArray();
        }

        private void OnValidate()
        {
            requiredCharacterLevel = Mathf.Max(1, requiredCharacterLevel);
            minRank = Mathf.Max(1, minRank);
            maxRank = Mathf.Max(minRank, maxRank);
            skillPointsToLearn = Mathf.Max(0, skillPointsToLearn);
            totalSkillPointsToMax = Mathf.Max(skillPointsToLearn, totalSkillPointsToMax);
            minChiCost = Mathf.Max(0f, minChiCost);
            maxChiCost = Mathf.Max(minChiCost, maxChiCost);
            cooldown = Mathf.Max(0.1f, cooldown);
            minDuration = Mathf.Max(0f, minDuration);
            maxDuration = Mathf.Max(minDuration, maxDuration);
            channelTickInterval = Mathf.Max(0.1f, channelTickInterval);
            channelChiRecoveryPercent = Mathf.Max(0f, channelChiRecoveryPercent);
        }
    }
}
