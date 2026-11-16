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
    /// (<see cref="HostileSpawner.AuthorityOverride"/>) and the brain's player lookups. ON in a built server, OFF in the Editor
    /// (<see cref="IsOn"/>): a test turns it on with <see cref="Enabled"/>, a run with <c>KN_DOTS_HOSTILES=1</c> (<c>0</c> switches a build off). Never touches a client world.
    /// </summary>
    public static class HostileEntitiesSetup
    {
        public const string EnableVariable = "KN_DOTS_HOSTILES";
        const int DefaultTickRate = 30;

        /// <summary>Install on every new server world (null: from <see cref="EnableVariable"/>).</summary>
        public static bool? Enabled;

        // A built server has its monsters by default (cutover, 12 Nov 2026: build 3 had none, so no zone woke and every gm.hostile.spawn
        // failed); KN_DOTS_HOSTILES=0 switches them off there. The Editor keeps them off unless KN_DOTS_HOSTILES=1, so tests stay as they were.
        static bool On => Enabled ?? IsOn(Application.isEditor, Environment.GetEnvironmentVariable(EnableVariable));

        /// <summary>The switch: <paramref name="variable"/> "1" or "0" decides; unset, a built server is on and the Editor off.</summary>
        public static bool IsOn(bool editor, string variable) => variable == "1" || (variable != "0" && !editor);

        // After SubsystemRegistration, where KnWorldHooks clears its hooks, and before the worlds are made (as PlayerWorldsSetup): at
        // SubsystemRegistration the order between the two was not fixed, so the clear could come after and the brains never installed
        // (7 Nov 2026 hand test: KN_DOTS_HOSTILES=1 gave no [Hostiles] line).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
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
            if (assets.bodies != null) HostileSpawner.SpeciesOverride = () => SpeciesOf(assets.bodies);   // M6.3: gm.hostile.spawn / list
            s_Bodies = assets.bodies;
            Debug.Log("[Hostiles] Entity brains installed in " + server.Name + " (" + tickRate + " Hz, server bodies " + (assets.bodies != null ? "on" : "MISSING") + ").");
            return true;
        }

        // The server bodies this process installed (null: none, or the NGO game); checked once the first scene is in.
        static HostileBodyCatalog s_Bodies;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetBodies() => s_Bodies = null;

        /// <summary>
        /// After the first scene (live bug, 15 Nov 2026): a built server made its worlds, and so set <see cref="HostilePool.BodyOf"/>, at
        /// BeforeSceneLoad, where another startup hook could still wipe it; every zone then spawned bodies with no entity brain and no
        /// Hostile ghost (no monster seen or targeted on build 17). Says in the log that the hook held, or sets it again and says it was lost.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void CheckBodiesAfterStartup()
        {
            if (s_Bodies == null) return;
            Debug.Log(CheckBodies(s_Bodies));
        }

        /// <summary>The check itself (tests): sets <see cref="HostilePool.BodyOf"/> again when it was lost; the log line.</summary>
        public static string CheckBodies(HostileBodyCatalog bodies)
        {
            if (HostilePool.BodyOf != null) return "[Hostiles] After startup: the server bodies hook held (zones spawn bodies with entity brains).";
            HostileBodyCatalog catalog = bodies;
            HostilePool.BodyOf = species => catalog.BodyOf(species);
            return "[Hostiles] After startup: the server bodies hook was LOST (a startup reset ran after the world was made); set again. " +
                   "Bodies spawned before this line have no entity brain.";
        }

        /// <summary>The species prefabs of the server body catalog (what NGO's network prefab list gives the GM commands).</summary>
        static System.Collections.Generic.IEnumerable<HostileNet> SpeciesOf(HostileBodyCatalog catalog)
        {
            foreach (HostileBodyCatalog.Entry e in catalog.entries)
                if (e.species != null) yield return e.species;
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
