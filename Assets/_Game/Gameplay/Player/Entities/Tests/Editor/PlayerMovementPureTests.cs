using System;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;

namespace KryticalNova.Player.Entities.Tests
{
    /// <summary>
    /// Layers A and B of the test matrix: the walk as plain integer code, no world, no netcode. What these prove is what makes
    /// the server and the owning client agree: the same inputs always give the same state.
    /// </summary>
    public class PlayerMovementPureTests
    {
        const int Tick30 = 33333;

        // ------------------------------------------------------------------------------------------------ a little harness
        sealed class Walker : IDisposable
        {
            public MoveState M;
            public PlayerPose Pose;
            public PlayerInput Input;
            public ServerOrder Order;
            public PathPlan Plan;
            public ClientPlanHeader Client;
            public MoveStats Stats = new MoveStats { WalkMmps = 5000, RunMmps = 9000 };
            public PlayerMoveSettings Cfg = PlayerMoveSettings.Default;
            public NativeArray<int3> ServerCorners = new NativeArray<int3>(PlayerLimits.MaxCorners, Allocator.Persistent);
            public NativeArray<int3> ClientCorners = new NativeArray<int3>(PlayerLimits.MaxCorners, Allocator.Persistent);
            public int Ticks;

            public void SetClient(ushort seq, params int3[] corners)
            {
                for (int i = 0; i < corners.Length; i++) ClientCorners[i] = corners[i];
                Client.Seq = seq; Client.Count = (byte)corners.Length;
            }

            public void SetServer(ushort seq, params int3[] corners)
            {
                for (int i = 0; i < corners.Length; i++) ServerCorners[i] = corners[i];
                Plan.Seq = seq; Plan.Count = (byte)corners.Length;
            }

            public void Click(ushort seq, bool run = false)
            {
                Input.Click.Set(); Input.ClickSeq = seq; Input.Run = (byte)(run ? 1 : 0);
            }

            /// <summary>One tick; the events of the input are used up afterwards, like a command stream delivers them once.</summary>
            public void Step()
            {
                PlayerMovement.Step(ref M, ref Pose, in Input, in Order, in Plan, ServerCorners, in Client, ClientCorners, in Stats, in Cfg, Tick30);
                Input = default;
                Ticks++;
            }

            public int RunUntilIdle(int max = 5000)
            {
                int n = 0;
                while ((M.Flags & MoveFlags.Moving) != 0 && n < max) { Step(); n++; }
                return n;
            }

            public void Dispose() { ServerCorners.Dispose(); ClientCorners.Dispose(); }
        }

        static int3 P(int x, int y, int z) => new int3(x, y, z);

        // ------------------------------------------------------------------------------------------------ path math
        [Test]
        public void ISqrt_IsExactFloor_ForManyValues()
        {
            var rnd = new System.Random(7);
            for (int i = 0; i < 20000; i++)
            {
                long v = i < 1000 ? i : (long)(rnd.NextDouble() * 4.0e12);
                long r = PlayerPath.ISqrt(v);
                Assert.LessOrEqual(r * r, v, "v=" + v);
                Assert.Greater((r + 1) * (r + 1), v, "v=" + v);
            }
            Assert.AreEqual(0, PlayerPath.ISqrt(-5));
            Assert.AreEqual(3_000_000, PlayerPath.ISqrt(9_000_000_000_000L));
        }

        [Test]
        public void PointAt_WalksCorners_AndEndsOnTheLastPoint()
        {
            using var c = new NativeArray<int3>(3, Allocator.Temp);
            c[0] = P(0, 0, 0); c[1] = P(10000, 0, 0); c[2] = P(10000, 0, 10000);
            var start = P(0, 0, 0);
            Assert.AreEqual(20000, PlayerPath.LengthMm(start, c, 3));
            PlayerPath.PointAt(start, c, 3, 0, out var p0, out _);
            Assert.AreEqual(P(0, 0, 0), p0);
            PlayerPath.PointAt(start, c, 3, 4000, out var p1, out var d1);
            Assert.AreEqual(P(4000, 0, 0), p1);
            Assert.AreEqual(P(10000, 0, 0), d1);
            PlayerPath.PointAt(start, c, 3, 15000, out var p2, out _);
            Assert.AreEqual(P(10000, 0, 5000), p2);
            PlayerPath.PointAt(start, c, 3, 99999, out var p3, out _);
            Assert.AreEqual(P(10000, 0, 10000), p3, "a progress past the end is the last point");
        }

        [Test]
        public void PathStartsFromTheStartPoint_NotFromTheFirstCorner()
        {
            using var c = new NativeArray<int3>(2, Allocator.Temp);
            c[0] = P(500, 0, 0);          // the planner's start differs from where the player really is
            c[1] = P(10000, 0, 0);
            var start = P(0, 0, 0);
            Assert.AreEqual(10000, PlayerPath.LengthMm(start, c, 2));
        }

        // ------------------------------------------------------------------------------------------------ the walk
        [Test]
        public void Click_WalksToTheGoalAndArrivesExactly()
        {
            using var w = new Walker();
            w.SetClient(1, P(0, 0, 0), P(20000, 0, 0));
            w.Click(1);
            w.Step();
            Assert.AreNotEqual(0, w.M.Flags & MoveFlags.Moving);
            Assert.AreEqual(1, w.M.OrderSeq);
            int n = w.RunUntilIdle();
            Assert.Less(n, 400, "a 20 m walk at 5 m/s takes about 4.5 s");
            Assert.AreEqual(20000, w.Pose.XMm);
            Assert.AreEqual(0, w.M.SpeedMmps);
            Assert.AreEqual(0, w.M.ProgressMm);
            Assert.AreEqual(9000, w.Pose.YawCdeg, "facing +X is 90 degrees");
        }

        [Test]
        public void SpeedRisesToWalkSpeed_ThenEasesInTheArrivalZone()
        {
            using var w = new Walker();
            w.SetClient(1, P(0, 0, 0), P(60000, 0, 0));
            w.Click(1);
            int top = 0; int lastSpeed = 0; bool sawEase = false;
            for (int i = 0; i < 2000 && (i == 0 || (w.M.Flags & MoveFlags.Moving) != 0); i++)
            {
                w.Step();
                top = Math.Max(top, w.M.SpeedMmps);
                if (lastSpeed == 5000 && w.M.SpeedMmps < 5000 && w.M.SpeedMmps > 0) sawEase = true;
                lastSpeed = (w.M.Flags & MoveFlags.Moving) != 0 ? w.M.SpeedMmps : lastSpeed;
            }
            Assert.AreEqual(5000, top, "walk speed is reached and not exceeded");
            Assert.IsTrue(sawEase, "the speed drops near the destination (arrival factor)");
        }

        [Test]
        public void RunUsesRunSpeed_AndIsFasterThanWalking()
        {
            int walk, run;
            using (var a = new Walker()) { a.SetClient(1, P(0, 0, 0), P(40000, 0, 0)); a.Click(1, false); a.Step(); walk = 1 + a.RunUntilIdle(); }
            using (var b = new Walker()) { b.SetClient(1, P(0, 0, 0), P(40000, 0, 0)); b.Click(1, true); b.Step(); run = 1 + b.RunUntilIdle(); }
            Assert.Less(run, walk);
            using var c = new Walker();
            c.SetClient(1, P(0, 0, 0), P(60000, 0, 0)); c.Click(1, true);
            int top = 0;
            for (int i = 0; i < 400; i++) { c.Step(); top = Math.Max(top, c.M.SpeedMmps); }
            Assert.AreEqual(9000, top);
        }

        [Test]
        public void ManyShortCornersInOneTick_NeverOvershootACorner()
        {
            // 34 m/s is 1133 mm a tick; corners 300 mm apart are crossed four at a time
            using var w = new Walker();
            w.Stats = new MoveStats { WalkMmps = 34000, RunMmps = 34000 };
            w.Cfg.AccelMmps2 = 10_000_000;
            var pts = new int3[PlayerLimits.MaxCorners];
            for (int i = 0; i < pts.Length; i++) pts[i] = P(i * 300, 0, (i % 2) * 100);
            w.SetClient(1, pts);
            w.Click(1);
            int guard = 0;
            do { w.Step(); Assert.Less(w.Pose.XMm, 9301); Assert.LessOrEqual(guard++, 200); } while ((w.M.Flags & MoveFlags.Moving) != 0);
            Assert.AreEqual(pts[pts.Length - 1], w.Pose.Mm);
        }

        [Test]
        public void SameInputsTwice_GiveIdenticalState_EveryTick()
        {
            using var a = new Walker();
            using var b = new Walker();
            var pts = new[] { P(0, 0, 0), P(8000, 200, 3000), P(12000, 0, 9000), P(-4000, 0, 9500) };
            a.SetClient(3, pts); b.SetClient(3, pts);
            for (int t = 0; t < 600; t++)
            {
                if (t == 2) { a.Click(3, true); b.Click(3, true); }
                a.Step(); b.Step();
                Assert.AreEqual(a.M, b.M, "tick " + t);
                Assert.AreEqual(a.Pose, b.Pose, "tick " + t);
            }
        }

        [Test]
        public void SubMillimetreRemainderIsKept_SoSpeedTimesTimeIsExact()
        {
            using var w = new Walker();
            w.Cfg.AccelMmps2 = 100_000_000;
            w.Stats = new MoveStats { WalkMmps = 1000, RunMmps = 1000 };
            w.Cfg.ArrivalMinRadiusMm = 0; w.Cfg.ArrivalMaxRadiusMm = 0;
            w.SetClient(1, P(0, 0, 0), P(1_000_000, 0, 0));
            w.Click(1);
            for (int i = 0; i < 300; i++) w.Step();       // 300 ticks of 33333 us = 9.9999 s at 1000 mm/s
            Assert.AreEqual(9999, w.M.ProgressMm, "1000 mm/s over 9.9999 s is 9999.9 mm: 9999 whole, the rest kept");
            Assert.AreEqual(900_000, w.M.RemMicro);
        }

        [Test]
        public void Stop_EndsTheWalkWhereThePlayerIs()
        {
            using var w = new Walker();
            w.SetClient(1, P(0, 0, 0), P(50000, 0, 0));
            w.Click(1);
            for (int i = 0; i < 60; i++) w.Step();
            int x = w.Pose.XMm;
            Assert.Greater(x, 1000);
            Assert.Less(x, 50000);
            w.Input.Stop.Set();
            w.Step();
            Assert.AreEqual(0, w.M.Flags & MoveFlags.Moving);
            Assert.AreEqual(0, w.M.SpeedMmps);
            Assert.AreEqual(x, w.Pose.XMm, "a stop does not move the player");
            for (int i = 0; i < 30; i++) w.Step();
            Assert.AreEqual(x, w.Pose.XMm);
        }

        [Test]
        public void ServerOrder_OverridesAClickOfTheSameTick()
        {
            using var w = new Walker();
            w.SetClient(5, P(0, 0, 0), P(0, 0, 20000));          // the click goes +Z
            w.SetServer(PlayerLimits.ServerSeqBit | 1, P(0, 0, 0), P(20000, 0, 0));   // the server order goes +X
            w.Order = new ServerOrder { Seq = PlayerLimits.ServerSeqBit | 1 };
            w.Click(5);
            w.Step();
            w.RunUntilIdle();
            Assert.AreEqual(20000, w.Pose.XMm);
            Assert.AreEqual(0, w.Pose.ZMm);
            Assert.AreEqual(PlayerLimits.ServerSeqBit | 1, w.M.OrderSeq);
        }

        [Test]
        public void ServerOrder_IsNotRestartedByALaterClick()
        {
            using var w = new Walker();
            w.SetServer(PlayerLimits.ServerSeqBit | 1, P(0, 0, 0), P(5000, 0, 0));
            w.Order = new ServerOrder { Seq = PlayerLimits.ServerSeqBit | 1 };
            w.Step();
            w.RunUntilIdle();
            Assert.AreEqual(5000, w.Pose.XMm);
            w.SetClient(2, P(5000, 0, 0), P(5000, 0, 8000));
            w.Click(2);
            w.Step();
            w.RunUntilIdle();
            Assert.AreEqual(8000, w.Pose.ZMm, "the click walks; the old server order stays done");
            Assert.AreEqual(5000, w.Pose.XMm);
        }

        [Test]
        public void ServerCornersOverrideTheOwnersPlan_WhenTheyCarryTheSameOrderNumber()
        {
            using var w = new Walker();
            w.SetClient(1, P(0, 0, 0), P(10000, 0, 0));
            w.SetServer(1, P(0, 0, 0), P(0, 0, 10000));            // the server planned another way for the same click
            w.Click(1);
            w.Step();
            w.RunUntilIdle();
            Assert.AreEqual(0, w.Pose.XMm);
            Assert.AreEqual(10000, w.Pose.ZMm);
        }

        [Test]
        public void ARefusedClick_IsNotStartedAgain()
        {
            using var w = new Walker();
            w.SetClient(4, P(0, 0, 0), P(10000, 0, 0));
            w.M.RefusedSeq = 4;
            w.Click(4);
            w.Step();
            Assert.AreEqual(0, w.M.Flags & MoveFlags.Moving);
            Assert.AreEqual(0, w.Pose.XMm);
        }

        [Test]
        public void AClickWithoutAnyPath_IsIgnored()
        {
            using var w = new Walker();
            w.Click(9);
            w.Step();
            Assert.AreEqual(0, w.M.Flags);
            Assert.AreEqual(0, w.M.OrderSeq);
        }

        [Test]
        public void ANewClickMidWalk_StartsFromTheCurrentPose_AndKeepsTheSpeed()
        {
            using var w = new Walker();
            w.SetClient(1, P(0, 0, 0), P(50000, 0, 0));
            w.Click(1);
            for (int i = 0; i < 40; i++) w.Step();
            int x = w.Pose.XMm, speed = w.M.SpeedMmps;
            Assert.Greater(speed, 0);
            w.SetClient(2, P(x, 0, 0), P(x, 0, 30000));
            w.Click(2);
            w.Step();
            Assert.AreEqual(x, w.M.StartXMm, "the new walk begins at the pose, wherever the plan said");
            Assert.GreaterOrEqual(w.M.SpeedMmps, speed - 1);
            w.RunUntilIdle();
            Assert.AreEqual(x, w.Pose.XMm);
            Assert.AreEqual(30000, w.Pose.ZMm);
        }

        [Test]
        public void ClientSeqWraps_AndServerSeqKeepsItsHighBit()
        {
            Assert.AreEqual(1, PlayerInputGatherSystem.NextClientSeq(0));
            Assert.AreEqual(1, PlayerInputGatherSystem.NextClientSeq(PlayerLimits.ClientSeqMax));
            Assert.AreEqual(8, PlayerInputGatherSystem.NextClientSeq(7));
            ushort s = 0;
            for (int i = 0; i < 40000; i++)
            {
                s = PlayerServerPlanSystem.NextServerSeq(s);
                Assert.AreNotEqual(0, s & PlayerLimits.ServerSeqBit);
                Assert.AreNotEqual(PlayerLimits.ServerSeqBit, s);
            }
        }

        [Test]
        public void RandomWalks_NeverLeaveThePathOrBreakTheInvariants()
        {
            var rnd = new System.Random(2026);
            for (int round = 0; round < 40; round++)
            {
                using var w = new Walker();
                int n = rnd.Next(2, 12);
                var pts = new int3[n];
                pts[0] = P(0, 0, 0);
                for (int i = 1; i < n; i++) pts[i] = pts[i - 1] + P(rnd.Next(-9000, 9000), rnd.Next(-300, 300), rnd.Next(-9000, 9000));
                w.SetClient(1, pts);
                w.Click(1, rnd.Next(2) == 0);
                using var arr = new NativeArray<int3>(pts, Allocator.Temp);
                int total = PlayerPath.LengthMm(P(0, 0, 0), arr, n);
                int guard = 0;
                do
                {
                    w.Step();
                    Assert.GreaterOrEqual(w.M.ProgressMm, 0);
                    Assert.LessOrEqual(w.M.ProgressMm, total);
                    Assert.Less(guard++, 20000);
                } while ((w.M.Flags & MoveFlags.Moving) != 0);
                if (total > 0) Assert.AreEqual(pts[n - 1], w.Pose.Mm, "round " + round);
            }
        }
    }
}
