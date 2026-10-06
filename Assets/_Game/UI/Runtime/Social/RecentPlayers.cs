using System;
using System.Collections.Generic;
using UnityEngine;

namespace KryticalNova.UI
{
    /// <summary>
    /// The players this machine's player has dealt with since the game started: Squad mates, trade partners, Callout
    /// opponents and whoever whispered. Most recent first. It is a convenience for the Contacts window and is
    /// forgotten when the game closes; nothing here is known to the server.
    /// </summary>
    public static class RecentPlayers
    {
        private const int Capacity = 20;
        private static readonly List<string> Names = new List<string>(Capacity);

        public static IReadOnlyList<string> All => Names;
        public static event Action Changed;

        // Enter Play Mode runs without a domain reload, so statics must be reset by hand.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Names.Clear();
            Changed = null;
        }

        /// <summary>Notes that the local player just dealt with the player called <paramref name="playerName"/>.</summary>
        public static void Note(string playerName)
        {
            if (string.IsNullOrWhiteSpace(playerName)) return;
            int at = Names.FindIndex(n => string.Equals(n, playerName, StringComparison.OrdinalIgnoreCase));
            if (at == 0) return;
            if (at > 0) Names.RemoveAt(at);
            Names.Insert(0, playerName);
            if (Names.Count > Capacity) Names.RemoveAt(Names.Count - 1);
            Changed?.Invoke();
        }
    }
}
