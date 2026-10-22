namespace KryticalNova.Core
{
    /// <summary>Where a chat line belongs. System and Combat are written by the game, never by a player.</summary>
    public enum ChatChannel : byte
    {
        /// <summary>Local shout: heard by players near the speaker.</summary>
        General = 0,
        /// <summary>Everyone of the speaker's faction on this server, wherever they are.</summary>
        Faction = 1,
        /// <summary>Everyone in the speaker's guild.</summary>
        Guild = 2,
        /// <summary>One player to another.</summary>
        Whisper = 3,
        /// <summary>Game master broadcast: every player of every faction. Only the host can send.</summary>
        Gm = 4,
        /// <summary>Notices from the game to this player (errors, duel requests, countdowns).</summary>
        System = 5,
        /// <summary>This player's own combat: damage dealt and taken, evades and misses.</summary>
        Combat = 6,
        /// <summary>Everyone in the speaker's party, wherever they are.</summary>
        Party = 7,
    }

    /// <summary>One line for the chat window.</summary>
    public struct ChatLine
    {
        public ChatChannel Channel;
        /// <summary>Who said it. For a whisper this player sent, the player it went to. Empty for System and Combat.</summary>
        public string Sender;
        public string Text;
        /// <summary>True for a whisper this player sent (shown as "To Name").</summary>
        public bool Outgoing;
        /// <summary>
        /// The items linked in the line (owner, 22 Oct 2026: share an item into chat), or null. Link <c>i</c> sits in
        /// <see cref="Text"/> where <see cref="ChatLinks.Marker"/>(i) is; the server put each one there from the
        /// sender's own Pack or worn gear, so a link is always a real item with its own state.
        /// </summary>
        public ushort[] LinkItems;
        /// <summary>Each linked item's own state (dye, Temper and the rest), beside <see cref="LinkItems"/>.</summary>
        public ulong[] LinkData;

        public bool HasLinks => LinkItems != null && LinkItems.Length > 0;
    }

    /// <summary>
    /// Where an item link sits in a chat line: one private-use character per link, never typed and never shown. A
    /// line keeps at most <see cref="Max"/> links; the server strips any marker it did not put there itself.
    /// </summary>
    public static class ChatLinks
    {
        public const int Max = 3;
        private const char First = '\uE000';

        public static char Marker(int index) => (char)(First + index);

        /// <summary>The link a character stands for, or -1 when it is ordinary text.</summary>
        public static int IndexOf(char c) => c >= First && c < First + Max ? c - First : -1;

        /// <summary>The text with every link marker taken out.</summary>
        public static string Strip(string text)
        {
            if (string.IsNullOrEmpty(text)) return text ?? string.Empty;
            var kept = new System.Text.StringBuilder(text.Length);
            foreach (char c in text) if (IndexOf(c) < 0) kept.Append(c);
            return kept.ToString();
        }
    }

    /// <summary>The chat window. Gameplay hands it finished lines; it never decides who hears what.</summary>
    public interface IChatSink
    {
        void Add(in ChatLine line);
    }
}
