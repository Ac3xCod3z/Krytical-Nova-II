using System;
using KryticalNova.Brains;
using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace KryticalNova.Hostiles.Entities.Tests
{
    /// <summary>A plain world with the Brain's and the Hostiles mind's systems so far (M5.3b1), ticked by hand at 30 Hz.</summary>
    public sealed class HostileTestWorld : IDisposable
    {
        public const int TickRate = 30;
        public readonly World World;
        public uint Tick;
        readonly SystemHandle m_Sense, m_Clock, m_Threat, m_Scan;
        readonly EntityQuery m_TickQuery, m_SenseQuery;

        public HostileTestWorld()
        {
            World = new World("HostileTestWorld");
            EntityManager em = World.EntityManager;
            var brain = ScriptableObject.CreateInstance<BrainSettings>();
            var hostile = ScriptableObject.CreateInstance<HostileBrainSettings>();
            em.CreateSingleton(BrainConfig.From(brain, TickRate));
            em.CreateSingleton(HostileBrainConfig.From(hostile, TickRate));
            UnityEngine.Object.DestroyImmediate(brain);
            UnityEngine.Object.DestroyImmediate(hostile);
            em.CreateSingleton(new BrainTick());
            m_Sense = World.CreateSystem<SenseBuildSystem>();
            m_Clock = World.CreateSystem<ThinkClockSystem>();
            m_Threat = World.CreateSystem<HostileThreatSystem>();
            m_Scan = World.CreateSystem<HostileSenseScanSystem>();
            m_TickQuery = em.CreateEntityQuery(typeof(BrainTick));
            m_SenseQuery = em.CreateEntityQuery(typeof(SenseWorld));
        }

        public EntityManager Em => World.EntityManager;

        public Entity Hostile(float3 at, byte behaviour, uint stableId = 1, float aggro = 9f, float range = 2f)
        {
            EntityManager em = Em;
            Entity e = em.CreateEntity(typeof(BrainTag), typeof(BrainActive), typeof(BrainClock), typeof(LocalTransform), typeof(HostileBrain),
                typeof(HostileTarget), typeof(HostileLife));
            em.AddBuffer<BrainEvent>(e);
            em.AddBuffer<ThreatEntry>(e);
            em.SetComponentData(e, LocalTransform.FromPosition(at));
            em.SetComponentData(e, new BrainClock { StableId = stableId });
            em.SetComponentData(e, new HostileLife { Behaviour = behaviour, AggroRadius = aggro, AttackRange = range, ReachAbove = 3f });
            return e;
        }

        /// <summary>Players by ghost id 100, 101... (flags Alive unless given).</summary>
        public void SetPlayers(params (float3 feet, byte flags)[] players)
        {
            SenseWorld sense = m_SenseQuery.GetSingleton<SenseWorld>();
            sense.Players.Clear();
            for (int i = 0; i < players.Length; i++)
                sense.Players.Add(new SensedPlayer { GhostId = 100 + i, Feet = players[i].feet, Flags = players[i].flags });
        }

        public void Hit(Entity hostile, int ghostId, float amount, byte kind = BrainEventKind.Damaged) =>
            Em.GetBuffer<BrainEvent>(hostile).Add(new BrainEvent { Kind = kind, GhostId = ghostId, Amount = amount, Tick = Tick + 1 });

        public void Step(int ticks = 1)
        {
            for (int i = 0; i < ticks; i++)
            {
                Tick++;
                m_TickQuery.SetSingleton(new BrainTick { Tick = Tick });
                m_Sense.Update(World.Unmanaged);
                m_Clock.Update(World.Unmanaged);
                m_Threat.Update(World.Unmanaged);
                m_Scan.Update(World.Unmanaged);
            }
        }

        public HostileTarget Target(Entity e) => Em.GetComponentData<HostileTarget>(e);
        public HostileBrain Brain(Entity e) => Em.GetComponentData<HostileBrain>(e);

        public void Dispose()
        {
            if (World.IsCreated) World.Dispose();
        }
    }

    public sealed class ThreatTests
    {
        const byte Alive = SensedFlags.Alive;

        [Test]
        public void TheFirstHitTakesTheTarget_AChallengerNeeds110InReach_130OutOfReach()
        {
            using var w = new HostileTestWorld();
            Entity h = w.Hostile(float3.zero, HostileBehaviourKind.FightsBack);
            w.SetPlayers((new float3(1f, 0f, 0f), Alive), (new float3(1.5f, 0f, 0f), Alive), (new float3(20f, 0f, 0f), Alive));
            w.Hit(h, 100, 100f);
            w.Step();
            Assert.AreEqual(100, w.Target(h).GhostId, "H03: the hit turns it the same tick");

            w.Hit(h, 101, 110f);   // in reach, exactly 110 %: not enough
            w.Step();
            Assert.AreEqual(100, w.Target(h).GhostId);
            w.Hit(h, 101, 1f);     // 111 %
            w.Step();
            Assert.AreEqual(101, w.Target(h).GhostId, "Q5: above 110 % in reach");

            w.Hit(h, 102, 140f);   // out of reach: 140 < 1.3 x 111
            w.Step();
            Assert.AreEqual(101, w.Target(h).GhostId, "out of reach needs 130 %");
            w.Hit(h, 102, 5f);     // 145 > 144.3
            w.Step();
            Assert.AreEqual(102, w.Target(h).GhostId);
        }

        [Test]
        public void SixPlayersKept_TheLowestReplaced_HealsCountHalf_OldThreatDecays()
        {
            using var w = new HostileTestWorld();
            Entity h = w.Hostile(float3.zero, HostileBehaviourKind.FightsBack);
            for (int i = 0; i < 6; i++) w.Hit(h, 200 + i, 10f + i);
            w.Step();
            w.Hit(h, 300, 5f);     // lower than everyone: not kept
            w.Step();
            Assert.AreEqual(6, w.Em.GetBuffer<ThreatEntry>(h).Length);
            Assert.IsFalse(Has(w, h, 300));
            w.Hit(h, 301, 50f);    // replaces the lowest (200)
            w.Step();
            Assert.IsTrue(Has(w, h, 301));
            Assert.IsFalse(Has(w, h, 200));

            w.Hit(h, 205, 20f, BrainEventKind.Healed);   // a heal of 20 adds 10
            w.Step();
            Assert.AreEqual(15f + 10f, Of(w, h, 205), 1e-4f);

            float before = Of(w, h, 301);
            w.Step(5 * HostileTestWorld.TickRate + 30);   // 5 s then 1 s more
            Assert.Less(Of(w, h, 301), before, "decays after 5 s without new threat");
            Assert.Greater(Of(w, h, 301), before * 0.9f, "5 % a second");
        }

        static bool Has(HostileTestWorld w, Entity h, int ghostId) => Of(w, h, ghostId) > 0f;

        static float Of(HostileTestWorld w, Entity h, int ghostId)
        {
            foreach (ThreatEntry t in w.Em.GetBuffer<ThreatEntry>(h)) if (t.GhostId == ghostId) return t.Threat;
            return 0f;
        }
    }

    public sealed class SightScanTests
    {
        const byte Alive = SensedFlags.Alive;

        [Test]
        public void AnAttacksOnSightHostile_SeesThePlayerWhoStepsIntoItsAggroRadius()
        {
            using var w = new HostileTestWorld();
            Entity h = w.Hostile(float3.zero, HostileBehaviourKind.AttacksOnSight);
            w.SetPlayers((new float3(13f, 0f, 0f), Alive));
            w.Step(10);
            Assert.AreEqual(0, w.Brain(h).Saw, "13 m: outside 9 m");
            w.SetPlayers((new float3(7f, 0f, 0f), Alive));
            int ticks = 0;
            while (w.Brain(h).Saw == 0 && ticks < 10) { w.Step(); ticks++; }
            Assert.LessOrEqual(ticks, 3, "seen on its next think (tier 0 near a player)");
            Assert.AreEqual(100, w.Target(h).GhostId);
        }

        [Test]
        public void FightsBack_NeutralGround_AndIgnored_AreNeverSeen()
        {
            using var w = new HostileTestWorld();
            Entity fightsBack = w.Hostile(float3.zero, HostileBehaviourKind.FightsBack, 1);
            Entity onSight = w.Hostile(new float3(100f, 0f, 0f), HostileBehaviourKind.AttacksOnSight, 2);
            w.SetPlayers((new float3(3f, 0f, 0f), Alive), (new float3(103f, 0f, 0f), (byte)(Alive | SensedFlags.Neutral)),
                (new float3(97f, 0f, 0f), (byte)(Alive | SensedFlags.Ignored)));
            w.Step(10);
            Assert.AreEqual(0, w.Brain(fightsBack).Saw, "H02: FightsBack never by sight");
            Assert.AreEqual(0, w.Brain(onSight).Saw, "neutral ground and ignored players are not seen (H17: sight only)");
        }
    }
}
