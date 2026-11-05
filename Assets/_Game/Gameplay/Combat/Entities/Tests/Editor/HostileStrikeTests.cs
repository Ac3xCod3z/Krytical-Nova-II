using KryticalNova.Core;
using KryticalNova.Networking.Entities.Tests;
using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;

namespace KryticalNova.Combat.Entities.Tests
{
    /// <summary>M4 step 9: a Hostile's blow reaches the hit player's game (number and hurt effect), and the server's queue drains.</summary>
    public sealed class HostileStrikeTests
    {
        [Test]
        public void FortyBlowsAndHurts_AllReachTheHitPlayersGame_ByTheReliableMessage()
        {
            using var tw = new KnTestWorld();
            tw.CreateWorlds(1);
            tw.Connect();
            int id = KnTestWorld.NetworkIdOf(tw.Clients[0]);
            var strikes = tw.Clients[0].GetExistingSystemManaged<HostileStrikeClientSystem>();
            var hurts = tw.Clients[0].GetExistingSystemManaged<HurtClientSystem>();
            Assert.IsNotNull(strikes, "the client world has the blow system");
            Assert.IsNotNull(hurts, "the client world has the hurt system");
            for (int i = 0; i < 40; i++)
            {
                Assert.IsTrue(HitNumbers.SendTo(tw.Server, id, HostileStrikes.Of(new DamageResult { DamageDealt = 5 + i, IsCritical = i % 4 == 0 }, i % 7 == 0)));
                Assert.IsTrue(HitNumbers.SendTo(tw.Server, id, HurtServerSystem.Of(0.01f * i)));
            }
            tw.TickUntil(() => strikes.Received >= 40 && hurts.Received >= 40, 300);
            tw.Tick(10);
            Assert.AreEqual(40, strikes.Received, "every blow, once");
            Assert.AreEqual(40, hurts.Received, "every hurt, once");
        }

        [Test]
        public void ABlow_ShowsWhatNgosStrikeShows()
        {
            HostileStrikeRpc sent = HostileStrikes.Of(new DamageResult { DamageDealt = 12.5f, IsCritical = true, IsReflected = true, ReflectedDamage = 3f }, true);
            Assert.AreEqual(HostileStrikes.Critical | HostileStrikes.Reflected | HostileStrikes.Heavy, sent.Flags);
            DamageResult shown = HostileStrikes.ResultOf(sent);
            Assert.AreEqual(12.5f, shown.DamageDealt, 1e-3);
            Assert.IsTrue(shown.IsCritical);
            Assert.IsFalse(shown.IsEvaded);
            Assert.IsTrue(shown.IsReflected);
            Assert.AreEqual(12.5f, shown.ReflectedDamage, 1e-3, "NGO ShowStrike: a reflected blow shows its damage as reflected");
            Assert.AreEqual(HostileStrikes.Evaded, HostileStrikes.Of(new DamageResult { IsEvaded = true }, false).Flags);
            Assert.AreEqual(1000, HurtServerSystem.Of(1.7f).FractionMilli, "a share of Max HP, never more than all of it");
            Assert.AreEqual(250, HurtServerSystem.Of(0.25f).FractionMilli);
        }

        [Test]
        public void TheQueue_IsDrainedEachUpdate_AndABlowForNobodyIsDropped()
        {
            using var tw = new KnTestWorld();
            tw.CreateWorlds(1);
            tw.Connect();
            var server = tw.Server.GetExistingSystemManaged<HostileStrikeServerSystem>();
            Assert.IsNotNull(server, "the server world has the blow system");
            for (int i = 0; i < 3; i++)
                Assert.IsTrue(HostileStrikes.Queue(tw.Server, new HostileStrike
                {
                    Target = Entity.Null,
                    Result = new DamageResult { DamageDealt = 10f, MitigatedPhysicalDamage = 10f },
                    From = new float3(1f, 0f, 0f),
                }));
            tw.Tick(2);
            Assert.AreEqual(3, server.Dropped, "nobody is hit: each blow dropped once");
            Assert.AreEqual(0, server.Applied);
            Assert.AreEqual(0, server.Sent);
            EntityManager em = tw.Server.EntityManager;
            using var query = em.CreateEntityQuery(ComponentType.ReadOnly<HostileStrikeQueue>());
            Assert.AreEqual(0, em.GetBuffer<HostileStrike>(query.GetSingletonEntity()).Length, "the queue is empty");
            Assert.IsFalse(HostileStrikes.Queue(tw.Clients[0], new HostileStrike()), "a client world has no queue");
        }
    }
}
