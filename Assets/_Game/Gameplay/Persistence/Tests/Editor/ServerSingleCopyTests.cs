using System.Collections.Generic;
using KryticalNova.Networking;
using NUnit.Framework;

namespace KryticalNova.Tests
{
    /// <summary>A second server started on the same PC stops at once (17 Oct 2026, Start Server.bat opened twice).</summary>
    public sealed class ServerSingleCopyTests
    {
        private static ServerLaunchOptions Options(int port = 7777, int pagePort = 8090, string mode = ServerLaunchOptions.Direct) =>
            new ServerLaunchOptions { port = port, pagePort = pagePort, connectionMode = mode };

        [Test]
        public void TheGamePortAndThePagePortAreChecked()
        {
            List<ServerSingleCopy.NeededPort> ports = ServerSingleCopy.PortsOf(Options());
            Assert.AreEqual(2, ports.Count);
            Assert.AreEqual("UDP 7777 (game)", ports[0].ToString());
            Assert.AreEqual("TCP 8090 (control page)", ports[1].ToString());
        }

        [Test]
        public void RelayModeAndAPageSwitchedOffNeedFewerPorts()
        {
            List<ServerSingleCopy.NeededPort> relay = ServerSingleCopy.PortsOf(Options(mode: ServerLaunchOptions.Relay));
            Assert.AreEqual(1, relay.Count, "in Relay mode the players do not come to this PC's game port");
            Assert.IsTrue(relay[0].Tcp);
            List<ServerSingleCopy.NeededPort> noPage = ServerSingleCopy.PortsOf(Options(pagePort: 0));
            Assert.AreEqual(1, noPage.Count);
            Assert.IsFalse(noPage[0].Tcp);
            Assert.AreEqual(0, ServerSingleCopy.PortsOf(Options(pagePort: 0, mode: ServerLaunchOptions.Relay)).Count);
        }

        [Test]
        public void OnlyTakenPortsAreReported()
        {
            ServerLaunchOptions options = Options();
            Assert.AreEqual(0, ServerSingleCopy.Taken(options, _ => false, _ => false).Count);
            List<ServerSingleCopy.NeededPort> both = ServerSingleCopy.Taken(options, p => p == 7777, p => p == 8090);
            Assert.AreEqual(2, both.Count);
            List<ServerSingleCopy.NeededPort> page = ServerSingleCopy.Taken(options, _ => false, p => p == 8090);
            Assert.AreEqual(1, page.Count, "a second copy with another game port still meets the first one's page");
            Assert.AreEqual(8090, page[0].Number);
            Assert.AreEqual(0, ServerSingleCopy.Taken(Options(7787, 8091), p => p == 7777, p => p == 8090).Count,
                "a second server with ports of its own runs");
        }

        [Test]
        public void TheWindowSaysPlainlyThatThisCopyStops()
        {
            ServerLaunchOptions options = Options();
            Assert.IsNull(ServerSingleCopy.Message(options, new List<ServerSingleCopy.NeededPort>(), "192.168.1.20"));
            string message = ServerSingleCopy.Message(options, ServerSingleCopy.Taken(options, _ => true, _ => true), "192.168.1.20");
            StringAssert.StartsWith("[Server] A Krytical Nova server is already running on this PC (control page http://192.168.1.20:8090). This copy stops.", message);
            StringAssert.Contains("UDP 7777 (game), TCP 8090 (control page)", message);
            StringAssert.Contains("-port 7787 -pagePort 8091", message);
            string noPage = ServerSingleCopy.Message(Options(pagePort: 0), ServerSingleCopy.Taken(Options(pagePort: 0), _ => true, _ => true), "192.168.1.20");
            StringAssert.StartsWith("[Server] A Krytical Nova server is already running on this PC. This copy stops.", noPage);
        }

        [Test]
        public void TheStartScriptDoesNotRestartACopyThatStopped()
        {
            Assert.AreNotEqual(3, ServerSingleCopy.AlreadyRunningExitCode, "Start Server.bat starts the server again on exit code 3");
            Assert.AreNotEqual(0, ServerSingleCopy.AlreadyRunningExitCode, "and a stop for this reason is not a clean stop");
        }
    }
}
