namespace KryticalNova.Factions
{
    /// <summary>Anything that belongs to a faction: players today, faction NPCs later.</summary>
    public interface IFactionMember
    {
        FactionId Faction { get; }

        /// <summary>A GM override of the faction rule for this member. <see cref="PvpStance.Faction"/> means none.</summary>
        PvpStance Stance { get; }
    }
}
