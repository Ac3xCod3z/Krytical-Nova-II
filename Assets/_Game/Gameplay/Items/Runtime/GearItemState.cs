using System;

namespace KryticalNova.Items
{
    /// <summary>
    /// What one piece of ladder gear carries of its own: its Temper %, Fusion stage and Tuning stage. Packed into the
    /// low bits of <see cref="ItemStack.data"/>, so it goes wherever the item goes (Pack, slot, Vault, a trade, a
    /// Kiosk, the ground) and is saved with it. An untouched piece is all zeroes. The bits above are left for sockets.
    /// Only ladder gear reads its data this way: a pet's data is a <c>PetItemState</c>.
    /// </summary>
    public readonly struct GearItemState : IEquatable<GearItemState>
    {
        public const int MaxTemper = 150;
        public const int MaxFusion = 12;
        public const int MaxTune = 25;

        private const int FusionShift = 8, TuneShift = 12;
        private const ulong TemperMask = 0xFF, FusionMask = 0xF, TuneMask = 0x1F;
        /// <summary>The bits this state owns: everything else in the item's data is kept as it is.</summary>
        private const ulong OwnedBits = TemperMask | (FusionMask << FusionShift) | (TuneMask << TuneShift);

        public readonly byte Temper;
        public readonly byte Fusion;
        public readonly byte Tune;

        public GearItemState(int temper, int fusion, int tune)
        {
            Temper = (byte)Math.Clamp(temper, 0, MaxTemper);
            Fusion = (byte)Math.Clamp(fusion, 0, MaxFusion);
            Tune = (byte)Math.Clamp(tune, 0, MaxTune);
        }

        public static GearItemState Unpack(ulong data) => new GearItemState(
            (int)(data & TemperMask), (int)((data >> FusionShift) & FusionMask), (int)((data >> TuneShift) & TuneMask));

        public static GearItemState Of(in ItemStack stack) => Unpack(stack.data);

        /// <summary>This state written over <paramref name="data"/>, keeping the bits it does not own.</summary>
        public ulong PackInto(ulong data) =>
            (data & ~OwnedBits) | Temper | ((ulong)Fusion << FusionShift) | ((ulong)Tune << TuneShift);

        public ulong Pack() => PackInto(0);

        /// <summary>Never tempered, fused or tuned: the only kind of piece that can be fed to a Fusion or reforged.</summary>
        public bool IsClean => Temper == 0 && Fusion == 0 && Tune == 0;

        public GearItemState WithTemper(int temper) => new GearItemState(temper, Fusion, Tune);
        public GearItemState WithFusion(int fusion) => new GearItemState(Temper, fusion, Tune);
        public GearItemState WithTune(int tune) => new GearItemState(Temper, Fusion, tune);

        public bool Equals(GearItemState other) => Temper == other.Temper && Fusion == other.Fusion && Tune == other.Tune;
        public override bool Equals(object obj) => obj is GearItemState other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Temper, Fusion, Tune);

        /// <summary>The suffix shown after an item's name: "+36%  FS3  T7", or nothing for an untouched piece.</summary>
        public override string ToString()
        {
            if (IsClean) return string.Empty;
            string text = Temper > 0 ? $"+{Temper}%" : string.Empty;
            if (Fusion > 0) text += (text.Length > 0 ? "  " : string.Empty) + $"FS{Fusion}";
            if (Tune > 0) text += (text.Length > 0 ? "  " : string.Empty) + $"T{Tune}";
            return text;
        }
    }
}
