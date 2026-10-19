using KryticalNova.Networking;
using UnityEngine;

namespace KryticalNova.Social
{
    /// <summary>
    /// Lets the server's own code (the control page's Announce, the countdown of a safe stop) say a line to every player
    /// through the GM channel (<see cref="ServerHooks.Announce"/>), without Networking knowing the chat.
    /// </summary>
    internal static class ServerAnnouncer
    {
#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#endif
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Register() => ServerHooks.Announce = PlayerChat.ServerBroadcastGm;
    }
}
