using KryticalNova.Player.Entities;
using KryticalNova.Player.Entities.Presentation;
using KryticalNova.Progression;
using Unity.Entities;
using UnityEngine;

namespace KryticalNova.Persistence.Entities
{
    /// <summary>
    /// The player ghost's walk numbers from the game's own movement data (M3a leftovers): <see cref="MovementSettings"/> baked into
    /// <see cref="PlayerMoveSettings"/> for both worlds, the speeds of <see cref="PlayerMovementSkills"/> (Run rank, mount, Move Speed
    /// stat, GM speed) into <see cref="MoveStats"/>, and the Run rank's Flux into <see cref="IRunCost"/>. No numbers of its own.
    /// </summary>
    public static class PlayerMoveFromSkills
    {
        /// <summary>
        /// The settings asset a player prefab's or body's skills use. Null when there is none yet (the scene may load after the world:
        /// the worlds install again from the first body); a warning when the object exists but carries no settings.
        /// </summary>
        public static MovementSettings SettingsOf(GameObject player)
        {
            if (player == null) return null;
            var skills = player.GetComponent<PlayerMovementSkills>();
            if (skills != null && skills.MovementSettings != null) return skills.MovementSettings;
            Debug.LogWarning("[KnPlayer] " + player.name + " has no PlayerMovementSkills with MovementSettings: the ghost walks with built-in numbers.");
            return null;
        }

        /// <summary><see cref="MovementSettings"/> in the ghost's integers (mm, mm/s, ms, permille). The click range has no setting yet: 0 = no limit.</summary>
        public static PlayerMoveSettings Bake(MovementSettings s) => new PlayerMoveSettings
        {
            AccelMmps2 = Mm(s.acceleration),
            AccelPerSpeed = Mathf.RoundToInt(s.accelerationPerSpeed),
            ArrivalSlowdownMs = Mm(s.arrivalSlowdownTime),
            ArrivalMinRadiusMm = Mm(s.arrivalMinRadius),
            ArrivalMaxRadiusMm = Mm(Mathf.Max(s.arrivalMinRadius, s.arrivalMaxRadius)),
            ArrivalMinPermille = Mm(s.arrivalMinSpeedFactor),
            StopMm = Mm(s.stoppingDistance),
            MaxClickRangeMm = 0,
            TurnCdegPerSecond = Mathf.RoundToInt(s.turnSpeed * 100f),
        };

        /// <summary>Puts the baked settings in <paramref name="world"/> as its one <see cref="PlayerMoveSettings"/> (replaces an older one).</summary>
        public static void InstallSettings(World world, MovementSettings settings)
        {
            if (world == null || !world.IsCreated || settings == null) return;
            EntityManager em = world.EntityManager;
            using var query = em.CreateEntityQuery(ComponentType.ReadWrite<PlayerMoveSettings>());
            Entity holder = query.CalculateEntityCount() > 0 ? query.GetSingletonEntity() : em.CreateEntity(typeof(PlayerMoveSettings));
            em.SetComponentData(holder, Bake(settings));
        }

        /// <summary>Walk and run speed in mm/s from metres a second (run never below walk).</summary>
        public static MoveStats Stats(float walkMps, float runMps) =>
            new MoveStats { WalkMmps = Mm(walkMps), RunMmps = Mm(Mathf.Max(walkMps, runMps)) };

        /// <summary>The ghost's speeds for a server body now; null when the body has no movement skills (the prefab's own then).</summary>
        public static MoveStats? StatsOf(GameObject body)
        {
            var skills = body != null ? body.GetComponent<PlayerMovementSkills>() : null;
            if (skills == null || skills.MovementSettings == null) return null;
            return Stats(skills.SpeedFor(false), skills.SpeedFor(true));
        }

        static int Mm(float metres) => Mathf.RoundToInt(metres * 1000f);
    }

    /// <summary>
    /// Server: the move-click limit of the request limits (<c>RpcBudget</c>, kind Move; the numbers are the server settings' "callLimits"),
    /// counted per player by the <c>PlayerKey</c> the registry holds for the ghost. A ghost without a key (a test world) is not limited.
    /// </summary>
    public sealed class BudgetClickLimit : IClickBudget
    {
        readonly World m_World;

        public BudgetClickLimit(World serverWorld) => m_World = serverWorld;

        public bool Allow(Entity ghost)
        {
            if (!GhostBodyRegistry.TryGetByGhost(m_World, ghost, out GhostBodyEntry entry) || entry.Key == KryticalNova.Core.PlayerKey.None) return true;
            return KryticalNova.Networking.RpcBudget.Spend(entry.Key, KryticalNova.Networking.CallKind.Move, Time.realtimeSinceStartup)
                   == KryticalNova.Networking.RpcBudget.SpendResult.Allowed;
        }
    }

    /// <summary>Server: running costs the Run rank's Flux per destination, paid by the ghost's server body (<see cref="PlayerMovementSkills"/>).</summary>
    public sealed class BodyRunCost : IRunCost
    {
        public const string NoBodyText = "You can't run right now.";
        readonly World m_World;

        public BodyRunCost(World serverWorld) => m_World = serverWorld;

        public bool CanPay(Entity ghost, out string reason)
        {
            PlayerMovementSkills skills = SkillsOf(ghost);
            if (skills == null) { reason = NoBodyText; return false; }
            return skills.CanPayForCommand(MovementSkillId.Run, out reason);
        }

        public void Pay(Entity ghost)
        {
            PlayerMovementSkills skills = SkillsOf(ghost);
            if (skills != null) skills.TryPayForCommand(MovementSkillId.Run, out _, out _);
        }

        PlayerMovementSkills SkillsOf(Entity ghost) =>
            GhostBodyRegistry.TryGetByGhost(m_World, ghost, out GhostBodyEntry entry) && entry.Body != null
                ? entry.Body.GetComponent<PlayerMovementSkills>() : null;
    }

    /// <summary>
    /// Server: keeps each player ghost's <see cref="MoveStats"/> equal to its body's speeds, so a GM speed, a mount, a new Run rank or
    /// Boots reach the owner's prediction (a replicated field) on the next snapshot. Writes only when a number changed.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class PlayerMoveStatsRefreshSystem : SystemBase
    {
        /// <summary>Ghosts whose speeds were rewritten (tests).</summary>
        public int Writes { get; private set; }

        protected override void OnUpdate()
        {
            EntityManager em = EntityManager;
            foreach (GhostBodyEntry entry in GhostBodyRegistry.Entries)
            {
                if (!ReferenceEquals(entry.World, World) || !em.Exists(entry.Ghost) || !em.HasComponent<MoveStats>(entry.Ghost)) continue;
                MoveStats? now = PlayerMoveFromSkills.StatsOf(entry.Body);
                if (!now.HasValue) continue;
                MoveStats was = em.GetComponentData<MoveStats>(entry.Ghost);
                if (was.WalkMmps == now.Value.WalkMmps && was.RunMmps == now.Value.RunMmps) continue;
                em.SetComponentData(entry.Ghost, now.Value);
                Writes++;
            }
        }
    }
}
