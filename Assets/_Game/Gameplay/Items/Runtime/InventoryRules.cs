using System;
using System.Collections.Generic;

namespace KryticalNova.Items
{
    /// <summary>
    /// Pure inventory/equipment rules: no networking, no Unity objects beyond item definitions. The server runs these
    /// against a scratch copy of the player's state and writes back the result, so every mutation is validated in one place
    /// (and is unit-testable). Every Try* method leaves the state untouched when it returns false.
    /// </summary>
    public static class InventoryRules
    {
        public const string ReasonInvalidLocation = "Invalid slot.";
        public const string ReasonEmpty = "There is nothing there.";
        public const string ReasonWrongSlot = "That item doesn't go in that slot.";
        public const string ReasonNotEquippable = "That item can't be equipped.";
        public const string ReasonSplitStack = "Split the stack before equipping.";
        public const string ReasonCannotSwap = "Can't swap with that item.";
        public const string ReasonBagFull = "Your Pack is full.";
        public const string ReasonUnknownItem = "Unknown item.";
        public const string ReasonNotUsable = "That item can't be used.";

        /// <summary>
        /// Says why this wearer may not equip an item (a user-facing reason), or null when it may. Supplied by the
        /// caller, who knows the wearer's faction and body; the rules stay pure.
        /// </summary>
        public delegate string EquipGate(ItemDefinition item);

        public static bool TryMove(ItemStack[] bag, ref EquipmentLoadout loadout, IItemLookup lookup,
            ItemLocation from, ItemLocation to, out string reason, EquipGate gate = null)
        {
            reason = null;
            if (!IsValid(bag, from) || !IsValid(bag, to)) { reason = ReasonInvalidLocation; return false; }
            if (from.Equals(to)) return true;

            if (from.IsBag && to.IsBag) return MoveBagToBag(bag, lookup, from.index, to.index, out reason);
            if (from.IsBag) return MoveBagToEquip(bag, ref loadout, lookup, from.index, to.Slot, out reason, gate);
            if (to.IsBag) return MoveEquipToBag(bag, ref loadout, lookup, from.Slot, to.index, out reason, gate);

            reason = ReasonWrongSlot; // equip -> different equip slot: no two slots share an item type
            return false;
        }

        public static bool TryQuickEquip(ItemStack[] bag, ref EquipmentLoadout loadout, IItemLookup lookup,
            int bagIndex, out string reason, EquipGate gate = null)
        {
            reason = null;
            if (bagIndex < 0 || bagIndex >= bag.Length) { reason = ReasonInvalidLocation; return false; }
            if (bag[bagIndex].IsEmpty) { reason = ReasonEmpty; return false; }
            var def = lookup.Get(bag[bagIndex].itemId);
            if (def == null) { reason = ReasonUnknownItem; return false; }
            if (!def.IsEquippable) { reason = ReasonNotEquippable; return false; }
            return MoveBagToEquip(bag, ref loadout, lookup, bagIndex, def.equipSlot, out reason, gate);
        }

        public static bool TryQuickUnequip(ItemStack[] bag, ref EquipmentLoadout loadout, IItemLookup lookup,
            EquipSlot slot, out string reason)
        {
            reason = null;
            if (!EquipSlots.IsValid(slot)) { reason = ReasonInvalidLocation; return false; }
            if (loadout[slot] == 0) { reason = ReasonEmpty; return false; }
            int cell = FirstEmpty(bag);
            if (cell < 0) { reason = ReasonBagFull; return false; }
            return MoveEquipToBag(bag, ref loadout, lookup, slot, cell, out reason);
        }

        public static bool TryDestroy(ItemStack[] bag, ref EquipmentLoadout loadout, ItemLocation at, out string reason)
        {
            reason = null;
            if (!IsValid(bag, at)) { reason = ReasonInvalidLocation; return false; }
            if (at.IsBag)
            {
                if (bag[at.index].IsEmpty) { reason = ReasonEmpty; return false; }
                bag[at.index] = ItemStack.Empty;
                return true;
            }
            if (loadout[at.Slot] == 0) { reason = ReasonEmpty; return false; }
            loadout[at.Slot] = 0;
            return true;
        }

        /// <summary>Removes one consumable from the stack at <paramref name="bagIndex"/> and reports what was used.</summary>
        public static bool TryConsume(ItemStack[] bag, IItemLookup lookup, int bagIndex, out ItemDefinition used, out string reason)
        {
            used = null;
            reason = null;
            if (bagIndex < 0 || bagIndex >= bag.Length) { reason = ReasonInvalidLocation; return false; }
            var stack = bag[bagIndex];
            if (stack.IsEmpty) { reason = ReasonEmpty; return false; }
            var def = lookup.Get(stack.itemId);
            if (def == null) { reason = ReasonUnknownItem; return false; }
            if (!def.IsConsumable) { reason = ReasonNotUsable; return false; }

            bag[bagIndex] = stack.count > 1 ? new ItemStack(stack.itemId, (ushort)(stack.count - 1)) : ItemStack.Empty;
            used = def;
            return true;
        }

        /// <summary>Adds up to <paramref name="count"/>, topping up existing stacks first. Returns how many were added.</summary>
        public static int TryAdd(ItemStack[] bag, IItemLookup lookup, ushort itemId, int count)
        {
            var def = lookup.Get(itemId);
            if (def == null || count <= 0) return 0;
            int maxStack = Math.Max(1, (int)def.maxStack);
            int remaining = count;

            if (maxStack > 1)
            {
                for (int i = 0; i < bag.Length && remaining > 0; i++)
                {
                    if (bag[i].itemId != itemId || bag[i].count >= maxStack) continue;
                    int take = Math.Min(remaining, maxStack - bag[i].count);
                    bag[i] = new ItemStack(itemId, (ushort)(bag[i].count + take));
                    remaining -= take;
                }
            }
            for (int i = 0; i < bag.Length && remaining > 0; i++)
            {
                if (!bag[i].IsEmpty) continue;
                int take = Math.Min(remaining, maxStack);
                bag[i] = new ItemStack(itemId, (ushort)take);
                remaining -= take;
            }
            return count - remaining;
        }

        /// <summary>Merges partial stacks, then orders by equip slot, rarity (high first), item level (high first), name; empties last.</summary>
        public static void Sort(ItemStack[] bag, IItemLookup lookup)
        {
            var stacks = new List<ItemStack>(bag.Length);
            foreach (var s in bag) if (!s.IsEmpty) stacks.Add(s);
            Array.Clear(bag, 0, bag.Length);

            stacks.Sort((a, b) => Compare(a, b, lookup));

            // Re-add in sorted order through TryAdd-style merging so partial stacks of the same item combine.
            int write = 0;
            foreach (var s in stacks)
            {
                var def = lookup.Get(s.itemId);
                int maxStack = def != null ? Math.Max(1, (int)def.maxStack) : 1;
                int remaining = s.count;
                if (write > 0 && bag[write - 1].itemId == s.itemId && bag[write - 1].count < maxStack)
                {
                    int take = Math.Min(remaining, maxStack - bag[write - 1].count);
                    bag[write - 1] = new ItemStack(s.itemId, (ushort)(bag[write - 1].count + take));
                    remaining -= take;
                }
                if (remaining > 0 && write < bag.Length) bag[write++] = new ItemStack(s.itemId, (ushort)remaining);
            }
        }

        public static int FirstEmpty(ItemStack[] bag)
        {
            for (int i = 0; i < bag.Length; i++) if (bag[i].IsEmpty) return i;
            return -1;
        }

        private static int Compare(ItemStack a, ItemStack b, IItemLookup lookup)
        {
            var da = lookup.Get(a.itemId);
            var db = lookup.Get(b.itemId);
            if (da == null || db == null) return (da == null).CompareTo(db == null); // unknown last
            int slotA = da.IsEquippable ? (int)da.equipSlot : int.MaxValue; // non-gear after gear
            int slotB = db.IsEquippable ? (int)db.equipSlot : int.MaxValue;
            int c = slotA.CompareTo(slotB);
            if (c != 0) return c;
            c = db.rarity.CompareTo(da.rarity);
            if (c != 0) return c;
            c = db.itemLevel.CompareTo(da.itemLevel);
            if (c != 0) return c;
            c = string.Compare(da.displayName, db.displayName, StringComparison.OrdinalIgnoreCase);
            if (c != 0) return c;
            c = a.itemId.CompareTo(b.itemId);
            return c != 0 ? c : b.count.CompareTo(a.count);
        }

        private static bool IsValid(ItemStack[] bag, ItemLocation loc) =>
            loc.IsBag ? loc.index < bag.Length : EquipSlots.IsValid(loc.Slot);

        private static bool MoveBagToBag(ItemStack[] bag, IItemLookup lookup, int from, int to, out string reason)
        {
            reason = null;
            var src = bag[from];
            if (src.IsEmpty) { reason = ReasonEmpty; return false; }
            var dst = bag[to];

            if (!dst.IsEmpty && dst.itemId == src.itemId)
            {
                var def = lookup.Get(src.itemId);
                int maxStack = def != null ? def.maxStack : 1;
                if (maxStack > 1 && dst.count < maxStack)
                {
                    int take = Math.Min(src.count, maxStack - dst.count);
                    bag[to] = new ItemStack(dst.itemId, (ushort)(dst.count + take));
                    int left = src.count - take;
                    bag[from] = left > 0 ? new ItemStack(src.itemId, (ushort)left) : ItemStack.Empty;
                    return true;
                }
            }

            bag[to] = src;
            bag[from] = dst;
            return true;
        }

        private static bool MoveBagToEquip(ItemStack[] bag, ref EquipmentLoadout loadout, IItemLookup lookup,
            int from, EquipSlot slot, out string reason, EquipGate gate)
        {
            reason = null;
            var src = bag[from];
            if (src.IsEmpty) { reason = ReasonEmpty; return false; }
            var def = lookup.Get(src.itemId);
            if (def == null) { reason = ReasonUnknownItem; return false; }
            if (!def.IsEquippable) { reason = ReasonNotEquippable; return false; }
            if (def.equipSlot != slot) { reason = ReasonWrongSlot; return false; }
            if (src.count != 1) { reason = ReasonSplitStack; return false; }
            if (gate != null && (reason = gate(def)) != null) return false;

            ushort previous = loadout[slot];
            loadout[slot] = src.itemId;
            bag[from] = previous != 0 ? new ItemStack(previous, 1) : ItemStack.Empty;
            return true;
        }

        private static bool MoveEquipToBag(ItemStack[] bag, ref EquipmentLoadout loadout, IItemLookup lookup,
            EquipSlot slot, int to, out string reason, EquipGate gate = null)
        {
            reason = null;
            ushort equipped = loadout[slot];
            if (equipped == 0) { reason = ReasonEmpty; return false; }
            var dst = bag[to];

            if (dst.IsEmpty)
            {
                bag[to] = new ItemStack(equipped, 1);
                loadout[slot] = 0;
                return true;
            }

            // Dropping an equipped item onto a compatible bag item swaps them (equips the bag item).
            var def = lookup.Get(dst.itemId);
            if (def == null || def.equipSlot != slot || dst.count != 1) { reason = ReasonCannotSwap; return false; }
            if (gate != null && (reason = gate(def)) != null) return false;
            loadout[slot] = dst.itemId;
            bag[to] = new ItemStack(equipped, 1);
            return true;
        }
    }
}
