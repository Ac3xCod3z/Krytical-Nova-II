using System;
using System.Collections.Generic;

namespace KryticalNova.Items
{
    /// <summary>
    /// Pure inventory/equipment rules: no networking, no Unity objects beyond item definitions. The server runs these
    /// against a scratch copy of the player's state and writes back the result, so every mutation is validated in one place
    /// (and is unit-testable). Every Try* method leaves the state untouched when it returns false.
    /// Items move as whole <see cref="ItemStack"/>s, so an item's own <see cref="ItemStack.data"/> goes with it.
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

        public static bool TryMove(ItemStack[] bag, ref EquippedItems equipped, IItemLookup lookup,
            ItemLocation from, ItemLocation to, out string reason, EquipGate gate = null)
        {
            reason = null;
            if (!IsValid(bag, from) || !IsValid(bag, to)) { reason = ReasonInvalidLocation; return false; }
            if (from.Equals(to)) return true;

            if (from.IsBag && to.IsBag) return MoveBagToBag(bag, lookup, from.index, to.index, out reason);
            if (from.IsBag) return MoveBagToEquip(bag, ref equipped, lookup, from.index, to.Slot, out reason, gate);
            if (to.IsBag) return MoveEquipToBag(bag, ref equipped, lookup, from.Slot, to.index, out reason, gate);

            reason = ReasonWrongSlot; // equip -> different equip slot: no two slots share an item type
            return false;
        }

        public static bool TryQuickEquip(ItemStack[] bag, ref EquippedItems equipped, IItemLookup lookup,
            int bagIndex, out string reason, EquipGate gate = null)
        {
            reason = null;
            if (bagIndex < 0 || bagIndex >= bag.Length) { reason = ReasonInvalidLocation; return false; }
            if (bag[bagIndex].IsEmpty) { reason = ReasonEmpty; return false; }
            var def = lookup.Get(bag[bagIndex].itemId);
            if (def == null) { reason = ReasonUnknownItem; return false; }
            if (!def.IsEquippable) { reason = ReasonNotEquippable; return false; }
            return MoveBagToEquip(bag, ref equipped, lookup, bagIndex, def.equipSlot, out reason, gate);
        }

        public static bool TryQuickUnequip(ItemStack[] bag, ref EquippedItems equipped, IItemLookup lookup,
            EquipSlot slot, out string reason)
        {
            reason = null;
            if (!EquipSlots.IsValid(slot)) { reason = ReasonInvalidLocation; return false; }
            if (equipped.IsEmpty(slot)) { reason = ReasonEmpty; return false; }
            int cell = FirstEmpty(bag);
            if (cell < 0) { reason = ReasonBagFull; return false; }
            return MoveEquipToBag(bag, ref equipped, lookup, slot, cell, out reason);
        }

        public static bool TryDestroy(ItemStack[] bag, ref EquippedItems equipped, ItemLocation at, out string reason) =>
            TryTake(bag, ref equipped, at, out _, out reason);

        /// <summary>Removes whatever is at <paramref name="at"/> and hands it back (to drop it on the ground, for one).</summary>
        public static bool TryTake(ItemStack[] bag, ref EquippedItems equipped, ItemLocation at, out ItemStack taken, out string reason)
        {
            reason = null;
            taken = ItemStack.Empty;
            if (!IsValid(bag, at)) { reason = ReasonInvalidLocation; return false; }
            if (at.IsBag)
            {
                if (bag[at.index].IsEmpty) { reason = ReasonEmpty; return false; }
                taken = bag[at.index];
                bag[at.index] = ItemStack.Empty;
                return true;
            }
            if (equipped.IsEmpty(at.Slot)) { reason = ReasonEmpty; return false; }
            taken = equipped[at.Slot];
            equipped[at.Slot] = ItemStack.Empty;
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

            bag[bagIndex] = stack.WithCount(stack.count - 1);
            used = def;
            return true;
        }

        /// <summary>Adds up to <paramref name="count"/>, topping up existing stacks first. Returns how many were added.</summary>
        public static int TryAdd(ItemStack[] bag, IItemLookup lookup, ushort itemId, int count, ulong data = 0)
        {
            var def = lookup.Get(itemId);
            if (def == null || count <= 0) return 0;
            int maxStack = Math.Max(1, (int)def.maxStack);
            int remaining = count;

            if (maxStack > 1)
            {
                for (int i = 0; i < bag.Length && remaining > 0; i++)
                {
                    if (bag[i].itemId != itemId || bag[i].data != data || bag[i].count >= maxStack) continue;
                    int take = Math.Min(remaining, maxStack - bag[i].count);
                    bag[i] = new ItemStack(itemId, (ushort)(bag[i].count + take), data);
                    remaining -= take;
                }
            }
            for (int i = 0; i < bag.Length && remaining > 0; i++)
            {
                if (!bag[i].IsEmpty) continue;
                int take = Math.Min(remaining, maxStack);
                bag[i] = new ItemStack(itemId, (ushort)take, data);
                remaining -= take;
            }
            return count - remaining;
        }

        /// <summary>Adds a whole stack, state included. Returns how many of it fitted.</summary>
        public static int TryAdd(ItemStack[] bag, IItemLookup lookup, in ItemStack stack) =>
            stack.IsEmpty ? 0 : TryAdd(bag, lookup, stack.itemId, stack.count, stack.data);

        /// <summary>
        /// True when every stack of <paramref name="incoming"/> fits. <paramref name="scratch"/> is overwritten with
        /// a copy of the bag as it would then be.
        /// </summary>
        public static bool Fits(ItemStack[] bag, ItemStack[] scratch, IItemLookup lookup, IReadOnlyList<ItemStack> incoming)
        {
            Array.Copy(bag, scratch, bag.Length);
            for (int i = 0; i < incoming.Count; i++)
            {
                ItemStack stack = incoming[i];
                if (stack.IsEmpty) continue;
                if (TryAdd(scratch, lookup, stack) != stack.count) return false;
            }
            return true;
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
                if (write > 0 && bag[write - 1].StacksWith(s) && bag[write - 1].count < maxStack)
                {
                    int take = Math.Min(remaining, maxStack - bag[write - 1].count);
                    bag[write - 1] = bag[write - 1].WithCount(bag[write - 1].count + take);
                    remaining -= take;
                }
                if (remaining > 0 && write < bag.Length) bag[write++] = s.WithCount(remaining);
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
            if (c != 0) return c;
            c = b.data.CompareTo(a.data); // same item: the further-grown one first, and equal states side by side
            return c != 0 ? c : b.count.CompareTo(a.count);
        }

        private static bool IsValid(ItemStack[] bag, ItemLocation loc) =>
            loc.IsBag ? loc.index < bag.Length : loc.container == ItemContainer.Equipment && EquipSlots.IsValid(loc.Slot);

        private static bool MoveBagToBag(ItemStack[] bag, IItemLookup lookup, int from, int to, out string reason)
        {
            reason = null;
            var src = bag[from];
            if (src.IsEmpty) { reason = ReasonEmpty; return false; }
            var dst = bag[to];

            if (!dst.IsEmpty && dst.StacksWith(src))
            {
                var def = lookup.Get(src.itemId);
                int maxStack = def != null ? def.maxStack : 1;
                if (maxStack > 1 && dst.count < maxStack)
                {
                    int take = Math.Min(src.count, maxStack - dst.count);
                    bag[to] = dst.WithCount(dst.count + take);
                    bag[from] = src.WithCount(src.count - take);
                    return true;
                }
            }

            bag[to] = src;
            bag[from] = dst;
            return true;
        }

        private static bool MoveBagToEquip(ItemStack[] bag, ref EquippedItems equipped, IItemLookup lookup,
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

            ItemStack previous = equipped[slot];
            equipped[slot] = src;
            bag[from] = previous;
            return true;
        }

        private static bool MoveEquipToBag(ItemStack[] bag, ref EquippedItems equipped, IItemLookup lookup,
            EquipSlot slot, int to, out string reason, EquipGate gate = null)
        {
            reason = null;
            ItemStack worn = equipped[slot];
            if (worn.IsEmpty) { reason = ReasonEmpty; return false; }
            var dst = bag[to];

            if (dst.IsEmpty)
            {
                bag[to] = worn;
                equipped[slot] = ItemStack.Empty;
                return true;
            }

            // Dropping an equipped item onto a compatible bag item swaps them (equips the bag item).
            var def = lookup.Get(dst.itemId);
            if (def == null || def.equipSlot != slot || dst.count != 1) { reason = ReasonCannotSwap; return false; }
            if (gate != null && (reason = gate(def)) != null) return false;
            equipped[slot] = dst;
            bag[to] = worn;
            return true;
        }
    }
}
