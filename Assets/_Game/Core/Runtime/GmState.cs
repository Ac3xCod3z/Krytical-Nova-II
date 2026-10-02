namespace KryticalNova.Core
{
    /// <summary>
    /// Development / GM toggles read by gameplay code. Set from the Quantum Console (KryticalNova.DevTools.GmCommands).
    /// Process-wide and not networked: on a host they affect the host's own player (the commands target only that player).
    /// TODO(GM): move to a per-player, permission-checked component when the GM mod gets real accounts.
    /// </summary>
    public static class GmState
    {
        /// <summary>Player takes no damage.</summary>
        public static bool GodMode;

        /// <summary>Chi spends always succeed and never drain the pool.</summary>
        public static bool InfiniteChi;

        /// <summary>Combat skills ignore cooldowns.</summary>
        public static bool NoCooldowns;

        /// <summary>Multiplier applied to the player's walk/run speed.</summary>
        public static float SpeedMultiplier = 1f;

        // Statics survive Play Mode when domain reload is disabled; start every session with GM toggles off.
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ResetAll()
        {
            GodMode = false;
            InfiniteChi = false;
            NoCooldowns = false;
            SpeedMultiplier = 1f;
        }
    }
}
