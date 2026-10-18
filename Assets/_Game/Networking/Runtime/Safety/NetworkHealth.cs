using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace KryticalNova.Networking
{
    /// <summary>What a late or missed request tells about a connection, never about cheating (owner, 18 Oct 2026).</summary>
    public enum LagEvent : byte
    {
        /// <summary>A hit report that came after its swing had closed.</summary>
        LateHit,
        /// <summary>A hit report on a target that had died or gone a moment before.</summary>
        GoneTarget,
    }

    /// <summary>
    /// Lag, kept apart from the cheat flags (owner, 18 Oct 2026: "maybe we should track lag/deaths for performance
    /// reasons"). Requests that arrive too late to count are normal with any delay and are only ignored, but many of them
    /// from one player means a bad connection, and many from everyone at once a server that is struggling. The control
    /// page shows each player's ping and their late hits of the last five minutes; the server's performance log has the
    /// totals per minute beside its own load.
    /// </summary>
    public static class NetworkHealth
    {
        public const float WindowSeconds = 300f;

        private static readonly Dictionary<ulong, Queue<(float time, LagEvent kind)>> Events = new Dictionary<ulong, Queue<(float, LagEvent)>>();
        private static int lateSinceTaken;
        private static int goneSinceTaken;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Events.Clear();
            lateSinceTaken = goneSinceTaken = 0;
        }

        /// <summary>Server: a request from <paramref name="clientId"/> came too late to count.</summary>
        public static void Record(ulong clientId, LagEvent kind)
        {
            if (!Events.TryGetValue(clientId, out var queue)) Events[clientId] = queue = new Queue<(float, LagEvent)>();
            float now = Time.realtimeSinceStartup;
            queue.Enqueue((now, kind));
            Trim(queue, now);
            if (kind == LagEvent.LateHit) lateSinceTaken++;
            else goneSinceTaken++;
        }

        /// <summary>A player left: their events go with them.</summary>
        public static void Forget(ulong clientId) => Events.Remove(clientId);

        /// <summary>How many of <paramref name="kind"/> from <paramref name="clientId"/> in the last five minutes.</summary>
        public static int Count(ulong clientId, LagEvent kind)
        {
            if (!Events.TryGetValue(clientId, out var queue)) return 0;
            Trim(queue, Time.realtimeSinceStartup);
            int count = 0;
            foreach (var e in queue)
                if (e.kind == kind) count++;
            return count;
        }

        /// <summary>The player's round trip to the server in milliseconds, or -1 when unknown.</summary>
        public static int PingMs(ulong clientId)
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null || !manager.IsServer || manager.NetworkConfig.NetworkTransport == null) return -1;
            try { return (int)manager.NetworkConfig.NetworkTransport.GetCurrentRtt(clientId); }
            catch (System.Exception) { return -1; }
        }

        /// <summary>For the performance log: late hits and gone targets since the last call, and the worst ping now.</summary>
        public static void TakeTotals(out int lateHits, out int goneTargets, out int worstPingMs)
        {
            lateHits = lateSinceTaken;
            goneTargets = goneSinceTaken;
            lateSinceTaken = goneSinceTaken = 0;
            worstPingMs = 0;
            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null || !manager.IsServer) return;
            foreach (ulong id in manager.ConnectedClientsIds)
                if (id != NetworkManager.ServerClientId) worstPingMs = Mathf.Max(worstPingMs, PingMs(id));
        }

        private static void Trim(Queue<(float time, LagEvent kind)> queue, float now)
        {
            while (queue.Count > 0 && now - queue.Peek().time > WindowSeconds) queue.Dequeue();
        }
    }
}
