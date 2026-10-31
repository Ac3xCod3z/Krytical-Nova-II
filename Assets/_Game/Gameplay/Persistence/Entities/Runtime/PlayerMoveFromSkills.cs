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

        /// <summary>The ghost's learned Run and Jump ranks for a server body now; null when the body has no movement skills.</summary>
        public static MoveRanks? RanksOf(GameObject body)
        {
            var skills = body != null ? body.GetComponent<PlayerMovementSkills>() : null;
            if (skills == null) return null;
            return new MoveRanks { Run = (byte)Mathf.Clamp(skills.RunRank, 0, byte.MaxValue), Jump = (byte)Mathf.Clamp(skills.JumpRank, 0, byte.MaxValue) };
        }

        /// <summary>The ghost's Flux for a server body now (thousandths); null when the body has no resources.</summary>
        public static OwnerFlux? FluxOf(GameObject body)
        {
            var resources = body != null ? body.GetComponent<PlayerResources>() : null;
            if (resources == null) return null;
            return new OwnerFlux { Milli = Mm(resources.Chi), MaxMilli = Mm(resources.MaxChi) };
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
    /// Boots reach the owner's prediction (a replicated field) on the next snapshot. Writes only when a number changed. The learned
    /// ranks go the same way into <see cref="MoveRanks"/> and the Flux into <see cref="OwnerFlux"/> (both owner only), for the owner's
    /// own jump plan and cost check.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class PlayerMoveStatsRefreshSystem : SystemBase
    {
        /// <summary>Ghosts whose speeds were rewritten (tests).</summary>
        public int Writes { get; private set; }
        /// <summary>Ghosts whose ranks were rewritten (tests).</summary>
        public int RankWrites { get; private set; }
        /// <summary>Ghosts whose Flux was rewritten (tests).</summary>
        public int FluxWrites { get; private set; }

        protected override void OnUpdate()
        {
            EntityManager em = EntityManager;
            foreach (GhostBodyEntry entry in GhostBodyRegistry.Entries)
            {
                if (!ReferenceEquals(entry.World, World) || !em.Exists(entry.Ghost)) continue;
                if (em.HasComponent<MoveRanks>(entry.Ghost))
                {
                    MoveRanks? ranks = PlayerMoveFromSkills.RanksOf(entry.Body);
                    MoveRanks had = em.GetComponentData<MoveRanks>(entry.Ghost);
                    if (ranks.HasValue && (had.Run != ranks.Value.Run || had.Jump != ranks.Value.Jump))
                    {
                        em.SetComponentData(entry.Ghost, ranks.Value);
                        RankWrites++;
                    }
                }
                if (em.HasComponent<OwnerFlux>(entry.Ghost))
                {
                    OwnerFlux? flux = PlayerMoveFromSkills.FluxOf(entry.Body);
                    OwnerFlux hadFlux = em.GetComponentData<OwnerFlux>(entry.Ghost);
                    if (flux.HasValue && (hadFlux.Milli != flux.Value.Milli || hadFlux.MaxMilli != flux.Value.MaxMilli))
                    {
                        em.SetComponentData(entry.Ghost, flux.Value);
                        FluxWrites++;
                    }
                }
                if (!em.HasComponent<MoveStats>(entry.Ghost)) continue;
                MoveStats? now = PlayerMoveFromSkills.StatsOf(entry.Body);
                if (!now.HasValue) continue;
                MoveStats was = em.GetComponentData<MoveStats>(entry.Ghost);
                if (was.WalkMmps == now.Value.WalkMmps && was.RunMmps == now.Value.RunMmps) continue;
                em.SetComponentData(entry.Ghost, now.Value);
                Writes++;
            }
        }
    }

    /// <summary>
    /// Client: the own body's <see cref="PlayerMovementSkills"/> takes the ranks the server sent (<see cref="MoveRanks"/>, owner only),
    /// so the owner's jump plan and the click's check use the same rules and messages as the server (GUI run 4, 31 Oct: without
    /// them the owner's planner said "Learn Jump first." and the jump waited for the server); its <see cref="PlayerResources"/> takes
    /// the Flux (<see cref="OwnerFlux"/>), so a jump it cannot pay for is refused at once, never started and taken back. Other
    /// players' bodies keep none.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class PlayerMoveRanksMirrorSystem : SystemBase
    {
        /// <summary>Times the own body's ranks were set (tests).</summary>
        public int Mirrored { get; private set; }

        protected override void OnUpdate()
        {
            EntityManager em = EntityManager;
            foreach (GhostBodyEntry entry in GhostBodyRegistry.Entries)
            {
                if (!entry.IsLocal || !ReferenceEquals(entry.World, World) || entry.Body == null) continue;
                if (!em.Exists(entry.Ghost)) continue;
                var skills = entry.Body.GetComponent<PlayerMovementSkills>();
                if (skills != null && em.HasComponent<MoveRanks>(entry.Ghost))
                {
                    MoveRanks ranks = em.GetComponentData<MoveRanks>(entry.Ghost);
                    if (skills.RunRank != ranks.Run || skills.JumpRank != ranks.Jump)
                    {
                        skills.MirrorRanks(ranks.Run, ranks.Jump);
                        Mirrored++;
                    }
                }
                var resources = entry.Body.GetComponent<PlayerResources>();
                // MaxMilli 0 = no snapshot yet: the body keeps its own numbers until the server's first Flux arrives
                if (resources != null && em.HasComponent<OwnerFlux>(entry.Ghost))
                {
                    OwnerFlux flux = em.GetComponentData<OwnerFlux>(entry.Ghost);
                    if (flux.MaxMilli > 0) resources.MirrorChi(flux.Milli * 0.001f, flux.MaxMilli * 0.001f);
                }
            }
        }
    }
}
