namespace KryticalNova.Progression
{
    /// <summary>
    /// The player-facing name and number format of every stat, in one place, so tooltips and the Character Sheet
    /// read the same ("Flux", "Lockup Resist") and never show an enum name.
    /// </summary>
    public static class StatDisplay
    {
        public static string Name(StatType stat)
        {
            switch (stat)
            {
                case StatType.Strength: return "Strength";
                case StatType.Vitality: return "Vitality";
                case StatType.Agility: return "Agility";
                case StatType.Spirit: return "Spirit";
                case StatType.AttackPower: return "Attack Power";
                case StatType.DefensePower: return "Defense";
                case StatType.HitRate: return "Hit Rate";
                case StatType.EvasionRate: return "Evasion";
                case StatType.MaxHP: return "Max HP";
                case StatType.MaxChi: return "Max Flux";
                case StatType.CriticalRate: return "Critical Rate";
                case StatType.CriticalDamagePercent: return "Critical Damage";
                case StatType.ElementalAttack: return "Elemental Attack";
                case StatType.ElementalDefense: return "Elemental Defense";
                case StatType.AcupressureStunResistance: return "Lockup Resist";
                case StatType.Luck: return "Luck";
                case StatType.MoveSpeedPercent: return "Move Speed";
                case StatType.AttackSpeedPercent: return "Attack Speed";
                case StatType.CoreResistancePercent: return "Core Resist";
                case StatType.KnockbackResistancePercent: return "Knockback Resist";
                case StatType.CriticalDefensePercent: return "Critical Defense";
                case StatType.KillExpRatePercent: return "Kill-XP Rate";
                case StatType.KillExpAmountPercent: return "Kill-XP Amount";
                default: return stat.ToString();
            }
        }

        /// <summary>True for stats whose value is already a percentage (Critical Rate 15 means 15%).</summary>
        public static bool IsPercentStat(StatType stat)
        {
            switch (stat)
            {
                case StatType.CriticalRate:
                case StatType.CriticalDamagePercent:
                case StatType.AcupressureStunResistance:
                case StatType.MoveSpeedPercent:
                case StatType.AttackSpeedPercent:
                case StatType.CoreResistancePercent:
                case StatType.KnockbackResistancePercent:
                case StatType.CriticalDefensePercent:
                case StatType.KillExpRatePercent:
                case StatType.KillExpAmountPercent:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>One bonus line, e.g. "+884 Attack Power" or "+18% Critical Damage".</summary>
        public static string FormatModifier(StatType stat, ModifierType type, float value)
        {
            bool percent = type != ModifierType.Flat || IsPercentStat(stat);
            string number = value.ToString(percent ? "+0.#;-0.#" : "+0;-0");
            return percent ? $"{number}% {Name(stat)}" : $"{number} {Name(stat)}";
        }
    }
}
