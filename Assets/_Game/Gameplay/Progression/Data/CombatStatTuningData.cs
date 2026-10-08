using UnityEngine;

namespace KryticalNova.Progression
{
    /// <summary>Designer-tunable formulas, scaling ratios, and baseline combat values for derived stats and combat checks.</summary>
    [CreateAssetMenu(menuName = "Krytical Nova/Combat Stat Tuning")]
    public sealed class CombatStatTuningData : ScriptableObject
    {
        [Header("Primary Attribute Multipliers")]
        [Tooltip("Attack Power gained per point of Strength.")]
        [Min(0f)] public float strengthToAttackPower = 1f;

        [Tooltip("Attack Power gained per point of Spirit/Chi.")]
        [Min(0f)] public float spiritToAttackPower = 1f;

        [Tooltip("Hit Rate gained per point of Strength.")]
        [Min(0f)] public float strengthToHitRate = 2f;

        [Tooltip("Defense Power gained per point of Agility.")]
        [Min(0f)] public float agilityToDefensePower = 1f;

        [Tooltip("Evasion Rate gained per point of Agility.")]
        [Min(0f)] public float agilityToEvasion = 2f;

        [Tooltip("Evasion Rate gained per point of Vitality.")]
        [Min(0f)] public float vitalityToEvasion = 1f;

        [Tooltip("Max HP gained per point of Vitality.")]
        [Min(0f)] public float vitalityToHealth = 20f;

        [Tooltip("Max Chi gained per point of Spirit.")]
        [Min(0f)] public float spiritToChi = 20f;

        [Header("Level Scaling")]
        [Tooltip("Max HP gained per character level above 1.")]
        [Min(0f)] public float healthPerLevel = 15f;

        [Tooltip("Max Chi gained per character level above 1.")]
        [Min(0f)] public float chiPerLevel = 10f;

        [Header("Baseline Combat Stats")]
        [Tooltip("Baseline critical strike chance percentage (e.g. 15 for 15%).")]
        [Range(0f, 100f)] public float baselineCriticalRate = 15f;

        [Tooltip("Base critical damage multiplier before bonus critical damage % (e.g. 1.5 for 150% damage).")]
        [Min(1f)] public float baseCriticalMultiplier = 1.5f;

        [Tooltip("Baseline movement speed percentage.")]
        [Min(0f)] public float baselineMoveSpeedPercent = 100f;

        [Tooltip("Baseline elemental attack value.")]
        [Min(0f)] public float baselineElementalAttack = 0f;

        [Tooltip("Baseline elemental defense value.")]
        [Min(0f)] public float baselineElementalDefense = 0f;

        [Tooltip("Baseline Acupressure Stun Resistance percentage.")]
        [Range(0f, 100f)] public float baselineStunResistance = 0f;

        [Tooltip("Baseline Luck before gear, pets, mounts and buffs. Not read by combat resolution.")]
        [Min(0f)] public float baseLuck = 50f;

        [Header("Hit & Evasion Roll Clamps")]
        [Tooltip("Minimum guaranteed hit chance percentage regardless of target evasion.")]
        [Range(0f, 50f)] public float minimumHitChance = 10f;

        [Tooltip("Maximum hit chance percentage cap.")]
        [Range(50f, 100f)] public float maximumHitChance = 95f;

        [Header("How a hit is shown")]
        [Tooltip("A hit is shown as Elemental (its own colours on every screen) when at least this share of its damage is the Elemental part. Looks only: no damage depends on it.")]
        [Range(0f, 1f)] public float elementalHitShare = 0.35f;

        private void OnValidate()
        {
            strengthToAttackPower = Mathf.Max(0f, strengthToAttackPower);
            spiritToAttackPower = Mathf.Max(0f, spiritToAttackPower);
            strengthToHitRate = Mathf.Max(0f, strengthToHitRate);
            agilityToDefensePower = Mathf.Max(0f, agilityToDefensePower);
            agilityToEvasion = Mathf.Max(0f, agilityToEvasion);
            vitalityToEvasion = Mathf.Max(0f, vitalityToEvasion);
            vitalityToHealth = Mathf.Max(0f, vitalityToHealth);
            spiritToChi = Mathf.Max(0f, spiritToChi);
            healthPerLevel = Mathf.Max(0f, healthPerLevel);
            chiPerLevel = Mathf.Max(0f, chiPerLevel);
            baselineCriticalRate = Mathf.Clamp(baselineCriticalRate, 0f, 100f);
            baseCriticalMultiplier = Mathf.Max(1f, baseCriticalMultiplier);
            baselineMoveSpeedPercent = Mathf.Max(0f, baselineMoveSpeedPercent);
            baselineElementalAttack = Mathf.Max(0f, baselineElementalAttack);
            baselineElementalDefense = Mathf.Max(0f, baselineElementalDefense);
            baselineStunResistance = Mathf.Clamp(baselineStunResistance, 0f, 100f);
            baseLuck = Mathf.Max(0f, baseLuck);
            minimumHitChance = Mathf.Clamp(minimumHitChance, 0f, maximumHitChance);
            maximumHitChance = Mathf.Clamp(maximumHitChance, minimumHitChance, 100f);
        }
    }
}
