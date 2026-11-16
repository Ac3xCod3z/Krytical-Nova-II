using System.Collections.Generic;
using System.Diagnostics;
using KryticalNova.Player.Entities;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace KryticalNova.Tests
{
    /// <summary>
    /// Perf night chunk 6: <see cref="PlayerSeparation.ResolvePairs{T}"/> finds the close pairs through a grid instead of testing every
    /// pair. The old loop is kept here verbatim; both run on copies of the same crowds, tick after tick, with the same slide (carry of
    /// part millimetres, a wall that stops a slide so the other steps aside or the pair is blocked), and every slide, in order, every
    /// blocked count and every final spot must be the same. Timing prints one tick of the old loop vs the grid at N = 50 / 200 / 1000.
    /// </summary>
    public sealed class PlayerSeparationGridTests
    {
        const int Radius = 400;
        const double Step = 1500.0 / 45.0;   // 33.3 mm: slideOutSpeed 1500 mm/s at 45 ticks

        /// <summary>The server's TrySlide in miniature: the carry, and a wall at <see cref="WallX"/> (the walkable surface's edge).</summary>
        sealed class Slider
        {
            public readonly Dictionary<int, double2> Carry = new Dictionary<int, double2>();
            public readonly List<(int who, int x, int z)> Log = new List<(int, int, int)>();
            public bool HasWall;
            public int WallX;
            Dictionary<int, double2> m_MarkCarry = new Dictionary<int, double2>();
            int m_MarkLog;
            public int Undos;

            /// <summary>Perf round 4: the point <see cref="Undo"/> goes back to (each tick's start).</summary>
            public void Mark() { m_MarkCarry = new Dictionary<int, double2>(Carry); m_MarkLog = Log.Count; }

            public void Undo()
            {
                Carry.Clear();
                foreach (var kv in m_MarkCarry) Carry[kv.Key] = kv.Value;
                Log.RemoveRange(m_MarkLog, Log.Count - m_MarkLog);
                Undos++;
            }
        }

        struct Slide : PlayerSeparation.IUndoableSlide
        {
            public Slider S;
            public void Undo() => S.Undo();
            public bool TrySlide(NativeList<PlayerSeparation.Body> standing, int at, double2 moveMm)
            {
                PlayerSeparation.Body b = standing[at];
                int id = b.Entity.Index;
                S.Carry.TryGetValue(id, out double2 carry);
                int2 whole = PlayerSeparation.Carry(ref carry, moveMm);
                if (whole.x == 0 && whole.y == 0) { S.Carry[id] = carry; S.Log.Add((id, int.MinValue, 0)); return true; }
                int tx = b.XMm + whole.x, tz = b.ZMm + whole.y;
                if (S.HasWall && tx > S.WallX)
                {
                    if (b.XMm >= S.WallX) { S.Carry.Remove(id); S.Log.Add((id, int.MaxValue, 0)); return false; }   // against the wall
                    tx = S.WallX; carry = double2.zero;
                }
                S.Carry[id] = carry;
                b.XMm = tx; b.ZMm = tz;
                standing[at] = b;
                S.Log.Add((id, tx, tz));
                return true;
            }
        }

        // The old pair loop of PlayerSeparationSystem.OnUpdate, verbatim (TrySlide through the same slide).
        static int OldLoop(NativeList<PlayerSeparation.Body> standing, int radiusMm, double stepMm, ref Slide slide)
        {
            int blocked = 0;
            for (int i = 0; i < standing.Length; i++)
            for (int j = i + 1; j < standing.Length; j++)
            {
                if (!PlayerSeparation.SlideOut(standing[i], standing[j], radiusMm, stepMm, out int which, out double2 away)) continue;
                if (slide.TrySlide(standing, which == 0 ? i : j, away)) continue;
                if (slide.TrySlide(standing, which == 0 ? j : i, -away)) continue;
                blocked++;
            }
            return blocked;
        }

        static PlayerSeparation.Body Body(int index, int x, int y, int z, uint since) =>
            new PlayerSeparation.Body { Entity = new Entity { Index = index, Version = 1 }, XMm = x, YMm = y, ZMm = z, StoodSince = since };

        static NativeList<PlayerSeparation.Body> Crowd(int seed, out bool wall, out int wallX)
        {
            var rng = new System.Random(seed);
            int n = 2 + rng.Next(120);
            int area = 800 + rng.Next(12000);   // from a tight clump to a spread town square
            int ox = rng.Next(-30000, 30000), oz = rng.Next(-30000, 30000);   // either side of zero (the grid's floor division)
            var list = new NativeList<PlayerSeparation.Body>(n, Allocator.Temp);
            for (int k = 0; k < n; k++)
            {
                int x = ox + rng.Next(area), z = oz + rng.Next(area);
                if (k > 0 && rng.Next(8) == 0) { x = list[rng.Next(k)].XMm; z = list[rng.Next(k)].ZMm; }   // right on top of another
                int y = rng.Next(6) == 0 ? rng.Next(4000) : 0;                                        // some on a ledge above
                list.Add(Body(1 + k * (1 + rng.Next(3)), x, y, z, (uint)rng.Next(5)));
            }
            wall = rng.Next(3) == 0;
            wallX = ox + area / 2;
            return list;
        }

        [Test]
        public void TheGrid_MakesTheOldLoopsSlides_InTheSameOrder_TickAfterTick()
        {
            int pairsSlid = 0, blockedSeen = 0;
            for (int seed = 1; seed <= 150; seed++)
            {
                var a = Crowd(seed, out bool wall, out int wallX);
                var b = new NativeList<PlayerSeparation.Body>(a.Length, Allocator.Temp);
                b.AddRange(a.AsArray());
                var oldSlide = new Slide { S = new Slider { HasWall = wall, WallX = wallX } };
                var newSlide = new Slide { S = new Slider { HasWall = wall, WallX = wallX } };
                for (int tick = 0; tick < 12; tick++)
                {
                    int blockedOld = OldLoop(a, Radius, Step, ref oldSlide);
                    int blockedNew = PlayerSeparation.ResolvePairs(b, Radius, Step, ref newSlide);
                    Assert.AreEqual(blockedOld, blockedNew, $"seed {seed} tick {tick}: blocked");
                    Assert.AreEqual(oldSlide.S.Log.Count, newSlide.S.Log.Count, $"seed {seed} tick {tick}: slides");
                    for (int e = 0; e < oldSlide.S.Log.Count; e++) Assert.AreEqual(oldSlide.S.Log[e], newSlide.S.Log[e], $"seed {seed} tick {tick}: slide {e}");
                    for (int k = 0; k < a.Length; k++)
                        Assert.IsTrue(a[k].XMm == b[k].XMm && a[k].ZMm == b[k].ZMm, $"seed {seed} tick {tick}: body {k} stands elsewhere");
                    blockedSeen += blockedOld;
                }
                pairsSlid += oldSlide.S.Log.Count;
                a.Dispose(); b.Dispose();
            }
            // the script must really exercise slides, carries, walls and blocks
            Assert.Greater(pairsSlid, 1000, "slides happened");
            Assert.Greater(blockedSeen, 0, "some pairs were blocked by the wall");
        }

        [Test]
        public void ASlideAcrossACellEdge_IsFollowed_ByTheGrid()
        {
            // body 1 (stopped last) slides away from body 2, to the right, across the cell edge and on toward bodies 3 and 4 over
            // the ticks; the old loop meets them from its new spot each tick, and so must the grid
            int cell = 2 * Radius;
            var a = new NativeList<PlayerSeparation.Body>(4, Allocator.Temp)
            {
                Body(1, cell - 20, 0, 0, 5),
                Body(2, cell - 60, 0, 0, 1),
                Body(3, cell + 700, 0, 0, 0),
                Body(4, 2 * cell + 600, 0, 0, 0),
            };
            var b = new NativeList<PlayerSeparation.Body>(4, Allocator.Temp); b.AddRange(a.AsArray());
            var s1 = new Slide { S = new Slider() }; var s2 = new Slide { S = new Slider() };
            for (int tick = 0; tick < 40; tick++)
            {
                Assert.AreEqual(OldLoop(a, Radius, Step, ref s1), PlayerSeparation.ResolvePairs(b, Radius, Step, ref s2));
                CollectionAssert.AreEqual(s1.S.Log, s2.S.Log, $"tick {tick}");
            }
            Assert.Greater(s1.S.Log.Count, 0);
            a.Dispose(); b.Dispose();
        }

        [Test]
        public void CellBorders_Corners_AndAStackOnOnePoint_PushExactlyAsTheOldLoop()
        {
            int cell = 2 * Radius;
            var cases = new List<NativeList<PlayerSeparation.Body>>();
            // pairs across a border (x, z, both, at zero, negative), just inside touching (2R - 1) and exactly touching (2R: no push)
            foreach (int border in new[] { 0, cell, -cell, 5 * cell, -7 * cell })
            foreach (int gap in new[] { 1, 2 * Radius - 1, 2 * Radius, 2 * Radius + 1 })
            {
                var l = new NativeList<PlayerSeparation.Body>(8, Allocator.Temp)
                {
                    Body(1, border - 1, 0, 3, 2), Body(2, border - 1 + gap, 0, 3, 1),                 // across an x border
                    Body(3, 40000, 0, border - 1, 0), Body(4, 40000, 0, border - 1 + gap, 3),         // across a z border
                    Body(5, border - 1, 0, border - 1, 1), Body(6, border - 1 + gap * 7 / 10, 0, border - 1 + gap * 7 / 10, 1),   // diagonal, across a corner
                };
                cases.Add(l);
            }
            // twelve players on one point, sitting on a cell corner, and twelve on one point at a negative corner beside a wall
            var stack = new NativeList<PlayerSeparation.Body>(24, Allocator.Temp);
            for (int k = 0; k < 12; k++) stack.Add(Body(10 + k * 3, cell, 0, cell, (uint)(k % 4)));
            for (int k = 0; k < 12; k++) stack.Add(Body(100 + k, -3 * cell, 0, -3 * cell, (uint)(k % 3)));
            cases.Add(stack);

            int slid = 0;
            for (int c = 0; c < cases.Count; c++)
            {
                var a = cases[c];
                var b = new NativeList<PlayerSeparation.Body>(a.Length, Allocator.Temp); b.AddRange(a.AsArray());
                bool wall = c == cases.Count - 1;
                var s1 = new Slide { S = new Slider { HasWall = wall, WallX = -3 * cell + 150 } };
                var s2 = new Slide { S = new Slider { HasWall = wall, WallX = -3 * cell + 150 } };
                for (int tick = 0; tick < 60; tick++)
                {
                    Assert.AreEqual(OldLoop(a, Radius, Step, ref s1), PlayerSeparation.ResolvePairs(b, Radius, Step, ref s2), $"case {c} tick {tick}: blocked");
                    CollectionAssert.AreEqual(s1.S.Log, s2.S.Log, $"case {c} tick {tick}: pushes");
                    for (int k = 0; k < a.Length; k++)
                        Assert.IsTrue(a[k].XMm == b[k].XMm && a[k].ZMm == b[k].ZMm, $"case {c} tick {tick}: body {k}");
                }
                slid += s1.S.Log.Count;
                a.Dispose(); b.Dispose();
            }
            Assert.Greater(slid, 100, "the edge cases really pushed");
        }

        [TestCase(50), TestCase(200), TestCase(1000)]
        public void Timing_OneTick_OldLoopVsGrid(int n)
        {
            // a crowd on a square of spots a little more than a personal space apart (a busy square), a tenth of them in pairs
            var rng = new System.Random(n);
            int side = (int)math.ceil(math.sqrt(n));
            var a = new NativeList<PlayerSeparation.Body>(n, Allocator.Temp);
            for (int k = 0; k < n; k++)
            {
                int x = (k % side) * 1000, z = (k / side) * 1000;
                if (rng.Next(10) == 0) x += 300;   // inside the neighbour's circle
                a.Add(Body(k + 1, x, 0, z, (uint)rng.Next(5)));
            }
            var b = new NativeList<PlayerSeparation.Body>(n, Allocator.Temp); b.AddRange(a.AsArray());
            var s1 = new Slide { S = new Slider() }; var s2 = new Slide { S = new Slider() };
            Assert.AreEqual(OldLoop(a, Radius, Step, ref s1), PlayerSeparation.ResolvePairs(b, Radius, Step, ref s2));   // warm up, same
            CollectionAssert.AreEqual(s1.S.Log, s2.S.Log);
            int ticks = n >= 1000 ? 5 : 30;
            var sw = Stopwatch.StartNew();
            for (int t = 0; t < ticks; t++) OldLoop(a, Radius, Step, ref s1);
            double oldMs = sw.Elapsed.TotalMilliseconds / ticks;
            sw.Restart();
            for (int t = 0; t < ticks; t++) PlayerSeparation.ResolvePairs(b, Radius, Step, ref s2);
            double newMs = sw.Elapsed.TotalMilliseconds / ticks;
            string line = $"[perf chunk 6] PlayerSeparation N={n}: old every pair {oldMs:F3} ms/tick, grid {newMs:F3} ms/tick";
            TestContext.WriteLine(line);
            UnityEngine.Debug.Log(line);
            CollectionAssert.AreEqual(s1.S.Log, s2.S.Log, "same slides while timed");
            if (n >= 1000) Assert.Less(newMs * 3, oldMs, line);
            a.Dispose(); b.Dispose();
        }

        // ---- perf round 4: the jobs (who is too close, in parallel over the cells; their groups) + the checked slides ----

        /// <summary>One tick the server's way: the Burst jobs find the groups with a pair too close, the main thread slides those.</summary>
        static int JobTick(NativeList<PlayerSeparation.Body> standing, double slackMm, ref Slide slide, out int active, out bool fellBack)
        {
            var list = new NativeList<int>(16, Allocator.TempJob);
            var copy = new NativeList<PlayerSeparation.Body>(standing.Length, Allocator.TempJob);
            copy.AddRange(standing.AsArray());
            PlayerSeparation.ScheduleFindActive(copy, Radius, slackMm, list, Allocator.TempJob, default).Complete();
            copy.Dispose();
            active = list.Length;
            slide.S.Mark();
            int blocked = PlayerSeparation.ResolveActive(standing, list, Radius, Step, slackMm, ref slide, out fellBack);
            list.Dispose();
            return blocked;
        }

        /// <summary>The jobs + checked slides against the old loop, over the random crowds of the grid test, with the server's slack and
        /// with a slack so small that groups are run again over everyone (the undo), tick after tick: the same slides, the same order,
        /// the same blocked pairs, the same carries and the same final spots.</summary>
        [TestCase(2.0 * Radius), TestCase(3.0)]
        public void TheJobs_MakeTheOldLoopsSlides_InTheSameOrder_TickAfterTick(double slackMm)
        {
            int pairsSlid = 0, blockedSeen = 0, partial = 0, untouched = 0, undone = 0;
            for (int seed = 1; seed <= 150; seed++)
            {
                var a = Crowd(seed, out bool wall, out int wallX);
                var b = new NativeList<PlayerSeparation.Body>(a.Length, Allocator.Temp);
                b.AddRange(a.AsArray());
                var oldSlide = new Slide { S = new Slider { HasWall = wall, WallX = wallX } };
                var newSlide = new Slide { S = new Slider { HasWall = wall, WallX = wallX } };
                for (int tick = 0; tick < 12; tick++)
                {
                    int blockedOld = OldLoop(a, Radius, Step, ref oldSlide);
                    int blockedNew = JobTick(b, slackMm, ref newSlide, out int active, out bool fellBack);
                    if (active > 0 && active < b.Length && !fellBack) partial++;
                    if (active == 0) untouched++;
                    if (fellBack) undone++;
                    Assert.AreEqual(blockedOld, blockedNew, $"seed {seed} tick {tick}: blocked");
                    Assert.AreEqual(oldSlide.S.Log.Count, newSlide.S.Log.Count, $"seed {seed} tick {tick}: slides");
                    for (int e = 0; e < oldSlide.S.Log.Count; e++) Assert.AreEqual(oldSlide.S.Log[e], newSlide.S.Log[e], $"seed {seed} tick {tick}: slide {e}");
                    for (int k = 0; k < a.Length; k++)
                        Assert.IsTrue(a[k].XMm == b[k].XMm && a[k].ZMm == b[k].ZMm, $"seed {seed} tick {tick}: body {k} stands elsewhere");
                    CollectionAssert.AreEquivalent(oldSlide.S.Carry, newSlide.S.Carry, $"seed {seed} tick {tick}: carries");
                    blockedSeen += blockedOld;
                }
                pairsSlid += oldSlide.S.Log.Count;
                a.Dispose(); b.Dispose();
            }
            TestContext.WriteLine($"slack {slackMm}: {pairsSlid} slides, {blockedSeen} blocked, ticks: {partial} part of the crowd, {untouched} nobody, {undone} run again");
            Assert.Greater(pairsSlid, 1000, "slides happened");
            Assert.Greater(blockedSeen, 0, "some pairs were blocked by the wall");
            Assert.Greater(partial, 20, "ticks where only some groups went to the checked slides");
            Assert.Greater(untouched, 20, "ticks where everyone stood apart");
            if (slackMm < 10) Assert.Greater(undone, 20, "the tiny slack made groups run again over everyone");
        }

        /// <summary>A crowd that stands apart sends nobody to the main thread; a pair in it sends only its group.</summary>
        [Test]
        public void ACrowdStandingApart_SendsNobody_APairSendsOnlyItsGroup()
        {
            var a = new NativeList<PlayerSeparation.Body>(64, Allocator.Temp);
            for (int k = 0; k < 64; k++) a.Add(Body(k + 1, (k % 8) * 2000, 0, (k / 8) * 2000, 0));   // 2 m apart, circles of 0.4 m
            var slide = new Slide { S = new Slider() };
            Assert.AreEqual(0, JobTick(a, 2.0 * Radius, ref slide, out int active, out _));
            Assert.AreEqual(0, active);
            Assert.AreEqual(0, slide.S.Log.Count);
            var moved = a[9]; moved.XMm = a[10].XMm - 300; a[9] = moved;   // body 10 steps into body 11's circle
            JobTick(a, 2.0 * Radius, ref slide, out active, out bool fellBack);
            Assert.AreEqual(2, active, "only the two of them");
            Assert.IsFalse(fellBack);
            Assert.Greater(slide.S.Log.Count, 0);
            a.Dispose();
        }

        /// <summary>Times one tick of the grid (all on the main thread) against the jobs + checked slides for a crowd standing apart
        /// with a tenth in pairs. Printed only: the proof that counts is the built server under bots.</summary>
        [TestCase(200), TestCase(1000)]
        public void Timing_OneTick_GridVsJobs(int n)
        {
            var rng = new System.Random(n);
            int side = (int)math.ceil(math.sqrt(n));
            var a = new NativeList<PlayerSeparation.Body>(n, Allocator.Temp);
            for (int k = 0; k < n; k++)
            {
                int x = (k % side) * 1000, z = (k / side) * 1000;
                if (rng.Next(10) == 0) x += 300;
                a.Add(Body(k + 1, x, 0, z, (uint)rng.Next(5)));
            }
            var b = new NativeList<PlayerSeparation.Body>(n, Allocator.Temp); b.AddRange(a.AsArray());
            var s1 = new Slide { S = new Slider() }; var s2 = new Slide { S = new Slider() };
            PlayerSeparation.ResolvePairs(a, Radius, Step, ref s1);
            JobTick(b, 2.0 * Radius, ref s2, out _, out _);
            CollectionAssert.AreEqual(s1.S.Log, s2.S.Log);
            var sw = Stopwatch.StartNew();
            for (int t = 0; t < 30; t++) PlayerSeparation.ResolvePairs(a, Radius, Step, ref s1);
            double gridMs = sw.Elapsed.TotalMilliseconds / 30;
            sw.Restart();
            for (int t = 0; t < 30; t++) JobTick(b, 2.0 * Radius, ref s2, out _, out _);
            double jobMs = sw.Elapsed.TotalMilliseconds / 30;
            string line = $"[perf round 4] PlayerSeparation N={n}: grid {gridMs:F3} ms/tick, jobs {jobMs:F3} ms/tick";
            TestContext.WriteLine(line);
            UnityEngine.Debug.Log(line);
            CollectionAssert.AreEqual(s1.S.Log, s2.S.Log, "same slides while timed");
            a.Dispose(); b.Dispose();
        }
    }
}
