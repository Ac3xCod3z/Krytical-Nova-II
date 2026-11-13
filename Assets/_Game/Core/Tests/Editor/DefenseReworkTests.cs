using KryticalNova.Core;
using NUnit.Framework;

namespace KryticalNova.Tests
{
    /// <summary>
    /// Build 17, item 8 (owner pick 13 Nov 2026: the proposed model + a tankier Block 40 % / -70 % + Evasion cuts crit damage):
    /// Defense is a curve that never makes anyone immune, Block and crit resistance come from Defense and Evasion, all from the tuning.
    /// </summary>
    public sealed class DefenseReworkTests
    {
        static CombatTuning Item8 => new CombatTuning
        {
            MinHitChance = 10f, MaxHitChance = 95f, BaseCriticalMultiplier = 1.5f,
            DefenseK = 200f, DefenseKPerLevel = 12f, BlockCap = 40f, BlockCut = 70f,
            ResistFromDefense = 35f, ResistFromEvasion = 25f, ResistCap = 60f, EvasionCritDamageCut = 30f, EvasionBacklashBonus = 5f,
        };

        // Level 50 attacker: K = 200 + 12 x 50 = 800.
        static CombatStatBlock Attacker => new CombatStatBlock { AttackPower = 1000f, HitRate = 100f, CriticalRate = 50f, Level = 50f };
        static CombatStatBlock Defender(float dp, float eva = 0f) => new CombatStatBlock { DefensePower = dp, EvasionRate = eva };
        static CombatStrike Plain(float skill = 1f) => new CombatStrike { SkillMultiplier = skill, ReflectionChance = 0f, ShatterChance = 0f };
        // hits, no crit, no block
        static CombatRolls Clean => new CombatRolls { Hit = 0f, Reflect = 100f, Crit = 100f, Shatter = 100f, Block = 100f };

        [Test]
        public void DefenseEqualToK_HalvesAHit_AndNeverMakesImmune()
        {
            Assert.AreEqual(500f, CombatMath.Resolve(Attacker, Defender(800f), Item8, Plain(), Clean).DamageDealt, 0.01f);
            Assert.AreEqual(1000f / 3f, CombatMath.Resolve(Attacker, Defender(1600f), Item8, Plain(), Clean).DamageDealt, 0.01f);
            Assert.GreaterOrEqual(CombatMath.Resolve(Attacker, Defender(1e6f), Item8, Plain(), Clean).DamageDealt, 1f, "no immunity: at least the 1-damage floor");
            // a skill keeps its multiplier through Defense (the old subtraction let Defense matter only against plain hits)
            Assert.AreEqual(1500f, CombatMath.Resolve(Attacker, Defender(800f), Item8, Plain(3f), Clean).DamageDealt, 0.01f);
        }

        [Test]
        public void Block_IsCappedAt40_AndCuts70Percent()
        {
            CombatRolls blockRoll = Clean;
            blockRoll.Block = 19f; // DP = K -> chance 40 x 0.5 = 20 %
            DamageResult r = CombatMath.Resolve(Attacker, Defender(800f), Item8, Plain(), blockRoll);
            Assert.IsTrue(r.IsBlocked);
            Assert.AreEqual(150f, r.DamageDealt, 0.01f, "500 less 70 %");
            blockRoll.Block = 21f;
            Assert.IsFalse(CombatMath.Resolve(Attacker, Defender(800f), Item8, Plain(), blockRoll).IsBlocked);
            blockRoll.Block = 39.9f;
            Assert.IsTrue(CombatMath.Resolve(Attacker, Defender(1e7f), Item8, Plain(), blockRoll).IsBlocked, "near the cap");
            blockRoll.Block = 40f;
            Assert.IsFalse(CombatMath.Resolve(Attacker, Defender(1e7f), Item8, Plain(), blockRoll).IsBlocked, "never above 40 %");
        }

        [Test]
        public void CritResistance_CutsTheRate_UpTo60Percent()
        {
            CombatRolls crit = Clean;
            crit.Crit = 45f; // a 50 % crit rate
            Assert.IsTrue(CombatMath.Resolve(Attacker, Defender(0f), Item8, Plain(), crit).IsCritical, "no defence: the full rate");
            // DP = K (share 0.5) and Evasion = Hit (share 1): 17.5 + 25 = 42.5 % off -> 28.75 %
            Assert.IsFalse(CombatMath.Resolve(Attacker, Defender(800f, 100f), Item8, Plain(), crit).IsCritical);
            crit.Crit = 28f;
            Assert.IsTrue(CombatMath.Resolve(Attacker, Defender(800f, 100f), Item8, Plain(), crit).IsCritical);
            // a big cape cannot take it past 60 %: 50 x 0.4 = 20 %
            var caped = Defender(1e7f, 1e7f);
            caped.CriticalDefensePercent = 100f;
            crit.Crit = 19.9f;
            Assert.IsTrue(CombatMath.Resolve(Attacker, caped, Item8, Plain(), crit).IsCritical, "the cap leaves 40 % of the rate");
        }

        [Test]
        public void Evasion_TakesPartOfTheCritBonusAway()
        {
            CombatRolls crit = Clean;
            crit.Crit = float.NegativeInfinity;
            float slow = CombatMath.Resolve(Attacker, Defender(0f, 0f), Item8, Plain(), crit).DamageDealt;
            Assert.AreEqual(1500f, slow, 0.01f, "x1.5 with no Evasion");
            float agile = CombatMath.Resolve(Attacker, Defender(0f, 100f), Item8, Plain(), crit).DamageDealt;
            Assert.AreEqual(1350f, agile, 0.01f, "the bonus half loses 30 %: x1.35");
        }

        [Test]
        public void WithNoItem8Numbers_TheFormulaIsTheOldOne()
        {
            DamageResult r = CombatMath.Resolve(Attacker, Defender(300f), CombatTuning.Default, Plain(), Clean);
            Assert.AreEqual(700f, r.DamageDealt, 0.01f, "subtractive Defense");
            CombatRolls zeroBlock = Clean;
            zeroBlock.Block = 0f;
            Assert.IsFalse(CombatMath.Resolve(Attacker, Defender(300f), CombatTuning.Default, Plain(), zeroBlock).IsBlocked);
        }
    }
}
