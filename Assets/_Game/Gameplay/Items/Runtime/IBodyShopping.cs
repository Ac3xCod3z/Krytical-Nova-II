using System.Collections.Generic;

namespace KryticalNova.Items
{
    /// <summary>
    /// A player body's side of the NPC shops, for the entity netcode (M8 8.2.2): the network runs its owner's buy and sell requests on the
    /// server body, reads what it bought of limited items this week, and shows that on the owner's own view. Here, below Economy, so the
    /// network need not know Economy (as <see cref="IBodyStorage"/>).
    /// </summary>
    public interface IBodyShopping
    {
        void RunBuy(string vendor, ushort itemId, int count);
        void RunSell(int cell, int count, ItemStack expected, bool confirmed);

        /// <summary>The vendor week the purchases below belong to (NGO's netBoughtWeek).</summary>
        long BoughtWeek { get; }

        /// <summary>What was bought of each limited item that week (NGO's netBought).</summary>
        IReadOnlyDictionary<ushort, int> Bought { get; }

        /// <summary>The owner's own view: a new copy of the week begins (empty).</summary>
        void MirrorWeek(long week);

        /// <summary>The owner's own view: one item's count of that week.</summary>
        void MirrorBought(ushort itemId, int count);
    }
}
