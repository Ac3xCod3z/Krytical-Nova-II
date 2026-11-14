using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace KryticalNova.World.Tests
{
    /// <summary>Swings move the grass (owner's day list 13 Nov 2026, item 20): the push pool, its spring and its limits.</summary>
    public sealed class FoliagePushTests
    {
        private const int High = 2, Low = 0;
        private FoliagePushSettings settings;
        private FoliagePushSettings before;
        private readonly Vector4[] published = new Vector4[FoliagePushes.PublishedVectors];

        [SetUp]
        public void SetUp()
        {
            before = FoliagePushes.Settings;
            settings = ScriptableObject.CreateInstance<FoliagePushSettings>();
            FoliagePushes.Settings = settings;
            FoliagePushes.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            FoliagePushes.Clear();
            FoliagePushes.Settings = before;
            Object.DestroyImmediate(settings);
        }

        private static bool Add(FoliagePushKind kind, float now, bool local = false, Vector3 at = default, float scale = 1f, float delay = 0f, int tier = High)
            => FoliagePushes.Add(kind, at, Vector3.forward, scale, 0, local, delay, now, Vector3.zero, tier);

        [Test]
        public void PoolCapAndReplace()
        {
            for (int i = 0; i < FoliagePushes.MaxPushes; i++) Assert.IsTrue(Add(FoliagePushKind.Arc, i * 0.01f), $"push {i + 1}");
            Assert.AreEqual(FoliagePushes.MaxPushes, FoliagePushes.Alive);
            // a ninth takes the place of the one with the least left (the oldest), never grows the pool
            Assert.IsTrue(Add(FoliagePushKind.Wave, 0.1f));
            Assert.AreEqual(FoliagePushes.MaxPushes, FoliagePushes.Alive);

            // this machine's player's pushes outlive others': a pool of them turns another player's away
            FoliagePushes.Clear();
            for (int i = 0; i < FoliagePushes.MaxPushes; i++) Assert.IsTrue(Add(FoliagePushKind.Arc, 0f, local: true));
            Assert.IsFalse(Add(FoliagePushKind.Finisher, 0.05f, local: false), "another player's push must not replace this player's");
            Assert.IsTrue(Add(FoliagePushKind.Finisher, 0.05f, local: true), "this player's newest replaces this player's oldest");
        }

        [Test]
        public void WorldEffectsLowHoldsFour()
        {
            for (int i = 0; i < 4; i++) Assert.IsTrue(Add(FoliagePushKind.Arc, 0f, tier: Low));
            Assert.IsTrue(Add(FoliagePushKind.Arc, 0.01f, tier: Low));
            Assert.AreEqual(4, FoliagePushes.Alive, "World Effects Low keeps 4 alive at once");
            Assert.AreEqual(4, settings.PushesAt(Low));
            Assert.AreEqual(FoliagePushes.MaxPushes, settings.PushesAt(High));
        }

        [Test]
        public void CullAndExpire()
        {
            Assert.IsFalse(Add(FoliagePushKind.Arc, 0f, at: new Vector3(settings.cullDistance + 1f, 0f, 0f)), "past the cut-off");
            Assert.IsTrue(Add(FoliagePushKind.Arc, 0f, at: new Vector3(settings.cullDistance - 1f, 50f, 0f)), "height does not count");
            Assert.AreEqual(1, FoliagePushes.Publish(0.1f, published));
            Assert.AreEqual(0, FoliagePushes.Publish(settings.arc.springSeconds + 0.01f, published), "sprung back: freed");
            Assert.AreEqual(0, FoliagePushes.Alive);

            settings.enabled = false;
            Assert.IsFalse(Add(FoliagePushKind.Arc, 0f), "the master switch");
            settings.enabled = true;
            FoliagePushes.Settings = null;
            Assert.IsFalse(Add(FoliagePushKind.Arc, 0f), "no presenter (a server): nothing is added");
        }

        [Test]
        public void HeldPushWaitsForItsBlow()
        {
            Assert.IsTrue(Add(FoliagePushKind.Hostile, 0f, delay: 0.4f));
            Assert.AreEqual(0, FoliagePushes.Publish(0.3f, published), "the blow has not landed");
            Assert.AreEqual(1, FoliagePushes.Publish(0.45f, published));
            Assert.AreEqual(0.05f, published[2].x, 1e-4f, "its age counts from the blow");
        }

        [Test]
        public void ShapesTakeTheirNumbers()
        {
            Assert.IsTrue(Add(FoliagePushKind.Hostile, 0f, scale: settings.BodyScale(0.9f, 1f)));
            FoliagePushes.Publish(0f, published);
            Assert.AreEqual(settings.hostile.radius * 2f, published[0].w, 1e-4f, "a body twice as wide pushes twice as far");

            FoliagePushes.Clear();
            Assert.IsTrue(Add(FoliagePushKind.Hostile, 0f, scale: 100f));
            FoliagePushes.Publish(0f, published);
            Assert.AreEqual(settings.hostile.radius * settings.maxBodyScale, published[0].w, 1e-4f, "capped");

            FoliagePushes.Clear();
            Assert.IsTrue(Add(FoliagePushKind.Ring, 0f, scale: 0.5f));
            FoliagePushes.Publish(0f, published);
            Assert.AreEqual(settings.ring.radius * 0.5f, published[0].w, 1e-4f, "a ring reaches the skill's own range");
            Assert.AreEqual(-2f, published[1].z, "a ring has a running front, no arc");
            Assert.AreEqual(settings.ring.frontSpeed, published[2].y, 1e-4f);

            FoliagePushes.Clear();
            Assert.IsTrue(Add(FoliagePushKind.Arc, 0f));
            FoliagePushes.Publish(0f, published);
            Assert.AreEqual(Mathf.Cos(settings.arc.arcDegrees * 0.5f * Mathf.Deg2Rad), published[1].z, 1e-4f);
            Assert.AreEqual(0f, published[2].y, "an arc has no front");
        }

        [Test]
        public void SpringCurve()
        {
            float rate = settings.springRate, damping = settings.springDamping, rise = settings.riseSeconds;
            Assert.AreEqual(0f, FoliagePushes.Spring(0f, rate, damping, rise));
            float peak = 0f, peakAt = 0f, lowest = 0f;
            for (float t = 0f; t <= 0.6f; t += 0.002f)
            {
                float e = FoliagePushes.Spring(t, rate, damping, rise);
                if (e > peak) { peak = e; peakAt = t; }
                lowest = Mathf.Min(lowest, e);
            }
            Assert.LessOrEqual(peakAt, 0.06f, "peaks within 0.06 s");
            Assert.Greater(peak, 0.5f);
            Assert.Less(lowest, -0.05f, "swings back past upright once");
            Assert.Less(Mathf.Abs(FoliagePushes.Spring(0.5f, rate, damping, rise)), 0.05f, "under 5 % left at 0.5 s");
        }

        [Test]
        public void ShaderConstantMatches()
        {
            string compute = File.ReadAllText(Path.Combine(Application.dataPath, "_Game/Environment/Vegetation/Grass/Compute/GrassTrample.compute"));
            Match pool = Regex.Match(compute, @"#define\s+KN_MAX_SWING_PUSHES\s+(\d+)");
            Assert.IsTrue(pool.Success, "the compute names its pool size");
            Assert.AreEqual(FoliagePushes.MaxPushes, int.Parse(pool.Groups[1].Value));
            StringAssert.Contains("_KN_SwingPushes[KN_MAX_SWING_PUSHES * 4]", compute, "four float4 a push, as FoliagePushes.VectorsPerPush");
            Assert.AreEqual(4, FoliagePushes.VectorsPerPush);
            // the same spring as the C# copy
            StringAssert.Contains("exp(-_KN_SwingSpring.y * _KN_SwingSpring.x * t) * cos(_KN_SwingSpring.x * t)", compute);
        }

        [Test]
        public void NoAllocation()
        {
            Add(FoliagePushKind.Arc, 0f);
            FoliagePushes.Publish(0f, published);   // warm up
            Assert.That(() =>
            {
                for (int i = 0; i < 1000; i++) Add((FoliagePushKind)(i % 5), i * 0.016f, local: (i & 1) == 0, at: new Vector3(i % 7, 0f, 0f));
                for (int i = 0; i < 600; i++) FoliagePushes.Publish(i * 0.016f, published);
            }, Is.Not.AllocatingGCMemory());
        }
    }
}
