using System;

namespace KryticalNova.Factions
{
    /// <summary>A set of factions, e.g. "who may use this gear". Bit n-1 is the faction with <see cref="FactionId"/> n.</summary>
    [Flags]
    public enum FactionMask : byte
    {
        None = 0,
        Hegemony = 1 << 0,
        Null = 1 << 1,
        Covenant = 1 << 2,
        All = Hegemony | Null | Covenant
    }
}
