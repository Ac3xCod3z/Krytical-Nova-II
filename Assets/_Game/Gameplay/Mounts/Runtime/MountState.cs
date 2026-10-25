using System;

namespace KryticalNova.Mounts
{
    /// <summary>
    /// What one of a character's mounts carries of its own: the EXP that fills its growth meter and how hungry it is.
    /// A mount just registered is all zeroes: no EXP and a full belly. Whole numbers only, so nothing drifts: EXP in
    /// hundredths (a kill is worth 0.04), hunger as how far below full it is, in thousandths of a percent.
    /// </summary>
    public readonly struct MountState : IEquatable<MountState>
    {
        /// <summary>EXP units per point of EXP.</summary>
        public const uint XpUnitsPerXp = 100;
        /// <summary>Hunger units in a full belly: 100 % in thousandths of a percent.</summary>
        public const uint FullHungerUnits = 100_000;

        /// <summary>EXP earned, in hundredths.</summary>
        public readonly uint XpUnits;
        /// <summary>How far below full the hunger meter is, in thousandths of a percent. 0 = full.</summary>
        public readonly uint HungerDeficit;

        public MountState(uint xpUnits, uint hungerDeficit)
        {
            XpUnits = xpUnits;
            HungerDeficit = Math.Min(hungerDeficit, FullHungerUnits);
        }

        /// <summary>EXP as the design counts it (100,000 fills the meter).</summary>
        public float Xp => XpUnits / (float)XpUnitsPerXp;
        /// <summary>Hunger from 100 (full) to 0 (starving).</summary>
        public float HungerPercent => (FullHungerUnits - HungerDeficit) * 100f / FullHungerUnits;
        /// <summary>False at 0 % hunger: no stats, no EXP and its slowest speed until it is fed.</summary>
        public bool IsFed => HungerDeficit < FullHungerUnits;

        public MountState WithXp(uint xpUnits) => new MountState(xpUnits, HungerDeficit);
        public MountState WithHungerDeficit(uint deficit) => new MountState(XpUnits, deficit);

        public bool Equals(MountState other) => XpUnits == other.XpUnits && HungerDeficit == other.HungerDeficit;
        public override bool Equals(object obj) => obj is MountState other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(XpUnits, HungerDeficit);
        public override string ToString() => $"{Xp:0.##} EXP, {HungerPercent:0.###}% hunger";
    }
}
