using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using Unity.NetCode;

namespace KryticalNova.Networking.Entities.Tests
{
    /// <summary>
    /// Perf round 3 (R3-3 fill cost): the fill runs one connection per job index on the workers and merges in connection order. It must
    /// give the same answer as the plain rule written out by hand here: the same pairs, each once, the same "became relevant" pairs, the
    /// same most-for-one. Checked on 200 players clumped (the case that cost ~22 ms a fill), with Hostiles, always-relevant pairs that
    /// overlap own and near ghosts, players on the leave margin, a connection with no body, over several fills (hysteresis).
    /// </summary>
    public class RelevancyFillSameAnswerTests
    {
        static readonly RelevancyConfig Config = new RelevancyConfig { PlayerRadius = 150f, ObjectRadius = 150f, LeaveMargin = 16f, FillIntervalTicks = 6 };

        // the rule, one connection after another, with plain collections
        static (HashSet<RelevantGhostForConnection> pairs, HashSet<RelevantGhostForConnection> became, int most) Reference(
            List<RelevancyGhost> ghosts, List<int> viewers, List<RelevancyAlways> always, HashSet<RelevantGhostForConnection> old)
        {
            var pairs = new HashSet<RelevantGhostForConnection>();
            var became = new HashSet<RelevantGhostForConnection>();
            int most = 0;
            foreach (int id in viewers)
            {
                var mine = new HashSet<int>();
                float2? place = null;
                foreach (RelevancyGhost g in ghosts)
                    if (g.Owner == id) { place ??= g.At; mine.Add(g.GhostId); }
                foreach (RelevancyAlways a in always)
                    if (a.NetworkId == id && a.GhostId != 0) mine.Add(a.GhostId);
                if (place.HasValue)
                    foreach (RelevancyGhost g in ghosts)
                    {
                        if (g.Owner == id) continue;
                        float radius = g.Owner != 0 ? Config.PlayerRadius : Config.ObjectRadius;
                        bool before = old.Contains(new RelevantGhostForConnection(id, g.GhostId));
                        if (RelevancyRules.Sees(math.distancesq(place.Value, g.At), radius, Config.LeaveMargin, before)) mine.Add(g.GhostId);
                    }
                foreach (int ghost in mine)
                {
                    var pair = new RelevantGhostForConnection(id, ghost);
                    pairs.Add(pair);
                    if (!old.Contains(pair)) became.Add(pair);
                }
                most = math.max(most, mine.Count);
            }
            return (pairs, became, most);
        }

        [Test]
        public void TheParallelFill_GivesThePlainRulesAnswer_OverSeveralFills_WithAClumpOfTwoHundred()
        {
            var rng = new Random(20261115);
            var ghosts = new List<RelevancyGhost>();
            var viewers = new List<int>();
            int nextGhost = 1;
            for (int p = 1; p <= 200; p++)   // 200 players within 40 m of the spawn
            {
                viewers.Add(p);
                ghosts.Add(new RelevancyGhost { GhostId = nextGhost++, Owner = p, At = rng.NextFloat2(new float2(-40f), new float2(40f)) });
            }
            for (int h = 0; h < 60; h++)   // Hostiles near and far, some on the edge band
                ghosts.Add(new RelevancyGhost { GhostId = nextGhost++, Owner = 0, At = rng.NextFloat2(new float2(-400f), new float2(400f)) });
            for (int e = 0; e < 20; e++)   // players far off, around 150..170 m
                ghosts.Add(new RelevancyGhost { GhostId = nextGhost++, Owner = 300 + e, At = new float2(150f + e, 0f) });
            viewers.Add(999);   // signed in, no body yet: only its always-relevant pairs
            var always = new List<RelevancyAlways>
            {
                new RelevancyAlways { NetworkId = 3, GhostId = 205 },   // a far Hostile
                new RelevancyAlways { NetworkId = 3, GhostId = 4 },     // a near player as well (given once)
                new RelevancyAlways { NetworkId = 7, GhostId = 7 },     // its own ghost as well (given once)
                new RelevancyAlways { NetworkId = 999, GhostId = 1 },
                new RelevancyAlways { NetworkId = 5, GhostId = 0 },     // no ghost: ignored
            };

            using var nGhosts = new NativeList<RelevancyGhost>(ghosts.Count, Allocator.Persistent);
            using var nViewers = new NativeList<int>(viewers.Count, Allocator.Persistent);
            using var nAlways = new NativeList<RelevancyAlways>(always.Count, Allocator.Persistent);
            using var set = new NativeParallelHashMap<RelevantGhostForConnection, int>(16, Allocator.Persistent);
            using var pairs = new NativeList<RelevantGhostForConnection>(16, Allocator.Persistent);
            using var became = new NativeList<RelevantGhostForConnection>(16, Allocator.Persistent);
            foreach (int v in viewers) nViewers.Add(v);
            foreach (RelevancyAlways a in always) nAlways.Add(a);

            var old = new HashSet<RelevantGhostForConnection>();
            for (int fill = 0; fill < 4; fill++)
            {
                if (fill > 0)   // everyone moves a little; the far players cross the edge band both ways
                    for (int i = 0; i < ghosts.Count; i++)
                    {
                        RelevancyGhost g = ghosts[i];
                        g.At += rng.NextFloat2(new float2(-12f), new float2(12f));
                        ghosts[i] = g;
                    }
                nGhosts.Clear();
                foreach (RelevancyGhost g in ghosts) nGhosts.Add(g);

                var expected = Reference(ghosts, viewers, always, old);
                int most = RelevancyFill.Build(nGhosts.AsArray(), nViewers.AsArray(), nAlways.AsArray(), set, Config, pairs, became);
                RelevancyFill.Write(set, pairs);

                var got = new HashSet<RelevantGhostForConnection>();
                for (int i = 0; i < pairs.Length; i++) Assert.IsTrue(got.Add(pairs[i]), $"fill {fill}: a pair given twice: {pairs[i].Connection}/{pairs[i].Ghost}");
                var gotBecame = new HashSet<RelevantGhostForConnection>();
                for (int i = 0; i < became.Length; i++) gotBecame.Add(became[i]);
                Assert.IsTrue(expected.pairs.SetEquals(got), $"fill {fill}: the pairs differ ({got.Count} vs {expected.pairs.Count})");
                Assert.IsTrue(expected.became.SetEquals(gotBecame), $"fill {fill}: the 'became relevant' pairs differ ({gotBecame.Count} vs {expected.became.Count})");
                Assert.AreEqual(expected.most, most, $"fill {fill}: the most for one connection");
                Assert.AreEqual(got.Count, set.Count(), "the package's set holds exactly the pairs");
                if (fill == 0) Assert.Greater(got.Count, 200 * 200, "the clump: everyone sees everyone");
                old = got;
            }
        }
    }
}
