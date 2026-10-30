using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using UnityEngine;

namespace KryticalNova.Player.Entities
{
    /// <summary>
    /// Server only. Inside the predicted group but before <see cref="PredictedMoveSystem"/>, so a path exists on the tick its
    /// order starts. It never runs on a re-simulation (the server does not re-simulate), so the NavMesh is safe here.
    ///
    /// For a click of this tick: check the range, plan the way again with the server's own planner (the server's corners
    /// override the owner's), fill <see cref="PathCorner"/> and <see cref="PathPlan"/>; or refuse: <see cref="MoveState.RefusedSeq"/>
    /// is set (replicated, so the owner's rollback does not start the order again) and the click is wiped from this tick's input.
    /// For a <see cref="ServerOrderRequest"/>: plan it and write <see cref="ServerOrder"/> with the next server number.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(PredictedSimulationSystemGroup))]
    [UpdateAfter(typeof(PlayerTeleportSystem))]
    [UpdateBefore(typeof(PredictedMoveSystem))]
    public partial class PlayerServerPlanSystem : SystemBase
    {
        EntityQuery m_Query;
        bool m_WarnedPlanner;

        /// <summary>Set by <see cref="PlayerEntitiesSetup.Install"/>; no silent default.</summary>
        public IPathPlanner Planner { get; set; }

        /// <summary>Clicks refused and requests dropped (tests and the status page read it).</summary>
        public int Refused { get; private set; }

        protected override void OnCreate()
        {
            RequireForUpdate<NetworkTime>();
            m_Query = GetEntityQuery(new EntityQueryBuilder(Allocator.Temp)
                .WithAll<PlayerGhostTag, PlayerInput, PlayerPose, MoveState>()
                .WithAll<PathPlan, ServerOrder, PathCorner, Simulate>());
            RequireForUpdate(m_Query);
        }

        protected override void OnUpdate()
        {
            var nt = SystemAPI.GetSingleton<NetworkTime>();
            if (!nt.ServerTick.IsValid) return;
            var cfg = SystemAPI.TryGetSingleton<PlayerMoveSettings>(out var set) ? set : PlayerMoveSettings.Default;
            var em = EntityManager;
            using var entities = m_Query.ToEntityArray(Allocator.Temp);
            foreach (var e in entities)
            {
                var input = em.GetComponentData<PlayerInput>(e);
                bool click = input.Click.IsSet && input.ClickSeq != 0;
                bool request = em.IsComponentEnabled<ServerOrderRequest>(e);
                if (!click && !request) continue;
                if (Planner == null)
                {
                    if (!m_WarnedPlanner)
                    {
                        m_WarnedPlanner = true;
                        Debug.LogError("PlayerServerPlanSystem: no IPathPlanner installed (PlayerEntitiesSetup.Install); orders are refused.");
                    }
                    if (click) Refuse(em, e, input);
                    if (request) em.SetComponentEnabled<ServerOrderRequest>(e, false);
                    Refused++;
                    continue;
                }

                var pose = em.GetComponentData<PlayerPose>(e);
                if (request)
                {
                    var r = em.GetComponentData<ServerOrderRequest>(e);
                    em.SetComponentEnabled<ServerOrderRequest>(e, false);
                    var order = em.GetComponentData<ServerOrder>(e);
                    ushort seq = NextServerSeq(order.Seq);
                    var goal = new int3(r.GoalXMm, r.GoalYMm, r.GoalZMm);
                    if (TryWritePlan(em, e, pose, goal, seq, out bool truncated))
                    {
                        order.Seq = seq;
                        order.StartTick = nt.ServerTick;
                        order.GoalXMm = goal.x; order.GoalYMm = goal.y; order.GoalZMm = goal.z;
                        order.Flags = (byte)(r.Flags | (truncated ? OrderFlags.Truncated : 0));
                        em.SetComponentData(e, order);
                    }
                    else Refused++;
                    continue;      // a server order wins over a click of the same tick (the move step does the same)
                }

                {
                    var goal = new int3(input.GoalXMm, input.GoalYMm, input.GoalZMm);
                    bool tooFar = cfg.MaxClickRangeMm > 0 && PlayerPath.DistanceMm(pose.Mm, goal) > cfg.MaxClickRangeMm;
                    if (tooFar || !TryWritePlan(em, e, pose, goal, input.ClickSeq, out _))
                    {
                        Refuse(em, e, input);
                        Refused++;
                    }
                    else if (PlayerClickLog.Sink != null) PlayerClickLog.Server(input.ClickSeq, nt.ServerTick.TickIndexForValidTick);
                }
            }
        }

        bool TryWritePlan(EntityManager em, Entity e, in PlayerPose pose, int3 goal, ushort seq, out bool truncated)
        {
            truncated = false;
            var corners = new NativeList<int3>(PlayerLimits.MaxCorners, Allocator.Temp);
            try
            {
                if (!Planner.TryPlan(pose.Mm, goal, corners) || corners.Length < 2) return false;
                int count = math.min(corners.Length, PlayerLimits.MaxCorners);
                truncated = corners.Length > PlayerLimits.MaxCorners;
                var buffer = em.GetBuffer<PathCorner>(e);
                buffer.Clear();
                for (int i = 0; i < count; i++)
                    buffer.Add(new PathCorner { XMm = corners[i].x, YMm = corners[i].y, ZMm = corners[i].z });
                em.SetComponentData(e, new PathPlan { Seq = seq, Count = (byte)count });
                return true;
            }
            finally { corners.Dispose(); }
        }

        static void Refuse(EntityManager em, Entity e, PlayerInput input)
        {
            var m = em.GetComponentData<MoveState>(e);
            m.RefusedSeq = input.ClickSeq;
            em.SetComponentData(e, m);
            input.Click = default;      // this tick only: the next tick's input is copied in again
            em.SetComponentData(e, input);
        }

        /// <summary>Server order numbers carry the high bit and never use 0x8000 (that would read as 0 in the low bits).</summary>
        public static ushort NextServerSeq(ushort last)
        {
            int low = (last & PlayerLimits.ClientSeqMax) + 1;
            if (low > PlayerLimits.ClientSeqMax) low = 1;
            return (ushort)(PlayerLimits.ServerSeqBit | low);
        }
    }
}
