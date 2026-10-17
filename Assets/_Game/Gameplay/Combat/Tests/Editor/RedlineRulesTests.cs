using KryticalNova.Progression;
using NUnit.Framework;

namespace KryticalNova.Combat.Tests
{
    /// <summary>When Redline's charge is spent: on one hit, or kept for all three of a three-hit attack pressed on the beat.</summary>
    public sealed class RedlineRulesTests
    {
        [Test]
        public void OnlyAThreeHitAttackPressedOnCadenceWithACharge_HoldsItForEveryHit()
        {
            Assert.IsTrue(RedlineRules.HoldsForEveryHit(SkillKind.ThreeHit, charged: true, onCadence: true));
            Assert.IsFalse(RedlineRules.HoldsForEveryHit(SkillKind.ThreeHit, charged: true, onCadence: false), "the press decides");
            Assert.IsFalse(RedlineRules.HoldsForEveryHit(SkillKind.ThreeHit, charged: false, onCadence: true), "nothing to hold");
            Assert.IsFalse(RedlineRules.HoldsForEveryHit(SkillKind.TwoHit, charged: true, onCadence: true));
            Assert.IsFalse(RedlineRules.HoldsForEveryHit(SkillKind.SingleHit, charged: true, onCadence: true));
            Assert.IsFalse(RedlineRules.HoldsForEveryHit(SkillKind.Area, charged: true, onCadence: true));
        }

        [Test]
        public void Held_AllThreeLand_SpentWithTheThird()
        {
            Assert.AreEqual(ChargeStep.Keep, RedlineRules.AfterHit(SkillKind.ThreeHit, true, HitOutcome.Landed, 0, 3));
            Assert.AreEqual(ChargeStep.Keep, RedlineRules.AfterHit(SkillKind.ThreeHit, true, HitOutcome.Landed, 1, 3));
            Assert.AreEqual(ChargeStep.Spend, RedlineRules.AfterHit(SkillKind.ThreeHit, true, HitOutcome.Landed, 2, 3));
        }

        [Test]
        public void Held_AnEvadedOrMissingHit_SpendsItThere()
        {
            Assert.AreEqual(ChargeStep.Spend, RedlineRules.AfterHit(SkillKind.ThreeHit, true, HitOutcome.Evaded, 0, 3));
            Assert.AreEqual(ChargeStep.Spend, RedlineRules.AfterHit(SkillKind.ThreeHit, true, HitOutcome.Evaded, 1, 3));
            Assert.AreEqual(ChargeStep.Spend, RedlineRules.AfterHit(SkillKind.ThreeHit, true, HitOutcome.None, 1, 3), "the target gone");
        }

        [Test]
        public void OffTheBeat_ThreeHit_SpentOnTheFirstHitThatLandsOrIsEvaded()
        {
            Assert.AreEqual(ChargeStep.Spend, RedlineRules.AfterHit(SkillKind.ThreeHit, false, HitOutcome.Landed, 0, 3));
            Assert.AreEqual(ChargeStep.Spend, RedlineRules.AfterHit(SkillKind.ThreeHit, false, HitOutcome.Evaded, 0, 3), "lost on the miss");
            Assert.AreEqual(ChargeStep.Keep, RedlineRules.AfterHit(SkillKind.ThreeHit, false, HitOutcome.None, 0, 3), "no hit was made: kept");
        }

        [Test]
        public void OtherAttacks_AsBefore_SpentOnTheFirstHitThatLands()
        {
            foreach (SkillKind kind in new[] { SkillKind.SingleHit, SkillKind.TwoHit, SkillKind.Area })
            {
                Assert.AreEqual(ChargeStep.Spend, RedlineRules.AfterHit(kind, false, HitOutcome.Landed, 0, 2), kind.ToString());
                Assert.AreEqual(ChargeStep.Keep, RedlineRules.AfterHit(kind, false, HitOutcome.Evaded, 0, 2), kind.ToString());
                Assert.AreEqual(ChargeStep.Keep, RedlineRules.AfterHit(kind, false, HitOutcome.None, 0, 2), kind.ToString());
            }
        }
    }
}
