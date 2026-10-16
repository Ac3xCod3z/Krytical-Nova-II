namespace KryticalNova.Core
{
    /// <summary>What an announcement to every player is about. Append only: the value goes over the network.</summary>
    public enum AnnouncementKind : byte
    {
        /// <summary>A message from the GM.</summary>
        Gm = 0,
        /// <summary>A player's gear reached the Temper that is announced.</summary>
        Temper = 1,
        /// <summary>A rare piece of loot fell for a player (an Epic drop from a Hostile, a boss, or another source).</summary>
        Drop = 2,
    }

    /// <summary>
    /// Shows news that every player on the server is told, on the HUD's news ticker. The same text is always a chat
    /// line too (the server sends both in one message), so the two cannot differ.
    /// </summary>
    public interface IAnnouncementSink
    {
        /// <param name="sender">Who it is from, when that is said ("GM"); otherwise empty.</param>
        /// <param name="highlight">The names the news is about (a player, a piece of gear), "|" between them, to be lit in the line; or null. For item news the last one is the item.</param>
        /// <param name="rarity">The rarity of the item the news is about (an <c>ItemRarity</c> as a byte): it picks the colours of the line. 0 when there is no item.</param>
        void Announce(AnnouncementKind kind, string sender, string text, string highlight = null, byte rarity = 0);
    }
}
