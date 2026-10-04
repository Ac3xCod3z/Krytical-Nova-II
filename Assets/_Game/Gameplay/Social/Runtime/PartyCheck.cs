namespace KryticalNova.Social
{
    /// <summary>Whether a player may join a party, and if not, why.</summary>
    public enum PartyCheck : byte
    {
        Allowed = 0,
        /// <summary>Parties are for one faction only.</summary>
        OtherFaction,
        /// <summary>More than <see cref="PartyRules.MaxLevelGap"/> levels from one of the members.</summary>
        LevelTooFar,
        Full,
    }
}
