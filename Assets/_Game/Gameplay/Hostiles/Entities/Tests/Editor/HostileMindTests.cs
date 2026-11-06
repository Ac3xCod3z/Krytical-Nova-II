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
        readonly SystemHandle m_Sense, m_Clock, m_Threat, m_Scan, m_Decide;
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
            m_Decide = World.CreateSystem<HostileDecideSystem>();
            m_TickQuery = em.CreateEntityQuery(typeof(BrainTick));
            m_SenseQuery = em.CreateEntityQuery(typeof(SenseWorld));
        }

        public EntityManager Em => World.EntityManager;

        public Entity Hostile(float3 at, byte behaviour, uint stableId = 1, float aggro = 9f, float range = 2f)
        {
            EntityManager em = Em;
            Entity e = em.CreateEntity(typeof(BrainTag), typeof(BrainActive), typeof(BrainClock), typeof(LocalTransform), typeof(HostileBrain),
                typeof(HostileTarget), typeof(HostileLife), typeof(HostileHome), typeof(BrainMover));
            em.SetComponentData(e, new HostileHome { Home = at, WanderRadius = 8f });
            em.AddBuffer<BrainEvent>(e);
            em.AddBuffer<ThreatEntry>(e);
            em.SetComponentData(e, LocalTransform.FromPosition(at));
            em.SetComponentData(e, new BrainClock { StableId = stableId });
            em.SetComponentData(e, new HostileLife { Behaviour = behaviour, AggroRadius = aggro, AttackRange = range, ReachAbove = 3f, NoticeTicks = 3 });
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
                m_Decide.Update(World.Unmanaged);
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

    public sealed class ReactionTests
    {
        const byte Alive = SensedFlags.Alive;

        [Test]
        public void SightAggro_EngagesWithinScanPlusNotice()
        {
            // plan M5.3: worst 3 (scan) + 3 (notice) = 6 ticks, mean <= 4.5 over many brains
            int worst = 0, total = 0;
            const int brains = 30;
            for (uint id = 1; id <= brains; id++)
            {
                using var w = new HostileTestWorld();
                Entity h = w.Hostile(float3.zero, HostileBehaviourKind.AttacksOnSight, id);
                w.SetPlayers((new float3(13f, 0f, 0f), Alive));
                w.Step(5 + (int)(id % 3));
                w.SetPlayers((new float3(7f, 0f, 0f), Alive));
                int ticks = 0;
                while (w.Brain(h).Mode != HostileMode.Engage && ticks < 20) { w.Step(); ticks++; }
                worst = System.Math.Max(worst, ticks);
                total += ticks;
            }
            Assert.LessOrEqual(worst, 6, "scan + notice (3 + 3 ticks at worst)");
            Assert.LessOrEqual(total / (float)brains, 4.5f, "mean");
        }

        [Test]
        public void HitTurnsSameTick()
        {
            using var w = new HostileTestWorld();
            Entity h = w.Hostile(float3.zero, HostileBehaviourKind.FightsBack);
            w.SetPlayers((new float3(4f, 0f, 0f), Alive));
            w.Step(5);
            Assert.AreEqual(HostileMode.Idle, w.Brain(h).Mode);
            w.Hit(h, 100, 10f);
            w.Step();
            Assert.AreEqual(HostileMode.Engage, w.Brain(h).Mode, "engaged on the tick of the hit");
            Assert.AreEqual(100, w.Target(h).GhostId);
            Assert.AreEqual(w.Tick, w.Brain(h).ModeSinceTick);
        }
    }

    public sealed class HostileBrainTests
    {
        const byte Alive = SensedFlags.Alive;

        static Entity Engaged(HostileTestWorld w, float3 at)
        {
            Entity h = w.Hostile(at, HostileBehaviourKind.FightsBack);
            w.Em.SetComponentData(h, new HostileHome { Home = at, WanderRadius = 8f, ZoneCentre = at.xz, ZoneRadius = 10f, LeashRadius = 15f });
            w.SetPlayers((at + new float3(2f, 0f, 0f), Alive));
            w.Hit(h, 100, 10f);
            w.Step();
            Assert.AreEqual(HostileMode.Engage, w.Brain(h).Mode);
            return h;
        }

        [Test]
        public void Leash_ReturnsAndHealsAtHome()
        {
            using var w = new HostileTestWorld();
            Entity h = Engaged(w, float3.zero);
            w.Em.SetComponentData(h, LocalTransform.FromPosition(new float3(26f, 0f, 0f)));   // zone 10 + leash 15 = 25 m
            w.SetPlayers((new float3(27f, 0f, 0f), Alive));
            w.Hit(h, 100, 5f);
            w.Step();
            Assert.AreEqual(HostileMode.Return, w.Brain(h).Mode, "H14: past the leash it goes home, hit or not");
            Assert.AreEqual(1, w.Brain(h).HealOnHome);
            Assert.AreEqual(BrainMoverFlags.Run, w.Em.GetComponentData<BrainMover>(h).Flags);
            w.Em.SetComponentData(h, LocalTransform.FromPosition(new float3(0.2f, 0f, 0f)));
            w.Step();
            Assert.AreEqual(HostileMode.Idle, w.Brain(h).Mode, "home");
            Assert.AreEqual(0, w.Em.GetBuffer<ThreatEntry>(h).Length, "threat cleared after a leash return");
        }

        [Test]
        public void GiveUp_After5sBeyondSight_ReturnsWithoutAHeal()
        {
            using var w = new HostileTestWorld();
            Entity h = Engaged(w, float3.zero);
            w.SetPlayers((new float3(9.5f, 0f, 0f), Alive));   // beyond the 9 m detection radius, inside the leash
            w.Step(5 * HostileTestWorld.TickRate - 2);
            Assert.AreEqual(HostileMode.Engage, w.Brain(h).Mode, "still chasing before 5 s");
            w.Step(3);
            Assert.AreEqual(HostileMode.Return, w.Brain(h).Mode, "H14: Emerald's give-up");
            Assert.AreEqual(0, w.Brain(h).HealOnHome, "without a heal");
            w.Em.SetComponentData(h, LocalTransform.FromPosition(float3.zero));
            w.Step();
            Assert.AreEqual(HostileMode.Idle, w.Brain(h).Mode);
            Assert.Greater(w.Em.GetBuffer<ThreatEntry>(h).Length, 0, "a give-up keeps its threat (no tag release)");
        }

        [Test]
        public void NeutralGround_DropsTheTarget_ForTheNextThreatOrHome()
        {
            using var w = new HostileTestWorld();
            Entity h = Engaged(w, float3.zero);
            w.SetPlayers((new float3(2f, 0f, 0f), (byte)(Alive | SensedFlags.Neutral)), (new float3(3f, 0f, 0f), Alive));
            w.Hit(h, 101, 1f);
            w.Step();
            w.Step();
            Assert.AreEqual(HostileMode.Engage, w.Brain(h).Mode);
            Assert.AreEqual(101, w.Target(h).GhostId, "the next threat");
            w.SetPlayers((new float3(2f, 0f, 0f), (byte)(Alive | SensedFlags.Neutral)), (new float3(3f, 0f, 0f), (byte)(Alive | SensedFlags.Neutral)));
            w.Step();
            Assert.AreEqual(HostileMode.Return, w.Brain(h).Mode, "nobody left: home");
        }
    }
}
