using System;
using UnityEngine;

namespace KryticalNova.Networking
{
    /// <summary>
    /// Entering the world on a server (online step 4, owner: Enter joins the last server). Character select names the
    /// server (<see cref="Begin"/>); the world joins it under the loading screen (<see cref="NetworkConnectionManager"/>)
    /// and says how it went (<see cref="Joined"/> or <see cref="Fail"/>). The menus listen: the loading screen waits
    /// while <see cref="Joining"/>, and a failure takes the player back to character select with the reason.
    /// </summary>
    public static class JoinFlow
    {
        /// <summary>The server to join on entering the world, or null to play here (the Editor hosts, a test build plays offline).</summary>
        public static ServerListing Target { get; private set; }

        /// <summary>
        /// True from Enter until the server let the player in or the join failed, and, while a world holds the door
        /// (<see cref="HoldUntilWorldReady"/>), until that world is ready to be seen. The loading screen waits on it.
        /// </summary>
        public static bool Joining => s_Joining || HoldingForWorld;

        /// <summary>A world asked to be ready before the loading screen opens, and is not yet.</summary>
        public static bool HoldingForWorld
        {
            get
            {
                if (s_WorldReady == null) return false;
                bool ready;
                try { ready = s_WorldReady(); }
                catch (Exception e) { Debug.LogException(e); ready = true; }   // a broken check must not lock the player out
                if (ready) s_WorldReady = null;
                return !ready;
            }
        }

        static bool s_Joining;
        static Func<bool> s_WorldReady;

        /// <summary>Why the last join failed, for character select to say; cleared once read.</summary>
        public static string LastFailure { get; set; }

        /// <summary>Raised when a join starts (the server's name).</summary>
        public static event Action<string> Started;

        /// <summary>Raised when a join failed (the reason, in the words to show).</summary>
        public static event Action<string> Failed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Target = null;
            s_Joining = false;
            s_WorldReady = null;
            LastFailure = null;
            Started = null;
            Failed = null;
        }

        /// <summary>Character select's Enter: the world should join <paramref name="server"/> (null: play here).</summary>
        public static void Begin(ServerListing server)
        {
            Target = server != null ? server.Copy() : null;
            s_Joining = Target != null;
            LastFailure = null;
            if (s_Joining) Started?.Invoke(Target.name);
        }

        /// <summary>The server let the player in: the loading screen may open (once a world holding the door is ready too).</summary>
        public static void Joined() => s_Joining = false;

        /// <summary>
        /// A world that must be ready before it is shown (the entity netcode's client: the player's body made, the camera on it,
        /// steady frames) holds the door until <paramref name="ready"/> says yes once; filled by the layer that owns the body, so
        /// this layer knows nothing of it. A newer hold replaces an older one.
        /// </summary>
        public static void HoldUntilWorldReady(Func<bool> ready) => s_WorldReady = ready;

        /// <summary>The join failed: the menus take the player back with <paramref name="reason"/>.</summary>
        public static void Fail(string reason)
        {
            s_Joining = false;
            s_WorldReady = null;
            Target = null;
            LastFailure = reason;
            Failed?.Invoke(reason);
        }
    }
}
