using KryticalNova.Player;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Persistence.Entities.Tests
{
    /// <summary>
    /// The entity-netcode vault (M3c, plan section 55) uses the NGO vault's own timing, lifted out of <see cref="PlayerVaultMotor"/>'s
    /// Begin into <see cref="PlayerVaultMotor.VaultTiming"/>, which the NGO warp keeps using. Numbers worked by hand.
    /// </summary>
    public class VaultSourceTests
    {
        [Test]
        public void TheTiming_IsTheFlatDistanceAtTheRunsSpeed_RisingBeforeTheNearEdge_FallingAfterTheFarEdge()
        {
            PlayerVaultMotor.VaultTiming(3f, 1f, 2f, 1f, new Vector2(0.4f, 1.2f), 2f, 0.3f, 0.12f, 5f,
                out float airTime, out float raise, out float riseEnds, out float fallStarts);
            Assert.AreEqual(0.6f, airTime, 1e-5f, "3 m at 5 m/s");
            Assert.AreEqual(0.82f, raise, 1e-5f, "1 m top - 0.3 m pose clearance + 0.12 m gap");
            Assert.AreEqual(0.15f, riseEnds, 1e-5f, "(1 - 0.25 edge lead) / 3 = 0.25 of the air time");
            Assert.AreEqual(0.45f, fallStarts, 1e-5f, "(2 + 0.25) / 3 = 0.75 of the air time");
        }

        [Test]
        public void TheAirTime_StaysInsideTheStylesWindowAndTheClip_AndTheShapeInsideItsLimits()
        {
            PlayerVaultMotor.VaultTiming(3f, 1f, 2f, 1f, new Vector2(0.4f, 1.2f), 2f, 0.3f, 0.12f, 1f, out float slow, out _, out _, out _);
            Assert.AreEqual(1.2f, slow, 1e-5f, "a walk would take 3 s: the style's longest");
            PlayerVaultMotor.VaultTiming(3f, 1f, 2f, 1f, new Vector2(0.4f, 1.2f), 0.8f, 0.3f, 0.12f, 1f, out float shortClip, out _, out _, out _);
            Assert.AreEqual(0.78f, shortClip, 1e-5f, "never past the clip (its length less 0.02 s)");
            PlayerVaultMotor.VaultTiming(3f, 1f, 2f, 1f, new Vector2(0.4f, 1.2f), 2f, 0.3f, 0.12f, 50f, out float fast, out _, out _, out _);
            Assert.AreEqual(0.4f, fast, 1e-5f, "the style's shortest");
            PlayerVaultMotor.VaultTiming(3f, 0f, 3f, 0.05f, new Vector2(0.4f, 1.2f), 2f, 0.3f, 0.12f, 5f,
                out float t, out float lowRaise, out float rise, out float fall);
            Assert.AreEqual(0.05f, lowRaise, 1e-5f, "at least the minimum raise");
            Assert.AreEqual(t * 0.15f, rise, 1e-5f, "rise ends no earlier than 15 % of the air time");
            Assert.AreEqual(t * 0.85f, fall, 1e-5f, "fall starts no later than 85 %");
        }
    }
}
