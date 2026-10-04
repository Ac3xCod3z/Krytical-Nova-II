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
    }

    /// <summary>The chat window. Gameplay hands it finished lines; it never decides who hears what.</summary>
    public interface IChatSink
    {
        void Add(in ChatLine line);
    }
}
