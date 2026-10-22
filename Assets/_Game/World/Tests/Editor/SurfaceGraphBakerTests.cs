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
        public void NoPlaceIsInsideASolid_ASlabOrAThickWall()
        {
            // A closed mesh (a thick roof slab, a wall) round the block: the block's faces inside it are no place to
            // stand, though nothing touches a body there (owner, 18 Oct 2026: places "inside what looks like roofs").
            var shell = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(shell.GetComponent<BoxCollider>());
            shell.AddComponent<MeshCollider>().sharedMesh = shell.GetComponent<MeshFilter>().sharedMesh;
            shell.transform.position = Site + new Vector3(0f, 6f, 0f);
            shell.transform.localScale = new Vector3(7f, 3f, 7f);
            Physics.SyncTransforms();
            try
            {
                SurfaceGraphBaker.Result r = Bake();
                Assert.Greater(r.RejectedSolid, 0, "the block's faces inside the shell were offered and turned down");
                var inside = new Bounds(shell.transform.position, shell.transform.localScale - Vector3.one * 0.05f);
                foreach (Vector3 p in r.Points) Assert.IsFalse(inside.Contains(p), $"a place at {p} is inside the shell");
                Assert.IsFalse(Physics.queriesHitBackfaces && !backfacesBefore, "the bake leaves back faces as it found them");
            }
            finally
            {
                Object.DestroyImmediate(shell);
            }
        }

        private readonly bool backfacesBefore = Physics.queriesHitBackfaces;

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

        [Test]
        public void AWayAlongTheBlockGoesRoundItsCorner_StepByStep()
        {
            SurfaceGraphBaker.Result r = Bake();
            var graph = ScriptableObject.CreateInstance<SurfaceGraph>();
            try
            {
                graph.Set(r.Points, r.Normals, r.EdgeStart, r.EdgeTo, r.EdgeKind, r.Joins, "test");
                int front = graph.Nearest(Site + new Vector3(0f, 3f, 2f + options.StandOff), 0.6f);
                int side = graph.Nearest(Site + new Vector3(2f + options.StandOff, 6f, 0f), 0.6f);
                Assert.GreaterOrEqual(front, 0);
                Assert.GreaterOrEqual(side, 0);
                SurfaceRoute.Route way = SurfaceRoute.Along(graph, front, side);
                Assert.IsNotNull(way);
                Assert.AreEqual(front, way.Nodes[0]);
                Assert.AreEqual(side, way.Nodes[way.Nodes.Length - 1]);
                Assert.IsFalse(way.HasExit, "a way along the wall stays on it");
                bool turned = false;
                for (int i = 1; i < way.Nodes.Length; i++)
                {
                    Assert.LessOrEqual(Vector3.Distance(graph.points[way.Nodes[i - 1]], graph.points[way.Nodes[i]]), options.LinkDistance * 1.7f + 0.01f, "one step at a time");
                    if (Vector3.Angle(graph.normals[way.Nodes[i - 1]], graph.normals[way.Nodes[i]]) > options.MaxTurn) turned = true;
                }
                Assert.IsTrue(turned, "the way goes round the corner, not through the block");
            }
            finally
            {
                Object.DestroyImmediate(graph);
            }
        }

        [Test]
        public void TheGraphComesBackFromItsFile_AsItWasBaked()
        {
            SurfaceGraphBaker.Result r = Bake();
            var baked = ScriptableObject.CreateInstance<SurfaceGraph>();
            var read = ScriptableObject.CreateInstance<SurfaceGraph>();
            try
            {
                baked.Set(r.Points, r.Normals, r.EdgeStart, r.EdgeTo, r.EdgeKind, r.Joins, "test", r.Scales, r.Rule);
                byte[] bytes = baked.Encode();
                typeof(SurfaceGraph).GetMethod("Decode", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                    .Invoke(read, new object[] { bytes });
                Assert.AreEqual(baked.NodeCount, read.NodeCount);
                // Version 4 keeps each node's edges, in the order of the node they lead to.
                for (int i = 0; i < baked.NodeCount; i++)
                    CollectionAssert.AreEquivalent(Edges(baked, i), Edges(read, i), $"node {i} keeps its edges");
                Assert.AreEqual(baked.joins.Length, read.joins.Length);
                for (int i = 0; i < baked.NodeCount; i++)
                {
                    Assert.AreEqual(0f, Vector3.Distance(baked.points[i], read.points[i]), 0.01f, "points are kept to the centimetre");
                    Assert.Less(Vector3.Angle(baked.normals[i], read.normals[i]), 1.5f, "normals are kept to about a degree");
                }
            }
            finally
            {
                Object.DestroyImmediate(baked);
                Object.DestroyImmediate(read);
            }
        }

        [Test]
        public void AVersion3File_StillReads()
        {
            SurfaceGraphBaker.Result r = Bake();
            var baked = ScriptableObject.CreateInstance<SurfaceGraph>();
            var read = ScriptableObject.CreateInstance<SurfaceGraph>();
            try
            {
                baked.Set(r.Points, r.Normals, r.EdgeStart, r.EdgeTo, r.EdgeKind, r.Joins, "test"); // no spacing: version 3
                byte[] bytes = baked.Encode();
                typeof(SurfaceGraph).GetMethod("Decode", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                    .Invoke(read, new object[] { bytes });
                CollectionAssert.AreEqual(baked.edgeTo, read.edgeTo);
                CollectionAssert.AreEqual(baked.edgeKind, read.edgeKind);
            }
            finally
            {
                Object.DestroyImmediate(baked);
                Object.DestroyImmediate(read);
            }
        }

        private static string[] Edges(SurfaceGraph g, int i)
        {
            var list = new string[g.edgeStart[i + 1] - g.edgeStart[i]];
            for (int e = g.edgeStart[i]; e < g.edgeStart[i + 1]; e++) list[e - g.edgeStart[i]] = g.edgeTo[e] + ":" + g.edgeKind[e];
            return list;
        }

        private static int Face(Vector3 n) =>
            Vector3.Dot(n, Vector3.forward) > 0.9f ? 0 : Vector3.Dot(n, Vector3.back) > 0.9f ? 1 : Vector3.Dot(n, Vector3.left) > 0.9f ? 2 : 3;
    }
}
