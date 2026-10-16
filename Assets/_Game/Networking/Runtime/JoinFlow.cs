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

        /// <summary>True from Enter until the server let the player in or the join failed.</summary>
        public static bool Joining { get; private set; }

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
            Joining = false;
            LastFailure = null;
            Started = null;
            Failed = null;
        }

        /// <summary>Character select's Enter: the world should join <paramref name="server"/> (null: play here).</summary>
        public static void Begin(ServerListing server)
        {
            Target = server != null ? server.Copy() : null;
            Joining = Target != null;
            LastFailure = null;
            if (Joining) Started?.Invoke(Target.name);
        }

        /// <summary>The server let the player in: the loading screen may open.</summary>
        public static void Joined() => Joining = false;

        /// <summary>The join failed: the menus take the player back with <paramref name="reason"/>.</summary>
        public static void Fail(string reason)
        {
            Joining = false;
            Target = null;
            LastFailure = reason;
            Failed?.Invoke(reason);
        }
    }
}
