using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;

namespace KryticalNova.Vegetation.Grass.Entities
{
    /// <summary>
    /// Every frame, on all cores: each tile's distance, frustum test and look (near / far / out, with hysteresis), its
    /// density scale and tint; then the streaming queue: tiles that came into range are built nearest first, a budget per
    /// frame, and tiles that left range are let go. Client and presentation worlds only.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.Presentation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [UpdateAfter(typeof(GrassTileViewSystem))]
    [BurstCompile]
    public partial struct GrassTileCullSystem : ISystem
    {
        ProfilerMarker m_CullMarker, m_StreamMarker, m_ScheduleMarker;
        EntityQuery m_Tiles;
        ComponentTypeHandle<GrassTile> m_TileHandle;
        ComponentTypeHandle<GrassTileState> m_StateHandle;
        ComponentTypeHandle<GrassChunkBounds> m_BoundsHandle;
        EntityTypeHandle m_EntityHandle;

        public void OnCreate(ref SystemState state)
        {
            m_Tiles = SystemAPI.QueryBuilder().WithAll<GrassTile, GrassSection>().WithAllRW<GrassTileState>()
                .WithAllChunkComponent<GrassChunkBounds>().Build();
            m_TileHandle = state.GetComponentTypeHandle<GrassTile>(true);
            m_StateHandle = state.GetComponentTypeHandle<GrassTileState>();
            m_BoundsHandle = state.GetComponentTypeHandle<GrassChunkBounds>(true);
            m_EntityHandle = state.GetEntityTypeHandle();
            m_CullMarker = new ProfilerMarker(GrassTileMarkers.Cull);
            m_StreamMarker = new ProfilerMarker(GrassTileMarkers.Stream);
            m_ScheduleMarker = new ProfilerMarker(GrassTileMarkers.Schedule);
            state.RequireForUpdate<GrassView>();
            state.RequireForUpdate<GrassTileStats>();
            state.RequireForUpdate(m_Tiles);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            GrassView view = SystemAPI.GetSingleton<GrassView>();
            if (!view.Valid) return;
            using var schedule = m_ScheduleMarker.Auto();
            m_TileHandle.Update(ref state);
            m_StateHandle.Update(ref state);
            m_BoundsHandle.Update(ref state);
            m_EntityHandle.Update(ref state);

            int tiles = m_Tiles.CalculateEntityCount();
            int chunks = m_Tiles.CalculateChunkCountWithoutFiltering();
            var wanted = new NativeList<Candidate>(tiles, state.WorldUpdateAllocator);
            var leaving = new NativeList<Entity>(tiles, state.WorldUpdateAllocator);
            var perChunk = new NativeArray<int4>(chunks, state.WorldUpdateAllocator);

            JobHandle cull = new CullJob
            {
                View = view,
                TileHandle = m_TileHandle,
                StateHandle = m_StateHandle,
                BoundsHandle = m_BoundsHandle,
                EntityHandle = m_EntityHandle,
                Wanted = wanted.AsParallelWriter(),
                Leaving = leaving.AsParallelWriter(),
                PerChunk = perChunk,
                Marker = m_CullMarker,
            }.ScheduleParallel(m_Tiles, state.Dependency);

            state.Dependency = new StreamJob
            {
                Wanted = wanted,
                Leaving = leaving,
                PerChunk = perChunk,
                Budget = math.max(0, view.StreamBudget),
                Tiles = tiles,
                States = SystemAPI.GetComponentLookup<GrassTileState>(),
                StatsEntity = SystemAPI.GetSingletonEntity<GrassTileStats>(),
                StatsLookup = SystemAPI.GetComponentLookup<GrassTileStats>(),
                Marker = m_StreamMarker,
            }.Schedule(cull);
        }

        public struct Candidate : System.IComparable<Candidate>
        {
            public Entity Entity;
            public float Distance;
            public int CompareTo(Candidate other) => Distance.CompareTo(other.Distance);
        }

        [BurstCompile]
        struct CullJob : IJobChunk
        {
            public GrassView View;
            [ReadOnly] public ComponentTypeHandle<GrassTile> TileHandle;
            public ComponentTypeHandle<GrassTileState> StateHandle;
            [ReadOnly] public ComponentTypeHandle<GrassChunkBounds> BoundsHandle;
            [ReadOnly] public EntityTypeHandle EntityHandle;
            public NativeList<Candidate>.ParallelWriter Wanted;
            public NativeList<Entity>.ParallelWriter Leaving;
            [NativeDisableParallelForRestriction] public NativeArray<int4> PerChunk;   // near, far, visible, resident
            public ProfilerMarker Marker;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                using var scope = Marker.Auto();
                var tiles = chunk.GetNativeArray(ref TileHandle);
                var states = chunk.GetNativeArray(ref StateHandle);
                var entities = chunk.GetNativeArray(EntityHandle);
                GrassChunkBounds bounds = chunk.GetChunkComponentData(ref BoundsHandle);
                float2 cam = View.Camera.xz;
                float limit = View.FarEnd + View.Hysteresis;
                // The whole chunk (one section's tiles) beyond the far edge: every tile is out, no per-tile work.
                bool chunkOut = GrassTileRules.DistanceToRect(cam, bounds.Min, bounds.Max) >= limit;
                int4 counts = 0;
                for (int i = 0; i < chunk.Count; i++)
                {
                    GrassTile tile = tiles[i];
                    GrassTileState s = states[i];
                    s.Distance = chunkOut ? limit : GrassTileRules.DistanceToRect(cam, tile.Min, tile.Max);
                    s.Lod = chunkOut ? GrassTileLod.Out : GrassTileRules.Choose(s.Distance, s.Lod, View.NearEnd, View.FarEnd, View.Hysteresis);
                    s.Visible = s.Lod != GrassTileLod.Out && GrassTileRules.BoxInFrustum(
                        new float3(tile.Min.x, tile.MinY, tile.Min.y), new float3(tile.Max.x, tile.MaxY, tile.Max.y), View);
                    s.DensityScale = View.DensityScale;
                    if (s.Lod == GrassTileLod.Out) { if (s.Resident) Leaving.AddNoResize(entities[i]); }
                    else if (!s.Resident) Wanted.AddNoResize(new Candidate { Entity = entities[i], Distance = s.Distance });
                    states[i] = s;
                    counts += new int4(s.Lod == GrassTileLod.Near ? 1 : 0, s.Lod == GrassTileLod.Far ? 1 : 0, s.Visible ? 1 : 0, s.Resident ? 1 : 0);
                }
                PerChunk[unfilteredChunkIndex] = counts;
            }
        }

        [BurstCompile]
        struct StreamJob : IJob
        {
            public NativeList<Candidate> Wanted;
            public NativeList<Entity> Leaving;
            [ReadOnly] public NativeArray<int4> PerChunk;
            public int Budget, Tiles;
            public ComponentLookup<GrassTileState> States;
            public Entity StatsEntity;
            public ComponentLookup<GrassTileStats> StatsLookup;
            public ProfilerMarker Marker;

            public void Execute()
            {
                using var scope = Marker.Auto();
                for (int i = 0; i < Leaving.Length; i++)
                {
                    GrassTileState s = States[Leaving[i]];
                    s.Resident = false;
                    States[Leaving[i]] = s;
                }
                // Nearest first, so the ground under the camera fills before the horizon.
                Wanted.Sort();
                int built = math.min(Budget, Wanted.Length);
                for (int i = 0; i < built; i++)
                {
                    GrassTileState s = States[Wanted[i].Entity];
                    s.Resident = true;
                    States[Wanted[i].Entity] = s;
                }
                int4 sum = 0;
                for (int c = 0; c < PerChunk.Length; c++) sum += PerChunk[c];
                StatsLookup[StatsEntity] = new GrassTileStats
                {
                    Tiles = Tiles, Near = sum.x, Far = sum.y, Visible = sum.z,
                    Resident = sum.w + built - Leaving.Length, Queued = Wanted.Length - built,
                    StreamedIn = built, StreamedOut = Leaving.Length,
                };
            }
        }
    }

    /// <summary>The profiler marker names of the tile jobs (worker threads), read back into the perf log by GrassTileViewSystem.</summary>
    public static class GrassTileMarkers
    {
        public const string Cull = "Grass.TileCull";
        public const string Stream = "Grass.TileStream";
        public const string Schedule = "Grass.TileSchedule";   // main thread: the cull system's scheduling
    }
}
