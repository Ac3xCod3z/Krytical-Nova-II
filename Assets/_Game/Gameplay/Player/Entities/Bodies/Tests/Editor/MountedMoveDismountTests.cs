using KryticalNova.Player.Entities.Bodies;
using NUnit.Framework;

namespace KryticalNova.Player.Entities.Bodies.Tests
{
    /// <summary>
    /// Wave 3, 12 Nov 2026: NGO took a rider off at once when its motor played Jumping (a jump, a vault, a wall run and its roof routes);
    /// the water motor never set it. A DOTS server body has no motor: ServerBodyMover.JumpingMove is that rule from the ghost.
    /// </summary>
    public class MountedMoveDismountTests
    {
        [Test]
        public void AJumpAVaultOrAWall_TakesTheRiderOff()
        {
            Assert.IsTrue(ServerBodyMover.JumpingMove(airborne: true, vault: false, wall: 0), "a jump's flight (the roof flip's route jump too)");
            Assert.IsTrue(ServerBodyMover.JumpingMove(false, true, 0), "a vault");
            Assert.IsTrue(ServerBodyMover.JumpingMove(false, false, 1), "on a wall (Wallstride)");
            Assert.IsTrue(ServerBodyMover.JumpingMove(false, false, 2), "perched in a tree");
        }

        [Test]
        public void WalkingOrWater_KeepsTheRiderOn()
        {
            Assert.IsFalse(ServerBodyMover.JumpingMove(false, false, 0), "a walk or standing");
        }
    }
}
