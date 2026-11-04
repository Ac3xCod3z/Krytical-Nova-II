using KryticalNova.Core;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Progression.Tests
{
    /// <summary>CombatCalculator hands each stat to the one formula (<see cref="CombatMath"/>, M4) under its own name.</summary>
    public sealed class CombatCalculatorTests
    {
        static readonly StatType[] Fed =
        {
            StatType.AttackPower, StatType.DefensePower, StatType.HitRate, StatType.EvasionRate, StatType.CriticalRate,
            StatType.CriticalDamagePercent, StatType.CriticalDefensePercent, StatType.ElementalAttack, StatType.ElementalDefense,
            StatType.AcupressureStunResistance,
        };

        static CharacterCombatStats Stats(float first)
        {
            var go = new GameObject("CombatCalculatorTests");
            var stats = go.AddComponent<CharacterCombatStats>();
            var mods = new System.Collections.Generic.List<StatModifier>();
            // a different flat bonus on every stat, so a stat read under the wrong name changes the result
            for (int i = 0; i < Fed.Length; i++) mods.Add(new StatModifier(Fed[i], ModifierType.Flat, first + i * 7f, stats));
            stats.SetSourceModifiers(stats, mods);
            return stats;
        }

        static CombatStatBlock Read(CharacterCombatStats s) => new CombatStatBlock
        {
            AttackPower = s.GetStatValue(StatType.AttackPower), DefensePower = s.GetStatValue(StatType.DefensePower),
            HitRate = s.GetStatValue(StatType.HitRate), EvasionRate = s.GetStatValue(StatType.EvasionRate),
            CriticalRate = s.GetStatValue(StatType.CriticalRate), CriticalDamagePercent = s.GetStatValue(StatType.CriticalDamagePercent),
            CriticalDefensePercent = s.GetStatValue(StatType.CriticalDefensePercent), ElementalAttack = s.GetStatValue(StatType.ElementalAttack),
            ElementalDefense = s.GetStatValue(StatType.ElementalDefense), StunResistance = s.GetStatValue(StatType.AcupressureStunResistance),
        };

        [Test]
        public void TheCalculator_GivesTheOneFormulaEveryStatUnderItsOwnName()
        {
            CharacterCombatStats a = Stats(30f), d = Stats(11f);
            try
            {
                var strike = new CombatStrike { SkillMultiplier = 1.35f, ReflectionChance = 10f, ShatterChance = 10f, AttackerHasShieldShatter = true, HitChanceModifier = -5f };
                foreach (var rolls in new[]
                {
                    new CombatRolls { Hit = 5f, Reflect = 50f, Crit = 1f, Shatter = 3f },   // hit, crit, shatter
                    new CombatRolls { Hit = 5f, Reflect = 50f, Crit = 99f, Shatter = 90f },  // hit, no crit
                    new CombatRolls { Hit = 99f, Reflect = 50f, Crit = 1f, Shatter = 3f },   // missed
                })
                {
                    DamageResult want = CombatMath.Resolve(Read(a), Read(d), CombatTuning.Default, strike, rolls);
                    DamageResult got = CombatCalculator.CalculateAttackInternal(a, d, strike.SkillMultiplier, rolls.Hit, rolls.Reflect, rolls.Crit, rolls.Shatter,
                        false, strike.ReflectionChance, true, strike.ShatterChance, strike.HitChanceModifier);
                    Assert.AreEqual(want.HitChance, got.HitChance, 1e-4);
                    Assert.AreEqual(want.IsEvaded, got.IsEvaded);
                    Assert.AreEqual(want.IsCritical, got.IsCritical);
                    Assert.AreEqual(want.ShieldShattered, got.ShieldShattered);
                    Assert.AreEqual(want.NetElementalDamage, got.NetElementalDamage, 1e-4);
                    Assert.AreEqual(want.DamageDealt, got.DamageDealt, 1e-4);
                }
                Assert.Greater(CombatCalculator.CalculateAttackInternal(a, d, 1f, 5f, 50f, 99f, 90f, false, 10f, false, 10f).DamageDealt, 1f, "a real hit");
            }
            finally
            {
                Object.DestroyImmediate(a.gameObject);
                Object.DestroyImmediate(d.gameObject);
            }
        }
    }
}
