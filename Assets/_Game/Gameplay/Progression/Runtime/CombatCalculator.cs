using UnityEngine;

namespace KryticalNova.Progression
{
    public struct DamageResult
    {
        public float DamageDealt;
        public bool IsCritical;
        public bool IsEvaded;
        public bool IsReflected;
        public float ReflectedDamage;
        public bool ShieldShattered;
        public float RawPhysicalDamage;
        public float MitigatedPhysicalDamage;
        public float NetElementalDamage;
        public float HitChance;
        public bool BypassesShield;
    }

    /// <summary>Deterministic combat resolution pipeline using designer ScriptableObject tuning.</summary>
    public static class CombatCalculator
    {
        /// <summary>Calculates a combat strike using live Random rolls.</summary>
        public static DamageResult CalculateAttack(
            CharacterCombatStats attacker,
            CharacterCombatStats defender,
            float skillMultiplier = 1.0f,
            bool defenderHasReflection = false,
            float reflectionChance = 10f,
            bool attackerHasShieldShatter = false,
            float shatterChance = 10f,
            bool canCrit = true,
            float hitChanceModifier = 0f)
        {
            float hitRoll = Random.Range(0f, 100f);
            float reflectRoll = Random.Range(0f, 100f);
            // A roll above any possible Critical Rate: the strike can never be critical.
            float critRoll = canCrit ? Random.Range(0f, 100f) : float.PositiveInfinity;
            float shatterRoll = Random.Range(0f, 100f);

            return CalculateAttackInternal(
                attacker,
                defender,
                skillMultiplier,
                hitRoll,
                reflectRoll,
                critRoll,
                shatterRoll,
                defenderHasReflection,
                reflectionChance,
                attackerHasShieldShatter,
                shatterChance,
                hitChanceModifier);
        }

        /// <summary>Calculates a combat strike with explicitly provided dice rolls for deterministic tests.</summary>
        public static DamageResult CalculateAttackInternal(
            CharacterCombatStats attacker,
            CharacterCombatStats defender,
            float skillMultiplier,
            float hitRoll,
            float reflectRoll,
            float critRoll,
            float shatterRoll,
            bool defenderHasReflection,
            float reflectionChance,
            bool attackerHasShieldShatter,
            float shatterChance,
            float hitChanceModifier = 0f)
        {
            DamageResult result = new DamageResult();
            if (attacker == null || defender == null) return result;

            CombatStatTuningData tuning = attacker.TuningData != null ? attacker.TuningData : defender.TuningData;
            float minHit = tuning != null ? tuning.minimumHitChance : 10f;
            float maxHit = tuning != null ? tuning.maximumHitChance : 95f;
            float baseCritMult = tuning != null ? tuning.baseCriticalMultiplier : 1.5f;

            // 1. Hit & Evasion Roll
            float attackerHit = attacker.GetStatValue(StatType.HitRate);
            float defenderEvade = defender.GetStatValue(StatType.EvasionRate);
            float rawHitChance = (attackerHit / Mathf.Max(1f, attackerHit + defenderEvade)) * 100f;
            // The modifier is in percent points (a Hostile far above the attacker is harder to hit) and never
            // takes the chance outside the limits.
            float hitChance = Mathf.Clamp(rawHitChance + hitChanceModifier, minHit, maxHit);
            result.HitChance = hitChance;

            if (hitRoll > hitChance)
            {
                result.IsEvaded = true;
                return result;
            }

            // 2. Reactive Reflection Roll (Heavenly Repulsion)
            if (defenderHasReflection && reflectRoll <= reflectionChance)
            {
                result.IsReflected = true;
                result.DamageDealt = 0f;
                result.ReflectedDamage = attacker.GetStatValue(StatType.AttackPower) * skillMultiplier;
                return result;
            }

            // 3. Offensive Shield Shatter Roll (Heavenly Destruction)
            if (attackerHasShieldShatter && shatterRoll <= shatterChance)
            {
                result.ShieldShattered = true;
            }

            // 4. Physical Attack Calculation
            float rawAP = attacker.GetStatValue(StatType.AttackPower) * skillMultiplier;
            float targetDP = defender.GetStatValue(StatType.DefensePower);
            float physicalDamage = Mathf.Max(1f, rawAP - targetDP);
            result.RawPhysicalDamage = rawAP;
            result.MitigatedPhysicalDamage = physicalDamage;

            // 5. Critical Damage Roll. Luck is intentionally excluded: it is a self-buff stat reserved for
            //    gear enhancement / loot systems and must never influence combat resolution.
            float critChance = attacker.GetStatValue(StatType.CriticalRate);
            if (critRoll <= critChance)
            {
                result.IsCritical = true;
                float critBonusMult = baseCritMult + (attacker.GetStatValue(StatType.CriticalDamagePercent) / 100f);
                // Critical Defense (capes) trims the bonus part of the crit, never the base hit. 0 for everyone without it.
                float critDefense = Mathf.Clamp(defender.GetStatValue(StatType.CriticalDefensePercent), 0f, 100f);
                if (critDefense > 0f) critBonusMult = 1f + (Mathf.Max(1f, critBonusMult) - 1f) * (1f - critDefense / 100f);
                physicalDamage *= critBonusMult;

                // Acupressure Resistance mitigates fatal/critical trauma
                float stunRes = defender.GetStatValue(StatType.AcupressureStunResistance);
                if (stunRes > 0f)
                {
                    physicalDamage *= Mathf.Max(0.7f, 1f - (stunRes / 100f * 0.5f));
                }

                result.MitigatedPhysicalDamage = physicalDamage;
            }

            // 6. Elemental Attack Calculation (Bypasses Physical DP)
            float eleAttack = attacker.GetStatValue(StatType.ElementalAttack);
            float eleDefense = defender.GetStatValue(StatType.ElementalDefense);
            float netElementalDamage = Mathf.Max(0f, eleAttack - eleDefense);
            result.NetElementalDamage = netElementalDamage;

            result.DamageDealt = physicalDamage + netElementalDamage;
            return result;
        }
    }
}
