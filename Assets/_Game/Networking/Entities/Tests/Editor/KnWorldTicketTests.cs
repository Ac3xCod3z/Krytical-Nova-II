using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using Unity.Entities;
using KryticalNova.Core;
using KryticalNova.Networking;
using static KryticalNova.Networking.Entities.Tests.KnJoinCheckTestSupport;

namespace KryticalNova.Networking.Entities.Tests
{
    /// <summary>A world service the test scripts: every call gets its own task, completed when the test says.</summary>
    public sealed class FakeTicketTransport : IKnTicketTransport
    {
        public readonly List<(string path, string json, TaskCompletionSource<string> done)> Calls = new List<(string, string, TaskCompletionSource<string>)>();
        public bool ThrowOnCall;

        public Task<string> PostAsync(string path, string json)
        {
            if (ThrowOnCall) throw new InvalidOperationException("no network");
            var done = new TaskCompletionSource<string>();
            Calls.Add((path, json, done));
            return done.Task;
        }

        public static string Granted(string account, string character = "Tolan", string from = "pc:7777", string map = "Zone02", string arrival = "gate") =>
            "{\"granted\":true,\"refused\":null,\"account\":\"" + account + "\",\"character\":\"" + character + "\",\"from\":\"" + from + "\",\"map\":\"" + map + "\",\"arrival\":\"" + arrival + "\"}";

        public const string Denied = "{\"granted\":false,\"refused\":\"no such ticket (forged, used or run out)\"}";
    }

    /// <summary>M2b-2: the travel ticket against the world service (<c>POST /world/redeem</c>), polled like the Unity token.</summary>
    public class KnWorldTicketTests
    {
        private FakeTicketTransport m_Transport;
        private List<string> m_Log;

        [SetUp]
        public void SetUp()
        {
            m_Transport = new FakeTicketTransport();
            m_Log = new List<string>();
            KnArrivals.Reset();
            BanLookup.Current = new FakeBans();
        }

        [TearDown]
        public void TearDown()
        {
            KnArrivals.Reset();
            BanLookup.Current = null;
        }

        private WorldServiceTicketCheck Tickets(bool requireTicket = false) =>
            new WorldServiceTicketCheck(m_Transport, "pc:7778", requireTicket, m_Log.Add);

        [Test]
        public void NoTicket_MainServerLetsThemIn_MapServerTurnsThemAway_WithoutAskingTheService()
        {
            Assert.AreEqual(KnJoinStatus.Accepted, Tickets().Check(Conn(1), AccountA, "").Status);
            KnJoinVerdict v = Tickets(requireTicket: true).Check(Conn(1), AccountA, "");
            Assert.AreEqual(KnJoinStatus.Refused, v.Status);
            Assert.AreEqual(KnRefuseReason.BadTicket, v.Reason);
            Assert.AreEqual(WorldServiceTicketCheck.NoTicketText, v.Detail);
            Assert.AreEqual(0, m_Transport.Calls.Count);
        }

        [Test]
        public void AGoodTicket_IsPending_WhileTheServiceThinks_ThenAccepted_AndTheArrivalIsKept()
        {
            WorldServiceTicketCheck check = Tickets(requireTicket: true);
            Assert.AreEqual(KnJoinStatus.Pending, check.Check(Conn(1), AccountA, "T1").Status);
            Assert.AreEqual(KnJoinStatus.Pending, check.Check(Conn(1), AccountA, "T1").Status);
            Assert.AreEqual(1, m_Transport.Calls.Count, "one call per connection, however often it is polled");
            Assert.AreEqual("world/redeem", m_Transport.Calls[0].path);
            StringAssert.Contains("\"ticket\":\"T1\"", m_Transport.Calls[0].json);
            StringAssert.Contains("\"server\":\"pc:7778\"", m_Transport.Calls[0].json);

            m_Transport.Calls[0].done.SetResult(FakeTicketTransport.Granted(AccountA));
            Assert.AreEqual(KnJoinStatus.Accepted, check.Check(Conn(1), AccountA, "T1").Status);
            Assert.IsTrue(KnArrivals.TryTake(AccountA, out KnArrival arrival));
            Assert.AreEqual("Tolan", arrival.Character);
            Assert.AreEqual("Zone02", arrival.Map);
            Assert.AreEqual("gate", arrival.Place);
            Assert.AreEqual("pc:7777", arrival.FromServer);
            Assert.IsFalse(KnArrivals.TryTake(AccountA, out _), "an arrival is used once");
        }

        [Test]
        public void ATicketOfAnotherAccount_IsRefused_HoweverValid()
        {
            WorldServiceTicketCheck check = Tickets();
            check.Check(Conn(1), AccountA, "T1");
            m_Transport.Calls[0].done.SetResult(FakeTicketTransport.Granted("b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2"));
            KnJoinVerdict v = check.Check(Conn(1), AccountA, "T1");
            Assert.AreEqual(KnJoinStatus.Refused, v.Status);
            Assert.AreEqual(KnRefuseReason.BadTicket, v.Reason);
            Assert.AreEqual(0, KnArrivals.Count);
        }

        [Test]
        public void ADeniedTicket_ShowsThePlayerPlainWords_AndTheLogTheServicesReason()
        {
            WorldServiceTicketCheck check = Tickets();
            check.Check(Conn(1), AccountA, "forged");
            m_Transport.Calls[0].done.SetResult(FakeTicketTransport.Denied);
            KnJoinVerdict v = check.Check(Conn(1), AccountA, "forged");
            Assert.AreEqual(WorldServiceTicketCheck.BadTicketText, v.Detail);
            Assert.IsTrue(m_Log.Exists(l => l.Contains("forged, used or run out")));
            Assert.IsFalse(v.Detail.Contains("forged"), "the player is not told how the service judged");
        }

        [Test]
        public void ServiceDown_Faulted_Cancelled_Unreadable_ThrowingTransport_AllRefuseTheJoin()
        {
            WorldServiceTicketCheck check = Tickets();
            check.Check(Conn(1), AccountA, "T");
            m_Transport.Calls[0].done.SetException(new System.Net.Http.HttpRequestException("the world service answered 500"));
            Assert.AreEqual(WorldServiceTicketCheck.NoServiceText, check.Check(Conn(1), AccountA, "T").Detail);

            check.Check(Conn(2), AccountA, "T");
            m_Transport.Calls[1].done.SetCanceled();
            Assert.AreEqual(WorldServiceTicketCheck.NoServiceText, check.Check(Conn(2), AccountA, "T").Detail);

            check.Check(Conn(3), AccountA, "T");
            m_Transport.Calls[2].done.SetResult("<html>not json");
            Assert.AreEqual(KnJoinStatus.Refused, check.Check(Conn(3), AccountA, "T").Status);

            m_Transport.ThrowOnCall = true;
            Assert.AreEqual(WorldServiceTicketCheck.NoServiceText, Tickets().Check(Conn(4), AccountA, "T").Detail);
        }

        [Test]
        public void Forget_DropsTheCallState_SoTheConnectionNumberCanBeReused()
        {
            WorldServiceTicketCheck check = Tickets();
            check.Check(Conn(1), AccountA, "T");
            check.Forget(Conn(1));
            check.Check(Conn(1), AccountA, "T");
            Assert.AreEqual(2, m_Transport.Calls.Count);
        }

        [Test]
        public void InsideTheJoinCheck_TheTicketRidesInTheSeventhPart_AndComesAfterTheProofAndTheBanCheck()
        {
            var check = Check(new ScriptedProof(), Tickets(requireTicket: true));
            KnJoinVerdict first = check.Poll(Conn(1), Payload(ticket: "T9"));
            Assert.AreEqual(KnJoinStatus.Pending, first.Status);
            StringAssert.Contains("\"ticket\":\"T9\"", m_Transport.Calls[0].json);
            m_Transport.Calls[0].done.SetResult(FakeTicketTransport.Granted(AccountA));
            KnJoinVerdict v = check.Poll(Conn(1), Payload(ticket: "T9"));
            Assert.AreEqual(KnJoinStatus.Accepted, v.Status);
            Assert.AreEqual(AccountA, v.Account);

            ((FakeBans)BanLookup.Current).Banned[AccountA] = "banned";
            var banned = Check(new ScriptedProof(), Tickets(requireTicket: true));
            Assert.AreEqual(KnRefuseReason.Banned, banned.Poll(Conn(2), Payload(ticket: "T9")).Reason);
            Assert.AreEqual(1, m_Transport.Calls.Count, "a banned account never reaches the world service");
        }

        // ------------------------------------------------------------------ through the approval of a test world

        [Test]
        public void World_AGoodTicketJoins()
        {
            using var tw = new KnTestWorld { JoinPayload = Payload(ticket: "T1") };
            tw.CreateWorlds(1);
            Install(tw, new ScriptedProof(), Tickets(requireTicket: true));
            tw.Listen();
            tw.ConnectClient(tw.Clients[0]);
            Assert.GreaterOrEqual(tw.TickUntil(() => m_Transport.Calls.Count == 1, 300), 0, "the service was never asked");
            tw.Tick(5);
            Assert.IsFalse(KnTestWorld.HasNetworkId(tw.Clients[0]), "still waiting for the service");
            m_Transport.Calls[0].done.SetResult(FakeTicketTransport.Granted(AccountA));
            AssertJoined(tw, tw.Clients[0]);
            Assert.IsTrue(KnArrivals.TryTake(AccountA, out _), "the arrival waits for the player body");
        }

        [Test]
        public void World_ABadTicket_IsRefused_AsBadTicket()
        {
            using var tw = new KnTestWorld { JoinPayload = Payload(ticket: "T2") };
            tw.CreateWorlds(1);
            Install(tw, new ScriptedProof(), Tickets(requireTicket: true));
            tw.Listen();
            tw.ConnectClient(tw.Clients[0]);
            Assert.GreaterOrEqual(tw.TickUntil(() => m_Transport.Calls.Count == 1, 300), 0);
            m_Transport.Calls[0].done.SetResult(FakeTicketTransport.Denied);
            AssertRefused(tw, tw.Clients[0], KnRefuseReason.BadTicket);
        }

#if UNITY_SERVER || UNITY_EDITOR
        // ------------------------------------------------------------------ the real HTTP transport against a fake service on a socket

        [Test]
        public void HttpTransport_PostsJsonWithTheKey_AndTheCheckAcceptsWhatTheFakeServiceGrants()
        {
            var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            listener.Start();
            int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
            string seenKey = null, seenBody = null, seenPath = null;
            var serving = Task.Run(() =>
            {
                for (int i = 0; i < 2; i++)
                {
                    using var client = listener.AcceptTcpClient();
                    client.ReceiveTimeout = 5000;
                    var stream = client.GetStream();
                    ControlPageRules.Request request = ControlPageRules.Read(stream);
                    seenKey = request.Header("X-KN-Key");
                    seenBody = request.Body;
                    seenPath = request.Path;
                    string body = i == 0 ? FakeTicketTransport.Granted(AccountA) : "";
                    string head = i == 0 ? "HTTP/1.1 200 OK\r\n" : "HTTP/1.1 500 Internal Server Error\r\n";
                    byte[] bytes = System.Text.Encoding.UTF8.GetBytes(head + "Content-Type: application/json\r\nContent-Length: " + body.Length + "\r\nConnection: close\r\n\r\n" + body);
                    stream.Write(bytes, 0, bytes.Length);
                }
            });
            try
            {
                using var http = new KnHttpTicketTransport("http://127.0.0.1:" + port, "service-key", 5);
                var check = new WorldServiceTicketCheck(http, "pc:7778", true, m_Log.Add);
                KnJoinVerdict v = default;
                for (int i = 0; i < 400; i++)
                {
                    v = check.Check(Conn(1), AccountA, "REAL");
                    if (v.Status != KnJoinStatus.Pending) break;
                    System.Threading.Thread.Sleep(10);
                }
                Assert.AreEqual(KnJoinStatus.Accepted, v.Status, string.Join(" | ", m_Log));
                Assert.AreEqual("service-key", seenKey);
                Assert.AreEqual("/world/redeem", seenPath);
                StringAssert.Contains("\"ticket\":\"REAL\"", seenBody);

                for (int i = 0; i < 400; i++)
                {
                    v = check.Check(Conn(2), AccountA, "REAL2");
                    if (v.Status != KnJoinStatus.Pending) break;
                    System.Threading.Thread.Sleep(10);
                }
                Assert.AreEqual(KnJoinStatus.Refused, v.Status, "an HTTP error from the service refuses the join");
                Assert.AreEqual(WorldServiceTicketCheck.NoServiceText, v.Detail);
                serving.Wait(5000);
            }
            finally
            {
                listener.Stop();
            }
        }
#endif
    }
}
