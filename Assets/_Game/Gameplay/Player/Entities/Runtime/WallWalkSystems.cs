using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;

namespace KryticalNova.Player.Entities
{
    /// <summary><see cref="PlayerInput.Wall"/> bits: the click is Wallstride's wall walk (NGO <c>ServerWallClick</c>), with Jump selected.</summary>
    public static class WallClick
    {
        public const byte Wall = 1;
        public const byte Jump = 2;
    }

    /// <summary>What the wall walk made of a click.</summary>
    public enum WallAnswer : byte
    {
        /// <summary>Not the wall's (no wall here and the body is not on one): the ordinary walk takes it.</summary>
        NotWall,
        Planned,
        /// <summary>The wall walk's own refusal (NGO's words in the reason).</summary>
        Refused,
    }

    /// <summary>
    /// Plans Wallstride's wall walk where the world is known (the zone's surface graph, the NavMesh, the colliders): filled by the layer
    /// that knows the body, on the server and (for the owner's prediction) on the client. Corners are the pose's millimetres (the root);
    /// <paramref name="legs"/> holds one <see cref="WallCorner"/> per corner.
    /// </summary>
    public interface IWallPlanner
    {
        WallAnswer Plan(Entity ghost, int3 poseMm, int yawCdeg, in PoseTilt tilt, int3 pointMm, int3 normalPermille, bool run, bool jump,
            bool onWall, NativeList<int3> corners, NativeList<WallCorner> legs, out WallPlan plan, out string reason);

        /// <summary>Server, out of Grip: off the wall in a flip to the ground in front of it (NGO <c>PushOff</c>). False when there is no body.</summary>
        bool PushOff(Entity ghost, int3 poseMm, int yawCdeg, in PoseTilt tilt, NativeList<int3> corners, NativeList<WallCorner> legs, out WallPlan plan);
    }

    /// <summary>The Grip of a body on a wall, paid where it is decided (the server's body). Managed.</summary>
    public interface IWallGrip
    {
        /// <summary>Flux a second on a wall, thousandths (NGO's <c>gripFluxPerSecond</c> 15 = 15000).</summary>
        int GripPerSecondMilli { get; }

        /// <summary>Takes <paramref name="points"/> whole Flux; false when the body has not got them (nothing is taken).</summary>
        bool TryPay(Entity ghost, int points);

        /// <summary>The straight climb's price, whole Flux (NGO <c>fluxCost</c> 12).</summary>
        int ClimbFlux { get; }
    }

    /// <summary>
    /// Server only: the click of a wall walk cut at the corner budget (<see cref="WallFlags.Truncated"/>), so the rest is asked again from
    /// where the cut walk stands.
    /// </summary>
    public struct WallGoal : IComponentData
    {
        public ushort Seq;
        public int3 PointMm, NormalPermille;
        public bool Run, Jump;
    }

    /// <summary>Writes a planned wall walk into a ghost's buffers.</summary>
    public static class WallWrite
    {
        /// <summary>The server's: <see cref="PathCorner"/>, <see cref="PathPlan"/>, <see cref="WallCorner"/>, <see cref="WallPlan"/> for <paramref name="seq"/>.</summary>
        public static void Server(EntityManager em, Entity e, ushort seq, NativeList<int3> corners, NativeList<WallCorner> legs, WallPlan plan)
        {
            int count = math.min(corners.Length, PlayerLimits.MaxWallCorners);
            var buffer = em.GetBuffer<PathCorner>(e);
            buffer.Clear();
            for (int i = 0; i < count; i++) buffer.Add(new PathCorner { XMm = corners[i].x, YMm = corners[i].y, ZMm = corners[i].z });
            em.SetComponentData(e, new PathPlan { Seq = seq, Count = (byte)count });
            var wall = em.GetBuffer<WallCorner>(e);
            wall.Clear();
            for (int i = 0; i < count; i++) wall.Add(i < legs.Length ? legs[i] : default);
            plan.Seq = seq;
            em.SetComponentData(e, plan);
        }

        /// <summary>The owner's own: <see cref="ClientPlanCorner"/> (its header is the gather's), <see cref="ClientWallCorner"/>, <see cref="ClientWallPlan"/>.</summary>
        public static int Client(EntityManager em, Entity e, ushort seq, NativeList<int3> corners, NativeList<WallCorner> legs, WallPlan plan)
        {
            int count = math.min(corners.Length, PlayerLimits.MaxWallCorners);
            var buffer = em.GetBuffer<ClientPlanCorner>(e);
            buffer.Clear();
            for (int i = 0; i < count; i++) buffer.Add(new ClientPlanCorner { XMm = corners[i].x, YMm = corners[i].y, ZMm = corners[i].z });
            var wall = em.GetBuffer<ClientWallCorner>(e);
            wall.Clear();
            for (int i = 0; i < count; i++) wall.Add(new ClientWallCorner { Leg = i < legs.Length ? legs[i] : default });
            plan.Seq = seq;
            em.SetComponentData(e, new ClientWallPlan { Plan = plan });
            return count;
        }

        /// <summary>Starts a server walk of the planned corners on the next number (the push-off, the rest of a cut wall walk).</summary>
        public static ushort Order(EntityManager em, Entity e, NetworkTick tick, NativeList<int3> corners, NativeList<WallCorner> legs, WallPlan plan)
        {
            var order = em.GetComponentData<ServerOrder>(e);
            ushort seq = PlayerServerPlanSystem.NextServerSeq(order.Seq);
            Server(em, e, seq, corners, legs, plan);
            int3 goal = corners[math.min(corners.Length, PlayerLimits.MaxWallCorners) - 1];
            order.Seq = seq;
            order.StartTick = tick;
            order.GoalXMm = goal.x; order.GoalYMm = goal.y; order.GoalZMm = goal.z;
            order.Flags = 0;
            em.SetComponentData(e, order);
            return seq;
        }
    }

    /// <summary>
    /// Server only, after the walk of the tick (the server never re-simulates, so nothing is paid twice). Every tick a body is on a wall
    /// (<see cref="MoveState.Wall"/>, moving or standing) adds its share of the Grip a second to what it owes, paid a whole point at a
    /// time (NGO <c>UpdateWall</c>); when it cannot pay, it pushes off and flips down to the ground in front of the wall (NGO <c>PushOff</c>,
    /// the only way off a wall that is not the player's choice). A wall walk cut at the corner budget goes on from where it stopped.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(PredictedSimulationSystemGroup))]
    [UpdateAfter(typeof(PlayerMotionSystem))]
    public partial class WallGripSystem : SystemBase
    {
        EntityQuery m_Query;

        /// <summary>Set by <see cref="PlayerEntitiesSetup.Install"/>; null = walls are free (tests, a world without the game's resources).</summary>
        public IWallGrip Grip { get; set; }

        /// <summary>Plans the push-off and the rest of a cut wall walk; null = out of Grip the body stays where it is.</summary>
        public IWallPlanner Planner { get; set; }

        /// <summary>Whole Grip points paid, push-offs, and cut walks taken up again (tests, the scripted run).</summary>
        public int Paid { get; private set; }
        public int PushOffs { get; private set; }
        public int Continued { get; private set; }

        protected override void OnCreate()
        {
            RequireForUpdate<NetworkTime>();
            m_Query = GetEntityQuery(new EntityQueryBuilder(Allocator.Temp)
                .WithAll<PlayerGhostTag, MoveState, PlayerPose, PoseTilt>().WithAll<WallPlan, WallGripOwed, ServerOrder, Simulate>());
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
                var owed = em.GetComponentData<WallGripOwed>(e);
                if (move.Wall != 1)
                {
                    // off a wall (perched in a tree costs nothing, NGO)
                    // counted from the moment on the wall (NGO resets it as the wall move begins)
                    if (owed.Micro != 0) em.SetComponentData(e, new WallGripOwed());
                    continue;
                }
                if (Grip != null && Grip.GripPerSecondMilli > 0)
                {
                    owed.Micro += WallLeg.OwedMicro(Grip.GripPerSecondMilli, tickMicros);
                    if (owed.Micro >= 1000000L)
                    {
                        int whole = (int)(owed.Micro / 1000000L);
                        if (Grip.TryPay(e, whole)) { owed.Micro -= whole * 1000000L; Paid += whole; }
                        else
                        {
                            em.SetComponentData(e, new WallGripOwed());
                            PushOff(em, e, nt.ServerTick);
                            continue;
                        }
                    }
                    em.SetComponentData(e, owed);
                }
                if ((move.Flags & MoveFlags.Moving) == 0) TakeUpCutWalk(em, e, nt.ServerTick, in move);
            }
            PayClimbs(em, nt.ServerTick);
        }

        /// <summary>Straight climbs paid (tests, the scripted run), and stopped at the foot for want of Flux.</summary>
        public int ClimbsPaid { get; private set; }
        public int ClimbsRefused { get; private set; }

        /// <summary>
        /// A straight climb's Flux, taken as its plant starts (NGO <c>Begin</c>: "Not enough Flux (12 to climb)." and the player stays at the
        /// foot). Decided once per climb (<see cref="WallFlags.Paid"/>).
        /// </summary>
        void PayClimbs(EntityManager em, NetworkTick tick)
        {
            using var entities = m_Query.ToEntityArray(Allocator.Temp);
            foreach (var e in entities)
            {
                var move = em.GetComponentData<MoveState>(e);
                var wall = em.GetComponentData<WallPlan>(e);
                if ((move.Flags & MoveFlags.Moving) == 0 || !WallLeg.Of(in wall, move.OrderSeq)) continue;
                if ((wall.Flags & WallFlags.PayOnPlant) == 0 || (wall.Flags & WallFlags.Paid) != 0) continue;
                var plan = em.GetComponentData<PathPlan>(e);
                if (plan.Seq != move.OrderSeq) continue;
                var corners = em.GetBuffer<PathCorner>(e).Reinterpret<int3>().AsNativeArray();
                var legs = em.GetBuffer<WallCorner>(e);
                if (!WallLeg.SegmentAt(new int3(move.StartXMm, move.StartYMm, move.StartZMm), corners, math.min(plan.Count, corners.Length), move.ProgressMm,
                        out int i, out _, out _, out _, out _) || i >= legs.Length || legs[i].Micros <= 0) continue;   // still on the run to the foot
                wall.Flags |= WallFlags.Paid;
                em.SetComponentData(e, wall);
                int price = Grip != null ? Grip.ClimbFlux : 0;
                if (price <= 0 || Grip.TryPay(e, price)) { ClimbsPaid++; continue; }
                ClimbsRefused++;
                if (PlayerClickLog.Sink != null) PlayerClickLog.Sink("[KnWall] Not enough Flux to climb: stopped at the foot");
                // NGO: the climb does not begin; the player stands where the run to the foot ended
                var pose = em.GetComponentData<PlayerPose>(e);
                var stay = new NativeList<int3>(2, Allocator.Temp);
                var none = new NativeList<WallCorner>(2, Allocator.Temp);
                stay.Add(pose.Mm); stay.Add(pose.Mm);
                none.Add(default); none.Add(default);
                WallWrite.Order(em, e, tick, stay, none, default);
                stay.Dispose(); none.Dispose();
            }
        }

        /// <summary>Out of Grip: a server order off the wall (or, with no body to plan it, nothing: the next tick tries again).</summary>
        void PushOff(EntityManager em, Entity e, NetworkTick tick)
        {
            PushOffs++;
            if (PlayerClickLog.Sink != null) PlayerClickLog.Sink("[KnWall] out of Grip: pushed off the wall (push-off " + PushOffs + ")");
            var pose = em.GetComponentData<PlayerPose>(e);
            var tilt = em.GetComponentData<PoseTilt>(e);
            var corners = new NativeList<int3>(PlayerLimits.MaxWallCorners, Allocator.Temp);
            var legs = new NativeList<WallCorner>(PlayerLimits.MaxWallCorners, Allocator.Temp);
            try
            {
                if (Planner == null || !Planner.PushOff(e, pose.Mm, pose.YawCdeg, in tilt, corners, legs, out WallPlan plan) || corners.Length < 2) return;
                WallWrite.Order(em, e, tick, corners, legs, plan);
            }
            finally { corners.Dispose(); legs.Dispose(); }
        }

        /// <summary>A wall walk that was cut at the corner budget and has reached its cut: the rest of the same click, from here.</summary>
        void TakeUpCutWalk(EntityManager em, Entity e, NetworkTick tick, in MoveState move)
        {
            if (Planner == null || !em.HasComponent<WallGoal>(e)) return;
            var wall = em.GetComponentData<WallPlan>(e);
            var goal = em.GetComponentData<WallGoal>(e);
            if ((wall.Flags & WallFlags.Truncated) == 0 || wall.Seq != move.OrderSeq || goal.Seq != move.OrderSeq) return;
            var pose = em.GetComponentData<PlayerPose>(e);
            var tilt = em.GetComponentData<PoseTilt>(e);
            var corners = new NativeList<int3>(PlayerLimits.MaxWallCorners, Allocator.Temp);
            var legs = new NativeList<WallCorner>(PlayerLimits.MaxWallCorners, Allocator.Temp);
            try
            {
                goal.Seq = 0;   // asked once: a refusal from here leaves the body standing on the wall
                if (Planner.Plan(e, pose.Mm, pose.YawCdeg, in tilt, goal.PointMm, goal.NormalPermille, goal.Run, goal.Jump, true, corners, legs,
                        out WallPlan plan, out _) == WallAnswer.Planned && corners.Length >= 2)
                {
                    goal.Seq = WallWrite.Order(em, e, tick, corners, legs, plan);
                    if ((plan.Flags & WallFlags.Truncated) == 0) goal.Seq = 0;
                    Continued++;
                }
                em.SetComponentData(e, goal);
            }
            finally { corners.Dispose(); legs.Dispose(); }
        }
    }
}
