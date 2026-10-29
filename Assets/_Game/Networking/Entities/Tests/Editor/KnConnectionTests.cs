using NUnit.Framework;
using Unity.Entities;
using Unity.NetCode;
using KryticalNova.Core;

namespace KryticalNova.Networking.Entities.Tests
{
    /// <summary>M2a: server world plus clients in one process; connect, approve, refuse, PlayerKey. Layer C of the test matrix.</summary>
    public class KnConnectionTests
    {
        [Test]
        public void ServerAndOneClient_Connect_ClientReceivesNetworkId()
        {
            using var tw = new KnTestWorld();
            tw.CreateWorlds(1);
            tw.Connect();
            Assert.IsTrue(KnTestWorld.HasNetworkId(tw.Clients[0]));
            Assert.AreEqual(1, KnTestWorld.Count<NetworkId>(tw.Server));
            Assert.Greater(KnTestWorld.NetworkIdOf(tw.Clients[0]), 0);
        }

        [Test]
        public void ThreeChunkApproval_Succeeds_AndTheCheckSeesTheWholePayload()
        {
            string payload = KnTestQueries.Pattern(1100); // 480 + 480 + 140 = three chunks
            using var tw = new KnTestWorld { JoinPayload = payload };
            tw.CreateWorlds(1);
            var check = new ScriptedJoinCheck();
            tw.Server.GetExistingSystemManaged<KnApprovalAssembler>().JoinCheck = check;
            tw.Connect();
            Assert.IsTrue(KnTestWorld.HasNetworkId(tw.Clients[0]), "not approved. Logs: " + string.Join(" | ", tw.Interesting));
            Assert.AreEqual(1, check.Payloads.Count);
            Assert.AreEqual(payload, check.Payloads[0]);
            Assert.GreaterOrEqual(check.Forgotten, 1, "the check should be told when the connection is decided");
        }

        [Test]
        public void AsyncCheck_StaysPendingForAWhile_ThenApproves()
        {
            using var tw = new KnTestWorld();
            tw.CreateWorlds(1);
            tw.Server.GetExistingSystemManaged<KnApprovalAssembler>().JoinCheck =
                new ScriptedJoinCheck { Rule = (payload, polls) => polls < 8 ? KnJoinVerdict.Pending : KnJoinVerdict.Accept };
            tw.Connect();
            Assert.IsTrue(KnTestWorld.HasNetworkId(tw.Clients[0]));
        }

        [Test]
        public void TooManyChunks_IsRefused_AndTheConnectionCloses()
        {
            using var tw = new KnTestWorld { JoinPayload = KnTestQueries.Pattern(1100) }; // three chunks
            tw.CreateWorlds(1);
            tw.Server.EntityManager.CreateSingleton(new KnApprovalLimits { MaxChunks = 2, MaxTotalBytes = 4000, MaxSeconds = 10f });
            AssertRefused(tw, KnRefuseReason.TooManyChunks);
        }

        [Test]
        public void TooManyBytes_IsRefused_AndTheConnectionCloses()
        {
            using var tw = new KnTestWorld { JoinPayload = KnTestQueries.Pattern(1100) };
            tw.CreateWorlds(1);
            tw.Server.EntityManager.CreateSingleton(new KnApprovalLimits { MaxChunks = 8, MaxTotalBytes = 600, MaxSeconds = 10f });
            AssertRefused(tw, KnRefuseReason.TooManyBytes);
        }

        [Test]
        public void TooSlowAnAnswer_IsRefused_AsTimedOut()
        {
            using var tw = new KnTestWorld();
            tw.CreateWorlds(1);
            var check = new ScriptedJoinCheck { Rule = (payload, polls) => KnJoinVerdict.Pending }; // never answers
            tw.Server.GetExistingSystemManaged<KnApprovalAssembler>().JoinCheck = check;
            tw.Server.EntityManager.CreateSingleton(new KnApprovalLimits { MaxChunks = 8, MaxTotalBytes = 4000, MaxSeconds = 1f });
            AssertRefused(tw, KnRefuseReason.TimedOut);
            Assert.GreaterOrEqual(check.Forgotten, 1);
        }

        [Test]
        public void RefusedByTheCheck_ClientGetsJoinRefusedRpcWithTheReason()
        {
            using var tw = new KnTestWorld { JoinPayload = "nope" };
            tw.CreateWorlds(1);
            tw.Server.GetExistingSystemManaged<KnApprovalAssembler>().JoinCheck = new ScriptedJoinCheck
            {
                Rule = (payload, polls) => payload == "nope" ? KnJoinVerdict.Refuse(KnRefuseReason.Refused, "closed for testing") : KnJoinVerdict.Accept,
            };
            AssertRefused(tw, KnRefuseReason.Refused);
            Assert.AreEqual("closed for testing", KnTestWorld.Singleton<KnJoinRefusal>(tw.Clients[0]).Detail.ToString());
        }

        [Test]
        public void PlayerKey_IsMintedOncePerConnection_AndNeverGivenOutAgain()
        {
            using var tw = new KnTestWorld();
            tw.CreateWorlds(2);
            var closed = new RecordingClosedHandler();
            tw.Server.GetExistingSystemManaged<ConnectionClosedSystem>().Handlers.Add(closed);
            tw.Connect();
            tw.Tick(5);

            var first = KnTestQueries.ServerKeys(tw);
            Assert.AreEqual(2, first.Count);
            Assert.AreNotEqual(first[0].key, first[1].key);
            Assert.IsTrue(first.TrueForAll(k => k.key != 0));
            Assert.AreEqual(2, PlayerKeys.Count);

            tw.Tick(30); // no re-minting while the connections live
            CollectionAssert.AreEquivalent(first, KnTestQueries.ServerKeys(tw));
            Assert.AreEqual(2, PlayerKeys.Count);

            // the first player leaves
            int leavingId = KnTestWorld.NetworkIdOf(tw.Clients[0]);
            ulong leavingKey = first.Find(k => k.id == leavingId).key;
            ulong stayingKey = first.Find(k => k.id != leavingId).key;
            KnTestQueries.Disconnect(tw.Clients[0]);
            Assert.GreaterOrEqual(tw.TickUntil(() => KnTestWorld.Count<NetworkId>(tw.Server) == 1, 300), 0, "server never saw the leave");
            tw.Tick(3);
            Assert.IsFalse(PlayerKeys.IsAlive(new PlayerKey(leavingKey)), "the key of a closed connection must be dead");
            Assert.IsTrue(PlayerKeys.IsAlive(new PlayerKey(stayingKey)));
            CollectionAssert.AreEqual(new[] { new PlayerKey(leavingKey) }, closed.Closed, "the close hook runs exactly once");

            // a later player (netcode may give it the freed NetworkId) gets a key nobody had
            World late = tw.AddClient("kn-late");
            Assert.GreaterOrEqual(tw.TickUntil(() => KnTestWorld.HasNetworkId(late), 300), 0, "late client never connected");
            tw.Tick(5);
            var now = KnTestQueries.ServerKeys(tw);
            Assert.AreEqual(2, now.Count);
            int lateId = KnTestWorld.NetworkIdOf(late);
            ulong lateKey = now.Find(k => k.id == lateId).key;
            Assert.AreNotEqual(leavingKey, lateKey);
            Assert.AreNotEqual(stayingKey, lateKey);
            Assert.Greater(lateKey, stayingKey, "keys count up and are not reused");
            UnityEngine.Debug.Log($"[KnTest] NetworkId of the first player {leavingId}, of the late player {lateId}; keys {leavingKey}, {stayingKey}, {lateKey}");
        }

        private static void AssertRefused(KnTestWorld tw, KnRefuseReason reason)
        {
            tw.Listen();
            World client = tw.Clients[0];
            tw.ConnectClient(client);
            int used = tw.TickUntil(() => KnTestWorld.Count<KnJoinRefusal>(client) == 1 && KnTestWorld.Count<NetworkStreamConnection>(client) == 0, 400);
            Assert.GreaterOrEqual(used, 0, "no refusal arrived and closed the connection. Logs: " + string.Join(" | ", tw.Interesting));
            Assert.AreEqual((byte)reason, KnTestWorld.Singleton<KnJoinRefusal>(client).Reason);
            Assert.IsFalse(KnTestWorld.HasNetworkId(client), "a refused client must not get a NetworkId");
            Assert.AreEqual(0, KnTestWorld.Count<NetworkId>(tw.Server));
            tw.Tick(3);
            Assert.AreEqual(0, PlayerKeys.Count, "a refused connection must not hold a PlayerKey");
        }
    }
}
