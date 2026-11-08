using KryticalNova.Player;
using NUnit.Framework;

namespace KryticalNova.Tests
{
    /// <summary>
    /// Owner, 5 Nov 2026: a player who dies off solid ground de-rezzes, never drops; on the ground (the crouch before a
    /// jump's take-off included) the body falls as before.
    /// </summary>
    public sealed class DeathGroundRulesTests
    {
        private static bool DeRezzes(DeathGroundQuery q) => DeathGroundRules.DeRezzes(q);

        [Test]
        public void OnFlatGround_ItFalls() => Assert.IsFalse(DeRezzes(new DeathGroundQuery()));

        [Test]
        public void TheCrouchBeforeTakeOff_IsAFall() =>
            Assert.IsFalse(DeRezzes(new DeathGroundQuery { JumpPressed = true, JumpAirborne = false }));

        [Test]
        public void JustAfterTakeOff_ItDeRezzes() =>
            Assert.IsTrue(DeRezzes(new DeathGroundQuery { JumpPressed = true, JumpAirborne = true, FeetAboveGround = 0.02f }));

        [Test]
        public void MidJump_ItDeRezzes() =>
            Assert.IsTrue(DeRezzes(new DeathGroundQuery { JumpPressed = true, JumpAirborne = true, FeetAboveGround = 1.8f }));

        [Test]
        public void OnAWall_ItDeRezzes() => Assert.IsTrue(DeRezzes(new DeathGroundQuery { OnWall = true }));

        [Test]
        public void OnTheWater_ItDeRezzes() => Assert.IsTrue(DeRezzes(new DeathGroundQuery { OnWater = true }));

        [Test]
        public void InAVault_ItDeRezzes() => Assert.IsTrue(DeRezzes(new DeathGroundQuery { InVault = true }));

        [Test]
        public void JustOffALedge_ItDeRezzes() =>
            Assert.IsTrue(DeRezzes(new DeathGroundQuery { FeetAboveGround = DeathGroundRules.AirborneHeight }));

        [Test]
        public void ALittleUnderTheGap_ItFalls() =>
            Assert.IsFalse(DeRezzes(new DeathGroundQuery { FeetAboveGround = DeathGroundRules.AirborneHeight - 0.01f }));

        [Test]
        public void TheGap_IsMeasuredToTheNearestSurfaceUnderTheFeet()
        {
            // A walkway 4 m over the terrain: standing on it is on the ground.
            Assert.AreEqual(0f, DeathGroundRules.FeetAboveGround(4f, 0f, 4f), 1e-4f);
            // Over the terrain with no walkway near: the whole height.
            Assert.AreEqual(1.2f, DeathGroundRules.FeetAboveGround(1.2f, 0f, float.NaN), 1e-4f);
            // A surface above the feet (a roof over the head) is not the ground under them.
            Assert.AreEqual(0.5f, DeathGroundRules.FeetAboveGround(0.5f, 0f, 3f), 1e-4f);
            // Nothing found below: not off the ground by this rule (the roof rule still applies).
            Assert.AreEqual(0f, DeathGroundRules.FeetAboveGround(5f, float.NaN, float.NaN), 1e-4f);
        }
    }
}
