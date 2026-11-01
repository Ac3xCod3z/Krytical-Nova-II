using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace KryticalNova.Player.Entities.Tests
{
    /// <summary>
    /// Waterstride in the server and owner worlds (M3c, `m3c-waterstride-design.md`): both plan the same run onto the water and stand on
    /// it, the server pays the Flux each second while on it, and out of Flux it carries the player back to the shore at the stranded
    /// speed, refusing clicks on the way (NGO).
    /// </summary>
    public class WaterWorldTests
    {
        const int Level = -1200;

        /// <summary>Water east of x = 3 m at -1.2 m; the shore at x = 2 m (stands in for the water map, the NavMesh and NGO's rules).</summary>
        sealed class StubWaterPlanner : IWaterPlanner
        {
            public int Refusals;

            public WaterAnswer Plan(Entity ghost, int3 startMm, int3 goalMm, bool run, bool onWater, bool stranded, NativeList<int3> corners,
                out WaterPlan leg, out string reason)
            {
                leg = default;
                reason = null;
                bool waterGoal = goalMm.y == Level && goalMm.x > 3000;
                if (!onWater && !(waterGoal && run)) return WaterAnswer.NotWater;
                if (stranded) { Refusals++; reason = "Out of Flux: back to the shore first."; return WaterAnswer.Refused; }
                corners.Add(startMm);
                if (!onWater) corners.Add(new int3(3000, 0, startMm.z));
                corners.Add(goalMm);
                leg = new WaterPlan
                {
                    LegStartMm = onWater ? 0 : PlayerPath.DistanceMm(startMm, new int3(3000, 0, startMm.z)), SpeedMmps = 9000,
                    TurnCdegPerSecond = 72000, Flags = WaterFlags.EndsOnWater,
                };
                return WaterAnswer.Planned;
            }

            public bool PlanStranded(Entity ghost, int3 startMm, NativeList<int3> corners, out WaterPlan leg)
            {
                corners.Add(startMm);
                corners.Add(new int3(3000, Level, startMm.z));
                corners.Add(new int3(2000, 0, startMm.z));
                leg = new WaterPlan
                {
                    LegStartMm = 0, LegEndMm = PlayerPath.DistanceMm(startMm, new int3(3000, Level, startMm.z)), SpeedMmps = 6000,
                    TurnCdegPerSecond = 72000, Flags = WaterFlags.Stranded,
                };
                return true;
            }
        }

        /// <summary>A purse of whole Flux points.</summary>
        sealed class StubFlux : IWaterFlux
        {
            public int Points;
            public int FluxPerSecondMilli => 3000;

            public bool TryPay(Entity ghost, int points)
            {
                if (Points < points) return false;
                Points -= points;
                return true;
            }
        }

        static PlayerRig Rig(int delayMs, StubWaterPlanner planner, StubFlux flux)
        {
            var rig = PlayerRig.Create(1, delayMs);
            rig.Server.GetExistingSystemManaged<PlayerServerPlanSystem>().WaterPlanner = planner;
            var system = rig.Server.GetExistingSystemManaged<WaterstrideFluxSystem>();
            system.Planner = planner;
            system.Flux = flux;
            rig.Clients[0].GetExistingSystemManaged<PlayerInputGatherSystem>().WaterPlanner = planner;
            return rig;
        }

        [TestCase(0)]
        [TestCase(100)]
        public void TheServerAndTheOwner_RunTheSameLegOut_AndStandOnTheWater_PayingEachSecond(int delayMs)
        {
            var planner = new StubWaterPlanner();
            var flux = new StubFlux { Points = 1000 };
            using var rig = Rig(delayMs, planner, flux);
            var goal = new int3(9000, Level, 0);
            rig.Sources[0].Click(goal, run: true);
            rig.Tick(90);   // 3 s: ~1 s to the shore and over the water, then standing
            var server = rig.Get<WaterPlan>(rig.Server, rig.ServerGhosts[0]);
            var own = rig.Get<ClientWaterPlan>(rig.Clients[0], rig.GhostIn(0, 0)).Plan;
            Assert.AreNotEqual(0, server.Seq, "the server planned the run for the click");
            Assert.AreEqual(server.Seq, own.Seq, "the owner's own leg belongs to the same click");
            Assert.AreEqual(server.LegStartMm, own.LegStartMm, "the same run-out along the path");
            Assert.AreEqual(goal, rig.ServerPose().Mm, "the server's body stands where clicked, on the water");
            Assert.AreEqual(goal, rig.OwnerPose().Mm, "the owner's too");
            Assert.AreEqual(1, rig.ServerMove().Water, "on the water (server)");
            Assert.AreEqual(1, rig.OwnerMove().Water, "on the water (owner)");
            var system = rig.Server.GetExistingSystemManaged<WaterstrideFluxSystem>();
            Assert.That(system.Paid, Is.InRange(5, 8), "3 Flux a second for the 2-2.5 s on the water, whole points");
            Assert.AreEqual(0, system.Strandings);
        }

        [Test]
        public void OutOfFlux_TheServerCarriesThePlayerBackToTheShore_RefusingClicksOnTheWay()
        {
            var planner = new StubWaterPlanner();
            var flux = new StubFlux { Points = 3 };
            using var rig = Rig(0, planner, flux);
            rig.Sources[0].Click(new int3(15000, Level, 0), run: true);
            var system = rig.Server.GetExistingSystemManaged<WaterstrideFluxSystem>();
            bool clicked = false;
            for (int i = 0; i < 300; i++)
            {
                rig.Tick();
                if (!clicked && system.Strandings > 0 && WaterLeg.Stranded(rig.Get<WaterPlan>(rig.Server, rig.ServerGhosts[0]), rig.ServerMove().OrderSeq))
                {
                    clicked = true;
                    rig.Sources[0].Click(new int3(15000, Level, 9000), run: true);   // a click on the way back
                }
            }
            Assert.AreEqual(1, system.Strandings, "stranded once, when the 4th point could not be paid");
            Assert.AreEqual(3, system.Paid, "every point it had");
            Assert.IsTrue(clicked);
            Assert.Greater(planner.Refusals, 0, "the owner's click on the way back was refused by the rules");
            Assert.AreEqual(new int3(2000, 0, 0), rig.ServerPose().Mm, "back ashore (server)");
            Assert.AreEqual(new int3(2000, 0, 0), rig.OwnerPose().Mm, "back ashore (owner)");
            Assert.AreEqual(0, rig.ServerMove().Water, "off the water");
            Assert.AreEqual(0, rig.OwnerMove().Water);
        }
    }
}
