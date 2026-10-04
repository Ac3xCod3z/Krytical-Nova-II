namespace KryticalNova.Factions
{
    /// <summary>Anything that belongs to a faction: players today, faction NPCs later.</summary>
    public interface IFactionMember
    {
        FactionId Faction { get; }
    }
}
