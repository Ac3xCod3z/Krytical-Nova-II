using KryticalNova.Factions;

namespace KryticalNova.Items
{
    /// <summary>
    /// Holoweave: showing the look of one piece of gear over another, and dyeing an outfit piece in another
    /// colourway. A look is collected by gear line (every level tier of a line looks the same), and is shown by the
    /// first item of that line, which every machine has. Free of networking so it can be tested; every machine
    /// works out what is shown with <see cref="Shown"/> from what is worn and the Weave on the slot, so nothing
    /// has to be rewritten when the worn piece changes.
    /// </summary>
    public static class WeaveRules
    {
        public const string ReasonNotAWeave = "That has no look to weave.";
        public const string ReasonNotOwned = "That Weave is not in your Weave Library.";
        public const string ReasonNothingWorn = "Wear something in that slot first.";
        public const string ReasonWrongSlot = "That Weave is for another slot.";
        public const string ReasonWrongClass = "That Weave is for another weapon class.";
        public const string ReasonNoDye = "That piece takes no dye.";
        public const string ReasonNoColourway = "That colourway does not exist.";

        /// <summary>The slots whose piece has a look of its own.</summary>
        public static readonly EquipSlot[] WeaveSlots = { EquipSlot.Weapon, EquipSlot.Armor, EquipSlot.Gloves, EquipSlot.Boots, EquipSlot.Cape };
        /// <summary>The slots whose piece wears colours on the character's colour map. A weapon has materials of its own.</summary>
        public static readonly EquipSlot[] DyeSlots = { EquipSlot.Weapon, EquipSlot.Armor, EquipSlot.Gloves, EquipSlot.Boots, EquipSlot.Cape };

        public static bool IsWeaveSlot(EquipSlot slot) => System.Array.IndexOf(WeaveSlots, slot) >= 0;
        public static bool IsDyeSlot(EquipSlot slot) => System.Array.IndexOf(DyeSlots, slot) >= 0;

        /// <summary>The key a piece's look is collected under: its gear line. 0 = nothing to collect.</summary>
        public static ushort KeyOf(ItemDefinition item)
        {
            if (item == null || !item.IsLadderGear || !IsWeaveSlot(item.equipSlot)) return 0;
            bool hasLook = item.equipSlot == EquipSlot.Weapon
                ? item.weaponPrefab != null
                : (item.appearancePartIds != null && item.appearancePartIds.Length > 0) || (item.appearanceParts != null && item.appearanceParts.Count > 0);
            return hasLook ? item.lineId : (ushort)0;
        }

        /// <summary>The item that stands for a collected look on every machine: the first tier of its line.</summary>
        public static ushort ItemOf(ushort lineId) =>
            lineId == 0 ? (ushort)0 : (ushort)(GearDictionary.LadderIdBase + (lineId - 1) * GearDictionary.IdsPerLine);

        /// <summary>
        /// What a slot shows: the Weave when it still suits what is worn, the worn piece otherwise. An empty slot
        /// shows nothing, and a weapon of another class shows itself (its class decides how it is held and swung).
        /// </summary>
        public static ItemDefinition Shown(ItemDefinition worn, ItemDefinition weave)
        {
            if (worn == null) return null;
            if (weave == null || weave.equipSlot != worn.equipSlot) return worn;
            if (worn.equipSlot == EquipSlot.Weapon && weave.weaponClass != worn.weaponClass) return worn;
            return weave;
        }

        /// <summary>
        /// A Weave as players read it, the way a dye is read by its colourway: the set it is from ("Commander"), or
        /// the name of its line for a piece that belongs to no set (a weapon).
        /// </summary>
        public static string NameOf(GearDictionary gear, ItemDefinition weave)
        {
            if (weave == null) return string.Empty;
            if (gear != null && gear.TryGetLine(weave.lineId, out GearDictionary.Line line))
            {
                if (!string.IsNullOrEmpty(line.set)) return line.set;
                if (!string.IsNullOrEmpty(line.displayName)) return line.displayName;
            }
            return weave.displayName;
        }

        /// <summary>Why a Weave may not go on a slot (shown to the player), or null.</summary>
        public static string ApplyBlockedReason(GearDictionary gear, EquipSlot slot, ItemDefinition worn, ItemDefinition weave,
            bool owned, FactionId faction, BodyFit body)
        {
            if (!IsWeaveSlot(slot) || KeyOf(weave) == 0) return ReasonNotAWeave;
            if (weave.equipSlot != slot) return ReasonWrongSlot;
            if (!owned) return ReasonNotOwned;
            if (worn == null) return ReasonNothingWorn;
            if (gear != null && !gear.CanUse(weave.itemId, faction, body, out string reason)) return reason;
            if (slot == EquipSlot.Weapon && weave.weaponClass != worn.weaponClass) return ReasonWrongClass;
            return null;
        }

        /// <summary>
        /// Whether a piece wears colours a dye can change: an outfit piece paints cells of the character's colour
        /// map, a weapon has a model whose paints are repainted (<c>WeaponPaint</c>).
        /// </summary>
        public static bool TakesDye(ItemDefinition shown)
        {
            if (shown == null || !IsDyeSlot(shown.equipSlot)) return false;
            if (shown.equipSlot == EquipSlot.Weapon) return shown.weaponPrefab != null;
            if (shown.colorCells != null && shown.colorCells.Length > 0) return true;
            // A painted piece (owner, 24 Oct 2026) reads no colour cells: its paint is dyed instead (GearPaint).
            var parts = shown.GetAppearanceParts();
            if (parts != null)
                for (int i = 0; i < parts.Count; i++)
                    if (KryticalNova.Items.Visuals.GearPaint.TryGetLook(parts[i], out _)) return true;
            return false;
        }

        /// <summary>Why the piece shown in a slot may not be dyed in a colourway (0 = back to its own colours), or null.</summary>
        public static string DyeBlockedReason(EquipSlot slot, ItemDefinition shown, ushort colourway, GearThemeLibrary colourways)
        {
            if (!IsDyeSlot(slot)) return ReasonNoDye;
            if (shown == null) return ReasonNothingWorn;
            if (!TakesDye(shown)) return ReasonNoDye;
            if (colourway != 0 && (colourways == null || !colourways.TryGet(colourway, out _))) return ReasonNoColourway;
            return null;
        }
    }
}
