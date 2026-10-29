using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;

namespace KryticalNova.Networking.Entities.Tests
{
    /// <summary>A join check the test scripts: records what it was asked and answers from <see cref="Rule"/>.</summary>
    public sealed class ScriptedJoinCheck : IKnJoinCheck
    {
        public Func<string, int, KnJoinVerdict> Rule = (payload, poll) => KnJoinVerdict.Accept; // payload, polls so far for this connection
        public readonly List<string> Payloads = new List<string>();
        public int Forgotten;
        private readonly Dictionary<Entity, int> m_Polls = new Dictionary<Entity, int>();

        public KnJoinVerdict Poll(Entity connection, string payload)
        {
            m_Polls.TryGetValue(connection, out int polls);
            m_Polls[connection] = ++polls;
            if (polls == 1) Payloads.Add(payload);
            return Rule(payload, polls);
        }

        public void Forget(Entity connection) => Forgotten++;
    }

    public sealed class RecordingClosedHandler : IKnConnectionClosedHandler
    {
        public readonly List<KryticalNova.Core.PlayerKey> Closed = new List<KryticalNova.Core.PlayerKey>();
        public void OnConnectionClosed(KryticalNova.Core.PlayerKey key) => Closed.Add(key);
    }

    public static class KnTestQueries
    {
        /// <summary>(NetworkId, PlayerKey value) of every approved connection on the server.</summary>
        public static List<(int id, ulong key)> ServerKeys(KnTestWorld tw)
        {
            using var q = tw.Server.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<NetworkId>(), ComponentType.ReadOnly<ConnectionPlayerKey>());
            var ids = q.ToComponentDataArray<NetworkId>(Allocator.Temp);
            var keys = q.ToComponentDataArray<ConnectionPlayerKey>(Allocator.Temp);
            var list = new List<(int, ulong)>();
            for (int i = 0; i < ids.Length; i++) list.Add((ids[i].Value, keys[i].Key));
            ids.Dispose();
            keys.Dispose();
            return list;
        }

        /// <summary>Asks the client world to close its connection (a player leaving).</summary>
        public static void Disconnect(World client)
        {
            using var q = client.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<NetworkStreamConnection>());
            var connections = q.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < connections.Length; i++)
                client.EntityManager.AddComponentData(connections[i], new NetworkStreamRequestDisconnect());
            connections.Dispose();
        }

        /// <summary>A text of the given length that is not one repeated character (so a lost or swapped chunk shows).</summary>
        public static string Pattern(int length) => new string(Enumerable.Range(0, length).Select(i => (char)('a' + i % 26)).ToArray());
    }
}
