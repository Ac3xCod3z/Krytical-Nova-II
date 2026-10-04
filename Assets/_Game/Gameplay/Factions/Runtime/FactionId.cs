namespace KryticalNova.Factions
{
    /// <summary>The playable factions. The numeric value is saved and sent over the network: never renumber, only append.</summary>
    public enum FactionId : byte
    {
        None = 0,
        Hegemony = 1,
        Null = 2,
        Covenant = 3
    }
}
