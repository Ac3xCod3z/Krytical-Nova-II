using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using KryticalNova.Items;

namespace KryticalNova.Persistence.Entities
{
    /// <summary>
    /// What other players see a player wear (M8 8.1.2b), as one line of the look text: NGO's <c>PlayerEquipment</c> network variables for
    /// observers (the loadout, the looks, the Temper), in plain numbers. <c>g1|loadout(8)|looks(14)|temper(7)</c>.
    /// </summary>
    public static class GearText
    {
        public const string Prefix = "g1";

        static readonly EquipSlot[] LoadoutSlots =
            { EquipSlot.Weapon, EquipSlot.Armor, EquipSlot.Gloves, EquipSlot.Boots, EquipSlot.Ring, EquipSlot.Amulet, EquipSlot.Cape, EquipSlot.Pet };
        static readonly EquipSlot[] TemperSlots =
            { EquipSlot.Weapon, EquipSlot.Armor, EquipSlot.Gloves, EquipSlot.Boots, EquipSlot.Ring, EquipSlot.Amulet, EquipSlot.Cape };

        public static string Encode(in EquipmentLoadout loadout, in EquipmentLooks looks, in EquipmentTemper temper)
        {
            var sb = new StringBuilder(160).Append(Prefix).Append('|');
            for (int i = 0; i < LoadoutSlots.Length; i++) Num(sb, loadout[LoadoutSlots[i]], i == 0);
            sb.Append('|');
            int[] l =
            {
                looks.weapon, looks.armor, looks.gloves, looks.boots, looks.cape, looks.armorDye, looks.glovesDye, looks.bootsDye, looks.capeDye,
                looks.weaponDye, looks.pet, looks.petStage, looks.helmetHidden ? 1 : 0, looks.capeHidden ? 1 : 0,
            };
            for (int i = 0; i < l.Length; i++) Num(sb, l[i], i == 0);
            sb.Append('|');
            for (int i = 0; i < TemperSlots.Length; i++) Num(sb, temper[TemperSlots[i]], i == 0);
            return sb.ToString();
        }

        /// <summary>False for anything malformed or out of range (the body then keeps what it wears; nothing is guessed).</summary>
        public static bool TryDecode(string text, out EquipmentLoadout loadout, out EquipmentLooks looks, out EquipmentTemper temper)
        {
            loadout = default;
            looks = default;
            temper = default;
            if (string.IsNullOrEmpty(text)) return false;
            string[] parts = text.Split('|');
            if (parts.Length != 4 || parts[0] != Prefix) return false;
            if (!Nums(parts[1], LoadoutSlots.Length, ushort.MaxValue, out List<int> a)
                || !Nums(parts[2], 14, ushort.MaxValue, out List<int> b)
                || !Nums(parts[3], TemperSlots.Length, byte.MaxValue, out List<int> c)) return false;
            for (int i = 0; i < LoadoutSlots.Length; i++) loadout[LoadoutSlots[i]] = (ushort)a[i];
            if (b[11] > byte.MaxValue || b[12] > 1 || b[13] > 1) return false;
            looks = new EquipmentLooks
            {
                weapon = (ushort)b[0], armor = (ushort)b[1], gloves = (ushort)b[2], boots = (ushort)b[3], cape = (ushort)b[4],
                armorDye = (ushort)b[5], glovesDye = (ushort)b[6], bootsDye = (ushort)b[7], capeDye = (ushort)b[8], weaponDye = (ushort)b[9],
                pet = (ushort)b[10], petStage = (byte)b[11], helmetHidden = b[12] == 1, capeHidden = b[13] == 1,
            };
            for (int i = 0; i < TemperSlots.Length; i++) temper[TemperSlots[i]] = (byte)c[i];
            return true;
        }

        static void Num(StringBuilder sb, int value, bool first)
        {
            if (!first) sb.Append(',');
            sb.Append(value.ToString(CultureInfo.InvariantCulture));
        }

        static bool Nums(string text, int count, int max, out List<int> values)
        {
            values = new List<int>(count);
            string[] items = text.Split(',');
            if (items.Length != count) return false;
            foreach (string item in items)
            {
                if (!int.TryParse(item, NumberStyles.None, CultureInfo.InvariantCulture, out int v) || v > max) return false;
                values.Add(v);
            }
            return true;
        }
    }
}
