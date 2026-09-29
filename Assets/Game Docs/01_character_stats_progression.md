# Twelve Sky 2 — Module 01: Character, Stats & Progression

## 1. Character foundation
Twelve Sky 2 does not use a conventional permanent class. A character's effective role is produced by faction + combat skill set + stat allocation + equipment.

## 2. Core attributes
The four primary attributes are:
- Strength — offensive power / attack-related performance.
- Agility — defense and evasion-related performance.
- Vitality — HP and survivability.
- Chi — Chi/Spirit resource and related offensive scaling.

The official Classic guide confirms that players distribute earned points among Strength, Agility, Vitality and Chi whenever they level.

## 3. Weapon-independent progression model
The important design pattern is that progression is layered:
Level → Stat Points → Skills → Equipment → Enhancement → PvP/PvE rewards → Higher progression.

## 4. Leveling
Early characters hunt monsters around their faction's territory. As level rises, the game moves players through progressively stronger fields and dungeon areas.

## 5. Common Skills
Starting around level 60 in the Classic progression guide, characters can access Common Skills regardless of faction. This adds a universal layer on top of faction-specific combat skills.

## 6. Rebirth
Later progression moves beyond ordinary leveling into Rebirth ranks. Rebirth unlocks stronger systems and higher-end equipment/content.

## 7. Contribution-based progression
PvP and Holy Stone participation can produce Class Points. The Class System converts those points into additional class-based bonuses.

## 8. Design takeaway
The character is not a single class choice. It is a stack of progression decisions:
- What stats do I invest in?
- Which combat skills do I develop?
- What gear do I enhance?
- What PvP/faction progression do I pursue?
- Which endgame systems do I optimize?

1. Primary Attributes & Derived Combat Stats
Characters utilize a dual-layer stat architecture: Primary Attributes (manually allocated on level-up) and Derived Combat Stats (calculated dynamically from primary attributes, weapon baselines, gear enchants, and active stances/buffs).

1.1 Primary Attributes (Allocated Stats)
Attribute	Derived Effect / Multiplier	Core Function
Strength (STR)	
• +1 Attack Power (AP)


• +2 Hit Rate / Attack Rate

Primary scaling attribute for physical damage output and accuracy.
Vitality (VIT)	
• +20 Max Health Points (HP)


• +1 Evade / Dodge

Direct health pool expansion with secondary evasion scaling.
Agility (AGI)	
• +2 Evade / Dodge


• +1 Defense (DP)

Defensive attribute for dodging strikes and mitigating physical damage.
Spirit (CHI)	
• +20 Max Chi (MP)


• +1 Attack Power (AP)

Resource pool expansion for skill casting and movement toggles.
1.2 Derived Primary Combat Stats
Stat Name	Acronym	Derived Formula	Math Mechanics & Purpose
Attack Power	AP	STR+Spirit+Weapon Base AP	Raw offensive damage calculated before target defense reduction.
Defense Power	DP	AGI+Armor DP	Flat damage reduction subtracted from incoming physical damage.
Hit Rate	Hit / AR	STR×2+Item Hit Rate	Accuracy rating tested against Target Evade.
Evasion Rate	Dodge / EV	AGI×2+VIT×1+Item Dodge	Chance to completely miss/evade an incoming strike.
Max Health	Max HP	(VIT×20)+Base Level HP+Gear HP	Total survivability pool.
Max Chi	Max Spirit	(Spirit×20)+Base Level Chi+Gear Chi	Mana/Skill resource pool consumed by actions and movement toggles.
1.3 Secondary Modifiers & Combat Modifiers
Stat Name	Primary Source	Math Mechanics / Operational Function
Critical Rate (Crit %)	Gear Enchants, Self-Buffs	Percentage chance for an attack to score a Critical Strike (deals Max Base Damage × Crit Multiplier). Baseline sits at 15%.
Critical Damage %	Refines & Accessories	Multiplier added to critical strikes (e.g., +20% Crit Damage turns a 1,000 damage hit into 1,200).
Elemental Attack	Weapon Enchants / Gems	Flat elemental damage added on top of physical strikes. Bypasses standard Physical Defense.
Elemental Defense	Armor Enchants / Gems	Flat elemental damage reduction subtracted from incoming Elemental Attack.
Acupressure Resistance	Passive Utilities, Master Buffs	Percentage resistance against incoming Stun effects (e.g., 60% resistance against a 50% stun roll).
Luck	Support Buffs, Special Gear	Increases Enchantment/Refinement Success Rate and Item Upgrade Safety.
Move Speed %	Movement Buffs, Formations, Boots	Percentage movement speed multiplier applied over base movement rate.
2. Generic Unity C# Implementation Architecture
The runtime architecture decouples Data Definitions (ScriptableObjects) from Runtime Character Logic (MonoBehaviours) and Presentation Layers (UI Controllers).

Architecture Components
StatType.cs — Enumerations for all core stats and modifier types.

StatDefinitionSO.cs — ScriptableObject storing stat limits, UI formatting, and descriptions.

CharacterClassSO.cs — ScriptableObject holding base class/faction properties.

StatModifier.cs — Class handling flat and percentage modifiers for equipment, stances, and buffs.

CharacterStats.cs — Runtime MonoBehaviour calculating final stats dynamically using stat formulas.

CombatCalculator.cs — Static pipeline executing hit checks, critical strikes, damage mitigation, and damage reflection.

CharacterSheetUI.cs — Interactive UI Controller managing attribute allocation and derived stat display.

2.1 Enums & Stat Modifiers (StatType.cs)
C#
using System;

namespace GameEngine.Stats
{
    public enum StatType
    {
        // Primary Attributes
        Strength,
        Vitality,
        Agility,
        Spirit,

        // Derived Primary Combat Stats
        AttackPower,
        DefensePower,
        HitRate,
        EvasionRate,
        MaxHP,
        MaxChi,

        // Secondary & Advanced Stats
        CriticalRate,
        CriticalDamagePercent,
        ElementalAttack,
        ElementalDefense,
        AcupressureStunResistance,
        Luck,
        MoveSpeedPercent
    }

    public enum ModifierType
    {
        Flat,
        PercentAdd, // Additive percentage (+20%)
        PercentMult // Multiplicative percentage (*1.20)
    }

    [Serializable]
    public class StatModifier
    {
        public StatType Stat;
        public ModifierType ModType;
        public float Value;
        public object Source; // Identifies buff, item, or stance causing the modifier

        public StatModifier(StatType stat, ModifierType modType, float value, object source = null)
        {
            Stat = stat;
            ModType = modType;
            Value = value;
            Source = source;
        }
    }
}
2.2 Stat Definition ScriptableObject (StatDefinitionSO.cs)
C#
using UnityEngine;

namespace GameEngine.Stats
{
    [CreateAssetMenu(fileName = "NewStatDefinition", menuName = "GameEngine/Stats/Stat Definition")]
    public class StatDefinitionSO : ScriptableObject
    {
        public StatType statType;
        public string statName;
        [TextArea] public string description;
        public float minValue = 0f;
        public float maxValue = 999999f;
        public bool isPercentage = false;
    }
}
2.3 Character Class ScriptableObject (CharacterClassSO.cs)
C#
using UnityEngine;

namespace GameEngine.Stats
{
    [CreateAssetMenu(fileName = "NewCharacterClass", menuName = "GameEngine/Stats/Character Class Configuration")]
    public class CharacterClassSO : ScriptableObject
    {
        public string factionName; 
        public string weaponType;  

        [Header("Base Starting Attributes")]
        public int baseStrength = 10;
        public int baseVitality = 10;
        public int baseAgility = 10;
        public int baseSpirit = 10;

        [Header("Base Health & Chi Pools")]
        public float baseHealthPool = 100f;
        public float baseChiPool = 50f;

        [Header("Weapon Base Modifiers")]
        public float baseWeaponAP = 15f;
        public float baseWeaponDefense = 5f;
    }
}
2.4 Runtime Character Stats Component (CharacterStats.cs)
C#
using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameEngine.Stats
{
    public class CharacterStats : MonoBehaviour
    {
        [Header("Class Config")]
        [SerializeField] private CharacterClassSO characterClass;

        [Header("Allocated Stat Points")]
        public int allocatedStrength;
        public int allocatedVitality;
        public int allocatedAgility;
        public int allocatedSpirit;

        // Active modifiers from equipment, buffs, and stances
        private readonly List<StatModifier> _modifiers = new List<StatModifier>();

        // Events for UI update callbacks
        public event Action OnStatsRecalculated;

        private void Awake()
        {
            if (characterClass != null)
            {
                allocatedStrength = characterClass.baseStrength;
                allocatedVitality = characterClass.baseVitality;
                allocatedAgility = characterClass.baseAgility;
                allocatedSpirit = characterClass.baseSpirit;
            }
        }

        #region Modifier Management
        public void AddModifier(StatModifier mod)
        {
            _modifiers.Add(mod);
            OnStatsRecalculated?.Invoke();
        }

        public void RemoveModifiersFromSource(object source)
        {
            _modifiers.RemoveAll(m => m.Source == source);
            OnStatsRecalculated?.Invoke();
        }
        #endregion

        #region Calculated Stat Accessors
        public float GetStatValue(StatType stat)
        {
            float baseValue = GetBaseStatValue(stat);
            float flatSum = 0f;
            float percentAddSum = 0f;

            foreach (var mod in _modifiers)
            {
                if (mod.Stat != stat) continue;

                if (mod.ModType == ModifierType.Flat)
                    flatSum += mod.Value;
                else if (mod.ModType == ModifierType.PercentAdd)
                    percentAddSum += mod.Value;
            }

            // Formula: (Base + Flat) * (1 + SumOfPercentAdd)
            float finalValue = (baseValue + flatSum) * (1f + (percentAddSum / 100f));
            return Mathf.Max(0f, finalValue);
        }

        private float GetBaseStatValue(StatType stat)
        {
            int totalStr = allocatedStrength;
            int totalVit = allocatedVitality;
            int totalAgi = allocatedAgility;
            int totalSpirit = allocatedSpirit;

            switch (stat)
            {
                case StatType.Strength: return totalStr;
                case StatType.Vitality: return totalVit;
                case StatType.Agility: return totalAgi;
                case StatType.Spirit: return totalSpirit;

                // Derived Combat Stat Formulas
                case StatType.AttackPower:
                    float weaponAP = characterClass != null ? characterClass.baseWeaponAP : 0f;
                    return totalStr + totalSpirit + weaponAP;

                case StatType.DefensePower:
                    float weaponDef = characterClass != null ? characterClass.baseWeaponDefense : 0f;
                    return totalAgi + weaponDef;

                case StatType.HitRate:
                    return totalStr * 2f;

                case StatType.EvasionRate:
                    return (totalAgi * 2f) + (totalVit * 1f);

                case StatType.MaxHP:
                    float baseHP = characterClass != null ? characterClass.baseHealthPool : 0f;
                    return baseHP + (totalVit * 20f);

                case StatType.MaxChi:
                    float baseChi = characterClass != null ? characterClass.baseChiPool : 0f;
                    return baseChi + (totalSpirit * 20f);

                case StatType.CriticalRate: return 15f; // Baseline 15%
                case StatType.CriticalDamagePercent: return 0f;
                case StatType.ElementalAttack: return 0f;
                case StatType.ElementalDefense: return 0f;
                case StatType.AcupressureStunResistance: return 0f;
                case StatType.Luck: return 0f;
                case StatType.MoveSpeedPercent: return 100f; // Baseline 100% speed

                default: return 0f;
            }
        }
        #endregion
    }
}
2.5 Combat Calculation Engine (CombatCalculator.cs)
C#
using UnityEngine;

namespace GameEngine.Stats
{
    public static class CombatCalculator
    {
        public struct DamageResult
        {
            public float DamageDealt;
            public bool IsCritical;
            public bool IsEvaded;
            public bool IsReflected;
            public float ReflectedDamage;
        }

        public static DamageResult CalculateAttack(CharacterStats attacker, CharacterStats defender, float skillMultiplier = 1.0f)
        {
            DamageResult result = new DamageResult();

            // 1. Hit & Evasion Roll
            float attackerHit = attacker.GetStatValue(StatType.HitRate);
            float defenderEvade = defender.GetStatValue(StatType.EvasionRate);
            float hitChance = Mathf.Clamp((attackerHit / Mathf.Max(1f, attackerHit + defenderEvade)) * 100f, 10f, 95f);

            if (Random.Range(0f, 100f) > hitChance)
            {
                result.IsEvaded = true;
                return result;
            }

            // 2. Reactive Damage Reflection Roll (Defensive Repulsion Buffs)
            bool defenderHasReflection = false; // Resolved via active buff lookup
            if (defenderHasReflection && Random.Range(0f, 100f) <= 10f)
            {
                result.IsReflected = true;
                result.DamageDealt = 0f;
                result.ReflectedDamage = attacker.GetStatValue(StatType.AttackPower) * skillMultiplier;
                return result;
            }

            // 3. Physical Attack Calculation
            float rawAP = attacker.GetStatValue(StatType.AttackPower) * skillMultiplier;
            float targetDP = defender.GetStatValue(StatType.DefensePower);
            float physicalDamage = Mathf.Max(1f, rawAP - targetDP);

            // 4. Critical Damage Roll
            float critChance = attacker.GetStatValue(StatType.CriticalRate);
            if (Random.Range(0f, 100f) <= critChance)
            {
                result.IsCritical = true;
                float critBonusMult = 1.5f + (attacker.GetStatValue(StatType.CriticalDamagePercent) / 100f);
                physicalDamage *= critBonusMult;
            }

            // 5. Elemental Attack Calculation (Bypasses Physical DP)
            float eleAttack = attacker.GetStatValue(StatType.ElementalAttack);
            float eleDefense = defender.GetStatValue(StatType.ElementalDefense);
            float netElementalDamage = Mathf.Max(0f, eleAttack - eleDefense);

            result.DamageDealt = physicalDamage + netElementalDamage;
            return result;
        }
    }
}
3. Unity UI Character Sheet Controller
Attach CharacterSheetUI.cs to your main UI Canvas or Character Panel. It handles point allocation, pending point previews, and dynamic stat updates.

C#
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using GameEngine.Stats;

namespace GameEngine.UI
{
    public class CharacterSheetUI : MonoBehaviour
    {
        [Header("Target Character Reference")]
        [SerializeField] private CharacterStats characterStats;

        [Header("Stat Point Allocation Management")]
        [SerializeField] private TextMeshProUGUI unallocatedPointsText;
        public int currentUnallocatedPoints = 10;

        [System.Serializable]
        public struct StatAllocationUI
        {
            public StatType statType;
            public TextMeshProUGUI attributeValueText;
            public Button addButton;
            public Button subtractButton;
            [HideInInspector] public int pendingPoints;
        }

        [Header("Primary Attribute Controls (STR, VIT, AGI, CHI)")]
        [SerializeField] private List<StatAllocationUI> primaryAttributeControls;

        [Header("Derived Combat Stat Displays")]
        [SerializeField] private TextMeshProUGUI attackPowerText;
        [SerializeField] private TextMeshProUGUI defensePowerText;
        [SerializeField] private TextMeshProUGUI hitRateText;
        [SerializeField] private TextMeshProUGUI evasionRateText;
        [SerializeField] private TextMeshProUGUI maxHPText;
        [SerializeField] private TextMeshProUGUI maxChiText;
        [SerializeField] private TextMeshProUGUI criticalRateText;
        [SerializeField] private TextMeshProUGUI moveSpeedText;

        [Header("Action Buttons")]
        [SerializeField] private Button applyButton;
        [SerializeField] private Button resetButton;

        private void Start()
        {
            if (characterStats == null)
            {
                characterStats = FindObjectOfType<CharacterStats>();
            }

            InitializeUIEvents();
            RefreshUI();
        }

        private void OnEnable()
        {
            if (characterStats != null)
            {
                characterStats.OnStatsRecalculated += RefreshUI;
            }
        }

        private void OnDisable()
        {
            if (characterStats != null)
            {
                characterStats.OnStatsRecalculated -= RefreshUI;
            }
        }

        private void InitializeUIEvents()
        {
            for (int i = 0; i < primaryAttributeControls.Count; i++)
            {
                int index = i; // Closure capture
                var control = primaryAttributeControls[index];

                if (control.addButton != null)
                    control.addButton.onClick.AddListener(() => OnAddPoint(index));

                if (control.subtractButton != null)
                    control.subtractButton.onClick.AddListener(() => OnSubtractPoint(index));
            }

            if (applyButton != null)
                applyButton.onClick.AddListener(ApplyPendingPoints);

            if (resetButton != null)
                resetButton.onClick.AddListener(ResetPendingPoints);
        }

        #region Point Allocation Logic
        private void OnAddPoint(int controlIndex)
        {
            if (currentUnallocatedPoints <= 0) return;

            var control = primaryAttributeControls[controlIndex];
            control.pendingPoints++;
            primaryAttributeControls[controlIndex] = control;

            currentUnallocatedPoints--;
            RefreshUI();
        }

        private void OnSubtractPoint(int controlIndex)
        {
            var control = primaryAttributeControls[controlIndex];
            if (control.pendingPoints <= 0) return;

            control.pendingPoints--;
            primaryAttributeControls[controlIndex] = control;

            currentUnallocatedPoints++;
            RefreshUI();
        }

        public void ApplyPendingPoints()
        {
            foreach (var control in primaryAttributeControls)
            {
                if (control.pendingPoints <= 0) continue;

                switch (control.statType)
                {
                    case StatType.Strength:
                        characterStats.allocatedStrength += control.pendingPoints;
                        break;
                    case StatType.Vitality:
                        characterStats.allocatedVitality += control.pendingPoints;
                        break;
                    case StatType.Agility:
                        characterStats.allocatedAgility += control.pendingPoints;
                        break;
                    case StatType.Spirit:
                        characterStats.allocatedSpirit += control.pendingPoints;
                        break;
                }
            }

            ClearPendingPoints();
            RefreshUI();
        }

        public void ResetPendingPoints()
        {
            int totalReturned = 0;
            for (int i = 0; i < primaryAttributeControls.Count; i++)
            {
                totalReturned += primaryAttributeControls[i].pendingPoints;
                var control = primaryAttributeControls[i];
                control.pendingPoints = 0;
                primaryAttributeControls[i] = control;
            }

            currentUnallocatedPoints += totalReturned;
            RefreshUI();
        }

        private void ClearPendingPoints()
        {
            for (int i = 0; i < primaryAttributeControls.Count; i++)
            {
                var control = primaryAttributeControls[i];
                control.pendingPoints = 0;
                primaryAttributeControls[i] = control;
            }
        }
        #endregion

        #region UI Refresh Engine
        public void RefreshUI()
        {
            if (characterStats == null) return;

            // 1. Update Remaining Points
            if (unallocatedPointsText != null)
                unallocatedPointsText.text = $"Stat Points: <color=#FFD700>{currentUnallocatedPoints}</color>";

            // 2. Refresh Primary Attributes (STR, VIT, AGI, CHI) + Pending Indicator
            for (int i = 0; i < primaryAttributeControls.Count; i++)
            {
                var control = primaryAttributeControls[i];
                int currentAllocated = GetAllocatedStatValue(control.statType);

                if (control.attributeValueText != null)
                {
                    if (control.pendingPoints > 0)
                    {
                        control.attributeValueText.text = $"{currentAllocated} <color=#00FF00>(+{control.pendingPoints})</color>";
                    }
                    else
                    {
                        control.attributeValueText.text = $"{currentAllocated}";
                    }
                }

                // Button Interactability
                if (control.addButton != null)
                    control.addButton.interactable = currentUnallocatedPoints > 0;

                if (control.subtractButton != null)
                    control.subtractButton.interactable = control.pendingPoints > 0;
            }

            // 3. Update Derived Combat Stats
            if (attackPowerText != null)
                attackPowerText.text = Mathf.RoundToInt(characterStats.GetStatValue(StatType.AttackPower)).ToString();

            if (defensePowerText != null)
                defensePowerText.text = Mathf.RoundToInt(characterStats.GetStatValue(StatType.DefensePower)).ToString();

            if (hitRateText != null)
                hitRateText.text = Mathf.RoundToInt(characterStats.GetStatValue(StatType.HitRate)).ToString();

            if (evasionRateText != null)
                evasionRateText.text = Mathf.RoundToInt(characterStats.GetStatValue(StatType.EvasionRate)).ToString();

            if (maxHPText != null)
                maxHPText.text = Mathf.RoundToInt(characterStats.GetStatValue(StatType.MaxHP)).ToString();

            if (maxChiText != null)
                maxChiText.text = Mathf.RoundToInt(characterStats.GetStatValue(StatType.MaxChi)).ToString();

            if (criticalRateText != null)
                criticalRateText.text = $"{characterStats.GetStatValue(StatType.CriticalRate):F1}%";

            if (moveSpeedText != null)
                moveSpeedText.text = $"{characterStats.GetStatValue(StatType.MoveSpeedPercent):F0}%";

            // Action Buttons
            bool hasPending = HasAnyPendingPoints();
            if (applyButton != null) applyButton.interactable = hasPending;
            if (resetButton != null) resetButton.interactable = hasPending;
        }

        private int GetAllocatedStatValue(StatType type)
        {
            switch (type)
            {
                case StatType.Strength: return characterStats.allocatedStrength;
                case StatType.Vitality: return characterStats.allocatedVitality;
                case StatType.Agility: return characterStats.allocatedAgility;
                case StatType.Spirit: return characterStats.allocatedSpirit;
                default: return 0;
            }
        }

        private bool HasAnyPendingPoints()
        {
            foreach (var control in primaryAttributeControls)
            {
                if (control.pendingPoints > 0) return true;
            }
            return false;
        }
        #endregion
    }
}