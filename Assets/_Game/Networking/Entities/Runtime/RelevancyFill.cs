using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;

namespace KryticalNova.Networking.Entities
{
    // =================================================================================================================
    // Relevancy, the fill (wave 4, M9): which ghosts the server sends to which connection at all. The rule is pure data in,
    // data out (RelevancyFill.Build), so it is tested without a world; RelevancyFillSystem only gathers and writes.
    // No gameplay type is named here: a player is "a ghost with an owner", everything else is an object, and whoever must
    // always see a far ghost says so in AlwaysRelevantGhost (the Callout does, from a higher layer).
    // =================================================================================================================

    /// <summary>The fill's numbers on a server world (from <see cref="RelevancySettings"/>). It exists only while relevancy is on.</summary>
    public struct RelevancyConfig : IComponentData
    {
        /// <summary>Metres, flat: a ghost with an owner (a player) enters a connection's view inside this.</summary>
        public float PlayerRadius;
        /// <summary>Metres, flat: every other ghost (Hostiles, Strike Dummies).</summary>
        public float ObjectRadius;
        /// <summary>Metres past the radius before a ghost that was sent is taken away again.</summary>
        public float LeaveMargin;
        /// <summary>Server ticks between two fills when no ghost or connection appeared or went.</summary>
        public int FillIntervalTicks;
        /// <summary>
        /// Bumped by anything that moves a ghost too far for the next timed fill to be soon enough (a teleport: the players where it
        /// lands must show at once, not up to <see cref="FillIntervalTicks"/> later). A change fills at once, like a ghost appearing.
        /// </summary>
        public uint Nudge;

        public static RelevancyConfig From(RelevancySettings settings) => new RelevancyConfig
        {
            PlayerRadius = math.max(1f, settings.playerRadius),
            ObjectRadius = math.max(1f, settings.objectRadius),
            LeaveMargin = math.max(0f, settings.leaveMargin),
            FillIntervalTicks = math.max(1, settings.fillIntervalTicks),
        };
    }

    /// <summary>
    /// Server only, on a connection: a ghost this connection is sent however far away it is. Each filler keeps its own entries, found
    /// again by <see cref="Reason"/> (a number the filler picks; the Callout keeps its opponent here). The ghost a connection owns
    /// needs no entry.
    /// </summary>
    [InternalBufferCapacity(2)]
    public struct AlwaysRelevantGhost : IBufferElementData
    {
        public int GhostId;
        public byte Reason;
    }

    /// <summary>
    /// Server only, on a connection: the ghosts that became relevant to it in the last fill (sent now, not sent before). The entries
    /// live for one server tick, so a system in the Simulation group reads each exactly once: it is the moment to tell that connection
    /// again what the others were told once by rpc about that ghost (the Recall aura).
    /// </summary>
    [InternalBufferCapacity(0)]
    public struct BecameRelevantGhost : IBufferElementData
    {
        public int GhostId;
    }

    public static class AlwaysRelevantGhosts
    {
        /// <summary>True when the list holds exactly what <see cref="SetOnly"/> would leave: one entry of this reason for the ghost, or none for ghost 0.</summary>
        public static bool IsOnly(DynamicBuffer<AlwaysRelevantGhost> list, byte reason, int ghostId)
        {
            int found = 0;
            for (int i = 0; i < list.Length; i++)
            {
                if (list[i].Reason != reason) continue;
                if (list[i].GhostId != ghostId) return false;
                found++;
            }
            return ghostId == 0 ? found == 0 : found == 1;
        }

        /// <summary>Leaves exactly one entry of <paramref name="reason"/> in the list, for <paramref name="ghostId"/> (0 = none). Other reasons' entries stay.</summary>
        public static void SetOnly(DynamicBuffer<AlwaysRelevantGhost> list, byte reason, int ghostId)
        {
            for (int i = list.Length - 1; i >= 0; i--)
                if (list[i].Reason == reason) list.RemoveAt(i);
            if (ghostId != 0) list.Add(new AlwaysRelevantGhost { GhostId = ghostId, Reason = reason });
        }
    }

    /// <summary>One ghost as the fill sees it: its id, the connection that owns it (0 = nobody: an object) and where it stands, flat.</summary>
    public struct RelevancyGhost
    {
        public int GhostId;
        public int Owner;
        public float2 At;
    }

    /// <summary>One always-relevant pair: the connection and the ghost.</summary>
    public struct RelevancyAlways
    {
        public int NetworkId;
        public int GhostId;
    }

    /// <summary>The relevancy rule's two small decisions (pure, tested).</summary>
    public static class RelevancyRules
    {
        /// <summary>
        /// Is a ghost at squared flat distance <paramref name="distSq"/> sent? Inside the radius: yes. Between the radius and
        /// radius + margin: only when it was sent before (so a body on the edge does not flicker). Past that: no.
        /// </summary>
        public static bool Sees(float distSq, float radius, float margin, bool sentBefore)
        {
            if (distSq <= radius * radius) return true;
            float far = radius + margin;
            return sentBefore && distSq <= far * far;
        }

        /// <summary>The grid cell a flat place falls in.</summary>
        public static int2 Cell(float2 at, float cellSize) => (int2)math.floor(at / cellSize);

        /// <summary>A place in the world, flat: x and z. Height never hides anybody.</summary>
        public static float2 Flat(float3 position) => position.xz;
    }

    /// <summary>One sent pair from a connection's part of the fill, and whether it was not sent before.</summary>
    public struct RelevancyTaken
    {
        public RelevantGhostForConnection Pair;
        public byte Became;
    }

    /// <summary>The fill's grid of ghost places and its owner lookup (perf round 3: built once, read by every connection's part).</summary>
    [BurstCompile]
    struct RelevancyGridJob : IJob
    {
        [ReadOnly] public NativeArray<RelevancyGhost> Ghosts;
        public float CellSize;
        public NativeParallelMultiHashMap<int2, int> Grid;
        public NativeParallelMultiHashMap<int, int> Owned;

        public void Execute()
        {
            for (int i = 0; i < Ghosts.Length; i++)
            {
                Grid.Add(RelevancyRules.Cell(Ghosts[i].At, CellSize), i);
                if (Ghosts[i].Owner != 0) Owned.Add(Ghosts[i].Owner, i);
            }
        }
    }

    /// <summary>
    /// The fill itself, one connection per index, on the job workers (perf round 3: 200 players in one place cost ~22 ms a fill on the
    /// main thread): from the ghosts, the always-relevant pairs and the set sent so far, the pairs of that connection.
    /// A connection's place is where the ghost it owns stands; with no ghost of its own (signed in, no body yet) it has no place and
    /// gets only its always-relevant pairs. The ghosts a connection owns are always in, whatever the distance. Each connection writes
    /// its own stream slot in the order: its own ghosts, its always-relevant ones, then the near ones cell by cell, so the merge gives
    /// exactly the order of the one-thread fill.
    /// </summary>
    [BurstCompile]
    public struct RelevancyFillJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<RelevancyGhost> Ghosts;
        /// <summary>The NetworkId of every in-game connection.</summary>
        [ReadOnly] public NativeArray<int> Viewers;
        [ReadOnly] public NativeArray<RelevancyAlways> Always;
        /// <summary>What is sent now (the last fill): the hysteresis reads it.</summary>
        [ReadOnly] public NativeParallelHashMap<RelevantGhostForConnection, int> Old;
        [ReadOnly] public NativeParallelMultiHashMap<int2, int> Grid;
        [ReadOnly] public NativeParallelMultiHashMap<int, int> Owned;
        public RelevancyConfig Config;
        public float CellSize;

        /// <summary>Out: slot v holds connection v's pairs.</summary>
        public NativeStream.Writer Out;

        public void Execute(int v)
        {
            int id = Viewers[v];
            Out.BeginForEachIndex(v);

            // What it is given besides the near ones (own and always-relevant ghosts), to keep each ghost once. A near ghost is in
            // exactly one cell and its own ghosts are skipped there, so only a connection with always-relevant ghosts needs the check.
            bool hasAlways = false;
            for (int a = 0; a < Always.Length && !hasAlways; a++) hasAlways = Always[a].NetworkId == id && Always[a].GhostId != 0;
            var special = new NativeHashSet<int>(hasAlways ? 16 : 1, Allocator.Temp);

            // its own ghosts: always, and its place
            // ONE owner = ONE ghost today (the player's body; pinned by OneOwnedGhostLintTests): the place is the first owned
            // ghost found. The day a pet, mount or stand becomes an owned ghost, the place must be the BODY's, picked on purpose.
            bool hasPlace = false;
            float2 place = default;
            if (Owned.TryGetFirstValue(id, out int ownIndex, out NativeParallelMultiHashMapIterator<int> ownIt))
            {
                do
                {
                    if (!hasPlace) { hasPlace = true; place = Ghosts[ownIndex].At; }
                    TakeSpecial(id, Ghosts[ownIndex].GhostId, ref special);
                }
                while (Owned.TryGetNextValue(out ownIndex, ref ownIt));
            }

            // whoever it must always see
            if (hasAlways)
                for (int a = 0; a < Always.Length; a++)
                    if (Always[a].NetworkId == id && Always[a].GhostId != 0) TakeSpecial(id, Always[a].GhostId, ref special);

            // everything near its place
            if (hasPlace)
            {
                int2 cell = RelevancyRules.Cell(place, CellSize);
                for (int dx = -1; dx <= 1; dx++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    if (!Grid.TryGetFirstValue(cell + new int2(dx, dz), out int index, out NativeParallelMultiHashMapIterator<int2> it)) continue;
                    do
                    {
                        RelevancyGhost ghost = Ghosts[index];
                        if (ghost.Owner == id) continue;   // its own: taken above
                        if (hasAlways && special.Contains(ghost.GhostId)) continue;   // already given as always-relevant
                        float radius = ghost.Owner != 0 ? Config.PlayerRadius : Config.ObjectRadius;
                        var pair = new RelevantGhostForConnection(id, ghost.GhostId);
                        bool sentBefore = Old.ContainsKey(pair);   // looked up once: the hysteresis and "became" both read it
                        if (RelevancyRules.Sees(math.distancesq(place, ghost.At), radius, Config.LeaveMargin, sentBefore))
                            Out.Write(new RelevancyTaken { Pair = pair, Became = (byte)(sentBefore ? 0 : 1) });
                    }
                    while (Grid.TryGetNextValue(out index, ref it));
                }
            }
            special.Dispose();
            Out.EndForEachIndex();
        }

        void TakeSpecial(int connection, int ghostId, ref NativeHashSet<int> special)
        {
            if (!special.Add(ghostId)) return;
            var pair = new RelevantGhostForConnection(connection, ghostId);
            Out.Write(new RelevancyTaken { Pair = pair, Became = (byte)(Old.ContainsKey(pair) ? 0 : 1) });
        }
    }

    /// <summary>The connections' slots, in connection order, into the pair and "became" lists, and the most for one connection.</summary>
    [BurstCompile]
    struct RelevancyMergeJob : IJob
    {
        public NativeStream.Reader In;
        public int Connections;
        public NativeList<RelevantGhostForConnection> Pairs;
        public NativeList<RelevantGhostForConnection> Became;
        public NativeReference<int> MostForOne;

        public void Execute()
        {
            int most = 0;
            for (int v = 0; v < Connections; v++)
            {
                int count = In.BeginForEachIndex(v);
                most = math.max(most, count);
                for (int i = 0; i < count; i++)
                {
                    RelevancyTaken taken = In.Read<RelevancyTaken>();
                    Pairs.Add(taken.Pair);
                    if (taken.Became != 0) Became.Add(taken.Pair);
                }
                In.EndForEachIndex();
            }
            MostForOne.Value = most;
        }
    }

    /// <summary>The package's set made to hold exactly the pairs, in Burst (56,000 pairs at 200 players in one place).</summary>
    [BurstCompile]
    struct RelevancyWriteJob : IJob
    {
        public NativeParallelHashMap<RelevantGhostForConnection, int> Set;
        [ReadOnly] public NativeArray<RelevantGhostForConnection> Pairs;

        public void Execute()
        {
            Set.Clear();
            if (Pairs.Length > Set.Capacity) Set.Capacity = math.max(Pairs.Length, Set.Capacity * 2);
            for (int i = 0; i < Pairs.Length; i++) Set.TryAdd(Pairs[i], 1);
        }
    }

    public static class RelevancyFill
    {
        /// <summary>Connections per job batch of the parallel fill.</summary>
        const int ConnectionsPerBatch = 8;

        /// <summary>
        /// Runs the fill now, in Burst: the connections' parts on the job workers, then merged in connection order on this thread, so the
        /// answer and its order are the one-thread fill's. <paramref name="pairs"/> and <paramref name="became"/> are emptied first;
        /// the return value is the most ghosts any one connection is sent.
        /// </summary>
        public static int Build(NativeArray<RelevancyGhost> ghosts, NativeArray<int> viewers, NativeArray<RelevancyAlways> always,
            NativeParallelHashMap<RelevantGhostForConnection, int> old, in RelevancyConfig config,
            NativeList<RelevantGhostForConnection> pairs, NativeList<RelevantGhostForConnection> became)
        {
            pairs.Clear();
            became.Clear();
            float reach = math.max(config.PlayerRadius, config.ObjectRadius) + config.LeaveMargin;
            float cellSize = math.max(1f, reach);   // nothing can be seen from farther than one cell away
            var grid = new NativeParallelMultiHashMap<int2, int>(math.max(1, ghosts.Length), Allocator.TempJob);
            var owned = new NativeParallelMultiHashMap<int, int>(math.max(1, ghosts.Length), Allocator.TempJob);
            var stream = new NativeStream(math.max(1, viewers.Length), Allocator.TempJob);
            var most = new NativeReference<int>(Allocator.TempJob);
            try
            {
                new RelevancyGridJob { Ghosts = ghosts, CellSize = cellSize, Grid = grid, Owned = owned }.Run();
                if (viewers.Length > 0)
                {
                    new RelevancyFillJob
                    {
                        Ghosts = ghosts, Viewers = viewers, Always = always, Old = old, Grid = grid, Owned = owned,
                        Config = config, CellSize = cellSize, Out = stream.AsWriter(),
                    }.Schedule(viewers.Length, ConnectionsPerBatch).Complete();
                    new RelevancyMergeJob { In = stream.AsReader(), Connections = viewers.Length, Pairs = pairs, Became = became, MostForOne = most }.Run();
                }
                return most.Value;
            }
            finally
            {
                most.Dispose();
                stream.Dispose();
                owned.Dispose();
                grid.Dispose();
            }
        }

        /// <summary>
        /// Makes <paramref name="set"/> hold exactly <paramref name="pairs"/>, in Burst. The set's room only ever grows, by doubling, never
        /// per fill.
        /// </summary>
        public static void Write(NativeParallelHashMap<RelevantGhostForConnection, int> set, NativeList<RelevantGhostForConnection> pairs) =>
            new RelevancyWriteJob { Set = set, Pairs = pairs.AsArray() }.Run();
    }

    /// <summary>The ghosts with an owner (players): id, owner, place; and their part of the signature.</summary>
    [BurstCompile]
    [WithAll(typeof(GhostInstance))]
    partial struct GatherOwnedGhostsJob : IJobEntity
    {
        public NativeList<RelevancyGhost> Out;
        public NativeReference<uint> Signature;

        void Execute(in GhostInstance ghost, in GhostOwner owner, in LocalTransform at)
        {
            if (ghost.ghostId == 0) return;   // no id yet: the send system gives it one this tick, the next fill takes it up
            Out.Add(new RelevancyGhost { GhostId = ghost.ghostId, Owner = owner.NetworkId, At = RelevancyRules.Flat(at.Position) });
            Signature.Value += RelevancyFillSystem.Mark((uint)ghost.ghostId, ghost.spawnTick.SerializedData);
        }
    }

    /// <summary>The ghosts with no owner (Hostiles, Strike Dummies, any later object).</summary>
    [BurstCompile]
    [WithAll(typeof(GhostInstance))]
    [WithNone(typeof(GhostOwner))]
    partial struct GatherObjectGhostsJob : IJobEntity
    {
        public NativeList<RelevancyGhost> Out;
        public NativeReference<uint> Signature;

        void Execute(in GhostInstance ghost, in LocalTransform at)
        {
            if (ghost.ghostId == 0) return;
            Out.Add(new RelevancyGhost { GhostId = ghost.ghostId, Owner = 0, At = RelevancyRules.Flat(at.Position) });
            Signature.Value += RelevancyFillSystem.Mark((uint)ghost.ghostId, ghost.spawnTick.SerializedData);
        }
    }

    /// <summary>
    /// Server, only while <see cref="RelevancyConfig"/> exists: the ONE writer of the package's relevancy set (mode SetIsRelevant).
    /// Last in the Simulation group, after the end-of-simulation command buffer (so a ghost destroyed this tick is already gone) and
    /// right before <c>GhostSendSystem</c> reads the set. Every tick it gathers the ghosts and connections; it fills when
    /// <see cref="RelevancyConfig.FillIntervalTicks"/> have passed, or AT ONCE when a ghost, a connection or an always-relevant pair
    /// appeared or went (so a new body is not late and a stale pair never meets a re-used ghost id). A ghost with no place
    /// (no <c>LocalTransform</c>) is world state and goes to everyone, through the package's default query.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderLast = true)]
    [UpdateAfter(typeof(EndSimulationEntityCommandBufferSystem))]
    [UpdateBefore(typeof(GhostSendSystem))]
    public partial struct RelevancyFillSystem : ISystem
    {
        EntityQuery m_NoBecame, m_NoAlways, m_WorldState;
        uint m_Signature;
        int m_SinceFill;
        bool m_Filled, m_ModeSet;

        // the performance log's "relevancy" line, once a second while it records
        long m_WindowStart;
        int m_Fills, m_Pairs, m_Most;
        double m_SumMs, m_MaxMs;

        /// <summary>One ghost's or connection's part of the signature (order does not matter: the parts are summed).</summary>
        public static uint Mark(uint a, uint b) => math.hash(new uint2(a, b)) | 1u;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<RelevancyConfig>();
            state.RequireForUpdate<GhostRelevancy>();
            m_NoBecame = SystemAPI.QueryBuilder().WithAll<NetworkId, NetworkStreamInGame>().WithNone<BecameRelevantGhost>().Build();
            m_NoAlways = SystemAPI.QueryBuilder().WithAll<NetworkId, NetworkStreamInGame>().WithNone<AlwaysRelevantGhost>().Build();
            m_WorldState = SystemAPI.QueryBuilder().WithAll<GhostInstance>().WithNone<LocalTransform>().Build();
        }

        public void OnUpdate(ref SystemState state)
        {
            RelevancyConfig config = SystemAPI.GetSingleton<RelevancyConfig>();
            if (!m_ModeSet)
            {
                RefRW<GhostRelevancy> first = SystemAPI.GetSingletonRW<GhostRelevancy>();
                first.ValueRW.GhostRelevancyMode = GhostRelevancyMode.SetIsRelevant;
                first.ValueRW.DefaultRelevancyQuery = m_WorldState;
                m_ModeSet = true;
            }

            // every in-game connection carries the two lists
            if (!m_NoBecame.IsEmpty) state.EntityManager.AddComponent<BecameRelevantGhost>(m_NoBecame);
            if (!m_NoAlways.IsEmpty) state.EntityManager.AddComponent<AlwaysRelevantGhost>(m_NoAlways);

            // the last fill's "became relevant" entries have been read for one tick: gone
            foreach (DynamicBuffer<BecameRelevantGhost> became in SystemAPI.Query<DynamicBuffer<BecameRelevantGhost>>())
                if (became.Length > 0) became.Clear();

            // gather
            // The jobs this system depends on (whoever still writes LocalTransform, GhostOwner or GhostInstance this tick: the
            // teleport job, the brains' movers) are finished first. IJobEntity.Run() does NOT do that: the code the package
            // generates for Run only hands the job its type handles and runs it with an empty dependency, so without this line the
            // safety system refuses the gather every tick ("the previously scheduled job TeleportJob writes to LocalTransform")
            // and the set is never filled. This completes this system's own input dependency, nothing else.
            state.CompleteDependency();
            var ghosts = new NativeList<RelevancyGhost>(256, state.WorldUpdateAllocator);
            var signature = new NativeReference<uint>(state.WorldUpdateAllocator);
            new GatherOwnedGhostsJob { Out = ghosts, Signature = signature }.Run();
            new GatherObjectGhostsJob { Out = ghosts, Signature = signature }.Run();

            var viewers = new NativeList<int>(64, state.WorldUpdateAllocator);
            var connectionOf = new NativeHashMap<int, Entity>(64, state.WorldUpdateAllocator);
            var always = new NativeList<RelevancyAlways>(16, state.WorldUpdateAllocator);
            uint sum = signature.Value + Mark(config.Nudge, 0x4B4E5633u);
            foreach (var (id, list, connection) in SystemAPI.Query<RefRO<NetworkId>, DynamicBuffer<AlwaysRelevantGhost>>().WithAll<NetworkStreamInGame>().WithEntityAccess())
            {
                int networkId = id.ValueRO.Value;
                viewers.Add(networkId);
                connectionOf.TryAdd(networkId, connection);
                sum += Mark((uint)networkId, 0x4B4E5631u);
                for (int i = 0; i < list.Length; i++)
                {
                    always.Add(new RelevancyAlways { NetworkId = networkId, GhostId = list[i].GhostId });
                    sum += Mark((uint)networkId, (uint)list[i].GhostId ^ 0x4B4E5632u);
                }
            }

            m_SinceFill++;
            if (m_Filled && sum == m_Signature && m_SinceFill < config.FillIntervalTicks) return;

            // fill
            // timed for the performance log's line and for the server CSV's relevancy_ms column (a batch server records no
            // performance log, so the gauge is the only place its cost shows)
            bool recording = KryticalNova.Diagnostics.PerformanceLog.IsRecording;
            bool timed = recording || KryticalNova.Diagnostics.PerformanceLog.GaugesOn;
            long start = timed ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;

            NativeParallelHashMap<RelevantGhostForConnection, int> set = SystemAPI.GetSingletonRW<GhostRelevancy>().ValueRO.GhostRelevancySet;
            var pairs = new NativeList<RelevantGhostForConnection>(math.max(64, set.Count()), state.WorldUpdateAllocator);
            var becameNow = new NativeList<RelevantGhostForConnection>(64, state.WorldUpdateAllocator);
            int most = RelevancyFill.Build(ghosts.AsArray(), viewers.AsArray(), always.AsArray(), set, config, pairs, becameNow);
            RelevancyFill.Write(set, pairs);
            for (int i = 0; i < becameNow.Length; i++)
                if (connectionOf.TryGetValue(becameNow[i].Connection, out Entity connection))
                    state.EntityManager.GetBuffer<BecameRelevantGhost>(connection).Add(new BecameRelevantGhost { GhostId = becameNow[i].Ghost });

            m_Signature = sum;
            m_SinceFill = 0;
            m_Filled = true;

            if (!timed) return;
            long end = System.Diagnostics.Stopwatch.GetTimestamp();
            long frequency = System.Diagnostics.Stopwatch.Frequency;
            double ms = (end - start) * 1000.0 / frequency;
            if (m_Fills == 0) m_WindowStart = start;
            m_Fills++;
            m_SumMs += ms;
            if (ms > m_MaxMs) m_MaxMs = ms;
            m_Pairs = pairs.Length;
            if (most > m_Most) m_Most = most;
            if (end - m_WindowStart < frequency) return;
            KryticalNova.Diagnostics.PerformanceLog.SetGauge(KryticalNova.Diagnostics.PerformanceLog.GaugeNames.RelevancyMs, m_MaxMs);   // the longest fill of the last second
            if (recording)
                KryticalNova.Diagnostics.PerformanceLog.Mark("relevancy",
                    $"{m_Fills} fills, mean {m_SumMs / m_Fills:F3} ms, longest {m_MaxMs:F3} ms, {m_Pairs} pairs for {viewers.Length} connections, most {m_Most} for one, {ghosts.Length} ghosts",
                    (long)(m_MaxMs * 1000.0));
            m_Fills = 0;
            m_SumMs = 0;
            m_MaxMs = 0;
            m_Most = 0;
        }
    }
}
