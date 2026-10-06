using System;
using KryticalNova.Items;

namespace KryticalNova.Pets
{
    /// <summary>
    /// What one pet item carries of its own: the XP that fills its growth meter and how hungry it is. Packed into
    /// <see cref="ItemStack.data"/>, so it goes wherever the item goes (Pack, slot, Vault, a trade, a Kiosk).
    /// A pet that has never been touched is all zeroes: no XP and a full belly.
    /// Whole numbers only, so nothing drifts: XP in hundredths (a kill is worth 0.04), hunger as how far below
    /// full it is, in thousandths of a percent.
    /// </summary>
    public readonly struct PetItemState : IEquatable<PetItemState>
    {
        /// <summary>XP units per point of XP.</summary>
        public const uint XpUnitsPerXp = 100;
        /// <summary>Hunger units in a full belly: 100 % in thousandths of a percent.</summary>
        public const uint FullHungerUnits = 100_000;
        private const int HungerShift = 32;

        /// <summary>XP earned, in hundredths.</summary>
        public readonly uint XpUnits;
        /// <summary>How far below full the hunger meter is, in thousandths of a percent. 0 = full.</summary>
        public readonly uint HungerDeficit;

        public PetItemState(uint xpUnits, uint hungerDeficit)
        {
            XpUnits = xpUnits;
            HungerDeficit = Math.Min(hungerDeficit, FullHungerUnits);
        }

        public static PetItemState Unpack(ulong data) => new PetItemState((uint)(data & uint.MaxValue), (uint)(data >> HungerShift));
        public static PetItemState Of(in ItemStack stack) => Unpack(stack.data);
        public ulong Pack() => XpUnits | ((ulong)HungerDeficit << HungerShift);

        /// <summary>XP as the design counts it (100,000 fills the meter).</summary>
        public float Xp => XpUnits / (float)XpUnitsPerXp;
        /// <summary>Hunger from 100 (full) to 0 (starving).</summary>
        public float HungerPercent => (FullHungerUnits - HungerDeficit) * 100f / FullHungerUnits;
        /// <summary>False at 0 % hunger: the pet gives no stat and earns no XP until it is fed.</summary>
        public bool IsFed => HungerDeficit < FullHungerUnits;

        public PetItemState WithXp(uint xpUnits) => new PetItemState(xpUnits, HungerDeficit);
        public PetItemState WithHungerDeficit(uint deficit) => new PetItemState(XpUnits, deficit);

        public bool Equals(PetItemState other) => XpUnits == other.XpUnits && HungerDeficit == other.HungerDeficit;
        public override bool Equals(object obj) => obj is PetItemState other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(XpUnits, HungerDeficit);
        public override string ToString() => $"{Xp:0.##} XP, {HungerPercent:0.###}% hunger";
    }
}
