using KryticalNova.World;
using KryticalNova.World.Surfaces;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Tests
{
    /// <summary>
    /// Walls as a second ground: the surface graph baked round a plain block, far above anything in the open scene. The
    /// block is 4 m square and 8 m tall, on a slab as its ground.
    /// </summary>
    public sealed class SurfaceGraphBakerTests
    {
        private static readonly Vector3 Site = new Vector3(0f, 5000f, 0f);
        private GameObject block, slab;
        private SurfaceGraphBaker.Options options;

        [SetUp]
        public void SetUp()
        {
            block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.transform.position = Site + new Vector3(0f, 4f, 0f);
            block.transform.localScale = new Vector3(4f, 8f, 4f);
            slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            slab.transform.position = Site + new Vector3(0f, -0.5f, 0f);
            slab.transform.localScale = new Vector3(20f, 1f, 20f);
            Physics.SyncTransforms();
            options = SurfaceGraphBaker.Options.Default;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(block);
            Object.DestroyImmediate(slab);
        }

        private SurfaceGraphBaker.Result Bake() =>
            SurfaceGraphBaker.Bake(new[] { new Bounds(Site + new Vector3(0f, 4f, 0f), new Vector3(8f, 9f, 8f)) }, options);

        [Test]
        public void EveryFaceOfTheBlockGetsNodes_StandingOutFromIt_FromFootToTop()
        {
            SurfaceGraphBaker.Result r = Bake();
            Assert.Greater(r.Points.Length, 100);
            var faces = new[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right };
            foreach (Vector3 face in faces)
            {
                int count = 0;
                float low = float.MaxValue, high = float.MinValue;
                for (int i = 0; i < r.Points.Length; i++)
                {
                    if (Vector3.Dot(r.Normals[i], face) < 0.99f) continue;
                    count++;
                    low = Mathf.Min(low, r.Points[i].y - Site.y);
                    high = Mathf.Max(high, r.Points[i].y - Site.y);
                    // Out from the face by the stand-off, never inside the block.
                    float outFromCentre = Vector3.Dot(r.Points[i] - block.transform.position, face);
                    Assert.AreEqual(2f + options.StandOff, outFromCentre, 0.02f);
                }
                Assert.Greater(count, 20, $"face {face}");
                Assert.Less(low, 1f, $"face {face} reaches its foot");
                Assert.Greater(high, 7f, $"face {face} reaches its top");
            }
        }

        [Test]
        public void TheTopAndTheGroundAreNotWalls()
        {
            SurfaceGraphBaker.Result r = Bake();
            foreach (Vector3 n in r.Normals)
                Assert.GreaterOrEqual(Vector3.Angle(n, Vector3.up), options.MinAngle);
        }

        [Test]
        public void NodesJoinUpAlongAFace_AndRoundEveryCorner()
        {
            SurfaceGraphBaker.Result r = Bake();
            int corners = 0, alone = 0;
            var cornerPairs = new System.Collections.Generic.HashSet<(int, int)>();
            for (int i = 0; i < r.Points.Length; i++)
            {
                if (r.EdgeStart[i + 1] == r.EdgeStart[i]) alone++;
                for (int e = r.EdgeStart[i]; e < r.EdgeStart[i + 1]; e++)
                {
                    if (r.EdgeKind[e] != SurfaceGraph.EdgeKind.Corner) continue;
                    corners++;
                    Vector3 a = r.Normals[i], b = r.Normals[r.EdgeTo[e]];
                    cornerPairs.Add((Face(a), Face(b)));
                }
            }
            Assert.LessOrEqual(alone, r.Points.Length / 100, "almost every node has a step to take");
            Assert.Greater(corners, 0);
            // All four corners of the block: each face joins its two neighbours.
            Assert.IsTrue(cornerPairs.Contains((0, 2)) || cornerPairs.Contains((2, 0)));
            Assert.IsTrue(cornerPairs.Contains((0, 3)) || cornerPairs.Contains((3, 0)));
            Assert.IsTrue(cornerPairs.Contains((1, 2)) || cornerPairs.Contains((2, 1)));
            Assert.IsTrue(cornerPairs.Contains((1, 3)) || cornerPairs.Contains((3, 1)));
        }

        [Test]
        public void ABodyThatDoesNotFitIsLeftOut()
        {
            // A beam close along one face leaves no room there for the body.
            var beam = GameObject.CreatePrimitive(PrimitiveType.Cube);
            beam.transform.position = Site + new Vector3(0f, 4f, 2.35f);
            beam.transform.localScale = new Vector3(4f, 1f, 0.2f);
            Physics.SyncTransforms();
            try
            {
                SurfaceGraphBaker.Result r = Bake();
                Assert.Greater(r.RejectedBody, 0);
                foreach (Vector3 p in r.Points)
                    Assert.IsFalse(Physics.CheckSphere(p, options.BodyRadius), $"a node at {p} has no room");
            }
            finally
            {
                Object.DestroyImmediate(beam);
            }
        }

        [Test]
        public void TheNearestNodeIsFound_WithinReachOnly()
        {
            SurfaceGraphBaker.Result r = Bake();
            var graph = ScriptableObject.CreateInstance<SurfaceGraph>();
            try
            {
                graph.Set(r.Points, r.Normals, r.EdgeStart, r.EdgeTo, r.EdgeKind, r.Joins, "test");
                Vector3 onFace = Site + new Vector3(0f, 4f, 2f + options.StandOff);
                int near = graph.Nearest(onFace, 1f);
                Assert.GreaterOrEqual(near, 0);
                Assert.Less(Vector3.Distance(graph.points[near], onFace), 0.5f);
                Assert.AreEqual(-1, graph.Nearest(Site + new Vector3(0f, 4f, 0f), 1f), "nothing inside the block");
            }
            finally
            {
                Object.DestroyImmediate(graph);
            }
        }

        private static int Face(Vector3 n) =>
            Vector3.Dot(n, Vector3.forward) > 0.9f ? 0 : Vector3.Dot(n, Vector3.back) > 0.9f ? 1 : Vector3.Dot(n, Vector3.left) > 0.9f ? 2 : 3;
    }
}
