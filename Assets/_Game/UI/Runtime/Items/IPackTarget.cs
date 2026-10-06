using KryticalNova.Items;
using UnityEngine;

namespace KryticalNova.UI
{
    /// <summary>
    /// A window that takes items from the Pack while it is open: the Vault, a trade, a Kiosk being set up. The Pack
    /// (<see cref="GearUIController"/>) asks the open one first when an item is dropped or right-clicked, so each
    /// window decides what that means for it, and the Pack itself never learns about any of them.
    /// </summary>
    public interface IPackTarget
    {
        bool IsOpen { get; }

        /// <summary>What a right-click on a Pack item does while this window is open ("deposit", "offer"); shown in the tooltip.</summary>
        string QuickHint { get; }

        /// <summary>A Pack item was dropped at a point of the panel. True when this window took it.</summary>
        bool TryDrop(ItemLocation from, Vector2 panelPosition);

        /// <summary>A Pack item was right-clicked. True when this window took it.</summary>
        bool TryQuick(ItemLocation from);

        /// <summary>True while this window holds on to a Pack cell (it is offered or listed): the Pack greys it and leaves it alone.</summary>
        bool LocksCell(int cell);
    }
}
