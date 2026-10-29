using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;

namespace KryticalNova.Player.Entities
{
    /// <summary>
    /// The player's walk, identical on the server and on the owning client. Runs inside <see cref="PredictedSimulationSystemGroup"/>
    /// (once per tick on the server, again after every snapshot on the owner), on FULL ticks only: a partial tick is skipped
    /// and the presentation interpolates between the last two full poses (plan M3, advisor item 8). All the arithmetic is in
    /// <see cref="PlayerMovement"/> (integers); the only floats here are the LocalTransform outputs.
    /// Observers never run it: their ghost is interpolated.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(PredictedSimulationSystemGroup))]
    [BurstCompile(FloatMode = FloatMode.Deterministic, FloatPrecision = FloatPrecision.High)]
    public partial struct PredictedMoveSystem : ISystem
    {
        BufferLookup<PlayerMoveTrace> m_Traces;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<NetworkTime>();
            state.RequireForUpdate<PlayerGhostTag>();
            m_Traces = state.GetBufferLookup<PlayerMoveTrace>();
        }

        [BurstCompile(FloatMode = FloatMode.Deterministic, FloatPrecision = FloatPrecision.High)]
        public void OnUpdate(ref SystemState state)
        {
            var nt = SystemAPI.GetSingleton<NetworkTime>();
            if (!nt.ServerTick.IsValid || nt.IsPartialTick) return;

            int simRate = 30;
            if (SystemAPI.TryGetSingleton<ClientServerTickRate>(out var rate) && rate.SimulationTickRate > 0)
                simRate = (int)rate.SimulationTickRate;
            var cfg = SystemAPI.TryGetSingleton<PlayerMoveSettings>(out var set) ? set : PlayerMoveSettings.Default;

            m_Traces.Update(ref state);
            new MoveJob
            {
                TickMicros = PlayerMovement.TickMicros(simRate),
                Tick = nt.ServerTick.TickIndexForValidTick,
                First = nt.IsFirstTimeFullyPredictingTick,
                Cfg = cfg,
                Traces = m_Traces,
            }.Schedule();
        }

        [BurstCompile(FloatMode = FloatMode.Deterministic, FloatPrecision = FloatPrecision.High)]
        [WithAll(typeof(Simulate), typeof(PlayerGhostTag))]
        partial struct MoveJob : IJobEntity
        {
            public int TickMicros;
            public uint Tick;
            public bool First;
            public PlayerMoveSettings Cfg;
            [NativeDisableParallelForRestriction] public BufferLookup<PlayerMoveTrace> Traces;

            void Execute(Entity entity,
                ref MoveState move, ref PlayerPose pose, ref LocalTransform transform, ref PlayerMoveDiag diag,
                in PlayerInput input, in ServerOrder order, in PathPlan plan, in ClientPlanHeader client, in MoveStats stats,
                in DynamicBuffer<PathCorner> serverBuffer, in DynamicBuffer<ClientPlanCorner> clientBuffer)
            {
                ushort orderBefore = move.OrderSeq;
                var serverCorners = serverBuffer.Reinterpret<int3>().AsNativeArray();
                var clientCorners = clientBuffer.Reinterpret<int3>().AsNativeArray();

                PlayerMovement.Step(ref move, ref pose, in input, in order, in plan, serverCorners, in client, clientCorners,
                    in stats, in Cfg, TickMicros);

                transform.Position = new float3(pose.XMm, pose.YMm, pose.ZMm) * 0.001f;
                transform.Rotation = quaternion.RotateY(math.radians(pose.YawCdeg * 0.01f));

                diag.Runs++;
                if (First) diag.FirstRuns++;
                if (diag.LastTick == Tick) diag.RunsThisTick++;
                else { diag.LastTick = Tick; diag.RunsThisTick = 1; }
                if (diag.RunsThisTick > diag.MaxRunsOneTick) diag.MaxRunsOneTick = diag.RunsThisTick;
                if (move.OrderSeq != orderBefore) diag.OrderStarts++;

                if (Traces.HasBuffer(entity))
                {
                    var trace = Traces[entity];
                    if (trace.Length > 0)
                    {
                        ref var t = ref trace.ElementAt((int)(Tick % (uint)trace.Length));
                        int runs = t.Tick == Tick ? t.RunsForTick + 1 : 1;
                        t = new PlayerMoveTrace
                        {
                            Tick = Tick,
                            ProgressMm = move.ProgressMm,
                            SpeedMmps = move.SpeedMmps,
                            XMm = pose.XMm, YMm = pose.YMm, ZMm = pose.ZMm,
                            YawCdeg = pose.YawCdeg,
                            Flags = move.Flags,
                            OrderSeq = move.OrderSeq,
                            RunsForTick = runs,
                        };
                    }
                }
            }
        }
    }

    /// <summary>
    /// Server only: applies a <see cref="TeleportRequest"/>. The pose is set, the walk dropped, and <see cref="TeleportSeq"/>
    /// bumped so the presentation body snaps instead of easing. The owner is not asked to predict it: the snapshot carries it.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(PredictedSimulationSystemGroup))]
    [UpdateBefore(typeof(PredictedMoveSystem))]
    [BurstCompile]
    public partial struct PlayerTeleportSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<NetworkTime>();
            m_Requests = state.GetComponentLookup<TeleportRequest>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            m_Requests.Update(ref state);
            new TeleportJob { Requests = m_Requests }.Schedule();
        }

        ComponentLookup<TeleportRequest> m_Requests;

        [BurstCompile]
        [WithAll(typeof(Simulate), typeof(PlayerGhostTag), typeof(TeleportRequest))]
        partial struct TeleportJob : IJobEntity
        {
            [NativeDisableParallelForRestriction] public ComponentLookup<TeleportRequest> Requests;

            void Execute(Entity entity, ref MoveState move, ref PlayerPose pose, ref LocalTransform transform, ref TeleportSeq seq)
            {
                var request = Requests[entity];
                pose.XMm = request.XMm; pose.YMm = request.YMm; pose.ZMm = request.ZMm;
                pose.YawCdeg = ((request.YawCdeg % 36000) + 36000) % 36000;
                PlayerMovement.CancelWalk(ref move);
                transform.Position = new float3(pose.XMm, pose.YMm, pose.ZMm) * 0.001f;
                transform.Rotation = quaternion.RotateY(math.radians(pose.YawCdeg * 0.01f));
                seq.Value = (byte)(seq.Value + 1);
                Requests.SetComponentEnabled(entity, false);
            }
        }
    }
}
