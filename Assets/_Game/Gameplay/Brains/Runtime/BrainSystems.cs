using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;

namespace KryticalNova.Brains
{
    /// <summary>
    /// The server's brain tick (implementation plan s1.3), after the players' predicted movement and melee: the mind's bridges, the
    /// sense grids, the think clock, then the mind's own systems.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(PredictedSimulationSystemGroup))]
    public partial class BrainSimulationGroup : ComponentSystemGroup
    {
        BrainTimeWindow m_Window;

        /// <summary>
        /// The brains' whole tick, timed only while the performance log records (M6.6 soak pass line "brains &lt; 0.3 ms at 102"): one
        /// "brains" line a second with its ticks, mean and longest tick, the number being the longest in microseconds.
        /// </summary>
        protected override void OnUpdate()
        {
            // also timed while the server CSV wants gauges (wave 4 soak: a batch-mode server records no performance log)
            if (!KryticalNova.Diagnostics.PerformanceLog.IsRecording && !KryticalNova.Diagnostics.PerformanceLog.GaugesOn && !BrainTickSamples.On) { base.OnUpdate(); return; }
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            base.OnUpdate();
            long end = System.Diagnostics.Stopwatch.GetTimestamp();
            if (BrainTickSamples.On) BrainTickSamples.Add((end - start) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
            if (m_Window.Add(start, end, System.Diagnostics.Stopwatch.Frequency, out int ticks, out double meanMs, out double maxMs))
            {
                KryticalNova.Diagnostics.PerformanceLog.SetGauge(KryticalNova.Diagnostics.PerformanceLog.GaugeNames.BrainsMs, maxMs);
                if (KryticalNova.Diagnostics.PerformanceLog.IsRecording)
                    KryticalNova.Diagnostics.PerformanceLog.Mark("brains", $"{ticks} ticks, mean {meanMs:F3} ms, longest {maxMs:F3} ms", (long)(maxMs * 1000.0));
            }
        }
    }

    /// <summary>
    /// Every brain tick's time while <see cref="On"/> (wave 4 brain soak: the test-lane server's <c>-knbrainsoak</c> reads p50 / p99 / max
    /// from it every 10 s and clears it). Off = nothing kept. Main thread only.
    /// </summary>
    public static class BrainTickSamples
    {
        public static bool On;
        static readonly System.Collections.Generic.List<double> Samples = new System.Collections.Generic.List<double>(512);

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { On = false; Samples.Clear(); }

        public static void Add(double ms) { if (On) Samples.Add(ms); }

        /// <summary>The kept ticks' p50 / p99 / max and count, then forgets them; false when none were kept.</summary>
        public static bool TakeSummary(out double p50, out double p99, out double max, out int count)
        {
            count = Samples.Count;
            p50 = p99 = max = 0;
            if (count == 0) return false;
            Samples.Sort();
            p50 = Samples[count / 2];
            p99 = Samples[System.Math.Min(count - 1, (int)(count * 0.99))];
            max = Samples[count - 1];
            Samples.Clear();
            return true;
        }
    }

    /// <summary>The brain tick times of one second, summed up when the second is over (pure, tested).</summary>
    public struct BrainTimeWindow
    {
        long m_Since;
        int m_Ticks;
        double m_SumMs, m_MaxMs;

        /// <summary>Adds one tick that ran from <paramref name="start"/> to <paramref name="end"/>; true (with the second's numbers) once a second has passed.</summary>
        public bool Add(long start, long end, long frequency, out int ticks, out double meanMs, out double maxMs)
        {
            if (m_Ticks == 0) m_Since = start;
            double ms = (end - start) * 1000.0 / frequency;
            m_Ticks++;
            m_SumMs += ms;
            if (ms > m_MaxMs) m_MaxMs = ms;
            ticks = m_Ticks;
            meanMs = m_SumMs / m_Ticks;
            maxMs = m_MaxMs;
            if (end - m_Since < frequency) return false;
            this = default;
            return true;
        }
    }

    /// <summary>Copies the server tick into <see cref="BrainTick"/> (tests set it themselves; ticks start at 1).</summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(BrainSimulationGroup), OrderFirst = true)]
    [BurstCompile]
    public partial struct BrainTickSystem : ISystem
    {
        public void OnCreate(ref SystemState state) => state.EntityManager.CreateSingleton(new BrainTick());

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (!SystemAPI.TryGetSingleton(out NetworkTime time) || !time.ServerTick.IsValid) return;
            SystemAPI.SetSingleton(new BrainTick { Tick = time.ServerTick.TickIndexForValidTick });
        }
    }

    /// <summary>
    /// Rebuilds the sense grids from <see cref="SenseWorld.Players"/> (filled earlier this tick by the mind's feeder) and the brains'
    /// places. Owns <see cref="SenseWorld"/>'s containers.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(BrainSimulationGroup))]
    [UpdateAfter(typeof(BrainTickSystem))]
    [BurstCompile]
    public partial struct SenseBuildSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<BrainConfig>();
            state.EntityManager.CreateSingleton(new SenseWorld
            {
                Players = new NativeList<SensedPlayer>(64, Allocator.Persistent),
                PlayerGrid = new NativeParallelMultiHashMap<int2, int>(256, Allocator.Persistent),
                BrainGrid = new NativeParallelMultiHashMap<int2, Entity>(1024, Allocator.Persistent),
                ByGhostId = new NativeParallelHashMap<int, int>(64, Allocator.Persistent),
            });
        }

        public void OnDestroy(ref SystemState state)
        {
            if (!SystemAPI.TryGetSingleton(out SenseWorld world)) return;
            if (world.Players.IsCreated) world.Players.Dispose();
            if (world.PlayerGrid.IsCreated) world.PlayerGrid.Dispose();
            if (world.BrainGrid.IsCreated) world.BrainGrid.Dispose();
            if (world.ByGhostId.IsCreated) world.ByGhostId.Dispose();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            BrainConfig c = SystemAPI.GetSingleton<BrainConfig>();
            SenseWorld world = SystemAPI.GetSingleton<SenseWorld>();
            world.PlayerGrid.Clear();
            world.ByGhostId.Clear();
            world.BrainGrid.Clear();
            // job later (perf phase): parallel writers into the grids
            for (int i = 0; i < world.Players.Length; i++)
            {
                SensedPlayer p = world.Players[i];
                world.PlayerGrid.Add(SenseQueries.CellOf(p.Feet, c.PlayerCell), i);
                world.ByGhostId[p.GhostId] = i;
            }
            foreach (var (transform, entity) in SystemAPI.Query<RefRO<LocalTransform>>().WithAll<BrainTag>().WithEntityAccess())
                world.BrainGrid.Add(SenseQueries.CellOf(transform.ValueRO.Position, c.BrainCell), entity);
        }
    }

    /// <summary>
    /// Each awake brain's tier from the nearest player (or a fight) and whether it thinks this tick (<see cref="BrainRules.Step"/>); an
    /// event of this tick makes it think now. Every think-time decision of a mind reads <see cref="BrainClock.ThinkNow"/>.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(BrainSimulationGroup))]
    [UpdateAfter(typeof(SenseBuildSystem))]
    [BurstCompile]
    public partial struct ThinkClockSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<BrainConfig>();
            state.RequireForUpdate<SenseWorld>();
            state.RequireForUpdate<BrainTick>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            BrainConfig c = SystemAPI.GetSingleton<BrainConfig>();
            SenseWorld world = SystemAPI.GetSingleton<SenseWorld>();
            uint now = SystemAPI.GetSingleton<BrainTick>().Tick;
            float farthest = math.sqrt(c.TierRadiusSq.z);
            // job later (perf phase): an IJobEntity over the brains (the grid is read-only here)
            foreach (var (clock, events, transform) in SystemAPI.Query<RefRW<BrainClock>, DynamicBuffer<BrainEvent>, RefRO<LocalTransform>>()
                         .WithAll<BrainTag, BrainActive>())
            {
                ref BrainClock k = ref clock.ValueRW;
                bool eventNow = false;
                for (int i = 0; i < events.Length; i++)
                {
                    BrainEvent e = events[i];
                    if (e.Tick != now) continue;
                    eventNow = true;
                    if (e.Kind == BrainEventKind.Damaged || e.Kind == BrainEventKind.Aggro || e.Kind == BrainEventKind.Assist
                        || e.Kind == BrainEventKind.Taunt)
                        k.EngagedUntilTick = now + c.EngagedLingerTicks;
                }
                bool engaged = now < k.EngagedUntilTick;
                float nearestSq = SenseQueries.NearestSq(world, transform.ValueRO.Position, farthest, c.PlayerCell);
                BrainRules.Step(ref k, BrainRules.TierFor(nearestSq, engaged, c), eventNow, now, c);
            }
        }
    }
}
