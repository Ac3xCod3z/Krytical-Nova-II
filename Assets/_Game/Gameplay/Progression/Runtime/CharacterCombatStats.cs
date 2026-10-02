using System;
using System.Collections.Generic;
using UnityEngine;

namespace KryticalNova.Progression
{
    /// <summary>Calculates primary and derived combat stats dynamically using primary attributes, class baselines, and active modifiers.</summary>
    [DefaultExecutionOrder(InitializationOrder)]
    [DisallowMultipleComponent]
    public sealed class CharacterCombatStats : MonoBehaviour
    {
        private const int InitializationOrder = -175;
        private const float BaselineCriticalRate = 15f;
        private const float BaselineMoveSpeedPercent = 100f;

        [Header("Tuning & Class Configuration (ScriptableObjects)")]
        [SerializeField] private CombatStatTuningData tuningData;
        [SerializeField] private CharacterClassData characterClass;

        [Header("Manual Fallback Attributes (Used if no PlayerCharacterProgression)")]
        [SerializeField] private int fallbackStrength = 10;
        [SerializeField] private int fallbackVitality = 10;
        [SerializeField] private int fallbackAgility = 10;
        [SerializeField] private int fallbackSpirit = 10;

        private PlayerCharacterProgression progression;
        private readonly List<StatModifier> modifiers = new List<StatModifier>();

        public event Action OnStatsRecalculated;

        public CombatStatTuningData TuningData
        {
            get => tuningData;
            set
            {
                tuningData = value;
                RecalculateStats();
            }
        }

        public CharacterClassData ClassData
        {
            get => characterClass;
            set
            {
                characterClass = value;
                if (characterClass != null && progression == null)
                {
                    fallbackStrength = characterClass.baseStrength;
                    fallbackVitality = characterClass.baseVitality;
                    fallbackAgility = characterClass.baseAgility;
                    fallbackSpirit = characterClass.baseSpirit;
                }
                RecalculateStats();
            }
        }

        public int BaseStrength => Progression != null ? Progression.GetAttribute(CharacterAttribute.Strength) : fallbackStrength;
        public int BaseVitality => Progression != null ? Progression.GetAttribute(CharacterAttribute.Vitality) : fallbackVitality;
        public int BaseAgility => Progression != null ? Progression.GetAttribute(CharacterAttribute.Agility) : fallbackAgility;
        public int BaseSpirit => Progression != null ? Progression.GetAttribute(CharacterAttribute.Spirit) : fallbackSpirit;

        public PlayerCharacterProgression Progression
        {
            get
            {
                if (progression == null) EnsureInitialized();
                return progression;
            }
        }

        public float TotalStrength => GetStatValue(StatType.Strength);
        public float TotalVitality => GetStatValue(StatType.Vitality);
        public float TotalAgility => GetStatValue(StatType.Agility);
        public float TotalSpirit => GetStatValue(StatType.Spirit);

        public int Strength => Mathf.RoundToInt(TotalStrength);
        public int Vitality => Mathf.RoundToInt(TotalVitality);
        public int Agility => Mathf.RoundToInt(TotalAgility);
        public int Spirit => Mathf.RoundToInt(TotalSpirit);

        public void EnsureInitialized()
        {
            if (progression == null)
            {
                progression = GetComponent<PlayerCharacterProgression>();
            }
            if (characterClass != null && progression == null)
            {
                fallbackStrength = characterClass.baseStrength;
                fallbackVitality = characterClass.baseVitality;
                fallbackAgility = characterClass.baseAgility;
                fallbackSpirit = characterClass.baseSpirit;
            }
        }

        private void Awake()
        {
            EnsureInitialized();
        }

        private void OnEnable()
        {
            EnsureInitialized();
            if (progression != null)
            {
                progression.Changed -= RecalculateStats;
                progression.Changed += RecalculateStats;
            }
        }

        private void OnDisable()
        {
            if (progression != null)
                progression.Changed -= RecalculateStats;
        }

        #region Modifier Management
        public void AddModifier(StatModifier mod)
        {
            if (mod == null) return;
            modifiers.Add(mod);
            RecalculateStats();
        }

        public bool RemoveModifier(StatModifier mod)
        {
            if (mod == null) return false;
            bool removed = modifiers.Remove(mod);
            if (removed) RecalculateStats();
            return removed;
        }

        public int RemoveModifiersFromSource(object source)
        {
            if (source == null) return 0;
            int removed = modifiers.RemoveAll(m => m.Source == source);
            if (removed > 0) RecalculateStats();
            return removed;
        }

        /// <summary>
        /// Replaces every modifier owned by a source in one recalculation. Generic entry point for any stat
        /// provider (mounts, pets, titles, GM tools): pass an empty/null list to clear the source.
        /// </summary>
        public void SetSourceModifiers(object source, IEnumerable<StatModifier> sourceModifiers)
        {
            if (source == null) return;
            modifiers.RemoveAll(m => m.Source == source);
            if (sourceModifiers != null)
            {
                foreach (var mod in sourceModifiers)
                    if (mod != null) modifiers.Add(mod);
            }
            RecalculateStats();
        }

        /// <summary>Read-only view of active modifiers (debug / GM listing).</summary>
        public IReadOnlyList<StatModifier> Modifiers => modifiers;

        public void ClearModifiers()
        {
            if (modifiers.Count == 0) return;
            modifiers.Clear();
            RecalculateStats();
        }
        #endregion

        #region Calculated Stat Accessors
        public float GetStatValue(StatType stat)
        {
            float baseValue = GetBaseStatValue(stat);
            float flatSum = 0f;
            float percentAddSum = 0f;
            float percentMult = 1f;

            for (int i = 0; i < modifiers.Count; i++)
            {
                var mod = modifiers[i];
                if (mod.Stat != stat) continue;

                if (mod.ModType == ModifierType.Flat)
                    flatSum += mod.Value;
                else if (mod.ModType == ModifierType.PercentAdd)
                    percentAddSum += mod.Value;
                else if (mod.ModType == ModifierType.PercentMult)
                    percentMult *= (1f + (mod.Value / 100f));
            }

            // Standard Formula: (Base + Flat) * (1 + SumOfPercentAdd / 100) * ProductOfPercentMult
            float finalValue = (baseValue + flatSum) * (1f + (percentAddSum / 100f)) * percentMult;
            return Mathf.Max(0f, finalValue);
        }

        public float GetBaseStatValue(StatType stat)
        {
            float strToAp = tuningData != null ? tuningData.strengthToAttackPower : 1f;
            float spiritToAp = tuningData != null ? tuningData.spiritToAttackPower : 1f;
            float strToHit = tuningData != null ? tuningData.strengthToHitRate : 2f;
            float agiToDp = tuningData != null ? tuningData.agilityToDefensePower : 1f;
            float agiToEvade = tuningData != null ? tuningData.agilityToEvasion : 2f;
            float vitToEvade = tuningData != null ? tuningData.vitalityToEvasion : 1f;
            float vitToHp = tuningData != null ? tuningData.vitalityToHealth : 20f;
            float spiritToChi = tuningData != null ? tuningData.spiritToChi : 20f;
            float hpPerLevel = tuningData != null ? tuningData.healthPerLevel : 15f;
            float chiPerLevel = tuningData != null ? tuningData.chiPerLevel : 10f;
            float critRate = tuningData != null ? tuningData.baselineCriticalRate : BaselineCriticalRate;
            float moveSpeed = tuningData != null ? tuningData.baselineMoveSpeedPercent : BaselineMoveSpeedPercent;
            float eleAtk = tuningData != null ? tuningData.baselineElementalAttack : 0f;
            float eleDef = tuningData != null ? tuningData.baselineElementalDefense : 0f;
            float stunRes = tuningData != null ? tuningData.baselineStunResistance : 0f;

            switch (stat)
            {
                // Primary Attributes return allocated base values (modifiers are applied in GetStatValue)
                case StatType.Strength: return BaseStrength;
                case StatType.Vitality: return BaseVitality;
                case StatType.Agility: return BaseAgility;
                case StatType.Spirit: return BaseSpirit;

                // Derived Primary & Secondary Combat Stats (ScriptableObject-Tuned Formulas)
                // These scale directly from total effective attributes (Base Allocated + Gear/Buff Modifiers)
                case StatType.AttackPower:
                {
                    // Strength: Primary (+1 AP) | Spirit: Secondary (+1 AP)
                    float weaponAP = characterClass != null ? characterClass.baseWeaponAP : 15f;
                    return (TotalStrength * strToAp) + (TotalSpirit * spiritToAp) + weaponAP;
                }

                case StatType.DefensePower:
                {
                    // Agility: Secondary (+1 Defense)
                    float weaponDef = characterClass != null ? characterClass.baseWeaponDefense : 5f;
                    return (TotalAgility * agiToDp) + weaponDef;
                }

                case StatType.HitRate:
                {
                    // Strength: Secondary (+2 Hit Rate)
                    return TotalStrength * strToHit;
                }

                case StatType.EvasionRate:
                {
                    // Agility: Primary (+2 Evade) | Vitality: Secondary (+1 Evade)
                    return (TotalAgility * agiToEvade) + (TotalVitality * vitToEvade);
                }

                case StatType.MaxHP:
                {
                    // Vitality: Primary (+20 Max HP) + Level scaling (tuning: healthPerLevel per level above 1)
                    float baseHP = characterClass != null ? characterClass.baseHealthPool : 100f;
                    int level = Progression != null ? Progression.CurrentLevel : 1;
                    float levelHP = Mathf.Max(0, level - 1) * hpPerLevel;
                    return baseHP + levelHP + (TotalVitality * vitToHp);
                }

                case StatType.MaxChi:
                {
                    // Spirit: Primary (+20 Max Chi) + Level scaling (tuning: chiPerLevel per level above 1)
                    float baseChi = characterClass != null ? characterClass.baseChiPool : 50f;
                    int level = Progression != null ? Progression.CurrentLevel : 1;
                    float levelChi = Mathf.Max(0, level - 1) * chiPerLevel;
                    return baseChi + levelChi + (TotalSpirit * spiritToChi);
                }

                // Secondary & Advanced Stats
                case StatType.CriticalRate: return critRate;
                case StatType.CriticalDamagePercent: return 0f;
                case StatType.ElementalAttack: return eleAtk;
                case StatType.ElementalDefense: return eleDef;
                case StatType.AcupressureStunResistance: return stunRes;
                // Luck is a self-buff stat reserved for gear enhancement / loot systems. It is NOT read by combat resolution.
                // Gear, pets, mounts and buffs raise it through modifiers.
                case StatType.Luck: return tuningData != null ? tuningData.baseLuck : 50f;
                case StatType.MoveSpeedPercent: return moveSpeed;

                default: return 0f;
            }
        }
        #endregion

        /// <summary>
        /// Splits a resource pool (MaxHP or MaxChi) into its sources for display:
        /// class base + level scaling, the attribute contribution (Vitality / Spirit, including gear on the attribute),
        /// and everything else (flat/percent MaxHP or MaxChi modifiers from gear, pets, mounts, buffs).
        /// </summary>
        public void GetPoolBreakdown(StatType pool, out float baseAndLevel, out float fromAttribute, out float fromBonuses)
        {
            float total = GetStatValue(pool);
            if (pool == StatType.MaxHP)
            {
                float perPoint = tuningData != null ? tuningData.vitalityToHealth : 20f;
                fromAttribute = TotalVitality * perPoint;
            }
            else if (pool == StatType.MaxChi)
            {
                float perPoint = tuningData != null ? tuningData.spiritToChi : 20f;
                fromAttribute = TotalSpirit * perPoint;
            }
            else
            {
                fromAttribute = 0f;
            }
            baseAndLevel = GetBaseStatValue(pool) - fromAttribute;
            fromBonuses = total - baseAndLevel - fromAttribute;
        }

        public void RecalculateStats()
        {
            OnStatsRecalculated?.Invoke();
        }

        public void SetFallbackAttributes(int str, int vit, int agi, int spirit)
        {
            fallbackStrength = str;
            fallbackVitality = vit;
            fallbackAgility = agi;
            fallbackSpirit = spirit;
            RecalculateStats();
        }
    }
}
