using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using KryticalNova.Core;
using UnityEngine;

namespace KryticalNova.Networking.Entities
{
    /// <summary>
    /// Rule code on a server body asks to drop its player (<see cref="NetRole.DisconnectOwner"/>); the request waits here until
    /// <see cref="KnPlayerRefusalSystem"/> turns it into a refusal message and a disconnect. One entry per key, a dead key is
    /// ignored. Main thread only; reset for Enter Play Mode without a domain reload.
    /// </summary>
    public static class KnPlayerRefusals
    {
        private static readonly List<KeyValuePair<PlayerKey, string>> Pending = new List<KeyValuePair<PlayerKey, string>>();

        public static int Count => Pending.Count;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Clear();

        public static void Clear() => Pending.Clear();

        /// <summary>Queues the refusal; false when the key is not alive or one is already queued for it.</summary>
        public static bool Enqueue(PlayerKey key, string reason)
        {
            if (!PlayerKeys.IsAlive(key)) return false;
            for (int i = 0; i < Pending.Count; i++)
                if (Pending[i].Key == key) return false;
            Pending.Add(new KeyValuePair<PlayerKey, string>(key, reason ?? string.Empty));
            return true;
        }

        /// <summary>Takes the oldest request whose player is still alive (dead ones are dropped).</summary>
        public static bool TryDequeue(out PlayerKey key, out string reason)
        {
            while (Pending.Count > 0)
            {
                KeyValuePair<PlayerKey, string> first = Pending[0];
                Pending.RemoveAt(0);
                if (!PlayerKeys.IsAlive(first.Key)) continue;
                key = first.Key;
                reason = first.Value;
                return true;
            }
            key = PlayerKey.None;
            reason = null;
            return false;
        }
    }

    /// <summary>Server: sends the <see cref="JoinRefusedRpc"/> of each queued refusal and closes that connection a few ticks later.</summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    public partial class KnPlayerRefusalSystem : SystemBase
    {
        /// <summary>Ticks between the refusal message and the disconnect (the message must have left).</summary>
        public const int DisconnectTicks = 5;

        private EntityQuery m_Connections;

        protected override void OnCreate()
        {
            m_Connections = GetEntityQuery(ComponentType.ReadOnly<ConnectionPlayerKey>(), ComponentType.ReadOnly<NetworkId>());
        }

        protected override void OnUpdate()
        {
            if (KnPlayerRefusals.Count == 0) return;
            while (KnPlayerRefusals.TryDequeue(out PlayerKey key, out string reason))
            {
                Entity connection = FindConnection(key);
                if (connection == Entity.Null) continue;
                Entity rpc = EntityManager.CreateEntity();
                EntityManager.AddComponentData(rpc, new JoinRefusedRpc { Reason = (byte)KnRefuseReason.Refused, Detail = KnFixed.Text512(reason) });
                EntityManager.AddComponentData(rpc, new SendRpcCommandRequest { TargetConnection = connection });
                if (!EntityManager.HasComponent<KnPendingDisconnect>(connection))
                    EntityManager.AddComponentData(connection, new KnPendingDisconnect { TicksLeft = DisconnectTicks });
            }
        }

        private Entity FindConnection(PlayerKey key)
        {
            var entities = m_Connections.ToEntityArray(Allocator.Temp);
            var keys = m_Connections.ToComponentDataArray<ConnectionPlayerKey>(Allocator.Temp);
            Entity found = Entity.Null;
            for (int i = 0; i < keys.Length; i++)
                if (keys[i].Key == key.Value) { found = entities[i]; break; }
            entities.Dispose();
            keys.Dispose();
            return found;
        }
    }
}
