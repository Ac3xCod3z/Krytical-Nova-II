using KryticalNova.Progression;
using NUnit.Framework;

namespace KryticalNova.Combat.Tests
{
    /// <summary>Build 17, item 7.3 (owner, 13 Nov 2026): the combo crit rule, every 1/2/3-hit x on/off beat x Redline case.</summary>
    public sealed class CritRulesTests
    {
        //        kind                 onBeat redline  expected
        [TestCase(SkillKind.SingleHit, false, false, CritMode.Roll)]   // a 1-hit off the beat rolls as usual
        [TestCase(SkillKind.SingleHit, false, true,  CritMode.Roll)]
        [TestCase(SkillKind.SingleHit, true,  false, CritMode.Never)]  // on the beat the 1-hit can't crit
        [TestCase(SkillKind.SingleHit, true,  true,  CritMode.Never)]
        [TestCase(SkillKind.TwoHit,    false, false, CritMode.Never)]  // the 2-hit can't crit at all
        [TestCase(SkillKind.TwoHit,    false, true,  CritMode.Never)]
        [TestCase(SkillKind.TwoHit,    true,  false, CritMode.Never)]
        [TestCase(SkillKind.TwoHit,    true,  true,  CritMode.Never)]
        [TestCase(SkillKind.ThreeHit,  false, false, CritMode.Never)]  // the 3-hit can't crit ...
        [TestCase(SkillKind.ThreeHit,  false, true,  CritMode.Never)]
        [TestCase(SkillKind.ThreeHit,  true,  false, CritMode.Never)]
        [TestCase(SkillKind.ThreeHit,  true,  true,  CritMode.Always)] // ... except a Redline combo on the beat: always
        [TestCase(SkillKind.Area,      false, false, CritMode.Roll)]   // area attacks keep the roll
        [TestCase(SkillKind.Area,      true,  false, CritMode.Roll)]
        public void TheRuleTable(SkillKind kind, bool onBeat, bool redline, CritMode expected)
        {
            Assert.AreEqual(expected, CritRules.ModeFor(kind, onBeat, redline));
        }

        [Test]
        public void AllThreeHitsOfTheOnBeatRedlineThreeHitAreCrits()
        {
            for (int hit = 0; hit < 3; hit++)
                Assert.AreEqual(CritMode.Always, CritRules.ModeFor(SkillKind.ThreeHit, hit, 3, onBeat: true, redlineCombo: true), $"hit {hit + 1}");
        }

        [Test]
        public void EveryHitOfATwoHitOrAPlainThreeHitIsNever()
        {
            for (int hit = 0; hit < 2; hit++)
                Assert.AreEqual(CritMode.Never, CritRules.ModeFor(SkillKind.TwoHit, hit, 2, onBeat: false, redlineCombo: false));
            for (int hit = 0; hit < 3; hit++)
                Assert.AreEqual(CritMode.Never, CritRules.ModeFor(SkillKind.ThreeHit, hit, 3, onBeat: true, redlineCombo: false));
        }

        [Test]
        public void TheRedlineComboIsTheOneRedlineHoldsForEveryHit()
        {
            bool holds = RedlineRules.HoldsForEveryHit(SkillKind.ThreeHit, charged: true, onCadence: true);
            Assert.IsTrue(holds);
            Assert.AreEqual(CritMode.Always, CritRules.ModeFor(SkillKind.ThreeHit, true, holds));
            Assert.IsFalse(RedlineRules.HoldsForEveryHit(SkillKind.ThreeHit, charged: true, onCadence: false));
        }
    }
}
