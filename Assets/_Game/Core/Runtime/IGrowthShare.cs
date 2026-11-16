namespace KryticalNova.Core
{
    /// <summary>A companion that grows on a player: their active pet, their selected mount.</summary>
    public enum GrowthShareKind : byte { Pet = 0, Mount = 1 }

    /// <summary>
    /// A player's growing companion, as a reward that is not its own system reaches it (owner, 16 Nov 2026: Crossfire pays the active pet
    /// and mount a share of EXP, and a fully grown one its share in Creds). Implemented by the pet and the mount on the player's body;
    /// systems that pay rewards use this, never the pet or mount assemblies. Server only.
    /// </summary>
    public interface IGrowthShare
    {
        GrowthShareKind Kind { get; }
        /// <summary>There is one out (a pet worn, a mount selected).</summary>
        bool HasActive { get; }
        /// <summary>It is fully grown: EXP would do nothing.</summary>
        bool IsFullyGrown { get; }
        /// <summary>Adds <paramref name="percent"/> % of the EXP that fills its growth meter (its flat "next level").</summary>
        void ServerGrantPercentOfFull(float percent);
    }
}
