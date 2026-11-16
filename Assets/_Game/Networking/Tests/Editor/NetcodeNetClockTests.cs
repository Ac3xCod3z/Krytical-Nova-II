using KryticalNova.Core;
using NUnit.Framework;

namespace KryticalNova.Networking.Tests
{
    public class NetcodeNetClockTests
    {
        private INetClockProvider saved;

        [SetUp] public void SetUp() => saved = NetClock.Provider;
        [TearDown] public void TearDown() => NetClock.Install(saved);

        [Test]
        public void InstallProvider_InstallsAdapter_AndReinstallIsSafe()
        {
            NetClock.Install(null);
            NetcodeNetClock.InstallProvider();
            INetClockProvider first = NetClock.Provider;
            Assert.IsInstanceOf<NetcodeNetClock>(first);
            NetcodeNetClock.InstallProvider();
            Assert.AreSame(first, NetClock.Provider);
        }

        [Test]
        public void NoSession_AdapterIsNotRunning_SoClockFallsBackToLocal()
        {
            NetClock.Install(null);
            NetcodeNetClock.InstallProvider();
            if (Unity.Netcode.NetworkManager.Singleton != null && Unity.Netcode.NetworkManager.Singleton.IsListening) Assert.Ignore("A session is running.");
            Assert.IsFalse(NetClock.Provider.IsRunning);
            Assert.AreEqual(0, NetClock.TickRate);
        }

        /// <summary>
        /// Startup race (15 Nov 2026): a built entity server makes its worlds in Entities' BeforeSceneLoad bootstrap, in no fixed order
        /// with this hook; the entity clock already in the slot must stay (the hook used to replace it with one that never runs there).
        /// </summary>
        [Test]
        public void InstallProvider_LeavesAnotherNetcodesClockInTheSlot()
        {
            var other = new OtherClock();
            NetClock.Install(other);
            NetcodeNetClock.InstallProvider();
            Assert.AreSame(other, NetClock.Provider);
        }

        private sealed class OtherClock : INetClockProvider
        {
            public bool IsRunning => true;
            public double Seconds => 12.0;
            public int Tick => 360;
            public int TickRate => 30;
        }
    }
}
