using System.Collections.Generic;
using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;

namespace KryticalNova.Player.Entities.Tests
{
    /// <summary>
    /// The jump in the server and owner worlds (M3b, plan section 53): it lands where the server planned, the owner flies it from the
    /// press, Flux is taken once at take-off, and NGO's refusals hold (in the air, landing cooldown, unpaid).
    /// </summary>
    public class JumpWorldTests
    {
        /// <summary>Straight to the clicked point; apex 1.4 m over the higher end (the clearance of a short hop).</summary>
        sealed class StubPlanner : IJumpPlanner
        {
            public int Calls;
            public bool TryPlan(Entity ghost, int3 fromFeetMm, int3 clickedMm, out int3 landingFeetMm, out int apexYMm, out string reason)
            {
                Calls++;
                landingFeetMm = clickedMm;
                apexYMm = math.max(fromFeetMm.y, clickedMm.y) + 1400;
                reason = null;
                return true;
            }
        }

        sealed class StubCost : IJumpCost
        {
            public bool CanPayNow = true;
            public readonly List<uint> PaidOnTicks = new List<uint>();
            public System.Func<uint> Tick;
            public bool CanPay(Entity ghost, out string reason) { reason = CanPayNow ? null : "Not enough Flux."; return CanPayNow; }
            public void Pay(Entity ghost) => PaidOnTicks.Add(Tick());
        }

        static (PlayerRig rig, StubCost cost) Rig(int delayMs)
        {
            var rig = PlayerRig.Create(1, delayMs);
            var cost = new StubCost { Tick = () => rig.ServerTick };
            var planner = new StubPlanner();
            var jump = rig.Server.GetExistingSystemManaged<PlayerJumpServerSystem>();
            jump.Planner = planner;
            jump.Cost = cost;
            rig.Server.GetExistingSystemManaged<PlayerJumpPaySystem>().Cost = cost;
            rig.Clients[0].GetExistingSystemManaged<PlayerInputGatherSystem>().JumpPlanner = planner;
            return (rig, cost);
        }

        MotionPlan ServerPlan(PlayerRig rig) => rig.Get<MotionPlan>(rig.Server, rig.ServerGhosts[0]);

        [TestCase(0)]
        [TestCase(100)]
        public void AJump_LandsWhereTheServerPlanned_TheOwnerFliesItToo_AndFluxIsTakenAtTakeoff(int delayMs)
        {
            var (rig, cost) = Rig(delayMs);
            using (rig)
            {
                var goal = new int3(4000, 0, 3000);
                rig.Sources[0].Jump(goal);
                int serverTop = 0, ownerTop = 0;
                for (int i = 0; i < 90; i++)
                {
                    rig.Tick();
                    serverTop = math.max(serverTop, rig.ServerPose().YMm);
                    ownerTop = math.max(ownerTop, rig.OwnerPose().YMm);
                }
                var plan = ServerPlan(rig);
                Assert.AreEqual(MotionKind.Jump, plan.Kind, "the server planned the jump; last refusal: "
                    + rig.Server.GetExistingSystemManaged<PlayerJumpServerSystem>().LastRefusal);
                Assert.AreEqual(goal, rig.ServerPose().Mm, "the server lands on the planned point");
                Assert.AreEqual(goal, rig.OwnerPose().Mm, "the owner lands there too");
                Assert.AreEqual(1400, serverTop, 5, "the server's arc reaches the apex");
                Assert.AreEqual(1400, ownerTop, 5, "the owner flew the same arc");
                Assert.AreEqual(1, cost.PaidOnTicks.Count, "Flux taken once");
                Assert.AreEqual(6, (int)(cost.PaidOnTicks[0] - plan.StartTick.TickIndexForValidTick), "on the take-off tick (0.183 s at 30 Hz)");
                Assert.AreNotEqual(0, plan.Flags & MotionFlags.Paid);
                Assert.AreEqual(JumpFlight.YawCdeg(int3.zero, goal), rig.ServerPose().YawCdeg, "it faces the way it jumped");
            }
        }

        [Test]
        public void TheOwner_LeavesTheGround_BeforeTheServersPlanCouldArrive()
        {
            var (rig, _) = Rig(100);
            using (rig)
            {
                rig.Sources[0].Jump(new int3(5000, 0, 0));
                // 100 ms each way: the server's plan needs about 7 ticks to come back; the owner takes off on tick 6 of its own plan
                int lifted = -1;
                for (int i = 0; i < 12 && lifted < 0; i++)
                {
                    rig.Tick();
                    var own = rig.Get<MotionPlan>(rig.Clients[0], rig.GhostIn(0, 0));
                    if (rig.OwnerPose().YMm > 0 && own.Kind == MotionKind.None) lifted = i;
                }
                Assert.GreaterOrEqual(lifted, 0, "the owner was in the air on its own plan before the server's plan reached it");
            }
        }

        [TestCase(0)]
        [TestCase(100)]
        public void TheServer_StartsTheJump_OnTheOwnersPressTick(int delayMs)
        {
            // GUI run predict-2 (31 Oct): a late click got the server's tick, and its plan restarted the owner's arc
            var (rig, _) = Rig(delayMs);
            using (rig)
            {
                rig.Sources[0].Jump(new int3(4000, 0, 0));
                for (int i = 0; i < 20; i++) rig.Tick();
                MotionPlan own = rig.Get<ClientMotionPlan>(rig.Clients[0], rig.GhostIn(0, 0)).Plan;
                MotionPlan server = ServerPlan(rig);
                Assert.AreEqual(MotionKind.Jump, own.Kind, "the owner planned it");
                Assert.AreEqual(MotionKind.Jump, server.Kind, "the server planned it");
                Assert.AreEqual(own.StartTick, server.StartTick, "one start tick on both sides");
            }
        }

        [Test]
        public void AClickInTheAir_IsRefused_AndASecondJumpWaitsForTheCooldown()
        {
            var (rig, _) = Rig(0);
            using (rig)
            {
                var jump = rig.Server.GetExistingSystemManaged<PlayerJumpServerSystem>();
                var walk = rig.Server.GetExistingSystemManaged<PlayerServerPlanSystem>();
                rig.Sources[0].Jump(new int3(6000, 0, 0));
                rig.Tick(15);
                int walkRefusedBefore = walk.Refused;
                rig.Sources[0].Click(new int3(-8000, 0, 0));
                rig.Tick(2);
                Assert.AreEqual(walkRefusedBefore + 1, walk.Refused, "a walk click in the air is refused");
                rig.Sources[0].Jump(new int3(0, 0, 6000));
                rig.Tick(2);
                StringAssert.StartsWith("Finish this jump", jump.LastRefusal);
                Assert.AreEqual(new int3(6000, 0, 0), rig.Get<ClientMotionPlan>(rig.Clients[0], rig.GhostIn(0, 0)).Plan.ToMm,
                    "the owner did not fly a jump clicked in the air");
                rig.Tick(60);
                Assert.AreEqual(new int3(6000, 0, 0), rig.ServerPose().Mm, "the first jump landed; nothing else moved it");
                // the landing cooldown (0.25 s = 8 ticks) is long over: a new jump is taken
                int planned = jump.Planned;
                rig.Sources[0].Jump(new int3(6000, 0, 6000));
                rig.Tick(70);
                Assert.AreEqual(planned + 1, jump.Planned);
                Assert.AreEqual(new int3(6000, 0, 6000), rig.ServerPose().Mm);
            }
        }

        [Test]
        public void ATeleportInTheAir_CallsTheJumpOff_AndTheBodyStaysWhereItWasPut()
        {
            var (rig, _) = Rig(0);
            using (rig)
            {
                rig.Sources[0].Jump(new int3(6000, 0, 0));
                rig.Tick(15);
                Assert.Greater(rig.ServerPose().YMm, 0, "in the air when the teleport comes");
                var spot = new int3(-20000, 0, 7000);
                rig.Teleport(0, spot);
                rig.Tick(45);
                Assert.AreEqual(MotionKind.None, ServerPlan(rig).Kind);
                Assert.AreEqual(spot, rig.ServerPose().Mm, "the flight did not put it back on the arc");
                Assert.AreEqual(spot, rig.OwnerPose().Mm);
            }
        }

        [Test]
        public void AJumpRightAfterTheLanding_IsRefused_ForTheCooldown()
        {
            var (rig, _) = Rig(0);
            using (rig)
            {
                var jump = rig.Server.GetExistingSystemManaged<PlayerJumpServerSystem>();
                rig.Sources[0].Jump(new int3(6000, 0, 0));
                // touchdown on tick 43 after the press; ask again a tick or two after it
                rig.Tick(46);
                rig.Sources[0].Jump(new int3(0, 0, 0));
                rig.Tick(3);
                StringAssert.StartsWith("Let the landing settle", jump.LastRefusal);
            }
        }

        [Test]
        public void AJumpThatCannotBePaid_IsRefused_AndCostsNothing()
        {
            var (rig, cost) = Rig(0);
            using (rig)
            {
                cost.CanPayNow = false;
                rig.Sources[0].Jump(new int3(5000, 0, 0));
                rig.Tick(60);
                Assert.AreEqual(MotionKind.None, ServerPlan(rig).Kind);
                Assert.AreEqual(int3.zero, rig.ServerPose().Mm, "never left");
                Assert.AreEqual(int3.zero, rig.OwnerPose().Mm, "the owner's own plan was dropped with the refusal");
                Assert.AreEqual(0, cost.PaidOnTicks.Count);
            }
        }

        [Test]
        public void FluxGoneBeforeTakeoff_CallsTheJumpOff_Free_BackOnTheStart()
        {
            var (rig, cost) = Rig(0);
            using (rig)
            {
                rig.Sources[0].Jump(new int3(5000, 0, 0));
                rig.Tick(2);
                Assert.AreEqual(MotionKind.Jump, ServerPlan(rig).Kind, "planned while it could pay");
                cost.CanPayNow = false;             // spent elsewhere before the take-off tick
                rig.Tick(60);
                Assert.AreEqual(1, rig.Server.GetExistingSystemManaged<PlayerJumpPaySystem>().Aborted);
                Assert.AreEqual(0, cost.PaidOnTicks.Count);
                Assert.AreEqual(int3.zero, rig.ServerPose().Mm);
                Assert.AreEqual(int3.zero, rig.OwnerPose().Mm);
            }
        }
    }
}
