using KryticalNova.Core;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Tests
{
    /// <summary>
    /// The one damage formula (M4, owner 4 Nov 2026): hand-worked strikes, and 10 000 random strikes against a copy of the managed
    /// formula as it was before <see cref="CombatMath"/> (CombatCalculator.CalculateAttackInternal, 3 Nov 2026).
    /// </summary>
    public sealed class CombatMathTests
    {
        // A level-1 Guanyin Blade with no gear: AP 25, DP 15, Hit 20, Evasion 30, crit 15 %
        static CombatStatBlock Blade() => new CombatStatBlock { AttackPower = 25f, DefensePower = 15f, HitRate = 20f, EvasionRate = 30f, CriticalRate = 15f };
        static CombatStatBlock Dummy() => new CombatStatBlock { DefensePower = 5f, HitRate = 20f, EvasionRate = 30f };
        static CombatStrike Plain() => new CombatStrike { SkillMultiplier = 1f, ReflectionChance = 10f, ShatterChance = 10f };
        static CombatRolls Rolls(float hit, float crit, float reflect = 50f, float shatter = 50f) => new CombatRolls { Hit = hit, Crit = crit, Reflect = reflect, Shatter = shatter };

        [Test]
        public void HandWorked_HitMissCritReflectFloorAndElemental()
        {
            CombatTuning t = CombatTuning.Default;

            DamageResult r = CombatMath.Resolve(Blade(), Dummy(), t, Plain(), Rolls(10f, 50f));
            Assert.AreEqual(40f, r.HitChance, 1e-4, "20 / (20 + 30)");
            Assert.IsFalse(r.IsEvaded);
            Assert.AreEqual(25f, r.RawPhysicalDamage, 1e-4);
            Assert.AreEqual(20f, r.DamageDealt, 1e-4, "25 - 5");

            r = CombatMath.Resolve(Blade(), Dummy(), t, Plain(), Rolls(41f, 50f));
            Assert.IsTrue(r.IsEvaded, "a roll above the chance misses");
            Assert.AreEqual(0f, r.DamageDealt);

            r = CombatMath.Resolve(Blade(), Dummy(), t, Plain(), Rolls(10f, 5f));
            Assert.IsTrue(r.IsCritical);
            Assert.AreEqual(30f, r.DamageDealt, 1e-4, "x 1.5");

            CombatStatBlock caped = Dummy();
            caped.CriticalDefensePercent = 50f;
            caped.StunResistance = 100f;
            r = CombatMath.Resolve(Blade(), caped, t, Plain(), Rolls(10f, 5f));
            Assert.AreEqual(20f * 1.25f * 0.7f, r.DamageDealt, 1e-4, "the cape halves the bonus, the stun resistance floors at 0.7");

            CombatStrike reflected = Plain();
            reflected.DefenderHasReflection = true;
            r = CombatMath.Resolve(Blade(), Dummy(), t, reflected, Rolls(10f, 50f, reflect: 5f));
            Assert.IsTrue(r.IsReflected);
            Assert.AreEqual(0f, r.DamageDealt);
            Assert.AreEqual(25f, r.ReflectedDamage, 1e-4);

            CombatStatBlock weak = Blade();
            weak.AttackPower = 3f;
            weak.ElementalAttack = 7f;
            CombatStatBlock warded = Dummy();
            warded.ElementalDefense = 2f;
            r = CombatMath.Resolve(weak, warded, t, Plain(), Rolls(10f, 50f));
            Assert.AreEqual(1f, r.MitigatedPhysicalDamage, 1e-4, "never below 1");
            Assert.AreEqual(5f, r.NetElementalDamage, 1e-4);
            Assert.AreEqual(6f, r.DamageDealt, 1e-4);

            CombatStatBlock blind = Blade();
            blind.HitRate = 1f;
            CombatStatBlock nimble = Dummy();
            nimble.EvasionRate = 1000f;
            Assert.AreEqual(10f, CombatMath.Resolve(blind, nimble, t, Plain(), Rolls(10f, 50f)).HitChance, 1e-4, "the floor");
            CombatStrike easy = Plain();
            easy.HitChanceModifier = 90f;
            Assert.AreEqual(95f, CombatMath.Resolve(Blade(), Dummy(), t, easy, Rolls(10f, 50f)).HitChance, 1e-4, "the ceiling");

            Assert.IsFalse(CombatMath.Resolve(Blade(), Dummy(), t, Plain(), Rolls(10f, float.PositiveInfinity)).IsCritical, "canCrit false");
            Assert.IsTrue(CombatMath.Resolve(Blade(), Dummy(), t, Plain(), Rolls(10f, float.NegativeInfinity)).IsCritical, "forceCrit");
        }

        [Test]
        public void TenThousandRandomStrikes_EqualTheManagedFormula()
        {
            var rng = new System.Random(4112026);
            float F(float lo, float hi) => lo + (float)rng.NextDouble() * (hi - lo);
            for (int i = 0; i < 10000; i++)
            {
                var a = new CombatStatBlock
                {
                    AttackPower = F(0f, 600f), DefensePower = F(0f, 300f), HitRate = F(0f, 300f), EvasionRate = F(0f, 300f), CriticalRate = F(0f, 60f),
                    CriticalDamagePercent = F(0f, 150f), CriticalDefensePercent = F(-10f, 120f), ElementalAttack = F(0f, 200f), ElementalDefense = F(0f, 200f), StunResistance = F(0f, 120f),
                };
                var d = new CombatStatBlock
                {
                    AttackPower = F(0f, 600f), DefensePower = F(0f, 300f), HitRate = F(0f, 300f), EvasionRate = F(0f, 300f), CriticalRate = F(0f, 60f),
                    CriticalDamagePercent = F(0f, 150f), CriticalDefensePercent = F(-10f, 120f), ElementalAttack = F(0f, 200f), ElementalDefense = F(0f, 200f), StunResistance = F(0f, 120f),
                };
                var t = rng.Next(4) == 0 ? new CombatTuning { MinHitChance = F(0f, 20f), MaxHitChance = F(80f, 100f), BaseCriticalMultiplier = F(1f, 2.5f) } : CombatTuning.Default;
                var s = new CombatStrike
                {
                    SkillMultiplier = F(0f, 5f), DefenderHasReflection = rng.Next(3) == 0, ReflectionChance = F(0f, 15f),
                    AttackerHasShieldShatter = rng.Next(3) == 0, ShatterChance = F(0f, 25f), HitChanceModifier = F(-20f, 20f),
                };
                int c = rng.Next(10);
                var r = new CombatRolls { Hit = F(0f, 100f), Reflect = F(0f, 100f), Crit = c == 0 ? float.PositiveInfinity : c == 1 ? float.NegativeInfinity : F(0f, 100f), Shatter = F(0f, 100f) };

                DamageResult want = Managed(a, d, t, s, r);
                DamageResult got = CombatMath.Resolve(a, d, t, s, r);
                string at = "strike " + i;
                Assert.AreEqual(want.HitChance, got.HitChance, 1e-4, at);
                Assert.AreEqual(want.IsEvaded, got.IsEvaded, at);
                Assert.AreEqual(want.IsReflected, got.IsReflected, at);
                Assert.AreEqual(want.ReflectedDamage, got.ReflectedDamage, 1e-4, at);
                Assert.AreEqual(want.ShieldShattered, got.ShieldShattered, at);
                Assert.AreEqual(want.RawPhysicalDamage, got.RawPhysicalDamage, 1e-4, at);
                Assert.AreEqual(want.MitigatedPhysicalDamage, got.MitigatedPhysicalDamage, 1e-4, at);
                Assert.AreEqual(want.IsCritical, got.IsCritical, at);
                Assert.AreEqual(want.NetElementalDamage, got.NetElementalDamage, 1e-4, at);
                Assert.AreEqual(want.DamageDealt, got.DamageDealt, 1e-4, at);
            }
        }

        // CombatCalculator.CalculateAttackInternal as it was (3 Nov 2026), its GetStatValue reads swapped for the blocks' fields.
        static DamageResult Managed(CombatStatBlock attacker, CombatStatBlock defender, CombatTuning tuning, CombatStrike strike, CombatRolls rolls)
        {
            DamageResult result = new DamageResult();
            float minHit = tuning.MinHitChance, maxHit = tuning.MaxHitChance, baseCritMult = tuning.BaseCriticalMultiplier;
            float attackerHit = attacker.HitRate;
            float defenderEvade = defender.EvasionRate;
            float rawHitChance = (attackerHit / Mathf.Max(1f, attackerHit + defenderEvade)) * 100f;
            float hitChance = Mathf.Clamp(rawHitChance + strike.HitChanceModifier, minHit, maxHit);
            result.HitChance = hitChance;
            if (rolls.Hit > hitChance) { result.IsEvaded = true; return result; }
            if (strike.DefenderHasReflection && rolls.Reflect <= strike.ReflectionChance)
            {
                result.IsReflected = true;
                result.DamageDealt = 0f;
                result.ReflectedDamage = attacker.AttackPower * strike.SkillMultiplier;
                return result;
            }
            if (strike.AttackerHasShieldShatter && rolls.Shatter <= strike.ShatterChance) result.ShieldShattered = true;
            float rawAP = attacker.AttackPower * strike.SkillMultiplier;
            float targetDP = defender.DefensePower;
            float physicalDamage = Mathf.Max(1f, rawAP - targetDP);
            result.RawPhysicalDamage = rawAP;
            result.MitigatedPhysicalDamage = physicalDamage;
            float critChance = attacker.CriticalRate;
            if (rolls.Crit <= critChance)
            {
                result.IsCritical = true;
                float critBonusMult = baseCritMult + (attacker.CriticalDamagePercent / 100f);
                float critDefense = Mathf.Clamp(defender.CriticalDefensePercent, 0f, 100f);
                if (critDefense > 0f) critBonusMult = 1f + (Mathf.Max(1f, critBonusMult) - 1f) * (1f - critDefense / 100f);
                physicalDamage *= critBonusMult;
                float stunRes = defender.StunResistance;
                if (stunRes > 0f) physicalDamage *= Mathf.Max(0.7f, 1f - (stunRes / 100f * 0.5f));
                result.MitigatedPhysicalDamage = physicalDamage;
            }
            float eleAttack = attacker.ElementalAttack;
            float eleDefense = defender.ElementalDefense;
            float netElementalDamage = Mathf.Max(0f, eleAttack - eleDefense);
            result.NetElementalDamage = netElementalDamage;
            result.DamageDealt = physicalDamage + netElementalDamage;
            return result;
        }
    }
}
