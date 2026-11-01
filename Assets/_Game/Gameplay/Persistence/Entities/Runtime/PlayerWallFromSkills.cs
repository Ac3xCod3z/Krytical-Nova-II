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
    /// Plans Wallstride's wall walk with the ghost's body in <paramref name="world"/> (the server's body, or the owner's own view body for
    /// its prediction): NGO's rules (<see cref="PlayerWallRunMotor.PlanWallForGhost"/>, `m3c-wallstride-design.md` section 3) in the
    /// ghost's integers. The corners are the pose's millimetres (the root: the feet plus the body's up times the agent's base offset, so on a
    /// wall the root stands out from the face); one <see cref="WallCorner"/> per corner. No numbers of its own.
    /// </summary>
    public sealed class BodyWallPlanner : IWallPlanner
    {
        readonly World m_World;
        readonly bool m_TellOwner;
        // where each ghost's wall walk started from the ground (NGO plan.BaseFeet: a push-off with no ground in front lands there)
        readonly Dictionary<Entity, Vector3> m_Base = new Dictionary<Entity, Vector3>();

        /// <summary>Why the last click was or was not the wall's (NGO's words; tests and the scripted run).</summary>
        public string LastDecision { get; private set; }

        /// <param name="tellOwner">The owner's client: a refusal is shown on the own body in NGO's words, as the NGO client did.</param>
        public BodyWallPlanner(World world, bool tellOwner = false)
        {
            m_World = world;
            m_TellOwner = tellOwner;
        }

        public WallAnswer Plan(Entity ghost, int3 poseMm, int yawCdeg, in PoseTilt tilt, int3 pointMm, int3 normalPermille, bool run, bool jump,
            bool onWall, NativeList<int3> corners, NativeList<WallCorner> legs, out WallPlan plan, out string reason)
        {
            plan = default;
            reason = null;
            corners.Clear();
            legs.Clear();
            GameObject body = PlayerJumpFromSkills.BodyOf(m_World, ghost);
            var motor = body != null ? body.GetComponent<PlayerWallRunMotor>() : null;
            if (motor == null) { LastDecision = "no wall motor"; return WallAnswer.NotWall; }
            float offset = OffsetOf(body);
            var ask = Ask(body, motor, ghost, yawCdeg, in tilt, run, jump, onWall);
            Vector3 feet = M(poseMm) - ask.Up * offset;
            if (!onWall) m_Base[ghost] = feet;
            Vector3 wallBase = m_Base.TryGetValue(ghost, out Vector3 b) ? b : feet;
            Vector3 normal = new Vector3(normalPermille.x, normalPermille.y, normalPermille.z) * 0.001f;
            Vector3 point = M(pointMm);
            // NGO's order: the wall walk where the zone's graph covers the wall (or the body is on one), else the straight climb (part 2)
            string why;
            WallWalkLayout layout;
            if (tilt.Phase == WallPhase.Perched)
            {
                // perched in a tree: every click is a drop to the ground below (NGO PointAndClickPlayer 713-718, Jump selected or not)
                why = motor.PlanDropForGhost(feet, point, ask.Forward, out layout);
            }
            else if (onWall || motor.WantsWallWalkClick(point, normal, false)) why = motor.PlanWallForGhost(feet, point, normal, in ask, wallBase, out layout);
            else
            {
                if (jump) { LastDecision = "a jump at a wall without the graph (the catch's: Catch)"; return WallAnswer.NotWall; }
                why = motor.PlanClimbForGhost(feet, point, normal, in ask, out layout);
                if (layout == null && string.IsNullOrEmpty(why)) { LastDecision = "not climbable"; return WallAnswer.NotWall; }
            }
            if (layout == null)
            {
                LastDecision = reason = string.IsNullOrEmpty(why) ? "no way" : why;
                if (m_TellOwner) body.GetComponent<PointAndClickPlayer>()?.ShowViewRefusal(reason);
                return WallAnswer.Refused;
            }
            Write(layout, poseMm, offset, corners, legs, out plan);
            LastDecision = layout.EndsPerched ? "a tree climb, perched" : layout.PayOnPlant ? "a straight climb" : layout.EndsOnWall ? "onto the wall" : "over or off the wall";
            return WallAnswer.Planned;
        }

        public WallAnswer Catch(Entity ghost, int3 poseMm, int yawCdeg, int3 pointMm, int3 normalPermille, bool run,
            NativeList<int3> corners, NativeList<WallCorner> legs, out WallPlan plan, out int3 catchMm, out int apexYMm, out string reason)
        {
            plan = default;
            reason = null;
            catchMm = poseMm;
            apexYMm = poseMm.y;
            corners.Clear();
            legs.Clear();
            GameObject body = PlayerJumpFromSkills.BodyOf(m_World, ghost);
            var motor = body != null ? body.GetComponent<PlayerWallRunMotor>() : null;
            if (motor == null) { LastDecision = "no wall motor"; return WallAnswer.NotWall; }
            float offset = OffsetOf(body);
            var ask = Ask(body, motor, ghost, yawCdeg, default, run, true, false);
            Vector3 feet = M(poseMm) - Vector3.up * offset;
            Vector3 normal = new Vector3(normalPermille.x, normalPermille.y, normalPermille.z) * 0.001f;
            string why = motor.PlanCatchForGhost(feet, M(pointMm), normal, in ask, out Vector3 catchFeet, out float peak, out WallWalkLayout layout);
            if (layout == null && string.IsNullOrEmpty(why)) { LastDecision = "not climbable (the ordinary jump)"; return WallAnswer.NotWall; }
            if (layout == null)
            {
                LastDecision = reason = why;
                if (m_TellOwner) body.GetComponent<PointAndClickPlayer>()?.ShowViewRefusal(reason);
                return WallAnswer.Refused;
            }
            catchMm = Mm(catchFeet + Vector3.up * offset);
            apexYMm = Mathf.RoundToInt((peak + offset) * 1000f);
            Write(layout, catchMm, offset, corners, legs, out plan);
            LastDecision = layout.EndsPerched ? "a jump at a tree, caught, perched" : "a jump at a wall, caught";
            return WallAnswer.Planned;
        }

        public bool PushOff(Entity ghost, int3 poseMm, int yawCdeg, in PoseTilt tilt, NativeList<int3> corners, NativeList<WallCorner> legs, out WallPlan plan)
        {
            plan = default;
            corners.Clear();
            legs.Clear();
            GameObject body = PlayerJumpFromSkills.BodyOf(m_World, ghost);
            var motor = body != null ? body.GetComponent<PlayerWallRunMotor>() : null;
            if (motor == null) return false;
            float offset = OffsetOf(body);
            var ask = Ask(body, motor, ghost, yawCdeg, in tilt, false, false, true);
            Vector3 feet = M(poseMm) - ask.Up * offset;
            WallWalkLayout layout = motor.PushOffForGhost(feet, in ask, m_Base.TryGetValue(ghost, out Vector3 b) ? b : feet);
            if (layout == null) return false;
            Write(layout, poseMm, offset, corners, legs, out plan);
            LastDecision = "pushed off";
            return true;
        }

        /// <summary>What NGO's checks read of the player: the gate, alive, the Flux for a second, the body's up and way, the ground speed.</summary>
        PlayerWallRunMotor.GhostWallAsk Ask(GameObject body, PlayerWallRunMotor motor, Entity ghost, int yawCdeg, in PoseTilt tilt, bool run, bool jump, bool onWall)
        {
            var em = m_World.EntityManager;
            // the level and ranks the ghost carries (the server writes them from this same body; the owner's client has only these)
            MoveRanks ranks = em.HasComponent<MoveRanks>(ghost) ? em.GetComponentData<MoveRanks>(ghost) : default;
            MoveStats stats = em.HasComponent<MoveStats>(ghost) ? em.GetComponentData<MoveStats>(ghost) : default;
            OwnerFlux flux = em.HasComponent<OwnerFlux>(ghost) ? em.GetComponentData<OwnerFlux>(ghost) : default;
            var resources = body.GetComponent<PlayerResources>();
            Vector3 heading = Quaternion.Euler(0f, yawCdeg * 0.01f, 0f) * Vector3.forward;
            return new PlayerWallRunMotor.GhostWallAsk
            {
                Open = motor.WallstrideOpenFor(ranks.Level, ranks.Run, ranks.Jump),
                Alive = resources == null || resources.IsAlive,
                Busy = false,   // a click in the air or in a leg is refused before it gets here (the plan system, the gather)
                Jump = jump,
                Run = run,
                HasFluxForOne = flux.Milli >= 1000,
                Flux = flux.Milli * 0.001f,
                OnWall = onWall,
                Up = tilt.Tilted ? (Vector3)tilt.Up : Vector3.up,
                Forward = tilt.Tilted ? (Vector3)tilt.Forward : heading,
                GroundSpeed = (run ? stats.RunMmps : stats.WalkMmps) * 0.001f,
            };
        }

        /// <summary>The layout in the ghost's integers: the roots (feet + up x offset), the legs, the header.</summary>
        static void Write(WallWalkLayout layout, int3 poseMm, float offset, NativeList<int3> corners, NativeList<WallCorner> legs, out WallPlan plan)
        {
            for (int i = 0; i < layout.Corners.Count; i++)
            {
                WallWalkLayout.Corner c = layout.Corners[i];
                Vector3 up = c.Up.sqrMagnitude > 1e-4f ? c.Up.normalized : Vector3.up;
                corners.Add(i == 0 ? poseMm : Mm(c.Feet + up * offset));
                legs.Add(new WallCorner
                {
                    Micros = c.Seconds > 0f ? Mathf.Max(1, Mathf.RoundToInt(c.Seconds * 1000000f)) : 0,
                    Ease = WallCorner.Eases(c.EaseUp, c.EaseAlong),
                    Phase = (byte)c.Phase,
                    UpX = P(c.Up.x), UpY = P(c.Up.y), UpZ = P(c.Up.z),
                    FwdX = P(c.Forward.x), FwdY = P(c.Forward.y), FwdZ = P(c.Forward.z),
                });
            }
            plan = new WallPlan
            {
                SpeedMmps = Mathf.RoundToInt(layout.WallSpeed * 1000f),
                OnWallFrom = (byte)Mathf.Clamp(layout.OnWallFrom, 0, 255),
                OffWallFrom = (byte)Mathf.Clamp(layout.OffWallFrom, 0, 255),
                Flags = (byte)((layout.EndsOnWall ? WallFlags.EndsOnWall : 0) | (layout.Truncated ? WallFlags.Truncated : 0)
                    | (layout.PayOnPlant ? WallFlags.PayOnPlant : 0) | (layout.EndsPerched ? WallFlags.EndsPerched : 0)),
            };
        }

        static short P(float v) => (short)Mathf.Clamp(Mathf.RoundToInt(v * 1000f), -1000, 1000);

        static float OffsetOf(GameObject body)
        {
            var agent = body.GetComponent<NavMeshAgent>();
            return agent != null ? agent.baseOffset : 0f;
        }

        static Vector3 M(int3 mm) => new Vector3(mm.x, mm.y, mm.z) * 0.001f;

        static int3 Mm(Vector3 m) => new int3(Mathf.RoundToInt(m.x * 1000f), Mathf.RoundToInt(m.y * 1000f), Mathf.RoundToInt(m.z * 1000f));
    }

    /// <summary>Wallstride's Grip, paid by the ghost's server body (NGO: 15 Flux a second on a wall, a whole point at a time). Server only.</summary>
    public sealed class BodyWallGrip : IWallGrip
    {
        readonly World m_World;
        readonly int m_PerSecondMilli, m_ClimbFlux;

        public BodyWallGrip(World world, GameObject prefab)
        {
            m_World = world;
            var motor = prefab != null ? prefab.GetComponent<PlayerWallRunMotor>() : null;
            m_PerSecondMilli = motor != null ? Mathf.RoundToInt(motor.GripFluxPerSecond * 1000f) : 0;
            m_ClimbFlux = motor != null ? Mathf.CeilToInt(motor.ClimbFlux) : 0;
        }

        public int GripPerSecondMilli => m_PerSecondMilli;

        public int ClimbFlux => m_ClimbFlux;

        public bool TryPay(Entity ghost, int points)
        {
            GameObject body = PlayerJumpFromSkills.BodyOf(m_World, ghost);
            var resources = body != null ? body.GetComponent<PlayerResources>() : null;
            return resources != null && resources.TrySpendChi(points);
        }
    }
}
