namespace KryticalNova.Core
{
    /// <summary>The stats one side of a strike brings (each as <c>CharacterCombatStats.GetStatValue</c> gives it).</summary>
    public struct CombatStatBlock
    {
        public float AttackPower, DefensePower, HitRate, EvasionRate;
        public float CriticalRate, CriticalDamagePercent, CriticalDefensePercent;
        public float ElementalAttack, ElementalDefense, StunResistance;
    }

    /// <summary>The designer limits of the formula (<c>CombatStatTuningData</c>); <see cref="Default"/> when there is no asset.</summary>
    public struct CombatTuning
    {
        public float MinHitChance, MaxHitChance, BaseCriticalMultiplier;

        public static CombatTuning Default => new CombatTuning { MinHitChance = 10f, MaxHitChance = 95f, BaseCriticalMultiplier = 1.5f };
    }

    /// <summary>What kind of strike it is: the skill's multiplier, the reflection and shatter buffs in play, a hit-chance modifier.</summary>
    public struct CombatStrike
    {
        public float SkillMultiplier;
        public bool DefenderHasReflection;
        public float ReflectionChance;
        public bool AttackerHasShieldShatter;
        public float ShatterChance;
        /// <summary>Percent points on the hit chance (a Hostile far above the attacker is harder to hit).</summary>
        public float HitChanceModifier;
    }

    /// <summary>The four dice of a strike, each 0-100 (a crit roll of +infinity never crits, -infinity always does if it hits).</summary>
    public struct CombatRolls
    {
        public float Hit, Reflect, Crit, Shatter;
    }

    /// <summary>
    /// The one damage formula of the game (owner, 4 Nov 2026: one shared formula for the old netcode and the entity netcode, M4): plain
    /// structs in, a <see cref="DamageResult"/> out, no Unity objects and no random draws, so a Burst job and <c>CombatCalculator</c> call the
    /// same code. The steps and their order are the managed formula's, kept to the bit (CombatMathTests compare it with a copy of it).
    /// </summary>
    public static class CombatMath
    {
        public static DamageResult Resolve(in CombatStatBlock attacker, in CombatStatBlock defender, in CombatTuning tuning, in CombatStrike strike, in CombatRolls rolls)
        {
            var result = new DamageResult();

            // 1. Hit & Evasion Roll; the modifier never takes the chance outside the limits
            float rawHitChance = (attacker.HitRate / Max(1f, attacker.HitRate + defender.EvasionRate)) * 100f;
            float hitChance = Clamp(rawHitChance + strike.HitChanceModifier, tuning.MinHitChance, tuning.MaxHitChance);
            result.HitChance = hitChance;
            if (rolls.Hit > hitChance)
            {
                result.IsEvaded = true;
                return result;
            }

            // 2. Reactive Reflection Roll (Heavenly Repulsion)
            if (strike.DefenderHasReflection && rolls.Reflect <= strike.ReflectionChance)
            {
                result.IsReflected = true;
                result.DamageDealt = 0f;
                result.ReflectedDamage = attacker.AttackPower * strike.SkillMultiplier;
                return result;
            }

            // 3. Offensive Shield Shatter Roll (Heavenly Destruction)
            if (strike.AttackerHasShieldShatter && rolls.Shatter <= strike.ShatterChance) result.ShieldShattered = true;

            // 4. Physical Attack Calculation
            float rawAP = attacker.AttackPower * strike.SkillMultiplier;
            float physicalDamage = Max(1f, rawAP - defender.DefensePower);
            result.RawPhysicalDamage = rawAP;
            result.MitigatedPhysicalDamage = physicalDamage;

            // 5. Critical Damage Roll (Luck never counts in combat)
            if (rolls.Crit <= attacker.CriticalRate)
            {
                result.IsCritical = true;
                float critBonusMult = tuning.BaseCriticalMultiplier + (attacker.CriticalDamagePercent / 100f);
                // Critical Defense (capes) trims the bonus part of the crit, never the base hit
                float critDefense = Clamp(defender.CriticalDefensePercent, 0f, 100f);
                if (critDefense > 0f) critBonusMult = 1f + (Max(1f, critBonusMult) - 1f) * (1f - critDefense / 100f);
                physicalDamage *= critBonusMult;
                // Acupressure Resistance mitigates fatal/critical trauma
                if (defender.StunResistance > 0f) physicalDamage *= Max(0.7f, 1f - (defender.StunResistance / 100f * 0.5f));
                result.MitigatedPhysicalDamage = physicalDamage;
            }

            // 6. Elemental Attack Calculation (bypasses Physical DP)
            float netElementalDamage = Max(0f, attacker.ElementalAttack - defender.ElementalDefense);
            result.NetElementalDamage = netElementalDamage;
            result.DamageDealt = physicalDamage + netElementalDamage;
            return result;
        }

        // Mathf's own rules (not math.max / math.clamp, which differ on NaN), so the result is the managed formula's to the bit.
        private static float Max(float a, float b) => a > b ? a : b;

        private static float Clamp(float value, float min, float max)
        {
            if (value < min) value = min;
            else if (value > max) value = max;
            return value;
        }
    }
}
