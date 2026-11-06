using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace KryticalNova.Brains
{
    /// <summary>
    /// A wall of the straight-line backend (singleton buffer, tests only): a brain cannot walk across the segment A-B (x, z), and a path
    /// search across it fails. The NavWorld backend (M5.4b, after the Linux proof) replaces it in the game.
    /// </summary>
    public struct PathTestWall : IBufferElementData
    {
        public float2 A, B;
    }

    /// <summary>The pure rules of brain movement and paths (M5.4, implementation plan s4): Burst-friendly, no state of their own.</summary>
    public static class PathRules
    {
        /// <summary>Metres from a waypoint or the destination at which the brain counts as there.</summary>
        public const float ArriveRadius = 0.2f;

        /// <summary>Whether the segments p-q and a-b cross (on the ground plane).</summary>
        public static bool Crosses(float2 p, float2 q, float2 a, float2 b)
        {
            static float Side(float2 o, float2 u, float2 v) => (u.x - o.x) * (v.y - o.y) - (u.y - o.y) * (v.x - o.x);
            float d1 = Side(a, b, p), d2 = Side(a, b, q), d3 = Side(p, q, a), d4 = Side(p, q, b);
            return d1 * d2 < 0f && d3 * d4 < 0f;
        }

        /// <summary>The straight-line backend's raycast: is the walk from <paramref name="from"/> to <paramref name="to"/> blocked by a wall?</summary>
        public static bool StraightBlocked(float3 from, float3 to, NativeArray<PathTestWall> walls)
        {
            if (!walls.IsCreated) return false;
            for (int i = 0; i < walls.Length; i++)
                if (Crosses(from.xz, to.xz, walls[i].A, walls[i].B)) return true;
            return false;
        }

        /// <summary>
        /// The straight-line backend's search: one waypoint at <paramref name="to"/> when nothing is in the way, else no path (it never
        /// routes around a wall; the NavWorld backend will).
        /// </summary>
        public static bool StraightFind(float3 from, float3 to, NativeArray<PathTestWall> walls, DynamicBuffer<BrainWaypoint> into)
        {
            into.Clear();
            if (StraightBlocked(from, to, walls)) return false;
            into.Add(new BrainWaypoint { P = to });
            return true;
        }

        /// <summary>
        /// Whether a brain whose straight line is blocked asks for a new path (plan s4): none yet; fighting, once the target moved more than
        /// <see cref="BrainConfig.RepathTargetMovedSq"/> since the last request and that request is at least
        /// <see cref="BrainConfig.RepathMinTicks"/> old; otherwise once per destination (with the same minimum gap).
        /// </summary>
        public static bool NeedsRequest(in BrainPath path, float3 goal, bool engaged, uint now, in BrainConfig c)
        {
            if (path.Status == BrainPathStatus.None) return true;
            float movedSq = SenseQueries.FlatSq(path.To, goal);
            bool moved = engaged ? movedSq > c.RepathTargetMovedSq : movedSq > 1e-4f;
            return moved && now - path.RequestTick >= c.RepathMinTicks;
        }

        /// <summary>The ground velocity toward <paramref name="steer"/>: full speed, slowing so it stops on the spot; none within <see cref="ArriveRadius"/>.</summary>
        public static float3 Seek(float3 at, float3 steer, float speed, float dt)
        {
            float2 to = steer.xz - at.xz;
            float dist = math.length(to);
            if (dist <= ArriveRadius || speed <= 0f || dt <= 0f) return float3.zero;
            float v = math.min(speed, dist / dt);
            return new float3(to.x / dist * v, 0f, to.y / dist * v);
        }

        /// <summary>The yaw (radians about up, 0 = +z) a rotation faces.</summary>
        public static float YawOf(quaternion q)
        {
            float3 f = math.mul(q, new float3(0f, 0f, 1f));
            return math.atan2(f.x, f.z);
        }

        /// <summary><paramref name="yaw"/> turned toward <paramref name="wanted"/> by at most <paramref name="maxStep"/>, the short way round.</summary>
        public static float TurnToward(float yaw, float wanted, float maxStep)
        {
            float d = wanted - yaw;
            d -= 2f * math.PI * math.floor((d + math.PI) / (2f * math.PI));
            return yaw + math.clamp(d, -maxStep, maxStep);
        }

        /// <summary>
        /// The way <paramref name="self"/> steps from <paramref name="other"/> when both stand on the very same spot: fixed by the pair
        /// (the same every run) and opposite for the two of them.
        /// </summary>
        public static float2 TieBreak(Entity self, Entity other)
        {
            int lo = math.min(self.Index, other.Index), hi = math.max(self.Index, other.Index);
            float angle = math.frac(lo * 0.618034f + hi * 0.381966f) * 2f * math.PI;
            float2 dir = new float2(math.sin(angle), math.cos(angle));
            return self.Index == lo ? dir : -dir;
        }
    }
}
