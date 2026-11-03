namespace KryticalNova.CharacterCreation
{
    /// <summary>
    /// Something that wears a character's look without being that character (a Kiosk stand's doll; M7.6): the entity netcode reads the look
    /// on the server and shows it on a client's view. Here because the look (<see cref="CharacterRecipe"/>) lives here.
    /// </summary>
    public interface ILookCarrier
    {
        bool HasLook { get; }
        CharacterRecipe Look { get; }
        void ShowLook(in CharacterRecipe look);
    }
}
