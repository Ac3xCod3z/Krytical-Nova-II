using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;

namespace KryticalNova.Player.Entities.Tests
{
    /// <summary>
    /// Waterstride as a leg of the walk (M3c, `m3c-waterstride-design.md` section 2): the run onto the water at the leg's own speed, on
    /// the water flag, standing at the end, ashore and back to the ground's walk; NGO's rules on the water (a stop does nothing, a click
    /// while stranded starts nothing); the Flux owed per tick.
    /// </summary>
    public class WaterLegTests
    {
        static readonly int Tick = PlayerMovement.TickMicros(30);
        static readonly MoveStats Stats = new MoveStats { WalkMmps = 5000, RunMmps = 9000 };

        // a straight walk along x: the shore run-out at 3 m, the water to 9 m (ends there, or ashore at 9 m and on to 12 m)
        static WaterPlan Water(ushort seq, bool endsOnWater, bool stranded = false) => new WaterPlan
        {
            Seq = seq, LegStartMm = 3000, LegEndMm = 9000, SpeedMmps = 9000, TurnCdegPerSecond = 72000,
            Flags = (byte)((endsOnWater ? WaterFlags.EndsOnWater : 0) | (stranded ? WaterFlags.Stranded : 0)),
        };

        static PlayerInput Click(ushort seq, bool run = true)
        {
            var input = new PlayerInput { ClickSeq = seq, Run = (byte)(run ? 1 : 0) };
            input.Click.Set();
            return input;
        }

        [Test]
        public void TheLeg_IsItsOwnWalksOnly_AndEndsAshoreOrNever()
        {
            var ashore = Water(1, false);
            Assert.IsFalse(WaterLeg.On(ashore, 1, 2999), "on the way to the shore");
            Assert.IsTrue(WaterLeg.On(ashore, 1, 3000) && WaterLeg.On(ashore, 1, 8999));
            Assert.IsFalse(WaterLeg.On(ashore, 1, 9000), "ashore");
            Assert.IsFalse(WaterLeg.On(ashore, 2, 5000), "another walk's");
            Assert.IsTrue(WaterLeg.On(Water(1, true), 1, 50000), "a run that ends on the water stays on it");
            Assert.IsTrue(WaterLeg.Stranded(Water(1, false, true), 1) && !WaterLeg.Stranded(Water(1, false, true), 2));
        }

        [Test]
        public void TheFluxOwed_IsThreeASecond_ATenthAtATick()
        {
            Assert.AreEqual(99999, WaterLeg.OwedMicro(3000, Tick), "3 Flux a second over one 30 Hz tick, in millionths");
            long owed = 0;
            for (int i = 0; i < 30; i++) owed += WaterLeg.OwedMicro(3000, Tick);
            Assert.AreEqual(3000000, owed, 30, "a second's ticks owe 3 whole points");
        }

        [Test]
        public void ARunOutOverTheWater_RunsTheLegAtItsSpeed_AndStandsOnTheWaterAtTheEnd()
        {
            using var corners = new NativeArray<int3>(new[] { int3.zero, new int3(3000, 0, 0), new int3(9000, -1200, 0) }, Allocator.Temp);
            var m = default(MoveState);
            var pose = default(PlayerPose);
            var plan = new PathPlan { Seq = 1, Count = 3 };
            var cfg = PlayerMoveSettings.Default;
            var water = Water(1, true);
            var input = Click(1);
            int legSpeed = -1, ticks = 0;
            bool wetBeforeShore = false;
            while (ticks++ < 200)
            {
                PlayerMovement.Step(ref m, ref pose, in input, default, in plan, corners, default, default, default, default,
                    in water, default, in Stats, in cfg, Tick);
                input = default;
                if (m.ProgressMm < 3000 && m.Water != 0 && (m.Flags & MoveFlags.Moving) != 0) wetBeforeShore = true;
                if (WaterLeg.On(water, m.OrderSeq, m.ProgressMm) && (m.Flags & MoveFlags.Moving) != 0) legSpeed = m.SpeedMmps;
                if ((m.Flags & MoveFlags.Moving) == 0) break;
            }
            Assert.IsFalse(wetBeforeShore, "dry until the run onto the water starts");
            Assert.AreEqual(9000, legSpeed, "the leg's own speed to the very end (no slowing)");
            Assert.AreEqual(new int3(9000, -1200, 0), pose.Mm, "on the place clicked, at the water's height");
            Assert.AreEqual(1, m.Water, "standing on the water");

            // NGO: a stop on the water does nothing; the player stays on it
            var stop = default(PlayerInput);
            stop.Stop.Set();
            PlayerMovement.Step(ref m, ref pose, in stop, default, in plan, corners, default, default, default, default, in water, default, in Stats, in cfg, Tick);
            Assert.AreEqual(1, m.Water);
        }

        [Test]
        public void ALandClickFromTheWater_RunsAshore_ThenWalksOnAtTheGroundsSpeed_OffTheWater()
        {
            // from the water (the leg from progress 0) to the shore at 6 m, then on to 9 m on the ground
            using var corners = new NativeArray<int3>(new[] { new int3(0, -1200, 0), new int3(6000, -1200, 0), new int3(9000, 0, 0) }, Allocator.Temp);
            var m = new MoveState { Water = 1 };
            var pose = new PlayerPose { YMm = -1200 };
            var plan = new PathPlan { Seq = 1, Count = 3 };
            var cfg = PlayerMoveSettings.Default;
            var water = new WaterPlan { Seq = 1, LegStartMm = 0, LegEndMm = 6000, SpeedMmps = 9000, TurnCdegPerSecond = 72000 };
            var input = Click(1, run: false);
            int dryTick = -1, ticks = 0;
            while (ticks++ < 200)
            {
                PlayerMovement.Step(ref m, ref pose, in input, default, in plan, corners, default, default, default, default,
                    in water, default, in Stats, in cfg, Tick);
                input = default;
                if (dryTick < 0 && m.Water == 0) dryTick = ticks;
                if (m.ProgressMm > 0 && m.ProgressMm < 6000) Assert.AreEqual(9000, m.SpeedMmps, "on the water: its speed, though Run is off");
                if ((m.Flags & MoveFlags.Moving) == 0) break;
            }
            Assert.Greater(dryTick, 0, "off the water once ashore");
            Assert.AreEqual(0, m.Water);
            Assert.AreEqual(new int3(9000, 0, 0), pose.Mm, "walked on to the place");
        }

        [Test]
        public void Stranded_AClickStartsNothing()
        {
            using var corners = new NativeArray<int3>(new[] { new int3(0, -1200, 0), new int3(9000, -1200, 0), new int3(10000, 0, 0) }, Allocator.Temp);
            using var other = new NativeArray<int3>(new[] { int3.zero, new int3(0, -1200, 9000) }, Allocator.Temp);
            var m = new MoveState { Water = 1 };
            var pose = new PlayerPose { YMm = -1200 };
            var order = new ServerOrder { Seq = 0x8001 };
            var plan = new PathPlan { Seq = 0x8001, Count = 3 };
            var client = new ClientPlanHeader { Seq = 2, Count = 2 };
            var cfg = PlayerMoveSettings.Default;
            var water = Water(0x8001, false, stranded: true);
            water.LegStartMm = 0;
            PlayerMovement.Step(ref m, ref pose, default, in order, in plan, corners, in client, other, default, default, in water, default, in Stats, in cfg, Tick);
            Assert.AreEqual(0x8001, m.OrderSeq, "the server's order back to the shore");
            var click = Click(2);
            PlayerMovement.Step(ref m, ref pose, in click, in order, in plan, corners, in client, other, default, default, in water, default, in Stats, in cfg, Tick);
            Assert.AreEqual(0x8001, m.OrderSeq, "a click on the way back starts nothing (NGO: back to the shore first)");
            Assert.AreEqual(9000, m.SpeedMmps, "the stranded leg's own speed");
        }

        [Test]
        public void OnTheWater_TheTurn_IsTheLegsRate()
        {
            // facing +x, the way turns to +z: at 720 deg/s a 30 Hz tick turns 24 degrees; the ground's 540 would turn 18
            using var corners = new NativeArray<int3>(new[] { int3.zero, new int3(0, -1200, 9000) }, Allocator.Temp);
            var m = new MoveState { Water = 1 };
            var pose = new PlayerPose { YMm = -1200, YawCdeg = 9000 };
            var plan = new PathPlan { Seq = 1, Count = 2 };
            var cfg = PlayerMoveSettings.Default;
            cfg.TurnCdegPerSecond = 54000;
            var water = new WaterPlan { Seq = 1, LegStartMm = 0, SpeedMmps = 9000, TurnCdegPerSecond = 72000, Flags = WaterFlags.EndsOnWater };
            var input = Click(1);
            PlayerMovement.Step(ref m, ref pose, in input, default, in plan, corners, default, default, default, default, in water, default, in Stats, in cfg, Tick);
            Assert.AreEqual(2399, PlayerMovement.TurnStepCdeg(72000, Tick), "about 24 degrees a tick");
            Assert.AreEqual(9000 - 2399, pose.YawCdeg, "the water's rate toward +z, not the ground's 18 degrees");
        }
    }
}
