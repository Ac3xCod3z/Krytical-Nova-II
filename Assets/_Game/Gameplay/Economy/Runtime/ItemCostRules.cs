using System.Collections.Generic;
using KryticalNova.Items;

namespace KryticalNova.Economy
{
    /// <summary>A number of one item that something costs: an Alloy for a Temper, the Chips for a Tuning.</summary>
    public readonly struct ItemCost
    {
        public readonly ushort ItemId;
        public readonly int Count;

        public ItemCost(ushort itemId, int count)
        {
            ItemId = itemId;
            Count = count;
        }
    }

    /// <summary>
    /// Paying for something in items, from more than one place: the Pack first, then the Vault, then the Deep Vault.
    /// Free of networking so it can be tested: the server runs it on scratch copies and keeps them only when it
    /// returns true. Only plain items count (no state of their own), so a cost never eats a worked piece of gear.
    /// </summary>
    public static class ItemCostRules
    {
        /// <summary>How many plain <paramref name="itemId"/> the cells hold.</summary>
        public static int Count(ItemStack[] cells, ushort itemId)
        {
            if (cells == null || itemId == 0) return 0;
            int count = 0;
            for (int i = 0; i < cells.Length; i++)
                if (cells[i].itemId == itemId && cells[i].data == 0) count += cells[i].count;
            return count;
        }

        /// <summary>
        /// Takes every cost out of the sources, emptying each source before the next is touched. All of it is
        /// there or nothing is taken. The same item named twice is owed twice.
        /// </summary>
        public static bool TrySpend(IReadOnlyList<ItemStack[]> sourcesInOrder, IReadOnlyList<ItemCost> costs)
        {
            if (costs == null || costs.Count == 0) return true;
            if (sourcesInOrder == null) return false;

            for (int i = 0; i < costs.Count; i++)
            {
                ItemCost cost = costs[i];
                if (cost.ItemId == 0 || cost.Count <= 0) continue;
                long owed = 0;
                for (int j = 0; j < costs.Count; j++)
                    if (costs[j].ItemId == cost.ItemId && costs[j].Count > 0) owed += costs[j].Count;
                long have = 0;
                for (int s = 0; s < sourcesInOrder.Count; s++) have += Count(sourcesInOrder[s], cost.ItemId);
                if (have < owed) return false;
            }

            for (int i = 0; i < costs.Count; i++)
            {
                ItemCost cost = costs[i];
                if (cost.ItemId == 0 || cost.Count <= 0) continue;
                int left = cost.Count;
                for (int s = 0; s < sourcesInOrder.Count && left > 0; s++) left -= Take(sourcesInOrder[s], cost.ItemId, left);
            }
            return true;
        }

        /// <summary>Takes up to <paramref name="count"/> out of the cells; returns how many it took.</summary>
        private static int Take(ItemStack[] cells, ushort itemId, int count)
        {
            if (cells == null) return 0;
            int taken = 0;
            for (int i = 0; i < cells.Length && taken < count; i++)
            {
                if (cells[i].itemId != itemId || cells[i].data != 0) continue;
                int take = System.Math.Min(count - taken, cells[i].count);
                cells[i] = cells[i].WithCount(cells[i].count - take);
                taken += take;
            }
            return taken;
        }
    }
}
