using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;

namespace KryticalNova.Player.Entities
{
    /// <summary><see cref="IWaterPlanner.Plan"/>'s answer.</summary>
    public enum WaterAnswer : byte
    {
        /// <summary>Not Waterstride's click: the ordinary walk plans it.</summary>
        NotWater,
        /// <summary>Planned: the corners and the leg are filled.</summary>
        Planned,
        /// <summary>Waterstride's click, refused (the reason in NGO's words).</summary>
        Refused,
    }

    /// <summary>What a jump's landing on water comes to (NGO's <c>ServerLandedOnWater</c>).</summary>
    public enum WaterLandingAnswer : byte
    {
        /// <summary>Not water: the jump landed on ground.</summary>
        NotWater,
        /// <summary>Waterstride open and Flux for a second: the player stands on the water.</summary>
        Stands,
        /// <summary>Fell in: a share of HP lost (taken by the planner's body), out on the shore point given.</summary>
        FallsIn,
    }

    /// <summary>
    /// Plans Waterstride where the world is known (the zone's water map, the NavMesh shore, the body's level, ranks, Flux and moves; NGO's
    /// rules in <c>WaterstrideRules</c>): filled by the layer that knows the body, on the server and, for the owner's prediction, on the
    /// client. Managed, main thread. Corners are the pose's millimetres (the body's root), as the walk's.
    /// </summary>
    public interface IWaterPlanner
    {
        /// <summary>
        /// A click to <paramref name="goalMm"/> from <paramref name="startMm"/>: a run out over the water, on it, or ashore from it. The first
        /// corner added is the start (as the path planner's). <paramref name="leg"/>'s Seq is the caller's to set.
        /// </summary>
        WaterAnswer Plan(Entity ghost, int3 startMm, int3 goalMm, bool run, bool onWater, bool stranded, NativeList<int3> corners,
            out WaterPlan leg, out string reason);

        /// <summary>Out of Flux: the way back to the nearest shore at the stranded speed. False when there is none within reach.</summary>
        bool PlanStranded(Entity ghost, int3 startMm, NativeList<int3> corners, out WaterPlan leg);

        /// <summary>
        /// Server: a jump came down at <paramref name="landingMm"/> (the pose's millimetres). On water it stands, or falls in: then the HP
        /// is taken here and <paramref name="shoreMm"/> is where the body comes out (the pose's millimetres).
        /// </summary>
        WaterLandingAnswer Landed(Entity ghost, int3 landingMm, out int3 shoreMm);
    }

    /// <summary>The Flux of a body on the water, paid where it is decided (the server's body). Managed.</summary>
    public interface IWaterFlux
    {
        /// <summary>Flux a second on the water, thousandths (NGO's settings: 3 = 3000).</summary>
        int FluxPerSecondMilli { get; }

        /// <summary>Takes <paramref name="points"/> whole Flux; false when the body has not got them (nothing is taken).</summary>
        bool TryPay(Entity ghost, int points);
    }

    /// <summary>
    /// Server only, after the walk and the jump of the tick (the server never re-simulates, so nothing is paid twice): every tick a body
    /// is on the water (<see cref="MoveState.Water"/>, running or standing) adds its share of the Flux a second to what it owes, paid a
    /// whole point at a time (NGO's <c>PayForTime</c>). When it cannot pay it is stranded: a server order carries it back to the nearest
    /// shore at the stranded speed (NGO's <c>Strand</c>), and on the way nothing more is owed.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(PredictedSimulationSystemGroup))]
    [UpdateAfter(typeof(PlayerMotionSystem))]
    public partial class WaterstrideFluxSystem : SystemBase
    {
        EntityQuery m_Query;

        /// <summary>Set by <see cref="PlayerEntitiesSetup.Install"/>; null = the water is free (tests, a world without the game's resources).</summary>
        public IWaterFlux Flux { get; set; }

        /// <summary>Plans the way back when the Flux runs out; null = the run is called off where it is.</summary>
        public IWaterPlanner Planner { get; set; }

        /// <summary>Whole Flux points paid, and bodies stranded (tests, the scripted run).</summary>
        public int Paid { get; private set; }
        public int Strandings { get; private set; }

        protected override void OnCreate()
        {
            RequireForUpdate<NetworkTime>();
            m_Query = GetEntityQuery(new EntityQueryBuilder(Allocator.Temp)
                .WithAll<PlayerGhostTag, MoveState, PlayerPose, WaterPlan, WaterFluxOwed, Simulate>());
            RequireForUpdate(m_Query);
        }

        protected override void OnUpdate()
        {
            var nt = SystemAPI.GetSingleton<NetworkTime>();
            if (!nt.ServerTick.IsValid || nt.IsPartialTick) return;
            int tickMicros = PlayerMovement.TickMicros(SystemAPI.TryGetSingleton<ClientServerTickRate>(out var rate) && rate.SimulationTickRate > 0
                ? rate.SimulationTickRate : 30);
            var em = EntityManager;
            using var entities = m_Query.ToEntityArray(Allocator.Temp);
            foreach (var e in entities)
            {
                var move = em.GetComponentData<MoveState>(e);
                var owed = em.GetComponentData<WaterFluxOwed>(e);
                if (move.Water == 0 || Flux == null || Flux.FluxPerSecondMilli <= 0)
                {
                    // counted from the first moment on the water (NGO resets it as the run starts)
                    if (owed.Micro != 0) em.SetComponentData(e, new WaterFluxOwed());
                    continue;
                }
                var water = em.GetComponentData<WaterPlan>(e);
                if ((move.Flags & MoveFlags.Moving) != 0 && WaterLeg.Stranded(water, move.OrderSeq)) continue;   // carried back for nothing more

                owed.Micro += WaterLeg.OwedMicro(Flux.FluxPerSecondMilli, tickMicros);
                if (owed.Micro >= 1000000L)
                {
                    int whole = (int)(owed.Micro / 1000000L);
                    if (Flux.TryPay(e, whole)) { owed.Micro -= whole * 1000000L; Paid += whole; }
                    else
                    {
                        em.SetComponentData(e, new WaterFluxOwed());
                        Strand(em, e, nt.ServerTick, ref move);
                        continue;
                    }
                }
                em.SetComponentData(e, owed);
            }
        }

        /// <summary>Out of Flux: a server order back to the shore (or, with no shore in reach, off the water where it is: NGO's Cancel).</summary>
        void Strand(EntityManager em, Entity e, NetworkTick tick, ref MoveState move)
        {
            Strandings++;
            if (PlayerClickLog.Sink != null) PlayerClickLog.Sink("[KnWater] out of Flux: back to the shore (stranding " + Strandings + ")");
            var pose = em.GetComponentData<PlayerPose>(e);
            var corners = new NativeList<int3>(PlayerLimits.MaxCorners, Allocator.Temp);
            try
            {
                if (Planner == null || !Planner.PlanStranded(e, pose.Mm, corners, out WaterPlan leg) || corners.Length < 2)
                {
                    move.Water = 0;
                    em.SetComponentData(e, move);
                    return;
                }
                var order = em.GetComponentData<ServerOrder>(e);
                ushort seq = PlayerServerPlanSystem.NextServerSeq(order.Seq);
                int count = math.min(corners.Length, PlayerLimits.MaxCorners);
                var buffer = em.GetBuffer<PathCorner>(e);
                buffer.Clear();
                for (int i = 0; i < count; i++) buffer.Add(new PathCorner { XMm = corners[i].x, YMm = corners[i].y, ZMm = corners[i].z });
                em.SetComponentData(e, new PathPlan { Seq = seq, Count = (byte)count });
                leg.Seq = seq;
                leg.Flags |= WaterFlags.Stranded;
                em.SetComponentData(e, leg);
                int3 goal = corners[count - 1];
                order.Seq = seq;
                order.StartTick = tick;
                order.GoalXMm = goal.x; order.GoalYMm = goal.y; order.GoalZMm = goal.z;
                order.Flags = (byte)(corners.Length > PlayerLimits.MaxCorners ? OrderFlags.Truncated : 0);
                em.SetComponentData(e, order);
            }
            finally { corners.Dispose(); }
        }
    }
    /// <summary>
    /// Server only, after the jump of the tick: a jump that has come down on the water is decided once (NGO's <c>ServerLandedOnWater</c>):
    /// with Waterstride open and the Flux for a second the player stands on it (<see cref="MoveState.Water"/>, the Flux system pays from
    /// there); otherwise they fall in, lose a share of HP and are put on the nearest shore (a teleport). The plan keeps the decision
    /// (<see cref="MotionFlags.WaterDecided"/>), so it is never made twice.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(PredictedSimulationSystemGroup))]
    [UpdateAfter(typeof(PlayerJumpPaySystem))]
    public partial class WaterstrideLandingSystem : SystemBase
    {
        EntityQuery m_Query;

        /// <summary>Set by <see cref="PlayerEntitiesSetup.Install"/>; null = every landing is ground (the jump as before).</summary>
        public IWaterPlanner Planner { get; set; }

        /// <summary>Landings on water that stood, and that fell in (tests, the scripted run).</summary>
        public int Stood { get; private set; }
        public int FellIn { get; private set; }

        protected override void OnCreate()
        {
            RequireForUpdate<NetworkTime>();
            m_Query = GetEntityQuery(new EntityQueryBuilder(Allocator.Temp).WithAll<PlayerGhostTag, MotionPlan, MoveState, Simulate>());
            RequireForUpdate(m_Query);
        }

        protected override void OnUpdate()
        {
            if (Planner == null) return;
            var nt = SystemAPI.GetSingleton<NetworkTime>();
            if (!nt.ServerTick.IsValid || nt.IsPartialTick) return;
            var curves = SystemAPI.TryGetSingleton<PlayerMotionCurves>(out var c) ? c : PlayerMotionCurves.Default;
            int tickMicros = PlayerMovement.TickMicros(SystemAPI.TryGetSingleton<ClientServerTickRate>(out var rate) && rate.SimulationTickRate > 0
                ? rate.SimulationTickRate : 30);
            var em = EntityManager;
            using var entities = m_Query.ToEntityArray(Allocator.Temp);
            foreach (var e in entities)
            {
                var plan = em.GetComponentData<MotionPlan>(e);
                if (plan.Kind != MotionKind.Jump || (plan.Flags & MotionFlags.WaterDecided) != 0) continue;
                if (!JumpFlight.Landed(plan, curves, JumpFlight.ElapsedMicros(plan, nt.ServerTick, tickMicros))) continue;
                plan.Flags |= MotionFlags.WaterDecided;
                em.SetComponentData(e, plan);
                var answer = Planner.Landed(e, plan.ToMm, out int3 shore);
                if (answer == WaterLandingAnswer.Stands)
                {
                    var move = em.GetComponentData<MoveState>(e);
                    move.Water = 1;
                    em.SetComponentData(e, move);
                    Stood++;
                }
                else if (answer == WaterLandingAnswer.FallsIn)
                {
                    FellIn++;
                    if (PlayerClickLog.Sink != null) PlayerClickLog.Sink("[KnWater] fell in the water; out on the shore " + shore);
                    if (em.HasComponent<TeleportRequest>(e))
                    {
                        var pose = em.HasComponent<PlayerPose>(e) ? em.GetComponentData<PlayerPose>(e) : default;
                        em.SetComponentData(e, new TeleportRequest { XMm = shore.x, YMm = shore.y, ZMm = shore.z, YawCdeg = pose.YawCdeg });
                        em.SetComponentEnabled<TeleportRequest>(e, true);
                    }
                }
            }
        }
    }
}
