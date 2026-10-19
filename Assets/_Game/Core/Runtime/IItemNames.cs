namespace KryticalNova.Core
{
    /// <summary>
    /// The name a player knows an item by, from its id. Systems below Items (Progression's boosts) ask the inventory on
    /// their own object for it through this, so they need not know the item catalog.
    /// </summary>
    public interface IItemNames
    {
        /// <summary>The item's display name, or null when there is no such item.</summary>
        string NameOf(ushort itemId);
    }
}
