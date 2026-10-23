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
        public void TheCombo_OneTwoThreeOnTheBeat_ClimbsToTheTop()
        {
            int step = CadenceCombo.Next(0, false, SkillKind.SingleHit, true);
            Assert.AreEqual(1, step);
            step = CadenceCombo.Next(step, true, SkillKind.TwoHit, true);
            Assert.AreEqual(2, step);
            step = CadenceCombo.Next(step, true, SkillKind.ThreeHit, true);
            Assert.AreEqual(3, step);
            Assert.AreEqual(1, CadenceCombo.Next(step, true, SkillKind.SingleHit, true), "after the top a new chain starts");
        }

        [Test]
        public void TheCombo_OffTheBeat_OutOfOrder_OrTooLate_IsBroken()
        {
            Assert.AreEqual(0, CadenceCombo.Next(1, true, SkillKind.TwoHit, false), "off the beat");
            Assert.AreEqual(0, CadenceCombo.Next(1, true, SkillKind.ThreeHit, true), "skipped a step");
            Assert.AreEqual(0, CadenceCombo.Next(1, true, SkillKind.Area, true), "an area skill has no place in it");
            Assert.AreEqual(0, CadenceCombo.Next(1, false, SkillKind.TwoHit, true), "the window ran out");
            Assert.AreEqual(1, CadenceCombo.Next(2, true, SkillKind.SingleHit, true), "a one-hit skill starts again");
            Assert.AreEqual(0, CadenceCombo.Next(0, false, SkillKind.ThreeHit, true), "a chain starts with a one-hit skill");
            Assert.IsTrue(CadenceCombo.Alive(1.9f, 2f));
            Assert.IsFalse(CadenceCombo.Alive(2.1f, 2f));
        }

        [Test]
        public void Redline_WaitsThroughStepsOneAndTwo_NotThree()
        {
            Assert.IsFalse(RedlineRules.SavedForTheChain(0), "no chain: spent as before");
            Assert.IsTrue(RedlineRules.SavedForTheChain(1));
            Assert.IsTrue(RedlineRules.SavedForTheChain(2));
            Assert.IsFalse(RedlineRules.SavedForTheChain(3), "step 3 holds it for every hit and spends it");
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
