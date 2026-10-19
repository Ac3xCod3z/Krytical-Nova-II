using KryticalNova.Core;
using KryticalNova.Networking;
using UnityEngine;

namespace KryticalNova.Social
{
    /// <summary>
    /// Lets code below Social reach the chat on the server: Networking's Announce and safe-stop countdown
    /// (<see cref="ServerHooks.Announce"/>, the GM channel) and the drop announcements (<see cref="ServerChat"/>).
    /// </summary>
    internal static class ServerAnnouncer
    {
#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#endif
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Register()
        {
            ServerHooks.Announce = PlayerChat.ServerBroadcastGm;
            ServerChat.Announce = (kind, text, highlight, rarity) =>
                PlayerChat.ServerAnnounce(kind, ChatChannel.System, string.Empty, text, highlight, rarity);
            ServerChat.SpeakerName = player => player.TryGetComponent(out PlayerChat chat) ? chat.SpeakerName : null;
        }
    }
}
