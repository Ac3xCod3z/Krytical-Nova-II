namespace KryticalNova.Hostiles
{
    /// <summary>How strong a Hostile is for its level. Append only: the values are saved in assets.</summary>
    public enum HostileTier : byte
    {
        Normal = 0,
        /// <summary>A field champion: one per Hostile Zone at most, far tougher, better rewards.</summary>
        Bruiser = 1,
        /// <summary>Kept for the boss system. No Hostile Zone accepts it.</summary>
        BossReserved = 2
    }
}
