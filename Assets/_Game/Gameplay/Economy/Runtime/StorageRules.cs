using System;
using KryticalNova.Items;

namespace KryticalNova.Economy
{
    /// <summary>
    /// How items move between the Pack and a storage (Vault, Deep Vault), and inside one. Free of networking so it
    /// can be tested: the server runs it on scratch copies and keeps the result only when it returns true.
    /// An item moves as a whole <see cref="ItemStack"/>, so what it carries of its own goes with it.
    /// </summary>
    public static class StorageRules
    {
        public const int PageSize = 48;
        public const int PageCount = 2;
        public const int Capacity = PageSize * PageCount;

        public const string ReasonNotThatMany = "There aren't that many.";
        public const string ReasonOccupied = "That cell holds something else. Move the whole stack to swap them.";

        /// <summary>
        /// Moves <paramref name="count"/> of the stack in <paramref name="from"/>[<paramref name="fromCell"/>]
        /// (0 = all of it) to <paramref name="to"/>[<paramref name="toCell"/>], or to wherever it fits when
        /// <paramref name="toCell"/> is negative. On a chosen cell it fills an empty one, tops up a matching stack
        /// (what does not fit stays behind), or swaps with a different item when the whole stack is moved. With no
        /// cell chosen all of it must fit, or nothing moves and <paramref name="fullReason"/> is the reason.
        /// The two arrays may be the same one.
        /// </summary>
        public static bool TryTransfer(ItemStack[] from, int fromCell, ItemStack[] to, int toCell, int count,
            IItemLookup lookup, string fullReason, out string reason)
        {
            reason = null;
            if (from == null || to == null || fromCell < 0 || fromCell >= from.Length || toCell >= to.Length)
            {
                reason = InventoryRules.ReasonInvalidLocation;
                return false;
            }
            ItemStack source = from[fromCell];
            if (source.IsEmpty) { reason = InventoryRules.ReasonEmpty; return false; }
            ItemDefinition item = lookup != null ? lookup.Get(source.itemId) : null;
            if (item == null) { reason = InventoryRules.ReasonUnknownItem; return false; }
            if (count <= 0) count = source.count;
            if (count > source.count) { reason = ReasonNotThatMany; return false; }
            int maxStack = Math.Max(1, (int)item.maxStack);
            bool same = ReferenceEquals(from, to);

            if (toCell < 0)
            {
                if (same) { reason = InventoryRules.ReasonInvalidLocation; return false; }
                ItemStack moving = source.WithCount(count);
                if (RoomFor(to, moving, maxStack) < count) { reason = fullReason; return false; }
                InventoryRules.TryAdd(to, lookup, moving);
                from[fromCell] = source.WithCount(source.count - count);
                return true;
            }

            if (same && toCell == fromCell) return true;
            ItemStack target = to[toCell];
            if (target.IsEmpty)
            {
                to[toCell] = source.WithCount(count);
                from[fromCell] = source.WithCount(source.count - count);
                return true;
            }
            if (target.StacksWith(source) && maxStack > 1 && target.count < maxStack)
            {
                int take = Math.Min(count, maxStack - target.count);
                to[toCell] = target.WithCount(target.count + take);
                from[fromCell] = source.WithCount(source.count - take);
                return true;
            }
            if (count != source.count) { reason = ReasonOccupied; return false; }
            to[toCell] = source;
            from[fromCell] = target;
            return true;
        }

        /// <summary>How many of a stack the cells can still take: room on matching stacks plus every empty cell.</summary>
        public static int RoomFor(ItemStack[] cells, in ItemStack stack, int maxStack)
        {
            long room = 0;
            for (int i = 0; i < cells.Length; i++)
            {
                if (cells[i].IsEmpty) room += maxStack;
                else if (maxStack > 1 && cells[i].StacksWith(stack)) room += Math.Max(0, maxStack - cells[i].count);
            }
            return (int)Math.Min(int.MaxValue, room);
        }

        public static int Used(ItemStack[] cells)
        {
            int used = 0;
            for (int i = 0; i < cells.Length; i++) if (!cells[i].IsEmpty) used++;
            return used;
        }
    }
}
