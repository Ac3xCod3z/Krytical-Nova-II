using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;

namespace KryticalNova.Networking.Entities
{
    /// <summary>
    /// Relevancy on a server world (wave 4, M9): who gets bandwidth first (importance, here) and who is sent to whom at all (the
    /// fill). One switch for both, <see cref="RelevancySettings.enabled"/>: off leaves the world exactly as it was before relevancy.
    /// </summary>
    public static class RelevancySetup
    {
        /// <summary>
        /// Installs relevancy into <paramref name="server"/> from <paramref name="settings"/> (null = the asset in Resources) and
        /// says what it did in the log. False: it is off (switched off, or the asset is missing, which is an error line).
        /// </summary>
        public static bool Install(World server, RelevancySettings settings = null)
        {
            settings ??= RelevancySettings.Load();
            if (settings == null)
            {
                Debug.LogError("[Relevancy] off: Resources/" + RelevancySettings.ResourcePath + " is missing, so every client gets every player and monster.");
                return false;
            }
            if (!settings.enabled)
            {
                Debug.Log("[Relevancy] off (the settings asset): every client gets every player and monster.");
                return false;
            }
            InstallImportance(server, settings);
            Debug.Log($"[Relevancy] importance on: tiles of {settings.importanceTileSize} m around each player are sent first.");
            return true;
        }

        /// <summary>
        /// The package's distance importance (Netcode for Entities 6.6.0, manual "Importance scaling"): the server sorts its ghosts
        /// into tiles and sends the tiles around a connection's player at full rate, farther ones less often. The plain scale
        /// function, NOT the "with relevancy" one: who is sent at all is the fill's decision alone.
        /// </summary>
        public static void InstallImportance(World server, RelevancySettings settings)
        {
            EntityManager em = server.EntityManager;
            using (EntityQuery q = em.CreateEntityQuery(ComponentType.ReadOnly<GhostImportance>()))
                if (!q.IsEmpty) return;
            int tile = math.max(1, settings.importanceTileSize);
            Entity e = em.CreateSingleton(new GhostDistanceData
            {
                TileSize = new int3(tile, tile, tile),
                TileCenter = int3.zero,
                TileBorderWidth = new float3(1f, 1f, 1f),
            });
            em.AddComponentData(e, new GhostImportance
            {
                BatchScaleImportanceFunction = GhostDistanceImportance.BatchScaleFunctionPointer,
                GhostConnectionComponentType = ComponentType.ReadOnly<GhostConnectionPosition>(),
                GhostImportanceDataType = ComponentType.ReadOnly<GhostDistanceData>(),
                GhostImportancePerChunkDataType = ComponentType.ReadOnly<GhostDistancePartitionShared>(),
            });
        }
    }

    /// <summary>
    /// Server, every tick, only while importance is installed: each in-game connection's place for the importance scaling is where
    /// the ghost it owns stands (the player: the one ghost kind with an owner). A connection with no body yet keeps its last place
    /// (the origin at first), which only orders what it is sent, never what it may see.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [BurstCompile]
    public partial struct GhostConnectionPositionSystem : ISystem
    {
        EntityQuery m_Bare;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<GhostImportance>();
            m_Bare = SystemAPI.QueryBuilder().WithAll<NetworkId, NetworkStreamInGame>().WithNone<GhostConnectionPosition>().Build();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (!m_Bare.IsEmpty) state.EntityManager.AddComponent<GhostConnectionPosition>(m_Bare);

            var places = new NativeHashMap<int, float3>(64, state.WorldUpdateAllocator);
            foreach (var (owner, transform) in SystemAPI.Query<RefRO<GhostOwner>, RefRO<LocalTransform>>())
                places.TryAdd(owner.ValueRO.NetworkId, transform.ValueRO.Position);
            foreach (var (id, place) in SystemAPI.Query<RefRO<NetworkId>, RefRW<GhostConnectionPosition>>())
                if (places.TryGetValue(id.ValueRO.Value, out float3 at)) place.ValueRW.Position = at;
        }
    }
}
