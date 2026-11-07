using System;
using KryticalNova.Brains;
using KryticalNova.Networking.Entities;
using KryticalNova.Player.Entities.Presentation;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using UnityEngine;

namespace KryticalNova.Hostiles.Entities
{
    using World = Unity.Entities.World;   // inside the namespace: KryticalNova.World (a namespace) would win otherwise

    /// <summary>
    /// Puts the Hostiles' entity brains into a server world (M5.5d): the Brain and mind settings as singletons, the NavWorld path backend,
    /// the server bodies in place of the species prefabs (<see cref="HostilePool.BodyOf"/>), this process deciding the Hostiles
    /// (<see cref="HostileSpawner.AuthorityOverride"/>) and the brain's player lookups. OFF until the clients can see Hostiles (M5.5e):
    /// a test turns it on with <see cref="Enabled"/>, a run with <c>KN_DOTS_HOSTILES=1</c>. Never touches a client world.
    /// </summary>
    public static class HostileEntitiesSetup
    {
        public const string EnableVariable = "KN_DOTS_HOSTILES";
        const int DefaultTickRate = 30;

        /// <summary>Install on every new server world (null: from <see cref="EnableVariable"/>).</summary>
        public static bool? Enabled;

        static bool On => Enabled ?? Environment.GetEnvironmentVariable(EnableVariable) == "1";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Hook()
        {
            Enabled = null;
            KnWorldHooks.ServerWorldMade -= InstallIfOn;
            KnWorldHooks.ServerWorldMade += InstallIfOn;
        }

        static void InstallIfOn(World server)
        {
            if (On) InstallServer(server);
        }

        /// <summary>Installs into <paramref name="server"/>; false (and a log line) when the assets are missing.</summary>
        public static bool InstallServer(World server, HostileWorldAssets assets = null)
        {
            assets ??= Resources.Load<HostileWorldAssets>(HostileWorldAssets.ResourcePath);
            if (assets == null || assets.brain == null || assets.hostiles == null)
            {
                Debug.LogError("[Hostiles] The entity brains are not installed: " + HostileWorldAssets.ResourcePath + " is missing or empty (run Krytical Nova ▸ Hostiles ▸ Build DOTS Assets).");
                return false;
            }
            EntityManager em = server.EntityManager;
            int tickRate = DefaultTickRate;
            using (var q = em.CreateEntityQuery(typeof(ClientServerTickRate)))
                if (q.TryGetSingleton(out ClientServerTickRate rate) && rate.SimulationTickRate > 0) tickRate = rate.SimulationTickRate;
            em.CreateSingleton(BrainConfig.From(assets.brain, tickRate));
            em.CreateSingleton(HostileBrainConfig.From(assets.hostiles, tickRate));
            em.CreateSingleton(new PathBackend { Kind = PathBackendKind.NavWorld, AgentTypeId = 0, Extents = new float3(1f, 2f, 1f) });

            HostileSpawner.AuthorityOverride = () => true;
            if (assets.bodies != null) HostilePool.BodyOf = species => assets.bodies.BodyOf(species);
            EcsHostileBrain.BodyOfGhost = ghost => BodyOf(server, ghost);
            EcsHostileBrain.GhostIdOfBody = body => GhostIdOf(server, body);
            HostileZone.PlayersOverride = players => ServerPlayers(server, players);   // M6.2: zones wake for this world's players
            Debug.Log("[Hostiles] Entity brains installed in " + server.Name + " (" + tickRate + " Hz, server bodies " + (assets.bodies != null ? "on" : "MISSING") + ").");
            return true;
        }

        /// <summary>The server bodies of the players of <paramref name="world"/> (what NGO's zones read as connected players).</summary>
        static void ServerPlayers(World world, System.Collections.Generic.List<Transform> players)
        {
            foreach (GhostBodyEntry e in GhostBodyRegistry.Entries)
                if (ReferenceEquals(e.World, world) && e.Body != null) players.Add(e.Body.transform);
        }

        /// <summary>A player's server body by its ghost (GhostBodyRegistry), or an attackable non-player's.</summary>
        static Transform BodyOf(World world, Entity ghost)
        {
            foreach (GhostBodyEntry e in GhostBodyRegistry.Entries)
                if (ReferenceEquals(e.World, world) && e.Ghost == ghost && e.Body != null) return e.Body.transform;
            return null;
        }

        /// <summary>The ghost id of a player's server body (0 when it is not one, or its ghost is gone).</summary>
        static int GhostIdOf(World world, Transform body)
        {
            if (body == null || !world.IsCreated) return 0;
            EntityManager em = world.EntityManager;
            foreach (GhostBodyEntry e in GhostBodyRegistry.Entries)
                if (ReferenceEquals(e.World, world) && e.Body != null && e.Body.transform == body && em.Exists(e.Ghost) && em.HasComponent<GhostInstance>(e.Ghost))
                    return em.GetComponentData<GhostInstance>(e.Ghost).ghostId;
            return 0;
        }
    }
}
