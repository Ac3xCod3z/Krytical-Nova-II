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
        readonly SystemHandle m_Sense, m_Clock, m_Threat, m_Scan, m_Decide, m_Slots, m_Attack;
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
            m_Slots = World.CreateSystem<HostileAttackSlotSystem>();
            m_Attack = World.CreateSystem<HostileAttackSystem>();
            m_TickQuery = em.CreateEntityQuery(typeof(BrainTick));
            m_SenseQuery = em.CreateEntityQuery(typeof(SenseWorld));
        }

        public EntityManager Em => World.EntityManager;

        public Entity Hostile(float3 at, byte behaviour, uint stableId = 1, float aggro = 9f, float range = 2f)
        {
            EntityManager em = Em;
            Entity e = em.CreateEntity(typeof(BrainTag), typeof(BrainActive), typeof(BrainClock), typeof(LocalTransform), typeof(HostileBrain),
                typeof(HostileTarget), typeof(HostileLife), typeof(HostileHome), typeof(BrainMover), typeof(BrainDice), typeof(HostileSlot));
            em.SetComponentData(e, new BrainDice { Rng = BrainDiceRules.Create(stableId, 77) });
            em.AddBuffer<HostileAbilityRow>(e).Add(new HostileAbilityRow { WindupTicks = 12, RecoverTicks = 6, Range = range, Weight = 1f });
            em.SetComponentData(e, new HostileHome { Home = at, WanderRadius = 8f });
            em.AddBuffer<BrainEvent>(e);
            em.AddBuffer<ThreatEntry>(e);
            em.SetComponentData(e, LocalTransform.FromPosition(at));
            em.SetComponentData(e, new BrainClock { StableId = stableId });
            em.SetComponentData(e, new HostileLife { Behaviour = behaviour, AggroRadius = aggro, AttackRange = range, ReachAbove = 3f, NoticeTicks = 3,
                RadiusMm = 450, HeightMm = 1800 });   // the species' default body (HostileDefinition.bodySize)
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
                m_Slots.Update(World.Unmanaged);
                m_Attack.Update(World.Unmanaged);
            }
        }

        public HostileTarget Target(Entity e) => Em.GetComponentData<HostileTarget>(e);
        public HostileBrain Brain(Entity e) => Em.GetComponentData<HostileBrain>(e);

        /// <summary>The swings resolved so far (the queue is the bridge's to empty in the game).</summary>
        public int Strikes => Em.CreateEntityQuery(typeof(PendingStrike)).GetSingletonBuffer<PendingStrike>(true).Length;

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
        public void TargetOnNeutralGround_IsTheLeash_HomeAndHeal()
        {
            // H14 parity (M5.0, Nova's A): the zone tick calms a Hostile whose target stands on cross-faction neutral ground
            using var w = new HostileTestWorld();
            Entity h = Engaged(w, float3.zero);
            w.SetPlayers((new float3(2f, 0f, 0f), (byte)(Alive | SensedFlags.Neutral)), (new float3(3f, 0f, 0f), Alive));
            w.Step();
            Assert.AreEqual(HostileMode.Return, w.Brain(h).Mode, "the target on neutral ground: home, whoever else is near");
            Assert.AreEqual(1, w.Brain(h).HealOnHome, "and it heals there");
        }

        [Test]
        public void ADeadTarget_GivesWayToTheNextThreat_OrHome()
        {
            using var w = new HostileTestWorld();
            Entity h = Engaged(w, float3.zero);
            w.SetPlayers((new float3(2f, 0f, 0f), SensedFlags.Dead), (new float3(3f, 0f, 0f), Alive));
            w.Hit(h, 101, 1f);
            w.Step();
            w.Step();
            Assert.AreEqual(HostileMode.Engage, w.Brain(h).Mode);
            Assert.AreEqual(101, w.Target(h).GhostId, "the next threat");
            w.SetPlayers((new float3(2f, 0f, 0f), SensedFlags.Dead), (new float3(3f, 0f, 0f), SensedFlags.Dead));
            w.Step();
            Assert.AreEqual(HostileMode.Return, w.Brain(h).Mode, "nobody left: home");
            Assert.AreEqual(0, w.Brain(h).HealOnHome);
        }
    }

    public sealed class SwingTests
    {
        const byte Alive = SensedFlags.Alive;

        // a FightsBack Hostile at the origin facing +z, engaged by a hit from a player 5 m ahead (out of reach)
        static Entity Engaged(HostileTestWorld w)
        {
            Entity h = w.Hostile(float3.zero, HostileBehaviourKind.FightsBack);
            w.SetPlayers((new float3(0f, 0f, 5f), Alive));
            w.Hit(h, 100, 1f);
            w.Step();
            Assert.AreEqual(HostileMode.Engage, w.Brain(h).Mode);
            Assert.AreEqual(HostilePhase.Approach, w.Brain(h).Phase, "5 m: out of reach");
            return h;
        }

        /// <summary>Steps through a light swing's tell (Q3 option A) to the windup's first tick.</summary>
        static void PastTheTell(HostileTestWorld w, Entity h)
        {
            for (int i = 0; i < 30 && w.Brain(h).Phase == HostilePhase.Tell; i++) w.Step();
        }

        [Test]
        public void SwingStartsWhenInReach()
        {
            using var w = new HostileTestWorld();
            Entity h = Engaged(w);
            w.SetPlayers((new float3(0f, 0f, 1.5f), Alive));
            w.Step();
            PastTheTell(w, h);   // Q3 option A: a light swing tells first (TellTests)
            Assert.AreEqual(HostilePhase.Windup, w.Brain(h).Phase);
            Assert.AreEqual(w.Tick, w.Brain(h).AttackStartTick, "the windup starts on the tick the tell ends");
            w.Step(12);
            Assert.AreEqual(1, w.Strikes, "it lands at the windup's end");
        }

        [Test]
        public void DodgeBeforeCommit_Cancels()
        {
            using var w = new HostileTestWorld();
            Entity h = Engaged(w);
            w.SetPlayers((new float3(0f, 0f, 1.5f), Alive));
            w.Step();
            PastTheTell(w, h);
            uint commit = w.Brain(h).CommitTick;
            w.Step((int)(commit - 1 - w.Tick) - 1);
            w.SetPlayers((new float3(0f, 0f, 4f), Alive));   // range 2 + slack 0.3 + 1 m < 4
            w.Step();
            Assert.AreEqual(commit - 1, w.Tick);
            Assert.AreEqual(HostilePhase.Approach, w.Brain(h).Phase, "called off before the commit");
            w.Step(20);
            Assert.AreEqual(0, w.Strikes);
        }

        [Test]
        public void DodgeAfterCommit_Misses()
        {
            using var w = new HostileTestWorld();
            Entity h = Engaged(w);
            w.SetPlayers((new float3(0f, 0f, 1.5f), Alive));
            w.Step();
            PastTheTell(w, h);
            uint commit = w.Brain(h).CommitTick;
            w.Step((int)(commit + 1 - w.Tick) - 1);
            w.SetPlayers((new float3(0f, 0f, 4f), Alive));
            w.Step();
            Assert.AreEqual(commit + 1, w.Tick);
            Assert.AreEqual(HostilePhase.Windup, w.Brain(h).Phase, "committed: the swing goes on");
            w.Step(12);
            Assert.AreEqual(0, w.Strikes, "and misses");
            Assert.AreNotEqual(HostilePhase.Windup, w.Brain(h).Phase);
        }

        [Test]
        public void FlinchNeverCancelsWindup()
        {
            using var w = new HostileTestWorld();
            Entity h = Engaged(w);
            w.SetPlayers((new float3(0f, 0f, 1.5f), Alive));
            w.Step();
            PastTheTell(w, h);
            for (int i = 0; i < 10; i++) { w.Hit(h, 100, 1f); w.Step(); }
            Assert.AreEqual(HostilePhase.Windup, w.Brain(h).Phase, "ten hits, still winding up");
            w.Step(2);
            Assert.AreEqual(1, w.Strikes, "the strike resolves at the windup's end");
        }
    }

    public sealed class HostileScoreTests
    {
        [Test]
        public void FlatCurves_EqualWeightedDice()
        {
            // NGO: three attacks weighted 45/40/15 (HostileAssetBuilder.cs:323-325)
            using var world = new World("HostileScoreTests");
            Entity e = world.EntityManager.CreateEntity();
            DynamicBuffer<HostileAbilityRow> rows = world.EntityManager.AddBuffer<HostileAbilityRow>(e);
            rows.Add(new HostileAbilityRow { Weight = 45f });
            rows.Add(new HostileAbilityRow { Weight = 40f });
            rows.Add(new HostileAbilityRow { Weight = 15f });
            var rng = BrainDiceRules.Create(3, 99);
            var counts = new int[3];
            const int picks = 100000;
            for (int i = 0; i < picks; i++) counts[HostileRules.PickAbility(rows, 1, ref rng)]++;
            Assert.AreEqual(0.45, counts[0] / (double)picks, 0.01);
            Assert.AreEqual(0.40, counts[1] / (double)picks, 0.01);
            Assert.AreEqual(0.15, counts[2] / (double)picks, 0.01);
            rows[0] = new HostileAbilityRow { Weight = 45f, ReadyTick = 10 };
            for (int i = 0; i < 1000; i++) Assert.AreNotEqual(0, HostileRules.PickAbility(rows, 5, ref rng), "an attack on cooldown is never picked");
        }
    }

    public sealed class HostileMindDeterminismTests
    {
        static (HostileBrain[] brains, int strikes) Run()
        {
            using var w = new HostileTestWorld();
            var hostiles = new Entity[50];
            for (int i = 0; i < hostiles.Length; i++)
                hostiles[i] = w.Hostile(new float3((i % 10) * 6f, 0f, (i / 10) * 6f), (byte)(i % 2), (uint)(i + 1));
            for (int t = 0; t < 1000; t++)
            {
                float3 a = new float3(math.sin(t * 0.02f) * 30f + 27f, 0f, 12f), b = new float3(t * 0.05f, 0f, math.cos(t * 0.03f) * 20f + 12f);
                // player 102 stands 1.5 m from Hostile 41 (attacks on sight): real strikes every run (M6.5: the two runners pass at 0.6 m a tick, so
                // their swings are cancelled or end behind them, and with attack slots their few lucky hits moved; brain plan s3.6)
                w.SetPlayers((a, SensedFlags.Alive), (b, SensedFlags.Alive), (new float3(6f, 0f, 25.5f), SensedFlags.Alive),
                    (new float3(50f, 0f, -5f), SensedFlags.Alive), (new float3(-10f, 0f, 10f), SensedFlags.Alive));
                if (t % 37 == 0) w.Hit(hostiles[t % hostiles.Length], 100 + t % 5, 10f);
                w.Step();
            }
            var brains = new HostileBrain[hostiles.Length];
            for (int i = 0; i < hostiles.Length; i++) brains[i] = w.Brain(hostiles[i]);
            return (brains, w.Strikes);
        }

        [Test]
        public void SameSeed_SameMind_1000Ticks()
        {
            var first = Run();
            var second = Run();
            CollectionAssert.AreEqual(first.brains, second.brains);
            Assert.AreEqual(first.strikes, second.strikes);
            Assert.Greater(first.strikes, 0, "the run had fights in it");
        }

        [Test]
        public void TheMind_AllocatesNothing_1000Ticks()
        {
            using var w = new HostileTestWorld();
            for (int i = 0; i < 100; i++) w.Hostile(new float3((i % 10) * 4f, 0f, (i / 10) * 4f), (byte)(i % 2), (uint)(i + 1));
            w.SetPlayers((new float3(10f, 0f, 10f), SensedFlags.Alive), (new float3(30f, 0f, 30f), SensedFlags.Alive));
            w.Step(100);   // warm-up
            long before = System.GC.GetAllocatedBytesForCurrentThread();
            w.Step(1000);
            Assert.AreEqual(0, System.GC.GetAllocatedBytesForCurrentThread() - before, "a mind tick allocates nothing");
        }
    }

    public sealed class PatrolTests
    {
        [Test]
        public void Patrol_StaysWithinWanderRadius_AndPausesThreeToSixSeconds()
        {
            using var w = new HostileTestWorld();
            float3 home = new float3(10f, 0f, -4f);
            Entity h = w.Hostile(home, HostileBehaviourKind.FightsBack);   // FightsBack: no sight scan in the way
            w.SetPlayers((new float3(200f, 0f, 200f), SensedFlags.Alive));
            int walks = 0;
            uint idleSince = 0;
            for (int t = 0; t < 10000; t++)
            {
                w.Step();
                HostileBrain b = w.Brain(h);
                BrainMover m = w.Em.GetComponentData<BrainMover>(h);
                if (b.Mode == HostileMode.Patrol && b.ModeSinceTick == w.Tick)
                {
                    walks++;
                    Assert.LessOrEqual(math.distance(m.Dest.xz, home.xz), 8f + 1e-3f, "within the wander radius of home");
                    if (idleSince != 0)
                        Assert.That(w.Tick - idleSince, Is.InRange(3 * HostileTestWorld.TickRate, 6 * HostileTestWorld.TickRate + 1), "the pause");
                    w.Em.SetComponentData(h, LocalTransform.FromPosition(m.Dest));   // M5.4 moves it; here it arrives at once
                }
                if (b.Mode == HostileMode.Idle && b.ModeSinceTick == w.Tick) idleSince = w.Tick;
            }
            Assert.Greater(walks, 20, "it keeps wandering");
        }
    }

    /// <summary>8 Nov 2026 probe-x6-12: every brain stood still (speed 0.00 though Engage and Run): the step speed comes from the mover's flags.</summary>
    public sealed class MoveSpeedTests
    {
        [Test]
        public void StopStands_RunRuns_AnythingElseWalks_AsEmeraldMovedNgosHostiles()
        {
            Assert.AreEqual(0f, HostileMoveSpeedSystem.SpeedFor(BrainMoverFlags.Stop, 1.8f, 4.2f), "Stop");
            Assert.AreEqual(0f, HostileMoveSpeedSystem.SpeedFor((byte)(BrainMoverFlags.Stop | BrainMoverFlags.Face), 1.8f, 4.2f), "Stop and face");
            Assert.AreEqual(4.2f, HostileMoveSpeedSystem.SpeedFor(BrainMoverFlags.Run, 1.8f, 4.2f), "a chase or the way home: run");
            Assert.AreEqual(1.8f, HostileMoveSpeedSystem.SpeedFor(0, 1.8f, 4.2f), "a patrol walk");
            Assert.AreEqual(1.8f, HostileMoveSpeedSystem.SpeedFor(BrainMoverFlags.Face, 1.8f, 4.2f), "facing while walking");
        }
    }
}
