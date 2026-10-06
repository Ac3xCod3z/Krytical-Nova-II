namespace KryticalNova.Core
{
    /// <summary>Whose data a <see cref="ISaveParticipant"/> holds.</summary>
    public enum SaveScope
    {
        /// <summary>This character only.</summary>
        Character,
        /// <summary>Shared by every character of the account (the Deep Vault).</summary>
        Account
    }

    /// <summary>
    /// A component on the player that has something to save. The save agent on the same object collects every
    /// participant's document when it saves and hands each its own back when the character enters the world, so a
    /// new system saves itself by implementing this and nothing else changes. Runs where the game is decided
    /// (the server, or offline).
    /// </summary>
    public interface ISaveParticipant
    {
        /// <summary>Stable name of this participant's document inside the save. Never rename it.</summary>
        string SaveKey { get; }

        SaveScope SaveScope { get; }

        /// <summary>This participant's state as JSON, or null to store nothing.</summary>
        string CaptureSave();

        /// <summary>
        /// Puts the state back. <paramref name="json"/> is null when the save has nothing for this participant
        /// (a new character, or a system added after the character was last saved): keep the fresh state then.
        /// </summary>
        void RestoreSave(string json);
    }
}
