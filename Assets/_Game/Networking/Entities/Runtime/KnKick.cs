using System.Collections.Concurrent;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

namespace KryticalNova.Networking.Entities
{
    /// <summary>
    /// Kick requests from the server page (any thread) to the main thread: the page cannot touch the entity world, so it queues
    /// the PlayerKey and <see cref="KnKickSystem"/> closes that player's connection on its next update.
    /// </summary>
    public static class KnKickRequests
    {
        private static readonly ConcurrentQueue<ulong> s_Queue = new ConcurrentQueue<ulong>();
        private static int s_Taken;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Reset()
        {
            while (s_Queue.TryDequeue(out _)) { }
            s_Taken = 0;
        }

        /// <summary>Any thread.</summary>
        public static void Request(ulong playerKey)
        {
            if (playerKey != 0) s_Queue.Enqueue(playerKey);
        }

        /// <summary>Main thread.</summary>
        public static bool TryTake(out ulong playerKey)
        {
            bool got = s_Queue.TryDequeue(out playerKey);
            if (got) s_Taken++;
            return got;
        }

        public static int Pending => s_Queue.Count;
        /// <summary>How many requests the main thread has taken (tests and the log).</summary>
        public static int Taken => s_Taken;
    }

    /// <summary>
    /// Server, main thread: closes the connection of each kicked player. A key that is no longer here (the player left first) is
    /// ignored. The close goes through the normal path, so <see cref="ConnectionClosedSystem"/> saves and cleans up as for any
    /// player who leaves.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    public partial class KnKickSystem : SystemBase
    {
        private EntityQuery m_Players;

        protected override void OnCreate()
        {
            m_Players = GetEntityQuery(ComponentType.ReadOnly<NetworkStreamConnection>(), ComponentType.ReadOnly<ConnectionPlayerKey>(),
                ComponentType.Exclude<NetworkStreamRequestDisconnect>());
        }

        protected override void OnUpdate()
        {
            if (KnKickRequests.Pending == 0) return;
            while (KnKickRequests.TryTake(out ulong key))
            {
                var entities = m_Players.ToEntityArray(Allocator.Temp);
                var keys = m_Players.ToComponentDataArray<ConnectionPlayerKey>(Allocator.Temp);
                for (int i = 0; i < entities.Length; i++)
                {
                    if (keys[i].Key != key) continue;
                    EntityManager.AddComponentData(entities[i], new NetworkStreamRequestDisconnect { Reason = NetworkStreamDisconnectReason.ConnectionClose });
                    Debug.Log("[KnServer] A player was kicked from the server page.");
                    break;
                }
                entities.Dispose();
                keys.Dispose();
            }
        }
    }
}
