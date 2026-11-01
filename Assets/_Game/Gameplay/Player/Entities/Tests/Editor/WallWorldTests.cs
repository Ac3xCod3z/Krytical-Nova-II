using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

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
                Plans++;
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
