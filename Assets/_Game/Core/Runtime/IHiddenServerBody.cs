namespace KryticalNova.Core
{
    /// <summary>
    /// A body the entity netcode's server decides with and never shows (M6.4: a Hostile's server body). In Play DOTS here it lives in the
    /// same process as the client's view of it, so whatever a player sees or picks (nameplates, minimap, Autopilot, the pointer, spirits)
    /// leaves it out and takes the view. NGO never has one.
    /// </summary>
    public interface IHiddenServerBody
    {
        bool IsHiddenServerBody { get; }
    }
}
