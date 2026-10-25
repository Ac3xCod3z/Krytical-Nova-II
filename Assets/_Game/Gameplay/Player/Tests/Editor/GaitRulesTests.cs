using KryticalNova.Player;
using NUnit.Framework;

namespace KryticalNova.Player.Tests
{
    /// <summary>Walk or run clip (owner, 24 Oct 2026: a walk on the dedicated server played as a slow run).</summary>
    public sealed class GaitRulesTests
    {
        private const float Threshold = 7f, Hysteresis = 2f, Delay = 0.15f;

        private static bool Run(bool wasRunning, float speed, bool runSelected, float slower = 1f) =>
            PlayerAnimancerMotor.UsesRunClip(wasRunning, speed, runSelected, Threshold, Hysteresis, slower, Delay);

        [Test]
        public void Walking_StaysOnTheWalkClip()
        {
            Assert.IsFalse(Run(false, 5f, false));
            Assert.IsFalse(Run(false, 6.9f, false));
        }

        [Test]
        public void AtTheThreshold_TheRunClipComes()
        {
            Assert.IsTrue(Run(false, 7f, true));
            Assert.IsTrue(Run(false, 7.5f, false)); // a GM speed or fast boots: a real run
        }

        [Test]
        public void RunNotSelected_AWalkAfterASpikeGoesBackToTheWalkClip()
        {
            // The case seen on the dedicated server: walking at 5.0 to 5.4 m/s with the run clip once put in by a spike.
            Assert.IsFalse(Run(true, 5.38f, false));
            Assert.IsFalse(Run(true, 5f, false));
        }

        [Test]
        public void RunSelected_BrakingHoldsTheRunClipToTheHysteresis()
        {
            Assert.IsTrue(Run(true, 5.5f, true));
            Assert.IsFalse(Run(true, 4.9f, true));
        }

        [Test]
        public void AMomentOfSlowing_HoldsTheRunClip()
        {
            Assert.IsTrue(Run(true, 3f, false, slower: 0.05f));
        }
    }
}
