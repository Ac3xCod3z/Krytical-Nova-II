using UnityEngine;

namespace KryticalNova.Core
{
    /// <summary>The zone music's shared state (Audio's <c>MusicState</c>, ~14 bytes), as the network carries it (M7.2).</summary>
    public struct MusicSync
    {
        public double Epoch;
        public short Forced;
        public float Nudge;

        public bool Same(in MusicSync other) => Epoch.Equals(other.Epoch) && Forced == other.Forced && Nudge.Equals(other.Nudge);
    }

    /// <summary>
    /// The scene's music conductor, for the entity netcode (M7.2, 2 Nov 2026): the server reads what plays, a client takes the server's. Audio
    /// (Presentation) fills it; the network sits below and only sees this (the layer fix of the M7 rules table, section 4).
    /// </summary>
    public interface IMusicStateLink
    {
        bool TryRead(out MusicSync state);
        void Apply(in MusicSync state);
    }

    public static class MusicStateLink
    {
        public static IMusicStateLink Current;

        /// <summary>A client of the entity netcode with no server in its process: the music is the server's, a GM there changes it.</summary>
        public static bool RemoteAuthority;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Current = null;
            RemoteAuthority = false;
        }
    }
}
