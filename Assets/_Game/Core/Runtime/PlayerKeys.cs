using System.Collections.Generic;
using UnityEngine;

namespace KryticalNova.Core
{
    /// <summary>
    /// The server's list of live <see cref="PlayerKey"/>s: one is minted per connection and maps both ways to that
    /// connection's transport id (an opaque number here; the Networking adapter <c>NetPlayerKeys</c> is the only code that
    /// knows it is Netcode's client id). Keys count up from 1 and are never reused, also after a release.
    /// Main thread only.
    /// </summary>
    public static class PlayerKeys
    {
        private static readonly Dictionary<ulong, PlayerKey> ByConnection = new Dictionary<ulong, PlayerKey>();
        private static readonly Dictionary<PlayerKey, ulong> ByKey = new Dictionary<PlayerKey, ulong>();
        private static ulong last;

        /// <summary>Number of live keys.</summary>
        public static int Count => ByKey.Count;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Reset();

        /// <summary>Forgets every key and restarts the count (tests, and a fresh Play session without domain reload).</summary>
        public static void Reset()
        {
            ByConnection.Clear();
            ByKey.Clear();
            last = 0;
        }

        /// <summary>The key of this connection, minting a new one the first time it is asked for.</summary>
        public static PlayerKey Mint(ulong connection)
        {
            if (ByConnection.TryGetValue(connection, out PlayerKey existing)) return existing;
            PlayerKey key = new PlayerKey(++last);
            ByConnection[connection] = key;
            ByKey[key] = connection;
            return key;
        }

        /// <summary>The key of this connection, or <see cref="PlayerKey.None"/> when it has none (never mints).</summary>
        public static PlayerKey Find(ulong connection) => ByConnection.TryGetValue(connection, out PlayerKey key) ? key : PlayerKey.None;

        /// <summary>The connection a live key belongs to.</summary>
        public static bool TryGetConnection(PlayerKey key, out ulong connection) => ByKey.TryGetValue(key, out connection);

        /// <summary>True while the key's connection lives; false once released (a stale capture after an async gap).</summary>
        public static bool IsAlive(PlayerKey key) => key.IsValid && ByKey.ContainsKey(key);

        /// <summary>The connection is gone: its key dies and is never given out again.</summary>
        public static void Release(ulong connection)
        {
            if (!ByConnection.TryGetValue(connection, out PlayerKey key)) return;
            ByConnection.Remove(connection);
            ByKey.Remove(key);
        }
    }
}
