using UnityEngine;

namespace KryticalNova.Hostiles
{
    /// <summary>
    /// The server-wide multipliers on what a Hostile kill pays, and the switch for the death penalty. Events,
    /// Sponsors and faction bonuses will set these when they exist; today only GM commands do. Where the game is
    /// decided only.
    /// </summary>
    public static class HostileRates
    {
        public static float Experience { get; private set; } = 1f;
        public static float Drops { get; private set; } = 1f;
        /// <summary>Off while testing: a death to a Hostile then costs no XP.</summary>
        public static bool DeathPenalty { get; set; } = true;

        // Enter Play Mode runs without a domain reload, so statics must be reset by hand.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Experience = 1f;
            Drops = 1f;
            DeathPenalty = true;
        }

        public static void SetExperience(float multiplier) => Experience = Mathf.Clamp(multiplier, 0f, 1000f);
        public static void SetDrops(float multiplier) => Drops = Mathf.Clamp(multiplier, 0f, 1000f);
    }
}
