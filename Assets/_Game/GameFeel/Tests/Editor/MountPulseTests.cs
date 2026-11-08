using KryticalNova.GameFeel;
using NUnit.Framework;

namespace KryticalNova.Tests
{
    /// <summary>Owner retest 5 (8 Nov 2026): the mount's pulse is one weighty jolt that settles fast, not a rocking sway.</summary>
    public sealed class MountPulseTests
    {
        [Test]
        public void TheJolt_GoesDownOnce_ThenSettlesWithinAThirdOfASecond()
        {
            Assert.AreEqual(0f, MountPulse.Jolt(0f), 1e-4f);
            Assert.AreEqual(1f, MountPulse.Jolt(MountPulse.JoltDownSeconds), 1e-3f, "full compression first");
            float overshoot = 0f;
            for (float t = MountPulse.JoltDownSeconds; t < 0.6f; t += 0.005f) overshoot = UnityEngine.Mathf.Min(overshoot, MountPulse.Jolt(t));
            Assert.Greater(overshoot, -0.35f, "a small rebound, not a swing to the other side");
            for (float t = 0.35f; t < 0.6f; t += 0.01f)
                Assert.Less(UnityEngine.Mathf.Abs(MountPulse.Jolt(t)), 0.05f, $"settled at {t:0.00} s");
        }
    }
}
