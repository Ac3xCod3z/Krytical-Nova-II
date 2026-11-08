using KryticalNova.Player;
using NUnit.Framework;

namespace KryticalNova.Player.Tests
{
    /// <summary>
    /// Owner, 7 Nov 2026 (retest): in the saddle with the weapon out, the upper body holds the weapon as on foot over
    /// the riding legs; never during a getting on or off, a strike or a draw, and never with the weapon sheathed.
    /// </summary>
    public sealed class SeatedHoldRulesTests
    {
        private const CharacterAnimState Free = CharacterAnimState.Locomotion;

        [Test]
        public void InTheSaddleWithTheWeaponOut_TheUpperBodyHoldsIt() =>
            Assert.IsTrue(PlayerAnimancerMotor.SeatedHoldWanted(true, true, true, true, Free));

        [Test]
        public void SheathedOrOnFoot_NoHold()
        {
            Assert.IsFalse(PlayerAnimancerMotor.SeatedHoldWanted(true, true, false, true, Free), "sheathed");
            Assert.IsFalse(PlayerAnimancerMotor.SeatedHoldWanted(false, false, true, true, Free), "on foot: the base layer holds it");
            Assert.IsFalse(PlayerAnimancerMotor.SeatedHoldWanted(true, true, true, false, Free), "no drawn idle to hold with");
        }

        [Test]
        public void GettingOnOrOff_TheClipKeepsTheArms() =>
            Assert.IsFalse(PlayerAnimancerMotor.SeatedHoldWanted(true, false, true, true, Free));

        [Test]
        public void AStrikeOrADrawTakesTheArmsWhileItPlays()
        {
            Assert.IsFalse(PlayerAnimancerMotor.SeatedHoldWanted(true, true, true, true, CharacterAnimState.Attacking));
            Assert.IsFalse(PlayerAnimancerMotor.SeatedHoldWanted(true, true, true, true, CharacterAnimState.Equipping));
            Assert.IsFalse(PlayerAnimancerMotor.SeatedHoldWanted(true, true, true, true, CharacterAnimState.Unequipping));
        }
    }
}
