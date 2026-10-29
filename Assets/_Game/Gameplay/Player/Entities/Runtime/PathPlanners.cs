using Unity.Collections;
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
    public sealed class NavMeshPathPlanner : IPathPlanner
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

        static Vector3 ToMeters(int3 mm) => new Vector3(mm.x, mm.y, mm.z) * 0.001f;

        static int3 ToMm(Vector3 m) => new int3(
            Mathf.RoundToInt(m.x * 1000f), Mathf.RoundToInt(m.y * 1000f), Mathf.RoundToInt(m.z * 1000f));
    }
}
