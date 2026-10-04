namespace KryticalNova.Factions
{
    /// <summary>Anything that belongs to a faction: players today, faction NPCs later.</summary>
    public interface IFactionMember
    {
        FactionId Faction { get; }

        /// <summary>
        /// True while this member and <paramref name="other"/> are in a duel with each other: the one case in which
        /// members who would be friendly may fight.
        /// </summary>
        bool IsDuelling(IFactionMember other);
    }
}
