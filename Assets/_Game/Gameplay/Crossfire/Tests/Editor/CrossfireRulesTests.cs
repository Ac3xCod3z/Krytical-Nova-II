using KryticalNova.Crossfire;
using NUnit.Framework;

namespace KryticalNova.Crossfire.Tests
{
    /// <summary>Crossfire's clock (owner's DAY list item 17): phases on time, nightfall on the world clock, the GM's start / skip / end, brackets.</summary>
    public sealed class CrossfireRulesTests
    {
        // the Atlas's numbers: call 10 min, join 3 min, muster 1 min, war 15 min, end 20 s; a war every hour
        static readonly CrossfireTimes T = new CrossfireTimes { CallSeconds = 600, JoinWindowSeconds = 180, MusterSeconds = 60, WarSeconds = 900, EndSeconds = 20 };
        static readonly CrossfireCycle Hourly = new CrossfireCycle { FirstStart = 3600, Period = 3600 };
        static CrossfireOverride None => CrossfireOverride.None;

        [TestCase(3000, CrossfirePhase.Call)]
        [TestCase(2999, CrossfirePhase.Gap)]
        [TestCase(3600, CrossfirePhase.Muster)]
        [TestCase(3659, CrossfirePhase.Muster)]
        [TestCase(3660, CrossfirePhase.War)]
        [TestCase(4559, CrossfirePhase.War)]
        [TestCase(4560, CrossfirePhase.End)]
        [TestCase(4580, CrossfirePhase.Gap)]
        [TestCase(6600, CrossfirePhase.Call)]
        public void ThePhases_FallOnTheSchedule(double now, CrossfirePhase expected)
        {
            Assert.AreEqual(expected, CrossfireRules.At(Hourly, T, None, now).Phase);
        }

        [Test]
        public void ThePhase_KnowsItsStartAndEnd_ForTheTimers()
        {
            CrossfireNow war = CrossfireRules.At(Hourly, T, None, 4000);
            Assert.AreEqual(3660, war.PhaseStart);
            Assert.AreEqual(4560, war.PhaseEnd, "the time left on the HUD runs to here");
            Assert.AreEqual(3600, war.WarStart);
            CrossfireNow gap = CrossfireRules.At(Hourly, T, None, 5000);
            Assert.AreEqual(7200, gap.WarStart, "the Gap points at the next war");
            Assert.AreEqual(6600, gap.PhaseEnd, "and ends at its Call");
        }

        [Test]
        public void TheJoinWindow_IsTheCallsLastMinutes_AndTheMuster_AndTheGatesOpenOnlyInTheWar()
        {
            Assert.IsFalse(CrossfireRules.JoinOpen(CrossfireRules.At(Hourly, T, None, 3400), T, 3400));
            Assert.IsTrue(CrossfireRules.JoinOpen(CrossfireRules.At(Hourly, T, None, 3420), T, 3420));
            Assert.IsTrue(CrossfireRules.JoinOpen(CrossfireRules.At(Hourly, T, None, 3630), T, 3630));
            Assert.IsFalse(CrossfireRules.JoinOpen(CrossfireRules.At(Hourly, T, None, 3700), T, 3700));
            Assert.IsFalse(CrossfireRules.GatesOpen(CrossfirePhase.Muster));
            Assert.IsTrue(CrossfireRules.GatesOpen(CrossfirePhase.War));
            Assert.IsFalse(CrossfireRules.GatesOpen(CrossfirePhase.End));
        }

        [Test]
        public void Nightfall_ComesOnceAGameDay_AtTheSetHour()
        {
            // a day of 3600 s at normal speed, 06:00 at clock 100: 20:00 is 14 game hours later = 2100 s
            CrossfireCycle c = CrossfireRules.Nightfalls(100, 0.25, 1, 3600, 20);
            Assert.AreEqual(3600, c.Period, 1e-9);
            Assert.AreEqual(2200, c.FirstStart, 1e-6);
            // past 20:00 already (22:00 at clock 0): the next is 22 game hours on
            CrossfireCycle late = CrossfireRules.Nightfalls(0, 22.0 / 24.0, 1, 3600, 20);
            Assert.AreEqual(3300, late.FirstStart, 1e-6);
            // a GM's double speed halves the real time between two nightfalls
            Assert.AreEqual(1800, CrossfireRules.Nightfalls(0, 0, 2, 3600, 20).Period, 1e-9);
            Assert.IsTrue(double.IsNaN(CrossfireRules.Nightfalls(0, 0, 1, 0, 20).FirstStart), "no clock, no war");
        }

        [Test]
        public void AFixedCycle_StartsAfterOneCall()
        {
            CrossfireCycle c = CrossfireRules.Fixed(50, 600, 3600);
            Assert.AreEqual(CrossfirePhase.Call, CrossfireRules.At(c, T, None, 50).Phase);
            Assert.AreEqual(650, CrossfireRules.At(c, T, None, 50).WarStart);
        }

        [Test]
        public void StartNow_MustersAtOnce_AndTheScheduledWarIsNotRunTwice()
        {
            CrossfireOverride o = None;
            CrossfireRules.StartNow(Hourly, T, ref o, 3100);   // during the 3600 war's Call
            Assert.AreEqual(CrossfirePhase.Muster, CrossfireRules.At(Hourly, T, o, 3100).Phase);
            Assert.AreEqual(CrossfirePhase.War, CrossfireRules.At(Hourly, T, o, 3160).Phase);
            Assert.AreEqual(CrossfirePhase.Gap, CrossfireRules.At(Hourly, T, o, 4080).Phase, "after the GM's war, no half-run 3600 war");
            Assert.AreEqual(7200, CrossfireRules.At(Hourly, T, o, 4080).WarStart, "the next hour's comes as usual");
        }

        [Test]
        public void Skip_GoesThroughEveryPhase_InOrder()
        {
            CrossfireOverride o = None;
            double now = 1000;   // the Gap
            CrossfirePhase[] order = { CrossfirePhase.Call, CrossfirePhase.Muster, CrossfirePhase.War, CrossfirePhase.End, CrossfirePhase.Gap };
            foreach (CrossfirePhase expected in order)
            {
                CrossfireRules.Skip(Hourly, T, ref o, now);
                Assert.AreEqual(expected, CrossfireRules.At(Hourly, T, o, now).Phase);
                now += 1;
            }
            Assert.AreEqual(CrossfirePhase.Muster, CrossfireRules.At(Hourly, T, o, 3610).Phase, "the GM's war did not touch the 3600 one: it comes as usual");
        }

        [Test]
        public void End_SendsAWarToItsEnd_AndCallsOffAWarOnlyCalled()
        {
            CrossfireOverride o = None;
            CrossfireRules.End(Hourly, T, ref o, 4000);   // in the War
            CrossfireNow at = CrossfireRules.At(Hourly, T, o, 4000);
            Assert.AreEqual(CrossfirePhase.End, at.Phase);
            Assert.AreEqual(4020, at.PhaseEnd, "the End's own length still runs");
            Assert.AreEqual(CrossfirePhase.Gap, CrossfireRules.At(Hourly, T, o, 4021).Phase);

            CrossfireOverride called = None;
            CrossfireRules.End(Hourly, T, ref called, 6700);   // the 7200 war's Call
            Assert.AreEqual(CrossfirePhase.Gap, CrossfireRules.At(Hourly, T, called, 6700).Phase, "called off");
            Assert.AreEqual(10800, CrossfireRules.At(Hourly, T, called, 6700).WarStart);

            CrossfireOverride idle = None;
            CrossfireRules.End(Hourly, T, ref idle, 1000);
            Assert.AreEqual(CrossfirePhase.Gap, CrossfireRules.At(Hourly, T, idle, 1000).Phase, "nothing to end in the Gap");
            Assert.AreEqual(3600, CrossfireRules.At(Hourly, T, idle, 1000).WarStart);
        }

        [TestCase(1, 0)]
        [TestCase(9, 0)]
        [TestCase(10, 1)]
        [TestCase(19, 1)]
        [TestCase(20, 2)]
        [TestCase(99, 9)]
        [TestCase(100, 10)]
        [TestCase(112, 10)]
        public void Brackets_AreTenLevelsFromTen_TheLastTakesTheRest(int level, int bracket)
        {
            Assert.AreEqual(bracket, CrossfireRules.BracketOf(level, 10, 10, 10));
        }

        [Test]
        public void TheBracketReadsInRomanNumerals()
        {
            Assert.AreEqual("I", CrossfireRules.Roman(1));
            Assert.AreEqual("IV", CrossfireRules.Roman(4));
            Assert.AreEqual("X", CrossfireRules.Roman(10));
            Assert.AreEqual(string.Empty, CrossfireRules.Roman(0));
        }
    }
}
