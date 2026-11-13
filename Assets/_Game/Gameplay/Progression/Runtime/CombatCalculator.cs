using KryticalNova.Core;
using UnityEngine;

namespace KryticalNova.Progression
{
    /// <summary>Deterministic combat resolution pipeline using designer ScriptableObject tuning; the formula itself is <see cref="CombatMath"/>.</summary>
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
            float hitChanceModifier = 0f,
            bool forceCrit = false)
        {
            float hitRoll = Random.Range(0f, 100f);
            float reflectRoll = Random.Range(0f, 100f);
            // A roll above any possible Critical Rate: the strike can never be critical. One below any: it always is,
            // if it hits at all (a three-hit attack pressed on the beat with Redline charged). Evasion is rolled first.
            float critRoll = !canCrit ? float.PositiveInfinity : forceCrit ? float.NegativeInfinity : Random.Range(0f, 100f);
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
                hitChanceModifier,
                Random.Range(0f, 100f));
        }

        /// <summary>The formula's numbers from the tuning asset (build 17, item 8 included).</summary>
        public static CombatTuning Limits(CombatStatTuningData tuning) => new CombatTuning
        {
            MinHitChance = tuning.minimumHitChance, MaxHitChance = tuning.maximumHitChance, BaseCriticalMultiplier = tuning.baseCriticalMultiplier,
            DefenseK = tuning.defenseK, DefenseKPerLevel = tuning.defenseKPerLevel, DefensePenetration = tuning.defensePenetration,
            BlockCap = tuning.blockCap, BlockCut = tuning.blockCut,
            ResistFromDefense = tuning.critResistFromDefense, ResistFromEvasion = tuning.critResistFromEvasion, ResistCap = tuning.critResistCap,
            EvasionCritDamageCut = tuning.evasionCritDamageCut, EvasionBacklashBonus = tuning.evasionBacklashBonus,
        };

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
            float hitChanceModifier = 0f,
            float blockRoll = 100f)
        {
            if (attacker == null || defender == null) return new DamageResult();

            CombatStatTuningData tuning = attacker.TuningData != null ? attacker.TuningData : defender.TuningData;
            CombatTuning limits = tuning != null
                ? Limits(tuning)
                : CombatTuning.Default;
            var strike = new CombatStrike
            {
                SkillMultiplier = skillMultiplier, DefenderHasReflection = defenderHasReflection, ReflectionChance = reflectionChance,
                AttackerHasShieldShatter = attackerHasShieldShatter, ShatterChance = shatterChance, HitChanceModifier = hitChanceModifier,
            };
            var rolls = new CombatRolls { Hit = hitRoll, Reflect = reflectRoll, Crit = critRoll, Shatter = shatterRoll, Block = blockRoll };
            // The one formula of the game (M4, owner 4 Nov 2026): the entity netcode's server calls the same CombatMath.
            return CombatMath.Resolve(StatsOf(attacker), StatsOf(defender), limits, strike, rolls);
        }

        /// <summary>The stats a strike reads from one side, as the formula takes them.</summary>
        public static CombatStatBlock StatsOf(CharacterCombatStats stats) => new CombatStatBlock
        {
            AttackPower = stats.GetStatValue(StatType.AttackPower),
            DefensePower = stats.GetStatValue(StatType.DefensePower),
            HitRate = stats.GetStatValue(StatType.HitRate),
            EvasionRate = stats.GetStatValue(StatType.EvasionRate),
            CriticalRate = stats.GetStatValue(StatType.CriticalRate),
            CriticalDamagePercent = stats.GetStatValue(StatType.CriticalDamagePercent),
            CriticalDefensePercent = stats.GetStatValue(StatType.CriticalDefensePercent),
            ElementalAttack = stats.GetStatValue(StatType.ElementalAttack),
            ElementalDefense = stats.GetStatValue(StatType.ElementalDefense),
            StunResistance = stats.GetStatValue(StatType.AcupressureStunResistance),
            Level = stats.Progression != null ? stats.Progression.CurrentLevel : 1,
        };
    }
}
