using KryticalNova.Player;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Tests
{
    /// <summary>
    /// The roof-edge drop's phases (owner v4, 7 Nov 2026; plan RoofFlip/code_change.md): the flip at its own speed, freefall until 0.40 s
    /// before touchdown (skipped under 0.12 s), the fall loop, the landing 0.133 s before touchdown; a short drop speeds the rest of the flip
    /// up, at most 1.25x. Air times from the arc itself: g 14, apex 1.4 m over the edge (NGO's apexClearance).
    /// </summary>
    public sealed class RouteFallTimelineTests
    {
        private const float G = 14f, Lift = 1.4f, FlipLength = 32f / 30f;
        private const float MaxSpeedUp = 1.25f, FreefallMin = 0.12f, LoopLead = 0.40f, LandLead = 0.133f;
        private static readonly float Rise = Mathf.Sqrt(2f * Lift / G);

        private static float Fall(float drop) => Mathf.Sqrt(2f * (Lift + drop) / G);

        private static RouteFallTimeline At(float drop, bool hasFreefall = true) =>
            RouteFallTimeline.Plan(FlipLength - Rise, Fall(drop), hasFreefall, MaxSpeedUp, FreefallMin, LoopLead, LandLead);

        [Test]
        public void AirTimes_AreThePlansOnes()
        {
            Assert.AreEqual(1.09f, Rise + Fall(1.5f), 0.01f);
            Assert.AreEqual(1.14f, Rise + Fall(2f), 0.01f);
            Assert.AreEqual(1.61f, Rise + Fall(8f), 0.01f);
            Assert.AreEqual(2.02f, Rise + Fall(16f), 0.01f);
        }

        [Test]
        public void TwoMetres_TheFlipSpeedsUpALittle_AndGoesStraightIntoTheLanding()
        {
            RouteFallTimeline t = At(2f);
            Assert.Greater(t.FlipSpeed, 1f);
            Assert.LessOrEqual(t.FlipSpeed, MaxSpeedUp);
            Assert.AreEqual(Fall(2f) - LandLead, t.FlipEnds, 1e-3f, "the flip ends as the landing starts");
            Assert.AreEqual(t.FlipEnds, t.LandAt, 1e-3f);
            Assert.AreEqual(-1f, t.FreefallAt, "no freefall");
            Assert.AreEqual(-1f, t.LoopAt, "no fall loop");
        }

        [Test]
        public void OneAndAHalfMetres_TheShortestDrop_StaysWithinTheSpeedUp()
        {
            RouteFallTimeline t = At(1.5f);
            Assert.LessOrEqual(t.FlipSpeed, MaxSpeedUp);
            Assert.AreEqual(Fall(1.5f) - LandLead, t.LandAt, 1e-3f, "the landing still meets touchdown");
            Assert.AreEqual(-1f, t.FreefallAt);
            Assert.AreEqual(-1f, t.LoopAt);
        }

        [Test]
        public void EightMetres_FlipAtItsOwnSpeed_Freefall014_FallLoop027_ThenTheLanding()
        {
            RouteFallTimeline t = At(8f);
            Assert.AreEqual(1f, t.FlipSpeed);
            Assert.AreEqual(t.FlipEnds, t.FreefallAt, 1e-4f, "freefall right after the flip");
            Assert.AreEqual(0.14f, t.LoopAt - t.FreefallAt, 0.01f, "freefall 0.14 s");
            Assert.AreEqual(0.27f, t.LandAt - t.LoopAt, 0.01f, "fall loop 0.27 s");
            Assert.AreEqual(Fall(8f) - LandLead, t.LandAt, 1e-4f);
        }

        [Test]
        public void SixteenMetres_Freefall056()
        {
            RouteFallTimeline t = At(16f);
            Assert.AreEqual(1f, t.FlipSpeed);
            Assert.AreEqual(0.56f, t.LoopAt - t.FreefallAt, 0.01f);
            Assert.AreEqual(0.27f, t.LandAt - t.LoopAt, 0.01f);
        }

        [Test]
        public void AFreefallWindowUnderTheMinimum_IsSkipped_AndTheFallLoopFollowsTheFlip()
        {
            // a drop whose window after the flip is between 0 and 0.12 s
            float drop = 6f;
            RouteFallTimeline t = At(drop);
            float window = (Fall(drop) - LoopLead) - t.FlipEnds;
            Assert.That(window, Is.GreaterThan(0f).And.LessThan(FreefallMin), "the case under test");
            Assert.AreEqual(-1f, t.FreefallAt);
            Assert.AreEqual(t.FlipEnds, t.LoopAt, 1e-4f);
            Assert.AreEqual(Fall(drop) - LandLead, t.LandAt, 1e-4f);
        }

        [Test]
        public void ABodyWithoutAFreefallClip_GetsTheFallLoopAfterTheFlip()
        {
            RouteFallTimeline t = At(16f, hasFreefall: false);
            Assert.AreEqual(-1f, t.FreefallAt);
            Assert.AreEqual(t.FlipEnds, t.LoopAt, 1e-4f);
        }

        [Test]
        public void SpeedRule_IsOneUntilTheFlipWouldOverrunTheLanding_ThenCapped()
        {
            Assert.AreEqual(1f, RouteFallTimeline.Plan(0.5f, 2f, true, MaxSpeedUp, FreefallMin, LoopLead, LandLead).FlipSpeed);
            Assert.AreEqual(1.1f, RouteFallTimeline.Plan(0.55f, 0.5f + LandLead, true, MaxSpeedUp, FreefallMin, LoopLead, LandLead).FlipSpeed, 1e-4f);
            RouteFallTimeline capped = RouteFallTimeline.Plan(1f, 0.3f + LandLead, true, MaxSpeedUp, FreefallMin, LoopLead, LandLead);
            Assert.AreEqual(MaxSpeedUp, capped.FlipSpeed, 1e-4f);
            Assert.AreEqual(capped.FlipEnds, capped.LandAt, 1e-4f, "capped: the landing waits for the flip, never cuts it");
            Assert.AreEqual(1f, RouteFallTimeline.Plan(0f, 1f, true, MaxSpeedUp, FreefallMin, LoopLead, LandLead).FlipSpeed, "no flip playing");
        }
    }
}
