using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;

namespace KryticalNova.Player.Entities
{
    /// <summary>
    /// Server only, before <see cref="PlayerServerPlanSystem"/>. A jump click (<see cref="PlayerInput.Jump"/>) is planned here with
    /// the installed <see cref="IJumpPlanner"/> and written as <see cref="MotionPlan"/> starting on this tick, or refused like a
    /// walk click (<see cref="MoveState.RefusedSeq"/>, so the owner drops its own plan). NGO's rules: no jump while one is in the air
    /// ("Finish this jump before choosing another destination."), none during the landing cooldown, the Flux checked now and taken
    /// at take-off (<see cref="PlayerJumpPaySystem"/>).
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(PredictedSimulationSystemGroup))]
    [UpdateAfter(typeof(PlayerTeleportSystem))]
    [UpdateBefore(typeof(PlayerServerPlanSystem))]
    public partial class PlayerJumpServerSystem : SystemBase
    {
        EntityQuery m_Query;
        bool m_WarnedPlanner;

        /// <summary>Set by the layer that knows the body; no silent default (a jump without a planner is refused with one error).</summary>
        public IJumpPlanner Planner { get; set; }

        /// <summary>The jump's Flux; null = free (tests, a world without the game's skills).</summary>
        public IJumpCost Cost { get; set; }

        /// <summary>Wallstride's catch (part 4): a jump click at a wall the graph does not cover; null = every jump click is the ordinary jump.</summary>
        public IWallPlanner WallPlanner { get; set; }

        /// <summary>Jumps planned as catches (tests, the scripted run).</summary>
        public int Catches { get; private set; }

        /// <summary>Why the last jump was refused (tests and the scripted run; the message to the player comes with M8's chat port).</summary>
        public string LastRefusal { get; private set; }

        public int Planned { get; private set; }
        public int Refused { get; private set; }
        /// <summary>How many ticks before its own tick the server started the last jump (0 = the click came in time; tests and the scripted run).</summary>
        public int LastStartTicksBack { get; private set; }

        protected override void OnCreate()
        {
            RequireForUpdate<NetworkTime>();
            m_Query = GetEntityQuery(new EntityQueryBuilder(Allocator.Temp)
                .WithAll<PlayerGhostTag, PlayerInput, PlayerPose, MoveState, MotionPlan, Simulate>());
            RequireForUpdate(m_Query);
        }

        protected override void OnUpdate()
        {
            var nt = SystemAPI.GetSingleton<NetworkTime>();
            if (!nt.ServerTick.IsValid) return;
            var curves = SystemAPI.TryGetSingleton<PlayerMotionCurves>(out var c) ? c : PlayerMotionCurves.Default;
            int tickMicros = PlayerMovement.TickMicros(SystemAPI.TryGetSingleton<ClientServerTickRate>(out var rate) && rate.SimulationTickRate > 0
                ? rate.SimulationTickRate : 30);
            var em = EntityManager;
            using var entities = m_Query.ToEntityArray(Allocator.Temp);
            foreach (var e in entities)
            {
                var input = em.GetComponentData<PlayerInput>(e);
                if (!input.Click.IsSet || input.Jump == 0 || input.ClickSeq == 0) continue;
                var plan = em.GetComponentData<MotionPlan>(e);
                if (plan.PlanSeq == input.ClickSeq && plan.Kind != MotionKind.None) continue;   // already taken
                var pose = em.GetComponentData<PlayerPose>(e);
                var goal = new int3(input.GoalXMm, input.GoalYMm, input.GoalZMm);
                string why = null;
                int3 landing = default;
                int apexY = 0;
                WallAnswer caught = WallAnswer.NotWall;
                WallPlan wall = default;
                int catchMicros = 0;
                using var corners = new NativeList<int3>(PlayerLimits.MaxWallCorners, Allocator.Temp);
                using var legs = new NativeList<WallCorner>(PlayerLimits.MaxWallCorners, Allocator.Temp);
                if (JumpFlight.Active(plan, curves, nt.ServerTick, tickMicros)) why = "Finish this jump before choosing another destination.";
                else if (JumpFlight.CoolingDown(plan, curves, nt.ServerTick, tickMicros)) why = "Let the landing settle before jumping again.";
                // NGO ignores a click while the body is over an obstacle (no message: it is back on the ground in a moment; M3c)
                else if (PlayerServerPlanSystem.InVault(em, e)) why = "over an obstacle";
                else if (Planner == null)
                {
                    if (!m_WarnedPlanner)
                    {
                        m_WarnedPlanner = true;
                        Debug.LogError("PlayerJumpServerSystem: no IJumpPlanner installed; jumps are refused.");
                    }
                    why = "Jump is not set up on this server.";
                }
                else
                {
                    // a jump at a wall the graph does not cover is Wallstride's catch (part 4; NGO judges the wall before the jump's Flux)
                    if ((input.Wall & WallClick.Wall) != 0 && WallPlanner != null && em.HasComponent<WallPlan>(e))
                        caught = WallPlanner.Catch(e, pose.Mm, pose.YawCdeg, goal, new int3(input.NormalX, input.NormalY, input.NormalZ), input.Run != 0,
                            corners, legs, out wall, out landing, out apexY, out catchMicros, out why);
                    if (caught == WallAnswer.Refused) { }
                    else if (Cost != null && !Cost.CanPay(e, out why)) { }
                    else if (caught == WallAnswer.NotWall && !Planner.TryPlan(e, pose.Mm, goal, out landing, out apexY, out why)) { }
                    else if (caught == WallAnswer.Planned && corners.Length < 2) why = "There is no way to the foot of that wall.";
                }
                if (why != null)
                {
                    LastRefusal = why;
                    Refused++;
                    var move = em.GetComponentData<MoveState>(e);
                    move.RefusedSeq = input.ClickSeq;
                    em.SetComponentData(e, move);
                    if (PlayerClickLog.Sink != null)
                        PlayerClickLog.Refused(em.HasComponent<GhostOwner>(e) ? em.GetComponentData<GhostOwner>(e).NetworkId : 0,
                            input.ClickSeq, nt.ServerTick.TickIndexForValidTick, "jump: " + why);
                    continue;
                }
                // on the owner's press tick when the click came late (clamped: the window, the previous jump, a server order)
                var claimed = new NetworkTick { SerializedData = input.ClickTick };
                NetworkTick orderStart = em.HasComponent<ServerOrder>(e) ? em.GetComponentData<ServerOrder>(e).StartTick : default;
                NetworkTick start = JumpFlight.ClickStartTick(nt.ServerTick, claimed, in plan, orderStart, in curves, tickMicros);
                LastStartTicksBack = nt.ServerTick.TicksSince(start);
                MotionPlan jump = JumpFlight.NewJump(input.ClickSeq, start, pose.Mm, landing, apexY);
                if (caught == WallAnswer.Planned)
                {
                    // the climb that runs on where the jump meets the wall, under the jump's own number
                    jump.Flags |= MotionFlags.Catch;
                    jump.CatchMicros = catchMicros;
                    WallWrite.Server(em, e, input.ClickSeq, corners, legs, wall);
                    Catches++;
                }
                em.SetComponentData(e, jump);
                Planned++;
                if (PlayerClickLog.Sink != null)
                    PlayerClickLog.Server(em.HasComponent<GhostOwner>(e) ? em.GetComponentData<GhostOwner>(e).NetworkId : 0,
                        input.ClickSeq, nt.ServerTick.TickIndexForValidTick);
            }
        }
    }

    /// <summary>
    /// The jump, identical on the server and the owner (full ticks, after the walk step): while the plan places the body
    /// (<see cref="JumpFlight.Places"/>, from the press until a newer walk or order) the pose is <see cref="JumpFlight.FeetAt"/> of the
    /// plan at THIS tick, facing the landing, and the walk is dropped; so a skipped tick never leaves the body short. The owner uses its
    /// own <see cref="ClientMotionPlan"/> until the server's plan with the same number arrives (a refused one is dropped). Nothing is
    /// cleared at the landing, so a rollback can never restart a jump.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(PredictedSimulationSystemGroup))]
    [UpdateAfter(typeof(PredictedMoveSystem))]
    public partial struct PlayerMotionSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<NetworkTime>();
            state.RequireForUpdate<PlayerGhostTag>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var nt = SystemAPI.GetSingleton<NetworkTime>();
            if (!nt.ServerTick.IsValid || nt.IsPartialTick) return;
            var curves = SystemAPI.TryGetSingleton<PlayerMotionCurves>(out var c) ? c : PlayerMotionCurves.Default;
            int tickMicros = PlayerMovement.TickMicros(SystemAPI.TryGetSingleton<ClientServerTickRate>(out var rate) && rate.SimulationTickRate > 0
                ? rate.SimulationTickRate : 30);
            var clientPlans = SystemAPI.GetComponentLookup<ClientMotionPlan>(true);
            var orders = SystemAPI.GetComponentLookup<ServerOrder>(true);
            foreach (var (plan, move, pose, transform, entity) in SystemAPI
                         .Query<RefRO<MotionPlan>, RefRW<MoveState>, RefRW<PlayerPose>, RefRW<LocalTransform>>()
                         .WithAll<PlayerGhostTag, Simulate>().WithEntityAccess())
            {
                MotionPlan use = plan.ValueRO;
                if (clientPlans.HasComponent(entity))
                    use = Choose(plan.ValueRO, clientPlans[entity].Plan, move.ValueRO.RefusedSeq);
                // a catch whose climb has begun (or is over): the climb moves the body now (Wallstride part 4)
                if (JumpFlight.IsCatch(in use) && move.ValueRO.OrderSeq == use.PlanSeq) continue;
                NetworkTick orderStart = orders.HasComponent(entity) ? orders[entity].StartTick : default;
                if (!JumpFlight.Places(use, curves, nt.ServerTick, tickMicros, JumpFlight.ClickWalkSeq(move.ValueRO), move.ValueRO.RefusedSeq, orderStart)) continue;
                long elapsed = JumpFlight.ElapsedMicros(use, nt.ServerTick, tickMicros);
                int3 feet = JumpFlight.FeetAt(use, curves, elapsed);
                ref var p = ref pose.ValueRW;
                p.XMm = feet.x; p.YMm = feet.y; p.ZMm = feet.z;
                p.YawCdeg = use.YawCdeg;
                if (JumpFlight.CatchHandOff(in use, in curves, elapsed, in move.ValueRO))
                {
                    // the jump met the wall: no landing, the climb of its number runs on from the catch point (NGO CatchSurface)
                    int3 caught = JumpFlight.CatchFeet(in use, in curves);
                    p.XMm = caught.x; p.YMm = caught.y; p.ZMm = caught.z;
                    PlayerMovement.BeginCatchClimb(ref move.ValueRW, caught, use.PlanSeq);
                    move.ValueRW.Water = 0;
                    transform.ValueRW.Position = new float3(p.XMm, p.YMm, p.ZMm) * 0.001f;
                    transform.ValueRW.Rotation = quaternion.RotateY(math.radians(p.YawCdeg * 0.01f));
                    continue;
                }
                PlayerMovement.CancelWalk(ref move.ValueRW);
                // in the air the body is off the water (NGO: ServerLeaveForJump); after touchdown the server's landing decides (a stand sets it)
                if (!JumpFlight.Landed(use, curves, elapsed)) move.ValueRW.Water = 0;
                transform.ValueRW.Position = new float3(p.XMm, p.YMm, p.ZMm) * 0.001f;
                transform.ValueRW.Rotation = quaternion.RotateY(math.radians(p.YawCdeg * 0.01f));
            }
        }

        /// <summary>
        /// The plan the owner flies: its own for a click the server has not answered yet (newer than the server's plan and not
        /// refused), else the server's.
        /// </summary>
        public static MotionPlan Choose(in MotionPlan server, in MotionPlan client, ushort refusedSeq)
        {
            if (client.Kind == MotionKind.None || client.PlanSeq == 0) return server;
            if (client.PlanSeq == refusedSeq) return server;
            if (client.PlanSeq == server.PlanSeq) return server;
            if (server.Kind != MotionKind.None && server.StartTick.IsValid && client.StartTick.IsValid
                && !client.StartTick.IsNewerThan(server.StartTick)) return server;
            return client;
        }
    }

    /// <summary>
    /// Server only, after the flight: the jump's Flux is taken on the take-off tick (owner answer Q2 a). If the player can no
    /// longer pay then, the jump is called off before it leaves the ground: free, back on the start, refused for the owner.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(PredictedSimulationSystemGroup))]
    [UpdateAfter(typeof(PlayerMotionSystem))]
    public partial class PlayerJumpPaySystem : SystemBase
    {
        public IJumpCost Cost { get; set; }

        /// <summary>Jumps paid and jumps called off at take-off (tests).</summary>
        public int Paid { get; private set; }
        public int Aborted { get; private set; }

        protected override void OnCreate() => RequireForUpdate<NetworkTime>();

        protected override void OnUpdate()
        {
            var nt = SystemAPI.GetSingleton<NetworkTime>();
            if (!nt.ServerTick.IsValid) return;
            var curves = SystemAPI.TryGetSingleton<PlayerMotionCurves>(out var c) ? c : PlayerMotionCurves.Default;
            int tickMicros = PlayerMovement.TickMicros(SystemAPI.TryGetSingleton<ClientServerTickRate>(out var rate) && rate.SimulationTickRate > 0
                ? rate.SimulationTickRate : 30);
            foreach (var (plan, move, pose, transform, entity) in SystemAPI
                         .Query<RefRW<MotionPlan>, RefRW<MoveState>, RefRW<PlayerPose>, RefRW<LocalTransform>>()
                         .WithAll<PlayerGhostTag, Simulate>().WithEntityAccess())
            {
                ref var p = ref plan.ValueRW;
                if (p.Kind != MotionKind.Jump || (p.Flags & MotionFlags.Paid) != 0) continue;
                long elapsed = JumpFlight.ElapsedMicros(p, nt.ServerTick, tickMicros);
                if (!JumpFlight.TakenOff(curves, elapsed)) continue;
                if (JumpFlight.Landed(p, curves, elapsed))
                {
                    // already down (ticks skipped past the whole flight): take the Flux if it can, never call a landed jump off
                    if (Cost == null || Cost.CanPay(entity, out _)) { Cost?.Pay(entity); Paid++; }
                    p.Flags |= MotionFlags.Paid;
                    continue;
                }
                if (Cost == null || Cost.CanPay(entity, out _))
                {
                    Cost?.Pay(entity);
                    p.Flags |= MotionFlags.Paid;
                    Paid++;
                    continue;
                }
                // called off at take-off: back on the start, the plan gone, the click refused for the owner
                pose.ValueRW.XMm = p.FromXMm; pose.ValueRW.YMm = p.FromYMm; pose.ValueRW.ZMm = p.FromZMm;
                transform.ValueRW.Position = new float3(p.FromXMm, p.FromYMm, p.FromZMm) * 0.001f;
                move.ValueRW.RefusedSeq = p.PlanSeq;
                p.Kind = MotionKind.None;
                Aborted++;
            }
        }
    }
}
