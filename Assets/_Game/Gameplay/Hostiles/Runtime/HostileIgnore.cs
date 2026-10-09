using System;
using System.Collections.Generic;
using UnityEngine;

namespace KryticalNova.Hostiles
{
    /// <summary>
    /// Players no Hostile notices by itself (a GM walking through Hostile Zones). Hitting one still starts a
    /// fight. Kept where the game is decided; the brain reads it.
    /// </summary>
    public static class HostileIgnore
    {
        private static readonly HashSet<Transform> ignored = new HashSet<Transform>();

        /// <summary>Raised when a player starts or stops being ignored: (player, ignored).</summary>
        public static event Action<Transform, bool> Changed;

        // Enter Play Mode runs without a domain reload, so statics must be reset by hand.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            ignored.Clear();
            Changed = null;
        }

        public static bool IsIgnored(Transform player) => player != null && ignored.Contains(player);

        public static void Set(Transform player, bool ignore)
        {
            if (player == null) return;
            if (ignore ? ignored.Add(player) : ignored.Remove(player)) Changed?.Invoke(player, ignore);
        }
    }
}
