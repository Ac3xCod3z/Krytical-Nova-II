using KryticalNova.Core;
using KryticalNova.Networking;
using NUnit.Framework;

namespace KryticalNova.Tests
{
    /// <summary>The NGO adapter: client id to PlayerKey and back; ServerAccounts is kept by key.</summary>
    public sealed class NetPlayerKeysTests
    {
        [SetUp] public void SetUp() => PlayerKeys.Reset();
        [TearDown] public void TearDown() => PlayerKeys.Reset();

        [Test]
        public void ClientId_MapsToAKey_AndBack()
        {
            PlayerKey key = NetPlayerKeys.Of(12);
            Assert.IsTrue(NetPlayerKeys.TryGetClientId(key, out ulong id));
            Assert.AreEqual(12UL, id);
            Assert.AreEqual(key, NetPlayerKeys.Find(12));
        }

        [Test]
        public void ServerAccounts_AreKeptPerKey_AndAReusedClientIdDoesNotInherit()
        {
            PlayerKey old = NetPlayerKeys.Of(3);
            ServerAccounts.Remember(old, "acct-A", "");
            Assert.IsTrue(ServerAccounts.TryGet(old, out ServerAccounts.Entry entry));
            Assert.AreEqual("acct-A", entry.Account);

            NetPlayerKeys.Release(3); // disconnect; the transport hands id 3 to the next player
            PlayerKey next = NetPlayerKeys.Of(3);
            Assert.IsFalse(ServerAccounts.TryGet(next, out _));
            Assert.IsFalse(ServerAccounts.TryGet(PlayerKey.None, out _));
            ServerAccounts.Forget(old);
        }
    }
}
