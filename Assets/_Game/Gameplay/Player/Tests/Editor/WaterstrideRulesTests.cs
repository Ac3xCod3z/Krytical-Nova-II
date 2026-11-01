using System.Collections.Generic;
using KryticalNova.Player;
using KryticalNova.World;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Tests
{
    /// <summary>
    /// Waterstride's rules (the NGO motor's, lifted for the entity netcode; `m3c-waterstride-design.md` section 1): which clicks run,
    /// why one is refused, in NGO's words, and the way it runs. A lake east of x = 0 on a handmade map; land west of it is the shore.
    /// </summary>
    public sealed class WaterstrideRulesTests
    {
        private const float Level = -1.2f;
        private WaterstrideSettings settings;
        private WaterMap map;
        private readonly List<Vector3> route = new List<Vector3>();

        /// <summary>The shore: land is x below 0 (and the ground is at 0); a point on the water finds the shore within its radius.</summary>
        private sealed class WestShore : WaterstrideRules.IShore
        {
            public bool Refuse;

            public bool Sample(Vector3 point, float radius, out Vector3 found)
            {
                found = point;
                if (Refuse) return false;
                if (point.x < 0f) { found = new Vector3(point.x, 0f, point.z); return true; }
                if (point.x - radius < 0f) { found = new Vector3(-0.01f, 0f, point.z); return true; }
                return false;
            }

            public bool PathComplete(Vector3 from, Vector3 to) => !Refuse;
        }

        [SetUp]
        public void SetUp()
        {
            settings = ScriptableObject.CreateInstance<WaterstrideSettings>();
            map = ScriptableObject.CreateInstance<WaterMap>();
            map.waterLevel = Level;
            map.origin = new Vector2(-20f, -20f);
            map.cellSize = 1f;
            map.width = map.depth = 40;
            var cells = new bool[40 * 40];
            for (int z = 0; z < 40; z++)
            for (int x = 20; x < 40; x++) cells[z * 40 + x] = true;   // x from 0 to 20 m is water
            map.Set(cells, "test lake");
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(settings);
            Object.DestroyImmediate(map);
        }

        // Open, alive, Run selected, Flux for a second, on walkable ground, on land.
        private static WaterstrideRules.Ask Ready() => new WaterstrideRules.Ask
        {
            Open = true, Alive = true, Running = true, HasFluxForASecond = true, OnWalkableGround = true,
        };

        private string FromLand(WaterstrideRules.Ask ask, Vector3 feet, Vector3 click, out WaterstrideRules.Way way) =>
            WaterstrideRules.PlanWaterClick(map, settings, ask, new WestShore(), feet, click, route, out way);

        [Test]
        public void ItOpensAtLevel60_WithRunAndJumpAtRank20()
        {
            Assert.IsTrue(WaterstrideRules.IsOpen(settings, 60, 20, 20));
            Assert.IsFalse(WaterstrideRules.IsOpen(settings, 59, 20, 20));
            Assert.IsFalse(WaterstrideRules.IsOpen(settings, 60, 19, 20));
            Assert.IsFalse(WaterstrideRules.IsOpen(settings, 60, 20, 19));
            Assert.IsFalse(WaterstrideRules.IsOpen(null, 99, 20, 20));
        }

        [Test]
        public void TheRefusals_AreNgosWords_InNgosOrder()
        {
            var click = new Vector3(10f, Level, 0f);
            var feet = new Vector3(-5f, 0f, 0f);
            var a = Ready(); a.Open = false; a.Alive = false;
            Assert.AreEqual("Waterstride opens at level 60 with Run and Jump at rank 20.", FromLand(a, feet, click, out _));
            a = Ready(); a.Alive = false; a.OtherMoveBusy = true;
            Assert.AreEqual("No HP remaining.", FromLand(a, feet, click, out _));
            a = Ready(); a.OtherMoveBusy = true; a.Running = false;
            Assert.AreEqual("Finish this move first.", FromLand(a, feet, click, out _));
            a = Ready(); a.Running = false; a.HasFluxForASecond = false;
            Assert.AreEqual("Select Run to run on water.", FromLand(a, feet, click, out _));
            a = Ready(); a.HasFluxForASecond = false;
            Assert.AreEqual("Not enough Flux to run on water.", FromLand(a, feet, click, out _));
            a = Ready(); a.OnWater = true; a.Stranded = true;
            Assert.AreEqual("Out of Flux: back to the shore first.", FromLand(a, feet, click, out _));
            a = Ready(); a.OnWalkableGround = false;
            Assert.AreEqual("Waterstride must start on walkable ground.", FromLand(a, feet, click, out _));
            Assert.AreEqual("That water cannot be run on.", FromLand(Ready(), feet, new Vector3(-10f, Level, 0f), out _));
            Assert.AreEqual("There is no water to run on here.",
                WaterstrideRules.PlanWaterClick(null, settings, Ready(), new WestShore(), feet, click, route, out _));
        }

        [Test]
        public void OnTheWater_Run_IsNotNeeded()
        {
            var a = Ready(); a.Running = false; a.OnWater = true;
            Assert.IsNull(WaterstrideRules.PlanWaterClick(map, settings, a, new WestShore(), new Vector3(5f, Level, 0f), new Vector3(15f, Level, 10f), route, out _));
        }

        [Test]
        public void FromLand_ItWalksToTheShoreOnTheLine_ThenRunsTheRouteToTheClick()
        {
            Assert.IsNull(FromLand(Ready(), new Vector3(-5f, 0f, 0f), new Vector3(10f, Level, 0f), out var way));
            Assert.IsTrue(way.FromLand);
            Assert.IsFalse(way.StartsNow, "4.5 m from the shore: it walks there first");
            Assert.AreEqual(-0.5f, way.ShoreFeet.x, 1e-4f, "the last land sample (every 0.5 m) on the line");
            Assert.AreEqual(2, route.Count, "a straight line over the water is one leg");
            Assert.AreEqual(new Vector3(0.5f, Level, 0.5f), route[0], "from the water cell nearest the first water sample");
            Assert.AreEqual(new Vector3(10.5f, Level, 0.5f), route[1], "to the clicked cell's centre");
        }

        [Test]
        public void FromLand_WithinPoint7m_OfTheShore_ItRunsOutAtOnce()
        {
            Assert.IsNull(FromLand(Ready(), new Vector3(-0.6f, 0f, 0f), new Vector3(10f, Level, 0f), out var way));
            Assert.IsTrue(way.FromLand && way.StartsNow);
        }

        [Test]
        public void FromLand_WithNoShoreOrNoWayToIt_ItIsRefused()
        {
            Assert.AreEqual("There is no shore on the way to that water.", FromLand(Ready(), new Vector3(1f, 0f, 0f), new Vector3(10f, Level, 0f), out _),
                "the feet already over water: no land before it on the line");
            Assert.AreEqual("There is no shore to run out from there.",
                WaterstrideRules.PlanWaterClick(map, settings, Ready(), new WestShore { Refuse = true }, new Vector3(-5f, 0f, 0f), new Vector3(10f, Level, 0f), route, out _));
            Assert.AreEqual(0, route.Count);
        }

        [Test]
        public void OnTheWater_AWaterClick_RunsFromTheFeet_WithNoShore()
        {
            var a = Ready(); a.OnWater = true;
            Assert.IsNull(WaterstrideRules.PlanWaterClick(map, settings, a, new WestShore(), new Vector3(5f, Level, 0f), new Vector3(15f, Level, 10f), route, out var way));
            Assert.IsFalse(way.FromLand || way.ToLand);
            Assert.AreEqual(new Vector3(5.5f, Level, 0.5f), route[0]);
            Assert.AreEqual(new Vector3(15.5f, Level, 10.5f), route[route.Count - 1]);
        }

        [Test]
        public void OnTheWater_ALandClick_RunsToTheEdgeOnTheLineBack_StepsAshore_AndWalksOn()
        {
            var a = Ready(); a.OnWater = true;
            Assert.IsNull(WaterstrideRules.PlanLandClick(map, settings, a, new WestShore(), new Vector3(10f, Level, 0f), new Vector3(-5f, 0f, 0f), route, out var way));
            Assert.IsTrue(way.ToLand);
            Assert.AreEqual(-0.5f, way.LandFeet.x, 1e-4f, "ashore where the line from the place back meets the water");
            Assert.AreEqual(new Vector3(-5f, 0f, 0f), way.WalkOnTo);
            Assert.AreEqual(new Vector3(0.5f, Level, 0.5f), route[route.Count - 1], "the water cell nearest the edge");
        }

        [Test]
        public void ALandClick_IsOnlyWaterstridesOnTheWater_AndNeedsWalkableGround()
        {
            Assert.AreEqual(string.Empty, WaterstrideRules.PlanLandClick(map, settings, Ready(), new WestShore(), new Vector3(-3f, 0f, 0f), new Vector3(-5f, 0f, 0f), route, out _),
                "on land a land click is the ordinary walk's");
            var a = Ready(); a.OnWater = true;
            Assert.AreEqual("Choose a walkable ground or platform surface.",
                WaterstrideRules.PlanLandClick(map, settings, a, new WestShore { Refuse = true }, new Vector3(10f, Level, 0f), new Vector3(-5f, 0f, 0f), route, out _));
            a.Stranded = true;
            Assert.AreEqual("Out of Flux: back to the shore first.",
                WaterstrideRules.PlanLandClick(map, settings, a, new WestShore(), new Vector3(10f, Level, 0f), new Vector3(-5f, 0f, 0f), route, out _));
        }

        [Test]
        public void OutOfFlux_TheWayBack_IsToTheNearestShore()
        {
            Assert.IsTrue(WaterstrideRules.PlanStranded(map, settings, new WestShore(), new Vector3(3f, Level, 0.3f), route, out Vector3 land));
            Assert.AreEqual(-0.01f, land.x, 1e-4f);
            Assert.AreEqual(new Vector3(0.5f, Level, 0.5f), route[route.Count - 1]);
            Assert.IsFalse(WaterstrideRules.PlanStranded(map, settings, new WestShore { Refuse = true }, new Vector3(3f, Level, 0f), route, out _),
                "no shore within 80 m: the run is called off where it is");
        }

        [Test]
        public void AJumpClick_ComesDownOnTheWater_WithinPoint3mOfItsHeight()
        {
            Assert.IsTrue(WaterstrideRules.WaterLanding(map, new Vector3(5f, -1.0f, 0f), out Vector3 surface));
            Assert.AreEqual(Level, surface.y, 1e-5f);
            Assert.IsFalse(WaterstrideRules.WaterLanding(map, new Vector3(5f, -0.8f, 0f), out _), "higher than 0.3 m over the water: not the water");
            Assert.IsFalse(WaterstrideRules.WaterLanding(map, new Vector3(-5f, Level, 0f), out _), "land");
        }

        [Test]
        public void AJumpOntoWater_Stands_WhenOpenWithFlux_ElseFallsIn()
        {
            Assert.IsTrue(WaterstrideRules.StandsOnLanding(settings, true, true, true));
            Assert.IsFalse(WaterstrideRules.StandsOnLanding(settings, false, true, true));
            Assert.IsFalse(WaterstrideRules.StandsOnLanding(settings, true, true, false));
            Assert.IsFalse(WaterstrideRules.StandsOnLanding(settings, true, false, true));
            settings.fluxPerSecond = 0f;
            Assert.IsTrue(WaterstrideRules.StandsOnLanding(settings, true, true, false), "free when it costs nothing");
        }

        [Test]
        public void FallingIn_Costs15PercentOfMaxHp_NeverTheLastPoint()
        {
            Assert.AreEqual(150f, WaterstrideRules.FallInLoss(settings, 1000f, 1000f, true), 1e-3f);
            Assert.AreEqual(99f, WaterstrideRules.FallInLoss(settings, 1000f, 100f, true), 1e-3f);
            Assert.AreEqual(0f, WaterstrideRules.FallInLoss(settings, 1000f, 1f, true), 1e-3f);
            Assert.AreEqual(0f, WaterstrideRules.FallInLoss(settings, 1000f, 500f, false), 1e-3f);
            Assert.AreEqual("You fell in the water and climbed out on the shore (-150 HP).", WaterstrideRules.FallInLine(150f));
            Assert.AreEqual("You fell in the water and climbed out on the shore.", WaterstrideRules.FallInLine(0f));
        }

        [Test]
        public void TheSpeed_IsTheGroundSpeed_AtLeast1_AndStrandedIs6()
        {
            Assert.AreEqual(9f, WaterstrideRules.Speed(settings, 9f, false), 1e-5f);
            Assert.AreEqual(1f, WaterstrideRules.Speed(settings, 0.5f, false), 1e-5f);
            Assert.AreEqual(6f, WaterstrideRules.Speed(settings, 9f, true), 1e-5f);
        }

        [Test]
        public void TheClickRay_MeetsTheWater_OnlyBeforeItsFirstHit()
        {
            var ray = new Ray(new Vector3(5f, 10f, 0f), Vector3.down);
            Assert.IsTrue(WaterstrideRules.WaterUnderRay(map, ray, 12f, out Vector3 at));
            Assert.AreEqual(Level, at.y, 1e-4f);
            Assert.IsFalse(WaterstrideRules.WaterUnderRay(map, ray, 5f, out _), "something stands above the water");
            Assert.IsFalse(WaterstrideRules.WaterUnderRay(map, new Ray(new Vector3(-5f, 10f, 0f), Vector3.down), 30f, out _), "over land");
        }
    }
}
