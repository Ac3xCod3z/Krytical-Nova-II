#if KN_DEV_BOTS || UNITY_EDITOR
using System;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using Unity.Networking.Transport;
using KryticalNova.Networking;

namespace KryticalNova.Networking.Entities
{
    /// <summary>
    /// Thin-client bots that join a development server (soak and 2-client tests). This whole file exists only in the Editor and in
    /// a build made with the <c>KN_DEV_BOTS</c> define: a release build has no bot token, no thin-client join system
    /// (<see cref="KnClientApprovalSendSystem"/> is filtered to real clients there) and no way to turn it on.
    /// Even where it exists it is off until <see cref="Enabled"/> is set (server-settings.json <c>devBots</c> or <c>-devbots</c>
    /// for the server; the test or bot launcher for the clients). A bot proves nothing: its account is named by its number.
    /// </summary>
    public static class KnDevBots
    {
        public const string TokenPrefix = "kn-devbot:";

        /// <summary>On = this process creates bot worlds, and a server of this process takes bot tokens. Reset on every Play.</summary>
        public static bool Enabled;

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Enabled = false;

        public static string TokenFor(int number) => TokenPrefix + number.ToString(System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>The save id of bot <paramref name="token"/>, or false when the text is not a bot token.</summary>
        public static bool TryAccount(string token, out string account)
        {
            account = null;
            if (string.IsNullOrEmpty(token) || !token.StartsWith(TokenPrefix, StringComparison.Ordinal)) return false;
            string number = token.Substring(TokenPrefix.Length);
            if (number.Length == 0 || number.Length > 9) return false;
            foreach (char c in number)
                if (c < '0' || c > '9') return false;
            account = AccountIds.FromUnityPlayer("devbot|" + number);
            return true;
        }

        /// <summary>The one place a thin-client world is made: null while <see cref="Enabled"/> is off (the guard the tests pin).</summary>
        public static World CreateWorld() => Enabled ? ClientServerBootstrap.CreateThinClientWorld() : null;

        /// <summary>
        /// Creates a thin-client world that connects to <paramref name="endpoint"/> and joins as bot <paramref name="number"/>.
        /// Null (and nothing created) while <see cref="Enabled"/> is off.
        /// </summary>
        public static World CreateThinClient(NetworkEndpoint endpoint, int number, string build)
        {
            World world = CreateWorld();
            if (world == null) return null;
            KnNetDefaults.Apply(world);
            string join = JoinRules.PayloadText(build, false, TokenFor(number));
            world.EntityManager.CreateSingleton(new KnJoinPayload { Text = new FixedString4096Bytes(join) });
            Entity request = world.EntityManager.CreateEntity(typeof(NetworkStreamRequestConnect));
            world.EntityManager.SetComponentData(request, new NetworkStreamRequestConnect { Endpoint = endpoint });
            return world;
        }
    }
}
#endif
