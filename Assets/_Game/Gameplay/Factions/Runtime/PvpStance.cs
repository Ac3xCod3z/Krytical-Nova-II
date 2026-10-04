namespace KryticalNova.Factions
{
    /// <summary>
    /// How one player stands towards other players. Normally the factions decide; a GM can pin a player to
    /// Friendly (nobody can attack them and they attack no player) or Hostile (they fight every player, their own
    /// faction included), for testing and events.
    /// </summary>
    public enum PvpStance : byte
    {
        /// <summary>Follow the faction rule: hostile to other factions while cross-faction PvP is on.</summary>
        Faction = 0,
        Friendly = 1,
        Hostile = 2
    }
}
