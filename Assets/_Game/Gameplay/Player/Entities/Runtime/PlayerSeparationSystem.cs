using System.Diagnostics;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;

namespace KryticalNova.Player.Entities
{
    /// <summary>
    /// Build 17, item 11.1 (owner pick C): players walk through each other, but nobody stays standing inside another. When two players
    /// both stand still closer than two personal circles, the one who stopped last slides straight out, a little each tick, until they
    /// touch. Only the server moves anyone (the owner's prediction takes the corrected pose from the next snapshot); a walk, a jump, a
    /// wall or the water is never touched. The rule itself is <see cref="PlayerSeparation"/> (pure, tested). Numbers: MovementSettings
    /// (personalSpaceRadius, slideOutSpeed) through <see cref="PlayerMoveSettings"/>.
    ///
    /// Perf round 4 (owner, 15 Nov: the work on all cores): who stands still, the grid and the search for every standing pair that is
    /// too close run as Burst jobs, the search in parallel over the grid's cells. Only the players in a group that has such a pair go
    /// to the main thread, where the walkable surface (NavMesh, main thread only) checks every slide, in the old pair order; a crowd
    /// that already stands apart costs the main thread nothing. The answer is the old loop's exactly
    /// (<see cref="PlayerSeparation.ResolveActive{T}"/>, PlayerSeparationGridTests). <c>-knseparation-serial</c> on the command line
    /// runs the old main-thread pass instead (the same build's before/after); <c>separation_ms</c> in the server CSV is its time.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(PredictedSimulationSystemGroup))]
    [UpdateAfter(typeof(PredictedMoveSystem))]
    public partial class PlayerSeparationSystem : SystemBase
    {
        NativeHashMap<Entity, uint> m_StoodSince;
        /// <summary>The part of a millimetre each sliding player has not moved yet (the pose is whole mm; a step may not be).</summary>
        NativeHashMap<Entity, double2> m_Carry;
        PlayerServerPlanSystem m_Plans;
        EntityQuery m_Players;
        bool m_Serial;
        KryticalNova.Diagnostics.GaugeWindow m_Gauge;

        /// <summary>Slides made; pairs the walkable surface kept from sliding either way (tests, diagnostics).</summary>
        public int Slides { get; private set; }
        public int Blocked { get; private set; }
        /// <summary>Players the last tick sent to the main thread's checked slides (0 when everyone stands apart); diagnostics.</summary>
        public int Active { get; private set; }
        /// <summary>Ticks whose groups moved too far to be kept apart and were run again over every player (the old pass).</summary>
        public int FellBack { get; private set; }

        /// <summary>The old main-thread pass instead of the jobs (tests and the before/after run).</summary>
        public bool Serial { get => m_Serial; set => m_Serial = value; }

        protected override void OnCreate()
        {
            RequireForUpdate<NetworkTime>();
            RequireForUpdate<PlayerMoveSettings>();
            m_StoodSince = new NativeHashMap<Entity, uint>(64, Allocator.Persistent);
            m_Carry = new NativeHashMap<Entity, double2>(16, Allocator.Persistent);
            m_Players = SystemAPI.QueryBuilder().WithAll<MoveState, PlayerPose, PlayerGhostTag>().Build();
            foreach (string arg in System.Environment.GetCommandLineArgs())
                if (arg == "-knseparation-serial") m_Serial = true;
        }

        protected override void OnDestroy()
        {
            if (m_StoodSince.IsCreated) m_StoodSince.Dispose();
            if (m_Carry.IsCreated) m_Carry.Dispose();
        }

        /// <summary>The server's tick rate: the world's own, else NetCode's default (never a number of ours).</summary>
        public static int TickRateOf(bool found, ClientServerTickRate rate)
        {
            if (!found) rate = default;
            rate.ResolveDefaults();
            return rate.SimulationTickRate;
        }

        protected override void OnUpdate()
        {
            PlayerMoveSettings cfg = SystemAPI.GetSingleton<PlayerMoveSettings>();
            if (cfg.PersonalRadiusMm <= 0 || cfg.SlideOutMmps <= 0) return;
            NetworkTime time = SystemAPI.GetSingleton<NetworkTime>();
            if (!time.ServerTick.IsValid) return;
            long started = Stopwatch.GetTimestamp();
            uint tick = time.ServerTick.TickIndexForValidTick;
            int tickRate = TickRateOf(SystemAPI.TryGetSingleton(out ClientServerTickRate rate), rate);
            double stepMm = PlayerSeparation.StepMm(cfg.SlideOutMmps, tickRate);
            m_Plans ??= World.GetExistingSystemManaged<PlayerServerPlanSystem>();
            // the walkable surface the server's walk is planned on (NavMeshPathPlanner); none = a flat test world, nothing to clamp to
            IWalkableSurface surface = m_Plans != null ? m_Plans.Planner as IWalkableSurface : null;

            if (m_Serial) SerialUpdate(tick, cfg.PersonalRadiusMm, stepMm, surface);
            else JobUpdate(tick, cfg.PersonalRadiusMm, stepMm, surface);
            m_Gauge.Add(KryticalNova.Diagnostics.PerformanceLog.GaugeNames.SeparationMs, started);
            Report(started);
        }

        long m_ReportAt, m_ReportTicks;
        int m_ReportActiveMost, m_ReportSlides, m_ReportBlocked, m_ReportFellBack;
        double m_ReportMs;

        /// <summary>Every 10 s while the server CSV is written: what the separation did (why it costs what it costs).</summary>
        void Report(long started)
        {
            if (!KryticalNova.Diagnostics.PerformanceLog.GaugesOn) return;
            long now = Stopwatch.GetTimestamp();
            if (m_ReportAt == 0) { m_ReportAt = now; m_ReportSlides = Slides; m_ReportBlocked = Blocked; m_ReportFellBack = FellBack; }
            m_ReportTicks++;
            m_ReportMs += (now - started) * 1000.0 / Stopwatch.Frequency;
            m_ReportActiveMost = math.max(m_ReportActiveMost, Active);
            if (now - m_ReportAt < 10 * Stopwatch.Frequency) return;
            UnityEngine.Debug.Log($"[Separation] {(m_Serial ? "serial" : "jobs")}: {m_ReportTicks} ticks, {m_ReportMs / m_ReportTicks:F3} ms/tick, " +
                $"{m_Players.CalculateEntityCount()} players, most to the checked slides {m_ReportActiveMost}, slides {Slides - m_ReportSlides}, " +
                $"blocked {Blocked - m_ReportBlocked}, run again {FellBack - m_ReportFellBack}");
            m_ReportAt = now; m_ReportTicks = 0; m_ReportMs = 0; m_ReportActiveMost = 0;
            m_ReportSlides = Slides; m_ReportBlocked = Blocked; m_ReportFellBack = FellBack;
        }

        void JobUpdate(uint tick, int radiusMm, double stepMm, IWalkableSurface surface)
        {
            int count = m_Players.CalculateEntityCount();
            var gathered = new NativeArray<PlayerSeparation.Gathered>(count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            var standing = new NativeList<PlayerSeparation.Body>(math.max(count, 1), Allocator.TempJob);
            var active = new NativeList<int>(math.max(count, 1), Allocator.TempJob);

            // 1. who stands still (parallel over the players' chunks)
            JobHandle handle = new PlayerSeparationGatherJob
            {
                Plans = SystemAPI.GetComponentLookup<MotionPlan>(true),
                Vitals = SystemAPI.GetComponentLookup<PlayerVitals>(true),
                Out = gathered,
            }.ScheduleParallel(m_Players, Dependency);
            // 2. since when (the same bookkeeping as before, in the old order)
            handle = new PlayerSeparation.StoodSinceJob
            {
                Gathered = gathered, StoodSince = m_StoodSince, Carry = m_Carry, Tick = tick, Standing = standing,
            }.Schedule(handle);
            // 3. the grid, the parallel search of every cell for a pair too close, the groups those pairs stand in
            handle = PlayerSeparation.ScheduleFindActive(standing, radiusMm, PlayerSeparation.SlackMm(radiusMm), active, Allocator.TempJob, handle);
            // the walkable surface is NavMesh (main thread only) and the poses are written here: the result is needed now
            handle.Complete();
            Dependency = handle;

            gathered.Dispose();
            Active = active.Length;
            if (active.Length > 0) Resolve(standing, active, radiusMm, stepMm, surface);
            standing.Dispose();
            active.Dispose();
        }

        void Resolve(NativeList<PlayerSeparation.Body> standing, NativeList<int> active, int radiusMm, double stepMm, IWalkableSurface surface)
        {
            var slide = new ServerSlide
            {
                Surface = surface,
                Carry = m_Carry,
                Pending = new NativeHashMap<Entity, double2>(active.Length, Allocator.Temp),
                Dropped = new NativeHashSet<Entity>(8, Allocator.Temp),
                Moved = new NativeHashSet<Entity>(active.Length, Allocator.Temp),
            };
            Blocked += PlayerSeparation.ResolveActive(standing, active, radiusMm, stepMm, PlayerSeparation.SlackMm(radiusMm), ref slide, out bool fellBack);
            if (fellBack) FellBack++;
            Commit(standing, ref slide);
        }

        /// <summary>The slides were made on the list; now the carry and every moved player's pose.</summary>
        void Commit(NativeList<PlayerSeparation.Body> standing, ref ServerSlide slide)
        {
            foreach (Entity e in slide.Dropped) m_Carry.Remove(e);
            foreach (KVPair<Entity, double2> kv in slide.Pending) m_Carry[kv.Key] = kv.Value;
            if (slide.Moved.Count > 0)
            {
                ComponentLookup<PlayerPose> poses = SystemAPI.GetComponentLookup<PlayerPose>();
                ComponentLookup<LocalTransform> transforms = SystemAPI.GetComponentLookup<LocalTransform>();
                for (int k = 0; k < standing.Length; k++)
                {
                    PlayerSeparation.Body b = standing[k];
                    if (!slide.Moved.Contains(b.Entity)) continue;
                    PlayerPose pose = poses[b.Entity];
                    pose.XMm = b.XMm;
                    pose.ZMm = b.ZMm;
                    poses[b.Entity] = pose;
                    LocalTransform transform = transforms[b.Entity];
                    transform.Position = new float3(b.XMm, pose.YMm, b.ZMm) * 0.001f;
                    transforms[b.Entity] = transform;
                }
            }
            Slides += slide.Slides;
            slide.Pending.Dispose();
            slide.Dropped.Dispose();
            slide.Moved.Dispose();
        }

        /// <summary>The server's slide: the walkable surface and the carry. Nothing is written to a player until <see cref="Commit"/>,
        /// so a pass that has to be run again over everyone (<see cref="PlayerSeparation.IUndoableSlide.Undo"/>) leaves no trace.</summary>
        struct ServerSlide : PlayerSeparation.IUndoableSlide
        {
            public IWalkableSurface Surface;
            public NativeHashMap<Entity, double2> Carry;
            public NativeHashMap<Entity, double2> Pending;
            public NativeHashSet<Entity> Dropped;
            public NativeHashSet<Entity> Moved;
            public int Slides;

            bool CarryOf(Entity e, out double2 carry)
            {
                carry = default;
                if (Dropped.Contains(e)) return false;
                if (Pending.TryGetValue(e, out carry)) return true;
                return Carry.TryGetValue(e, out carry);
            }

            void SetCarry(Entity e, double2 carry) { Pending[e] = carry; Dropped.Remove(e); }
            void DropCarry(Entity e) { Pending.Remove(e); Dropped.Add(e); }

            public bool TrySlide(NativeList<PlayerSeparation.Body> standing, int at, double2 moveMm)
            {
                PlayerSeparation.Body b = standing[at];
                CarryOf(b.Entity, out double2 carry);
                int2 whole = PlayerSeparation.Carry(ref carry, moveMm);
                if (whole.x == 0 && whole.y == 0) { SetCarry(b.Entity, carry); return true; }   // under a millimetre this tick: kept for the next
                var from = new int3(b.XMm, b.YMm, b.ZMm);
                var to = new int3(b.XMm + whole.x, b.YMm, b.ZMm + whole.y);
                if (Surface != null)
                {
                    if (!Surface.TryClampStep(from, to, out int3 reached) || (reached.x == from.x && reached.z == from.z))
                    {
                        DropCarry(b.Entity);
                        return false;
                    }
                    if (reached.x != to.x || reached.z != to.z) carry = double2.zero;   // stopped at the edge: no leftover pushes it on
                    to = new int3(reached.x, b.YMm, reached.z);
                }
                SetCarry(b.Entity, carry);
                b.XMm = to.x;
                b.ZMm = to.z;
                standing[at] = b;
                Moved.Add(b.Entity);
                Slides++;
                return true;
            }

            public void Undo()
            {
                Pending.Clear();
                Dropped.Clear();
                Moved.Clear();
                Slides = 0;
            }
        }

        // ---- the old main-thread pass (before perf round 4), kept for the same build's before/after ----

        void SerialUpdate(uint tick, int radiusMm, double stepMm, IWalkableSurface surface)
        {
            CompleteDependency();
            var standing = new NativeList<PlayerSeparation.Body>(16, Allocator.Temp);
            var seen = new NativeHashSet<Entity>(16, Allocator.Temp);
            foreach (var (move, pose, entity) in SystemAPI.Query<RefRO<MoveState>, RefRO<PlayerPose>>().WithAll<PlayerGhostTag>().WithEntityAccess())
            {
                seen.Add(entity);
                bool still = PlayerSeparation.IsStanding(in move.ValueRO);
                if (still && SystemAPI.HasComponent<MotionPlan>(entity)) still = SystemAPI.GetComponent<MotionPlan>(entity).Kind == MotionKind.None;
                if (still && SystemAPI.HasComponent<PlayerVitals>(entity))
                {
                    PlayerVitals v = SystemAPI.GetComponent<PlayerVitals>(entity);
                    still = v.MaxHealthMilli <= 0 || v.HealthMilli > 0; // a fallen body is left where it fell
                }
                if (!still) { m_StoodSince.Remove(entity); m_Carry.Remove(entity); continue; }
                if (!m_StoodSince.TryGetValue(entity, out uint since)) { since = tick; m_StoodSince[entity] = tick; }
                standing.Add(new PlayerSeparation.Body { Entity = entity, XMm = pose.ValueRO.XMm, YMm = pose.ValueRO.YMm, ZMm = pose.ValueRO.ZMm, StoodSince = since });
            }
            // players who left the world: forget them
            NativeArray<Entity> keys = m_StoodSince.GetKeyArray(Allocator.Temp);
            for (int k = 0; k < keys.Length; k++) if (!seen.Contains(keys[k])) { m_StoodSince.Remove(keys[k]); m_Carry.Remove(keys[k]); }

            Active = standing.Length;
            var slide = new SerialSlide { System = this, Surface = surface };
            Blocked += PlayerSeparation.ResolvePairs(standing, radiusMm, stepMm, ref slide);
        }

        struct SerialSlide : PlayerSeparation.ISlide
        {
            public PlayerSeparationSystem System;
            public IWalkableSurface Surface;
            public bool TrySlide(NativeList<PlayerSeparation.Body> standing, int at, double2 moveMm) => System.SerialTrySlide(standing, at, moveMm, Surface);
        }

        bool SerialTrySlide(NativeList<PlayerSeparation.Body> standing, int at, double2 moveMm, IWalkableSurface surface)
        {
            PlayerSeparation.Body b = standing[at];
            m_Carry.TryGetValue(b.Entity, out double2 carry);
            int2 whole = PlayerSeparation.Carry(ref carry, moveMm);
            if (whole.x == 0 && whole.y == 0) { m_Carry[b.Entity] = carry; return true; }
            var from = new int3(b.XMm, b.YMm, b.ZMm);
            var to = new int3(b.XMm + whole.x, b.YMm, b.ZMm + whole.y);
            if (surface != null)
            {
                if (!surface.TryClampStep(from, to, out int3 reached) || (reached.x == from.x && reached.z == from.z))
                {
                    m_Carry.Remove(b.Entity);
                    return false;
                }
                if (reached.x != to.x || reached.z != to.z) carry = double2.zero;
                to = new int3(reached.x, b.YMm, reached.z);
            }
            m_Carry[b.Entity] = carry;
            b.XMm = to.x;
            b.ZMm = to.z;
            standing[at] = b;
            RefRW<PlayerPose> pose = SystemAPI.GetComponentRW<PlayerPose>(b.Entity);
            pose.ValueRW.XMm = b.XMm;
            pose.ValueRW.ZMm = b.ZMm;
            RefRW<LocalTransform> transform = SystemAPI.GetComponentRW<LocalTransform>(b.Entity);
            transform.ValueRW.Position = new float3(b.XMm, pose.ValueRO.YMm, b.ZMm) * 0.001f;
            Slides++;
            return true;
        }
    }

    /// <summary>The rule of <see cref="PlayerSeparationSystem"/>, in integers (mm): pure, so it is tested without a world.</summary>
    public static partial class PlayerSeparation
    {
        public struct Body
        {
            public Entity Entity;
            public int XMm, YMm, ZMm;
            public uint StoodSince;
        }

        /// <summary>One slide of a standing body (the server's moves the pose; tests use their own). Must write the new spot into
        /// <c>standing[at]</c>; true when the body slid (or kept a part of a millimetre for later), false when it could not.</summary>
        public interface ISlide
        {
            bool TrySlide(NativeList<Body> standing, int at, double2 moveMm);
        }

        /// <summary>A slide that can forget every slide since it was made (or last undone), carries included.</summary>
        public interface IUndoableSlide : ISlide
        {
            void Undo();
        }

        /// <summary>
        /// Every standing pair closer than two circles, in the order of the old loop over every pair (i, then j &gt; i, positions as
        /// they are when the pair comes up): the one who stopped last slides, else the other steps aside, else the pair is blocked.
        /// Returns how many pairs were blocked. A grid of cells one personal space wide (two circles) finds the pairs: a pair that
        /// touches is always in neighbouring cells, the grid follows every slide, and when <c>i</c> itself slides its later partners
        /// are looked for again from where it now stands, so the answer is the old loop's exactly (perf night chunk 6).
        /// </summary>
        public static int ResolvePairs<T>(NativeList<Body> standing, int radiusMm, double stepMm, ref T slide) where T : struct, ISlide
        {
            int n = standing.Length;
            if (n < 2 || radiusMm <= 0) return 0;
            long cell = 2L * radiusMm;
            int blocked = 0;
            var grid = new NativeParallelMultiHashMap<long, int>(n, Allocator.Temp);
            var cellOf = new NativeArray<long>(n, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            for (int k = 0; k < n; k++) { cellOf[k] = CellKey(standing[k], cell); grid.Add(cellOf[k], k); }
            var candidates = new NativeList<int>(16, Allocator.Temp);
            for (int i = 0; i < n; i++)
            {
                int after = i;   // the pairs (i, j <= after) are done
                bool again = true;
                while (again)
                {
                    again = false;
                    Near(grid, standing[i], cell, after, candidates);
                    candidates.Sort();
                    for (int c = 0; c < candidates.Length; c++)
                    {
                        int j = candidates[c];
                        after = j;
                        if (!SlideOut(standing[i], standing[j], radiusMm, stepMm, out int which, out double2 away)) continue;
                        int first = which == 0 ? i : j, second = which == 0 ? j : i;
                        int moved = -1;
                        if (slide.TrySlide(standing, first, away)) moved = first;
                        else if (slide.TrySlide(standing, second, -away)) moved = second;
                        else blocked++;
                        if (moved < 0) continue;
                        long now = CellKey(standing[moved], cell);
                        if (now != cellOf[moved]) { Unfile(grid, cellOf[moved], moved); grid.Add(now, moved); cellOf[moved] = now; }
                        if (moved == i) { again = true; break; }   // i stands somewhere else now: its later partners are looked for again
                    }
                }
            }
            candidates.Dispose();
            cellOf.Dispose();
            grid.Dispose();
            return blocked;
        }

        static long CellKey(in Body b, long cell)
        {
            long cx = FloorDiv(b.XMm, cell), cz = FloorDiv(b.ZMm, cell);
            return (cx << 32) ^ (cz & 0xFFFFFFFFL);
        }

        static long FloorDiv(long v, long d) => v >= 0 ? v / d : -((-v + d - 1) / d);

        /// <summary>The bodies after <paramref name="after"/> filed in the 3 x 3 cells around <paramref name="b"/>.</summary>
        static void Near(NativeParallelMultiHashMap<long, int> grid, in Body b, long cell, int after, NativeList<int> into)
        {
            into.Clear();
            long cx = FloorDiv(b.XMm, cell), cz = FloorDiv(b.ZMm, cell);
            for (long x = cx - 1; x <= cx + 1; x++)
            for (long z = cz - 1; z <= cz + 1; z++)
            {
                long key = (x << 32) ^ (z & 0xFFFFFFFFL);
                if (!grid.TryGetFirstValue(key, out int k, out NativeParallelMultiHashMapIterator<long> it)) continue;
                do { if (k > after) into.Add(k); } while (grid.TryGetNextValue(out k, ref it));
            }
        }

        static void Unfile(NativeParallelMultiHashMap<long, int> grid, long key, int k)
        {
            if (!grid.TryGetFirstValue(key, out int v, out NativeParallelMultiHashMapIterator<long> it)) return;
            do { if (v == k) { grid.Remove(it); return; } } while (grid.TryGetNextValue(out v, ref it));
        }

        /// <summary>Standing still on the ground: no walk, not on the water or a wall.</summary>
        public static bool IsStanding(in MoveState move) => (move.Flags & MoveFlags.Moving) == 0 && move.Water == 0 && move.Wall == 0;

        /// <summary>
        /// When <paramref name="a"/> and <paramref name="b"/> stand closer than two circles (and about level: within 1.5 m up or down),
        /// which of them moves (0 = a, 1 = b: the one who stopped last; the higher entity index on a tie) and by how much (x, z in mm,
        /// at most <paramref name="stepMm"/>, never past touching).
        /// </summary>
        public static bool SlideOut(in Body a, in Body b, int radiusMm, int stepMm, out int which, out int2 moveMm)
        {
            moveMm = int2.zero;
            if (!SlideOut(a, b, radiusMm, (double)stepMm, out which, out double2 move)) return false;
            moveMm = new int2((int)math.round(move.x), (int)math.round(move.y));
            return moveMm.x != 0 || moveMm.y != 0;
        }

        /// <summary>As above, in fractions of a millimetre: the system keeps what a tick cannot move yet (<see cref="Carry"/>).</summary>
        public static bool SlideOut(in Body a, in Body b, int radiusMm, double stepMm, out int which, out double2 moveMm)
        {
            which = 0;
            moveMm = double2.zero;
            if (math.abs(a.YMm - b.YMm) > LevelMm) return false;
            long dx = (long)b.XMm - a.XMm, dz = (long)b.ZMm - a.ZMm;
            long apart = 2L * radiusMm;
            long d2 = dx * dx + dz * dz;
            if (d2 >= apart * apart) return false;
            bool bMoves = b.StoodSince > a.StoodSince || (b.StoodSince == a.StoodSince && b.Entity.Index > a.Entity.Index);
            which = bMoves ? 1 : 0;
            double d = math.sqrt((double)d2);
            double2 away;
            if (d < 1.0)
            {
                // exactly on top of each other: a fixed way for the pair, the same every tick
                int lo = math.min(a.Entity.Index, b.Entity.Index), hi = math.max(a.Entity.Index, b.Entity.Index);
                double angle = (((lo * 73856093) ^ (hi * 19349663)) & 0x7FFFFFFF) % 360 * (math.PI_DBL / 180.0);
                away = new double2(math.cos(angle), math.sin(angle));
                if (!bMoves) away = -away;
            }
            else
            {
                away = new double2(dx / d, dz / d);     // from a toward b
                if (!bMoves) away = -away;             // a moves away from b
            }
            double step = math.min(stepMm, apart - d);
            if (step <= 0) return false;
            moveMm = away * step;
            return true;
        }

        /// <summary>Two bodies further apart in height than this never push each other (a ledge above).</summary>
        public const int LevelMm = 1500;

        /// <summary>One tick's slide in mm at <paramref name="slideMmps"/>, fractional (1500 mm/s at 45 ticks is 33.3 mm).</summary>
        public static double StepMm(int slideMmps, int tickRate) => tickRate > 0 ? (double)slideMmps / tickRate : 0;

        /// <summary>Adds <paramref name="moveMm"/> to what was left over; returns the whole millimetres to move now and keeps the rest.</summary>
        public static int2 Carry(ref double2 carry, double2 moveMm)
        {
            double2 total = carry + moveMm;
            var whole = new int2((int)math.trunc(total.x), (int)math.trunc(total.y));
            carry = total - new double2(whole.x, whole.y);
            return whole;
        }
    }
}
