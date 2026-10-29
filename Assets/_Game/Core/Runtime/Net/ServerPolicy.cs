using UnityEngine;

namespace KryticalNova.Core
{
    /// <summary>
    /// Server-wide switches that gameplay classes read and the netcode of the day sets (so they never name a connection
    /// manager). Reset for Enter Play Mode without a domain reload.
    /// </summary>
    public static class ServerPolicy
    {
        /// <summary>
        /// A player must carry a proven account to enter (the release servers). Null = the netcode layer did not say, and the
        /// caller keeps asking its own source as before; the entity netcode sets it from the server options.
        /// </summary>
        public static bool? RequireProofOverride;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => RequireProofOverride = null;

        /// <summary>The override when set, else <paramref name="fallback"/> (what the caller's own source says).</summary>
        public static bool RequireProof(bool fallback) => RequireProofOverride ?? fallback;
    }
}
