using KryticalNova.Core;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Tests
{
    public class NetClockTests
    {
        private sealed class Fake : INetClockProvider
        {
            public bool Running; public double Secs; public int TickValue; public int Rate;
            public bool IsRunning => Running;
            public double Seconds => Secs;
            public int Tick => TickValue;
            public int TickRate => Rate;
        }

        private INetClockProvider saved;

        [SetUp] public void SetUp() { saved = NetClock.Provider; NetClock.Install(null); }
        [TearDown] public void TearDown() => NetClock.Install(saved);

        [Test]
        public void NoProvider_FallsBackToLocalTime()
        {
            Assert.IsFalse(NetClock.IsRunning);
            Assert.AreEqual(0, NetClock.Tick);
            Assert.AreEqual(0, NetClock.TickRate);
            Assert.AreEqual(Time.timeAsDouble, NetClock.Now, 1.0);
        }

        [Test]
        public void ProviderNotRunning_FallsBackToLocalTime()
        {
            NetClock.Install(new Fake { Running = false, Secs = 12345.0, TickValue = 9, Rate = 30 });
            Assert.IsFalse(NetClock.IsRunning);
            Assert.AreNotEqual(12345.0, NetClock.Now);
            Assert.AreEqual(0, NetClock.Tick);
        }

        [Test]
        public void ProviderRunning_ReturnsItsValues()
        {
            NetClock.Install(new Fake { Running = true, Secs = 12345.5, TickValue = 9, Rate = 30 });
            Assert.IsTrue(NetClock.IsRunning);
            Assert.AreEqual(12345.5, NetClock.Now);
            Assert.AreEqual(9, NetClock.Tick);
            Assert.AreEqual(30, NetClock.TickRate);
        }

        [Test]
        public void Uninstall_OnlyRemovesTheSameProvider()
        {
            var a = new Fake { Running = true, Secs = 1 };
            var b = new Fake { Running = true, Secs = 2 };
            NetClock.Install(a);
            NetClock.Install(b);
            NetClock.Uninstall(a);
            Assert.AreSame(b, NetClock.Provider);
            NetClock.Uninstall(b);
            Assert.IsNull(NetClock.Provider);
        }
    }
}
