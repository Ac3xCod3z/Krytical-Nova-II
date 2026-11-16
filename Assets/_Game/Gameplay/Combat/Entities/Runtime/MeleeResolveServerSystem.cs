using KryticalNova.Player.Entities;
using KryticalNova.Player.Entities.Presentation;
using KryticalNova.Core;
using KryticalNova.Progression;
using UnityEngine;
using Unity.Collections;
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
            m_Picks = new NativeList<int>(64, Allocator.Persistent);
        }

        protected override void OnDestroy()
        {
            m_Grid?.Dispose();
            if (m_PoolRings.IsCreated) m_PoolRings.Dispose();
            if (m_Picks.IsCreated) m_Picks.Dispose();
        }

        // Every body a swing could strike this tick (the old per-swing gathering, done once): players in registry order, then the
        // non-player targets; each one's position ring and its place in the grid.
        readonly System.Collections.Generic.List<Struck> m_Pool = new System.Collections.Generic.List<Struck>();
        readonly System.Collections.Generic.List<GhostBodyEntry> m_PoolEntry = new System.Collections.Generic.List<GhostBodyEntry>();
        readonly System.Collections.Generic.List<int> m_Kept = new System.Collections.Generic.List<int>();
        NativeList<PositionHistory> m_PoolRings;
        NativeList<int> m_Picks;
        MeleeBystanderGrid m_Grid;

        void BuildPool(EntityManager em)
        {
            m_Pool.Clear();
            m_PoolEntry.Clear();
            m_PoolRings.Clear();
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
            m_PoolRings.Add(ring);
            m_Grid.Add(ring, s.Proxy);
        }

        protected override void OnUpdate()
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
