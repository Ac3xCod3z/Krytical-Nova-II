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
            NetcodeNetClock.InstallProvider();
            if (Unity.Netcode.NetworkManager.Singleton != null && Unity.Netcode.NetworkManager.Singleton.IsListening) Assert.Ignore("A session is running.");
            Assert.IsFalse(NetClock.Provider.IsRunning);
            Assert.AreEqual(0, NetClock.TickRate);
        }
    }
}
