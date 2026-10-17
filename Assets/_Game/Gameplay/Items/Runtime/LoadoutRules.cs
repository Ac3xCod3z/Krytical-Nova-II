using System;
using System.Collections.Generic;
using System.Text;

namespace KryticalNova.Items
{
    /// <summary>What wearing a saved loadout did: how many pieces went on, and which could not.</summary>
    public sealed class WearSetResult
    {
        /// <summary>Pieces the loadout names (its empty slots are left alone and not counted).</summary>
        public int Wanted;
        /// <summary>Pieces put on now.</summary>
        public int Worn;
        /// <summary>Pieces that were on already.</summary>
        public int AlreadyOn;
        /// <summary>Pieces found nowhere in the Pack.</summary>
        public readonly List<ushort> Missing = new List<ushort>();
        /// <summary>Pieces in the Pack that this character may not wear now, and why.</summary>
        public readonly List<(ushort ItemId, string Reason)> Blocked = new List<(ushort, string)>();

        public void Clear()
        {
            Wanted = Worn = AlreadyOn = 0;
            Missing.Clear();
            Blocked.Clear();
        }
    }

    /// <summary>
    /// Equipment loadouts (owner, 17 Oct 2026): up to ten saved sets of worn gear, each with a name, put on again in
    /// one go from the Pack. Pure rules, free of networking, so a test can read them; <see cref="PlayerLoadouts"/>
    /// keeps the sets and <see cref="PlayerInventory.ServerWearSet"/> applies them.
    /// </summary>
    public static class LoadoutRules
    {
        public const int Slots = 10;
        /// <summary>The longest name: it is kept in 29 bytes (FixedString32Bytes) on the network.</summary>
        public const int MaxNameLength = 20;
        private const int MaxNameBytes = 29;

        /// <summary>"Loadout 3" for the slot at index 2.</summary>
        public static string DefaultName(int index) => $"Loadout {index + 1}";

        /// <summary>A loadout's name: one line, trimmed, not too long; the slot's default when nothing usable was typed.</summary>
        public static string CleanName(string typed, int index)
        {
            string name = (typed ?? string.Empty).Replace('\n', ' ').Replace('\r', ' ').Replace('\t', ' ').Replace('<', ' ').Replace('>', ' ').Trim();
            while (name.Contains("  ")) name = name.Replace("  ", " ");
            if (name.Length > MaxNameLength) name = name.Substring(0, MaxNameLength).Trim();
            while (name.Length > 0 && Encoding.UTF8.GetByteCount(name) > MaxNameBytes) name = name.Substring(0, name.Length - 1).Trim();
            return name.Length > 0 ? name : DefaultName(index);
        }

        /// <summary>The first slot that holds no loadout, or -1 when all ten do.</summary>
        public static int FirstFree(IReadOnlyList<bool> used)
        {
            for (int i = 0; i < Slots && i < used.Count; i++) if (!used[i]) return i;
            return used.Count < Slots ? used.Count : -1;
        }

        /// <summary>
        /// The Pack cell that holds a saved piece: the very same piece (its id and state) first, else the same item
        /// in another state (it was Tempered, or the pet grew, since the loadout was saved). -1 when there is none.
        /// </summary>
        public static int FindInPack(ItemStack[] bag, in ItemStack wanted)
        {
            if (bag == null || wanted.IsEmpty) return -1;
            int sameItem = -1;
            for (int i = 0; i < bag.Length; i++)
            {
                ItemStack cell = bag[i];
                if (cell.IsEmpty || cell.itemId != wanted.itemId || cell.count != 1) continue;
                if (cell.data == wanted.data) return i;
                if (sameItem < 0) sameItem = i;
            }
            return sameItem;
        }

        /// <summary>
        /// Puts a saved set on: for each piece that is not on already, the piece is taken from its Pack cell and what
        /// was worn in its slot goes into that cell, so the Pack never needs room. A piece missing from the Pack, or
        /// one this character may not wear now (<paramref name="gate"/>), is skipped and named in the result; the
        /// rest still go on. Never loses an item.
        /// </summary>
        public static void WearSet(ItemStack[] bag, ref EquippedItems equipped, IItemLookup lookup, IReadOnlyList<ItemStack> pieces,
            InventoryRules.EquipGate gate, WearSetResult result)
        {
            result.Clear();
            if (bag == null || lookup == null || pieces == null) return;
            foreach (ItemStack piece in pieces)
            {
                if (piece.IsEmpty) continue;
                ItemDefinition def = lookup.Get(piece.itemId);
                if (def == null || !def.IsEquippable) continue; // no longer in the game
                result.Wanted++;
                if (equipped[def.equipSlot].itemId == piece.itemId) { result.AlreadyOn++; continue; }

                int cell = FindInPack(bag, piece);
                if (cell < 0) { result.Missing.Add(piece.itemId); continue; }
                if (!InventoryRules.TryMove(bag, ref equipped, lookup, ItemLocation.Bag(cell), ItemLocation.Equip(def.equipSlot), out string reason, gate))
                {
                    result.Blocked.Add((piece.itemId, reason ?? InventoryRules.ReasonNotEquippable));
                    continue;
                }
                result.Worn++;
            }
        }

        /// <summary>What the player is told after wearing a loadout: "Wore Hunting set." or "Wore 6 of 8 of Hunting set. Not in your Pack: X, Y."</summary>
        public static string Describe(string loadoutName, WearSetResult result, Func<ushort, string> nameOf)
        {
            nameOf ??= id => id.ToString();
            int on = result.Worn + result.AlreadyOn;
            if (result.Wanted == 0) return $"{loadoutName} has nothing to wear.";
            var text = new StringBuilder();
            text.Append(on == result.Wanted ? $"Wore {loadoutName}." : $"Wore {on} of {result.Wanted} of {loadoutName}.");
            if (result.Missing.Count > 0)
            {
                text.Append(" Not in your Pack: ");
                for (int i = 0; i < result.Missing.Count; i++) text.Append(i == 0 ? string.Empty : ", ").Append(nameOf(result.Missing[i]));
                text.Append('.');
            }
            foreach ((ushort id, string reason) in result.Blocked) text.Append($" {nameOf(id)}: {reason}");
            return text.ToString();
        }
    }
}
