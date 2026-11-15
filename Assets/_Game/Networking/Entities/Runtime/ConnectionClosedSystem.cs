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
    /// <see cref="ServerLeaving.Notify"/>, then the account is forgotten.
    /// At most <see cref="MaxLeavesPerTick"/> closes are handled in one tick (perf round 3, R3-7: 200 at once cost a 490-530 ms
    /// frame); the rest wait in the order they closed, each still whole (its <see cref="ConnectionPlayerKey"/> is a cleanup
    /// component, so the gone connection's entity stays until its turn). A queued player whose account joins again is handled in
    /// that same tick, before the new connection can load the character.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateAfter(typeof(ConnectionKeySystem))]
    public partial class ConnectionClosedSystem : SystemBase
    {
        /// <summary>Handlers told of each close, in order.</summary>
        public readonly List<IKnConnectionClosedHandler> Handlers = new List<IKnConnectionClosedHandler>();

        /// <summary>The player's server body (a GameObject with <see cref="IServerLeaving"/> rules), if there is one. Null until the body exists.</summary>
        public Func<PlayerKey, GameObject> BodyOf;

        /// <summary>The most closes handled in one tick; 0 or less = all at once (the server settings' <c>leavesPerTick</c>).</summary>
        public int MaxLeavesPerTick = KnNetDefaults.LeavesPerTick;

        /// <summary>Closes seen but not handled yet (tests, the status page).</summary>
        public int Queued => m_Queue.Count;

        private EntityQuery m_Closed;
        private EntityQuery m_Live;
        // Registry serials of closed connections in the order they were first seen (main thread only).
        private readonly List<ulong> m_Queue = new List<ulong>();
        private readonly HashSet<ulong> m_InQueue = new HashSet<ulong>();
        private readonly Dictionary<ulong, int> m_IndexOf = new Dictionary<ulong, int>();
        private readonly HashSet<string> m_Rejoining = new HashSet<string>();
        private readonly List<ulong> m_Picked = new List<ulong>();

        protected override void OnCreate()
        {
            m_Closed = GetEntityQuery(ComponentType.ReadOnly<ConnectionPlayerKey>(), ComponentType.Exclude<NetworkStreamConnection>());
            m_Live = GetEntityQuery(ComponentType.ReadOnly<ConnectionAccount>(), ComponentType.ReadOnly<NetworkStreamConnection>());
        }

        protected override void OnUpdate()
        {
            if (m_Closed.IsEmpty)
            {
                if (m_Queue.Count > 0) { m_Queue.Clear(); m_InQueue.Clear(); }
                return;
            }
            var entities = m_Closed.ToEntityArray(Allocator.Temp);
            var keys = m_Closed.ToComponentDataArray<ConnectionPlayerKey>(Allocator.Temp);

            // the queue: what was waiting keeps its place, what closed this tick goes behind it
            m_IndexOf.Clear();
            for (int i = 0; i < keys.Length; i++)
            {
                m_IndexOf[keys[i].Serial] = i;
                if (m_InQueue.Add(keys[i].Serial)) m_Queue.Add(keys[i].Serial);
            }
            for (int q = m_Queue.Count - 1; q >= 0; q--) // gone some other way (never expected): forgotten
                if (!m_IndexOf.ContainsKey(m_Queue[q])) { m_InQueue.Remove(m_Queue[q]); m_Queue.RemoveAt(q); }

            int cap = MaxLeavesPerTick > 0 ? MaxLeavesPerTick : int.MaxValue;
            m_Picked.Clear();
            if (m_Queue.Count <= cap) m_Picked.AddRange(m_Queue);
            else
            {
                FillRejoining();
                for (int q = 0; q < m_Queue.Count; q++)
                {
                    if (m_Picked.Count < cap || (m_Rejoining.Count > 0 && Rejoins(keys[m_IndexOf[m_Queue[q]]].PlayerKey)))
                        m_Picked.Add(m_Queue[q]);
                }
            }

            // as before: every handled close loses its key component first, then each is handled in order
            var handled = new NativeArray<Entity>(m_Picked.Count, Allocator.Temp);
            for (int p = 0; p < m_Picked.Count; p++) handled[p] = entities[m_IndexOf[m_Picked[p]]];
            EntityManager.RemoveComponent<ConnectionPlayerKey>(handled);
            for (int p = 0; p < m_Picked.Count; p++)
            {
                ConnectionPlayerKey k = keys[m_IndexOf[m_Picked[p]]];
                PlayerKey key = k.PlayerKey;
                PlayerKeys.Release(k.Serial);
                for (int h = 0; h < Handlers.Count; h++) Handlers[h].OnConnectionClosed(key);
                GameObject body = BodyOf?.Invoke(key);
                if (body != null) ServerLeaving.Notify(body);
                ServerAccounts.Forget(key); // last: the handlers and the body above still read the account
                m_InQueue.Remove(k.Serial);
            }
            if (m_Picked.Count == m_Queue.Count) m_Queue.Clear();
            else m_Queue.RemoveAll(serial => !m_InQueue.Contains(serial));
            if (m_Queue.Count > 0) Debug.Log($"[KnServer] {m_Picked.Count} players left this tick, {m_Queue.Count} queued.");
            handled.Dispose();
            entities.Dispose();
            keys.Dispose();
        }

        // The accounts of the connections that are open now (only read while closes are waiting).
        private void FillRejoining()
        {
            m_Rejoining.Clear();
            if (m_Live.IsEmpty) return;
            var live = m_Live.ToComponentDataArray<ConnectionAccount>(Allocator.Temp);
            for (int i = 0; i < live.Length; i++)
                if (live[i].Account.Length > 0) m_Rejoining.Add(live[i].Account.ToString());
            live.Dispose();
        }

        private bool Rejoins(PlayerKey key) =>
            ServerAccounts.TryGet(key, out ServerAccounts.Entry entry) && !string.IsNullOrEmpty(entry.Account) && m_Rejoining.Contains(entry.Account);
    }
}
