using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;

namespace KryticalNova.Vegetation.Grass.Entities.Tests
{
    /// <summary>The ECS grass tiles (grass plan step 3): their rules, and that none of their systems ever reach a server world.</summary>
    public sealed class GrassTileTests
    {
        [Test]
        public void ServerWorldGetsNoGrassSystem()
        {
            var server = DefaultWorldInitialization.GetAllSystems(WorldSystemFilterFlags.ServerSimulation);
            foreach (var type in server)
                Assert.That(type.Assembly, Is.Not.EqualTo(typeof(GrassTileCullSystem).Assembly), type.FullName + " would run on the server");
            var client = DefaultWorldInitialization.GetAllSystems(WorldSystemFilterFlags.ClientSimulation);
            Assert.That(client, Does.Contain(typeof(GrassTileCullSystem)));
            Assert.That(client, Does.Contain(typeof(GrassTileViewSystem)));
        }

        [Test]
        public void LookKeepsItsSideOfAnEdge()
        {
            const float near = 60f, far = 170f, gap = 4f;
            Assert.That(GrassTileRules.Choose(59f, GrassTileLod.Out, near, far, gap), Is.EqualTo(GrassTileLod.Near));
            // just past the near edge: a near tile stays near, a far tile stays far
            Assert.That(GrassTileRules.Choose(62f, GrassTileLod.Near, near, far, gap), Is.EqualTo(GrassTileLod.Near));
            Assert.That(GrassTileRules.Choose(62f, GrassTileLod.Far, near, far, gap), Is.EqualTo(GrassTileLod.Far));
            Assert.That(GrassTileRules.Choose(65f, GrassTileLod.Near, near, far, gap), Is.EqualTo(GrassTileLod.Far));
            // the far edge: a shown tile stays until the gap, a hidden one waits for the edge itself
            Assert.That(GrassTileRules.Choose(172f, GrassTileLod.Far, near, far, gap), Is.EqualTo(GrassTileLod.Far));
            Assert.That(GrassTileRules.Choose(172f, GrassTileLod.Out, near, far, gap), Is.EqualTo(GrassTileLod.Out));
            Assert.That(GrassTileRules.Choose(175f, GrassTileLod.Far, near, far, gap), Is.EqualTo(GrassTileLod.Out));
        }

        [Test]
        public void DistanceIsToTheNearestPointOfTheTile()
        {
            Assert.That(GrassTileRules.DistanceToRect(new float2(5f, 5f), 0f, 10f), Is.EqualTo(0f));
            Assert.That(GrassTileRules.DistanceToRect(new float2(13f, 5f), 0f, 10f), Is.EqualTo(3f).Within(1e-5f));
            Assert.That(GrassTileRules.DistanceToRect(new float2(13f, 14f), 0f, 10f), Is.EqualTo(5f).Within(1e-5f));
        }

        [Test]
        public void BoxBehindTheCameraIsCulled()
        {
            // a box frustum round the origin looking down +Z: x and y within 10, z from 1 to 100 (inward normals)
            var view = new GrassView
            {
                Plane0 = new float4(1f, 0f, 0f, 10f), Plane1 = new float4(-1f, 0f, 0f, 10f),
                Plane2 = new float4(0f, 1f, 0f, 10f), Plane3 = new float4(0f, -1f, 0f, 10f),
                Plane4 = new float4(0f, 0f, 1f, -1f), Plane5 = new float4(0f, 0f, -1f, 100f),
            };
            Assert.That(GrassTileRules.BoxInFrustum(new float3(-1f, -1f, 5f), new float3(1f, 1f, 6f), view), Is.True);
            Assert.That(GrassTileRules.BoxInFrustum(new float3(-1f, -1f, -20f), new float3(1f, 1f, -10f), view), Is.False);
            // straddling a side plane still counts as seen
            Assert.That(GrassTileRules.BoxInFrustum(new float3(9f, -1f, 5f), new float3(30f, 1f, 6f), view), Is.True);
        }
    }
}
