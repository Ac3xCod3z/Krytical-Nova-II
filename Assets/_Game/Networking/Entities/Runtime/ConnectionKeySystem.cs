using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using KryticalNova.Core;
using UnityEngine;

namespace KryticalNova.Networking.Entities
{
    /// <summary>
    /// The server's PlayerKey of a connection, kept on the connection entity. A cleanup component: it outlives the connection
    /// entity by one step, so <see cref="ConnectionClosedSystem"/> sees every close exactly once. Never the raw NetworkId, which
    /// netcode hands out again to the next player.
    /// </summary>
    public struct ConnectionPlayerKey : ICleanupComponentData
    {
        public ulong Key;    // PlayerKey.Value
        public ulong Serial; // the registry id this key was minted for (unique per connection, never a NetworkId)

        public PlayerKey PlayerKey => new PlayerKey(Key);
    }

    /// <summary>
    /// Server: mints a <see cref="PlayerKey"/> (Core's registry, <see cref="PlayerKeys"/>) for each connection when its NetworkId
    /// appears, i.e. right after approval. Once per connection; a later connection gets a new key even if it gets the same NetworkId.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    public partial class ConnectionKeySystem : SystemBase
    {
        // Registry ids never repeat in the process (a restarted server world must not meet an old live key); main thread only.
        private static ulong s_Serial;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => s_Serial = 0;

        private EntityQuery m_New;

        protected override void OnCreate()
        {
            m_New = GetEntityQuery(ComponentType.ReadOnly<NetworkStreamConnection>(), ComponentType.ReadOnly<NetworkId>(),
                ComponentType.Exclude<ConnectionPlayerKey>());
        }

        protected override void OnUpdate()
        {
            if (m_New.IsEmpty) return;
            var connections = m_New.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < connections.Length; i++)
            {
                ulong serial = ++s_Serial;
                PlayerKey key = PlayerKeys.Mint(serial);
                EntityManager.AddComponentData(connections[i], new ConnectionPlayerKey { Key = key.Value, Serial = serial });
            }
            connections.Dispose();
        }

        protected override void OnDestroy()
        {
            // The world goes away with connections still open: their keys die with it.
            using var all = EntityManager.CreateEntityQuery(ComponentType.ReadOnly<ConnectionPlayerKey>());
            var keys = all.ToComponentDataArray<ConnectionPlayerKey>(Allocator.Temp);
            for (int i = 0; i < keys.Length; i++) PlayerKeys.Release(keys[i].Serial);
            keys.Dispose();
        }
    }
}
