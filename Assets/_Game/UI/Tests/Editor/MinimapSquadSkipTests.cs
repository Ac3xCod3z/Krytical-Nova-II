using System.Collections.Generic;
using System.Diagnostics;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.UI.Tests
{
    /// <summary>
    /// Perf night chunk 2: the minimap's Squad markers. The bodies loop used to skip a mate already placed by the players loop by
    /// searching EVERY active player (<c>Contains(players, seen)</c>); it now searches only the mates the players loop placed this
    /// frame. Both loops apply the same mate test to the same GameObject before that check, so "seen is an active player" and "seen
    /// was placed" are the same thing there. This model runs both rules on the same crowd (plain GameObjects stand in for the
    /// players, so the Unity-object compare costs what it costs in the game) and checks the same markers in the same order, then
    /// times one frame at N = 50 / 200 / 1000 with a Squad of 5.
    /// </summary>
    public class MinimapSquadSkipTests
    {
        sealed class Body { public GameObject Transform; public GameObject LivePlayer; public bool Mate; }

        readonly List<GameObject> m_Made = new List<GameObject>();

        [TearDown]
        public void TearDown() { foreach (var g in m_Made) if (g != null) Object.DestroyImmediate(g); m_Made.Clear(); }

        GameObject Go(string name) { var g = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave }; m_Made.Add(g); return g; }

        static bool Contains(IReadOnlyList<GameObject> list, GameObject item)
        {
            for (int i = 0; i < list.Count; i++) if (list[i] == item) return true;
            return false;
        }

        // The two loops of MinimapController.RefreshMarkers' Squad branch, with the mate test as a flag. useOld picks the old skip.
        static void Squad(GameObject self, List<GameObject> players, bool[] playerIsMate, List<Body> bodies, bool useOld,
                          List<GameObject> placed, List<GameObject> into)
        {
            into.Clear(); placed.Clear();
            for (int i = 0; i < players.Count; i++)
            {
                GameObject p = players[i];
                if (p == null || p == self || !playerIsMate[i]) continue;
                into.Add(p);
                placed.Add(p);
            }
            for (int i = 0; i < bodies.Count; i++)
            {
                Body b = bodies[i];
                if (b.Transform == null || b.Transform == self || !b.Mate) continue;
                if (b.LivePlayer != null && Contains(useOld ? players : placed, b.LivePlayer)) continue;   // already placed
                into.Add(b.Transform);
            }
        }

        void Crowd(int n, int seed, out GameObject self, out List<GameObject> players, out bool[] mates, out List<Body> bodies)
        {
            var rng = new System.Random(seed);
            self = Go("self");
            players = new List<GameObject> { self };
            mates = new bool[n + 1];
            bodies = new List<Body>();
            int squadLeft = 4;   // the other four of a Squad of 5
            for (int i = 0; i < n; i++)
            {
                var go = Go("p" + i);
                bool mate = squadLeft > 0 && rng.Next(n / 4 + 1) == 0 && squadLeft-- > 0;
                // A player either has a live PointAndClickPlayer (in the active list) or is only an entity view (bodies only); a
                // live one also shows in the bodies list, as on a host.
                bool live = rng.Next(3) != 0;
                if (live) { players.Add(go); mates[players.Count - 1] = mate; }
                bodies.Add(new Body { Transform = go, LivePlayer = live ? go : null, Mate = mate });
            }
            bodies.Add(new Body { Transform = self, LivePlayer = self, Mate = false });
        }

        [Test]
        public void TheSkipFromPlacedMates_PlacesTheSameMarkers_InTheSameOrder()
        {
            for (int seed = 1; seed <= 200; seed++)
            {
                Crowd(40, seed, out var self, out var players, out var mates, out var bodies);
                var a = new List<GameObject>(); var b = new List<GameObject>(); var placed = new List<GameObject>();
                Squad(self, players, mates, bodies, true, placed, a);
                Squad(self, players, mates, bodies, false, placed, b);
                CollectionAssert.AreEqual(a, b, $"seed {seed}");
                TearDown();
            }
        }

        [TestCase(50), TestCase(200), TestCase(1000)]
        public void Timing_OneFrameOfSquadMarkers_OldVsNew(int n)
        {
            Crowd(n, 7, out var self, out var players, out var mates, out var bodies);
            var a = new List<GameObject>(); var b = new List<GameObject>(); var placed = new List<GameObject>();
            int frames = 200;
            Squad(self, players, mates, bodies, true, placed, a); Squad(self, players, mates, bodies, false, placed, b);   // warm up
            CollectionAssert.AreEqual(a, b);
            var sw = Stopwatch.StartNew();
            for (int f = 0; f < frames; f++) Squad(self, players, mates, bodies, true, placed, a);
            double oldMs = sw.Elapsed.TotalMilliseconds / frames;
            sw.Restart();
            for (int f = 0; f < frames; f++) Squad(self, players, mates, bodies, false, placed, b);
            double newMs = sw.Elapsed.TotalMilliseconds / frames;
            string line = $"[perf chunk 2] minimap Squad markers N={n}, Squad of 5 ({a.Count} placed): old {oldMs:F4} ms/frame, new {newMs:F4} ms/frame";
            TestContext.WriteLine(line);
            UnityEngine.Debug.Log(line);
        }
    }
}
