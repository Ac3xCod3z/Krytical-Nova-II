namespace KryticalNova.Core
{
    /// <summary>The party invitation panel of the HUD.</summary>
    public interface IPartySink
    {
        /// <summary>Another player invites the local player to a party; the invitation lapses after <paramref name="seconds"/>.</summary>
        void ShowInvite(string inviterName, float seconds);
        void HideInvite();
    }
}
