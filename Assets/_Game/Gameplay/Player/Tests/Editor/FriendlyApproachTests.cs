using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Player.Tests
{
    /// <summary>
    /// Owner's 2-player test, 6 Nov 2026, item 1: a second plain click on a friend walks over to them in both games (NGO's WalkToUnit);
    /// the DOTS view sends the same spot to its ghost as a ground click.
    /// </summary>
    public class FriendlyApproachTests
    {
        [Test]
        public void AWalkToAFriend_EndsTheApproachDistanceShortOfThem_OnTheLineFromThem()
        {
            Assert.IsTrue(PointAndClickPlayer.TryFriendlyApproachPoint(new Vector3(10f, 0f, 0f), Vector3.zero, 2f, out Vector3 spot));
            Assert.AreEqual(new Vector3(2f, 0f, 0f), spot, "2 m short of the friend, on my side");

            Assert.IsTrue(PointAndClickPlayer.TryFriendlyApproachPoint(new Vector3(0f, 5f, 6f), new Vector3(0f, 1f, 0f), 2f, out spot));
            Assert.AreEqual(new Vector3(0f, 1f, 2f), spot, "measured on the ground plane, at the friend's height (the NavMesh takes it from there)");
        }

        [Test]
        public void AlreadyThatClose_ThereIsNowhereToGo()
        {
            Assert.IsFalse(PointAndClickPlayer.TryFriendlyApproachPoint(new Vector3(1.5f, 0f, 0f), Vector3.zero, 2f, out _));
        }
    }
}
