namespace KryticalNova.Items
{
    /// <summary>Equipment slots. The numeric value indexes <see cref="EquipmentLoadout"/>; None means "not equippable".</summary>
    public enum EquipSlot : byte
    {
        None = 0,
        Weapon = 1,
        Armor = 2,
        Gloves = 3,
        Boots = 4,
        Ring = 5,
        Amulet = 6,
        Cape = 7,
        Pet = 8
    }

    public enum ItemRarity : byte
    {
        Common,
        Uncommon,
        Rare,
        Epic,
        Legendary
    }

    /// <summary>
    /// Mirrors Sidekick's part codes (the NN in SK_..._NNCODE_HU01) so item data can name body regions without
    /// referencing the Sidekick assembly or its SQLite database.
    /// </summary>
    public enum BodyRegion : byte
    {
        None = 0,
        Head = 1,
        Hair = 2,
        EyebrowLeft = 3,
        EyebrowRight = 4,
        EyeLeft = 5,
        EyeRight = 6,
        EarLeft = 7,
        EarRight = 8,
        FacialHair = 9,
        Torso = 10,
        ArmUpperLeft = 11,
        ArmUpperRight = 12,
        ArmLowerLeft = 13,
        ArmLowerRight = 14,
        HandLeft = 15,
        HandRight = 16,
        Hips = 17,
        LegLeft = 18,
        LegRight = 19,
        FootLeft = 20,
        FootRight = 21,
        AttachmentHead = 22,
        AttachmentFace = 23,
        AttachmentBack = 24,
        AttachmentHipsFront = 25,
        AttachmentHipsBack = 26,
        AttachmentHipsLeft = 27,
        AttachmentHipsRight = 28,
        AttachmentShoulderLeft = 29,
        AttachmentShoulderRight = 30,
        AttachmentElbowLeft = 31,
        AttachmentElbowRight = 32,
        AttachmentKneeLeft = 33,
        AttachmentKneeRight = 34,
        Nose = 35,
        Teeth = 36,
        Tongue = 37,
        Wrap = 38
    }

    public static class EquipSlots
    {
        public const int Count = 9; // includes None at index 0 so slots index directly

        public static readonly EquipSlot[] All =
        {
            EquipSlot.Weapon, EquipSlot.Armor, EquipSlot.Gloves, EquipSlot.Boots,
            EquipSlot.Ring, EquipSlot.Amulet, EquipSlot.Cape, EquipSlot.Pet
        };

        public static bool IsValid(EquipSlot slot) => slot > EquipSlot.None && (int)slot < Count;

        /// <summary>The Sidekick regions each visual slot owns. Equipping replaces exactly these; empty falls back to defaults.</summary>
        public static BodyRegion[] RegionsFor(EquipSlot slot)
        {
            switch (slot)
            {
                case EquipSlot.Armor: return ArmorRegions;
                case EquipSlot.Gloves: return GloveRegions;
                case EquipSlot.Boots: return BootRegions;
                case EquipSlot.Cape: return CapeRegions;
                default: return System.Array.Empty<BodyRegion>();
            }
        }

        private static readonly BodyRegion[] ArmorRegions =
        {
            BodyRegion.Torso, BodyRegion.ArmUpperLeft, BodyRegion.ArmUpperRight, BodyRegion.ArmLowerLeft,
            BodyRegion.ArmLowerRight, BodyRegion.Hips, BodyRegion.LegLeft, BodyRegion.LegRight,
            BodyRegion.AttachmentHipsFront, BodyRegion.AttachmentHipsBack, BodyRegion.AttachmentHipsLeft,
            BodyRegion.AttachmentHipsRight, BodyRegion.AttachmentShoulderLeft, BodyRegion.AttachmentShoulderRight,
            BodyRegion.AttachmentElbowLeft, BodyRegion.AttachmentElbowRight, BodyRegion.AttachmentKneeLeft,
            BodyRegion.AttachmentKneeRight
        };
        private static readonly BodyRegion[] GloveRegions = { BodyRegion.HandLeft, BodyRegion.HandRight };
        private static readonly BodyRegion[] BootRegions = { BodyRegion.FootLeft, BodyRegion.FootRight };
        private static readonly BodyRegion[] CapeRegions = { BodyRegion.AttachmentBack };
    }
}
