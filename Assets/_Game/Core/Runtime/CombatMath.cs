namespace KryticalNova.Core
{
    /// <summary>The stats one side of a strike brings (each as <c>CharacterCombatStats.GetStatValue</c> gives it).</summary>
    public struct CombatStatBlock
    {
        public float AttackPower, DefensePower, HitRate, EvasionRate;
        public float CriticalRate, CriticalDamagePercent, CriticalDefensePercent;
        public float ElementalAttack, ElementalDefense, StunResistance;
        /// <summary>The character's level (the Defense curve's K grows with the attacker's level). 0 or 1 = level 1.</summary>
        public float Level;
    }

    /// <summary>The designer limits of the formula (<c>CombatStatTuningData</c>); <see cref="Default"/> when there is no asset.</summary>
    public struct CombatTuning
    {
        public float MinHitChance, MaxHitChance, BaseCriticalMultiplier;

        // Build 17, item 8 (owner pick 13 Nov 2026: the proposed model + a tankier Block + Evasion cuts crit damage). All 0 = the
        // formula before item 8 (subtractive Defense, no Block, no crit resistance), which Default keeps for the old tests.
        /// <summary>K = DefenseK + DefenseKPerLevel x attacker level; damage x K / (K + Defense). 0 = subtractive Defense.</summary>
        public float DefenseK, DefenseKPerLevel, DefensePenetration;
        /// <summary>Block chance = BlockCap x DP / (DP + K) percent (never above BlockCap); a blocked hit loses BlockCut percent.</summary>
        public float BlockCap, BlockCut;
        /// <summary>Crit resistance (percent off the attacker's crit rate) = DP share x ResistFromDefense + Evasion share x ResistFromEvasion + cape, cap ResistCap.</summary>
        public float ResistFromDefense, ResistFromEvasion, ResistCap;
        /// <summary>Percent of a crit's bonus damage Evasion takes away at Evasion = the attacker's Hit (scaled by the Evasion share).</summary>
        public float EvasionCritDamageCut;
        /// <summary>Percent points Evasion adds to the Backlash chance while that buff is up (scaled by the Evasion share).</summary>
        public float EvasionBacklashBonus;

        public bool DefenseCurve => DefenseK > 0f;

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
        /// <summary>Build 17, item 8: the Block die, 0-100. Only read when the tuning has a Block (BlockCap above 0).</summary>
        public float Block;
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

            // The defender's share of Evasion against this attacker's Hit (0-1): what Evasion buys besides the dodge (item 8).
            float evasionShare = Clamp(2f * defender.EvasionRate / Max(1f, defender.EvasionRate + attacker.HitRate), 0f, 1f);
            float k = tuning.DefenseCurve ? tuning.DefenseK + tuning.DefenseKPerLevel * Max(1f, attacker.Level) : 0f;
            float defenseShare = tuning.DefenseCurve ? Max(0f, defender.DefensePower) / Max(1f, Max(0f, defender.DefensePower) + k) : 0f;

            // 2. Reactive Reflection Roll (Heavenly Repulsion); Evasion adds a little while the buff is up (item 8)
            float reflectionChance = strike.ReflectionChance + (strike.DefenderHasReflection ? tuning.EvasionBacklashBonus * evasionShare * 0.5f : 0f);
            if (strike.DefenderHasReflection && rolls.Reflect <= reflectionChance)
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
            float physicalDamage = tuning.DefenseCurve
                // item 8: a curve, never immunity: Defense equal to K halves the hit
                ? Max(1f, rawAP * k / (k + Max(0f, defender.DefensePower) * (1f - Clamp(tuning.DefensePenetration, 0f, 100f) / 100f)))
                : Max(1f, rawAP - defender.DefensePower);
            result.RawPhysicalDamage = rawAP;

            // 4b. Block (item 8): rolled after the hit, so Evasion and Block stack; a blocked hit loses BlockCut percent
            if (tuning.BlockCap > 0f && rolls.Block < Min(tuning.BlockCap, tuning.BlockCap * defenseShare))
            {
                result.IsBlocked = true;
                physicalDamage = Max(1f, physicalDamage * (1f - Clamp(tuning.BlockCut, 0f, 100f) / 100f));
            }
            result.MitigatedPhysicalDamage = physicalDamage;

            // 5. Critical Damage Roll (Luck never counts in combat); Defense and Evasion resist it (item 8)
            float critRate = attacker.CriticalRate;
            if (tuning.ResistCap > 0f)
            {
                float resist = Min(tuning.ResistCap,
                    tuning.ResistFromDefense * defenseShare + tuning.ResistFromEvasion * evasionShare + Clamp(defender.CriticalDefensePercent, 0f, 100f));
                critRate *= 1f - Clamp(resist, 0f, 100f) / 100f;
            }
            if (rolls.Crit <= critRate)
            {
                result.IsCritical = true;
                float critBonusMult = tuning.BaseCriticalMultiplier + (attacker.CriticalDamagePercent / 100f);
                // Evasion takes part of the crit's bonus away (item 8, owner pick C): agile builds beat slow heavy hitters
                if (tuning.EvasionCritDamageCut > 0f)
                    critBonusMult = 1f + (Max(1f, critBonusMult) - 1f) * (1f - Clamp(tuning.EvasionCritDamageCut * evasionShare, 0f, 100f) / 100f);
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
        private static float Min(float a, float b) => a < b ? a : b;

        private static float Clamp(float value, float min, float max)
        {
            if (value < min) value = min;
            else if (value > max) value = max;
            return value;
        }
    }
}
