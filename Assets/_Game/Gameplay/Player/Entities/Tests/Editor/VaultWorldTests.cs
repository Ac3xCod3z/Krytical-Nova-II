using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;

namespace KryticalNova.Player.Entities.Tests
{
    /// <summary>
    /// The vault in the server and owner worlds (M3c, `m3c-vault-design.md`): both splice the same vault leg into the walk, both bodies
    /// rise over it on NGO's curve and land on the destination, and a click during the vault is refused (NGO ignores it).
    /// </summary>
    public class VaultWorldTests
    {
        /// <summary>An obstacle across x = 3..5 m for any walk heading past it along +x (stands in for the NavMesh and PhysX rules).</summary>
        sealed class StubVaultPlanner : IVaultPlanner
        {
            public int Calls;
            public bool TryPlan(Entity ghost, int3 startMm, int3 goalMm, int walkLengthMm, int speedMmps, out VaultSplice vault)
            {
                Calls++;
                vault = default;
                if (startMm.x >= 3000 || goalMm.x <= 5000) return false;
                vault = new VaultSplice
                {
                    TakeoffMm = new int3(3000, startMm.y, startMm.z), LandingMm = new int3(5000, startMm.y, startMm.z),
                    AirMicros = 500000, RaiseMm = 900, RiseEndsPermille = 250, FallStartsPermille = 750, Style = 1,
                };
                return true;
            }
        }

        static PlayerRig Rig(int delayMs, StubVaultPlanner planner)
        {
            var rig = PlayerRig.Create(1, delayMs);
            rig.Server.GetExistingSystemManaged<PlayerServerPlanSystem>().VaultPlanner = planner;
            rig.Clients[0].GetExistingSystemManaged<PlayerInputGatherSystem>().VaultPlanner = planner;
            return rig;
        }

        [TestCase(0)]
        [TestCase(100)]
        public void TheServerAndTheOwner_VaultTheSameLeg_RiseOverIt_AndLandOnTheDestination(int delayMs)
        {
            var planner = new StubVaultPlanner();
            using var rig = Rig(delayMs, planner);
            var goal = new int3(9000, 0, 0);
            rig.Sources[0].Click(goal);
            int serverTop = 0, ownerTop = 0;
            for (int i = 0; i < 120; i++)
            {
                rig.Tick();
                serverTop = math.max(serverTop, rig.ServerPose().YMm);
                ownerTop = math.max(ownerTop, rig.OwnerPose().YMm);
            }
            var server = rig.Get<VaultPlan>(rig.Server, rig.ServerGhosts[0]);
            var own = rig.Get<ClientVaultPlan>(rig.Clients[0], rig.GhostIn(0, 0)).Plan;
            Assert.AreNotEqual(0, server.Seq, "the server planned a vault for the click");
            Assert.AreEqual(server.Seq, own.Seq, "the owner's own vault belongs to the same click");
            Assert.AreEqual(server.LegStartMm, own.LegStartMm, "the same take-off along the path");
            Assert.AreEqual(server.LegLengthMm, own.LegLengthMm);
            Assert.AreEqual(2000, server.LegLengthMm);
            Assert.AreEqual(900, serverTop, 40, "the server's body rose over the obstacle");
            Assert.AreEqual(900, ownerTop, 40, "the owner's too");
            Assert.AreEqual(goal, rig.ServerPose().Mm, "the server lands and walks on to the destination");
            Assert.AreEqual(goal, rig.OwnerPose().Mm, "the owner too");
        }

        /// <summary>Two fences across the +x walk: x 3..5 m and x 8..10 m; the first one ahead of the start is vaulted.</summary>
        sealed class TwoFences : IVaultPlanner
        {
            public bool TryPlan(Entity ghost, int3 startMm, int3 goalMm, int walkLengthMm, int speedMmps, out VaultSplice vault)
            {
                vault = default;
                int takeoff = startMm.x < 3000 ? 3000 : startMm.x < 8000 ? 8000 : -1;
                if (takeoff < 0 || goalMm.x <= takeoff + 2000) return false;
                vault = new VaultSplice
                {
                    TakeoffMm = new int3(takeoff, startMm.y, startMm.z), LandingMm = new int3(takeoff + 2000, startMm.y, startMm.z),
                    AirMicros = 500000, RaiseMm = 900, RiseEndsPermille = 250, FallStartsPermille = 750, Style = 1,
                };
                return true;
            }
        }

        [TestCase(0)]
        [TestCase(100)]
        public void TwoFencesInARow_AreBothVaulted_TheSecondPlannedAtTheFirstLanding_OnTheServerAndTheOwner(int delayMs)
        {
            using var rig = PlayerRig.Create(1, delayMs);
            var fences = new TwoFences();
            var server = rig.Server.GetExistingSystemManaged<PlayerServerPlanSystem>();
            server.VaultPlanner = fences;
            rig.Clients[0].GetExistingSystemManaged<PlayerInputGatherSystem>().VaultPlanner = fences;
            var goal = new int3(13000, 0, 0);
            rig.Sources[0].Click(goal);
            int serverRises = 0, ownerRises = 0;
            bool serverUp = false, ownerUp = false;
            for (int i = 0; i < 200; i++)
            {
                rig.Tick();
                bool s = rig.ServerPose().YMm > 450, o = rig.OwnerPose().YMm > 450;
                if (s && !serverUp) serverRises++;
                if (o && !ownerUp) ownerRises++;
                serverUp = s; ownerUp = o;
            }
            Assert.AreEqual(1, server.ChainedVaults, "the second fence was planned from the first one's landing");
            Assert.AreEqual(2, serverRises, "the server's body went over both fences");
            Assert.AreEqual(2, ownerRises, "the owner's too (it predicts the server's order like a click)");
            Assert.AreEqual(goal, rig.ServerPose().Mm, "and walked on to the place clicked");
            Assert.AreEqual(goal, rig.OwnerPose().Mm);
        }

        [Test]
        public void AClickDuringTheVault_IsRefused_AndTheWalkGoesOnToItsDestination()
        {
            var planner = new StubVaultPlanner();
            using var rig = Rig(0, planner);
            var goal = new int3(9000, 0, 0);
            rig.Sources[0].Click(goal);
            bool clicked = false;
            for (int i = 0; i < 120; i++)
            {
                rig.Tick();
                var m = rig.Get<MoveState>(rig.Server, rig.ServerGhosts[0]);
                var v = rig.Get<VaultPlan>(rig.Server, rig.ServerGhosts[0]);
                if (!clicked && VaultLeg.On(v, m.OrderSeq, m.ProgressMm))
                {
                    clicked = true;
                    rig.Sources[0].Click(new int3(-5000, 0, 0));   // back the other way, mid-vault
                }
            }
            Assert.IsTrue(clicked, "the walk went over the leg");
            Assert.AreEqual(goal, rig.ServerPose().Mm, "NGO: the click mid-vault is ignored, the walk ends on its own destination");
            Assert.AreEqual(goal, rig.OwnerPose().Mm);
        }
    }
}
