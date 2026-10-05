using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

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

    /// <summary>A buff stat modifier whose value grows linearly from the first rank to the last.</summary>
    [Serializable]
    public struct RankScaledModifier
    {
        public StatType statType;
        public ModifierType modifierType;
        public float minValue;
        public float maxValue;

        public RankScaledModifier(StatType stat, ModifierType modType, float min, float max)
        {
            statType = stat;
            modifierType = modType;
            minValue = min;
            maxValue = max;
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

        [Header("Weapon Class")]
        [Tooltip("The class this skill belongs to. None = shared: every class can use it.")]
        public WeaponClassId weaponClass = WeaponClassId.None;
        [Tooltip("Which of the class's ten slots this is. Skills in the same slot share rank, unlock and cooldown across classes.")]
        public ClassSkillSlot classSlot = ClassSkillSlot.None;
        [Tooltip("Skill whose hit feel this one borrows (its ID). Empty = its own.")]
        public string presentationId = string.Empty;

        /// <summary>True for one of a weapon class's ten skills, as opposed to a shared skill.</summary>
        public bool IsClassSkill => weaponClass != WeaponClassId.None && classSlot != ClassSkillSlot.None;
        /// <summary>
        /// What a rank, an unlock and a cooldown are stored under. A class skill is stored under its slot, so the
        /// three classes of a faction share one set; a shared skill is stored under its own ID.
        /// </summary>
        public string RankKey => IsClassSkill ? SlotKeys[(int)classSlot] : skillId;
        /// <summary>The ID hit feel and effects are looked up under.</summary>
        public string PresentationId => string.IsNullOrEmpty(presentationId) ? skillId : presentationId;

        private static readonly string[] SlotKeys =
        {
            "slot:None", "slot:S1", "slot:S2", "slot:S3", "slot:S4", "slot:S5", "slot:S6", "slot:S7", "slot:S8", "slot:Stance", "slot:Guard"
        };

        [Header("Targeting & Execution")]
        public SkillTargetType targetType = SkillTargetType.Self;
        public SkillExecutionType executionType = SkillExecutionType.Instant;

        [Header("Acquisition & Crafting")]
        public SkillAcquisitionMethod acquisitionMethod = SkillAcquisitionMethod.FactionTrainer;
        [Tooltip("When true, this skill cannot be purchased via trainer skill points; it requires acquiring a rare skill book drop or crafting.")]
        public bool requiresSkillBook;
        [Tooltip("Note shown in the skill UI describing how this skill is obtained.")]
        public string acquisitionNote = "Faction Instructor";
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

        [Header("Channeled Recovery (e.g. Heavenly Serenity)")]
        [Tooltip("Interval in seconds between channeled ticks.")]
        [Min(0.1f)] public float channelTickInterval = 1.0f;
        [Tooltip("Percent of Max Chi restored per channel tick at the first rank.")]
        [FormerlySerializedAs("channelChiRecoveryPercent")]
        [Min(0f)] public float minChannelChiRecoveryPercent = 5.0f;
        [Tooltip("Percent of Max Chi restored per channel tick at the last rank.")]
        [Min(0f)] public float maxChannelChiRecoveryPercent = 5.0f;
        [Tooltip("Percent of Max HP restored per channel tick at the first rank. 0 = the channel does not heal.")]
        [Min(0f)] public float minChannelHealthRecoveryPercent;
        [Tooltip("Percent of Max HP restored per channel tick at the last rank.")]
        [Min(0f)] public float maxChannelHealthRecoveryPercent;

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

        [Header("Rank-Scaled Buff Modifiers (e.g. Spiritual Rage, Lightning Flash)")]
        public RankScaledModifier[] rankScaledModifiers = Array.Empty<RankScaledModifier>();
        [Tooltip("The buff is removed by the caster's next landed hit (only the first hit of a multi-hit benefits).")]
        public bool consumedOnNextHit;

        [Header("Attack (damage skills; leave Max Damage at 0 for buffs)")]
        [Tooltip("Damage applied as a percentage of Attack Power at the first rank. Multi-hit skills split this across their hits.")]
        [Min(0f)] public float minDamagePercent;
        [Min(0f)] public float maxDamagePercent;
        [Min(1)] public int hitCount = 1;
        [Tooltip("Metres. Reach to the target for single-target skills; blast radius around the caster for area skills.")]
        [Min(0f)] public float minRange = 2f;
        [Min(0f)] public float maxRange = 2f;
        [Tooltip("Extra damage multiplier in percent (100 = none). Stands in for elemental damage such as Yang until elements exist.")]
        [Min(0f)] public float minBonusDamagePercent = 100f;
        [Min(0f)] public float maxBonusDamagePercent = 100f;
        [Tooltip("Off = this skill's hits never roll a critical, whatever the caster's Critical Rate.")]
        public bool canCrit = true;

        [Header("Input")]
        [Tooltip("Seconds the hotbar key must be held before the skill casts. Releasing early cancels at no cost.")]
        [Min(0f)] public float chargeTime;
        [Tooltip("Holding the hotbar key recasts as soon as the cooldown and cast animation allow.")]
        public bool holdToRepeat;

        [Header("Presentation (all optional)")]
        [Tooltip("Hotbar, skill window and buff bar icon. Baked by Krytical Nova ▸ Progression ▸ Build Skill Icons.")]
        public Sprite icon;
        public AnimationClip castClip;
        [Min(0.1f)] public float castClipSpeed = 1f;
        [Tooltip("When each hit lands, as a fraction of the cast clip. Missing entries are spread evenly.")]
        public float[] hitTimesNormalized = Array.Empty<float>();
        [Tooltip("Fraction of the cast clip after which the caster may act again.")]
        [Range(0.1f, 1f)] public float castLockNormalized = 1f;
        [Tooltip("Fraction of the cast clip after which an attack (a skill or the basic combo) may cut the rest of it " +
                 "short. 1 = never: the cast lock runs its course.")]
        [Range(0f, 1f)] public float attackCancelNormalized = 1f;
        [Tooltip("Metres the body is raised (or lowered) over the cast clip's own time in seconds. The player's animator " +
                 "discards a clip's root motion, so without this a leap stays on the ground with the legs tucked up. " +
                 "Baked from the clip by Krytical Nova ▸ Progression ▸ Build Blade Skills.")]
        public AnimationCurve castLiftCurve = new AnimationCurve();
        [Tooltip("Looped while the hotbar key is held for a charged skill.")]
        public AnimationClip chargeLoopClip;
        [Tooltip("Channeled skills: held for as long as the channel lasts. The cast clip brings the body into this pose " +
                 "and, played backwards, out of it again.")]
        public AnimationClip channelLoopClip;
        [Tooltip("Spawned at the caster when the skill is cast.")]
        public GameObject castVfxPrefab;
        [Tooltip("Spawned on every target the skill damages.")]
        public GameObject hitVfxPrefab;
        [Tooltip("Spawned at the caster while a charged skill is being held.")]
        public GameObject chargeVfxPrefab;
        [Tooltip("Cast / charge VFX origin relative to the caster (x = right, y = up, z = forward).")]
        public Vector3 vfxOffset = new Vector3(0f, 1f, 0f);

        // Backward compatibility properties
        public float chiCost => minChiCost;
        public float duration => maxDuration;
        public int skillPointCost => skillPointsToLearn;

        /// <summary>Damage skills strike targets; everything else applies a timed buff to the caster.</summary>
        public bool IsAttack => maxDamagePercent > 0f;
        public bool IsAreaAttack => IsAttack && targetType == SkillTargetType.AreaOfEffect;
        public bool IsCharged => chargeTime > 0f;
        public bool IsChanneled => executionType == SkillExecutionType.Channeled;
        public bool HasCastLift => castLiftCurve != null && castLiftCurve.length > 1;

        /// <summary>Total damage of one cast as a fraction of Attack Power (1 = 100%), before splitting across hits.</summary>
        public float GetDamageMultiplier(int rank) =>
            Mathf.Lerp(minDamagePercent, maxDamagePercent, GetRankProgress(rank)) / 100f
            * Mathf.Lerp(minBonusDamagePercent, maxBonusDamagePercent, GetRankProgress(rank)) / 100f;

        public float GetRange(int rank) => Mathf.Lerp(minRange, maxRange, GetRankProgress(rank));

        /// <summary>Seconds from cast start until hit <paramref name="hitIndex"/> (0-based) lands.</summary>
        public float GetHitDelay(int hitIndex)
        {
            float normalized = hitTimesNormalized != null && hitIndex < hitTimesNormalized.Length
                ? hitTimesNormalized[hitIndex]
                : (hitIndex + 1f) / (Mathf.Max(1, hitCount) + 1f);
            return Mathf.Clamp01(normalized) * CastDuration;
        }

        /// <summary>
        /// Length of the cast animation at its playback speed. Without a clip, attacks use a short default so their
        /// hits are still timed, and buffs are instant.
        /// </summary>
        public float CastDuration =>
            (castClip != null ? castClip.length : IsAttack ? DefaultCastSeconds : 0f) / Mathf.Max(0.1f, castClipSpeed);

        /// <summary>Seconds after cast start during which the caster cannot begin another skill.</summary>
        public float CastLockDuration => CastDuration * castLockNormalized;

        /// <summary>Seconds after cast start from which an attack may cut the cast short; the whole lock when it may not.</summary>
        public float AttackCancelDelay => Mathf.Min(CastDuration * attackCancelNormalized, CastLockDuration);

        private const float DefaultCastSeconds = 0.6f;

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

        /// <summary>Percent of Max Chi a channeled skill restores per tick at <paramref name="rank"/>.</summary>
        public float GetChannelChiRecovery(int rank) =>
            Mathf.Lerp(minChannelChiRecoveryPercent, maxChannelChiRecoveryPercent, GetRankProgress(rank));

        /// <summary>Percent of Max HP a channeled skill restores per tick at <paramref name="rank"/>.</summary>
        public float GetChannelHealthRecovery(int rank) =>
            Mathf.Lerp(minChannelHealthRecoveryPercent, maxChannelHealthRecoveryPercent, GetRankProgress(rank));

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

            if (rankScaledModifiers != null)
            {
                float progress = GetRankProgress(rank);
                foreach (var scaled in rankScaledModifiers)
                    list.Add(new StatModifierPayload(scaled.statType, scaled.modifierType, Mathf.Lerp(scaled.minValue, scaled.maxValue, progress)));
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
            cooldown = Mathf.Max(0f, cooldown); // spammable skills are paced by their cast animation instead
            maxDamagePercent = Mathf.Max(minDamagePercent, maxDamagePercent);
            maxRange = Mathf.Max(minRange, maxRange);
            hitCount = Mathf.Max(1, hitCount);
            minDuration = Mathf.Max(0f, minDuration);
            maxDuration = Mathf.Max(minDuration, maxDuration);
            channelTickInterval = Mathf.Max(0.1f, channelTickInterval);
            minChannelChiRecoveryPercent = Mathf.Max(0f, minChannelChiRecoveryPercent);
            maxChannelChiRecoveryPercent = Mathf.Max(minChannelChiRecoveryPercent, maxChannelChiRecoveryPercent);
            minChannelHealthRecoveryPercent = Mathf.Max(0f, minChannelHealthRecoveryPercent);
            maxChannelHealthRecoveryPercent = Mathf.Max(minChannelHealthRecoveryPercent, maxChannelHealthRecoveryPercent);
        }
    }
}
