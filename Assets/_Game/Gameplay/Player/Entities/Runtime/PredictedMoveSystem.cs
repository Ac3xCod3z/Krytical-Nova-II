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
        ComponentLookup<PlayerVitals> m_Vitals;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<NetworkTime>();
            state.RequireForUpdate<PlayerGhostTag>();
            m_Traces = state.GetBufferLookup<PlayerMoveTrace>();
            m_Vitals = state.GetComponentLookup<PlayerVitals>(true);
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
            m_Vitals.Update(ref state);
            new MoveJob
            {
                TickMicros = PlayerMovement.TickMicros(simRate),
                Tick = nt.ServerTick.TickIndexForValidTick,
                First = nt.IsFirstTimeFullyPredictingTick,
                Cfg = cfg,
                Traces = m_Traces,
                Vitals = m_Vitals,
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
            [ReadOnly] public ComponentLookup<PlayerVitals> Vitals;

            void Execute(Entity entity,
                ref MoveState move, ref PlayerPose pose, ref PoseTilt tilt, ref LocalTransform transform, ref PlayerMoveDiag diag,
                in PlayerInput input, in ServerOrder order, in PathPlan plan, in ClientPlanHeader client, in MoveStats stats,
                in VaultPlan vault, in ClientVaultPlan clientVault, in WaterPlan water, in ClientWaterPlan clientWater,
                in WallPlan wall, in ClientWallPlan clientWall,
                in DynamicBuffer<PathCorner> serverBuffer, in DynamicBuffer<ClientPlanCorner> clientBuffer,
                in DynamicBuffer<WallCorner> serverLegs, in DynamicBuffer<ClientWallCorner> clientLegs)
            {
                ushort orderBefore = move.OrderSeq;
                var serverCorners = serverBuffer.Reinterpret<int3>().AsNativeArray();
                var clientCorners = clientBuffer.Reinterpret<int3>().AsNativeArray();

                // a jump click never starts a walk (PlayerMotionSystem flies it after this step)
                PlayerInput walkInput = input;
                if (walkInput.Jump != 0) walkInput.Click = default;
                // a dead player walks and jumps no more until it is up (NGO: its agent is off, PointAndClickPlayer leads no walk)
                if (Vitals.HasComponent(entity))
                {
                    PlayerVitals v = Vitals[entity];
                    if (v.MaxHealthMilli > 0 && v.HealthMilli <= 0) { walkInput.Click = default; walkInput.Jump = 0; }
                }
                PlayerMovement.Step(ref move, ref pose, ref tilt, in walkInput, in order, in plan, serverCorners, in client, clientCorners,
                    in vault, in clientVault.Plan, in water, in clientWater.Plan,
                    in wall, serverLegs.AsNativeArray(), in clientWall.Plan, clientLegs.Reinterpret<WallCorner>().AsNativeArray(),
                    in stats, in Cfg, TickMicros);

                transform.Position = new float3(pose.XMm, pose.YMm, pose.ZMm) * 0.001f;
                transform.Rotation = WallLeg.Rotation(in pose, in tilt);

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
    /// A jump in the air is called off too, else its flight would put the body back on the arc until touchdown.
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
            m_Plans = state.GetComponentLookup<MotionPlan>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            m_Requests.Update(ref state);
            m_Plans.Update(ref state);
            new TeleportJob { Requests = m_Requests, Plans = m_Plans }.Schedule();
        }

        ComponentLookup<TeleportRequest> m_Requests;
        ComponentLookup<MotionPlan> m_Plans;

        [BurstCompile]
        [WithAll(typeof(Simulate), typeof(PlayerGhostTag), typeof(TeleportRequest))]
        partial struct TeleportJob : IJobEntity
        {
            [NativeDisableParallelForRestriction] public ComponentLookup<TeleportRequest> Requests;
            [NativeDisableParallelForRestriction] public ComponentLookup<MotionPlan> Plans;

            void Execute(Entity entity, ref MoveState move, ref PlayerPose pose, ref PoseTilt tilt, ref LocalTransform transform, ref TeleportSeq seq)
            {
                var request = Requests[entity];
                pose.XMm = request.XMm; pose.YMm = request.YMm; pose.ZMm = request.ZMm;
                pose.YawCdeg = ((request.YawCdeg % 36000) + 36000) % 36000;
                PlayerMovement.CancelWalk(ref move);
                move.Water = 0;   // a teleport ends Waterstride where it puts the body (NGO: Cancel)
                move.Wall = 0;    // and the wall walk (NGO: the wall motor's Cancel lands it on the NavMesh)
                tilt = default;
                if (Plans.HasComponent(entity))
                {
                    var plan = Plans[entity];
                    plan.Kind = MotionKind.None;
                    Plans[entity] = plan;
                }
                transform.Position = new float3(pose.XMm, pose.YMm, pose.ZMm) * 0.001f;
                transform.Rotation = quaternion.RotateY(math.radians(pose.YawCdeg * 0.01f));
                seq.Value = (byte)(seq.Value + 1);
                Requests.SetComponentEnabled(entity, false);
            }
        }
    }
}
