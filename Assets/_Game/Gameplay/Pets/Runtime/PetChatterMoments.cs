using System;
using UnityEngine;

namespace KryticalNova.Pets
{
    /// <summary>
    /// Where the game says "this just happened to the player on this machine" for the pet to react to. Most moments
    /// are noticed by the pet's director itself (a death, a critical hit, a level, low HP, standing idle, a rare
    /// item picked up); a system that knows of another one (a Bruiser killed, a rare drop from a kill) raises it
    /// here. Client-side only: nothing is sent.
    /// </summary>
    public static class PetChatterMoments
    {
        public static event Action<PetChatterTrigger> Raised;

        /// <summary>The local player has just had this moment.</summary>
        public static void Raise(PetChatterTrigger trigger) => Raised?.Invoke(trigger);

        // Enter Play Mode runs without a domain reload, so statics must be reset by hand.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() => Raised = null;
    }
}
