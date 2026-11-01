using System.Collections.Generic;
using KryticalNova.Player;
using KryticalNova.World;
using KryticalNova.World.Surfaces;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Tests
{
    /// <summary>
    /// The wall walk laid out as one walk (`m3c-wallstride-design.md` section 3, step 2) on a handmade wall: NGO's plant, the face's
    /// straight runs thinned, the top-out's two mantle legs and the walk on, the way down from a roof head first, the stick probe's snap,
    /// and the corner budget. No zone, no NavMesh: the routes are made by hand as <see cref="SurfaceRoute"/> would give them.
    /// </summary>
    public class WallWalkLayoutTests
    {
        // a wall face at x = 5.4 facing -x, nodes every 0.5 m from 0.5 to 6 m up (the graph's points stand 0.38 m out from the face)
        const float Face = 5.4f;
        static readonly Vector3 Normal = Vector3.left;

        WallstrideSettings settings;
        SurfaceGraph graph;

        [SetUp]
        public void SetUp()
        {
            settings = ScriptableObject.CreateInstance<WallstrideSettings>();
            graph = ScriptableObject.CreateInstance<SurfaceGraph>();
            var points = new List<Vector3>();
            var normals = new List<Vector3>();
            for (int i = 0; i < 12; i++)
            {
                points.Add(new Vector3(Face, 0.5f + i * 0.5f, 0f) + Normal * settings.wallStandOff);
                normals.Add(Normal);
            }
            // a sideways run along the top row (z 0.5 .. 2), turning at the column's head
            for (int i = 1; i <= 4; i++)
            {
                points.Add(new Vector3(Face, 6f, i * 0.5f) + Normal * settings.wallStandOff);
                normals.Add(Normal);
            }
            graph.Set(points.ToArray(), normals.ToArray(), new int[points.Count + 1], new int[0], new SurfaceGraph.EdgeKind[0],
                new SurfaceGraph.Join[0], "test wall");
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(graph);
            Object.DestroyImmediate(settings);
        }

        static int[] Column(int from, int to)
        {
            var nodes = new List<int>();
            for (int i = from; i <= to; i++) nodes.Add(i);
            return nodes.ToArray();
        }

        Vector3 FeetOf(int node) => graph.points[node] - Normal * settings.wallStandOff + Normal * settings.wallFootLift;

        [Test]
        public void FromTheGround_ThePlant_TheFaceThinned_OverTheTop_AndTheWalkOn()
        {
            var route = new SurfaceRoute.Route
            {
                Nodes = Column(0, 11),
                HasEntry = true, Entry = new SurfaceGraph.Join { node = 0, navPoint = new Vector3(4.6f, 0f, 0f), kind = SurfaceGraph.JoinKind.Ground },
                HasExit = true, Exit = new SurfaceGraph.Join { node = 11, navPoint = new Vector3(6f, 6f, 0f), kind = SurfaceGraph.JoinKind.Top },
                Departure = new[] { new Vector3(6f, 6f, 0f), new Vector3(9f, 6f, 0f) },
            };
            var layout = new WallWalkLayout(settings, null, new Vector3(4.6f, 0f, 0f), Vector3.up, Vector3.right, 9f);
            layout.PlantAndAlong(graph, route, Vector3.right);
            List<WallWalkLayout.Corner> c = layout.Corners;

            Assert.AreEqual(6, c.Count, "start, plant, the face's top (the straight column thinned), two mantle legs, the walk on");
            Assert.AreEqual(WallRunPhase.Plant, c[1].Phase);
            Assert.AreEqual(settings.wallPlantSeconds, c[1].Seconds, 1e-5f, "NGO's 0.32 s plant");
            Assert.AreEqual(WallWalkLayout.EaseIn, c[1].EaseUp);
            Assert.AreEqual(WallWalkLayout.EaseOut, c[1].EaseAlong);
            Assert.That(Vector3.Distance(c[1].Feet, FeetOf(0)), Is.LessThan(1e-4f), "the feet meet the wall at the first node");
            Assert.That(Vector3.Angle(c[1].Up, Normal), Is.LessThan(0.5f), "standing on the face");
            Assert.That(Vector3.Angle(c[1].Forward, Vector3.up), Is.LessThan(0.5f), "facing up it");

            Assert.AreEqual(2, layout.OnWallFrom);
            Assert.AreEqual(3, layout.OffWallFrom);
            Assert.AreEqual(WallRunPhase.WallMove, c[2].Phase);
            Assert.That(Vector3.Distance(c[2].Feet, FeetOf(11)), Is.LessThan(1e-4f));
            Assert.AreEqual(0f, c[2].Seconds, "at the wall's speed");

            Assert.AreEqual(WallRunPhase.Mantle, c[3].Phase);
            Assert.AreEqual(6.25f, c[3].Feet.y, 1e-4f, "up past the edge by NGO's mantle clearance");
            Assert.AreEqual(2f * 0.25f / 9f, c[3].Seconds, 1e-4f, "the lift at the wall's speed");
            Assert.AreEqual(WallWalkLayout.EaseOut, c[3].EaseUp);
            Assert.That(Vector3.Angle(c[3].Up, Vector3.up), Is.LessThan(0.5f), "upright from the top-out on");
            Assert.AreEqual(settings.mantleSeconds, c[4].Seconds, 1e-5f, "NGO's 0.4 s mantle");
            Assert.AreEqual(new Vector3(6f, 6f, 0f), c[4].Feet);
            Assert.AreEqual(new Vector3(9f, 6f, 0f), c[5].Feet, "the walk on to the place clicked");
            Assert.AreEqual(0f, c[5].Seconds);
            Assert.IsFalse(layout.EndsOnWall);
        }

        [Test]
        public void AlongTheWall_KeepsTheTurn_AndEndsStandingOnIt()
        {
            int[] nodes = { 8, 9, 10, 11, 12, 13, 14, 15 };   // up the column, then sideways along the top row
            var layout = new WallWalkLayout(settings, null, FeetOf(8), Normal, Vector3.up, 9f);
            layout.AlongAndOff(graph, new SurfaceRoute.Route { Nodes = nodes }, 0);
            List<WallWalkLayout.Corner> c = layout.Corners;
            Assert.AreEqual(3, c.Count, "start, the turn at the column's head, the end of the row");
            Assert.That(Vector3.Distance(c[1].Feet, FeetOf(11)), Is.LessThan(1e-4f), "the turn is kept");
            Assert.That(Vector3.Distance(c[2].Feet, FeetOf(15)), Is.LessThan(1e-4f));
            Assert.That(Vector3.Angle(c[2].Forward, Vector3.forward), Is.LessThan(0.5f), "facing along the row");
            Assert.AreEqual(1, layout.OnWallFrom);
            Assert.AreEqual(3, layout.OffWallFrom);
            Assert.IsTrue(layout.EndsOnWall, "no way off: standing on the wall (NGO WallHold)");
        }

        [Test]
        public void FromARoof_OutOverTheEdge_ThenDownOntoTheFaceHeadFirst()
        {
            var route = new SurfaceRoute.Route
            {
                Nodes = Column(11, 11),
                HasEntry = true, Entry = new SurfaceGraph.Join { node = 11, navPoint = new Vector3(6f, 6f, 0f), kind = SurfaceGraph.JoinKind.Top },
            };
            var layout = new WallWalkLayout(settings, null, new Vector3(6f, 6f, 0f), Vector3.up, Vector3.left, 9f);
            layout.PlantAndAlong(graph, route, Vector3.left);
            List<WallWalkLayout.Corner> c = layout.Corners;
            Assert.AreEqual(3, c.Count);
            Assert.AreEqual(6f + settings.mantleClearance, c[1].Feet.y, 1e-4f, "out over the edge, lifted");
            Assert.That(c[1].Feet.x, Is.LessThan(FeetOf(11).x), "out past the face");
            Assert.That(Vector3.Distance(c[2].Feet, FeetOf(11)), Is.LessThan(1e-4f));
            Assert.That(Vector3.Angle(c[2].Forward, Vector3.down), Is.LessThan(0.5f), "head first down the face (NGO)");
            Assert.IsTrue(layout.EndsOnWall);
        }

        [Test]
        public void TheStickProbe_PutsTheFeetOnTheRealFace()
        {
            // the real face is 5 cm further in than the graph says, and turned 10 degrees
            Vector3 real = Quaternion.Euler(0f, 10f, 0f) * Normal;
            WallWalkLayout.FaceProbe probe = (Vector3 o, Vector3 d, float len, out Vector3 p, out Vector3 n) =>
            {
                p = new Vector3(Face + 0.05f, o.y, o.z);
                n = real;
                return true;
            };
            var layout = new WallWalkLayout(settings, probe, Vector3.zero, Vector3.up, Vector3.right, 9f);
            layout.FaceAt(graph, 4, out Vector3 feet, out Vector3 normal);
            Assert.AreEqual(Face + 0.05f + real.x * settings.wallFootLift, feet.x, 1e-4f);
            Assert.That(Vector3.Angle(normal, real), Is.LessThan(0.01f));

            WallWalkLayout.FaceProbe floor = (Vector3 o, Vector3 d, float len, out Vector3 p, out Vector3 n) =>
            {
                p = o; n = Vector3.up; return true;   // a floor is not a face (NGO: >= 36 degrees from up)
            };
            new WallWalkLayout(settings, floor, Vector3.zero, Vector3.up, Vector3.right, 9f).FaceAt(graph, 4, out Vector3 kept, out _);
            Assert.That(Vector3.Distance(kept, FeetOf(4)), Is.LessThan(1e-4f), "the graph's feet kept");
        }

        [Test]
        public void TheCornerBudget_CutsOnTheWall_AndStandsThere()
        {
            var layout = new WallWalkLayout(settings, null, FeetOf(8), Normal, Vector3.up, 9f);
            layout.AlongAndOff(graph, new SurfaceRoute.Route { Nodes = new[] { 8, 9, 10, 11, 12, 13, 14, 15 } }, 0);
            Assert.IsTrue(layout.FitTo(2));
            Assert.AreEqual(2, layout.Corners.Count);
            Assert.IsTrue(layout.Truncated && layout.EndsOnWall, "cut on the wall, standing there; the rest is asked again from there");
            Assert.AreEqual(2, layout.OffWallFrom);
            Assert.IsTrue(new WallWalkLayout(settings, null, Vector3.zero, Vector3.up, Vector3.right, 9f).FitTo(2), "a short one fits as it is");
        }

        [Test]
        public void AnArc_IsNgosFallUnderGravity()
        {
            var layout = new WallWalkLayout(settings, null, new Vector3(0f, 4f, 0f), Normal, Vector3.up, 9f);
            layout.WallEnds();
            layout.Arc(WallRunPhase.PushOff, new Vector3(3f, 0f, 0f), 1.2f, WallWalkLayout.Facing(Vector3.right));
            List<WallWalkLayout.Corner> c = layout.Corners;
            float g = settings.dropGravity, up = Mathf.Sqrt(2f * 1.2f / g), down = Mathf.Sqrt(2f * 5.2f / g);
            Assert.AreEqual(3, c.Count);
            Assert.AreEqual(5.2f, c[1].Feet.y, 1e-4f, "the apex 1.2 m over the higher end");
            Assert.AreEqual(3f * up / (up + down), c[1].Feet.x, 1e-4f);
            Assert.AreEqual(up, c[1].Seconds, 1e-4f);
            Assert.AreEqual(down, c[2].Seconds, 1e-4f);
            Assert.AreEqual(WallWalkLayout.EaseOut, c[1].EaseUp);
            Assert.AreEqual(WallWalkLayout.EaseIn, c[2].EaseUp);
            Assert.AreEqual(WallRunPhase.PushOff, c[2].Phase);
        }
    }
}
