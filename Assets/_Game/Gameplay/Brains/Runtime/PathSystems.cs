using System.Collections.Generic;
using Unity.AI.Navigation.LowLevel;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine.AI;

namespace KryticalNova.Brains
{
    /// <summary>
    /// Serves the brains' path requests (M5.4, plan s4), last in the brain tick after the mind's systems: the queued ones by priority
    /// (fighting first), then the oldest, at most <see cref="BrainConfig.PathSearchesPerTick"/> a tick; the rest wait their turn. A
    /// found path goes into the brain's <see cref="BrainWaypoint"/> buffer (its corners after the start, at most
    /// <see cref="BrainConfig.MaxWaypoints"/>, the last always the end). The backend is the <see cref="PathBackend"/> singleton's: the
    /// NavMesh through NavWorld (M5.4b: one Burst job a tick for the served searches, each within the node budget), or the straight
    /// line when there is none (tests). The update itself is not Burst: it owns the NavWorld query buffer.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(BrainSimulationGroup), OrderLast = true)]
    [UpdateBefore(typeof(BrainMoveSystem))]
    [BurstCompile]
    public partial struct PathServiceSystem : ISystem
    {
        struct Request
        {
            public byte Priority;
            public uint RequestTick;
            public Entity Brain;
        }

        struct ByTurn : IComparer<Request>
        {
            public int Compare(Request a, Request b)
            {
                if (a.Priority != b.Priority) return b.Priority.CompareTo(a.Priority);
                if (a.RequestTick != b.RequestTick) return a.RequestTick.CompareTo(b.RequestTick);
                return a.Brain.Index.CompareTo(b.Brain.Index);
            }
        }

        NavPaths m_Nav;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<BrainConfig>();
            state.RequireForUpdate<BrainTick>();
        }

        public void OnDestroy(ref SystemState state)
        {
            if (m_Nav.IsCreated) m_Nav.Dispose();
        }

        public void OnUpdate(ref SystemState state)
        {
            BrainConfig c = SystemAPI.GetSingleton<BrainConfig>();
            uint now = SystemAPI.GetSingleton<BrainTick>().Tick;
            var queued = new NativeList<Request>(64, Allocator.Temp);
            foreach (var (path, entity) in SystemAPI.Query<RefRO<BrainPath>>().WithAll<BrainTag, BrainWaypoint>().WithEntityAccess())
                if (path.ValueRO.Status == BrainPathStatus.Queued)
                    queued.Add(new Request { Priority = path.ValueRO.Priority, RequestTick = path.ValueRO.RequestTick, Brain = entity });
            if (queued.Length == 0) return;
            queued.Sort(new ByTurn());

            NativeArray<PathTestWall> walls = SystemAPI.TryGetSingletonBuffer(out DynamicBuffer<PathTestWall> wallBuffer, true)
                ? wallBuffer.AsNativeArray() : default;
            var pathOf = SystemAPI.GetComponentLookup<BrainPath>();
            var waypointsOf = SystemAPI.GetBufferLookup<BrainWaypoint>();
            var transformOf = SystemAPI.GetComponentLookup<LocalTransform>(true);
            int served = math.min(queued.Length, c.PathSearchesPerTick);
            if (SystemAPI.TryGetSingleton(out PathBackend backend) && backend.Kind == PathBackendKind.NavWorld)
            {
                ServeOnNavMesh(ref state, queued, served, backend, c, now);
                return;
            }
            for (int i = 0; i < served; i++)
            {
                Entity brain = queued[i].Brain;
                ref BrainPath path = ref pathOf.GetRefRW(brain).ValueRW;
                float3 from = transformOf.HasComponent(brain) ? transformOf[brain].Position : path.To;
                bool found = PathRules.StraightFind(from, path.To, walls, waypointsOf[brain]);
                path.Status = found ? BrainPathStatus.Ready : BrainPathStatus.Failed;
                path.ReadyTick = now;
            }
        }

        void ServeOnNavMesh(ref SystemState state, NativeList<Request> queued, int served, in PathBackend backend, in BrainConfig c, uint now)
        {
            NavWorld world = NavWorld.GetDefaultWorld();
            if (!m_Nav.IsCreated) m_Nav = NavPaths.Create(world, c.PathNodeBudget);
            var pathOf = SystemAPI.GetComponentLookup<BrainPath>();
            var waypointsOf = SystemAPI.GetBufferLookup<BrainWaypoint>();
            var transformOf = SystemAPI.GetComponentLookup<LocalTransform>(true);
            var starts = new NativeArray<float3>(served, Allocator.TempJob);
            var ends = new NativeArray<float3>(served, Allocator.TempJob);
            var corners = new NativeArray<float3>(served * NavPaths.MaxCorners, Allocator.TempJob);
            var counts = new NativeArray<int>(served, Allocator.TempJob);
            for (int i = 0; i < served; i++)
            {
                Entity brain = queued[i].Brain;
                ends[i] = pathOf[brain].To;
                starts[i] = transformOf.HasComponent(brain) ? transformOf[brain].Position : ends[i];
            }
            m_Nav.Search(world, backend, c.PathNodeBudget, starts, ends, corners, counts);
            for (int i = 0; i < served; i++)
            {
                Entity brain = queued[i].Brain;
                ref BrainPath path = ref pathOf.GetRefRW(brain).ValueRW;
                DynamicBuffer<BrainWaypoint> waypoints = waypointsOf[brain];
                waypoints.Clear();
                int n = counts[i];
                int from = i * NavPaths.MaxCorners;
                // corner 0 is the start; keep the next ones up to the cap, the last one always the end
                for (int k = 1; k < n && waypoints.Length < c.MaxWaypoints; k++)
                    waypoints.Add(new BrainWaypoint { P = corners[from + k] });
                if (n == 1) waypoints.Add(new BrainWaypoint { P = ends[i] });   // start and end on one spot
                if (n > 1 && waypoints.Length == c.MaxWaypoints) waypoints[waypoints.Length - 1] = new BrainWaypoint { P = corners[from + n - 1] };
                path.Status = n > 0 ? BrainPathStatus.Ready : BrainPathStatus.Failed;
                path.ReadyTick = now;
            }
            starts.Dispose(); ends.Dispose(); corners.Dispose(); counts.Dispose();
        }
    }

    /// <summary>
    /// Moves every brain toward its <see cref="BrainMover.Dest"/> (M5.4, plan s4), last in the brain tick: the straight line when nothing
    /// is in the way, else the path it asked the <see cref="PathServiceSystem"/> for (it waits while the request is queued or failed);
    /// seek and arrive within <see cref="PathRules.ArriveRadius"/>; kept <see cref="BrainConfig.SeparationRadius"/> from the other
    /// brains (it never walks into one ahead and steps out of an overlap); on the NavMesh (<see cref="PathBackend"/>) blocked by the
    /// mesh's raycast and standing at the mesh's height; turned toward its motion, or to <see cref="BrainMover.Yaw"/>
    /// with <see cref="BrainMoverFlags.Face"/>, at the fight or idle turn rate. Brains without <see cref="BrainPath"/> and
    /// <see cref="BrainWaypoint"/> are not moved.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(BrainSimulationGroup), OrderLast = true)]
    [BurstCompile]
    public partial struct BrainMoveSystem : ISystem
    {
        NavWorld m_World;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<BrainConfig>();
            state.RequireForUpdate<SenseWorld>();
            state.RequireForUpdate<BrainTick>();
            m_World = NavWorld.GetDefaultWorld();
        }

        /// <summary>The NavMesh's raycast: blocked when the mesh ends between the two points (a start off the mesh is not blocked).</summary>
        static bool NavBlocked(NavWorld world, in PathBackend backend, float3 from, float3 to)
        {
            NavLocation start = world.MapLocation(from, backend.Extents, backend.AgentTypeId);
            if (!world.IsValid(start)) return false;
            world.Raycast(out NavMeshHit hit, start, to);
            return hit.hit;
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            BrainConfig c = SystemAPI.GetSingleton<BrainConfig>();
            SenseWorld sense = SystemAPI.GetSingleton<SenseWorld>();
            uint now = SystemAPI.GetSingleton<BrainTick>().Tick;
            NativeArray<PathTestWall> walls = SystemAPI.TryGetSingletonBuffer(out DynamicBuffer<PathTestWall> wallBuffer, true)
                ? wallBuffer.AsNativeArray() : default;
            bool onNavMesh = SystemAPI.TryGetSingleton(out PathBackend backend) && backend.Kind == PathBackendKind.NavWorld && m_World.IsValid();
            var transformOf = SystemAPI.GetComponentLookup<LocalTransform>(true);
            float dt = c.TickSeconds;
            float r = c.SeparationRadius;

            // every brain decides from where all of them stood at the start of the move (no order effects), then all move at once
            // job later (perf phase): an IJobEntity writing the new places, then a second job applying them
            var moved = new NativeList<Entity>(64, Allocator.Temp);
            var places = new NativeList<LocalTransform>(64, Allocator.Temp);
            foreach (var (mover, path, waypoints, transform, clock, entity) in SystemAPI
                         .Query<RefRO<BrainMover>, RefRW<BrainPath>, DynamicBuffer<BrainWaypoint>, RefRO<LocalTransform>, RefRO<BrainClock>>()
                         .WithAll<BrainTag, BrainActive>().WithEntityAccess())
            {
                BrainMover m = mover.ValueRO;
                LocalTransform t = transform.ValueRO;
                float3 at = t.Position;
                bool engaged = now < clock.ValueRO.EngagedUntilTick;
                ref BrainPath p = ref path.ValueRW;

                float3 steer = at;
                if ((m.Flags & BrainMoverFlags.Stop) == 0)
                {
                    bool blocked = onNavMesh ? NavBlocked(m_World, backend, at, m.Dest) : PathRules.StraightBlocked(at, m.Dest, walls);
                    if (!blocked)
                    {
                        steer = m.Dest;
                        if (p.Status != BrainPathStatus.None) { p.Status = BrainPathStatus.None; waypoints.Clear(); }
                    }
                    else
                    {
                        if (PathRules.NeedsRequest(p, m.Dest, engaged, now, c))
                        {
                            p = new BrainPath { To = m.Dest, RequestTick = now, Status = BrainPathStatus.Queued, Priority = (byte)(engaged ? 1 : 0) };
                            waypoints.Clear();
                        }
                        if (p.Status == BrainPathStatus.Ready && waypoints.Length > 0)
                        {
                            while (waypoints.Length > 1 && SenseQueries.FlatSq(at, waypoints[0].P) <= PathRules.ArriveRadius * PathRules.ArriveRadius)
                                waypoints.RemoveAt(0);
                            steer = waypoints[0].P;
                        }
                    }
                }

                float3 v = PathRules.Seek(at, steer, m.Speed, dt);
                // a brain ahead is avoided from two steps away (both may be walking at each other this tick): the part of the step
                // toward it is dropped; overlaps left over are pushed apart below
                float look = r + 2f * math.length(v.xz) * dt;
                if (r > 0f)
                {
                    int2 cell = SenseQueries.CellOf(at, c.BrainCell);
                    for (int dx = -1; dx <= 1; dx++)
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        if (!sense.BrainGrid.TryGetFirstValue(cell + new int2(dx, dz), out Entity other, out var it)) continue;
                        do
                        {
                            if (other == entity || !transformOf.HasComponent(other)) continue;
                            float2 away = at.xz - transformOf[other].Position.xz;
                            float d = math.length(away);
                            if (d >= look) continue;
                            float2 n = d > 1e-5f ? away / d : PathRules.TieBreak(entity, other);
                            float into = -math.dot(v.xz, n);
                            if (into > 0f) v += new float3(n.x, 0f, n.y) * into;
                        } while (sense.BrainGrid.TryGetNextValue(out other, ref it));
                    }
                }

                float3 step = v * dt;
                float2 toSteer = steer.xz - at.xz;
                float reach = math.length(toSteer);
                // the straight-line backend follows the slope to the steer point (NavWorld's MapLocation height in M5.4b)
                float climb = reach > 1e-4f ? (steer.y - at.y) * math.saturate(math.length(v.xz * dt) / reach) : 0f;
                float3 next = at + step + new float3(0f, climb, 0f);
                if (onNavMesh)
                {
                    // on the NavMesh the ground's height is the mesh's under the new spot (off the mesh: the slope as above)
                    NavLocation ground = m_World.MapLocation(next, backend.Extents, backend.AgentTypeId);
                    if (m_World.IsValid(ground)) next.y = ground.position.y;
                }

                float yaw = PathRules.YawOf(t.Rotation);
                float wanted = (m.Flags & BrainMoverFlags.Face) != 0 ? m.Yaw
                    : math.lengthsq(v.xz) > 1e-6f ? math.atan2(v.x, v.z) : yaw;
                float rate = m.TurnRateRad > 0f ? m.TurnRateRad : engaged ? c.EngagedTurnRad : c.IdleTurnRad;
                yaw = PathRules.TurnToward(yaw, wanted, rate * dt);

                moved.Add(entity);
                places.Add(LocalTransform.FromPositionRotationScale(next, quaternion.RotateY(yaw), t.Scale));
            }
            if (r > 0f && moved.Length > 0) Separate(moved, places, sense, transformOf, c);
            for (int i = 0; i < moved.Length; i++)
                SystemAPI.SetComponent(moved[i], places[i]);
        }

        /// <summary>
        /// Overlaps pushed apart on the new places, <see cref="SeparationPasses"/> times (a crowd squeezing in from every side needs
        /// more than one pass): two moving brains each step half the overlap, a brain that is not moving holds its ground.
        /// </summary>
        static void Separate(NativeList<Entity> moved, NativeList<LocalTransform> places, in SenseWorld sense,
                             in ComponentLookup<LocalTransform> transformOf, in BrainConfig c)
        {
            float r = c.SeparationRadius;
            var index = new NativeParallelHashMap<Entity, int>(moved.Length, Allocator.Temp);
            for (int i = 0; i < moved.Length; i++) index[moved[i]] = i;
            var push = new NativeArray<float2>(moved.Length, Allocator.Temp);
            for (int pass = 0; pass < SeparationPasses; pass++)
            {
                for (int i = 0; i < moved.Length; i++)
                {
                    float3 at = places[i].Position;
                    float2 sum = float2.zero;
                    int2 cell = SenseQueries.CellOf(at, c.BrainCell);
                    for (int dx = -1; dx <= 1; dx++)
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        if (!sense.BrainGrid.TryGetFirstValue(cell + new int2(dx, dz), out Entity other, out var it)) continue;
                        do
                        {
                            if (other == moved[i]) continue;
                            bool moving = index.TryGetValue(other, out int j);
                            if (!moving && !transformOf.HasComponent(other)) continue;
                            float3 there = moving ? places[j].Position : transformOf[other].Position;
                            float2 away = at.xz - there.xz;
                            float d = math.length(away);
                            if (d >= r) continue;
                            float2 n = d > 1e-5f ? away / d : PathRules.TieBreak(moved[i], other);
                            sum += n * ((moving ? 0.5f : 1f) * (r - d));
                        } while (sense.BrainGrid.TryGetNextValue(out other, ref it));
                    }
                    push[i] = sum;
                }
                for (int i = 0; i < moved.Length; i++)
                {
                    LocalTransform t = places[i];
                    t.Position += new float3(push[i].x, 0f, push[i].y);
                    places[i] = t;
                }
            }
        }

        const int SeparationPasses = 4;
    }
}
