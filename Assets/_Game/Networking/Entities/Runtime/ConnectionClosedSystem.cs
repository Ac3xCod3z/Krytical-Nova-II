using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using KryticalNova.Core;
using UnityEngine;

namespace KryticalNova.Networking.Entities
{
    /// <summary>Something that must hear that a player's connection is gone (saves, trade cancel, ghost clean-up). Main thread.</summary>
    public interface IKnConnectionClosedHandler
    {
        void OnConnectionClosed(PlayerKey key);
    }

    /// <summary>
    /// Server: the one place a closed connection is noticed. For each connection that had a <see cref="ConnectionPlayerKey"/> and
    /// is gone: the key is released first (so every async continuation that re-checks <see cref="PlayerKeys.IsAlive"/> sees the
    /// player as gone), then the handlers run, then the body (when a lookup is set) is told through Core's
    /// <see cref="ServerLeaving.Notify"/>. Both hooks are empty today; the roster and the player body plug in at M2b/M3.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateAfter(typeof(ConnectionKeySystem))]
    public partial class ConnectionClosedSystem : SystemBase
    {
        /// <summary>Handlers told of each close, in order.</summary>
        public readonly List<IKnConnectionClosedHandler> Handlers = new List<IKnConnectionClosedHandler>();

        /// <summary>The player's server body (a GameObject with <see cref="IServerLeaving"/> rules), if there is one. Null until the body exists.</summary>
        public Func<PlayerKey, GameObject> BodyOf;

        private EntityQuery m_Closed;

        protected override void OnCreate()
        {
            m_Closed = GetEntityQuery(ComponentType.ReadOnly<ConnectionPlayerKey>(), ComponentType.Exclude<NetworkStreamConnection>());
        }

        protected override void OnUpdate()
        {
            if (m_Closed.IsEmpty) return;
            var keys = m_Closed.ToComponentDataArray<ConnectionPlayerKey>(Allocator.Temp);
            EntityManager.RemoveComponent<ConnectionPlayerKey>(m_Closed);
            for (int i = 0; i < keys.Length; i++)
            {
                PlayerKey key = keys[i].PlayerKey;
                PlayerKeys.Release(keys[i].Serial);
                for (int h = 0; h < Handlers.Count; h++) Handlers[h].OnConnectionClosed(key);
                GameObject body = BodyOf?.Invoke(key);
                if (body != null) ServerLeaving.Notify(body);
            }
            keys.Dispose();
        }
    }
}
