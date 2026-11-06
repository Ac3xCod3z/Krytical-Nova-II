using UnityEngine;

namespace KryticalNova.Networking
{
    /// <summary>
    /// True while the entity netcode runs this Play session (the Editor's "Play DOTS here", plan section 50). Then the Netcode for
    /// GameObjects side stays off: <see cref="NetworkConnectionManager"/> neither hosts nor joins (no port 7777, no player from the
    /// local profile), and <see cref="OfflinePlayerHandoff"/> keeps the scene's offline avatar hidden, so the clicks and the camera
    /// go to the entity netcode's own body. Set before the scene loads; reset when Play starts.
    /// </summary>
    public static class EntityNetcodeSession
    {
        public static bool Owns { get; set; }

        /// <summary>
        /// Set only by "Play DOTS here (test saves)" (A2): Log Out and Exit call it before their own save, so the server bodies leave the
        /// normal way (saved, then let go) and the worlds stop before character select. Null everywhere else: leaving is unchanged.
        /// </summary>
        public static System.Action StopWorlds { get; set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Owns = false;
            StopWorlds = null;
        }
    }
}
