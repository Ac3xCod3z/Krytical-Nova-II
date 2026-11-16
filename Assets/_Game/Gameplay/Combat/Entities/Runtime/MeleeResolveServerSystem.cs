using KryticalNova.Player.Entities;
using KryticalNova.Player.Entities.Presentation;
using KryticalNova.Core;
using KryticalNova.Progression;
using UnityEngine;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;

namespace KryticalNova.Combat.Entities
{
    /// <summary>
    /// Server: the basic attack's hits (M4.4e). On the tick a strike's window ends (<see cref="MeleeRules.HitDue"/>, NGO EnsureTargetStruck),
    /// the target is tested where the attacker saw it (the PositionHistory at the attacker's interpolation delay, M4.3) and within the
    /// server's check range now (<see cref="MeleeRules.Resolve"/>), then the body applies it as NGO does (<see cref="MeleeStrike"/>).
    /// One hit per swing, landed or not. Every other hostile in the swing's arc where the attacker saw it is struck too (M4.4g: NGO's
    /// blade sweep strikes each hostile it touches). Players, and the attackable ghosts that are not players (M4.2b: the Strike Dummy,
    /// each with its own capsule; <see cref="CombatTargetRegistry"/>).
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(PredictedSimulationSystemGroup))]
    public partial class MeleeResolveServerSystem : SystemBase
    {
        /// <summary>Strikes that landed (tests).</summary>
        public int Hits { get; private set; }

        /// <summary>Attack orders dropped because the target is gone, dead or not hostile (tests).</summary>
        public int Refused { get; private set; }

        /// <summary>Due swings whose claim was older than the ring (Resolve refused), whose target test failed, whose hit the body
        /// refused (MeleeStrike.Apply false); the numbers of the last due swing (diagnostics, KnCadenceProbe).</summary>
        public int Unresolved { get; private set; }
        public int Missed { get; private set; }
        public int NotApplied { get; private set; }
        public string LastVerdict => m_HasVerdict ? m_Verdict.ToString() : "no swing due yet";

        /// <summary>The numbers of one due swing. Kept as numbers and written out only when <see cref="LastVerdict"/> is read (perf night
        /// chunk 3: the server made a string for every due swing that only a probe reads).</summary>
        public struct Verdict
        {
            public byte Step;
            public int3 Me, Them;
            public int TicksBack, RingSamples, ReachMm;
            public bool Resolved, TargetHit;

            public override string ToString() =>
                $"step {Step}: me {Me}, target now {Them}, seen {TicksBack} ticks back, target ring " +
                $"{RingSamples} samples, resolved {Resolved}, target hit {TargetHit}, reach {ReachMm} mm";
        }

        Verdict m_Verdict;
        bool m_HasVerdict;

        /// <summary>NGO RequestAttackEntityServerRpc's check (PointAndClickPlayer:1881-1895), on the bodies, with the same rule.</summary>
        public static bool AttackAllowed(UnityEngine.GameObject attacker, IDamageable target) =>
            attacker != null && target != null && target.IsAlive && CombatHostility.IsHostile(attacker.transform, target);

        bool AttackAllowed(EntityManager em, GhostBodyEntry attacker, int targetGhostId) =>
            TryFind(em, targetGhostId, out Struck target) && target.Body.TryGetComponent(out IDamageable damageable)
            && AttackAllowed(attacker.Body, damageable);

        /// <summary>A body a swing can strike: a player's, or a non-player target's (M4.2b), with its ghost and its capsule.</summary>
        struct Struck
        {
            public GameObject Body;
            public Entity Ghost;
            public HitProxy Proxy;
        }

        protected override void OnCreate()
        {
            RequireForUpdate<NetworkTime>();
            m_Grid = new MeleeBystanderGrid();
            m_PoolRings = new NativeList<PositionHistory>(64, Allocator.Persistent);
            m_PoolProxies = new NativeList<HitProxy>(64, Allocator.Persistent);
            m_PoolGhosts = new NativeList<Entity>(64, Allocator.Persistent);
            m_Picks = new NativeList<int>(64, Allocator.Persistent);
        }

        protected override void OnDestroy()
        {
            m_Grid?.Dispose();
            if (m_PoolRings.IsCreated) m_PoolRings.Dispose();
            if (m_PoolProxies.IsCreated) m_PoolProxies.Dispose();
            if (m_PoolGhosts.IsCreated) m_PoolGhosts.Dispose();
            if (m_Picks.IsCreated) m_Picks.Dispose();
        }

        // Every body a swing could strike this tick (the old per-swing gathering, done once): players in registry order, then the
        // non-player targets; each one's position ring and its place in the grid.
        readonly System.Collections.Generic.List<Struck> m_Pool = new System.Collections.Generic.List<Struck>();
        readonly System.Collections.Generic.List<GhostBodyEntry> m_PoolEntry = new System.Collections.Generic.List<GhostBodyEntry>();
        readonly System.Collections.Generic.List<int> m_Kept = new System.Collections.Generic.List<int>();
        NativeList<PositionHistory> m_PoolRings;
        NativeList<HitProxy> m_PoolProxies;
        NativeList<Entity> m_PoolGhosts;
        readonly System.Collections.Generic.Dictionary<GhostBodyEntry, int> m_PoolPlaceOf = new System.Collections.Generic.Dictionary<GhostBodyEntry, int>();
        NativeList<int> m_Picks;
        MeleeBystanderGrid m_Grid;

        void BuildPool(EntityManager em)
        {
            m_Pool.Clear();
            m_PoolEntry.Clear();
            m_PoolRings.Clear();
            m_PoolProxies.Clear();
            m_PoolGhosts.Clear();
            m_PoolPlaceOf.Clear();
            m_Grid.Clear();
            foreach (GhostBodyEntry e in GhostBodyRegistry.Entries)
                if (ReferenceEquals(e.World, World) && e.Body != null && em.Exists(e.Ghost)
                    && em.HasComponent<PositionHistory>(e.Ghost) && e.Body.TryGetComponent(out IDamageable _))
                    AddToPool(new Struck { Body = e.Body, Ghost = e.Ghost, Proxy = HitProxy.Player }, e, em.GetComponentData<PositionHistory>(e.Ghost));
            foreach (CombatTargetEntry e in CombatTargetRegistry.Entries)
                if (ReferenceEquals(e.World, World) && em.Exists(e.Ghost) && em.HasComponent<PositionHistory>(e.Ghost)
                    && em.HasComponent<CombatTarget>(e.Ghost) && e.Body.TryGetComponent(out IDamageable _))
                    AddToPool(new Struck { Body = e.Body, Ghost = e.Ghost, Proxy = em.GetComponentData<CombatTarget>(e.Ghost).Proxy }, null,
                        em.GetComponentData<PositionHistory>(e.Ghost));
        }

        void AddToPool(Struck s, GhostBodyEntry entry, in PositionHistory ring)
        {
            m_Pool.Add(s);
            m_PoolEntry.Add(entry);
            if (entry != null) m_PoolPlaceOf.TryAdd(entry, m_Pool.Count - 1);
            m_PoolRings.Add(ring);
            m_PoolProxies.Add(s.Proxy);
            m_PoolGhosts.Add(s.Ghost);
            m_Grid.Add(ring, s.Proxy);
        }

        protected override void OnUpdate()
        {
            if (m_WalkOnly) UpdateInline();   // -knmelee-walk: the code before perf round 4, walk look-ups included
            else UpdateWithJob();
        }

        // ---- perf round 4 (owner, 16 Nov: the resolve as a Burst job across cores) ----

        /// <summary>One swing that is due as the tick begins, with everything its resolve reads (native copies).</summary>
        struct Candidate
        {
            public MeleeTiming T;
            public int3 Me;
            public int Yaw;
            public NetworkTick Seen;
            public Entity TargetGhost;
            public HitProxy TargetProxy;
            public int3 Them;
            public PositionHistory TargetRing;
            public int AttackerPlace;   // the attacker's place in the pool (-1: not in it)
        }

        struct Outcome
        {
            public bool Resolved, TargetHit;
            public int Collateral;      // how many pool places in Struck, from this candidate's start
        }

        /// <summary>Each due swing's resolve, in parallel: its bystanders from the grid, the target test, the arc test of each one.</summary>
        [BurstCompile]
        struct ResolveJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<Candidate> Candidates;
            [ReadOnly] public MeleeBystanderGrid.ReadOnlyView Grid;
            [ReadOnly] public NativeArray<PositionHistory> PoolRings;
            [ReadOnly] public NativeArray<HitProxy> PoolProxies;
            [ReadOnly] public NativeArray<Entity> PoolGhosts;
            public NetworkTick Now;
            public int Stride;
            public NativeArray<Outcome> Outcomes;
            [NativeDisableParallelForRestriction] public NativeArray<int> Struck;   // candidate i writes only [i * Stride, i * Stride + Stride)

            public void Execute(int i)
            {
                Candidate c = Candidates[i];
                var picks = new NativeList<int>(16, Allocator.Temp);
                Grid.Gather(c.Me, c.T.ReachMm, picks);
                var kept = new NativeList<int>(picks.Length, Allocator.Temp);
                for (int k = 0; k < picks.Length; k++)
                {
                    int p = picks[k];
                    if (p == c.AttackerPlace || PoolGhosts[p] == c.TargetGhost) continue;
                    kept.Add(p);
                }
                var rings = new NativeArray<PositionHistory>(kept.Length, Allocator.Temp);
                var proxies = new NativeArray<HitProxy>(kept.Length, Allocator.Temp);
                for (int k = 0; k < kept.Length; k++) { rings[k] = PoolRings[kept[k]]; proxies[k] = PoolProxies[kept[k]]; }
                var collateral = new NativeList<int>(8, Allocator.Temp);
                bool resolved = MeleeRules.Resolve(c.T, c.Me, c.Yaw, Now, c.Seen, c.TargetRing, c.Them, c.TargetProxy, rings, proxies,
                    HitProxy.Player, out bool targetHit, ref collateral);
                for (int k = 0; k < collateral.Length; k++) Struck[i * Stride + k] = kept[collateral[k]];
                Outcomes[i] = new Outcome { Resolved = resolved, TargetHit = targetHit, Collateral = collateral.Length };
            }
        }

        readonly System.Collections.Generic.Dictionary<GhostBodyEntry, int> m_CandidateOf = new System.Collections.Generic.Dictionary<GhostBodyEntry, int>();
        readonly System.Collections.Generic.List<int> m_StruckPlaces = new System.Collections.Generic.List<int>();

        /// <summary>Swings resolved by the job, and by the main thread because their inputs changed during the tick (diagnostics).</summary>
        public int ResolvedByJob { get; private set; }
        public int ResolvedInline { get; private set; }
        /// <summary>Editor and Development builds: job verdicts the inline resolve disagreed with (must stay 0).</summary>
        public int ResolveMismatches { get; private set; }

        bool IsAttacker(EntityManager em, GhostBodyEntry a) =>
            ReferenceEquals(a.World, World) && a.Body != null && em.Exists(a.Ghost) && em.HasComponent<CombatState>(a.Ghost)
            && em.HasComponent<MeleeTimingData>(a.Ghost);

        /// <summary>
        /// The same loop as <see cref="UpdateInline"/>, in the same order, with the same rule order (AttackAllowed, then HitDue), the same
        /// writes and the same hits. The difference: the swings that are due as the tick begins are resolved first, all at once, by
        /// <see cref="ResolveJob"/> (Burst, every core), from native copies of what the resolve reads; the loop then takes a swing's
        /// result when its inputs are still the ones the job had (attacker spot and facing, view delay, target, target spot), else it
        /// resolves that swing itself as before. Nothing in this loop writes positions, rings or timings, so the copies stay true; the
        /// hits themselves (GameObjects) are applied here, in order, as before.
        /// </summary>
        void UpdateWithJob()
        {
            NetworkTick now = SystemAPI.GetSingleton<NetworkTime>().ServerTick;
            if (!now.IsValid) return;
            int rate = SystemAPI.TryGetSingleton<ClientServerTickRate>(out var r) && r.SimulationTickRate > 0 ? (int)r.SimulationTickRate : 30;
            EntityManager em = EntityManager;
            m_FinderBuilt = false;
            m_CandidateOf.Clear();

            // 1. the swings due as the tick begins
            var candidates = new NativeList<Candidate>(16, Allocator.TempJob);
            foreach (GhostBodyEntry attacker in GhostBodyRegistry.Entries)
            {
                if (!IsAttacker(em, attacker)) continue;
                CombatState s = em.GetComponentData<CombatState>(attacker.Ghost);
                if (s.TargetGhostId == 0) continue;
                MeleeTiming t = em.GetComponentData<MeleeTimingData>(attacker.Ghost).Value;
                if (!MeleeRules.HitDue(t, s, now, rate)) continue;
                if (!TryFind(em, s.TargetGhostId, out Struck target)) continue;
                NetworkTick seen = now;
                if (em.HasComponent<CommandDataInterpolationDelay>(attacker.Ghost))
                    seen.Subtract(em.GetComponentData<CommandDataInterpolationDelay>(attacker.Ghost).Delay);
                PlayerPose me = em.GetComponentData<PlayerPose>(attacker.Ghost);
                m_CandidateOf[attacker] = candidates.Length;
                candidates.Add(new Candidate
                {
                    T = t, Me = me.Mm, Yaw = me.YawCdeg, Seen = seen, TargetGhost = target.Ghost, TargetProxy = target.Proxy,
                    Them = FeetOf(em, target.Ghost), TargetRing = em.GetComponentData<PositionHistory>(target.Ghost), AttackerPlace = -1,
                });
            }

            bool poolBuilt = false;
            NativeArray<Outcome> outcomes = default;
            NativeArray<int> struck = default;
            int stride = 0;
            if (candidates.Length > 0)
            {
                // the pool the old loop made on its first due swing (no hit has been applied by then, so it is the same pool)
                BuildPool(em);
                poolBuilt = true;
                foreach (var kv in m_CandidateOf)
                    if (m_PoolPlaceOf.TryGetValue(kv.Key, out int place))
                    {
                        Candidate c = candidates[kv.Value];
                        c.AttackerPlace = place;
                        candidates[kv.Value] = c;
                    }
                stride = math.max(1, m_Pool.Count);
                outcomes = new NativeArray<Outcome>(candidates.Length, Allocator.TempJob);
                struck = new NativeArray<int>(candidates.Length * stride, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                new ResolveJob
                {
                    Candidates = candidates.AsArray(), Grid = m_Grid.AsReadOnly(), PoolRings = m_PoolRings.AsArray(),
                    PoolProxies = m_PoolProxies.AsArray(), PoolGhosts = m_PoolGhosts.AsArray(), Now = now, Stride = stride,
                    Outcomes = outcomes, Struck = struck,
                }.Schedule(candidates.Length, 4).Complete();   // the hits are GameObjects, applied below on this thread
            }

            // 2. the old loop, in order
            var collateral = new NativeList<int>(Allocator.Temp);
            foreach (GhostBodyEntry attacker in GhostBodyRegistry.Entries)
            {
                if (!IsAttacker(em, attacker)) continue;
                CombatState s = em.GetComponentData<CombatState>(attacker.Ghost);
                if (s.TargetGhostId == 0) continue;
                if (!AttackAllowed(em, attacker, s.TargetGhostId))
                {
                    MeleeRules.Cancel(ref s);
                    em.SetComponentData(attacker.Ghost, s);
                    Refused++;
                    continue;
                }
                MeleeTiming t = em.GetComponentData<MeleeTimingData>(attacker.Ghost).Value;
                if (!MeleeRules.HitDue(t, s, now, rate)) continue;
                s.SwingHit = 1;
                em.SetComponentData(attacker.Ghost, s);

                if (!TryFind(em, s.TargetGhostId, out Struck target) || !target.Body.TryGetComponent(out IDamageable damageable)) continue;
                NetworkTick seen = now;
                if (em.HasComponent<CommandDataInterpolationDelay>(attacker.Ghost))
                    seen.Subtract(em.GetComponentData<CommandDataInterpolationDelay>(attacker.Ghost).Delay);
                PlayerPose me = em.GetComponentData<PlayerPose>(attacker.Ghost);
                int3 them = FeetOf(em, target.Ghost);
                if (!poolBuilt) { BuildPool(em); poolBuilt = true; }

                bool resolved, targetHit;
                m_StruckPlaces.Clear();
                if (m_CandidateOf.TryGetValue(attacker, out int ci) && candidates[ci].Me.Equals(me.Mm) && candidates[ci].Yaw == me.YawCdeg
                    && candidates[ci].Seen.Equals(seen) && candidates[ci].TargetGhost == target.Ghost && candidates[ci].Them.Equals(them))
                {
                    Outcome o = outcomes[ci];
                    resolved = o.Resolved;
                    targetHit = o.TargetHit;
                    for (int k = 0; k < o.Collateral; k++) m_StruckPlaces.Add(struck[ci * stride + k]);
                    ResolvedByJob++;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    // every test and every Development run checks the job against the resolve it replaces
                    var check = new System.Collections.Generic.List<int>();
                    bool r2 = ResolveHere(em, attacker, target, t, me, now, seen, them, ref collateral, check, out bool hit2);
                    if (r2 != resolved || hit2 != targetHit || !System.Linq.Enumerable.SequenceEqual(check, m_StruckPlaces))
                    {
                        ResolveMismatches++;
                        Debug.LogError($"[MeleeResolve] the job's verdict for {attacker.Ghost} differs: job {resolved}/{targetHit}/{m_StruckPlaces.Count}, here {r2}/{hit2}/{check.Count}");
                    }
#endif
                }
                else
                {
                    resolved = ResolveHere(em, attacker, target, t, me, now, seen, them, ref collateral, m_StruckPlaces, out targetHit);
                    ResolvedInline++;
                }

                PositionHistory targetRing = em.GetComponentData<PositionHistory>(target.Ghost);
                m_Verdict = new Verdict
                {
                    Step = s.Step, Me = me.Mm, Them = them, TicksBack = now.TicksSince(seen), RingSamples = targetRing.Samples.Length,
                    Resolved = resolved, TargetHit = targetHit, ReachMm = t.ReachMm,
                };
                m_HasVerdict = true;
                if (!resolved) { Unresolved++; continue; }
                if (!targetHit) Missed++;
                float multiplier = MeleeRules.StepMultiplier(t, s.Step);
                if (targetHit) { if (MeleeStrike.Apply(attacker.Body, damageable, s.Step, multiplier)) Hits++; else NotApplied++; }   // the ring is written by HitRecordServerSystem
                // a friend in the arc is left alone: MeleeStrike asks the same hostility rule
                for (int i = 0; i < m_StruckPlaces.Count; i++)
                {
                    Struck by = m_Pool[m_StruckPlaces[i]];
                    if (by.Body.TryGetComponent(out IDamageable other) && MeleeStrike.Apply(attacker.Body, other, s.Step, multiplier)) Collateral++;
                }
            }
            collateral.Dispose();
            candidates.Dispose();
            if (outcomes.IsCreated) outcomes.Dispose();
            if (struck.IsCreated) struck.Dispose();
        }

        /// <summary>One swing's resolve on this thread, as the old loop made it; the struck bystanders as pool places, in order.</summary>
        bool ResolveHere(EntityManager em, GhostBodyEntry attacker, in Struck target, in MeleeTiming t, in PlayerPose me, NetworkTick now,
            NetworkTick seen, int3 them, ref NativeList<int> collateral, System.Collections.Generic.List<int> struckPlaces, out bool targetHit)
        {
            m_Grid.Gather(me.Mm, t.ReachMm, m_Picks);
            m_Kept.Clear();
            for (int k = 0; k < m_Picks.Length; k++)
            {
                int p = m_Picks[k];
                if (ReferenceEquals(m_PoolEntry[p], attacker) || m_Pool[p].Ghost == target.Ghost) continue;
                m_Kept.Add(p);
            }
            var rings = new NativeArray<PositionHistory>(m_Kept.Count, Allocator.Temp);
            var proxies = new NativeArray<HitProxy>(m_Kept.Count, Allocator.Temp);
            for (int i = 0; i < m_Kept.Count; i++)
            {
                rings[i] = m_PoolRings[m_Kept[i]];
                proxies[i] = m_Pool[m_Kept[i]].Proxy;
            }
            collateral.Clear();
            bool resolved = MeleeRules.Resolve(t, me.Mm, me.YawCdeg, now, seen, em.GetComponentData<PositionHistory>(target.Ghost), them,
                target.Proxy, rings, proxies, HitProxy.Player, out targetHit, ref collateral);
            rings.Dispose();
            proxies.Dispose();
            struckPlaces.Clear();
            for (int i = 0; i < collateral.Length; i++) struckPlaces.Add(m_Kept[collateral[i]]);
            return resolved;
        }

        /// <summary>The loop before perf round 4, unchanged (<c>-knmelee-walk</c>: the same build's before/after).</summary>
        void UpdateInline()
        {
            NetworkTick now = SystemAPI.GetSingleton<NetworkTime>().ServerTick;
            if (!now.IsValid) return;
            int rate = SystemAPI.TryGetSingleton<ClientServerTickRate>(out var r) && r.SimulationTickRate > 0 ? (int)r.SimulationTickRate : 30;
            EntityManager em = EntityManager;
            var collateral = new NativeList<int>(Allocator.Temp);
            bool poolBuilt = false;
            m_FinderBuilt = false;   // perf round 4: the ghost id -> body map is made once per tick, on the first look-up
            foreach (GhostBodyEntry attacker in GhostBodyRegistry.Entries)
            {
                if (!ReferenceEquals(attacker.World, World) || attacker.Body == null || !em.Exists(attacker.Ghost)
                    || !em.HasComponent<CombatState>(attacker.Ghost) || !em.HasComponent<MeleeTimingData>(attacker.Ghost)) continue;
                CombatState s = em.GetComponentData<CombatState>(attacker.Ghost);
                if (s.TargetGhostId == 0) continue;
                // NGO RequestAttackEntityServerRpc: the target must exist, be alive and hostile, else the order is silently dropped
                // (the owner's predicted combo is then rolled back by the server's state)
                if (!AttackAllowed(em, attacker, s.TargetGhostId))
                {
                    MeleeRules.Cancel(ref s);
                    em.SetComponentData(attacker.Ghost, s);
                    Refused++;
                    continue;
                }
                MeleeTiming t = em.GetComponentData<MeleeTimingData>(attacker.Ghost).Value;
                if (!MeleeRules.HitDue(t, s, now, rate)) continue;
                s.SwingHit = 1;
                em.SetComponentData(attacker.Ghost, s);

                if (!TryFind(em, s.TargetGhostId, out Struck target) || !target.Body.TryGetComponent(out IDamageable damageable)) continue;
                NetworkTick seen = now;
                if (em.HasComponent<CommandDataInterpolationDelay>(attacker.Ghost))
                    seen.Subtract(em.GetComponentData<CommandDataInterpolationDelay>(attacker.Ghost).Delay);
                PlayerPose me = em.GetComponentData<PlayerPose>(attacker.Ghost);
                int3 them = FeetOf(em, target.Ghost);
                // M4.4g: everyone else who could be under the blade (NGO's sweep strikes each hostile it touches, once per swing).
                // Perf clean-ups round 2, chunk 7: the strikeable bodies are gathered once per tick (on its first due swing) and filed
                // by place; a swing reads only the ones near the attacker, in the same order as before, so the same ones are struck.
                if (!poolBuilt) { BuildPool(em); poolBuilt = true; }
                m_Grid.Gather(me.Mm, t.ReachMm, m_Picks);
                bystanders.Clear();
                m_Kept.Clear();
                for (int k = 0; k < m_Picks.Length; k++)
                {
                    int p = m_Picks[k];
                    if (ReferenceEquals(m_PoolEntry[p], attacker) || m_Pool[p].Ghost == target.Ghost) continue;
                    bystanders.Add(m_Pool[p]);
                    m_Kept.Add(p);
                }
                var rings = new NativeArray<PositionHistory>(bystanders.Count, Allocator.Temp);
                var proxies = new NativeArray<HitProxy>(bystanders.Count, Allocator.Temp);
                for (int i = 0; i < bystanders.Count; i++)
                {
                    rings[i] = m_PoolRings[m_Kept[i]];
                    proxies[i] = bystanders[i].Proxy;
                }
                collateral.Clear();
                bool resolved = MeleeRules.Resolve(t, me.Mm, me.YawCdeg, now, seen, em.GetComponentData<PositionHistory>(target.Ghost), them,
                    target.Proxy, rings, proxies, HitProxy.Player, out bool targetHit, ref collateral);
                rings.Dispose();
                proxies.Dispose();
                PositionHistory targetRing = em.GetComponentData<PositionHistory>(target.Ghost);
                m_Verdict = new Verdict
                {
                    Step = s.Step, Me = me.Mm, Them = them, TicksBack = now.TicksSince(seen), RingSamples = targetRing.Samples.Length,
                    Resolved = resolved, TargetHit = targetHit, ReachMm = t.ReachMm,
                };
                m_HasVerdict = true;
                if (!resolved) { Unresolved++; continue; }
                if (!targetHit) Missed++;
                float multiplier = MeleeRules.StepMultiplier(t, s.Step);
                if (targetHit) { if (MeleeStrike.Apply(attacker.Body, damageable, s.Step, multiplier)) Hits++; else NotApplied++; }   // the ring is written by HitRecordServerSystem
                // a friend in the arc is left alone: MeleeStrike asks the same hostility rule
                for (int i = 0; i < collateral.Length; i++)
                {
                    Struck by = bystanders[collateral[i]];
                    if (by.Body.TryGetComponent(out IDamageable other) && MeleeStrike.Apply(attacker.Body, other, s.Step, multiplier)) Collateral++;
                }
            }
            collateral.Dispose();
        }

        readonly System.Collections.Generic.List<Struck> bystanders = new System.Collections.Generic.List<Struck>();

        /// <summary>Others struck by a swing at its target (tests).</summary>
        public int Collateral { get; private set; }

        // ---- perf round 4 (moving 200 bots: this look-up was 25 ms of the server's frame) ----
        // TryFind used to walk every player entry, then every target entry, with up to five EntityManager calls each, for every
        // attacker on every tick (and again for each due swing). Now the same walk is made ONCE per tick into a map that keeps the
        // FIRST entry for each ghost id (players before targets, registry order), which is the one the walk returned. A hit is
        // checked again against the walk's own tests before it is used; a miss, or a hit that no longer passes (a body destroyed
        // during the tick), is answered by the old walk itself, so every answer is the walk's.
        /// <summary>The walk's answer for one ghost id: the body, and the registry entry it came from (to test it again).</summary>
        struct Found
        {
            public Struck Struck;
            public GhostBodyEntry Player;     // set for a player
            public CombatTargetEntry Target;  // set for a non-player target
        }

        readonly System.Collections.Generic.Dictionary<int, Found> m_ByGhostId = new System.Collections.Generic.Dictionary<int, Found>();
        bool m_FinderBuilt;
        bool m_WalkOnly = HasArg("-knmelee-walk");

        static bool HasArg(string arg)
        {
            foreach (string a in System.Environment.GetCommandLineArgs()) if (a == arg) return true;
            return false;
        }

        /// <summary>Look-ups answered by the per-tick map, and by the old walk (tests, diagnostics).</summary>
        public int FoundByMap { get; private set; }
        public int FoundByWalk { get; private set; }
        /// <summary>Editor and Development builds: map answers the walk disagreed with (must stay 0).</summary>
        public int FinderMismatches { get; private set; }

        void BuildFinder(EntityManager em)
        {
            m_ByGhostId.Clear();
            foreach (GhostBodyEntry e in GhostBodyRegistry.Entries)
                if (IsPlayerCandidate(em, e))
                    m_ByGhostId.TryAdd(em.GetComponentData<GhostInstance>(e.Ghost).ghostId,
                        new Found { Struck = new Struck { Body = e.Body, Ghost = e.Ghost, Proxy = HitProxy.Player }, Player = e });
            foreach (CombatTargetEntry e in CombatTargetRegistry.Entries)
                if (IsTargetCandidate(em, e))
                    m_ByGhostId.TryAdd(em.GetComponentData<GhostInstance>(e.Ghost).ghostId,
                        new Found { Struck = new Struck { Body = e.Body, Ghost = e.Ghost, Proxy = em.GetComponentData<CombatTarget>(e.Ghost).Proxy }, Target = e });
            m_FinderBuilt = true;
        }

        // the walk's own tests, one entry at a time
        bool IsPlayerCandidate(EntityManager em, GhostBodyEntry e) =>
            ReferenceEquals(e.World, World) && e.Body != null && em.Exists(e.Ghost) && em.HasComponent<GhostInstance>(e.Ghost)
            && em.HasComponent<PositionHistory>(e.Ghost);

        bool IsTargetCandidate(EntityManager em, CombatTargetEntry e) =>
            ReferenceEquals(e.World, World) && em.Exists(e.Ghost) && em.HasComponent<GhostInstance>(e.Ghost)
            && em.HasComponent<PositionHistory>(e.Ghost) && em.HasComponent<CombatTarget>(e.Ghost);

        /// <summary>The entry found when the map was made still passes the walk's tests, with the same ghost id.</summary>
        bool StillMatches(EntityManager em, in Found f, int ghostId)
        {
            bool candidate = f.Player != null ? IsPlayerCandidate(em, f.Player) : f.Target != null && IsTargetCandidate(em, f.Target);
            return candidate && em.GetComponentData<GhostInstance>(f.Struck.Ghost).ghostId == ghostId;
        }

        bool TryFind(EntityManager em, int ghostId, out Struck found)
        {
            if (m_WalkOnly) { FoundByWalk++; return TryFindByWalk(em, ghostId, out found); }   // -knmelee-walk: the old way (before/after)
            if (!m_FinderBuilt) BuildFinder(em);
            if (m_ByGhostId.TryGetValue(ghostId, out Found f) && StillMatches(em, f, ghostId))
            {
                found = f.Struck;
                FoundByMap++;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                // every test and every Development run checks the map against the walk it replaces
                if (!TryFindByWalk(em, ghostId, out Struck walked) || walked.Ghost != found.Ghost || walked.Body != found.Body)
                {
                    FinderMismatches++;
                    Debug.LogError($"[MeleeResolve] the ghost map found {found.Ghost} for ghost id {ghostId}, the walk {walked.Ghost}");
                }
#endif
                return true;
            }
            FoundByWalk++;
            return TryFindByWalk(em, ghostId, out found);
        }

        /// <summary>The old look-up, unchanged: every player entry, then every target entry.</summary>
        bool TryFindByWalk(EntityManager em, int ghostId, out Struck found)
        {
            foreach (GhostBodyEntry e in GhostBodyRegistry.Entries)
                if (ReferenceEquals(e.World, World) && e.Body != null && em.Exists(e.Ghost) && em.HasComponent<GhostInstance>(e.Ghost)
                    && em.HasComponent<PositionHistory>(e.Ghost) && em.GetComponentData<GhostInstance>(e.Ghost).ghostId == ghostId)
                { found = new Struck { Body = e.Body, Ghost = e.Ghost, Proxy = HitProxy.Player }; return true; }
            foreach (CombatTargetEntry e in CombatTargetRegistry.Entries)
                if (ReferenceEquals(e.World, World) && em.Exists(e.Ghost) && em.HasComponent<GhostInstance>(e.Ghost)
                    && em.HasComponent<PositionHistory>(e.Ghost) && em.HasComponent<CombatTarget>(e.Ghost)
                    && em.GetComponentData<GhostInstance>(e.Ghost).ghostId == ghostId)
                { found = new Struck { Body = e.Body, Ghost = e.Ghost, Proxy = em.GetComponentData<CombatTarget>(e.Ghost).Proxy }; return true; }
            found = default;
            return false;
        }

        static int3 FeetOf(EntityManager em, Entity ghost) =>
            em.HasComponent<PlayerPose>(ghost) ? em.GetComponentData<PlayerPose>(ghost).Mm : em.GetComponentData<CombatTarget>(ghost).FeetMm;
    }
}
