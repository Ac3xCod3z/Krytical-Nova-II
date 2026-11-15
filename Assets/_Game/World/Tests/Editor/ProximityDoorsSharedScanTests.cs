using System.Collections.Generic;
using System.Diagnostics;
using KryticalNova.Core;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.World.Tests
{
    /// <summary>
    /// Perf clean-ups round 2, chunk 11: <see cref="ProximitySlidingDoors"/> read every body's transform per door per scan; now the first
    /// door of a frame gathers the places once (<see cref="ProximitySlidingDoors.PlayerPlaces"/>) and every door tests them. D doors and N
    /// bodies give the same open / closed answers, old against new; the time of one frame's scans is printed at 10 doors x N.
    /// </summary>
    public sealed class ProximityDoorsSharedScanTests
    {
        sealed class Body : IPlayerBody
        {
            public Transform Transform { get; set; }
            public PlayerKey Owner => PlayerKey.None;
            public bool IsLocal => false;
            public bool IsJumping => false;
            public void RebindToScene() { }
        }

        readonly List<GameObject> m_Made = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in m_Made) if (go != null) Object.DestroyImmediate(go);
            m_Made.Clear();
        }

        // The old scan, verbatim.
        static bool OldNear(IReadOnlyList<IPlayerBody> bodies, Vector3 centre, float openDistance, float heightRange)
        {
            for (int i = 0; i < bodies.Count; i++)
            {
                if (bodies[i] == null || bodies[i].Transform == null) continue;
                Vector3 d = bodies[i].Transform.position - centre;
                if (Mathf.Abs(d.y) > heightRange) continue;
                d.y = 0f;
                if (d.sqrMagnitude <= openDistance * openDistance) return true;
            }
            return false;
        }

        List<IPlayerBody> Crowd(int n, int seed)
        {
            var rng = new System.Random(seed);
            var bodies = new List<IPlayerBody>(n);
            for (int i = 0; i < n; i++)
            {
                if (i % 17 == 3) { bodies.Add(null); continue; }   // a gap in the list
                if (i % 13 == 5) { bodies.Add(new Body()); continue; }   // a body with no transform
                var go = new GameObject("body " + i) { hideFlags = HideFlags.HideAndDontSave };
                m_Made.Add(go);
                go.transform.position = new Vector3((float)(rng.NextDouble() * 200 - 100), (float)(rng.NextDouble() * 16 - 8), (float)(rng.NextDouble() * 200 - 100));
                bodies.Add(new Body { Transform = go.transform });
            }
            return bodies;
        }

        static Vector3[] Doors(int d, int seed)
        {
            var rng = new System.Random(seed * 31);
            var doors = new Vector3[d];
            for (int i = 0; i < d; i++) doors[i] = new Vector3((float)(rng.NextDouble() * 200 - 100), (float)(rng.NextDouble() * 4 - 2), (float)(rng.NextDouble() * 200 - 100));
            return doors;
        }

        [TestCase(10, 50, 1)]
        [TestCase(10, 200, 2)]
        [TestCase(40, 1000, 3)]
        [TestCase(10, 0, 4)]
        public void EveryDoor_OpensAndClosesAsBefore(int d, int n, int seed)
        {
            List<IPlayerBody> bodies = Crowd(n, seed);
            Vector3[] doors = Doors(d, seed);
            var places = new List<Vector3>();
            ProximitySlidingDoors.PlayerPlaces.Gather(bodies, places);
            int open = 0;
            foreach (float dist in new[] { 0.5f, 7f, 15f })
                foreach (float height in new[] { 0.5f, 5f })
                    for (int i = 0; i < d; i++)
                    {
                        bool old = OldNear(bodies, doors[i], dist, height);
                        Assert.AreEqual(old, ProximitySlidingDoors.AnyNear(places, doors[i], dist, height), $"door {i}, {dist} m, {height} m");
                        if (old) open++;
                    }
            if (n >= 200) Assert.Greater(open, 0, "some doors open");
        }

        [Test]
        public void ADoorExactlyAtTheEdge_Opens_AsBefore()
        {
            var go = new GameObject("edge") { hideFlags = HideFlags.HideAndDontSave };
            m_Made.Add(go);
            go.transform.position = new Vector3(7f, 5f, 0f);
            var bodies = new List<IPlayerBody> { new Body { Transform = go.transform } };
            var places = new List<Vector3>();
            ProximitySlidingDoors.PlayerPlaces.Gather(bodies, places);
            Assert.AreEqual(OldNear(bodies, Vector3.zero, 7f, 5f), ProximitySlidingDoors.AnyNear(places, Vector3.zero, 7f, 5f));
            Assert.IsTrue(ProximitySlidingDoors.AnyNear(places, Vector3.zero, 7f, 5f));
        }

        [Test]
        public void Timing_10Doors_OldAndNew_AtN50_200_1000()
        {
            foreach (int n in new[] { 50, 200, 1000 })
            {
                List<IPlayerBody> bodies = Crowd(n, n);
                Vector3[] doors = Doors(10, n);
                const int frames = 100;   // every door scanning in the same frame, 100 times
                int a = 0, b = 0;
                var sw = Stopwatch.StartNew();
                for (int f = 0; f < frames; f++) for (int i = 0; i < doors.Length; i++) if (OldNear(bodies, doors[i], 0.01f, 0.01f)) a++;
                double oldMs = sw.Elapsed.TotalMilliseconds / frames;
                var places = new List<Vector3>(n);
                sw.Restart();
                for (int f = 0; f < frames; f++)
                {
                    ProximitySlidingDoors.PlayerPlaces.Gather(bodies, places);
                    for (int i = 0; i < doors.Length; i++) if (ProximitySlidingDoors.AnyNear(places, doors[i], 0.01f, 0.01f)) b++;
                }
                double newMs = sw.Elapsed.TotalMilliseconds / frames;
                UnityEngine.Debug.Log($"[perf chunk 11] 10 doors scanning in one frame, N={n} bodies: old {oldMs:F4} ms, new {newMs:F4} ms (x{oldMs / System.Math.Max(newMs, 1e-6):F1})");
                Assert.AreEqual(a, b);
            }
        }
    }
}
