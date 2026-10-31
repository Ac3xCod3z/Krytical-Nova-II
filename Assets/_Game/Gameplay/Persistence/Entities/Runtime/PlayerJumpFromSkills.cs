using KryticalNova.Player;
using KryticalNova.Player.Entities;
using KryticalNova.Player.Entities.Presentation;
using KryticalNova.Progression;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.AI;

namespace KryticalNova.Persistence.Entities
{
    /// <summary>
    /// The player ghost's jump from the game's own jump (M3b): <see cref="PlayerMotionCurves"/> baked from the body's jump clip and
    /// motion data for both worlds, the planner (<see cref="PlayerJumpMotor.PlanForGhost"/>: rank, landing, limits, apex, clear arc)
    /// and the Jump rank's Flux. No numbers of its own.
    /// </summary>
    public static class PlayerJumpFromSkills
    {
        /// <summary>The curves of a player prefab's or body's jump; null when it has no jump motor with a two-phase clip.</summary>
        public static PlayerMotionCurves? CurvesOf(GameObject player)
        {
            var motor = player != null ? player.GetComponent<PlayerJumpMotor>() : null;
            if (motor == null || motor.MotionData == null || !motor.TryReadJumpClip(out JumpPlanRules.Phases p, out AnimationCurve rootY))
            {
                if (player != null) Debug.LogWarning("[KnPlayer] " + player.name + " has no jump motor with a two-phase jump clip: the ghost jumps with straight curves.");
                return null;
            }
            return PlayerMotionCurves.FromClip(p.Takeoff, p.Apex, p.Land, motor.MotionData.landingCooldown, rootY.Evaluate,
                PlayerJumpMotor.OwnTakeoffHoldMax);
        }

        /// <summary>Puts the baked curves in <paramref name="world"/> as its one <see cref="PlayerMotionCurves"/> (replaces an older one).</summary>
        public static void InstallCurves(World world, GameObject player)
        {
            if (world == null || !world.IsCreated) return;
            PlayerMotionCurves? curves = CurvesOf(player);
            if (!curves.HasValue) return;
            EntityManager em = world.EntityManager;
            using var query = em.CreateEntityQuery(ComponentType.ReadWrite<PlayerMotionCurves>());
            Entity holder = query.CalculateEntityCount() > 0 ? query.GetSingletonEntity() : em.CreateEntity(typeof(PlayerMotionCurves));
            em.SetComponentData(holder, curves.Value);
        }

        internal static GameObject BodyOf(World world, Entity ghost) =>
            GhostBodyRegistry.TryGetByGhost(world, ghost, out GhostBodyEntry entry) ? entry.Body : null;
    }

    /// <summary>
    /// Plans a jump with the ghost's body in <paramref name="world"/> (the server's body, or the owner's own view body for its
    /// prediction). The ghost's pose is the body's root, the agent's base offset above the feet; the rules work on the feet.
    /// The owner's planner (<paramref name="checkCost"/>) also checks the rank and the Flux first, as the server's cost does before its
    /// plan: the owner never predicts a jump the server will refuse (no start, then a snap back).
    /// </summary>
    public sealed class BodyJumpPlanner : IJumpPlanner
    {
        public const string NoBodyText = "Jump controller is missing.";
        readonly World m_World;
        readonly BodyJumpCost m_Cost;

        public BodyJumpPlanner(World world, bool checkCost = false)
        {
            m_World = world;
            m_Cost = checkCost ? new BodyJumpCost(world) : null;
        }

        public bool TryPlan(Entity ghost, int3 fromFeetMm, int3 clickedMm, out int3 landingFeetMm, out int apexYMm, out string reason)
        {
            landingFeetMm = fromFeetMm;
            apexYMm = fromFeetMm.y;
            if (m_Cost != null && !m_Cost.CanPay(ghost, out reason)) return false;
            GameObject body = PlayerJumpFromSkills.BodyOf(m_World, ghost);
            var motor = body != null ? body.GetComponent<PlayerJumpMotor>() : null;
            if (motor == null) { reason = NoBodyText; return false; }
            var agent = body.GetComponent<NavMeshAgent>();
            float offset = agent != null ? agent.baseOffset : 0f;
            Vector3 startFeet = M(fromFeetMm) - Vector3.up * offset;
            reason = motor.PlanForGhost(startFeet, M(clickedMm), out Vector3 landing, out float peak);
            if (reason != null) return false;
            landingFeetMm = Mm(landing + Vector3.up * offset);
            apexYMm = Mathf.RoundToInt((peak + offset) * 1000f);
            return true;
        }

        static Vector3 M(int3 mm) => new Vector3(mm.x, mm.y, mm.z) * 0.001f;

        static int3 Mm(Vector3 m) => new int3(Mathf.RoundToInt(m.x * 1000f), Mathf.RoundToInt(m.y * 1000f), Mathf.RoundToInt(m.z * 1000f));
    }

    /// <summary>
    /// A jump costs the Jump rank's Flux, checked on the click and paid at take-off by the ghost's server body. On the owner's client
    /// only the check runs (its body holds the ranks and Flux the server sent); it never pays.
    /// </summary>
    public sealed class BodyJumpCost : IJumpCost
    {
        public const string NoBodyText = "You can't jump right now.";
        readonly World m_World;

        public BodyJumpCost(World world) => m_World = world;

        public bool CanPay(Entity ghost, out string reason)
        {
            PlayerMovementSkills skills = SkillsOf(ghost);
            if (skills == null) { reason = NoBodyText; return false; }
            // the NGO order and words: the learned rank first, then the Flux (the owner's client checks the same, in the same order)
            if (!skills.TryGetLearnedRank(MovementSkillId.Jump, out _)) { reason = PlayerJumpMotor.LearnJumpFirst; return false; }
            if (skills.CanPayForCommand(MovementSkillId.Jump, out reason)) { reason = null; return true; }
            return false;
        }

        public void Pay(Entity ghost)
        {
            PlayerMovementSkills skills = SkillsOf(ghost);
            if (skills != null) skills.TryPayForCommand(MovementSkillId.Jump, out _, out _);
        }

        PlayerMovementSkills SkillsOf(Entity ghost)
        {
            GameObject body = PlayerJumpFromSkills.BodyOf(m_World, ghost);
            return body != null ? body.GetComponent<PlayerMovementSkills>() : null;
        }
    }
}
