using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace KryticalNova.Player.Entities
{
    /// <summary>Perf round 4, step 1 of <see cref="PlayerSeparationSystem"/>: who stands still, in parallel over the players' chunks,
    /// in the order the old foreach met them (the query's own order: <c>EntityIndexInQuery</c>).</summary>
    [BurstCompile]
    public partial struct PlayerSeparationGatherJob : IJobEntity
    {
        [ReadOnly] public ComponentLookup<MotionPlan> Plans;
        [ReadOnly] public ComponentLookup<PlayerVitals> Vitals;
        [NativeDisableParallelForRestriction] public NativeArray<PlayerSeparation.Gathered> Out;

        void Execute(Entity entity, [EntityIndexInQuery] int index, in MoveState move, in PlayerPose pose)
        {
            bool still = PlayerSeparation.IsStanding(in move);
            if (still && Plans.TryGetComponent(entity, out MotionPlan plan)) still = plan.Kind == MotionKind.None;
            if (still && Vitals.TryGetComponent(entity, out PlayerVitals v)) still = v.MaxHealthMilli <= 0 || v.HealthMilli > 0;   // a fallen body is left where it fell
            Out[index] = new PlayerSeparation.Gathered { Entity = entity, XMm = pose.XMm, YMm = pose.YMm, ZMm = pose.ZMm, Still = still };
        }
    }

    public static partial class PlayerSeparation
    {
        /// <summary>One player as the gather found it.</summary>
        public struct Gathered
        {
            public Entity Entity;
            public int XMm, YMm, ZMm;
            public bool Still;
        }

        /// <summary>
        /// How far a group of players with a pair too close may slide in one tick and still be run apart from everyone else: groups
        /// are cut where players stand two circles plus this apart, so a group that moved no further than this never reached another
        /// (<see cref="ResolveActive{T}"/> checks it and runs everyone together when it did). One personal space (two circles).
        /// </summary>
        public static double SlackMm(int radiusMm) => 2.0 * radiusMm;

        /// <summary>Step 2: since when each player stands (the old bookkeeping, unchanged), and the standing list in the old order.</summary>
        [BurstCompile]
        public struct StoodSinceJob : IJob
        {
            [ReadOnly] public NativeArray<Gathered> Gathered;
            public NativeHashMap<Entity, uint> StoodSince;
            public NativeHashMap<Entity, double2> Carry;
            public uint Tick;
            public NativeList<Body> Standing;

            public void Execute()
            {
                Standing.Clear();
                var seen = new NativeHashSet<Entity>(math.max(Gathered.Length, 1), Allocator.Temp);
                for (int k = 0; k < Gathered.Length; k++)
                {
                    Gathered g = Gathered[k];
                    seen.Add(g.Entity);
                    if (!g.Still) { StoodSince.Remove(g.Entity); Carry.Remove(g.Entity); continue; }
                    if (!StoodSince.TryGetValue(g.Entity, out uint since)) { since = Tick; StoodSince[g.Entity] = Tick; }
                    Standing.Add(new Body { Entity = g.Entity, XMm = g.XMm, YMm = g.YMm, ZMm = g.ZMm, StoodSince = since });
                }
                // players who left the world: forget them
                NativeArray<Entity> keys = StoodSince.GetKeyArray(Allocator.Temp);
                for (int k = 0; k < keys.Length; k++) if (!seen.Contains(keys[k])) { StoodSince.Remove(keys[k]); Carry.Remove(keys[k]); }
            }
        }

        /// <summary>The grid the jobs share: the bodies sorted by cell, each cell's run in that order, and a cell's place by its key.</summary>
        public struct Grid
        {
            public NativeList<int> Order;          // body indices, by cell then index
            public NativeList<int2> Cells;         // x, z of each cell (cell coordinates)
            public NativeList<int2> Runs;          // start in Order, count
            public NativeHashMap<long, int> Place; // cell key -> its place in Cells / Runs
            public NativeList<int2> CellOf;        // each body's cell (x, z)
            public NativeList<byte> Close;         // 1 = the body stands too close to another (step 4)

            public Grid(int capacity, Allocator allocator)
            {
                capacity = math.max(capacity, 16);
                Order = new NativeList<int>(capacity, allocator);
                Cells = new NativeList<int2>(capacity, allocator);
                Runs = new NativeList<int2>(capacity, allocator);
                Place = new NativeHashMap<long, int>(capacity, allocator);
                CellOf = new NativeList<int2>(capacity, allocator);
                Close = new NativeList<byte>(capacity, allocator);
            }

            public void Dispose()
            {
                Order.Dispose(); Cells.Dispose(); Runs.Dispose(); Place.Dispose(); CellOf.Dispose(); Close.Dispose();
            }

            public JobHandle Dispose(JobHandle after) => JobHandle.CombineDependencies(
                JobHandle.CombineDependencies(Order.Dispose(after), Cells.Dispose(after), Runs.Dispose(after)),
                JobHandle.CombineDependencies(Place.Dispose(after), CellOf.Dispose(after), Close.Dispose(after)));
        }

        static long Key(int x, int z) => ((long)x << 32) ^ (z & 0xFFFFFFFFL);

        /// <summary>Step 3: files every standing body in a cell one personal space wide (Burst, one pass, sorted).</summary>
        [BurstCompile]
        public struct GridJob : IJob
        {
            [ReadOnly] public NativeList<Body> Standing;
            public long Cell;
            public Grid Grid;

            public void Execute()
            {
                int n = Standing.Length;
                Grid.Order.Clear(); Grid.Cells.Clear(); Grid.Runs.Clear(); Grid.Place.Clear();
                Grid.CellOf.ResizeUninitialized(n);
                Grid.Close.Resize(n, NativeArrayOptions.ClearMemory);
                if (n == 0) return;
                var sorted = new NativeArray<Filed>(n, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                for (int k = 0; k < n; k++)
                {
                    Body b = Standing[k];
                    var c = new int2((int)FloorDiv(b.XMm, Cell), (int)FloorDiv(b.ZMm, Cell));
                    Grid.CellOf[k] = c;
                    sorted[k] = new Filed { Key = Key(c.x, c.y), Index = k };
                }
                sorted.Sort(new ByCellThenIndex());
                for (int s = 0; s < n; s++)
                {
                    int k = sorted[s].Index;
                    if (s == 0 || sorted[s].Key != sorted[s - 1].Key)
                    {
                        Grid.Place[sorted[s].Key] = Grid.Cells.Length;
                        Grid.Cells.Add(Grid.CellOf[k]);
                        Grid.Runs.Add(new int2(s, 0));
                    }
                    int last = Grid.Runs.Length - 1;
                    Grid.Runs[last] = new int2(Grid.Runs[last].x, Grid.Runs[last].y + 1);
                    Grid.Order.Add(k);
                }
            }
        }

        struct Filed { public long Key; public int Index; }

        struct ByCellThenIndex : System.Collections.Generic.IComparer<Filed>
        {
            public int Compare(Filed a, Filed b) => a.Key != b.Key ? a.Key.CompareTo(b.Key) : a.Index.CompareTo(b.Index);
        }

        /// <summary>
        /// Step 4, in parallel over the grid's cells: every body that stands closer than two circles (and level) to any other, as the
        /// tick begins. When nobody does, nobody slides this tick (the old loop would find no pair either), and nothing else runs.
        /// </summary>
        [BurstCompile]
        public struct CloseScanJob : IJobParallelForDefer
        {
            [ReadOnly] public NativeArray<Body> Standing;
            [ReadOnly] public NativeArray<int> Order;
            [ReadOnly] public NativeArray<int2> Cells;
            [ReadOnly] public NativeArray<int2> Runs;
            [ReadOnly] public NativeHashMap<long, int> Place;
            [NativeDisableParallelForRestriction] public NativeArray<byte> Close;   // each body is written only by its own cell
            public long Apart;

            public void Execute(int cell)
            {
                int2 c = Cells[cell];
                int2 run = Runs[cell];
                long apart2 = Apart * Apart;
                for (int r = run.x; r < run.x + run.y; r++)
                {
                    int k = Order[r];
                    Body a = Standing[k];
                    bool close = false;
                    for (int x = c.x - 1; x <= c.x + 1 && !close; x++)
                    for (int z = c.y - 1; z <= c.y + 1 && !close; z++)
                    {
                        if (!Place.TryGetValue(Key(x, z), out int p)) continue;
                        int2 other = Runs[p];
                        for (int o = other.x; o < other.x + other.y; o++)
                        {
                            int m = Order[o];
                            if (m == k) continue;
                            Body b = Standing[m];
                            if (math.abs(a.YMm - b.YMm) > LevelMm) continue;
                            long dx = (long)b.XMm - a.XMm, dz = (long)b.ZMm - a.ZMm;
                            if (dx * dx + dz * dz < apart2) { close = true; break; }
                        }
                    }
                    if (close) Close[k] = 1;
                }
            }
        }

        /// <summary>
        /// Step 5: the groups the close bodies stand in. A group takes in every body closer than two circles plus the slack to one
        /// already in it (height left aside: a bigger group is never wrong). Out: the group's bodies in the old order (ascending).
        /// </summary>
        [BurstCompile]
        public struct GroupJob : IJob
        {
            [ReadOnly] public NativeList<Body> Standing;
            [ReadOnly] public Grid Grid;
            public long Link;    // two circles plus the slack
            public long Cell;
            public NativeList<int> Active;

            public void Execute()
            {
                Active.Clear();
                int n = Standing.Length;
                var queue = new NativeList<int>(16, Allocator.Temp);
                for (int k = 0; k < n; k++) if (Grid.Close[k] != 0) queue.Add(k);
                if (queue.Length == 0) return;
                var marked = new NativeArray<byte>(n, Allocator.Temp, NativeArrayOptions.ClearMemory);
                for (int q = 0; q < queue.Length; q++) marked[queue[q]] = 1;
                int reach = (int)((Link + Cell - 1) / Cell);
                long link2 = Link * Link;
                for (int head = 0; head < queue.Length; head++)
                {
                    int k = queue[head];
                    Body a = Standing[k];
                    int2 c = Grid.CellOf[k];
                    for (int x = c.x - reach; x <= c.x + reach; x++)
                    for (int z = c.y - reach; z <= c.y + reach; z++)
                    {
                        if (!Grid.Place.TryGetValue(Key(x, z), out int p)) continue;
                        int2 run = Grid.Runs[p];
                        for (int o = run.x; o < run.x + run.y; o++)
                        {
                            int m = Grid.Order[o];
                            if (marked[m] != 0) continue;
                            Body b = Standing[m];
                            long dx = (long)b.XMm - a.XMm, dz = (long)b.ZMm - a.ZMm;
                            if (dx * dx + dz * dz >= link2) continue;
                            marked[m] = 1;
                            queue.Add(m);
                        }
                    }
                }
                for (int k = 0; k < n; k++) if (marked[k] != 0) Active.Add(k);
            }
        }

        /// <summary>
        /// Steps 3-5 scheduled after <paramref name="dependsOn"/> (which fills <paramref name="standing"/>): <paramref name="active"/>
        /// gets the bodies of every group with a pair too close, ascending; empty when everyone stands apart.
        /// </summary>
        public static JobHandle ScheduleFindActive(NativeList<Body> standing, int radiusMm, double slackMm, NativeList<int> active,
            Allocator allocator, JobHandle dependsOn)
        {
            long cell = 2L * radiusMm;
            var grid = new Grid(standing.Capacity, allocator);
            JobHandle h = new GridJob { Standing = standing, Cell = cell, Grid = grid }.Schedule(dependsOn);
            h = new CloseScanJob
            {
                Standing = standing.AsDeferredJobArray(),
                Order = grid.Order.AsDeferredJobArray(),
                Cells = grid.Cells.AsDeferredJobArray(),
                Runs = grid.Runs.AsDeferredJobArray(),
                Place = grid.Place,
                Close = grid.Close.AsDeferredJobArray(),
                Apart = cell,
            }.Schedule(grid.Runs, 4, h);
            h = new GroupJob { Standing = standing, Grid = grid, Link = cell + (long)math.ceil(slackMm), Cell = cell, Active = active }.Schedule(h);
            if (allocator == Allocator.Temp || allocator == Allocator.TempJob || allocator == Allocator.Persistent)
                h = grid.Dispose(h);
            return h;
        }

        /// <summary>
        /// The slides of every pair too close, for the bodies in <paramref name="active"/> (ascending; their groups) only, and exactly
        /// as <see cref="ResolvePairs{T}"/> over everyone would make them: the groups are run on their own and kept when no body of
        /// them moved further than <paramref name="slackMm"/> (then none came near a body outside, which stood still); otherwise the
        /// slides are undone and everyone is run together. Returns the blocked pairs; the new spots are written into
        /// <paramref name="standing"/>.
        /// </summary>
        public static int ResolveActive<T>(NativeList<Body> standing, NativeList<int> active, int radiusMm, double stepMm, double slackMm,
            ref T slide, out bool fellBack) where T : struct, IUndoableSlide
        {
            fellBack = false;
            if (active.Length == 0) return 0;
            if (active.Length == standing.Length) return ResolvePairs(standing, radiusMm, stepMm, ref slide);
            var sub = new NativeList<Body>(active.Length, Allocator.Temp);
            for (int k = 0; k < active.Length; k++) sub.Add(standing[active[k]]);
            var tracked = new Tracked<T> { Inner = slide, Path = new NativeArray<double>(active.Length, Allocator.Temp) };
            int blocked = ResolvePairs(sub, radiusMm, stepMm, ref tracked);
            slide = tracked.Inner;
            double most = 0;
            for (int k = 0; k < tracked.Path.Length; k++) most = math.max(most, tracked.Path[k]);
            tracked.Path.Dispose();
            if (most <= slackMm)
            {
                for (int k = 0; k < active.Length; k++) standing[active[k]] = sub[k];
                sub.Dispose();
                return blocked;
            }
            sub.Dispose();
            slide.Undo();
            fellBack = true;
            return ResolvePairs(standing, radiusMm, stepMm, ref slide);
        }

        /// <summary>A slide that adds up how far each body went (the whole path, so it bounds every spot it passed).</summary>
        struct Tracked<T> : ISlide where T : struct, IUndoableSlide
        {
            public T Inner;
            public NativeArray<double> Path;

            public bool TrySlide(NativeList<Body> standing, int at, double2 moveMm)
            {
                Body before = standing[at];
                bool slid = Inner.TrySlide(standing, at, moveMm);
                Body after = standing[at];
                double dx = (double)after.XMm - before.XMm, dz = (double)after.ZMm - before.ZMm;
                Path[at] += math.sqrt(dx * dx + dz * dz);
                return slid;
            }
        }
    }
}
