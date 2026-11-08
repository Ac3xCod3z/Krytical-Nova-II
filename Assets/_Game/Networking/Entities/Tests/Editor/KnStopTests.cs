using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Entities;
using Unity.NetCode;
using KryticalNova.Core;
using static KryticalNova.Networking.Entities.Tests.KnJoinCheckTestSupport;

namespace KryticalNova.Networking.Entities.Tests
{
    /// <summary>M2b-1: the safe stop. The state machine with fakes, then the real system in a test world with players connected.</summary>
    public class KnStopTests
    {
        private sealed class Fake : IKnStopActions
        {
            public readonly List<string> Calls = new List<string>();
            public bool SaveThrows;
            public int Open;                 // connections "still open"
            public int GoneAfterChecks = -1; // >= 0: Open drops to 0 after this many checks of OpenConnections
            private int m_Checks;
            public void CloseDoor() => Calls.Add("door");
            public void SaveAll() { Calls.Add("save"); if (SaveThrows) throw new InvalidOperationException("disk full"); }
            public void DisconnectAll() => Calls.Add("disconnect");
            public int OpenConnections() { if (GoneAfterChecks >= 0 && ++m_Checks > GoneAfterChecks) Open = 0; return Open; }
            public void ForceCloseAll() { Calls.Add("force"); Open = 0; }
            public void Quit(int exitCode) => Calls.Add("quit:" + exitCode);
            public void Log(string line) => Calls.Add("log");
            public string Order => string.Join(",", Calls.FindAll(c => c != "log"));
        }

        [SetUp]
        public void SetUp()
        {
            KnStopRequests.Reset();
            KnStopSystem.QuitOverride = null;
            ServerShutdown.Handler = null;
        }

        [TearDown]
        public void TearDown()
        {
            KnStopRequests.Reset();
            KnStopSystem.QuitOverride = null;
            ServerShutdown.Handler = null;
        }

        // ------------------------------------------------------------------ the machine

        [Test]
        public void Machine_DoesNothing_UntilAskedToStop()
        {
            var fake = new Fake();
            var machine = new KnStopMachine(fake);
            for (int i = 0; i < 100; i++) machine.Step();
            Assert.AreEqual(KnStopMachine.Phase.Running, machine.Current);
            Assert.AreEqual(string.Empty, fake.Order);
        }

        [Test]
        public void Machine_ClosesTheDoor_Saves_Disconnects_ThenQuitsOnce_WithCodeZero()
        {
            var fake = new Fake { Open = 2, GoneAfterChecks = 3 };
            var machine = new KnStopMachine(fake) { GraceTicks = 50 };
            Assert.IsTrue(machine.Request("test"));
            machine.Step();
            Assert.AreEqual("door,save,disconnect", fake.Order, "all three happen in the first step, in this order");
            for (int i = 0; i < 3; i++) machine.Step();
            Assert.AreEqual("door,save,disconnect", fake.Order, "no quit while the connections are still open");
            machine.Step();
            Assert.AreEqual("door,save,disconnect,quit:0", fake.Order, "all gone: quit at once, nothing forced");
            Assert.AreEqual(KnStopMachine.Phase.Done, machine.Current);
            for (int i = 0; i < 50; i++) machine.Step();
            Assert.AreEqual("door,save,disconnect,quit:0", fake.Order, "quit happens once");
        }

        [Test]
        public void Machine_AllConnectionsAlreadyGone_QuitsAtOnce_NotAfterTheGrace()
        {
            var fake = new Fake { Open = 0 };
            var machine = new KnStopMachine(fake) { GraceTicks = 1000 };
            machine.Request("test");
            machine.Step();
            machine.Step();
            Assert.AreEqual("door,save,disconnect,quit:0", fake.Order);
        }

        [Test]
        public void Machine_ConnectionsThatLinger_AreForceClosed_AfterTheGrace_ThenItQuits()
        {
            var fake = new Fake { Open = 3 }; // never close by themselves
            var machine = new KnStopMachine(fake) { GraceTicks = 5 };
            machine.Request("test");
            machine.Step();
            for (int i = 0; i < 4; i++) machine.Step();
            Assert.AreEqual("door,save,disconnect", fake.Order, "still inside the grace");
            machine.Step();
            Assert.AreEqual("door,save,disconnect,force,quit:0", fake.Order, "force-close first, then quit");
            for (int i = 0; i < 50; i++) machine.Step();
            Assert.AreEqual("door,save,disconnect,force,quit:0", fake.Order, "once");
        }

        [Test]
        public void Machine_DefaultGrace_IsThreeSecondsOfTicks()
        {
            Assert.AreEqual(90, new KnStopMachine(new Fake()).GraceTicks);
            Assert.AreEqual(1, KnNetDefaults.StopGraceTicks(0f));
        }

        [Test]
        public void Machine_AskedTwice_StopsOnce()
        {
            var fake = new Fake();
            var machine = new KnStopMachine(fake) { GraceTicks = 2 };
            Assert.IsTrue(machine.Request("first"));
            Assert.IsFalse(machine.Request("second"));
            machine.Step();
            Assert.IsFalse(machine.Request("third, in the middle"));
            for (int i = 0; i < 10; i++) machine.Step();
            Assert.IsFalse(machine.Request("fourth, after it ended"));
            Assert.AreEqual("door,save,disconnect,quit:0", fake.Order);
            Assert.AreEqual("first", machine.Reason);
        }

        [Test]
        public void Machine_ASaveThatThrows_IsLogged_AndTheStopStillFinishes()
        {
            var fake = new Fake { SaveThrows = true, Open = 1 };
            var machine = new KnStopMachine(fake) { GraceTicks = 1 };
            machine.Request("test");
            machine.Step();
            machine.Step();
            Assert.AreEqual("door,save,disconnect,force,quit:0", fake.Order, "a throwing save still disconnects, force-closes and quits");
            Assert.IsTrue(fake.Calls.Exists(c => c == "log"));
        }

        [Test]
        public void Requests_AreIdempotent_AndTakenOnce()
        {
            Assert.IsFalse(KnStopRequests.Stopping);
            KnStopRequests.Request(KnStopSource.Signal);
            KnStopRequests.Request(KnStopSource.Http);
            Assert.IsTrue(KnStopRequests.Stopping);
            Assert.IsTrue(KnStopRequests.TryTake(out KnStopSource first));
            Assert.AreEqual(KnStopSource.Signal, first, "the first request is the one kept");
            Assert.IsFalse(KnStopRequests.TryTake(out _));
            Assert.IsTrue(KnStopRequests.Stopping, "stays 'stopping' for the page");
        }

        // ------------------------------------------------------------------ the page's decisions (loopback only, key, idempotent)

        [Test]
        public void StopEndpoint_IsLoopbackOnly_NeedsTheKey_AndTheFirstGoodCallWins()
        {
            var rules = new StopEndpointRules();
            var snap = new KnStatusSnapshot();
            KnPageRules.Reply remote = KnPageRules.Handle("POST", "/stop", false, "key", "key", snap, rules, false);
            Assert.AreEqual(403, remote.Status);
            Assert.IsFalse(remote.BeginStop);

            Assert.AreEqual(403, KnPageRules.Handle("POST", "/stop", true, "wrong", "key", snap, rules, false).Status);
            Assert.AreEqual(403, KnPageRules.Handle("POST", "/stop", true, "", "", snap, rules, false).Status, "no key configured: nothing is accepted");
            Assert.AreEqual(405, KnPageRules.Handle("GET", "/stop", true, "key", "key", snap, rules, false).Status);

            KnPageRules.Reply first = KnPageRules.Handle("POST", "/stop", true, "key", "key", snap, rules, false);
            Assert.AreEqual(200, first.Status);
            Assert.IsTrue(first.BeginStop);
            KnPageRules.Reply second = KnPageRules.Handle("POST", "/stop", true, "key", "key", snap, rules, true);
            Assert.AreEqual(202, second.Status);
            Assert.IsFalse(second.BeginStop, "only the first call starts the stop");
        }

        [Test]
        public void Page_ServesStatusToAnyone_AndHealthTurns503WhileStopping()
        {
            var rules = new StopEndpointRules();
            var snap = new KnStatusSnapshot { Name = "Test \"Server\"", Build = "b1", Map = "Zone01", Role = "map", Players = 3, MaxPlayers = 9, Tick = 1234 };
            KnPageRules.Reply status = KnPageRules.Handle("GET", "/status", false, null, "key", snap, rules, false);
            Assert.AreEqual(200, status.Status);
            StringAssert.Contains("\"players\":3", status.Body);
            StringAssert.Contains("\"tick\":1234", status.Body);
            StringAssert.Contains("\"map\":\"Zone01\"", status.Body);
            StringAssert.Contains("Test \\\"Server\\\"", status.Body);
            StringAssert.DoesNotContain("key", status.Body);

            Assert.AreEqual(200, KnPageRules.Handle("GET", "/health", false, null, "", snap, rules, false).Status);
            Assert.AreEqual(503, KnPageRules.Handle("GET", "/health", false, null, "", snap, rules, true).Status);
            Assert.AreEqual(404, KnPageRules.Handle("GET", "/nothing", true, null, "", snap, rules, false).Status);
            Assert.AreEqual(405, KnPageRules.Handle("DELETE", "/status", true, null, "", snap, rules, false).Status);
        }

#if UNITY_SERVER || UNITY_EDITOR
        [Test]
        public void Page_OverARealSocket_AnswersStatus_AndAGoodStopSetsTheRequest()
        {
            KnServerStatus.Publish(new KnStatusSnapshot { Name = "Socket Test", Players = 2, MaxPlayers = 5 });
            using var page = new KnServerPage("secret");
            Assert.IsTrue(page.Start(0, listenOnAllAddresses: false, udpStatus: false, out string problem), problem);

            string status = Http(page.TcpPort, "GET /status HTTP/1.1\r\nHost: localhost\r\n\r\n");
            StringAssert.StartsWith("HTTP/1.1 200", status);
            StringAssert.Contains("\"players\":2", status);

            string wrong = Http(page.TcpPort, "POST /stop HTTP/1.1\r\nHost: localhost\r\nX-KN-Key: nope\r\nContent-Length: 0\r\n\r\n");
            StringAssert.StartsWith("HTTP/1.1 403", wrong);
            Assert.IsFalse(KnStopRequests.Stopping);

            string good = Http(page.TcpPort, "POST /stop HTTP/1.1\r\nHost: localhost\r\nX-KN-Key: secret\r\nContent-Length: 0\r\n\r\n");
            StringAssert.StartsWith("HTTP/1.1 200", good);
            Assert.GreaterOrEqual(SpinUntil(() => KnStopRequests.Stopping), 0, "the stop request never arrived");
            string again = Http(page.TcpPort, "POST /stop HTTP/1.1\r\nHost: localhost\r\nX-KN-Key: secret\r\nContent-Length: 0\r\n\r\n");
            StringAssert.StartsWith("HTTP/1.1 202", again);
            Assert.IsTrue(KnStopRequests.TryTake(out KnStopSource source));
            Assert.AreEqual(KnStopSource.Http, source);
            Assert.IsFalse(KnStopRequests.TryTake(out _), "one request, however many calls");
        }

        private static string Http(int port, string request)
        {
            using var client = new System.Net.Sockets.TcpClient();
            client.Connect(System.Net.IPAddress.Loopback, port);
            client.ReceiveTimeout = 5000;
            System.Net.Sockets.NetworkStream stream = client.GetStream();
            byte[] bytes = System.Text.Encoding.ASCII.GetBytes(request);
            stream.Write(bytes, 0, bytes.Length);
            var all = new System.Text.StringBuilder();
            var buffer = new byte[2048];
            int n;
            while ((n = stream.Read(buffer, 0, buffer.Length)) > 0) all.Append(System.Text.Encoding.UTF8.GetString(buffer, 0, n));
            return all.ToString();
        }

        private static int SpinUntil(Func<bool> condition)
        {
            for (int i = 0; i < 200; i++)
            {
                if (condition()) return i;
                System.Threading.Thread.Sleep(10);
            }
            return -1;
        }
#endif

        // ------------------------------------------------------------------ the real system in a test world

        [Test]
        public void World_StopWithPlayersConnected_SavesFirst_ThenSendsThemAway_ThenQuitsOnce()
        {
            using var tw = new KnTestWorld { JoinPayload = Payload() };
            tw.CreateWorlds(2);
            Install(tw, new ScriptedProof());
            tw.Connect();
            tw.Tick(5);
            Assert.AreEqual(2, KnTestWorld.Count<NetworkId>(tw.Server));

            var stop = tw.Server.GetExistingSystemManaged<KnStopSystem>();
            stop.Machine.GraceTicks = 20;
            var events = new List<string>();
            int connectedAtSave = -1;
            ServerShutdown.Handler = ServerShutdown.FromAction(() =>
            {
                events.Add("save");
                connectedAtSave = KnTestWorld.Count<NetworkId>(tw.Server);
            });
            int quits = 0;
            int connectedAtQuit = -1;
            KnStopSystem.QuitOverride = code =>
            {
                quits++;
                events.Add("quit:" + code);
                connectedAtQuit = KnTestWorld.Count<NetworkStreamConnection>(tw.Server);
            };

            KnStopRequests.Request(KnStopSource.Signal);
            KnStopRequests.Request(KnStopSource.Http); // a second request changes nothing
            tw.Tick(2);
            Assert.AreEqual(1, events.FindAll(e => e == "save").Count, "saved exactly once");
            Assert.AreEqual(2, connectedAtSave, "the players were still connected when the save ran");
            Assert.AreEqual(1, KnTestWorld.Count<KnServerStopping>(tw.Server), "the door is closed");

            Assert.GreaterOrEqual(tw.TickUntil(() => quits > 0, 200), 0, "the server never quit");
            tw.Tick(30);
            Assert.AreEqual(1, quits, "quit happens once");
            Assert.AreEqual("save,quit:0", string.Join(",", events));
            Assert.IsFalse(KnSignals.ConsoleReleased, "the stop must not release the closing console's handler thread: Windows would end the process under the main thread's shutdown");
            Assert.AreEqual(0, connectedAtQuit, "everybody had been sent away before the quit");
            Assert.AreEqual(0, KnTestWorld.Count<NetworkId>(tw.Server));
            Assert.AreEqual(0, PlayerKeys.Count, "every player's key was released as they left");
        }

        [Test]
        public void World_AJoinDuringTheStop_IsRefused_AsServerStopping()
        {
            using var tw = new KnTestWorld { JoinPayload = Payload() };
            tw.CreateWorlds(1);
            Install(tw, new ScriptedProof());
            tw.Connect();
            tw.Tick(3);

            var stop = tw.Server.GetExistingSystemManaged<KnStopSystem>();
            stop.Machine.GraceTicks = 400; // stay in the stopping phase for the whole test
            ServerShutdown.Handler = ServerShutdown.FromAction(() => { });
            KnStopSystem.QuitOverride = _ => { };
            KnStopRequests.Request(KnStopSource.Console);
            tw.Tick(2);

            World late = tw.AddClient(Payload(), connect: true);
            AssertRefused(tw, late, KnRefuseReason.ServerStopping);
        }

        [Test]
        public void World_NothingRegisteredToSave_StillStops_AndSaysSo()
        {
            using var tw = new KnTestWorld();
            tw.CreateWorlds(0);
            tw.Server.GetExistingSystemManaged<KnStopSystem>().Machine.GraceTicks = 2;
            int quits = 0;
            KnStopSystem.QuitOverride = _ => quits++;
            KnStopRequests.Request(KnStopSource.Code);
            tw.Tick(6);
            Assert.AreEqual(1, quits);
            Assert.IsTrue(System.Linq.Enumerable.Any(tw.Interesting, l => l.Contains("Nothing is registered to save")), "the missing save handler must be reported");
        }

        [Test]
        public void StatusSystem_CopiesPlayersAndTickIntoTheSnapshot()
        {
            KnServerStatus.Reset();
            KnServerStatus.Name = "Status Test";
            KnServerStatus.MaxPlayers = 12;
            using var tw = new KnTestWorld { JoinPayload = Payload() };
            tw.CreateWorlds(2);
            Install(tw, new ScriptedProof());
            tw.Connect();
            tw.Tick(40); // the snapshot is taken once a second of world time
            KnStatusSnapshot s = KnServerStatus.Current;
            Assert.AreEqual("Status Test", s.Name);
            Assert.AreEqual(2, s.Players);
            Assert.AreEqual(12, s.MaxPlayers);
            Assert.Greater(s.Tick, 0u);
            Assert.IsFalse(s.Stopping);
            KnServerStatus.Reset();
        }
    }
}
