using KryticalNova.Combat;
using NUnit.Framework;

namespace KryticalNova.Combat.Tests
{
    /// <summary>Build 17 item 11.2: low is jumped, high is stepped aside (varied), two in a row earn a counter.</summary>
    public sealed class DodgeRulesTests
    {
        [Test]
        public void LowIsJumpedHighIsSidesteppedInTurn()
        {
            var s = new DodgeStreak();
            Assert.AreEqual(DodgeMove.JumpOver, DodgeRules.OnEvaded(ref s, AttackHeight.Low, false, 0f, 9, 3f, out _));
            DodgeMove a = DodgeRules.OnEvaded(ref s, AttackHeight.High, false, 0.5f, 9, 3f, out _);
            DodgeMove b = DodgeRules.OnEvaded(ref s, AttackHeight.High, false, 1f, 9, 3f, out _);
            Assert.AreNotEqual(a, b, "right and left in turn");
        }

        [Test]
        public void TheSecondDodgeInARowCounters()
        {
            var s = new DodgeStreak();
            DodgeRules.OnEvaded(ref s, AttackHeight.High, false, 0f, 2, 3f, out bool c1);
            DodgeRules.OnEvaded(ref s, AttackHeight.Low, false, 1f, 2, 3f, out bool c2);
            Assert.IsFalse(c1);
            Assert.IsTrue(c2);
            DodgeRules.OnEvaded(ref s, AttackHeight.High, false, 2f, 2, 3f, out bool c3);
            Assert.IsFalse(c3, "the streak starts again after a counter");
        }

        [Test]
        public void AttackingOrWaitingOrBeingHitBreaksTheStreak()
        {
            var s = new DodgeStreak();
            Assert.AreEqual(DodgeMove.None, DodgeRules.OnEvaded(ref s, AttackHeight.High, true, 0f, 2, 3f, out bool c0));
            Assert.IsFalse(c0);
            DodgeRules.OnEvaded(ref s, AttackHeight.High, false, 0f, 2, 3f, out _);
            DodgeRules.OnEvaded(ref s, AttackHeight.High, false, 5f, 2, 3f, out bool late);
            Assert.IsFalse(late, "too long between: a new streak");
            DodgeRules.OnHitTaken(ref s);
            DodgeRules.OnEvaded(ref s, AttackHeight.High, false, 5.5f, 2, 3f, out bool afterHit);
            Assert.IsFalse(afterHit);
        }
    }
}
