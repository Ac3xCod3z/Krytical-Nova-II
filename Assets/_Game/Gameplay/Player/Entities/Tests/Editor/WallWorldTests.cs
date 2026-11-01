using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using KryticalNova.Networking.Entities.Tests;

namespace KryticalNova.Player.Entities.Tests
{
    /// <summary>
    /// Wallstride's wall walk in the server and owner worlds (M3c, `m3c-wallstride-design.md` section 3, step 3): both plan the same way
    /// onto the wall and stand on it tilted onto the face, the server takes Grip each second while on it, and out of Grip it pushes the
    /// player off to the ground in front of the wall (NGO). On the wall a stop does nothing, every click is the wall's, and a click during
    /// the plant starts nothing.
    /// </summary>
    public class WallWorldTests
    {
        const int FaceX = 5970;   // the feet on the wall, out from a face at x = 6 m facing -x
        static readonly int3 Landing = new int3(3000, 0, 0);
        const int CatchClick = 55555;   // a Jump click at a wall the graph does not cover (part 4)
        static readonly int3 CatchAt = new int3(5600, 1400, 0);

        /// <summary>Refuses every ordinary jump: a catch test must never fall back to one.</summary>
        sealed class NoJumpPlanner : IJumpPlanner
        {
            public bool TryPlan(Entity ghost, int3 fromFeetMm, int3 clickedMm, out int3 landingFeetMm, out int apexYMm, out string reason)
            {
                landingFeetMm = fromFeetMm;
                apexYMm = fromFeetMm.y;
                reason = "an ordinary jump (the test wanted the catch)";
                return false;
            }
        }

        /// <summary>A wall at x = 6 m (stands in for the surface graph, the NavMesh and NGO's rules).</summary>
        sealed class StubWallPlanner : IWallPlanner
        {
            public int Plans, Refusals, OnWallPlans;

            static WallCorner Face(byte phase, int micros = 0, byte up = WallEase.Linear, byte along = WallEase.Linear) => new WallCorner
            {
                Micros = micros, Ease = WallCorner.Eases(up, along), Phase = phase, UpX = -1000, FwdY = 1000,
            };

            public WallAnswer Plan(Entity ghost, int3 poseMm, int yawCdeg, in PoseTilt tilt, int3 pointMm, int3 normalPermille, bool run, bool jump,
                bool onWall, NativeList<int3> corners, NativeList<WallCorner> legs, out WallPlan plan, out string reason)
            {
                plan = default;
                reason = null;
                if (pointMm.x == 99999) { Refusals++; reason = "That cannot be climbed."; return WallAnswer.Refused; }
                if (pointMm.x == CatchClick) return WallAnswer.NotWall;   // no graph there: the catch's (Catch)
                Plans++;
                if (pointMm.x == 77777)
                {
                    // a straight climb (part 2): the run to the foot, NGO's plant, up the face, over the top; Flux at the plant
                    corners.Add(poseMm); legs.Add(default);
                    corners.Add(new int3(5000, 0, poseMm.z)); legs.Add(default);
                    var lean = new WallCorner { Phase = WallPhase.Plant, Micros = 180000, Ease = WallCorner.Eases(WallEase.In, WallEase.Out), UpY = 978, UpX = -208, FwdX = 1000 };
                    corners.Add(new int3(5600, 700, poseMm.z)); legs.Add(lean);
                    lean.Phase = WallPhase.Climb; lean.Micros = 500000; lean.Ease = 0;
                    corners.Add(new int3(5600, 4000, poseMm.z)); legs.Add(lean);
                    corners.Add(new int3(6500, 5000, poseMm.z));
                    legs.Add(new WallCorner { Phase = WallPhase.Mantle, Micros = 400000, Ease = WallCorner.Eases(WallEase.In, WallEase.Out), UpY = 1000, FwdX = 1000 });
                    plan = new WallPlan { SpeedMmps = 9000, OnWallFrom = 255, OffWallFrom = 255, Flags = WallFlags.PayOnPlant };
                    return WallAnswer.Planned;
                }
                corners.Add(poseMm);
                legs.Add(onWall ? Face(WallPhase.WallMove) : default);
                if (!onWall)
                {
                    corners.Add(new int3(5000, 0, poseMm.z)); legs.Add(default);
                    corners.Add(new int3(FaceX, 500, poseMm.z)); legs.Add(Face(WallPhase.Plant, 320000, WallEase.In, WallEase.Out));
                    corners.Add(new int3(FaceX, pointMm.y, poseMm.z)); legs.Add(Face(WallPhase.WallMove));
                    plan = new WallPlan { SpeedMmps = 9000, OnWallFrom = 3, OffWallFrom = 4, Flags = WallFlags.EndsOnWall };
                    return WallAnswer.Planned;
                }
                OnWallPlans++;
                corners.Add(new int3(FaceX, pointMm.y, pointMm.z)); legs.Add(Face(WallPhase.WallMove));
                plan = new WallPlan { SpeedMmps = 9000, OnWallFrom = 1, OffWallFrom = 2, Flags = WallFlags.EndsOnWall };
                return WallAnswer.Planned;
            }

            public int Catches;

            /// <summary>A jump at the wall at x = 6 m: caught 1.4 m up at its foot, then NGO's plant from a jump, up the face, over the top.</summary>
            public WallAnswer Catch(Entity ghost, int3 poseMm, int yawCdeg, int3 pointMm, int3 normalPermille, bool run,
                NativeList<int3> corners, NativeList<WallCorner> legs, out WallPlan plan, out int3 catchMm, out int apexYMm, out string reason)
            {
                plan = default;
                reason = null;
                catchMm = poseMm;
                apexYMm = poseMm.y;
                if (pointMm.x != CatchClick) return WallAnswer.NotWall;
                Catches++;
                catchMm = CatchAt;
                apexYMm = CatchAt.y + 800;
                corners.Add(CatchAt); legs.Add(default);
                var lean = new WallCorner { Phase = WallPhase.Plant, Micros = 150000, Ease = WallCorner.Eases(WallEase.In, WallEase.Out), UpY = 978, UpX = -208, FwdX = 1000 };
                corners.Add(new int3(5600, 2000, 0)); legs.Add(lean);
                lean.Phase = WallPhase.Climb; lean.Micros = 400000; lean.Ease = 0;
                corners.Add(new int3(5600, 4000, 0)); legs.Add(lean);
                corners.Add(new int3(6500, 5000, 0));
                legs.Add(new WallCorner { Phase = WallPhase.Mantle, Micros = 400000, Ease = WallCorner.Eases(WallEase.In, WallEase.Out), UpY = 1000, FwdX = 1000 });
                plan = new WallPlan { SpeedMmps = 9000, OnWallFrom = 255, OffWallFrom = 255, Flags = WallFlags.PayOnPlant };
                return WallAnswer.Planned;
            }

            public bool PushOff(Entity ghost, int3 poseMm, int yawCdeg, in PoseTilt tilt, NativeList<int3> corners, NativeList<WallCorner> legs, out WallPlan plan)
            {
                corners.Add(poseMm); legs.Add(Face(WallPhase.WallMove));
                corners.Add(new int3(4500, poseMm.y + 1200, poseMm.z));
                legs.Add(new WallCorner { Micros = 300000, Ease = WallCorner.Eases(WallEase.Out, WallEase.Linear), Phase = WallPhase.PushOff, UpY = 1000, FwdX = -1000 });
                corners.Add(new int3(Landing.x, Landing.y, poseMm.z));
                legs.Add(new WallCorner { Micros = 500000, Ease = WallCorner.Eases(WallEase.In, WallEase.Linear), Phase = WallPhase.PushOff, UpY = 1000, FwdX = -1000 });
                plan = new WallPlan { SpeedMmps = 9000, OnWallFrom = 255, OffWallFrom = 1 };
                return true;
            }
        }

        /// <summary>A purse of whole Flux points.</summary>
        sealed class StubGrip : IWallGrip
        {
            public int Points;
            public int GripPerSecondMilli => 15000;
            public int ClimbFlux => 12;

            public bool TryPay(Entity ghost, int points)
            {
                if (Points < points) return false;
                Points -= points;
                return true;
            }
        }

        static PlayerRig Rig(int delayMs, StubWallPlanner planner, StubGrip grip)
        {
            var rig = PlayerRig.Create(1, delayMs);
            rig.Server.GetExistingSystemManaged<PlayerServerPlanSystem>().WallPlanner = planner;
            var system = rig.Server.GetExistingSystemManaged<WallGripSystem>();
            system.Planner = planner;
            system.Grip = grip;
            rig.Clients[0].GetExistingSystemManaged<PlayerInputGatherSystem>().WallPlanner = planner;
            return rig;
        }

        static readonly int3 Wall = new int3(-1000, 0, 0);

        [TestCase(0)]
        [TestCase(100)]
        public void TheServerAndTheOwner_WalkOntoTheWall_AndStandOnItTilted_PayingGripEachSecond(int delayMs)
        {
            var planner = new StubWallPlanner();
            var grip = new StubGrip { Points = 1000 };
            using var rig = Rig(delayMs, planner, grip);
            var top = new int3(FaceX, 6000, 0);
            rig.Sources[0].Wall(new int3(6000, 6000, 0), Wall);
            rig.Tick(150);   // 5 s: the walk to the foot, the plant, up the face, then standing on it
            var server = rig.Get<WallPlan>(rig.Server, rig.ServerGhosts[0]);
            var own = rig.Get<ClientWallPlan>(rig.Clients[0], rig.GhostIn(0, 0)).Plan;
            Assert.AreNotEqual(0, server.Seq, "the server planned the wall walk for the click");
            Assert.AreEqual(server.Seq, own.Seq, "the owner's own plan belongs to the same click");
            Assert.AreEqual(top, rig.ServerPose().Mm, "the server's body stands where clicked, on the wall");
            Assert.AreEqual(top, rig.OwnerPose().Mm, "the owner's too");
            Assert.AreEqual(1, rig.ServerMove().Wall, "on the wall (server)");
            Assert.AreEqual(1, rig.OwnerMove().Wall, "on the wall (owner)");
            var serverTilt = rig.Get<PoseTilt>(rig.Server, rig.ServerGhosts[0]);
            var ownTilt = rig.Get<PoseTilt>(rig.Clients[0], rig.GhostIn(0, 0));
            Assert.AreEqual(-1000, serverTilt.UpX, "standing on the face (server)");
            Assert.AreEqual(-1000, ownTilt.UpX, "standing on the face (owner)");
            Assert.AreEqual(WallPhase.WallHold, serverTilt.Phase);
            var system = rig.Server.GetExistingSystemManaged<WallGripSystem>();
            Assert.That(system.Paid, Is.InRange(40, 70), "15 Flux a second for the 3-4 s on the wall, whole points");
            Assert.AreEqual(0, system.PushOffs);

            // NGO: a stop on the wall does nothing; every click on it is the wall's (a plain click goes to the wall walk)
            rig.Sources[0].Stop();
            rig.Tick(10);
            Assert.AreEqual(top, rig.ServerPose().Mm, "a stop does nothing on the wall");
            Assert.AreEqual(1, rig.ServerMove().Wall);
            rig.Sources[0].Click(new int3(5600, 3000, 2000));
            rig.Tick(60);
            Assert.Greater(planner.OnWallPlans, 0, "the plain click was planned as the wall's");
            Assert.AreEqual(new int3(FaceX, 3000, 2000), rig.ServerPose().Mm, "along the wall to it (server)");
            Assert.AreEqual(new int3(FaceX, 3000, 2000), rig.OwnerPose().Mm, "and the owner");
            Assert.AreEqual(1, rig.OwnerMove().Wall);
        }

        [Test]
        public void OutOfGrip_TheServerPushesThePlayerOff_ToTheGroundInFrontOfTheWall()
        {
            var planner = new StubWallPlanner();
            var grip = new StubGrip { Points = 20 };
            using var rig = Rig(0, planner, grip);
            rig.Sources[0].Wall(new int3(6000, 6000, 0), Wall);
            rig.Tick(240);
            var system = rig.Server.GetExistingSystemManaged<WallGripSystem>();
            Assert.AreEqual(1, system.PushOffs, "pushed off once, when the 21st point could not be paid");
            Assert.AreEqual(20, system.Paid, "every point it had");
            Assert.AreEqual(Landing, rig.ServerPose().Mm, "down in front of the wall (server)");
            Assert.AreEqual(Landing, rig.OwnerPose().Mm, "down in front of the wall (owner)");
            Assert.AreEqual(0, rig.ServerMove().Wall, "off the wall");
            Assert.AreEqual(0, rig.OwnerMove().Wall);
            Assert.IsFalse(rig.Get<PoseTilt>(rig.Server, rig.ServerGhosts[0]).Tilted, "upright again");
            Assert.IsFalse(rig.Get<PoseTilt>(rig.Clients[0], rig.GhostIn(0, 0)).Tilted);
        }

        [TestCase(100, true)]
        [TestCase(5, false)]
        public void AStraightClimb_PaysItsFluxAsThePlantStarts_OrStopsAtTheFoot(int flux, bool climbs)
        {
            var planner = new StubWallPlanner();
            var grip = new StubGrip { Points = flux };
            using var rig = Rig(0, planner, grip);
            rig.Sources[0].Wall(new int3(77777, 3000, 0), Wall);
            var system = rig.Server.GetExistingSystemManaged<WallGripSystem>();
            bool paidBeforeFoot = false;
            for (int i = 0; i < 150; i++)
            {
                rig.Tick();
                if (system.ClimbsPaid > 0 && rig.ServerPose().XMm < 4900) paidBeforeFoot = true;
            }
            Assert.IsFalse(paidBeforeFoot, "nothing is paid on the run to the foot (NGO pays as the climb begins)");
            if (climbs)
            {
                Assert.AreEqual(1, system.ClimbsPaid, "paid once");
                Assert.AreEqual(100 - 12, grip.Points, "NGO's 12 Flux");
                Assert.AreEqual(new int3(6500, 5000, 0), rig.ServerPose().Mm, "up and over the top (server)");
                Assert.AreEqual(new int3(6500, 5000, 0), rig.OwnerPose().Mm, "and the owner");
                Assert.AreEqual(0, rig.ServerMove().Wall, "a straight climb takes no Grip");
                Assert.AreEqual(0, system.Paid, "no Grip was paid");
            }
            else
            {
                Assert.AreEqual(1, system.ClimbsRefused, "Not enough Flux: the climb does not begin");
                Assert.AreEqual(5, grip.Points, "nothing taken");
                Assert.Less(rig.ServerPose().YMm, 800, "stays at the foot (server)");
                Assert.Less(rig.OwnerPose().YMm, 800, "and the owner, corrected by the server");
            }
        }

        /// <summary>
        /// Part 4 (`m3c-wallstride-design.md` section 5): a Jump click at a wall the graph does not cover flies the jump to the catch point and,
        /// with no landing, the climb of the same number runs on from there on the touchdown tick, on the server and the owner alike; NGO's 12
        /// Flux are paid as the plant starts, or, short of them (spent while it flew: NGO's Decide already refuses a click without them),
        /// "Could not hold on. Returned to takeoff." (back on the take-off point).
        /// </summary>
        [TestCase(0, true)]
        [TestCase(100, true)]
        [TestCase(0, false)]
        [TestCase(100, false)]
        public void AJumpAtAWall_IsCaught_AndTheClimbRunsOnFromTheCatch_OrItReturnsToTakeoff(int delayMs, bool enoughFlux)
        {
            var planner = new StubWallPlanner();
            var grip = new StubGrip { Points = enoughFlux ? 100 : 5 };
            using var rig = Rig(delayMs, planner, grip);
            var jumps = rig.Server.GetExistingSystemManaged<PlayerJumpServerSystem>();
            jumps.Planner = new NoJumpPlanner();
            jumps.WallPlanner = planner;
            rig.Clients[0].GetExistingSystemManaged<PlayerInputGatherSystem>().JumpPlanner = new NoJumpPlanner();
            var curves = PlayerMotionCurves.Default;
            int tickMicros = PlayerMovement.TickMicros(30);
            var start = rig.ServerPose().Mm;

            rig.Sources[0].Wall(new int3(CatchClick, 3000, 0), Wall, run: false, jump: true);
            uint serverHandOff = 0, ownerHandOff = 0, serverStart = 0, ownerStart = 0;
            int highest = int.MinValue;
            int3 serverAtHandOff = default;
            long elapsedAtHandOff = -1;
            for (int i = 0; i < 200; i++)
            {
                rig.Tick();
                var em = rig.Server.EntityManager;
                var plan = em.GetComponentData<MotionPlan>(rig.ServerGhosts[0]);
                var move = rig.ServerMove();
                if (serverHandOff == 0 && JumpFlight.IsCatch(in plan) && move.OrderSeq == plan.PlanSeq)
                {
                    serverHandOff = rig.ServerTick;
                    serverAtHandOff = rig.ServerPose().Mm;
                    elapsedAtHandOff = JumpFlight.ElapsedMicros(in plan, KnTestWorld.Singleton<NetworkTime>(rig.Server).ServerTick, tickMicros);
                }
                if (serverHandOff == 0) highest = math.max(highest, rig.ServerPose().YMm);
                var client = rig.Clients[0];
                var ownPlan = PlayerMotionSystem.Choose(client.EntityManager.GetComponentData<MotionPlan>(rig.GhostIn(0, 0)),
                    client.EntityManager.GetComponentData<ClientMotionPlan>(rig.GhostIn(0, 0)).Plan, rig.OwnerMove().RefusedSeq);
                if (ownerHandOff == 0 && JumpFlight.IsCatch(in ownPlan) && rig.OwnerMove().OrderSeq == ownPlan.PlanSeq)
                {
                    // the owner's state is its last FULL tick: on a partial tick that is the one before the clock's
                    var ownNt = KnTestWorld.Singleton<NetworkTime>(client);
                    NetworkTick full = ownNt.ServerTick;
                    if (ownNt.ServerTickFraction > 0f && ownNt.ServerTickFraction < 1f) full.Decrement();
                    ownerHandOff = full.TickIndexForValidTick;
                    ownerStart = ownPlan.StartTick.TickIndexForValidTick;
                }
                if (serverStart == 0 && JumpFlight.IsCatch(in plan)) serverStart = plan.StartTick.TickIndexForValidTick;
            }
            var system = rig.Server.GetExistingSystemManaged<WallGripSystem>();
            Assert.AreEqual(1, jumps.Catches, "the server planned the click as a catch");
            Assert.AreNotEqual(0u, serverHandOff, "the climb ran on from the catch (server)");
            Assert.AreEqual(CatchAt, serverAtHandOff, "handed off on the catch point, no landing");
            Assert.GreaterOrEqual(elapsedAtHandOff, curves.LandMicros, "at the jump's touchdown time");
            Assert.Less(elapsedAtHandOff, curves.LandMicros + tickMicros, "on the touchdown tick");
            Assert.Greater(highest, CatchAt.y, "the jump's arc rose over the catch point first");
            Assert.AreEqual(serverStart, ownerStart, "the owner flew the same jump from the same press tick");
            Assert.AreEqual(serverHandOff, ownerHandOff, "the owner handed off on the same tick (predicted from the press)");
            if (enoughFlux)
            {
                Assert.AreEqual(1, system.ClimbsPaid, "paid once, as the plant starts (NGO BeginCaught)");
                Assert.AreEqual(100 - 12, grip.Points, "NGO's 12 Flux");
                Assert.AreEqual(new int3(6500, 5000, 0), rig.ServerPose().Mm, "up and over the top (server)");
                Assert.AreEqual(new int3(6500, 5000, 0), rig.OwnerPose().Mm, "and the owner");
            }
            else
            {
                Assert.AreEqual(1, system.ClimbsRefused, "Could not hold on");
                Assert.AreEqual(5, grip.Points, "nothing taken");
                Assert.AreEqual(start, rig.ServerPose().Mm, "Returned to takeoff (server)");
                Assert.AreEqual(start, rig.OwnerPose().Mm, "and the owner");
            }
        }

        [Test]
        public void DuringThePlant_AClickStartsNothing_AndARefusedWallClickIsNotSent()
        {
            var planner = new StubWallPlanner();
            var grip = new StubGrip { Points = 1000 };
            using var rig = Rig(0, planner, grip);
            rig.Sources[0].Wall(new int3(99999, 6000, 0), Wall);
            rig.Tick(10);
            Assert.AreEqual(1, planner.Refusals, "the owner's own rules refused it");
            Assert.AreEqual(0, rig.ServerMove().OrderSeq, "and nothing was sent");

            rig.Sources[0].Wall(new int3(6000, 6000, 0), Wall);
            bool clicked = false;
            for (int i = 0; i < 200; i++)
            {
                rig.Tick();
                if (!clicked && PlayerServerPlanSystem.InWallLeg(rig.Server.EntityManager, rig.ServerGhosts[0]))
                {
                    clicked = true;
                    rig.Sources[0].Click(new int3(-4000, 0, 0));   // a click mid-plant
                }
            }
            Assert.IsTrue(clicked, "the plant was seen");
            Assert.AreEqual(new int3(FaceX, 6000, 0), rig.ServerPose().Mm, "the wall walk went on to its end (server)");
            Assert.AreEqual(new int3(FaceX, 6000, 0), rig.OwnerPose().Mm, "and the owner");
            Assert.AreEqual(1, rig.ServerMove().Wall);
        }
    }
}
