using System;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Autopilot.Tests
{
    /// <summary>The Autopilot Pass by the clock: lengthening, stacking, the 90-day cap, and which item is which Pass.</summary>
    public sealed class PassClockTests
    {
        private static readonly long Now = new DateTime(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc).Ticks;
        private static long Days(double days) => TimeSpan.FromDays(days).Ticks;

        [Test]
        public void APassOnNoTimeRunsFromNow()
        {
            Assert.AreEqual(Now + Days(3), PassClock.Extend(Now, 0, 3));
            Assert.AreEqual(Now + Days(3), PassClock.Extend(Now, Now - Days(4), 3), "time that ran out is not given back");
        }

        [Test]
        public void PassesStackFromTheEnd()
        {
            long first = PassClock.Extend(Now, 0, 3);
            long second = PassClock.Extend(Now, first, 14);
            Assert.AreEqual(Now + Days(17), second);
            Assert.AreEqual(Now + Days(47), PassClock.Extend(Now, second, 30));
        }

        [Test]
        public void APassPastNinetyDaysIsRefused()
        {
            Assert.IsFalse(PassClock.WouldExceed(Now, Now + Days(60), 30, 90), "exactly 90 is allowed");
            Assert.IsTrue(PassClock.WouldExceed(Now, Now + Days(60.5), 30, 90));
            Assert.IsTrue(PassClock.WouldExceed(Now, Now + Days(88), 3, 90));
            Assert.IsFalse(PassClock.WouldExceed(Now, 0, 30, 90));
        }

        [Test]
        public void TimeRunsByTheClock()
        {
            long expires = PassClock.Extend(Now, 0, 3);
            Assert.IsTrue(PassClock.IsActive(Now + Days(2.9), expires));
            Assert.IsFalse(PassClock.IsActive(Now + Days(3), expires));
            Assert.AreEqual(3600f, PassClock.SecondsLeft(expires - TimeSpan.FromHours(1).Ticks, expires), 0.01f);
            Assert.AreEqual(0f, PassClock.SecondsLeft(expires + 10, expires));
        }

        [Test]
        public void SettingDaysEndsOrStartsTheTime()
        {
            Assert.AreEqual(Now, PassClock.SetDays(Now, 0));
            Assert.AreEqual(Now + Days(5), PassClock.SetDays(Now, 5));
        }

        [Test]
        public void TheLineSaysWhatIsLeftOrWhatToUse()
        {
            StringAssert.StartsWith("Autopilot: 2d", PassClock.TimeLeftLine("Autopilot", Now, Now + Days(2.5)));
            StringAssert.Contains("Autopilot Pass", PassClock.TimeLeftLine("Autopilot", Now, Now - 1));
        }

        [Test]
        public void EachAutopilotPassIsItsTier()
        {
            AutopilotSettings settings = ScriptableObject.CreateInstance<AutopilotSettings>();
            try
            {
                Assert.AreEqual(3, settings.AutopilotTierFor(9030)?.days);
                Assert.AreEqual(14, settings.AutopilotTierFor(9034)?.days);
                Assert.AreEqual(30, settings.AutopilotTierFor(9035)?.days);
                Assert.IsNull(settings.AutopilotTierFor(9031), "an Autobuff Pass is not an Autopilot Pass");
                Assert.AreEqual(90, settings.autopilotMaxDays);
            }
            finally { UnityEngine.Object.DestroyImmediate(settings); }
        }
    }
}
