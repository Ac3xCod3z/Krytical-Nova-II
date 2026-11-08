using System;
using System.Collections;

namespace KryticalNova.Networking
{
    /// <summary>
    /// The front end's way into a server of the entity netcode (DOTS front end, 8 Nov 2026). Character select, the roster and the join
    /// keep their screens, rules and refusals; only a listing marked <see cref="ServerListing.entities"/> goes through here instead of
    /// Netcode for GameObjects. This layer knows nothing of the entity worlds: the layer that owns them fills both hooks
    /// (<c>KryticalNova.Persistence.Entities.EntityServerJoin</c>). Unfilled = no entity server is ever listed or joined.
    /// </summary>
    public static class EntityServerLink
    {
        /// <summary>
        /// Joins <c>server</c> with the character chosen on the select screen and calls <c>done</c> once: null when this player's
        /// character stands in the world, else the reason in the words the player reads. Run as a coroutine.
        /// </summary>
        public static Func<ServerListing, Action<string>, IEnumerator> Join;

        /// <summary>Asks <c>server</c> a roster question (JSON); <c>done</c> gets the answer's text, or null and the problem.</summary>
        public static Func<ServerListing, string, Action<string, string>, IEnumerator> AskRoster;

        /// <summary>The entity test server on this PC (Builds\EntityServer, the ENTITY TEST lane): never a real-server port.</summary>
        public const int TestPort = 7799;

        public static bool Available => Join != null && AskRoster != null;

        public static bool IsEntityServer(ServerListing server) => server != null && server.entities && Available;

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Join = null;
            AskRoster = null;
        }
    }
}
