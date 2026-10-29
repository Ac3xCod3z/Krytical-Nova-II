using System;
using System.Linq;
using NUnit.Framework;
using Unity.Entities;
using Unity.NetCode;
using KryticalNova.Core;
using KryticalNova.Networking;

namespace KryticalNova.Networking.Entities.Tests
{
    /// <summary>M2b-2: the server page's player list and kick on Netcode for Entities (status, players, kick, stop).</summary>
    public class KnControlPageTests
    {
        private const string Key = "page-key";

        [SetUp]
        public void SetUp()
        {
            KnServerStatus.Reset();
            KnKickRequests.Reset();
            KnStopRequests.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            KnServerStatus.Reset();
            KnKickRequests.Reset();
            KnStopRequests.Reset();
        }

        private static KnStatusSnapshot Snapshot() => new KnStatusSnapshot
        {
            Name = "N",
            Players = 2,
            PlayerRows = new[]
            {
                new KnPlayerRow { Key = 18446744073709551615UL, NetworkId = 1, Name = "Tolan \"the\" <b>", PingMs = 34, Map = "Zone01" },
                new KnPlayerRow { Key = 7, NetworkId = 2, Name = "", PingMs = -1, Map = "Zone01" },
            },
        };

#if UNITY_SERVER || UNITY_EDITOR
        private static KnPageRules.Reply Call(string method, string path, bool loopback = true, string key = Key, string expected = Key,
            KnStatusSnapshot status = null, string query = null) =>
            KnPageRules.Handle(method, path, loopback, key, expected, status ?? Snapshot(), new StopEndpointRules(), false, query);

        [Test]
        public void Players_AreForTheOwnerOnly_ThisMachineAndTheKey()
        {
            Assert.AreEqual(200, Call("GET", "/players").Status);
            Assert.AreEqual(403, Call("GET", "/players", loopback: false).Status);
            Assert.AreEqual(403, Call("GET", "/players", key: "wrong").Status);
            Assert.AreEqual(403, Call("GET", "/players", key: null).Status);
            Assert.AreEqual(403, Call("GET", "/players", expected: "").Status, "no key configured = nothing is shown");
            Assert.AreEqual(405, Call("POST", "/players").Status);
        }

        [Test]
        public void Players_Json_HasKeyIdNamePingMap_AndNothingAboutAccountsOrAddresses()
        {
            string json = Call("GET", "/players").Body;
            StringAssert.Contains("\"count\":2", json);
            StringAssert.Contains("\"key\":\"18446744073709551615\"", json, "a 64-bit key is text, not a script number");
            StringAssert.Contains("\"ping\":34", json);
            StringAssert.Contains("\"ping\":-1", json);
            StringAssert.Contains("\"map\":\"Zone01\"", json);
            StringAssert.Contains("Tolan \\\"the\\\" \\u003cb\\u003e", json, "names are quoted and angle brackets are escaped");
            StringAssert.DoesNotContain("account", json.ToLowerInvariant());
            StringAssert.DoesNotContain("address", json.ToLowerInvariant());
            Assert.AreEqual("{\"count\":0,\"players\":[]}", new KnStatusSnapshot().PlayersJson());
        }

        [Test]
        public void Kick_NeedsPostKeyAndAPlayerThatIsOnline()
        {
            Assert.AreEqual(403, Call("POST", "/kick", loopback: false, query: "player=7").Status);
            Assert.AreEqual(403, Call("POST", "/kick", key: "x", query: "player=7").Status);
            Assert.AreEqual(405, Call("GET", "/kick", query: "player=7").Status);
            Assert.AreEqual(400, Call("POST", "/kick").Status);
            Assert.AreEqual(400, Call("POST", "/kick", query: "player=abc").Status);
            Assert.AreEqual(400, Call("POST", "/kick", query: "player=0").Status);
            Assert.AreEqual(404, Call("POST", "/kick", query: "player=8").Status, "not online");
            Assert.AreEqual(0UL, Call("POST", "/kick", query: "player=8").KickKey);

            KnPageRules.Reply ok = Call("POST", "/kick", query: "x=1&player=7");
            Assert.AreEqual(202, ok.Status);
            Assert.AreEqual(7UL, ok.KickKey);
            Assert.AreEqual(18446744073709551615UL, Call("POST", "/kick", query: "player=18446744073709551615").KickKey);
        }

        [Test]
        public void TryReadPlayer_ReadsOnlyAPlainPositiveDecimal()
        {
            Assert.IsTrue(KnPageRules.TryReadPlayer("player=12", out ulong k));
            Assert.AreEqual(12UL, k);
            Assert.IsFalse(KnPageRules.TryReadPlayer("player=-1", out _));
            Assert.IsFalse(KnPageRules.TryReadPlayer("player=+1", out _));
            Assert.IsFalse(KnPageRules.TryReadPlayer("player=18446744073709551616", out _));
            Assert.IsFalse(KnPageRules.TryReadPlayer("players=1", out _));
            Assert.IsFalse(KnPageRules.TryReadPlayer(null, out _));
        }

        [Test]
        public void Stop_AndStatus_StillWork_AsBefore()
        {
            Assert.AreEqual(200, Call("GET", "/status").Status);
            KnPageRules.Reply stop = Call("POST", "/stop");
            Assert.AreEqual(200, stop.Status);
            Assert.IsTrue(stop.BeginStop);
        }

        [Test]
        public void Page_OverARealSocket_ListsPlayers_AndAGoodKickIsQueued()
        {
            KnServerStatus.Publish(Snapshot());
            using var page = new KnServerPage(Key);
            Assert.IsTrue(page.Start(0, listenOnAllAddresses: false, udpStatus: false, out string problem), problem);

            string noKey = Http(page.TcpPort, "GET /players HTTP/1.1\r\nHost: localhost\r\n\r\n");
            StringAssert.StartsWith("HTTP/1.1 403", noKey);
            string list = Http(page.TcpPort, "GET /players HTTP/1.1\r\nHost: localhost\r\nX-KN-Key: " + Key + "\r\n\r\n");
            StringAssert.StartsWith("HTTP/1.1 200", list);
            StringAssert.Contains("\"count\":2", list);

            string nobody = Http(page.TcpPort, "POST /kick?player=99 HTTP/1.1\r\nHost: localhost\r\nX-KN-Key: " + Key + "\r\nContent-Length: 0\r\n\r\n");
            StringAssert.StartsWith("HTTP/1.1 404", nobody);
            Assert.AreEqual(0, KnKickRequests.Pending);

            string kick = Http(page.TcpPort, "POST /kick?player=7 HTTP/1.1\r\nHost: localhost\r\nX-KN-Key: " + Key + "\r\nContent-Length: 0\r\n\r\n");
            StringAssert.StartsWith("HTTP/1.1 202", kick);
            Assert.GreaterOrEqual(SpinUntil(() => KnKickRequests.Pending == 1), 0, "the kick request never arrived");
            Assert.IsTrue(KnKickRequests.TryTake(out ulong key));
            Assert.AreEqual(7UL, key);
            Assert.IsFalse(KnStopRequests.Stopping, "a kick is not a stop");
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

        // ------------------------------------------------------------------ in a test world

        [Test]
        public void StatusSystem_ListsTheConnectedPlayers_WithTheirKeysAndNames()
        {
            using var tw = new KnTestWorld();
            tw.CreateWorlds(2);
            KnServerStatus.Map = "Zone01";
            KnServerStatus.NameOf = key => "player-" + key.Value;
            tw.Connect();
            Assert.GreaterOrEqual(tw.TickUntil(() => KnServerStatus.Current.PlayerRows.Length == 2, 200), 0, "the snapshot never listed both players");
            var keys = KnTestQueries.ServerKeys(tw).Select(k => k.key).OrderBy(k => k).ToArray();
            var rows = KnServerStatus.Current.PlayerRows.OrderBy(r => r.Key).ToArray();
            CollectionAssert.AreEqual(keys, rows.Select(r => r.Key).ToArray());
            Assert.AreEqual("player-" + keys[0], rows[0].Name);
            Assert.AreEqual("Zone01", rows[0].Map);
            Assert.AreEqual(2, KnServerStatus.Current.Players);
        }

        [Test]
        public void Kick_ClosesThatPlayersConnection_OnTheMainThread_AndTheOtherStays()
        {
            using var tw = new KnTestWorld();
            tw.CreateWorlds(2);
            var closed = new RecordingClosedHandler();
            tw.Server.GetExistingSystemManaged<ConnectionClosedSystem>().Handlers.Add(closed);
            tw.Connect();
            tw.Tick(5);
            var keys = KnTestQueries.ServerKeys(tw);
            Assert.AreEqual(2, keys.Count);

            ulong victim = keys[0].key;
            KnKickRequests.Request(victim);
            Assert.AreEqual(1, KnKickRequests.Pending);
            tw.Tick(1);
            Assert.AreEqual(0, KnKickRequests.Pending, "the system took the request");

            Assert.GreaterOrEqual(tw.TickUntil(() => closed.Closed.Count == 1, 300), 0, "the kicked player's connection never closed");
            Assert.AreEqual(victim, closed.Closed[0].Value);
            var left = KnTestQueries.ServerKeys(tw);
            Assert.AreEqual(1, left.Count);
            Assert.AreEqual(keys[1].key, left[0].key);

            // a key that is gone (or never was) is ignored
            KnKickRequests.Request(victim);
            KnKickRequests.Request(123456789UL);
            tw.Tick(5);
            Assert.AreEqual(1, KnTestQueries.ServerKeys(tw).Count);
            Assert.AreEqual(1, closed.Closed.Count);
        }
    }
}
