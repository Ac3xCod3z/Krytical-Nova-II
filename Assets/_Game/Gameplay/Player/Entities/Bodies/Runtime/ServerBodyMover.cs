using Unity.Entities;
using UnityEngine;
using KryticalNova.Core;
using World = Unity.Entities.World;

namespace KryticalNova.Player.Entities.Bodies
{
    /// <summary>
    /// The movement of a ServerBody (M3a-2b): the body never steers itself, its ghost does. A request from rule code
    /// (<c>PointAndClickPlayer.ServerMoveTo</c>, a placement through <c>NetworkPlacement.Place</c>) becomes a ghost order or a
    /// ghost placement; <see cref="ServerBodySyncSystem"/> then brings the body to the ghost every tick. A ghost that is gone or
    /// leaving takes nothing.
    /// </summary>
    public sealed class ServerBodyMover : IBodyMover
    {
        readonly World m_World;
        readonly Entity m_Ghost;
        readonly Transform m_Body;

        public ServerBodyMover(World serverWorld, Entity ghost, Transform body)
        {
            m_World = serverWorld;
            m_Ghost = ghost;
            m_Body = body;
        }

        public Entity Ghost => m_Ghost;

        /// <summary>Orders, stops and placements handed to the ghost (tests).</summary>
        public int Orders { get; private set; }
        public int Stops { get; private set; }
        public int Placements { get; private set; }

        /// <summary>The ghost exists in a live world and its player has not left.</summary>
        public bool IsLive
        {
            get
            {
                if (m_World == null || !m_World.IsCreated) return false;
                var em = m_World.EntityManager;
                return em.Exists(m_Ghost) && em.HasComponent<PlayerPose>(m_Ghost) && !em.HasComponent<PlayerLeaving>(m_Ghost);
            }
        }

        /// <summary>A walk planned by the server's planner next tick: at run speed when <paramref name="run"/>; <paramref name="payRun"/> = Run's Flux taken then (refused without it).</summary>
        public void MoveTo(Vector3 point, bool run, bool payRun)
        {
            if (!IsLive) return;
            PlayerEntitiesSetup.IssueOrder(m_World.EntityManager, m_Ghost, PoseMath.Mm(point), run, run && payRun);
            Orders++;
        }

        /// <summary>
        /// Stops where the ghost stands. The ghost has no server stop order yet, so this is a placement on its own pose (it drops
        /// the walk; clients snap by at most the distance they had eased behind). A dedicated stop order is M3b work.
        /// </summary>
        public void Stop()
        {
            if (!IsLive) return;
            var em = m_World.EntityManager;
            PlayerPose pose = em.GetComponentData<PlayerPose>(m_Ghost);
            PlayerEntitiesSetup.Teleport(em, m_Ghost, pose.Mm, pose.YawCdeg);
            Stops++;
        }

        /// <summary>The ghost is in a jump's flight now.</summary>
        public bool Airborne => IsLive && m_World.GetExistingSystemManaged<PlayerJumpServerSystem>() is { } jump && jump.InFlight(m_Ghost);

        /// <summary>A jump's flight, a vault or a wall (NGO's motor state Jumping; wave 3, 12 Nov 2026): what takes a rider off.</summary>
        public bool InJumpingMove
        {
            get
            {
                if (!IsLive) return false;
                var em = m_World.EntityManager;
                byte wall = em.HasComponent<MoveState>(m_Ghost) ? em.GetComponentData<MoveState>(m_Ghost).Wall : (byte)0;
                return JumpingMove(Airborne, PlayerServerPlanSystem.InVault(em, m_Ghost), wall);
            }
        }

        /// <summary>
        /// NGO's rule for "the motor is Jumping" on a body without a motor: a jump's flight, a vault, or any wall state (Wall 1 on a wall,
        /// 2 perched: NGO's wall motor played its Jumping clips from the plant on). Water alone is not one.
        /// </summary>
        public static bool JumpingMove(bool airborne, bool vault, byte wall) => airborne || vault || wall != 0;

        /// <summary>Metres left of the ghost's walk along the server's planned path (0 standing or without a plan for this order).</summary>
        public float PathLeft
        {
            get
            {
                if (!IsLive) return 0f;
                var em = m_World.EntityManager;
                if (!em.HasComponent<MoveState>(m_Ghost) || !em.HasComponent<PathPlan>(m_Ghost) || !em.HasBuffer<PathCorner>(m_Ghost)) return 0f;
                MoveState move = em.GetComponentData<MoveState>(m_Ghost);
                PathPlan plan = em.GetComponentData<PathPlan>(m_Ghost);
                if ((move.Flags & MoveFlags.Moving) == 0 || plan.Seq != move.OrderSeq || plan.Count < 2) return 0f;
                DynamicBuffer<PathCorner> buffer = em.GetBuffer<PathCorner>(m_Ghost);
                using var corners = new Unity.Collections.NativeArray<Unity.Mathematics.int3>(buffer.Length, Unity.Collections.Allocator.Temp);
                var c = corners;
                for (int i = 0; i < buffer.Length; i++) c[i] = new Unity.Mathematics.int3(buffer[i].XMm, buffer[i].YMm, buffer[i].ZMm);
                var start = new Unity.Mathematics.int3(move.StartXMm, move.StartYMm, move.StartZMm);
                return PlayerPath.LeftMm(start, corners, plan.Count, move.ProgressMm) / 1000f;
            }
        }

        /// <summary>The ghost is not standing still: walking, in a jump or a vault, on a wall or the water.</summary>
        public bool Busy
        {
            get
            {
                if (!IsLive) return false;
                var em = m_World.EntityManager;
                if (em.HasComponent<MoveState>(m_Ghost))
                {
                    MoveState move = em.GetComponentData<MoveState>(m_Ghost);
                    if ((move.Flags & MoveFlags.Moving) != 0 || move.Wall != 0 || move.Water != 0) return true;
                }
                return PlayerServerPlanSystem.InVault(em, m_Ghost) || Airborne;
            }
        }

        /// <summary>A jump the server orders: the jump system's own rules, plan and Flux (M8 8.4.3c).</summary>
        public bool JumpTo(Vector3 landing, out string reason)
        {
            reason = "You cannot jump now.";
            if (!IsLive) return false;
            PlayerJumpServerSystem jump = m_World.GetExistingSystemManaged<PlayerJumpServerSystem>();
            if (jump == null) { reason = "Jump is not set up on this server."; return false; }
            return jump.TryServerJump(m_Ghost, PoseMath.Mm(landing), out reason);
        }

        /// <summary>Places the ghost (its teleport number goes up, so every client snaps) and the body at once.</summary>
        public void Teleport(Vector3 position, Quaternion rotation)
        {
            if (!IsLive) return;
            PlayerEntitiesSetup.Teleport(m_World.EntityManager, m_Ghost, PoseMath.Mm(position), PoseMath.Cdeg(rotation));
            if (m_Body != null) m_Body.SetPositionAndRotation(position, rotation);
            Placements++;
        }
    }
}
