using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using KryticalNova.Core;

namespace KryticalNova.Networking.Entities.Tests
{
    /// <summary>
    /// Perf round 3, R3-7: a mass leave is spread over ticks (at most <see cref="ConnectionClosedSystem.MaxLeavesPerTick"/> a tick)
    /// and gives the same answer as handling every close at once: each player handled exactly once, in the order they closed, with the
    /// account still readable by the handlers; a queued player whose account joins again is handled in that tick.
    /// </summary>
    public class ConnectionClosedQueueTests
    {
        sealed class Recorder : IKnConnectionClosedHandler
        {
            public readonly List<PlayerKey> Order = new List<PlayerKey>();
            public readonly List<string> AccountSeen = new List<string>();
            public bool KeyWasDead = true;

            public void OnConnectionClosed(PlayerKey key)
            {
                Order.Add(key);
                AccountSeen.Add(ServerAccounts.TryGet(key, out ServerAccounts.Entry e) ? e.Account : null);
                KeyWasDead &= !PlayerKeys.IsAlive(key);
            }
        }

        World m_World;
        ConnectionClosedSystem m_System;
        Recorder m_Recorder;
        ulong m_Serial = 9_000_000;

        [SetUp]
        public void SetUp()
        {
            m_World = new World("kn-closed-queue");
            m_System = m_World.GetOrCreateSystemManaged<ConnectionClosedSystem>();
            m_Recorder = new Recorder();
            m_System.Handlers.Add(m_Recorder);
        }

        [TearDown]
        public void TearDown()
        {
            m_World.Dispose();
        }

        // A connection that closed: only its cleanup key is left (as after Netcode destroys the connection entity).
        PlayerKey Close(string account)
        {
            ulong serial = ++m_Serial;
            PlayerKey key = PlayerKeys.Mint(serial);
            ServerAccounts.Remember(key, account, "");
            Entity e = m_World.EntityManager.CreateEntity();
            m_World.EntityManager.AddComponentData(e, new ConnectionPlayerKey { Key = key.Value, Serial = serial });
            return key;
        }

        List<PlayerKey> CloseMany(int n, string prefix)
        {
            var keys = new List<PlayerKey>();
            for (int i = 0; i < n; i++) keys.Add(Close(prefix + i));
            return keys;
        }

        int Remaining()
        {
            using var q = m_World.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<ConnectionPlayerKey>());
            return q.CalculateEntityCount();
        }

        List<PlayerKey> RunAll(int cap, int n, out int ticks)
        {
            m_System.MaxLeavesPerTick = cap;
            List<PlayerKey> closed = CloseMany(n, "acct-" + cap + "-");
            ticks = 0;
            while (Remaining() > 0 && ticks < 1000) { m_System.Update(); ticks++; }
            return closed;
        }

        [Test]
        public void TwoHundredCloses_CappedAtTen_GiveTheSameAnswerAsAllAtOnce_OverTwentyTicks()
        {
            List<PlayerKey> uncappedClosed = RunAll(0, 200, out int uncappedTicks);
            var uncapped = new List<PlayerKey>(m_Recorder.Order);
            Assert.AreEqual(1, uncappedTicks, "uncapped: everything in one tick, as before R3-7");
            CollectionAssert.AreEquivalent(uncappedClosed, uncapped);

            m_Recorder.Order.Clear();
            m_Recorder.AccountSeen.Clear();
            List<PlayerKey> cappedClosed = RunAll(10, 200, out int cappedTicks);
            Assert.AreEqual(20, cappedTicks, "10 a tick");
            Assert.AreEqual(200, m_Recorder.Order.Count, "each player handled exactly once");
            CollectionAssert.AllItemsAreUnique(m_Recorder.Order);
            CollectionAssert.AreEquivalent(cappedClosed, m_Recorder.Order);
            Assert.AreEqual(0, m_System.Queued);
            Assert.IsTrue(m_Recorder.KeyWasDead, "the key is released before the handlers run, as before");
            for (int i = 0; i < m_Recorder.AccountSeen.Count; i++)
                Assert.IsNotNull(m_Recorder.AccountSeen[i], "the handlers still read the account (it is forgotten after them)");
            for (int i = 0; i < cappedClosed.Count; i++)
                Assert.IsFalse(ServerAccounts.TryGet(cappedClosed[i], out _), "every account is forgotten in the end");
        }

        [Test]
        public void WaitingCloses_KeepTheirPlace_AheadOfLaterOnes()
        {
            m_System.MaxLeavesPerTick = 10;
            List<PlayerKey> first = CloseMany(25, "early-");
            m_System.Update();                       // 10 of the first 25
            List<PlayerKey> later = CloseMany(5, "late-");
            m_System.Update();                       // 10 more of the first 25
            m_System.Update();                       // the last 5 of the first, then the 5 later ones
            Assert.AreEqual(30, m_Recorder.Order.Count);
            var firstHandled = m_Recorder.Order.GetRange(0, 25);
            CollectionAssert.AreEquivalent(first, firstHandled, "the 25 that closed first are all handled before any later close");
            CollectionAssert.AreEquivalent(later, m_Recorder.Order.GetRange(25, 5));
        }

        [Test]
        public void AQueuedPlayerWhoJoinsAgain_IsHandledInThatTick()
        {
            m_System.MaxLeavesPerTick = 10;
            List<PlayerKey> closed = CloseMany(50, "q-");
            m_System.Update();
            Assert.AreEqual(40, m_System.Queued);
            PlayerKey last = closed[closed.Count - 1];
            Assert.IsFalse(m_Recorder.Order.Contains(last), "precondition: still waiting");

            // the same account opens a new connection
            Entity rejoin = m_World.EntityManager.CreateEntity();
            m_World.EntityManager.AddComponentData(rejoin, new NetworkStreamConnection());
            m_World.EntityManager.AddComponentData(rejoin, new ConnectionAccount { Account = new FixedString64Bytes("q-49"), Proven = true });
            m_System.Update();
            Assert.IsTrue(m_Recorder.Order.Contains(last), "the old session is gone before the new one can load the character");
            Assert.AreEqual(21, m_Recorder.Order.Count, "10 + 10 + the rejoining one");
            Assert.AreEqual(29, m_System.Queued);
        }
    }
}
