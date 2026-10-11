using System;
using System.Collections.Generic;
using KryticalNova.Items;
using KryticalNova.Items.Visuals;
using UnityEngine;

namespace KryticalNova.CharacterCreation
{
    /// <summary>
    /// One piece of gear a character wears, as the menus need it to draw the character: the slot, the item and its
    /// Temper %, and what the slot shows in place of the piece (its Weave and its dye). Kept with the character's
    /// roster entry (<see cref="CharacterProfile.worn"/>).
    /// </summary>
    [Serializable]
    public struct WornPiece : IEquatable<WornPiece>
    {
        /// <summary>The <see cref="EquipSlot"/>'s number.</summary>
        public int slot;
        public int itemId;
        public int temper;
        /// <summary>The item whose look the slot shows (its Weave); 0 = the piece's own.</summary>
        public int weave;
        /// <summary>The colourway the piece is dyed in; 0 = its own colours.</summary>
        public int dye;

        public WornPiece(EquipSlot slot, int itemId, int temper, int weave = 0, int dye = 0)
        {
            this.slot = (int)slot; this.itemId = itemId; this.temper = temper; this.weave = weave; this.dye = dye;
        }

        public bool Equals(WornPiece other) =>
            slot == other.slot && itemId == other.itemId && temper == other.temper && weave == other.weave && dye == other.dye;
        public override bool Equals(object other) => other is WornPiece piece && Equals(piece);
        public override int GetHashCode() => (((slot * 397 ^ itemId) * 397 ^ temper) * 397 ^ weave) * 397 ^ dye;
    }

    /// <summary>
    /// Dresses a display doll in gear: the outfit pieces that have a look (armour, gloves, boots, cape), the weapon
    /// in its carry place and the weapon's Temper glow, the way a player in the world shows them
    /// (<c>EquipmentVisualBinder</c>). Character select shows a saved character with it, and faction select its
    /// champions. A doll shows a weapon only when it was made to keep its weapon mount.
    /// </summary>
    public static class DollOutfit
    {
        /// <summary>The slots whose piece changes how a character looks, the weapon aside.</summary>
        public static readonly EquipSlot[] OutfitSlots = { EquipSlot.Armor, EquipSlot.Gloves, EquipSlot.Boots, EquipSlot.Cape };

        /// <summary>
        /// Puts <paramref name="worn"/> on the doll and takes off whatever it wore that is not in it (so one doll can
        /// show one character after another). An item the catalog does not know is left off.
        /// </summary>
        public static void Wear(CharacterDoll doll, IReadOnlyList<WornPiece> worn, IItemLookup items, GearProgressionSettings progression)
        {
            if (doll == null) return;
            CharacterAppearance appearance = doll.Appearance;
            WeaponVisualMount mount = doll.Weapon;
            GearThemeLibrary colourways = items is ItemCatalog catalog ? catalog.Themes : null;

            foreach (EquipSlot slot in OutfitSlots)
            {
                ItemDefinition piece = Find(worn, slot, items, out _);
                if (appearance != null) appearance.SetSlot(slot, Shown(worn, slot, items, piece), DyeOf(worn, slot, colourways));
                if (slot == EquipSlot.Cape && mount != null) mount.SetCapeWorn(piece != null);
            }
            if (mount == null) return;

            ItemDefinition weapon = Find(worn, EquipSlot.Weapon, items, out int temper);
            mount.SetWeapon(Shown(worn, EquipSlot.Weapon, items, weapon));
            if (weapon != null && progression != null && GearProgressionRules.TryGetGlow(progression.temper, temper, weapon.rarity,
                    out Color color, out TemperGlowLook look))
                mount.SetGlow(true, color, look);
            else
                mount.SetGlow(false, default, default);
        }

        /// <summary>The item worn in a slot, or null (nothing there, no catalog, or an item that no longer exists).</summary>
        public static ItemDefinition Find(IReadOnlyList<WornPiece> worn, EquipSlot slot, IItemLookup items, out int temper)
        {
            temper = 0;
            if (worn == null || items == null) return null;
            for (int i = 0; i < worn.Count; i++)
            {
                if (worn[i].slot != (int)slot || worn[i].itemId <= 0 || worn[i].itemId > ushort.MaxValue) continue;
                temper = worn[i].temper;
                return items.Get((ushort)worn[i].itemId);
            }
            return null;
        }

        /// <summary>What a slot shows: the piece, or its Weave when it has one that still suits it.</summary>
        private static ItemDefinition Shown(IReadOnlyList<WornPiece> worn, EquipSlot slot, IItemLookup items, ItemDefinition piece)
        {
            if (piece == null) return null;
            for (int i = 0; i < worn.Count; i++)
                if (worn[i].slot == (int)slot && worn[i].weave > 0 && worn[i].weave <= ushort.MaxValue)
                    return WeaveRules.Shown(piece, items.Get((ushort)worn[i].weave));
            return piece;
        }

        /// <summary>The colour grid of the colourway a slot is dyed in, or null.</summary>
        private static Color32[] DyeOf(IReadOnlyList<WornPiece> worn, EquipSlot slot, GearThemeLibrary colourways)
        {
            if (worn == null || colourways == null) return null;
            for (int i = 0; i < worn.Count; i++)
                if (worn[i].slot == (int)slot && worn[i].dye > 0 && worn[i].dye <= ushort.MaxValue)
                    return colourways.TryGet((ushort)worn[i].dye, out var theme) ? theme.cells : null;
            return null;
        }

        /// <summary>True when two records name the same pieces in the same order.</summary>
        public static bool Same(IReadOnlyList<WornPiece> a, IReadOnlyList<WornPiece> b)
        {
            int countA = a != null ? a.Count : 0, countB = b != null ? b.Count : 0;
            if (countA != countB) return false;
            for (int i = 0; i < countA; i++)
                if (!a[i].Equals(b[i])) return false;
            return true;
        }
    }
}
