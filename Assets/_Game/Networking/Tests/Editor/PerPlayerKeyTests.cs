using KryticalNova.Core;
using KryticalNova.Networking;
using NUnit.Framework;

namespace KryticalNova.Networking.Tests
{
    /// <summary>The server's per-player books (request allowances, lag events, access record) are kept by PlayerKey, never by the transport's client id.</summary>
    public sealed class PerPlayerKeyTests
    {
        [SetUp]
        public void SetUp()
        {
            PlayerKeys.Reset();
            RpcBudget.Configure(new CallLimits());
        }

        [TearDown]
        public void TearDown()
        {
            RpcBudget.Configure(null);
            PlayerKeys.Reset();
        }

        [Test]
        public void RpcBudget_AllowanceBelongsToTheKey_AndForgetEndsIt()
        {
            PlayerKey a = NetPlayerKeys.Of(7), b = NetPlayerKeys.Of(8);
            Assert.AreEqual(RpcBudget.SpendResult.Allowed, RpcBudget.Spend(a, CallKind.Chat, 0f));
            Assert.IsTrue(RpcBudget.IsTracking(a));
            Assert.IsFalse(RpcBudget.IsTracking(b), "another player has no books yet");
            RpcBudget.Forget(a);
            Assert.IsFalse(RpcBudget.IsTracking(a));
        }

        [Test]
        public void RpcBudget_ReusedClientId_GetsAFreshAllowance()
        {
            PlayerKey first = NetPlayerKeys.Of(3);
            CallLimit limit = RpcBudget.Limits.For(CallKind.Chat);
            for (int i = 0; i < (int)limit.burst; i++) RpcBudget.Spend(first, CallKind.Chat, 0f);
            Assert.AreEqual(RpcBudget.SpendResult.Over, RpcBudget.Spend(first, CallKind.Chat, 0f), "the burst is spent");
            RpcBudget.Forget(3UL);
            NetPlayerKeys.Release(3);
            PlayerKey second = NetPlayerKeys.Of(3); // the transport reuses id 3
            Assert.AreNotEqual(first, second);
            Assert.AreEqual(RpcBudget.SpendResult.Allowed, RpcBudget.Spend(second, CallKind.Chat, 0f), "a new player is not held to the old one's spent burst");
        }

        [Test]
        public void NetworkHealth_CountsPerKey_WithinTheWindow()
        {
            PlayerKey a = NetPlayerKeys.Of(1), b = NetPlayerKeys.Of(2);
            NetworkHealth.Record(a, LagEvent.LateHit, 10f);
            NetworkHealth.Record(a, LagEvent.LateHit, 20f);
            NetworkHealth.Record(a, LagEvent.GoneTarget, 20f);
            Assert.AreEqual(2, NetworkHealth.Count(a, LagEvent.LateHit, 30f));
            Assert.AreEqual(1, NetworkHealth.Count(a, LagEvent.GoneTarget, 30f));
            Assert.AreEqual(0, NetworkHealth.Count(b, LagEvent.LateHit, 30f));
            Assert.AreEqual(1, NetworkHealth.Count(a, LagEvent.LateHit, 10f + NetworkHealth.WindowSeconds + 5f), "the first one aged out");
            NetworkHealth.Forget(a);
            Assert.AreEqual(0, NetworkHealth.Count(a, LagEvent.LateHit, 30f));
        }

        [Test]
        public void NetworkHealth_ClientIdWrappers_GoThroughTheKey()
        {
            NetworkHealth.Record(5UL, LagEvent.LateHit);
            Assert.AreEqual(1, NetworkHealth.Count(5UL, LagEvent.LateHit));
            Assert.AreEqual(1, NetworkHealth.Count(NetPlayerKeys.Find(5), LagEvent.LateHit));
            NetworkHealth.Forget(5UL);
            Assert.AreEqual(0, NetworkHealth.Count(5UL, LagEvent.LateHit));
        }

        [Test]
        public void NetworkHealth_PingOfADeadKeyIsUnknown() => Assert.AreEqual(-1, NetworkHealth.PingMs(new PlayerKey(99)));

        [Test]
        public void ServerRecords_RemembersWhoIsInGame_ByKey()
        {
            ServerRecords.Joined(4);
            PlayerKey key = NetPlayerKeys.Find(4);
            Assert.IsTrue(key.IsValid);
            Assert.IsTrue(ServerRecords.IsInGame(key));
            ServerRecords.Left(4);
            Assert.IsFalse(ServerRecords.IsInGame(key));
        }
    }
}
