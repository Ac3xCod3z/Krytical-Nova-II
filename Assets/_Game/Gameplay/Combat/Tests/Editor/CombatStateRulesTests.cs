using NUnit.Framework;

namespace KryticalNova.Combat.Tests
{
    /// <summary>When a player counts as in a fight, and why: the rule every fight-only mechanic (Cadence) hangs on.</summary>
    public sealed class CombatStateRulesTests
    {
        private const float Linger = 6f;
        private const float Never = float.NegativeInfinity;

        private static CombatReasons Why(float now, float lastAct = Never, float lastThreat = Never,
            bool attacking = false, bool duelling = false, bool held = false) =>
            PlayerCombatState.Why(now, lastAct, lastThreat, Linger, attacking, duelling, held);

        [Test]
        public void NothingHappened_NotInAFight()
        {
            Assert.AreEqual(CombatReasons.None, Why(100f));
        }

        [Test]
        public void ABlow_IsAFightUntilTheLingerHasPassed()
        {
            Assert.AreEqual(CombatReasons.Blow, Why(100f, lastAct: 100f));
            Assert.AreEqual(CombatReasons.Blow, Why(105.9f, lastAct: 100f));
            Assert.AreEqual(CombatReasons.None, Why(106f, lastAct: 100f));
        }

        [Test]
        public void AHostileAfterThePlayer_IsAFightBeforeAnyBlow()
        {
            Assert.AreEqual(CombatReasons.Hostile, Why(100f, lastThreat: 99.5f));
        }

        [Test]
        public void AHostileThatGaveUp_LeavesTheFightAfterTheLinger()
        {
            Assert.AreEqual(CombatReasons.Hostile, Why(105f, lastThreat: 100f));
            Assert.AreEqual(CombatReasons.None, Why(106.1f, lastThreat: 100f));
        }

        [Test]
        public void AttackingDuellingAndHeld_EachHoldWithNoBlowAtAll()
        {
            Assert.AreEqual(CombatReasons.Attack, Why(100f, attacking: true));
            Assert.AreEqual(CombatReasons.Callout, Why(100f, duelling: true));
            Assert.AreEqual(CombatReasons.Held, Why(100f, held: true));
        }

        [Test]
        public void SeveralReasons_HoldTogether()
        {
            CombatReasons why = Why(100f, lastAct: 99f, lastThreat: 99.8f, duelling: true);
            Assert.AreEqual(CombatReasons.Blow | CombatReasons.Hostile | CombatReasons.Callout, why);
        }

        [Test]
        public void HeldGround_OutlastsEveryTimer()
        {
            Assert.AreEqual(CombatReasons.Held, Why(10000f, lastAct: 100f, lastThreat: 100f, held: true));
        }

        // The Strike Dummy (owner, 24 Oct 2026): after a blow on it, the fight holds while it is selected or near.
        [Test]
        public void StruckDummy_HoldsWhileSelectedOrWithin30m()
        {
            Assert.IsTrue(PlayerCombatState.DummyHolds(selected: true, distance: 80f, leaveMetres: 30f));
            Assert.IsTrue(PlayerCombatState.DummyHolds(selected: false, distance: 12f, leaveMetres: 30f));
        }

        [Test]
        public void StruckDummy_EndsOnlyWhenDeselectedAnd30mAway()
        {
            Assert.IsFalse(PlayerCombatState.DummyHolds(selected: false, distance: 30f, leaveMetres: 30f));
            Assert.IsFalse(PlayerCombatState.DummyHolds(selected: false, distance: 45f, leaveMetres: 30f));
        }
    }
}
