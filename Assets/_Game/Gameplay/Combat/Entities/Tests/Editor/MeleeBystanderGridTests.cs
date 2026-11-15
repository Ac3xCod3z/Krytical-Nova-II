using System.Collections.Generic;
using System.Diagnostics;
using KryticalNova.Player.Entities;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using Unity.NetCode;

namespace KryticalNova.Combat.Entities.Tests
{
    /// <summary>
    /// Perf clean-ups round 2, chunk 7: <see cref="MeleeResolveServerSystem"/> gathers a swing's bystanders from
    /// <see cref="MeleeBystanderGrid"/> (filled once per tick) instead of from every body. The same swings over the same crowd strike
    /// the same bodies in the same order, old way and new; the time is printed at N = 50 / 200 / 1000.
    /// </summary>
    public sealed class MeleeBystanderGridTests
    {
        const uint Now = 10_000;

        struct Crowd
        {
            public PositionHistory[] Rings;
            public HitProxy[] Proxies;
        }

        // N bodies at a crowd's density (about one per 3 m x 3 m), each with a full ring of walking samples; every 10th body
        // teleported inside its ring (its old place far away), every 7th a dummy-sized capsule.
        // circle: the bodies stand on a circle round an empty middle instead of in a square; every 9th body stands exactly on a cell edge.
        static Crowd MakeCrowd(int n, uint seed, bool circle = false)
        {
            var rng = new Random(seed);
            int side = (int)(math.sqrt(n) * 3000f);
            var c = new Crowd { Rings = new PositionHistory[n], Proxies = new HitProxy[n] };
            for (int b = 0; b < n; b++)
            {
                int3 p = new int3(rng.NextInt(-side / 2, side / 2), rng.NextInt(-300, 300), rng.NextInt(-side / 2, side / 2));
                if (circle)
                {
                    float a = rng.NextFloat(0f, 2f * math.PI);
                    float radius = n * 3000f / (2f * math.PI) + rng.NextInt(-1500, 1500);
                    p = new int3((int)(math.cos(a) * radius), p.y, (int)(math.sin(a) * radius));
                }
                if (b % 9 == 4) p.x = MeleeBystanderGrid.CellMm * rng.NextInt(-3, 4);
                int3 step = new int3(rng.NextInt(-300, 300), 0, rng.NextInt(-300, 300));
                var ring = new PositionHistory();
                int samples = b % 13 == 5 ? 0 : (b % 11 == 3 ? 3 : PositionHistoryRules.Ticks);   // some empty, some just arrived
                for (int k = 0; k < samples; k++)
                {
                    int3 at = p + step * (k - samples);
                    if (b % 10 == 0 && k < samples / 2) at += new int3(25_000, 0, -18_000);   // was far away, then teleported here
                    PositionHistoryRules.Record(ref ring, new NetworkTick(Now - (uint)(samples - 1 - k)), at);
                }
                c.Rings[b] = ring;
                c.Proxies[b] = b % 7 == 0 ? new HitProxy { RadiusMm = 900, HeightMm = 2400 } : HitProxy.Player;
            }
            return c;
        }

        static MeleeTiming Timing(int reachMm, int arcHalfCdeg = 6000) => new MeleeTiming { ReachMm = reachMm, ArcHalfAngleCdeg = arcHalfCdeg };

        static int3 FeetNow(in PositionHistory ring) => ring.Samples.Length > 0 ? ring.Samples[ring.Samples.Length - 1].FeetMm : int3.zero;

        // The old way: every other body of the world is a candidate.
        static void OldSwing(in Crowd c, int attacker, in MeleeTiming t, int yaw, NetworkTick now, NetworkTick seen, List<int> struck)
        {
            int3 me = FeetNow(c.Rings[attacker]);
            var picks = new List<int>(c.Rings.Length);
            for (int i = 0; i < c.Rings.Length; i++) if (i != attacker) picks.Add(i);
            Resolve(c, picks, me, t, yaw, now, seen, struck);
        }

        // The new way: only the bodies the grid gathers near the attacker.
        static void NewSwing(in Crowd c, MeleeBystanderGrid grid, NativeList<int> gathered, int attacker, in MeleeTiming t, int yaw,
            NetworkTick now, NetworkTick seen, List<int> struck)
        {
            int3 me = FeetNow(c.Rings[attacker]);
            grid.Gather(me, t.ReachMm, gathered);
            var picks = new List<int>(gathered.Length);
            for (int i = 0; i < gathered.Length; i++) if (gathered[i] != attacker) picks.Add(gathered[i]);
            Resolve(c, picks, me, t, yaw, now, seen, struck);
        }

        static void Resolve(in Crowd c, List<int> picks, int3 me, in MeleeTiming t, int yaw, NetworkTick now, NetworkTick seen, List<int> struck)
        {
            var rings = new NativeArray<PositionHistory>(picks.Count, Allocator.Temp);
            var proxies = new NativeArray<HitProxy>(picks.Count, Allocator.Temp);
            for (int i = 0; i < picks.Count; i++) { rings[i] = c.Rings[picks[i]]; proxies[i] = c.Proxies[picks[i]]; }
            var hit = new NativeList<int>(Allocator.Temp);
            MeleeRules.Resolve(t, me, yaw, now, seen, default, me + new int3(999_999, 0, 0), HitProxy.Player, rings, proxies, HitProxy.Player,
                out _, ref hit);
            struck.Clear();
            for (int i = 0; i < hit.Length; i++) struck.Add(picks[hit[i]]);
            hit.Dispose(); rings.Dispose(); proxies.Dispose();
        }

        static MeleeBystanderGrid Fill(in Crowd c)
        {
            var grid = new MeleeBystanderGrid(c.Rings.Length);
            for (int i = 0; i < c.Rings.Length; i++) grid.Add(c.Rings[i], c.Proxies[i]);
            return grid;
        }

        [TestCase(50, 1u, false)]
        [TestCase(200, 2u, false)]
        [TestCase(1000, 3u, false)]
        [TestCase(50, 4u, true)]
        [TestCase(200, 5u, true)]
        [TestCase(1000, 6u, true)]
        public void EverySwing_StrikesTheSameBodies_InTheSameOrder(int n, uint seed, bool circle)
        {
            Crowd c = MakeCrowd(n, seed, circle);
            var rng = new Random(seed * 7919u);
            var now = new NetworkTick(Now);
            int struckTotal = 0;
            using (var grid = Fill(c))
            using (var gathered = new NativeList<int>(Allocator.Temp))
            {
                var oldHits = new List<int>(); var newHits = new List<int>();
                for (int s = 0; s < 400; s++)
                {
                    int attacker = rng.NextInt(0, n);
                    // narrow to a full circle (a wide-arc skill: no target cap, everyone in reach)
                    var t = Timing(rng.NextInt(1500, 3200), s % 4 == 0 ? 18000 : rng.NextInt(2000, 9000));
                    int yaw = rng.NextInt(0, 36000);
                    var seen = new NetworkTick(Now - (uint)rng.NextInt(0, 6));
                    OldSwing(c, attacker, t, yaw, now, seen, oldHits);
                    NewSwing(c, grid, gathered, attacker, t, yaw, now, seen, newHits);
                    CollectionAssert.AreEqual(oldHits, newHits, $"swing {s} by body {attacker}");
                    struckTotal += oldHits.Count;
                }
            }
            Assert.Greater(struckTotal, 0, "the crowd is dense enough that swings strike bystanders");
        }

        [Test]
        public void ABodyThatTeleportedHere_IsStillFound_WhereItStoodBefore()
        {
            var ring = new PositionHistory();
            for (int k = 0; k < 8; k++) PositionHistoryRules.Record(ref ring, new NetworkTick(Now - 15 + (uint)k), new int3(60_000, 0, 60_000));
            for (int k = 8; k < 16; k++) PositionHistoryRules.Record(ref ring, new NetworkTick(Now - 15 + (uint)k), new int3(0, 0, 1_000));
            using (var grid = new MeleeBystanderGrid())
            using (var gathered = new NativeList<int>(Allocator.Temp))
            {
                grid.Add(ring, HitProxy.Player);
                grid.Gather(new int3(60_000, 0, 62_500), 2_000, gathered);   // where it stood 12 ticks ago, at the reach + radius edge
                Assert.AreEqual(1, gathered.Length);
                grid.Gather(new int3(0, 0, 0), 2_000, gathered);              // where it stands now
                Assert.AreEqual(1, gathered.Length);
                // filed by the box of every place in its ring (a candidate; the rewound check decides the hit), so a point inside that box
                // (30 m, 30 m) may be gathered; a point outside the box never is
                grid.Gather(new int3(90_000, 0, 90_000), 2_000, gathered);    // outside every place it has been
                Assert.AreEqual(0, gathered.Length);
            }
        }

        [Test]
        public void AnEmptyRing_IsNeverGathered()
        {
            using (var grid = new MeleeBystanderGrid())
            using (var gathered = new NativeList<int>(Allocator.Temp))
            {
                grid.Add(new PositionHistory(), HitProxy.Player);
                grid.Gather(int3.zero, 5_000, gathered);
                Assert.AreEqual(0, gathered.Length);
            }
        }

        [Test]
        public void Timing_OldAndNew_AtN50_200_1000()
        {
            var now = new NetworkTick(Now);
            foreach (int n in new[] { 50, 200, 1000 })
            {
                Crowd c = MakeCrowd(n, (uint)n);
                const int swings = 50;   // due swings in one tick (the spec's measure)
                var t = Timing(2500);
                var hits = new List<int>();
                var rng = new Random(11u);
                var attackers = new int[swings]; var yaws = new int[swings];
                for (int s = 0; s < swings; s++) { attackers[s] = rng.NextInt(0, n); yaws[s] = rng.NextInt(0, 36000); }

                OldSwing(c, 0, t, 0, now, now, hits);   // warm up
                var sw = Stopwatch.StartNew();
                for (int s = 0; s < swings; s++) OldSwing(c, attackers[s], t, yaws[s], now, now, hits);
                double oldMs = sw.Elapsed.TotalMilliseconds;

                sw.Restart();
                using (var grid = Fill(c))
                using (var gathered = new NativeList<int>(Allocator.Temp))
                    for (int s = 0; s < swings; s++) NewSwing(c, grid, gathered, attackers[s], t, yaws[s], now, now, hits);
                double newMs = sw.Elapsed.TotalMilliseconds;

                UnityEngine.Debug.Log($"[perf chunk 7] melee bystanders N={n}, {swings} swings in one tick: old {oldMs:F3} ms, new {newMs:F3} ms " +
                    $"(x{oldMs / math.max(newMs, 1e-6):F1}); pairs tested old {(long)swings * (n - 1)}");
            }
            Assert.Pass();
        }
    }
}
