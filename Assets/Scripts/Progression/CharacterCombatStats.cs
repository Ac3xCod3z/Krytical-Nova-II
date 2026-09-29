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

        public int BaseStrength => progression != null ? progression.GetAttribute(CharacterAttribute.Strength) : fallbackStrength;
        public int BaseVitality => progression != null ? progression.GetAttribute(CharacterAttribute.Vitality) : fallbackVitality;
        public int BaseAgility => progression != null ? progression.GetAttribute(CharacterAttribute.Agility) : fallbackAgility;
        public int BaseSpirit => progression != null ? progression.GetAttribute(CharacterAttribute.Spirit) : fallbackSpirit;

        public float TotalStrength => GetStatValue(StatType.Strength);
        public float TotalVitality => GetStatValue(StatType.Vitality);
        public float TotalAgility => GetStatValue(StatType.Agility);
        public float TotalSpirit => GetStatValue(StatType.Spirit);

        public int Strength => Mathf.RoundToInt(TotalStrength);
        public int Vitality => Mathf.RoundToInt(TotalVitality);
        public int Agility => Mathf.RoundToInt(TotalAgility);
        public int Spirit => Mathf.RoundToInt(TotalSpirit);

        private void Awake()
        {
            progression = GetComponent<PlayerCharacterProgression>();
            if (characterClass != null && progression == null)
            {
                fallbackStrength = characterClass.baseStrength;
                fallbackVitality = characterClass.baseVitality;
                fallbackAgility = characterClass.baseAgility;
                fallbackSpirit = characterClass.baseSpirit;
            }
        }

        private void OnEnable()
        {
            if (progression != null)
                progression.Changed += RecalculateStats;
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
                    // Vitality: Primary (+20 Max HP)
                    float baseHP = characterClass != null ? characterClass.baseHealthPool : 100f;
                    return baseHP + (TotalVitality * vitToHp);
                }

                case StatType.MaxChi:
                {
                    // Spirit: Primary (+20 Max Chi)
                    float baseChi = characterClass != null ? characterClass.baseChiPool : 50f;
                    return baseChi + (TotalSpirit * spiritToChi);
                }

                // Secondary & Advanced Stats
                case StatType.CriticalRate: return critRate;
                case StatType.CriticalDamagePercent: return 0f;
                case StatType.ElementalAttack: return eleAtk;
                case StatType.ElementalDefense: return eleDef;
                case StatType.AcupressureStunResistance: return stunRes;
                case StatType.Luck: return 0f;
                case StatType.MoveSpeedPercent: return moveSpeed;

                default: return 0f;
            }
        }
        #endregion

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
