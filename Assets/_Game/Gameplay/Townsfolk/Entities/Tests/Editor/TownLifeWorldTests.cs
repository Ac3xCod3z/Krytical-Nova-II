using System;
using System.Collections.Generic;
using KryticalNova.Brains;
using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace KryticalNova.Townsfolk.Tests
{
    /// <summary>
    /// A plain world with the Brain's systems and the town-life mind, ticked by hand at 30 Hz on the straight-line path backend (no
    /// NavMesh): the area's spots, slots and personalities set by the test (the job of the server bridge in the game).
    /// </summary>
    public sealed class TownTestWorld : IDisposable
    {
        public const int TickRate = 30;
        public readonly World World;
        public uint Tick;
        readonly SystemHandle m_Sense, m_Clock, m_Decide, m_Path, m_Move, m_Ghost;
        readonly EntityQuery m_TickQuery, m_SenseQuery, m_ClockQuery;
        public readonly Entity Area;
        readonly List<UnityEngine.Object> m_Made = new();
        uint m_NextId = 1;

        public TownTestWorld(params TownsfolkPersonality[] personalities)
        {
            World = new World("TownTestWorld");
            EntityManager em = World.EntityManager;
            var brain = ScriptableObject.CreateInstance<BrainSettings>();
            var town = ScriptableObject.CreateInstance<TownsfolkSettings>();
            em.CreateSingleton(BrainConfig.From(brain, TickRate));
            Entity config = em.CreateSingleton(TownsfolkConfig.From(town, TickRate));
            UnityEngine.Object.DestroyImmediate(brain);
            UnityEngine.Object.DestroyImmediate(town);
            DynamicBuffer<PersonalityRow> rows = em.AddBuffer<PersonalityRow>(config);
            foreach (TownsfolkPersonality p in personalities) rows.Add(TownsfolkServerSystem.RowOf(p));
            em.CreateSingleton(new BrainTick());
            em.CreateSingleton(new TownClock { Hours = 12f, Period = (byte)TownPeriod.Day });
            Area = em.CreateSingleton(new TownArea { Radius = 60f, Cast = 1, Ready = 1 });
            em.AddBuffer<TownSpot>(Area);
            em.AddBuffer<TownSlot>(Area);
            em.AddBuffer<TownScoreCache>(Area);
            m_Sense = World.CreateSystem<SenseBuildSystem>();
            m_Clock = World.CreateSystem<ThinkClockSystem>();
            m_Decide = World.CreateSystem<TownLifeDecideSystem>();
            m_Path = World.CreateSystem<PathServiceSystem>();
            m_Move = World.CreateSystem<BrainMoveSystem>();
            m_Ghost = World.CreateSystem<TownsfolkGhostWriteSystem>();
            m_TickQuery = em.CreateEntityQuery(typeof(BrainTick));
            m_SenseQuery = em.CreateEntityQuery(typeof(SenseWorld));
            m_ClockQuery = em.CreateEntityQuery(typeof(TownClock));
        }

        public EntityManager Em => World.EntityManager;

        public static TownsfolkPersonality Personality(byte id, float stop, Vector2 stay, params (TownSpotKind kind, float w)[] likes)
        {
            var p = ScriptableObject.CreateInstance<TownsfolkPersonality>();
            p.id = id;
            p.displayName = "Test " + id;
            p.stopChance = stop;
            p.staySeconds = stay;
            p.walkSpeed = new Vector2(1.4f, 1.4f);
            var list = new List<TownsfolkPersonality.SpotLike>();
            foreach ((TownSpotKind kind, float w) in likes) list.Add(new TownsfolkPersonality.SpotLike { kind = kind, weight = w });
            p.likes = list.ToArray();
            return p;
        }

        public int AddSpot(TownSpotKind kind, float3 at, int slots, float spacing = 1.2f, byte onlyFor = 0, TownSpotFlags flags = TownSpotFlags.None)
        {
            DynamicBuffer<TownSpot> spots = Em.GetBuffer<TownSpot>(Area);
            DynamicBuffer<TownSlot> slotRows = Em.GetBuffer<TownSlot>(Area);
            var spot = new TownSpot { Pos = at, Kind = (byte)kind, Flags = (byte)flags, OnlyFor = onlyFor, FirstSlot = (ushort)slotRows.Length, SlotCount = (byte)slots };
            for (int k = 0; k < slots; k++)
            {
                TownLifeRules.SlotPlace(at, 0f, spot.Kind, k, slots, spacing, out float3 p, out float yaw);
                slotRows.Add(new TownSlot { Pos = p, Yaw = yaw, Spot = (ushort)spots.Length });
            }
            spots.Add(spot);
            return spots.Length - 1;
        }

        public Entity Townsperson(float3 at, byte personality, float shiftStart = 0f, float shiftEnd = 0f)
        {
            EntityManager em = Em;
            Entity e = em.CreateEntity(typeof(BrainTag), typeof(BrainActive), typeof(BrainClock), typeof(BrainDice), typeof(BrainMover), typeof(BrainPath),
                typeof(LocalTransform), typeof(TownLife), typeof(TownPick), typeof(TownsfolkGhost), typeof(TownsfolkGhostSource));
            em.AddBuffer<BrainEvent>(e);
            em.AddBuffer<BrainWaypoint>(e);
            uint id = m_NextId++;
            em.SetComponentData(e, LocalTransform.FromPosition(at));
            em.SetComponentData(e, new BrainClock { StableId = id, Tier = 3 });   // born far: a player near raises it at once (the Brain lowers a tier only after its hysteresis)
            em.SetComponentData(e, new BrainDice { Rng = BrainDiceRules.Create(id, 77) });
            em.SetComponentData(e, new BrainMover { Dest = at, Flags = BrainMoverFlags.Stop });
            em.SetComponentData(e, new TownLife { Personality = personality, WalkSpeed = 1.4f, JogSpeed = 3.5f, ShiftStart = shiftStart, ShiftEnd = shiftEnd, Seed = id });
            em.SetComponentData(e, new TownsfolkGhostSource { LastPosition = at });
            return e;
        }

        public void SetClock(float hours, bool raining = false) =>
            m_ClockQuery.SetSingleton(new TownClock { Hours = hours, Period = TownLifeRules.PeriodOf(hours, new float4(5f, 8f, 18f, 22f)), Raining = (byte)(raining ? 1 : 0) });

        public void SetPlayers(params float3[] feet)
        {
            SenseWorld sense = m_SenseQuery.GetSingleton<SenseWorld>();
            sense.Players.Clear();
            for (int i = 0; i < feet.Length; i++) sense.Players.Add(new SensedPlayer { GhostId = 100 + i, Feet = feet[i], Flags = SensedFlags.Alive });
        }

        public void Step(int ticks = 1)
        {
            for (int i = 0; i < ticks; i++)
            {
                Tick++;
                m_TickQuery.SetSingleton(new BrainTick { Tick = Tick });
                m_Sense.Update(World.Unmanaged);
                m_Clock.Update(World.Unmanaged);
                m_Decide.Update(World.Unmanaged);
                m_Path.Update(World.Unmanaged);
                m_Move.Update(World.Unmanaged);
                m_Ghost.Update(World.Unmanaged);
            }
        }

        public TownLife Life(Entity e) => Em.GetComponentData<TownLife>(e);

        /// <summary>One townsperson's state for a failure message.</summary>
        public string Describe(Entity e)
        {
            TownLife L = Life(e);
            BrainMover m = Em.GetComponentData<BrainMover>(e);
            BrainClock c = Em.GetComponentData<BrainClock>(e);
            TownPick p = Em.GetComponentData<TownPick>(e);
            return $"state {L.State} spot {L.Spot} slot {L.Slot} at {Pos(e)} dest {m.Dest} flags {m.Flags} speed {m.Speed} tier {c.Tier} pick {p.Spot}/{p.WalkTo} score {p.Score:F2} " +
                   $"cache {Em.GetBuffer<TownScoreCache>(Area).Length} spots {Em.GetBuffer<TownSpot>(Area).Length}";
        }
        public float3 Pos(Entity e) => Em.GetComponentData<LocalTransform>(e).Position;

        public void Dispose()
        {
            if (World.IsCreated) World.Dispose();
        }
    }

    /// <summary>The town-life mind on the Brain (plan evals: claims, day plans, far plan step, ghost).</summary>
    public sealed class TownLifeWorldTests
    {
        readonly List<UnityEngine.Object> m_Made = new();

        TownsfolkPersonality Made(TownsfolkPersonality p) { m_Made.Add(p); return p; }

        [TearDown]
        public void TearDown()
        {
            foreach (UnityEngine.Object o in m_Made) if (o != null) UnityEngine.Object.DestroyImmediate(o);
            m_Made.Clear();
        }

        [Test]
        public void SixtyTownsfolkNeverDoubleBookASlot()
        {
            var stroller = Made(TownTestWorld.Personality(2, 0.9f, new Vector2(2f, 6f), (TownSpotKind.Bench, 1f), (TownSpotKind.ChatCircle, 0.8f), (TownSpotKind.Viewpoint, 0.6f)));
            using var w = new TownTestWorld(stroller);
            var rng = new Unity.Mathematics.Random(7);
            for (int i = 0; i < 20; i++)
            {
                TownSpotKind kind = i % 3 == 0 ? TownSpotKind.Bench : i % 3 == 1 ? TownSpotKind.ChatCircle : TownSpotKind.Viewpoint;
                w.AddSpot(kind, new float3(rng.NextFloat(-40f, 40f), 0f, rng.NextFloat(-40f, 40f)), kind == TownSpotKind.ChatCircle ? 3 : 2);
            }
            for (int i = 0; i < 10; i++) w.AddSpot(TownSpotKind.Walk, new float3(rng.NextFloat(-40f, 40f), 0f, rng.NextFloat(-40f, 40f)), 1);
            w.SetPlayers(float3.zero);   // a player in the middle: everyone near enough to think
            var folk = new List<Entity>();
            for (int i = 0; i < 60; i++) folk.Add(w.Townsperson(new float3(rng.NextFloat(-40f, 40f), 0f, rng.NextFloat(-40f, 40f)), 2));

            int arrivals = 0;
            var wasAt = new Dictionary<Entity, bool>();
            for (int t = 0; t < 10000; t += 50)
            {
                w.Step(50);
                DynamicBuffer<TownSlot> slots = w.Em.GetBuffer<TownSlot>(w.Area);
                var holders = new HashSet<ushort>();
                foreach (Entity e in folk)
                {
                    TownLife L = w.Life(e);
                    if (L.Slot == 0) continue;
                    Assert.IsTrue(holders.Add(L.Slot), $"tick {w.Tick}: slot {L.Slot} held by two townsfolk");
                    bool at = L.State == TownLifeState.AtSpot;
                    if (at && !(wasAt.TryGetValue(e, out bool before) && before)) arrivals++;
                    wasAt[e] = at;
                }
                // every claim belongs to the one whose life names it
                for (int i = 0; i < slots.Length; i++)
                {
                    if (slots[i].ClaimedBy == Entity.Null) continue;
                    Assert.AreEqual(i + 1, w.Life(slots[i].ClaimedBy).Slot, $"tick {w.Tick}: slot {i + 1}'s claim and its holder disagree");
                }
            }
            Assert.Greater(arrivals, 100, "townsfolk keep reaching spots and moving on");
        }

        [Test]
        public void SameSeedMakesTheSameTown()
        {
            // the think job runs in parallel; the claim job hands out slots in StableId order, so two runs never differ
            var stroller = Made(TownTestWorld.Personality(2, 0.9f, new Vector2(2f, 6f), (TownSpotKind.Bench, 1f), (TownSpotKind.ChatCircle, 0.8f)));
            float3[] Run()
            {
                using var w = new TownTestWorld(stroller);
                var rng = new Unity.Mathematics.Random(11);
                for (int i = 0; i < 8; i++) w.AddSpot(i % 2 == 0 ? TownSpotKind.Bench : TownSpotKind.ChatCircle, new float3(rng.NextFloat(-30f, 30f), 0f, rng.NextFloat(-30f, 30f)), 2);
                for (int i = 0; i < 6; i++) w.AddSpot(TownSpotKind.Walk, new float3(rng.NextFloat(-30f, 30f), 0f, rng.NextFloat(-30f, 30f)), 1);
                w.SetPlayers(float3.zero);
                var folk = new List<Entity>();
                for (int i = 0; i < 40; i++) folk.Add(w.Townsperson(new float3(rng.NextFloat(-30f, 30f), 0f, rng.NextFloat(-30f, 30f)), 2));
                w.Step(900);
                var result = new float3[folk.Count * 2];
                for (int i = 0; i < folk.Count; i++) { result[i * 2] = w.Pos(folk[i]); result[i * 2 + 1] = new float3(w.Life(folk[i]).Slot, w.Life(folk[i]).State, 0f); }
                return result;
            }
            float3[] a = Run(), b = Run();
            for (int i = 0; i < a.Length; i++) Assert.AreEqual(a[i], b[i], $"entry {i} differs between two runs of the same seed");
        }

        [Test]
        public void CrowdTierRowsClampToTheirEnds()
        {
            int[] row = { 16, 32, 48, 60 };
            Assert.AreEqual(16, TownsfolkSettings.ByTier(row, -1, 0));
            Assert.AreEqual(48, TownsfolkSettings.ByTier(row, 2, 0));
            Assert.AreEqual(60, TownsfolkSettings.ByTier(row, 9, 0));
            Assert.AreEqual(7, TownsfolkSettings.ByTier(System.Array.Empty<int>(), 2, 7));
        }

        [Test]
        public void EachVendorKeepsOneLookOnEveryMachine()
        {
            int smith = VendorLooksClientSystem.LookOf("NPC_Blacksmith", 1000);
            Assert.AreEqual(smith, VendorLooksClientSystem.LookOf("NPC_Blacksmith", 1000), "the same name, the same look");
            Assert.GreaterOrEqual(smith, 1000, "apart from the crowd's looks");
            Assert.Less(smith, 1997);
            Assert.AreNotEqual(smith, VendorLooksClientSystem.LookOf("NPC_Tailor", 1000), "two vendors, two looks");
        }

        [Test]
        public void StaleClaimFreesItself()
        {
            var p = Made(TownTestWorld.Personality(2, 1f, new Vector2(1f, 1f), (TownSpotKind.Bench, 1f)));
            using var w = new TownTestWorld(p);
            w.AddSpot(TownSpotKind.Bench, new float3(5f, 0f, 0f), 1);
            w.SetPlayers(float3.zero);
            Entity a = w.Townsperson(float3.zero, 2);
            w.Step(3);
            Assert.AreEqual(1, w.Life(a).Slot, "the first claims the bench");
            w.Em.DestroyEntity(a);   // gone without letting go (a GM clear, a despawn)
            w.Step(1);
            Assert.AreEqual(Entity.Null, w.Em.GetBuffer<TownSlot>(w.Area)[0].ClaimedBy, "a gone holder's claim is freed");
        }

        [Test]
        public void ShiftEndsAndRainSendHomeThenGone()
        {
            var worker = Made(TownTestWorld.Personality(1, 1f, new Vector2(600f, 600f), (TownSpotKind.Work, 1f)));
            var jogger = Made(TownTestWorld.Personality(3, 1f, new Vector2(600f, 600f), (TownSpotKind.Bench, 1f)));
            jogger.rain = RainBehaviour.GoHome;
            using var w = new TownTestWorld(worker, jogger);
            w.AddSpot(TownSpotKind.Work, new float3(4f, 0f, 0f), 1, onlyFor: 2);   // worker id 1 + 1
            w.AddSpot(TownSpotKind.Bench, new float3(-4f, 0f, 0f), 1);
            w.AddSpot(TownSpotKind.HomeDoor, new float3(0f, 0f, 8f), 1);
            w.SetPlayers(float3.zero);
            w.SetClock(10f);
            Entity a = w.Townsperson(float3.zero, 1, 6f, 14f);
            Entity b = w.Townsperson(new float3(0.5f, 0f, 0f), 3);
            w.Step(150);
            Assert.AreEqual(TownLifeState.AtSpot, w.Life(a).State, "the worker is at work on its shift: " + w.Describe(a));
            Assert.AreEqual(TownLifeState.AtSpot, w.Life(b).State, "the jogger rests on the bench in the dry");
            w.SetClock(15f, raining: true);   // past the shift, and raining
            w.Step(2);
            Assert.AreEqual(TownLifeState.GoingHome, w.Life(a).State, "off shift: home");
            Assert.AreEqual(TownLifeState.GoingHome, w.Life(b).State, "rain: home");
            Assert.AreEqual(0, w.Life(a).Slot, "its claim let go");
            w.Step(300);
            Assert.AreEqual(TownLifeState.Gone, w.Life(a).State);
            Assert.AreEqual(TownLifeState.Gone, w.Life(b).State);
        }

        [Test]
        public void FarTownsfolkKeepTheirPlanWithoutWalking()
        {
            var p = Made(TownTestWorld.Personality(2, 1f, new Vector2(1f, 1f), (TownSpotKind.Viewpoint, 1f)));
            using var w = new TownTestWorld(p);
            w.AddSpot(TownSpotKind.Viewpoint, new float3(30f, 0f, 0f), 1);
            w.AddSpot(TownSpotKind.Viewpoint, new float3(-30f, 0f, 0f), 1);
            w.SetPlayers(new float3(1000f, 0f, 0f));   // nobody within 120 m: Brain tier 3, it never thinks
            Entity a = w.Townsperson(float3.zero, 2);
            w.Step(1);
            TownLife L = w.Life(a);
            Assert.AreEqual(TownLifeState.Going, L.State, "a new townsperson picks its first spot at once");
            w.Step(10);
            Assert.AreEqual(0f, math.length(w.Pos(a).xz), 1e-3f, "far away it does not walk");
            w.Step(70);   // past a plan step (2 s)
            Assert.AreEqual(TownLifeState.AtSpot, w.Life(a).State, "the plan step put it at its spot");
            Assert.AreEqual(30f, math.abs(w.Pos(a).x), 0.01f, "standing on the spot's slot");
        }

        [Test]
        public void GhostShowsActivityAndChatCircle()
        {
            var p = Made(TownTestWorld.Personality(4, 1f, new Vector2(60f, 60f), (TownSpotKind.ChatCircle, 1f)));
            using var w = new TownTestWorld(p);
            int circle = w.AddSpot(TownSpotKind.ChatCircle, new float3(3f, 0f, 0f), 3);
            w.SetPlayers(float3.zero);
            Entity a = w.Townsperson(float3.zero, 4);
            w.Step(5);
            Assert.AreEqual((byte)TownActivity.Walk, w.Em.GetComponentData<TownsfolkGhost>(a).Activity);
            Assert.Greater(w.Em.GetComponentData<TownsfolkGhost>(a).SpeedSteps, 20, "walking about 1.4 m/s");
            w.Step(120);
            TownsfolkGhost g = w.Em.GetComponentData<TownsfolkGhost>(a);
            Assert.AreEqual((byte)TownActivity.Chat, g.Activity);
            Assert.AreEqual(circle + 1, g.GroupId);
            Assert.AreEqual(0, g.SpeedSteps);
        }
    }
}
