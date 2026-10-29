using KryticalNova.Core;
using NUnit.Framework;

namespace KryticalNova.Tests
{
    /// <summary>PlayerKey: minted once per connection, unique, never reused, gone after a reset.</summary>
    public sealed class PlayerKeysTests
    {
        [SetUp] public void SetUp() => PlayerKeys.Reset();
        [TearDown] public void TearDown() => PlayerKeys.Reset();

        [Test]
        public void None_IsNotValid_AndMintedKeysAre()
        {
            Assert.IsFalse(PlayerKey.None.IsValid);
            Assert.IsTrue(PlayerKeys.Mint(0).IsValid); // client id 0 (the host) is a real connection
            Assert.AreEqual("P-none", PlayerKey.None.ToString());
        }

        [Test]
        public void Mint_SameConnectionTwice_GivesSameKey_DifferentConnectionsDiffer()
        {
            PlayerKey a = PlayerKeys.Mint(7);
            Assert.AreEqual(a, PlayerKeys.Mint(7));
            PlayerKey b = PlayerKeys.Mint(8);
            Assert.AreNotEqual(a, b);
            Assert.IsTrue(a != b);
            Assert.AreEqual(2, PlayerKeys.Count);
        }

        [Test]
        public void MapsBothWays()
        {
            PlayerKey a = PlayerKeys.Mint(42);
            Assert.IsTrue(PlayerKeys.TryGetConnection(a, out ulong connection));
            Assert.AreEqual(42UL, connection);
            Assert.AreEqual(a, PlayerKeys.Find(42));
            Assert.AreEqual(PlayerKey.None, PlayerKeys.Find(43)); // Find never mints
            Assert.AreEqual(1, PlayerKeys.Count);
        }

        [Test]
        public void Release_KillsTheKey_AndTheNextConnectionOnTheSameIdGetsAFreshOne()
        {
            PlayerKey first = PlayerKeys.Mint(5);
            PlayerKeys.Release(5);
            Assert.IsFalse(PlayerKeys.IsAlive(first));
            Assert.IsFalse(PlayerKeys.TryGetConnection(first, out _));
            PlayerKey second = PlayerKeys.Mint(5); // the transport reused the id
            Assert.AreNotEqual(first, second);
            Assert.IsTrue(second.Value > first.Value);
            Assert.IsFalse(PlayerKeys.IsAlive(first));
            Assert.IsTrue(PlayerKeys.IsAlive(second));
        }

        [Test]
        public void ReleasingAnUnknownConnection_DoesNothing()
        {
            PlayerKeys.Mint(1);
            PlayerKeys.Release(99);
            Assert.AreEqual(1, PlayerKeys.Count);
        }

        [Test]
        public void Reset_ClearsEverything_AndRestartsTheCount()
        {
            PlayerKey a = PlayerKeys.Mint(1);
            PlayerKeys.Reset();
            Assert.AreEqual(0, PlayerKeys.Count);
            Assert.IsFalse(PlayerKeys.IsAlive(a));
            Assert.AreEqual(a, PlayerKeys.Mint(1)); // a new session counts from 1 again
        }
    }
}
