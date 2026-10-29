using KryticalNova.Core;

namespace KryticalNova.Networking
{
    /// <summary>
    /// The one place that turns Netcode for GameObjects' client id (<c>OwnerClientId</c>) into a <see cref="PlayerKey"/>
    /// and back (DOTS migration seam). Everything else asks for a key here and keeps the key.
    /// </summary>
    public static class NetPlayerKeys
    {
        /// <summary>The key of this client, minted when first asked for (a connection about to be proven or admitted).</summary>
        public static PlayerKey Of(ulong clientId) => PlayerKeys.Mint(clientId);

        /// <summary>The key of this client, or None when it has none; never mints, so a lookup cannot leak a key.</summary>
        public static PlayerKey Find(ulong clientId) => PlayerKeys.Find(clientId);

        /// <summary>The NGO client id behind a live key.</summary>
        public static bool TryGetClientId(PlayerKey key, out ulong clientId) => PlayerKeys.TryGetConnection(key, out clientId);

        /// <summary>The connection ended or was turned away: its key dies.</summary>
        public static void Release(ulong clientId) => PlayerKeys.Release(clientId);
    }
}
