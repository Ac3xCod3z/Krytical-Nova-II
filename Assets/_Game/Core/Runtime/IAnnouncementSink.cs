namespace KryticalNova.Core
{
    /// <summary>What an announcement to every player is about. Append only: the value goes over the network.</summary>
    public enum AnnouncementKind : byte
    {
        /// <summary>A message from the GM.</summary>
        Gm = 0,
        /// <summary>A player's gear reached the Temper that is announced.</summary>
        Temper = 1,
    }

    /// <summary>
    /// Shows news that every player on the server is told as a toast. The same text is always a chat line too
    /// (the server sends both in one message), so the two cannot differ.
    /// </summary>
    public interface IAnnouncementSink
    {
        /// <param name="sender">Who it is from, when that is said ("GM"); otherwise empty.</param>
        void Announce(AnnouncementKind kind, string sender, string text);
    }
}
