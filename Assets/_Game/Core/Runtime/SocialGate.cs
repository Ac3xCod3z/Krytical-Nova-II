using System;
using UnityEngine;

namespace KryticalNova.Core
{
    /// <summary>
    /// The one question every player-to-player request asks first: does either of these two refuse to hear from the
    /// other? Whispers, Squad invitations, Callouts, trade, contact and Crew requests all come through here, so a
    /// block list (Contacts) stops all of them by answering in one place. Server only.
    /// </summary>
    public static class SocialGate
    {
        /// <summary>
        /// Says why <c>from</c> may not approach <c>to</c> (shown to <c>from</c>), or null. Set by the system
        /// that keeps block lists; with none set, nobody is refused.
        /// </summary>
        public static Func<Component, Component, string> Check { get; set; }

        // Enter Play Mode runs without a domain reload, so statics must be reset by hand.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Check = null;

        /// <summary>Why <paramref name="from"/> may not approach <paramref name="to"/>, or null. Any component of each player will do.</summary>
        public static string Refusal(Component from, Component to) =>
            Check != null && from != null && to != null ? Check(from, to) : null;
    }
}
