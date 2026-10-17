using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Combat.Tests
{
    /// <summary>Cadence: when a skill key pressed in a fight counts as on the beat, and when the server believes it.</summary>
    public sealed class CadenceRulesTests
    {
        private CadenceSettings settings;

        [SetUp]
        public void SetUp()
        {
            settings = ScriptableObject.CreateInstance<CadenceSettings>();
            settings.windowMs = 90f;
            settings.keepSeconds = 1.5f;
            settings.maxPressAgeSeconds = 0.6f;
            settings.bonusPercent = 6f;
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(settings);

        private CadenceVerdict Judge(double errorSeconds, bool inFight = true, bool hasBeat = true, double now = 100.0, double pressedAt = 99.9) =>
            CadenceRules.Judge(settings, inFight, hasBeat, now, pressedAt, errorSeconds);

        [Test]
        public void OnTheBeat_EarlyOrLateInsideTheWindow_IsOnCadence()
        {
            Assert.AreEqual(CadenceVerdict.OnCadence, Judge(0.0));
            Assert.AreEqual(CadenceVerdict.OnCadence, Judge(-0.09));
            Assert.AreEqual(CadenceVerdict.OnCadence, Judge(0.09));
        }

        [Test]
        public void JustOutsideTheWindow_IsOffTheBeat_AndNothingMore()
        {
            Assert.AreEqual(CadenceVerdict.OffBeat, Judge(-0.091));
            Assert.AreEqual(CadenceVerdict.OffBeat, Judge(0.2));
        }

        [Test]
        public void OutOfAFight_NoPressEarnsIt()
        {
            Assert.AreEqual(CadenceVerdict.NotInAFight, Judge(0.0, inFight: false));
        }

        [Test]
        public void WithNoBeatPlaying_NoPressEarnsIt()
        {
            Assert.AreEqual(CadenceVerdict.NoBeat, Judge(0.0, hasBeat: false));
        }

        [Test]
        public void SwitchedOff_NoPressEarnsIt()
        {
            settings.enabled = false;
            Assert.AreEqual(CadenceVerdict.Off, Judge(0.0));
            Assert.AreEqual(CadenceVerdict.Off, CadenceRules.Judge(null, true, true, 100.0, 99.9, 0.0));
        }

        [Test]
        public void APressTooOld_OrFromTheFuture_IsNotBelieved()
        {
            Assert.AreEqual(CadenceVerdict.Stale, Judge(0.0, pressedAt: 99.3), "0.7 s old, 0.6 allowed");
            Assert.AreEqual(CadenceVerdict.OnCadence, Judge(0.0, pressedAt: 99.45), "inside the allowed age");
            Assert.AreEqual(CadenceVerdict.Stale, Judge(0.0, pressedAt: 100.5), "half a second ahead of the server's clock");
            Assert.AreEqual(CadenceVerdict.OnCadence, Judge(0.0, pressedAt: 100.05), "the two clocks are never quite the same");
        }

        [Test]
        public void AnEarnedPress_IsKeptForAWhile_ThenLost()
        {
            Assert.IsTrue(CadenceRules.StillKept(10.0f, 10.0f, settings.keepSeconds));
            Assert.IsTrue(CadenceRules.StillKept(11.4f, 10.0f, settings.keepSeconds));
            Assert.IsFalse(CadenceRules.StillKept(11.6f, 10.0f, settings.keepSeconds));
        }

        [Test]
        public void TheBonus_IsThePercentOnTop()
        {
            Assert.AreEqual(1.06f, settings.Multiplier, 1e-5f);
            settings.bonusPercent = 0f;
            Assert.AreEqual(1f, settings.Multiplier, 1e-5f);
        }
    }
}
