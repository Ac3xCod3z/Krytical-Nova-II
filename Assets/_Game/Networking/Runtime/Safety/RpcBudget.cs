using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace KryticalNova.Networking
{
    /// <summary>
    /// The speed limit on every request a game sends its server (online phase 2, milestone 3). Each game has an
    /// allowance per kind of request (<see cref="CallLimits"/>): a burst that refills steadily. A request over it is
    /// ignored and flagged (<see cref="SecurityFlags"/>); a game with too many ignored in a short time is disconnected.
    /// Every server-side request handler starts with <see cref="Allow"/>. Only the server counts: elsewhere every request
    /// is allowed (the server is what matters, and a game never limits itself).
    /// </summary>
    public static class RpcBudget
    {
        private struct Bucket
        {
            public float Tokens;
            public float Stamp;
        }

        private sealed class Client
        {
            public readonly Dictionary<CallKind, Bucket> Buckets = new Dictionary<CallKind, Bucket>();
            public readonly Queue<float> Drops = new Queue<float>();
            public bool Dropped;
        }

        private static readonly Dictionary<ulong, Client> Clients = new Dictionary<ulong, Client>();

        /// <summary>The limits in force (server-settings.json on a server; the defaults otherwise).</summary>
        public static CallLimits Limits { get; private set; } = new CallLimits();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Clients.Clear();
            Limits = new CallLimits();
        }

        /// <summary>A server starting: the limits it was given.</summary>
        public static void Configure(CallLimits limits) => Limits = limits ?? new CallLimits();

        /// <summary>A game left: its allowances go with it.</summary>
        public static void Forget(ulong clientId) => Clients.Remove(clientId);

        /// <summary>
        /// Server: may <paramref name="from"/>'s owner make this request now? False when it is over its allowance: the
        /// request is then ignored by the caller, and flagged here.
        /// </summary>
        public static bool Allow(NetworkBehaviour from, CallKind kind, string call)
        {
            if (from == null || !from.IsServer) return true;
            return Allow(from.OwnerClientId, kind, call, from.NetworkManager);
        }

        /// <summary>Server: the same for a request whose sender is known by its connection.</summary>
        public static bool Allow(ulong clientId, CallKind kind, string call, NetworkManager manager = null)
        {
            manager = manager != null ? manager : NetworkManager.Singleton;
            if (manager == null || !manager.IsServer || clientId == NetworkManager.ServerClientId) return true;

            float now = Time.realtimeSinceStartup;
            if (!Clients.TryGetValue(clientId, out Client client)) Clients[clientId] = client = new Client();
            if (client.Dropped) return false; // being disconnected for flooding

            CallLimit limit = Limits.For(kind);
            if (!client.Buckets.TryGetValue(kind, out Bucket bucket)) bucket = new Bucket { Tokens = limit.burst, Stamp = now };
            bucket.Tokens = Mathf.Min(limit.burst, bucket.Tokens + (now - bucket.Stamp) * limit.perSecond);
            bucket.Stamp = now;
            if (bucket.Tokens >= 1f)
            {
                bucket.Tokens -= 1f;
                client.Buckets[kind] = bucket;
                return true;
            }
            client.Buckets[kind] = bucket;

            // Over the limit: ignored, flagged, and counted towards the flood rule.
            client.Drops.Enqueue(now);
            while (client.Drops.Count > 0 && now - client.Drops.Peek() > Limits.floodSeconds) client.Drops.Dequeue();
            if (client.Drops.Count >= Mathf.Max(1, Limits.floodDrops))
            {
                client.Dropped = true;
                SecurityFlags.Raise(clientId, call, $"Sent {client.Drops.Count} requests over its limits in {Limits.floodSeconds:0} s",
                    "Flooding the server", "Disconnected");
                manager.DisconnectClient(clientId, "Too many requests: your game was disconnected.");
                return false;
            }
            SecurityFlags.Raise(clientId, call, $"Sent {call} faster than allowed", $"Over the {kind} limit ({limit.perSecond:0.#} a second, {limit.burst:0} at once)");
            return false;
        }

        /// <summary>
        /// Server: is <paramref name="text"/> acceptable for <paramref name="call"/> (not too long: <paramref name="longest"/>
        /// characters when given, else the settings' longestText)? False flags it; the caller ignores the request.
        /// </summary>
        public static bool TextFits(NetworkBehaviour from, string call, string text, int longest = 0)
        {
            if (from == null || !from.IsServer) return true;
            // A request with its own limit (settings saved as text) uses it; every other text, the general one.
            int most = longest > 0 ? longest : Limits.longestText;
            if (text == null || text.Length <= most) return true;
            SecurityFlags.Raise(from.OwnerClientId, call, $"Sent {call} with {text.Length} characters of text", $"Longer than {most} characters");
            return false;
        }

        /// <summary>Furthest a position in a request may be from the world's centre, in metres.</summary>
        private const float WorldReach = 100000f;

        /// <summary>
        /// Server: is <paramref name="point"/> a real place (no NaN or infinity, not absurdly far)? False flags it; the
        /// caller ignores the request.
        /// </summary>
        public static bool PointFits(NetworkBehaviour from, string call, Vector3 point)
        {
            if (from == null || !from.IsServer) return true;
            bool real = !float.IsNaN(point.x) && !float.IsNaN(point.y) && !float.IsNaN(point.z)
                && Mathf.Abs(point.x) < WorldReach && Mathf.Abs(point.y) < WorldReach && Mathf.Abs(point.z) < WorldReach;
            if (real) return true;
            SecurityFlags.Raise(from.OwnerClientId, call, $"Sent {call} with an impossible position", "Not a place in the world");
            return false;
        }

        /// <summary>
        /// Server: a request the real game would never send (a target that is not there, an impossible claim, junk
        /// data) is flagged with <paramref name="why"/>; the caller refuses it.
        /// </summary>
        public static void Refuse(NetworkBehaviour from, string call, string what, string why)
        {
            if (from == null || !from.IsServer) return;
            SecurityFlags.Raise(from.OwnerClientId, call, what, why);
        }
    }
}
