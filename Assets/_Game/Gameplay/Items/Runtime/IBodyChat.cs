using KryticalNova.Core;
using UnityEngine;

namespace KryticalNova.Items
{
    /// <summary>What kind of chat message a server body hands its own player (NGO's three owner RPCs of <c>PlayerChat</c>).</summary>
    public enum BodyChatKind : byte
    {
        /// <summary>A line (NGO's ReceiveRpc / ReceiveLinkedRpc).</summary>
        Line = 1,
        /// <summary>A line and the ticker's toast together (NGO's AnnounceRpc).</summary>
        Announce = 2,
        /// <summary>A hit in the combat log, worded on the owner's machine (NGO's CombatLineRpc).</summary>
        Combat = 3,
    }

    /// <summary>One chat message for a player, as NGO's owner RPCs carried it (only the fields of its kind are used).</summary>
    public struct BodyChatLine
    {
        public BodyChatKind kind;
        public ChatChannel channel;
        public bool outgoing;
        public string sender;
        public string text;
        /// <summary>Announce: the kind, the names lit on the ticker ("|" between them) and the rarity colour.</summary>
        public AnnouncementKind announcement;
        public string highlight;
        public byte rarity;
        /// <summary>Combat: the damage numbers and NGO's flag bits (critical, evaded, reflected, shattered).</summary>
        public float damage, reflected;
        /// <summary>Combat: the elemental part of <see cref="damage"/> (build 17, item 4.1), shown in brackets; 0 = none.</summary>
        public float elemental;
        public byte flags;
        /// <summary>Line: the linked items (at most <see cref="ChatLinks.Max"/>), or null.</summary>
        public ushort[] linkItems;
        public ulong[] linkData;
    }

    /// <summary>
    /// A player body's chat, for the entity netcode (M8 8.4.1): the server body says its owner's lines by NGO's rules and its player gets
    /// what NGO sent their owner; the owner's view shows it. Here, below Social, as <see cref="IBodyCrate"/>.
    /// </summary>
    public interface IBodyChat
    {
        /// <summary>The server body: its owner's line (NGO's SendRpc / SendLinkedRpc body, after the Chat limit).</summary>
        void RunSend(ChatChannel channel, string text, string whisperTarget, byte[] linkContainers, byte[] linkCells);
        /// <summary>The own view: a message the server sent this player.</summary>
        void MirrorLine(in BodyChatLine line);
    }

    /// <summary>Server: how a server body's chat reaches its own player (NGO's owner RPCs).</summary>
    public interface IChatOwnerLink
    {
        /// <summary>True when the message went to the player whose server body <paramref name="part"/> is on.</summary>
        bool Line(Component part, in BodyChatLine line);
    }

    /// <summary>The server's <see cref="IChatOwnerLink"/>; null where there is no entity netcode server.</summary>
    public static class ChatOwnerLink
    {
        public static IChatOwnerLink Current;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Current = null;
    }
}
