using System;
using UnityEngine;

namespace KryticalNova.Progression
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
        [field: NonSerialized]
        public object Source { get; set; } // Identifies buff, item, or stance causing the modifier

        public StatModifier(StatType stat, ModifierType modType, float value, object source = null)
        {
            Stat = stat;
            ModType = modType;
            Value = value;
            Source = source;
        }
    }
}
