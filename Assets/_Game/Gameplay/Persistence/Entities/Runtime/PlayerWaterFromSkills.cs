using System.Collections.Generic;
using KryticalNova.Player;
using KryticalNova.Player.Entities;
using KryticalNova.Progression;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.AI;

namespace KryticalNova.Persistence.Entities
{
    /// <summary>
    /// Plans Waterstride with the ghost's body in <paramref name="world"/> (the server's body, or the owner's own view body for its
    /// prediction): NGO's rules (<see cref="PlayerWaterstrideMotor.PlanForGhost"/>, `m3c-waterstride-design.md`) in the ghost's integers.
    /// The corners are the pose's millimetres (the root, the agent's base offset above the feet): from land, the walk to the shore cut
    /// 0.7 m short (NGO starts the run there), then the way over the water; ashore, the shore point and the walk on. No numbers of its own.
    /// </summary>
    public sealed class BodyWaterPlanner : IWaterPlanner
    {
        readonly World m_World;
        readonly IPathPlanner m_Paths;
        readonly List<Vector3> m_Route = new List<Vector3>(32);

        /// <summary>Why the last click was or was not Waterstride's (NGO's words; tests and the scripted run).</summary>
        public string LastDecision { get; private set; }

        public BodyWaterPlanner(World world, IPathPlanner paths)
        {
            m_World = world;
            m_Paths = paths;
        }

        public WaterAnswer Plan(Entity ghost, int3 startMm, int3 goalMm, bool run, bool onWater, bool stranded, NativeList<int3> corners,
            out WaterPlan leg, out string reason)
        {
            leg = default;
            reason = null;
            corners.Clear();
            GameObject body = PlayerJumpFromSkills.BodyOf(m_World, ghost);
            var motor = body != null ? body.GetComponent<PlayerWaterstrideMotor>() : null;
            if (motor == null) { LastDecision = "no water motor"; return WaterAnswer.NotWater; }
            float offset = OffsetOf(body);
            var em = m_World.EntityManager;
            // the level and ranks the ghost carries (the server writes them from this same body; the owner's client has only these)
            MoveRanks ranks = em.HasComponent<MoveRanks>(ghost) ? em.GetComponentData<MoveRanks>(ghost) : default;
            MoveStats stats = em.HasComponent<MoveStats>(ghost) ? em.GetComponentData<MoveStats>(ghost) : default;
            var resources = body.GetComponent<PlayerResources>();
            bool alive = resources != null && resources.IsAlive;
            bool hasFlux = resources != null && resources.CanSpendChi(motor.FluxPerSecond);
            Vector3 feet = M(startMm) - Vector3.up * offset;
            string why = motor.PlanForGhost(feet, M(goalMm), run, onWater, stranded, ranks.Level, ranks.Run, ranks.Jump, alive, hasFlux,
                (run ? stats.RunMmps : stats.WalkMmps) * 0.001f, m_Route, out PlayerWaterstrideMotor.GhostWater plan);
            if (why == string.Empty) { LastDecision = "not Waterstride's"; return WaterAnswer.NotWater; }
            if (why != null) { LastDecision = reason = why; return WaterAnswer.Refused; }

            corners.Add(startMm);
            int legStart = 0;
            if (plan.Way.FromLand && !plan.Way.StartsNow)
            {
                // the walk to the shore, cut where NGO starts the run onto the water (0.7 m before the shore point)
                var toShore = new NativeList<int3>(PlayerLimits.MaxCorners, Allocator.Temp);
                try
                {
                    if (m_Paths == null || !m_Paths.TryPlan(startMm, Mm(plan.Way.ShoreFeet + Vector3.up * offset), toShore) || toShore.Length < 2)
                    { LastDecision = reason = "There is no way to that shore."; corners.Clear(); return WaterAnswer.Refused; }
                    int length = PlayerPath.LengthMm(startMm, toShore.AsArray(), toShore.Length);
                    legStart = math.max(0, length - Mathf.RoundToInt(WaterstrideRules.ShoreTrigger * 1000f));
                    AddUpTo(corners, startMm, toShore, legStart);
                }
                finally { toShore.Dispose(); }
            }
            foreach (Vector3 p in m_Route) corners.Add(Mm(p + Vector3.up * offset));
            int legEnd = PlayerPath.LengthMm(startMm, corners.AsArray(), corners.Length);
            byte flags = 0;
            if (plan.Way.ToLand)
            {
                // ashore at the shore point (NGO puts the body there), then the walk on to the place clicked
                int3 land = Mm(plan.Way.LandFeet + Vector3.up * offset);
                corners.Add(land);
                if (WaterstrideRules.Flat(plan.Way.LandFeet, plan.Way.WalkOnTo) > WaterstrideRules.WalkOnMinimum && m_Paths != null)
                {
                    var on = new NativeList<int3>(PlayerLimits.MaxCorners, Allocator.Temp);
                    try
                    {
                        if (m_Paths.TryPlan(land, Mm(plan.Way.WalkOnTo + Vector3.up * offset), on))
                            for (int i = 1; i < on.Length; i++) corners.Add(on[i]);
                    }
                    finally { on.Dispose(); }
                }
            }
            else flags |= WaterFlags.EndsOnWater;
            if (corners.Length > PlayerLimits.MaxCorners) { LastDecision = reason = "No way over the water there."; corners.Clear(); return WaterAnswer.Refused; }
            leg = new WaterPlan
            {
                LegStartMm = legStart, LegEndMm = legEnd, Flags = flags,
                SpeedMmps = Mathf.RoundToInt(plan.Speed * 1000f),
                TurnCdegPerSecond = Mathf.RoundToInt(PlayerWaterstrideMotor.TurnDegreesPerSecond * 100f),
            };
            LastDecision = plan.Way.ToLand ? "ashore" : plan.Way.FromLand ? "out over the water" : "over the water";
            return WaterAnswer.Planned;
        }

        public bool PlanStranded(Entity ghost, int3 startMm, NativeList<int3> corners, out WaterPlan leg)
        {
            leg = default;
            corners.Clear();
            GameObject body = PlayerJumpFromSkills.BodyOf(m_World, ghost);
            var motor = body != null ? body.GetComponent<PlayerWaterstrideMotor>() : null;
            if (motor == null) return false;
            float offset = OffsetOf(body);
            if (!motor.PlanStrandedForGhost(M(startMm) - Vector3.up * offset, m_Route, out Vector3 landFeet)) return false;
            corners.Add(startMm);
            foreach (Vector3 p in m_Route) corners.Add(Mm(p + Vector3.up * offset));
            int legEnd = PlayerPath.LengthMm(startMm, corners.AsArray(), corners.Length);
            corners.Add(Mm(landFeet + Vector3.up * offset));
            leg = new WaterPlan
            {
                LegStartMm = 0, LegEndMm = legEnd, Flags = WaterFlags.Stranded,
                SpeedMmps = Mathf.RoundToInt(motor.StrandedSpeed * 1000f),
                TurnCdegPerSecond = Mathf.RoundToInt(PlayerWaterstrideMotor.TurnDegreesPerSecond * 100f),
            };
            return true;
        }

        /// <summary>The corners of <paramref name="path"/> up to <paramref name="progressMm"/> along it, ending exactly there.</summary>
        static void AddUpTo(NativeList<int3> corners, int3 start, NativeList<int3> path, int progressMm)
        {
            int walked = 0;
            int3 from = start;
            for (int i = 1; i < path.Length; i++)
            {
                int3 to = path[i];
                int seg = PlayerPath.DistanceMm(from, to);
                if (walked + seg >= progressMm)
                {
                    float t = seg > 0 ? (progressMm - walked) / (float)seg : 0f;
                    corners.Add(new int3((int)math.round(math.lerp(from.x, to.x, t)), (int)math.round(math.lerp(from.y, to.y, t)),
                        (int)math.round(math.lerp(from.z, to.z, t))));
                    return;
                }
                corners.Add(to);
                walked += seg;
                from = to;
            }
        }

        static float OffsetOf(GameObject body)
        {
            var agent = body.GetComponent<NavMeshAgent>();
            return agent != null ? agent.baseOffset : 0f;
        }

        static Vector3 M(int3 mm) => new Vector3(mm.x, mm.y, mm.z) * 0.001f;

        static int3 Mm(Vector3 m) => new int3(Mathf.RoundToInt(m.x * 1000f), Mathf.RoundToInt(m.y * 1000f), Mathf.RoundToInt(m.z * 1000f));
    }

    /// <summary>Waterstride's Flux, paid by the ghost's server body (NGO: 3 a second, a whole point at a time). Server only.</summary>
    public sealed class BodyWaterFlux : IWaterFlux
    {
        readonly World m_World;
        readonly int m_PerSecondMilli;

        public BodyWaterFlux(World world, GameObject prefab)
        {
            m_World = world;
            var motor = prefab != null ? prefab.GetComponent<PlayerWaterstrideMotor>() : null;
            m_PerSecondMilli = motor != null ? Mathf.RoundToInt(motor.FluxPerSecond * 1000f) : 0;
        }

        public int FluxPerSecondMilli => m_PerSecondMilli;

        public bool TryPay(Entity ghost, int points)
        {
            GameObject body = PlayerJumpFromSkills.BodyOf(m_World, ghost);
            var resources = body != null ? body.GetComponent<PlayerResources>() : null;
            return resources != null && resources.TrySpendChi(points);
        }
    }
}
