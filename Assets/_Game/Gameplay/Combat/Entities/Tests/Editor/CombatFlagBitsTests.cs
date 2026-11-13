using NUnit.Framework;

namespace KryticalNova.Combat.Entities.Tests
{
    /// <summary>Build 17, item 3.1: the ghost's CombatFlag carries the fight (bit 1) and whether it has a real target (bit 2).</summary>
    public sealed class CombatFlagBitsTests
    {
        [Test]
        public void TheFlagCarriesBothBitsAndNoRealFightWithoutAFight()
        {
            Assert.AreEqual(0, CombatFlagBits.Of(false, false));
            Assert.AreEqual(0, CombatFlagBits.Of(false, true), "no real fight outside a fight");
            Assert.AreEqual(1, CombatFlagBits.Of(true, false), "a fight on nothing reads as before");
            Assert.AreEqual(3, CombatFlagBits.Of(true, true));
            Assert.IsTrue(CombatFlagBits.Fighting(1));
            Assert.IsFalse(CombatFlagBits.RealFight(1));
            Assert.IsTrue(CombatFlagBits.Fighting(3) && CombatFlagBits.RealFight(3));
            Assert.IsFalse(CombatFlagBits.Fighting(0) || CombatFlagBits.RealFight(0));
        }
    }
}
