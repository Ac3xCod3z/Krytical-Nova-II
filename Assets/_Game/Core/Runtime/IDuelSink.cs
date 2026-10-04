namespace KryticalNova.Core
{
    /// <summary>The duel panels of the HUD: the request the local player was sent, and how their duel ended.</summary>
    public interface IDuelSink
    {
        /// <summary>Another player asks the local player for a duel; the request lapses after <paramref name="seconds"/>.</summary>
        void ShowRequest(string challengerName, float seconds);
        void HideRequest();
        /// <summary>The local player's duel is over: YOU WIN or YOU LOSE.</summary>
        void ShowResult(bool won, string opponentName);
    }
}
