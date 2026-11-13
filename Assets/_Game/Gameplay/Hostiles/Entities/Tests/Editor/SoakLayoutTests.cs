using NUnit.Framework;
using Unity.Mathematics;

namespace KryticalNova.Hostiles.Entities.Tests
{
    /// <summary>Wave 4: the brain soak's layout, shared by the EditMode soak and the test-lane server's -knbrainsoak.</summary>
    public class SoakLayoutTests
    {
        [Test]
        public void TwoThousand_AreTheOldGrid_HalfAggressive()
        {
            var points = SoakLayout.Build(new float2(110f, 110f), 2000);
            Assert.AreEqual(2000, points.Count);
            for (int i = 0; i < points.Count; i++)
            {
                Assert.AreEqual((i % 45) * 5f, points[i].Position.x, 1e-4f, "column " + i);
                Assert.AreEqual((i / 45) * 5f, points[i].Position.z, 1e-4f, "row " + i);
                Assert.AreEqual(i % 2 == 0, points[i].Aggressive);
            }
        }

        [Test]
        public void TheSquare_IsCentredOnTheSpot_AndSpeciesTakeTurns()
        {
            var points = SoakLayout.Build(new float2(500f, -40f), 2025, 5f, 3);
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            foreach (var p in points)
            {
                minX = math.min(minX, p.Position.x); maxX = math.max(maxX, p.Position.x);
                minZ = math.min(minZ, p.Position.z); maxZ = math.max(maxZ, p.Position.z);
            }
            Assert.AreEqual(500f, (minX + maxX) * 0.5f, 1e-3f);
            Assert.AreEqual(-40f, (minZ + maxZ) * 0.5f, 1e-3f);
            Assert.AreEqual(220f, maxX - minX, 1e-3f);
            Assert.AreEqual(0, points[0].Species);
            Assert.AreEqual(1, points[1].Species);
            Assert.AreEqual(0, points[3].Species);
        }

        [Test]
        public void NothingAsked_NothingMade()
        {
            Assert.AreEqual(0, SoakLayout.Build(float2.zero, 0).Count);
            int aggressive = 0;
            foreach (var p in SoakLayout.Build(float2.zero, 100, 5f, 1, 0.25f)) if (p.Aggressive) aggressive++;
            Assert.AreEqual(25, aggressive);
        }
    }
}
