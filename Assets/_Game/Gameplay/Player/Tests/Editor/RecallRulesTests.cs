using KryticalNova.Player;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Tests
{
    /// <summary>When a Recall Tag may be used, and what counts as moving during its channel.</summary>
    public sealed class RecallRulesTests
    {
        [Test]
        public void ItCanBeUsedWhenNothingStandsInTheWay()
        {
            Assert.IsNull(RecallRules.BlockedReason(false, false, false, false, true));
        }

        [Test]
        public void ItIsRefusedInAFightACalloutAHallOrTwice()
        {
            Assert.AreEqual(RecallRules.ReasonInFight, RecallRules.BlockedReason(false, true, false, false, true));
            Assert.AreEqual(RecallRules.ReasonCallout, RecallRules.BlockedReason(false, true, true, false, true));
            Assert.AreEqual(RecallRules.ReasonInHall, RecallRules.BlockedReason(false, false, false, true, true));
            Assert.AreEqual(RecallRules.ReasonAlready, RecallRules.BlockedReason(true, false, false, false, true));
        }

        [Test]
        public void ItIsRefusedOnAMapWithNoTown()
        {
            Assert.AreEqual(RecallRules.ReasonNoTown, RecallRules.BlockedReason(false, false, false, false, false));
        }

        [Test]
        public void ADriftWithinTheToleranceIsNotAMove()
        {
            Vector3 start = new Vector3(10f, 0f, 10f);
            Assert.IsFalse(RecallRules.Moved(start, start + new Vector3(0.3f, 0f, 0.3f), 0.5f));
            Assert.IsTrue(RecallRules.Moved(start, start + new Vector3(0.6f, 0f, 0f), 0.5f));
        }

        [Test]
        public void GoingUpOrDownIsNotAMove()
        {
            Vector3 start = Vector3.zero;
            Assert.IsFalse(RecallRules.Moved(start, new Vector3(0f, 3f, 0f), 0.5f));
        }

        [Test]
        public void SecondsLeftNeverGoBelowZero()
        {
            Assert.AreEqual(2f, RecallRules.SecondsLeft(3f, 5f), 1e-4f);
            Assert.AreEqual(0f, RecallRules.SecondsLeft(9f, 5f));
        }
    }
}
