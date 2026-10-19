namespace KryticalNova.Core
{
    /// <summary>
    /// Carries out one <see cref="ItemUseEffect"/> for the player it sits on. The inventory decides that an item
    /// may be used and takes it from the Pack; the system that owns the effect does the rest, so the inventory
    /// never needs to know about pets (or whatever comes next). Runs where the game is decided. In Core so that
    /// systems below Items (Progression's skills and Serums) can carry out an effect without knowing the catalog.
    /// </summary>
    public interface IItemUseHandler
    {
        ItemUseEffect Effect { get; }

        /// <summary>Why the item would be wasted right now (shown to the player), or null when it can be used.</summary>
        string UseBlockedReason(UsedItem item);

        /// <summary>The item has been consumed: apply its effect.</summary>
        void ServerUse(UsedItem item);
    }
}
