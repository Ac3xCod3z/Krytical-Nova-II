namespace KryticalNova.Core
{
    /// <summary>
    /// A component on the player that gives a brand-new character something once (build 17, item 2.4: the starting Creds). The save
    /// agent calls it where the game is decided, right after the first load found no save for the character and before its first
    /// save, so the gift is saved with it and an old character never gets it again.
    /// </summary>
    public interface INewCharacterGrant
    {
        void ServerGrantNewCharacter();
    }
}
