using KryticalNova.GameFeel;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Tests
{
    /// <summary>
    /// Perf clean-ups round 2, chunk 13: <see cref="HitReactor"/> (and the jump squash, by the same rule) stop writing the body's pose
    /// once their springs are at rest: within 1e-4 of the target and moving under 1e-5 a frame. Until then the spring calls are the
    /// old ones unchanged, so the curve is the same frame by frame; the look is checked in Play (a GIF for the owner).
    /// </summary>
    public sealed class SpringRestTests
    {
        [Test]
        public void Settled_NeedsBothNearTheTarget_AndStill()
        {
            Assert.IsTrue(HitReactor.Settled(Vector3.one, Vector3.one, Vector3.one), "there and still");
            Assert.IsTrue(HitReactor.Settled(new Vector3(1.00005f, 1f, 1f), new Vector3(1.00005f, 1f, 1f), Vector3.one), "within 1e-4, still");
            Assert.IsFalse(HitReactor.Settled(new Vector3(1.001f, 1f, 1f), new Vector3(1.001f, 1f, 1f), Vector3.one), "still, but not there");
            Assert.IsFalse(HitReactor.Settled(Vector3.one, new Vector3(1.00005f, 1f, 1f), Vector3.one), "there, but still moving");
            Assert.IsFalse(HitReactor.Settled(Vector3.zero, Vector3.zero, Vector3.one), "the squashed start of a hit is never rest");
        }

        [Test]
        public void TheLeanRule_IsTheSameAroundZero()
        {
            Assert.IsTrue(HitReactor.Settled(Vector3.zero, Vector3.zero, Vector3.zero));
            Assert.IsFalse(HitReactor.Settled(new Vector3(0.5f, 0f, 0f), new Vector3(0.5f, 0f, 0f), Vector3.zero), "still leaning half a degree");
        }
    }
}
