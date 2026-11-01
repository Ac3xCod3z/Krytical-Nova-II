using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;

namespace KryticalNova.Player.Entities.Tests
{
    /// <summary>
    /// Wallstride's wall walk as legs of the walk (M3c, `m3c-wallstride-design.md` section 3): NGO's eases, a timed leg taking its own
    /// time, the wall at the wall's speed with the body tilted onto the face, standing on the wall at the end, over the top and upright
    /// again; NGO's rules on the wall (a stop does nothing, nothing starts during a leg).
    /// </summary>
    public class WallLegTests
    {
        static readonly int Tick = PlayerMovement.TickMicros(30);
        static readonly MoveStats Stats = new MoveStats { WalkMmps = 5000, RunMmps = 9000 };
        const int WallSpeed = 9000;

        // a wall at x = 5.4 m facing -x: the ground to its foot at 5 m, the plant onto it (0.32 s), up its face to 6 m
        static readonly int3 Foot = new int3(5000, 0, 0), Planted = new int3(5400, 500, 0), High = new int3(5400, 6000, 0);
        // over the top: up past the edge, in over it, and the walk on along the roof
        static readonly int3 Lifted = new int3(5300, 6250, 0), Top = new int3(6000, 6000, 0), WalkedOn = new int3(9000, 6000, 0);

        static WallCorner OnFace(byte phase, int micros = 0, byte up = WallEase.Linear, byte along = WallEase.Linear) => new WallCorner
        {
            Micros = micros, Ease = WallCorner.Eases(up, along), Phase = phase, UpX = -1000, FwdY = 1000,
        };

        static WallCorner Upright(byte phase, int micros, byte up, byte along) => new WallCorner
        {
            Micros = micros, Ease = WallCorner.Eases(up, along), Phase = phase, UpY = 1000, FwdX = 1000,
        };

        static PlayerInput Click(ushort seq, bool run = true)
        {
            var input = new PlayerInput { ClickSeq = seq, Run = (byte)(run ? 1 : 0) };
            input.Click.Set();
            return input;
        }

        sealed class Walk : System.IDisposable
        {
            public NativeArray<int3> Corners;
            public NativeArray<WallCorner> Legs;
            public PathPlan Plan;
            public WallPlan Wall;
            public MoveState M;
            public PlayerPose Pose;
            public PoseTilt Tilt;
            public PlayerMoveSettings Cfg = PlayerMoveSettings.Default;

            public Walk(int3[] corners, WallCorner[] legs, WallPlan wall)
            {
                Corners = new NativeArray<int3>(corners, Allocator.Temp);
                Legs = new NativeArray<WallCorner>(legs, Allocator.Temp);
                Plan = new PathPlan { Seq = wall.Seq, Count = (byte)corners.Length };
                Wall = wall;
            }

            public void Step(in PlayerInput input, in ServerOrder order = default) =>
                PlayerMovement.Step(ref M, ref Pose, ref Tilt, in input, in order, in Plan, Corners, default, default, default, default,
                    default, default, in Wall, Legs, default, default, in Stats, in Cfg, Tick);

            public bool Moving => (M.Flags & MoveFlags.Moving) != 0;

            public void Dispose()
            {
                Corners.Dispose();
                Legs.Dispose();
            }
        }

        // ground (1), the plant (2, timed), the wall (3); ends standing on it
        static Walk OntoTheWall() => new Walk(
            new[] { int3.zero, Foot, Planted, High },
            new[] { default, default, OnFace(WallPhase.Plant, 320000, WallEase.In, WallEase.Out), OnFace(WallPhase.WallMove) },
            new WallPlan { Seq = 1, SpeedMmps = WallSpeed, OnWallFrom = 3, OffWallFrom = 4, Flags = WallFlags.EndsOnWall });

        [Test]
        public void TheEases_AreNgos()
        {
            Assert.AreEqual(250, WallLeg.EaseValue(WallEase.In, 500), "In: s squared");
            Assert.AreEqual(750, WallLeg.EaseValue(WallEase.Out, 500), "Out: 1 - (1 - s) squared");
            Assert.AreEqual(500, WallLeg.EaseValue(WallEase.InOut, 500), "InOut: smoothstep");
            Assert.AreEqual(156, WallLeg.EaseValue(WallEase.InOut, 250));
            Assert.AreEqual(500, WallLeg.EaseValue(WallEase.Linear, 500));
            Assert.AreEqual(1000, WallLeg.EaseValue(WallEase.Out, 1000));
            Assert.AreEqual(0, WallLeg.EaseValue(WallEase.In, 0));

            var leg = OnFace(WallPhase.Plant, 320000, WallEase.In, WallEase.Out);
            int3 half = WallLeg.TimedPoint(Foot, Planted, in leg, 500);
            Assert.AreEqual(Foot.x + 300, half.x, "the ground part on Out: three quarters at half time");
            Assert.AreEqual(125, half.y, "the height on In: a quarter at half time");
        }

        [Test]
        public void OntoAWall_ThePlantTakesItsTime_ThenTheWallAtItsSpeed_StandingOnItAtTheEnd()
        {
            using var w = OntoTheWall();
            var input = Click(1);
            int plantTicks = 0, wallTicks = 0, ticks = 0;
            bool onWallInPlant = false, steadyOnWall = true, tiltedOnWall = true;
            while (ticks++ < 300)
            {
                w.Step(in input);
                input = default;
                if (!w.Moving) break;
                WallLeg.SegmentAt(int3.zero, w.Corners, 4, w.M.ProgressMm, out int idx, out _, out _, out _, out _);
                if (idx == 2) { plantTicks++; if (w.M.Wall != 0) onWallInPlant = true; }
                if (idx == 3)
                {
                    wallTicks++;
                    if (w.M.SpeedMmps != WallSpeed) steadyOnWall = false;
                    if (w.Tilt.UpX > -990 || w.Tilt.Phase != WallPhase.WallMove || w.M.Wall != 1) tiltedOnWall = false;
                }
            }
            Assert.IsFalse(w.Moving, "the walk ended");
            // 0.32 s at 30 Hz: the leg's progress is spent across segments inside a tick, so it is on the plant 9 or 10 ticks
            Assert.That(plantTicks, Is.InRange(9, 10), "the plant takes NGO's 0.32 s");
            Assert.IsFalse(onWallInPlant, "no Grip during the plant (NGO: the wall phases start after it)");
            Assert.IsTrue(steadyOnWall, "the wall at the wall's own speed, no acceleration or slowing");
            Assert.IsTrue(tiltedOnWall, "up is the face's normal on the wall");
            Assert.That(wallTicks, Is.InRange(17, 19), "5.5 m at 9 m/s");
            Assert.AreEqual(High, w.Pose.Mm, "on the place clicked");
            Assert.AreEqual(1, w.M.Wall, "standing on the wall");
            Assert.AreEqual(WallPhase.WallHold, w.Tilt.Phase, "NGO's WallHold");
            Assert.AreEqual(-1000, w.Tilt.UpX, "still standing on the face");

            // NGO: a stop on the wall does nothing (the agent is off)
            var stop = default(PlayerInput);
            stop.Stop.Set();
            w.Step(in stop);
            Assert.AreEqual(1, w.M.Wall, "still on the wall after a stop");
            Assert.AreEqual(High, w.Pose.Mm);
        }

        [Test]
        public void DuringThePlant_AClickAndAStop_StartNothing()
        {
            using var w = OntoTheWall();
            var input = Click(1);
            int ticks = 0;
            while (ticks++ < 300)
            {
                w.Step(in input);
                input = default;
                WallLeg.SegmentAt(int3.zero, w.Corners, 4, w.M.ProgressMm, out int idx, out _, out _, out _, out _);
                if (idx == 2) break;
            }
            int progress = w.M.ProgressMm;
            var again = Click(2);
            again.Stop.Set();
            w.Step(in again);
            Assert.AreEqual(1, w.M.OrderSeq, "a click mid-plant starts nothing (NGO: ignored while the motor is busy)");
            Assert.IsTrue(w.Moving, "and a stop does not stop it");
            Assert.Greater(w.M.ProgressMm, progress, "the plant goes on");

            var order = new ServerOrder { Seq = PlayerLimits.ServerSeqBit | 1 };
            w.Step(default, in order);
            Assert.AreEqual(1, w.M.OrderSeq, "a server order waits for the leg's end");
        }

        [Test]
        public void OverTheTop_TheMantleLegs_ThenUprightAndTheWalkOn()
        {
            // from standing on the wall (the walk starts at High): the wall's last metre, up past the edge, in over it, the walk on
            var nearTop = new int3(5400, 5000, 0);
            using var w = new Walk(
                new[] { int3.zero, High, Lifted, Top, WalkedOn },
                new[]
                {
                    OnFace(WallPhase.WallMove), OnFace(WallPhase.WallMove),
                    Upright(WallPhase.Mantle, 120000, WallEase.Out, WallEase.In), Upright(WallPhase.Mantle, 400000, WallEase.In, WallEase.Out),
                    default,
                },
                new WallPlan { Seq = 1, SpeedMmps = WallSpeed, OnWallFrom = 1, OffWallFrom = 2 });
            w.Pose = new PlayerPose { XMm = nearTop.x, YMm = nearTop.y, ZMm = nearTop.z };
            w.M.Wall = 1;
            var input = Click(1);
            int mantleTicks = 0, ticks = 0;
            bool gripInMantle = false, uprightOnRoof = true;
            while (ticks++ < 300)
            {
                w.Step(in input);
                input = default;
                if (!w.Moving) break;
                WallLeg.SegmentAt(nearTop, w.Corners, 5, w.M.ProgressMm, out int idx, out _, out _, out _, out _);
                if (idx == 2 || idx == 3) { mantleTicks++; if (w.M.Wall != 0) gripInMantle = true; }
                if (idx == 4 && (w.Tilt.Tilted || w.M.Wall != 0)) uprightOnRoof = false;
            }
            Assert.That(mantleTicks, Is.InRange(15, 17), "0.12 s + NGO's 0.4 s mantle");
            Assert.IsFalse(gripInMantle, "off the wall from the top-out on");
            Assert.IsTrue(uprightOnRoof, "upright on the roof's walk");
            Assert.AreEqual(WalkedOn, w.Pose.Mm, "on the place clicked");
            Assert.AreEqual(0, w.M.Wall);
            Assert.IsFalse(w.Tilt.Tilted, "upright at the end");
            Assert.AreEqual(9000, PlayerPath.YawCdegOf(WalkedOn - Top, 0), "facing along the roof");
            Assert.AreEqual(9000, w.Pose.YawCdeg, 1);
        }

        [Test]
        public void ATreeClimb_EndsPerched_UprightOffTheGround_AndAStopDoesNothing()
        {
            var perch = new int3(5400, 5000, 0);
            using var w = new Walk(
                new[] { int3.zero, Foot, Planted, perch },
                new[] { default, default, OnFace(WallPhase.Plant, 180000, WallEase.In, WallEase.Out), Upright(WallPhase.ToPerch, 400000, WallEase.In, WallEase.Out) },
                new WallPlan { Seq = 1, SpeedMmps = WallSpeed, OnWallFrom = 255, OffWallFrom = 255, Flags = WallFlags.EndsPerched });
            var input = Click(1);
            for (int i = 0; i < 300 && (i == 0 || w.Moving); i++) { w.Step(in input); input = default; }
            Assert.AreEqual(perch, w.Pose.Mm, "in the tree, at the place to stand");
            Assert.AreEqual(2, w.M.Wall, "perched (off the ground, no Grip)");
            Assert.AreEqual(WallPhase.Perched, w.Tilt.Phase, "NGO's Perched: the crouch plays");
            Assert.AreEqual(1000, w.Tilt.UpY, "upright");
            var stop = default(PlayerInput);
            stop.Stop.Set();
            w.Step(in stop);
            Assert.AreEqual(2, w.M.Wall, "a stop does nothing in the tree");
        }

        [Test]
        public void TheWallsTilt_BlendsTheUp_AndFacesTheWayAlong()
        {
            var from = OnFace(WallPhase.WallMove);
            var to = new WallCorner { UpZ = -1000, Phase = WallPhase.WallMove };   // round a corner onto a face facing -z
            PoseTilt half = WallLeg.TiltOf(in from, in to, new int3(0, 0, 0), new int3(0, 2000, 0), true, 500, WallPhase.WallMove, 0);
            Assert.AreEqual(-707, half.UpX, 2);
            Assert.AreEqual(-707, half.UpZ, 2);
            Assert.AreEqual(1000, half.FwdY, 2, "going up the face: forward is up");
            PoseTilt sideways = WallLeg.TiltOf(in from, in from, new int3(0, 0, 0), new int3(0, 0, 3000), true, 300, WallPhase.WallMove, 0);
            Assert.AreEqual(1000, sideways.FwdZ, 2, "along the face: forward is the way along");
            Assert.AreEqual(0, WallLeg.YawOf(in sideways, 4500), "the pose's yaw follows a forward along the ground");
            Assert.AreEqual(4500, WallLeg.YawOf(in half, 4500), "and keeps its own when the forward is straight up");
            Assert.IsFalse(default(PoseTilt).Tilted);
        }
    }
}
