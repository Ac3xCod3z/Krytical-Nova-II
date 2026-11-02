using UnityEngine;

namespace KryticalNova.Core
{
    /// <summary>
    /// What a server body of the entity netcode says to its own player (a refusal, a "done", a GM's answer): NGO's owner-only message RPCs
    /// never run there (the body is never network-spawned). Filled by the entity netcode, which knows the player's connection (M7/M8
    /// system 0, 2 Nov 2026). Empty, or not a server body: nobody is told here, and the caller shows the line on this machine as before.
    /// </summary>
    public interface IOwnerMessages
    {
        /// <summary>True when <paramref name="text"/> went to the player whose server body <paramref name="part"/> is on.</summary>
        bool Tell(Component part, string text);
    }

    public static class OwnerMessages
    {
        public static IOwnerMessages Current;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Current = null;

        /// <summary>Tells the player of a server body; false when this body has no player to tell here (show it locally then).</summary>
        public static bool TryTell(Component part, string text) => Current != null && !string.IsNullOrEmpty(text) && Current.Tell(part, text);
    }
}
