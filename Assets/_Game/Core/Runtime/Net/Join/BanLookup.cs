using System;

namespace KryticalNova.Core
{
    /// <summary>
    /// "Is this account banned?" as a joining server asks it. Networking (below the game's systems) owns the ban list
    /// (<c>ServerBans</c>, saved as a world document) and fills <see cref="BanLookup.Current"/>; the Entities join check only asks.
    /// An empty slot means nothing is banned (an EditMode test of the join check alone).
    /// </summary>
    public interface IBanLookup
    {
        /// <summary>True when <paramref name="account"/> is banned now; <paramref name="refusal"/> is what the player reads (reason and time left).</summary>
        bool TryGetBan(string account, DateTime utcNow, out string refusal);
    }

    public static class BanLookup
    {
        public static IBanLookup Current;

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Current = null;
    }
}
