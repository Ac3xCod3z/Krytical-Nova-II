namespace KryticalNova.CharacterCreation
{
    /// <summary>
    /// Where saved characters live. Today that is PlayerPrefs on this machine; a server-backed store replaces it
    /// later without the menus or the player code changing.
    /// </summary>
    public interface ICharacterStore
    {
        /// <summary>Never returns null: a missing or unreadable save yields an empty roster.</summary>
        CharacterRoster Load();
        void Save(CharacterRoster roster);
        void DeleteAll();
    }
}
