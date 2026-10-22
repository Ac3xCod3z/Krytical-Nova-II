using System.Collections.Generic;
using KryticalNova.World;
using KryticalNova.World.Water;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.World.Tests
{
    /// <summary>Waterstride's map and its ways over the water (owner, 22 Oct 2026).</summary>
    public sealed class WaterRouteTests
    {
        private WaterMap map;

        // A 20 x 20 lake of one-metre cells with an island in the middle (x 8-11, z 6-13).
        [SetUp]
        public void SetUp()
        {
            map = ScriptableObject.CreateInstance<WaterMap>();
            map.width = 20;
            map.depth = 20;
            map.cellSize = 1f;
            map.origin = Vector2.zero;
            map.waterLevel = -1f;
            var water = new bool[400];
            for (int z = 0; z < 20; z++)
            for (int x = 0; x < 20; x++)
                water[z * 20 + x] = !(x >= 8 && x <= 11 && z >= 6 && z <= 13);
            map.Set(water, "test");
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(map);

        [Test]
        public void TheMapKeepsItsCells_ThroughItsPackedForm()
        {
            var read = Object.Instantiate(map); // a copy reads its cells from the packed bytes
            try
            {
                for (int z = 0; z < 20; z++)
                for (int x = 0; x < 20; x++)
                    Assert.AreEqual(map.IsWater(new Vector2Int(x, z)), read.IsWater(new Vector2Int(x, z)), $"cell {x},{z}");
                Assert.AreEqual(400 - 32, read.WaterCells);
            }
            finally { Object.DestroyImmediate(read); }
        }

        [Test]
        public void AStraightWayOverOpenWater_IsOneLine()
        {
            var route = new List<Vector3>();
            Assert.IsTrue(WaterRoute.Find(map, new Vector2Int(1, 1), new Vector2Int(18, 2), 10000, route));
            Assert.AreEqual(2, route.Count);
            Assert.AreEqual(-1f, route[1].y, 1e-4f, "on the water's surface");
        }

        [Test]
        public void AWayPastTheIsland_GoesRoundIt_NeverOverLand()
        {
            var route = new List<Vector3>();
            Assert.IsTrue(WaterRoute.Find(map, new Vector2Int(5, 10), new Vector2Int(15, 10), 10000, route));
            Assert.Greater(route.Count, 2, "it bends round the island");
            for (int i = 1; i < route.Count; i++)
                Assert.IsTrue(WaterRoute.Clear(map, route[i - 1], route[i]), $"leg {i} stays over water");
        }

        [Test]
        public void LandIsNoPlaceToRunTo()
        {
            var route = new List<Vector3>();
            Assert.IsFalse(WaterRoute.Find(map, new Vector2Int(1, 1), new Vector2Int(9, 9), 10000, route));
        }

        [Test]
        public void TheNearestWater_IsFoundFromTheShore()
        {
            Assert.IsTrue(WaterRoute.NearestWater(map, new Vector3(9.5f, 0f, 9.5f), 3f, out Vector2Int cell));
            Assert.IsTrue(map.IsWater(cell));
            Assert.IsFalse(WaterRoute.NearestWater(map, new Vector3(9.5f, 0f, 9.5f), 1f, out _), "the island's middle is two cells from the water");
        }
    }
}
