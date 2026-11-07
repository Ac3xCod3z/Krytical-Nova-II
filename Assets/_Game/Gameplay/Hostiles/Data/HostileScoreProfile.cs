namespace KryticalNova.Hostiles
{
    /// <summary>
    /// How a species picks its next attack and when it presses in (the entity brain, M6.1). Append only: the values are saved in
    /// assets. The roster's picks are a proposal for the owner's hand test (implementation plan M6.1).
    /// </summary>
    public enum HostileScoreProfile : byte
    {
        /// <summary>Stands and trades blows: the plain weighted dice of its attacks.</summary>
        Brute = 0,
        /// <summary>Darts in and out: favours its quick attacks.</summary>
        Skirmisher = 1,
        /// <summary>Saves its heavy attack for a weakened target.</summary>
        Finisher = 2
    }
}
