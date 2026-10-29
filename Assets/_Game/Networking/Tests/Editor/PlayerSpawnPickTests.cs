using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Networking.Tests
{
    /// <summary>M3a-2b step 5: the spawn point choice of the entity netcode server (pure part of PlayerSpawnService.PickPoint).</summary>
    public class PlayerSpawnPickTests
    {
        static readonly Vector3[] Points = { new Vector3(0f, 0f, 0f), new Vector3(10f, 0f, 0f), new Vector3(20f, 0f, 0f) };

        static int Pick(ref int next, params Vector3[] occupied) =>
            PlayerSpawnService.NextFreeIndex(Points.Length, i => Points[i], ref next, new List<Vector3>(occupied), 1.5f);

        [Test]
        public void TakesPointsInTurn_WhenNobodyStandsThere()
        {
            int next = 0;
            Assert.AreEqual(0, Pick(ref next));
            Assert.AreEqual(1, Pick(ref next));
            Assert.AreEqual(2, Pick(ref next));
            Assert.AreEqual(0, Pick(ref next));
        }

        [Test]
        public void SkipsAnOccupiedPoint_OnTheGroundPlane()
        {
            int next = 0;
            Assert.AreEqual(1, Pick(ref next, new Vector3(0.5f, 3f, 0.5f)), "a body 3 m higher on the same spot still takes it");
        }

        [Test]
        public void AllTaken_GivesTheFirstOneTried_NoPoints_GivesMinusOne()
        {
            int next = 1;
            Assert.AreEqual(1, Pick(ref next, Points));
            int none = 0;
            Assert.AreEqual(-1, PlayerSpawnService.NextFreeIndex(0, i => Vector3.zero, ref none, null, 1.5f));
        }
    }
}
