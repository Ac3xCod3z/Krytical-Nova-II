using System;

namespace KryticalNova.Items
{
    /// <summary>
    /// What one piece of ladder gear carries of its own: its Temper %, Fusion stage, Tuning stage and the colourway
    /// it is dyed in. Packed into the low 25 bits of <see cref="ItemStack.data"/>, so it goes wherever the item goes
    /// (Pack, slot, Vault, a trade, a Kiosk, the ground) and is saved with it. A piece never worked on and never
    /// dyed is all zeroes. The bits above are left for sockets.
    /// Only ladder gear reads its data this way: a pet's data is a <c>PetItemState</c>.
    /// </summary>
    public readonly struct GearItemState : IEquatable<GearItemState>
    {
        public const int MaxTemper = 150;
        public const int MaxFusion = 12;
        public const int MaxTune = 25;
        /// <summary>The highest colourway id a piece can store (a <c>GearThemeLibrary</c> id; 0 = its own colours).</summary>
        public const int MaxDye = 255;

        private const int FusionShift = 8, TuneShift = 12, DyeShift = 17;
        private const ulong TemperMask = 0xFF, FusionMask = 0xF, TuneMask = 0x1F, DyeMask = 0xFF;
        /// <summary>The bits this state owns: everything else in the item's data is kept as it is.</summary>
        private const ulong OwnedBits = TemperMask | (FusionMask << FusionShift) | (TuneMask << TuneShift) | (DyeMask << DyeShift);

        public readonly byte Temper;
        public readonly byte Fusion;
        public readonly byte Tune;
        /// <summary>The colourway the piece is dyed in, or 0. A look only: it gives nothing and takes nothing.</summary>
        public readonly byte Dye;

        public GearItemState(int temper, int fusion, int tune, int dye = 0)
        {
            Temper = (byte)Math.Clamp(temper, 0, MaxTemper);
            Fusion = (byte)Math.Clamp(fusion, 0, MaxFusion);
            Tune = (byte)Math.Clamp(tune, 0, MaxTune);
            Dye = (byte)Math.Clamp(dye, 0, MaxDye);
        }

        public static GearItemState Unpack(ulong data) => new GearItemState(
            (int)(data & TemperMask), (int)((data >> FusionShift) & FusionMask), (int)((data >> TuneShift) & TuneMask),
            (int)((data >> DyeShift) & DyeMask));

        public static GearItemState Of(in ItemStack stack) => Unpack(stack.data);

        /// <summary>This state written over <paramref name="data"/>, keeping the bits it does not own.</summary>
        public ulong PackInto(ulong data) =>
            (data & ~OwnedBits) | Temper | ((ulong)Fusion << FusionShift) | ((ulong)Tune << TuneShift) | ((ulong)Dye << DyeShift);

        public ulong Pack() => PackInto(0);

        /// <summary>
        /// Never tempered, fused or tuned: the only kind of piece that can be fed to a Fusion or reforged. A dye
        /// does not count: it is a colour, not work done on the piece.
        /// </summary>
        public bool IsClean => Temper == 0 && Fusion == 0 && Tune == 0;

        public GearItemState WithTemper(int temper) => new GearItemState(temper, Fusion, Tune, Dye);
        public GearItemState WithFusion(int fusion) => new GearItemState(Temper, fusion, Tune, Dye);
        public GearItemState WithTune(int tune) => new GearItemState(Temper, Fusion, tune, Dye);
        public GearItemState WithDye(int dye) => new GearItemState(Temper, Fusion, Tune, dye);

        public bool Equals(GearItemState other) => Temper == other.Temper && Fusion == other.Fusion && Tune == other.Tune && Dye == other.Dye;
        public override bool Equals(object obj) => obj is GearItemState other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Temper, Fusion, Tune, Dye);

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
