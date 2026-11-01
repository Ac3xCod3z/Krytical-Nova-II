using KryticalNova.Player;
using KryticalNova.World;
using KryticalNova.World.Surfaces;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Tests
{
    /// <summary>
    /// Props on a wall's face (owner, 1 Nov: the wall walk went through an air-conditioner box; no glitching or clipping): a body standing
    /// on the face where a box sticks out is found, the face itself is not, and the wall walk's way along goes round the box. A real graph
    /// baked round a block (as <c>SurfaceGraphBakerTests</c>), far above anything in the open scene, with a box on its front face.
    /// </summary>
    public sealed class WallPropTests
    {
        static readonly Vector3 Site = new Vector3(0f, 6000f, 0f);
        GameObject block, slab, box;
        SurfaceGraphBaker.Options options;
        SurfaceGraph graph;

        // the front face (normal +z) at z = 2; the box 0.8 m wide, 0.6 m tall, sticking out 0.6 m, centred 4 m up
        static readonly Vector3 BoxCentre = Site + new Vector3(0f, 4f, 2.3f);

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
            // baked before the box goes on (the zone's graph is baked from its buildings, the props come with them or later)
            SurfaceGraphBaker.Result r = SurfaceGraphBaker.Bake(new[] { new Bounds(Site + new Vector3(0f, 4f, 0f), new Vector3(8f, 9f, 8f)) }, options);
            graph = ScriptableObject.CreateInstance<SurfaceGraph>();
            graph.Set(r.Points, r.Normals, r.EdgeStart, r.EdgeTo, r.EdgeKind, r.Joins, "test");
            box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.transform.position = BoxCentre;
            box.transform.localScale = new Vector3(0.8f, 0.6f, 0.6f);
            Physics.SyncTransforms();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(graph);
            Object.DestroyImmediate(block);
            Object.DestroyImmediate(slab);
            Object.DestroyImmediate(box);
        }

        int Node(float x, float y) => graph.Nearest(Site + new Vector3(x, y, 2f + options.StandOff), 0.6f);

        Vector3 Feet(int node) => graph.points[node] - graph.normals[node] * options.StandOff;

        bool Blocked(int node) =>
            WallWalkLayout.PropOnFace(Feet(node), graph.normals[node], block.GetComponent<Collider>(), null, Physics.DefaultRaycastLayers);

        [Test]
        public void ABodyWhereTheBoxSticksOut_IsBlocked_TheBareFaceIsNot()
        {
            int behind = Node(0f, 4f), bare = Node(-1.5f, 4f), low = Node(0f, 1.5f);
            Assert.GreaterOrEqual(behind, 0);
            Assert.GreaterOrEqual(bare, 0);
            Assert.GreaterOrEqual(low, 0);
            Assert.IsTrue(Blocked(behind), "standing on the face behind the box: the box is in the body");
            Assert.IsFalse(Blocked(bare), "the bare face 1.5 m to the side");
            Assert.IsFalse(Blocked(low), "the bare face 2.5 m below the box");
            Assert.IsFalse(WallWalkLayout.PropOnFace(Feet(bare), graph.normals[bare], null, block.transform, Physics.DefaultRaycastLayers),
                "the body's own colliders do not count");
        }

        [Test]
        public void TheWayAlongTheWall_GoesRoundTheBox()
        {
            int from = Node(-1.5f, 4f), to = Node(1.5f, 4f);
            SurfaceRoute.Route straight = SurfaceRoute.Along(graph, from, to);
            Assert.IsNotNull(straight);
            bool throughBox = false;
            foreach (int n in straight.Nodes) if (Blocked(n)) throughBox = true;
            Assert.IsTrue(throughBox, "the known fault: without the check the way goes straight through the box");

            SurfaceRoute.Route round = SurfaceRoute.Along(graph, from, to, Blocked);
            Assert.IsNotNull(round, "there is a way round");
            foreach (int n in round.Nodes) Assert.IsFalse(Blocked(n), "no node of the way has the box in the body");
            Assert.AreEqual(to, round.Nodes[round.Nodes.Length - 1]);
        }
    }
}
