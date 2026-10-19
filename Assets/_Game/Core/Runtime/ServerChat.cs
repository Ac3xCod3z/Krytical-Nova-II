using System;
using UnityEngine;

namespace KryticalNova.Core
{
    /// <summary>
    /// The chat as systems below Social reach it on the server: a line every player reads (a rare drop found), and the
    /// name a player speaks with. Social fills it when the game loads (<c>PlayerChat</c> does the work, as before).
    /// Empty (an EditMode test without Social), an announcement is simply not made.
    /// </summary>
    public static class ServerChat
    {
        /// <summary>Tells every player a line in the System channel: (kind, text, highlight, rarity). Filled by Social.</summary>
        public static Action<AnnouncementKind, string, string, byte> Announce;

        /// <summary>The name a player speaks with in chat. Filled by Social.</summary>
        public static Func<GameObject, string> SpeakerName;

        /// <summary>The player's chat name, else the object's name.</summary>
        public static string NameOf(GameObject player)
        {
            string name = player != null && SpeakerName != null ? SpeakerName(player) : null;
            return name ?? (player != null ? player.name : string.Empty);
        }
    }
}
