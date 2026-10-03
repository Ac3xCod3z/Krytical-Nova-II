namespace KryticalNova.Progression
{
    /// <summary>
    /// Lets the skill hotbar hold and use bag items without referencing the item system. Implemented by the
    /// player's inventory; only items flagged as usable from the hotbar are accepted.
    /// </summary>
    public interface IHotbarItemSource
    {
        /// <summary>True when the item exists and is flagged as usable from the hotbar.</summary>
        bool CanSlotItem(ushort itemId);

        /// <summary>How many of the item are in the bag (0 leaves the slot assigned but unusable).</summary>
        int CountItem(ushort itemId);

        /// <summary>Short text for the slot.</summary>
        string GetItemLabel(ushort itemId);

        /// <summary>Uses one of the item from the bag. Returns false when there is none to use.</summary>
        bool UseItem(ushort itemId);
    }
}
