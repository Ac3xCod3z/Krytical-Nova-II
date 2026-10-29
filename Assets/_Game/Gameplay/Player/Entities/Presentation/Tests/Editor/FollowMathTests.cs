using NUnit.Framework;
using Unity.Mathematics;
using Unity.Entities;
using UnityEngine;
using KryticalNova.Core;

namespace KryticalNova.Player.Entities.Presentation.Tests
{
    /// <summary>The follow arithmetic and the registry, with no netcode world (fast, exact).</summary>
    public class FollowMathTests
    {
        const float Tick = 1f / 30f;
        const float Dt = 1f / 60f;
        const float Run = 9f;
        static readonly FollowSettings Cfg = FollowSettings.Default;

        static bool Step(ref FollowState s, float3 pos, float yaw = 0f, byte tele = 0, bool predicted = true, bool moving = true, float dt = Dt) =>
            FollowMath.Step(ref s, pos, yaw, tele, predicted, moving, Run, dt, Tick, in Cfg);

        [Test]
        public void FirstSample_Snaps()
        {
            var s = default(FollowState);
            Assert.IsTrue(Step(ref s, new float3(3, 0, 4)));
            Assert.AreEqual(new float3(3, 0, 4), s.Position);
        }

        [Test]
        public void AWalk_FullTicksOnly_IsEasedBetweenSamples_AndNeverPops()
        {
            // the ghost moves 5 m/s once per tick, frames come twice per tick
            var s = default(FollowState);
            float x = 0f;
            Step(ref s, new float3(x, 0, 0));
            float last = 0f, maxStep = 0f;
            for (int frame = 1; frame <= 120; frame++)
            {
                if (frame % 2 == 1) x += 5f * Tick;                  // a full tick happened this frame
                Step(ref s, new float3(x, 0, 0));
                float shown = s.Position.x;
                maxStep = math.max(maxStep, math.abs(shown - last));
                last = shown;
                Assert.LessOrEqual(shown, x + 1e-4f, "never ahead of the ghost");
                Assert.GreaterOrEqual(shown, x - 5f * Tick * 1.01f, "at most one tick behind");
            }
            Assert.LessOrEqual(maxStep, 5f * Dt * 1.05f, "an even step per frame, no hitch");
            Assert.AreEqual(0f, s.Offset.x, 1e-6f, "no correction was ever needed");
        }

        [Test]
        public void ACorrection_OfTwoMetres_Slides_AndIsGoneInTwoSeconds()
        {
            var s = default(FollowState);
            var home = new float3(10, 0, 0);
            Step(ref s, home, moving: false);
            var ghost = home + new float3(2, 0, 0);                  // an idle ghost is rolled back 2 m
            float last = s.Position.x, maxStep = 0f;
            for (int frame = 0; frame < 120; frame++)
            {
                Step(ref s, ghost, moving: false);
                maxStep = math.max(maxStep, math.abs(s.Position.x - last));
                last = s.Position.x;
            }
            // the ghost popped 2 m; the body's worst frame is a fraction of that (a quarter of the offset at 60 fps)
            Assert.Less(maxStep, 0.4f, "the body slid instead of popping");
            Assert.AreEqual(ghost.x, s.Position.x, 0.01f, "and arrived");
        }

        [Test]
        public void TheFirstFrameOfACorrection_DoesNotMoveTheBody()
        {
            var s = default(FollowState);
            Step(ref s, new float3(0, 0, 0), moving: false);
            Step(ref s, new float3(2, 0, 0), moving: false);
            Assert.AreEqual(0f, s.Position.x, 0.06f, "the offset starts to pay back from the next frame");
        }

        [Test]
        public void TeleportSeq_Change_SnapsInOneCall_EvenWhenSmall()
        {
            var s = default(FollowState);
            Step(ref s, new float3(0, 0, 0), tele: 4);
            Step(ref s, new float3(0.5f, 0, 0), tele: 4);
            Assert.IsTrue(Step(ref s, new float3(0.6f, 0, 0), yaw: 90f, tele: 5));
            Assert.AreEqual(0.6f, s.Position.x, 1e-6f);
            Assert.AreEqual(90f, s.Yaw, 1e-4f);
            Assert.AreEqual(0f, s.Offset.x);
        }

        [Test]
        public void ABigJump_Snaps()
        {
            var s = default(FollowState);
            Step(ref s, new float3(0, 0, 0));
            Assert.IsTrue(Step(ref s, new float3(Cfg.SnapDistanceM + 1f, 0, 0)));
            Assert.AreEqual(Cfg.SnapDistanceM + 1f, s.Position.x, 1e-5f);
        }

        [Test]
        public void TheOffset_HalvesEveryHalfLife()
        {
            var s = default(FollowState);
            Step(ref s, float3.zero, moving: false);
            Step(ref s, new float3(2, 0, 0), moving: false);          // offset about -2
            float start = math.abs(s.Offset.x);
            int frames = (int)math.round(Cfg.HalfLifeSeconds / Dt);
            for (int i = 0; i < frames; i++) Step(ref s, new float3(2, 0, 0), moving: false);
            Assert.AreEqual(start * 0.5f, math.abs(s.Offset.x), start * 0.08f);
        }

        [Test]
        public void Interpolated_FollowsEveryFrame_WithoutTickEasing()
        {
            var s = default(FollowState);
            Step(ref s, float3.zero, predicted: false);
            Step(ref s, new float3(0.08f, 0, 0), predicted: false);
            Assert.AreEqual(0.08f, s.Position.x, 1e-6f, "an interpolated ghost is already smooth: shown as is");
        }

        [Test]
        public void Yaw_TurnsTheShortWay_AcrossZero()
        {
            var s = default(FollowState);
            Step(ref s, float3.zero, yaw: 350f, predicted: true);
            Step(ref s, float3.zero, yaw: 10f, predicted: true);     // T = 0.5 after one frame of two per tick
            float yaw = FollowMath.DeltaAngle(0f, s.Yaw);
            Assert.AreEqual(0f, yaw, 6f, "half way is at 0, not at 180");
            Assert.AreEqual(170f, FollowMath.DeltaAngle(10f, 180f), 0.001f);
            Assert.AreEqual(20f, FollowMath.DeltaAngle(350f, 10f), 0.001f);
        }

        [Test]
        public void YawDegrees_OfAYRotation()
        {
            Assert.AreEqual(90f, FollowMath.YawDegrees(quaternion.RotateY(math.radians(90f))), 0.01f);
            Assert.AreEqual(0f, FollowMath.YawDegrees(quaternion.identity), 0.001f);
        }
    }
}
