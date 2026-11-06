using System;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace KryticalNova.Brains.Tests
{
    /// <summary>M5.1 (implementation plan s4): the Brain's clock, sense grid, scores, dice and allocation-free tick.</summary>
    public sealed class ThinkClockTests
    {
        [Test]
        public void Stagger_SpreadsThinksEvenly()
        {
            using var w = new BrainTestWorld();
            var brains = new Entity[300];
            for (int i = 0; i < brains.Length; i++) brains[i] = w.Brain(new float3(0f, 0f, 0f), (uint)i);
            w.SetPlayers(new float3(30f, 0f, 0f));   // 30 m: tier 1, every 3 ticks
            w.Step(6);
            for (int t = 0; t < 30; t++)
            {
                w.Step();
                int thinking = 0;
                foreach (Entity b in brains) if (w.Clock(b).ThinkNow != 0) thinking++;
                Assert.That(thinking, Is.InRange(99, 101), $"tick {w.Tick}");
            }
            Assert.AreEqual(1, w.Clock(brains[0]).Tier);
        }

        [Test]
        public void TierFromDistance()
        {
            using var w = new BrainTestWorld();
            BrainConfig c = w.Config;
            Assert.AreEqual(0, BrainRules.TierFor(10f * 10f, false, c));
            Assert.AreEqual(1, BrainRules.TierFor(30f * 30f, false, c));
            Assert.AreEqual(2, BrainRules.TierFor(100f * 100f, false, c));
            Assert.AreEqual(3, BrainRules.TierFor(200f * 200f, false, c));
            Assert.AreEqual(0, BrainRules.TierFor(200f * 200f, true, c), "fighting: tier 0 at any distance");
        }

        [Test]
        public void TierDown_WaitsHysteresis_AndUpIsImmediate()
        {
            using var w = new BrainTestWorld();
            Entity b = w.Brain(float3.zero, 0);
            w.SetPlayers(new float3(5f, 0f, 0f));
            w.Step(3);
            Assert.AreEqual(0, w.Clock(b).Tier);
            w.SetPlayers(new float3(30f, 0f, 0f));
            int hysteresis = 2 * BrainTestWorld.TickRate;
            w.Step(hysteresis - 1);
            Assert.AreEqual(0, w.Clock(b).Tier, "still tier 0 before 2 s");
            w.Step(2);
            Assert.AreEqual(1, w.Clock(b).Tier, "tier 1 after 2 s out of range");
            w.SetPlayers(new float3(5f, 0f, 0f));
            w.Step();
            Assert.AreEqual(0, w.Clock(b).Tier, "back up at once");
        }

        [Test]
        public void EventBumpsToTier0_SameTick()
        {
            using var w = new BrainTestWorld();
            Entity b = w.Brain(float3.zero, 1);
            w.SetPlayers(new float3(100f, 0f, 0f));   // tier 2: every 15 ticks
            w.Step(20);
            Assert.AreEqual(2, w.Clock(b).Tier);
            uint at = w.Tick + 1;
            w.Em.GetBuffer<BrainEvent>(b).Add(new BrainEvent { Kind = BrainEventKind.Damaged, GhostId = 100, Amount = 5f, Tick = at });
            w.Step();
            Assert.AreEqual(at, w.Tick);
            Assert.AreEqual(1, w.Clock(b).ThinkNow, "it thinks on the tick of the event");
            Assert.AreEqual(0, w.Clock(b).Tier);
            w.Step(10);
            Assert.AreEqual(0, w.Clock(b).Tier, "a hit keeps it engaged at tier 0");
        }
    }

    public sealed class SenseGridTests
    {
        [Test]
        public void Query_FindsAllWithinRadius()
        {
            using var w = new BrainTestWorld();
            var rnd = new Unity.Mathematics.Random(7);
            var feet = new float3[500];
            for (int i = 0; i < feet.Length; i++) feet[i] = new float3(rnd.NextFloat(-300f, 300f), rnd.NextFloat(-5f, 5f), rnd.NextFloat(-300f, 300f));
            w.SetPlayers(feet);
            w.Step();
            SenseWorld sense = w.Sense;
            float cell = w.Config.PlayerCell;
            using var found = new NativeList<int>(64, Allocator.Temp);
            for (int q = 0; q < 1000; q++)
            {
                float3 center = new float3(rnd.NextFloat(-320f, 320f), 0f, rnd.NextFloat(-320f, 320f));
                float radius = rnd.NextFloat(1f, 60f);
                SenseQueries.Within(sense, center, radius, cell, found);
                var expected = new System.Collections.Generic.HashSet<int>();
                for (int i = 0; i < feet.Length; i++) if (SenseQueries.FlatSq(feet[i], center) <= radius * radius) expected.Add(i);
                var got = new System.Collections.Generic.HashSet<int>();
                foreach (int i in found) got.Add(i);
                Assert.IsTrue(expected.SetEquals(got), $"query {q}: {got.Count} found, {expected.Count} expected");
            }
        }
    }

    public sealed class ScoreTests
    {
        [Test]
        public void Curves_MatchFormulas()
        {
            var linear = new ScoreCurve { Shape = ScoreShape.Linear, M = 0.8f, B = 0.1f, C = 0.05f };
            var quad = new ScoreCurve { Shape = ScoreShape.Quadratic, M = 1f, K = 2f, B = 0f, C = 0f };
            var logistic = new ScoreCurve { Shape = ScoreShape.Logistic, M = 1f, K = 1f, B = 0f, C = 0.5f };
            var logit = new ScoreCurve { Shape = ScoreShape.Logit, M = 1f, B = 0f, C = 0f };
            for (int i = 0; i <= 10; i++)
            {
                float x = i / 10f;
                Assert.AreEqual(Mathf.Clamp01(0.8f * (x - 0.05f) + 0.1f), Score.Evaluate(linear, x), 1e-5f, "linear " + x);
                Assert.AreEqual(Mathf.Clamp01(x * x), Score.Evaluate(quad, x), 1e-5f, "quadratic " + x);
                Assert.AreEqual(Mathf.Clamp01(1f / (1f + Mathf.Pow(1000f * (float)Math.E, 0.5f - x))), Score.Evaluate(logistic, x), 1e-5f, "logistic " + x);
                float z = Mathf.Clamp(x, 1e-4f, 1f - 1e-4f);
                Assert.AreEqual(Mathf.Clamp01(Mathf.Log10(z / (1f - z)) / 5f + 0.5f), Score.Evaluate(logit, x), 1e-5f, "logit " + x);
            }
            Assert.AreEqual(0.5f, Score.Evaluate(logistic, 0.5f), 1e-5f, "the logistic's middle");
        }

        [Test]
        public void EarlyOut_SameWinner()
        {
            var rnd = new Unity.Mathematics.Random(11);
            for (int round = 0; round < 200; round++)
            {
                int options = 6, considerations = 4;
                var scores = new float[options, considerations];
                for (int o = 0; o < options; o++) for (int k = 0; k < considerations; k++) scores[o, k] = rnd.NextFloat();
                int fullWinner = -1, quickWinner = -1;
                float fullBest = -1f, quickBest = -1f;
                for (int o = 0; o < options; o++)
                {
                    float product = 1f;
                    for (int k = 0; k < considerations; k++) product *= scores[o, k];
                    float s = Score.Compensate(product, considerations);
                    if (s > fullBest) { fullBest = s; fullWinner = o; }

                    float running = 1f;
                    bool cut = false;
                    for (int k = 0; k < considerations; k++)
                    {
                        running *= scores[o, k];
                        if (!Score.CanStillBeat(running, considerations, quickBest)) { cut = true; break; }
                    }
                    if (cut) continue;
                    float q = Score.Compensate(running, considerations);
                    if (q > quickBest) { quickBest = q; quickWinner = o; }
                }
                Assert.AreEqual(fullWinner, quickWinner, "round " + round);
            }
        }
    }

    public sealed class BrainDeterminismTests
    {
        static (BrainClock[] clocks, uint[] dice) Run(uint seed)
        {
            using var w = new BrainTestWorld(seed);
            var brains = new Entity[50];
            for (int i = 0; i < brains.Length; i++) brains[i] = w.Brain(new float3(i * 3f, 0f, 0f), (uint)(i + 1));
            for (int t = 0; t < 1000; t++)
            {
                w.SetPlayers(new float3(math.sin(t * 0.01f) * 150f, 0f, 0f), new float3(t * 0.1f, 0f, 20f));
                if (t % 97 == 0) w.Em.GetBuffer<BrainEvent>(brains[t % brains.Length]).Add(new BrainEvent { Kind = BrainEventKind.Damaged, Tick = w.Tick + 1 });
                w.Step();
            }
            var clocks = new BrainClock[brains.Length];
            var dice = new uint[brains.Length];
            for (int i = 0; i < brains.Length; i++)
            {
                clocks[i] = w.Clock(brains[i]);
                var rng = w.Em.GetComponentData<BrainDice>(brains[i]).Rng;
                dice[i] = rng.NextUInt();
            }
            return (clocks, dice);
        }

        [Test]
        public void SameSeed_SameState_1000Ticks()
        {
            var a = Run(5);
            var b = Run(5);
            CollectionAssert.AreEqual(a.clocks, b.clocks);
            CollectionAssert.AreEqual(a.dice, b.dice);
            CollectionAssert.AreNotEqual(a.dice, Run(6).dice, "another seed, other dice");
        }
    }

    public sealed class BrainGcTests
    {
        [Test]
        public void NoAllocs_1000Ticks()
        {
            using var w = new BrainTestWorld();
            for (int i = 0; i < 200; i++) w.Brain(new float3(i, 0f, 0f), (uint)i);
            w.SetPlayers(new float3(10f, 0f, 0f), new float3(60f, 0f, 0f), new float3(150f, 0f, 0f));
            w.Step(100);   // warm-up
            long before = GC.GetAllocatedBytesForCurrentThread();
            w.Step(1000);
            Assert.AreEqual(0, GC.GetAllocatedBytesForCurrentThread() - before, "the Brain's tick allocates nothing");
        }
    }
}
