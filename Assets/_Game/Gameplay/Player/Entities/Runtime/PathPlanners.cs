using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.AI;

namespace KryticalNova.Player.Entities
{
    /// <summary>
    /// Plans the way to a point. Managed on purpose: it may use the NavMesh, which must never run inside the predicted loop.
    /// It runs once per order, on the main thread: on the client when the player clicks, on the server when it validates the click.
    /// The first corner added is the start point (the walk ignores it and starts from the player's pose); the last is the goal.
    /// </summary>
    public interface IPathPlanner
    {
        /// <summary>Adds the corners to <paramref name="cornersMm"/> (empty on entry). False = no way: the order is refused.</summary>
        bool TryPlan(int3 startMm, int3 goalMm, NativeList<int3> cornersMm);
    }

    /// <summary>
    /// The walkable surface the walk is planned on, for a short straight step that is not a walk (build 17 item 11.1: a standing player
    /// sliding out of another). Managed, main thread, server only. The server's path planner implements it when it plans on the NavMesh.
    /// </summary>
    public interface IWalkableSurface
    {
        /// <summary>
        /// How far along the straight step from <paramref name="fromMm"/> to <paramref name="toMm"/> a body gets before the edge of the
        /// walkable surface (a wall, a drop): <paramref name="reachedMm"/> is <paramref name="toMm"/> when nothing is in the way, else the
        /// edge. False when the start is not on the surface at all (the step is not taken).
        /// </summary>
        bool TryClampStep(int3 fromMm, int3 toMm, out int3 reachedMm);
    }

    /// <summary>A vault the planner found (M3c): where it takes off and lands (the pose's height, mm) and its leg's numbers.</summary>
    public struct VaultSplice
    {
        public int3 TakeoffMm, LandingMm;
        public int AirMicros, RaiseMm;
        public short RiseEndsPermille, FallStartsPermille;
        /// <summary>The obstacle's height class (the body picks its own clip for it).</summary>
        public byte Style;
    }

    /// <summary>
    /// Finds a vault on the straight line from the start to the goal that beats the walk round (NGO's rules, filled by the layer that knows
    /// the body: <c>PlayerVaultMotor.PlanForGhost</c>); on the server and, for the owner's prediction, on the client. Managed, main thread.
    /// </summary>
    public interface IVaultPlanner
    {
        /// <param name="walkLengthMm">The walk round's length; <see cref="int.MaxValue"/> when there is no way round.</param>
        /// <param name="speedMmps">The speed the walk reaches the take-off at (the air time follows from it).</param>
        bool TryPlan(Entity ghost, int3 startMm, int3 goalMm, int walkLengthMm, int speedMmps, out VaultSplice vault);
    }

    /// <summary>
    /// The walk's corners with a vault in them (M3c, `m3c-vault-design.md`): the way to the take-off, the leg over the obstacle, the way
    /// on from the landing. One copy for the server's plan and the owner's own, so both splice the same corners.
    /// </summary>
    public static class VaultPath
    {
        /// <summary>
        /// Plans <paramref name="startMm"/> to <paramref name="goalMm"/> into <paramref name="corners"/>, over an obstacle when
        /// <paramref name="vaults"/> finds one shorter than the way round. <paramref name="vault"/> is the leg (its Seq is the caller's
        /// to set), all zero without one. False = no way at all.
        /// </summary>
        public static bool Plan(IPathPlanner planner, IVaultPlanner vaults, Entity ghost, int3 startMm, int3 goalMm, int speedMmps,
            NativeList<int3> corners, out VaultPlan vault)
        {
            vault = default;
            corners.Clear();
            bool walk = planner.TryPlan(startMm, goalMm, corners) && corners.Length >= 2;
            if (vaults == null) return walk;
            int walkLength = walk ? PlayerPath.LengthMm(startMm, corners.AsArray(), corners.Length) : int.MaxValue;
            if (!vaults.TryPlan(ghost, startMm, goalMm, walkLength, speedMmps, out VaultSplice s)) return walk;
            var toTakeoff = new NativeList<int3>(PlayerLimits.MaxCorners, Allocator.Temp);
            var onward = new NativeList<int3>(PlayerLimits.MaxCorners, Allocator.Temp);
            try
            {
                if (!planner.TryPlan(startMm, s.TakeoffMm, toTakeoff) || toTakeoff.Length < 1) return walk;
                if (!planner.TryPlan(s.LandingMm, goalMm, onward) || onward.Length < 1) return walk;
                if (toTakeoff.Length + onward.Length > PlayerLimits.MaxCorners) return walk;   // too long a way to splice: walk round
                corners.Clear();
                for (int i = 0; i < toTakeoff.Length; i++) corners.Add(toTakeoff[i]);
                int3 takeoff = corners[corners.Length - 1];
                int legStart = PlayerPath.LengthMm(startMm, corners.AsArray(), corners.Length);
                int3 landing = onward[0];
                corners.Add(landing);
                for (int i = 1; i < onward.Length; i++) corners.Add(onward[i]);
                if (corners.Length < 2) return walk;
                vault = new VaultPlan
                {
                    LegStartMm = legStart,
                    LegLengthMm = PlayerPath.DistanceMm(takeoff, landing),
                    AirMicros = s.AirMicros, RaiseMm = s.RaiseMm,
                    RiseEndsPermille = s.RiseEndsPermille, FallStartsPermille = s.FallStartsPermille, Style = s.Style,
                };
                return true;
            }
            finally { toTakeoff.Dispose(); onward.Dispose(); }
        }
    }

    /// <summary>The way is a straight line (tests, bots, flat test maps). With <see cref="Dogleg"/> it turns once, to test corners.</summary>
    public sealed class StraightLinePlanner : IPathPlanner
    {
        /// <summary>Go along X first, then along Z (two segments).</summary>
        public bool Dogleg;
        /// <summary>Plans refused while true (tests of the refusal path).</summary>
        public bool RefuseAll;

        public bool TryPlan(int3 startMm, int3 goalMm, NativeList<int3> cornersMm)
        {
            if (RefuseAll) return false;
            cornersMm.Add(startMm);
            if (Dogleg) cornersMm.Add(new int3(goalMm.x, startMm.y, startMm.z));
            cornersMm.Add(goalMm);
            return true;
        }
    }

    /// <summary>
    /// The way over the NavMesh, with the static API (<c>NavMesh.SamplePosition</c>, <c>NavMesh.CalculatePath</c>): it needs no
    /// agent. The goal is moved onto the mesh first; corners come back as millimetres lifted by <see cref="BaseOffsetM"/>
    /// (the old agent's base offset), so the loop never queries the ground. Main thread only.
    /// </summary>
    public sealed class NavMeshPathPlanner : IPathPlanner, IWalkableSurface
    {
        public int AgentTypeId;
        public int AreaMask = NavMesh.AllAreas;
        /// <summary>How far round the click a place on the mesh is looked for (the old destination sample radius).</summary>
        public float SampleRadius = 2f;
        /// <summary>The body stands this far above the mesh (agent base offset); added to every corner.</summary>
        public float BaseOffsetM;

        readonly NavMeshPath m_Path = new NavMeshPath();

        public bool TryPlan(int3 startMm, int3 goalMm, NativeList<int3> cornersMm)
        {
            var filter = new NavMeshQueryFilter { agentTypeID = AgentTypeId, areaMask = AreaMask };
            Vector3 start = ToMeters(startMm) - Vector3.up * BaseOffsetM;
            Vector3 goal = ToMeters(goalMm) - Vector3.up * BaseOffsetM;
            if (!NavMesh.SamplePosition(goal, out NavMeshHit goalHit, SampleRadius, filter)) return false;
            Vector3 from = start;
            if (NavMesh.SamplePosition(start, out NavMeshHit startHit, SampleRadius, filter)) from = startHit.position;
            if (!NavMesh.CalculatePath(from, goalHit.position, filter, m_Path)) return false;
            if (m_Path.status == NavMeshPathStatus.PathInvalid) return false;
            var corners = m_Path.corners;
            if (corners == null || corners.Length < 2) return false;
            cornersMm.Add(startMm);
            for (int i = 1; i < corners.Length; i++)
                cornersMm.Add(ToMm(corners[i] + Vector3.up * BaseOffsetM));
            return true;
        }

        /// <summary>The step on the same NavMesh, filter and base offset the walk is planned with: NavMesh.Raycast stops at its edge.</summary>
        public bool TryClampStep(int3 fromMm, int3 toMm, out int3 reachedMm)
        {
            reachedMm = fromMm;
            var filter = new NavMeshQueryFilter { agentTypeID = AgentTypeId, areaMask = AreaMask };
            Vector3 from = ToMeters(fromMm) - Vector3.up * BaseOffsetM;
            if (!NavMesh.SamplePosition(from, out NavMeshHit start, 0.5f, filter)) return false;
            Vector3 to = ToMeters(toMm) - Vector3.up * BaseOffsetM;
            to.y = start.position.y;
            if (!NavMesh.Raycast(start.position, to, out NavMeshHit edge, filter)) { reachedMm = new int3(toMm.x, fromMm.y, toMm.z); return true; }
            int3 hit = ToMm(edge.position);
            // the edge is measured from where the body stands on the mesh; keep the part of the step that was walkable
            reachedMm = new int3(fromMm.x + (hit.x - ToMm(start.position).x), fromMm.y, fromMm.z + (hit.z - ToMm(start.position).z));
            return true;
        }

        static Vector3 ToMeters(int3 mm) => new Vector3(mm.x, mm.y, mm.z) * 0.001f;

        static int3 ToMm(Vector3 m) => new int3(
            Mathf.RoundToInt(m.x * 1000f), Mathf.RoundToInt(m.y * 1000f), Mathf.RoundToInt(m.z * 1000f));
    }
}
