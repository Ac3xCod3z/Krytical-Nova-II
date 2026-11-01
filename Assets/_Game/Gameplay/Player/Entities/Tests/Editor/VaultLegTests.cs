using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;

namespace KryticalNova.Player.Entities.Tests
{
    /// <summary>
    /// The vault as a leg of the walk (M3c, `m3c-vault-design.md`): NGO's flat-key height curve in integers, the leg's own speed,
    /// the height on a real walk, and a click during the vault starting nothing (NGO ignores it; the walk goes on).
    /// </summary>
    public class VaultLegTests
    {
        static readonly int Tick = PlayerMovement.TickMicros(30);

        // a straight walk along x: start, the take-off at 3 m, the landing at 5 m, the destination at 9 m
        static VaultPlan Vault(ushort seq) => new VaultPlan
        {
            Seq = seq, LegStartMm = 3000, LegLengthMm = 2000, AirMicros = 500000, RaiseMm = 900,
            RiseEndsPermille = 250, FallStartsPermille = 750, Style = 1,
        };

        [Test]
        public void TheSmoothstep_IsUnitysFlatKeyCurve()
        {
            Assert.AreEqual(0, VaultLeg.Smooth(0));
            Assert.AreEqual(500, VaultLeg.Smooth(500));
            Assert.AreEqual(1000, VaultLeg.Smooth(1000));
            Assert.AreEqual(104, VaultLeg.Smooth(200), 1, "3(0.2)^2 - 2(0.2)^3 = 0.104");
        }

        [Test]
        public void TheHeight_RisesBeforeTheNearEdge_HoldsAcross_AndComesDown()
        {
            var v = Vault(1);
            Assert.AreEqual(0, VaultLeg.HeightMm(v, 3000), "at the take-off");
            Assert.AreEqual(450, VaultLeg.HeightMm(v, 3250), 1, "half way up the rise (smoothstep 0.5)");
            Assert.AreEqual(900, VaultLeg.HeightMm(v, 3500), "the rise ends at 25 %");
            Assert.AreEqual(900, VaultLeg.HeightMm(v, 4000), "level over the top");
            Assert.AreEqual(900, VaultLeg.HeightMm(v, 4500), "the fall starts at 75 %");
            Assert.AreEqual(450, VaultLeg.HeightMm(v, 4750), 1);
            Assert.AreEqual(0, VaultLeg.HeightMm(v, 5000), "at the landing");
            Assert.AreEqual(0, VaultLeg.HeightMm(v, 2000), "before the leg");
            Assert.AreEqual(4000, VaultLeg.SpeedMmps(v), "2 m in 0.5 s");
            Assert.IsTrue(VaultLeg.On(v, 1, 3000) && !VaultLeg.On(v, 1, 5000) && !VaultLeg.On(v, 2, 4000), "on the leg of its own walk only");
        }

        [Test]
        public void AWalkOverTheLeg_MovesAtTheLegsSpeed_RisesOverIt_IgnoresAClick_AndArrives()
        {
            // walk 1 on the server's corners and vault; the owner already holds a path of its own for click 2 (back the other way),
            // so only the vault rule keeps click 2 from starting
            using var corners = new NativeArray<int3>(new[] { int3.zero, new int3(3000, 0, 0), new int3(5000, 0, 0), new int3(9000, 0, 0) }, Allocator.Temp);
            using var back = new NativeArray<int3>(new[] { int3.zero, new int3(-5000, 0, 0) }, Allocator.Temp);
            var m = default(MoveState);
            var pose = default(PlayerPose);
            var plan = new PathPlan { Seq = 1, Count = 4 };
            var client = new ClientPlanHeader { Seq = 2, Count = 2 };
            var stats = new MoveStats { WalkMmps = 5000, RunMmps = 10000 };
            var cfg = PlayerMoveSettings.Default;
            var vault = Vault(1);
            var input = new PlayerInput { ClickSeq = 1 };
            input.Click.Set();
            int top = 0, legSpeed = -1, ticks = 0;
            bool clickedOnLeg = false;
            while (ticks++ < 200)
            {
                PlayerMovement.Step(ref m, ref pose, in input, default, in plan, corners, in client, back, in vault, default, in stats, in cfg, Tick);
                input = default;
                bool on = VaultLeg.On(vault, m.OrderSeq, m.ProgressMm);
                if (on)
                {
                    legSpeed = m.SpeedMmps;
                    top = math.max(top, pose.YMm);
                    if (!clickedOnLeg)
                    {
                        // a click during the vault: NGO ignores it, the walk goes on to its destination
                        clickedOnLeg = true;
                        input = new PlayerInput { ClickSeq = 2, GoalXMm = -5000 };
                        input.Click.Set();
                    }
                }
                if ((m.Flags & MoveFlags.Moving) == 0) break;
            }
            Assert.IsTrue(clickedOnLeg, "the walk went over the leg");
            Assert.AreEqual(4000, legSpeed, "the leg's own speed, not the walk's");
            Assert.AreEqual(900, top, 40, "it rose over the obstacle (the highest tick lands near the top)");
            Assert.AreEqual(1, m.OrderSeq, "the click during the vault started nothing");
            Assert.AreEqual(new int3(9000, 0, 0), new int3(pose.XMm, pose.YMm, pose.ZMm), "on to the destination, back on the ground");
        }
    }
}
