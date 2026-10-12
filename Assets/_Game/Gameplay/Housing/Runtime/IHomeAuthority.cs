using System;
using System.Collections.Generic;

namespace KryticalNova.Housing
{
    /// <summary>
    /// Whoever decides what stands in a Hall. The Arrange tool never changes a layout itself: it hands every edit
    /// here and draws what comes back. In the Hall sandbox the authority is on the player's own machine
    /// (<see cref="LocalHomeAuthority"/>); when Halls are shared it is the server, and the tool stays as it is.
    /// </summary>
    public interface IHomeAuthority
    {
        /// <summary>The Hall as this machine knows it. Read it, never write it.</summary>
        HomeSave Home { get; }
        FurnishingCatalog Catalog { get; }
        HousingSettings Settings { get; }

        AreaShape Shape(HomeArea area);
        int Budget(HomeArea area);
        int BudgetUsed(HomeArea area);

        /// <summary>
        /// Asks for an edit, all of it or none of it. The answer comes through <see cref="Changed"/> or
        /// <see cref="Refused"/> with the number returned here (at once in the sandbox, later from a server).
        /// </summary>
        ushort Submit(HomeArea area, IReadOnlyList<HomeOp> ops);

        /// <summary>
        /// Asks for one of the Hall's four looks to be changed to a Facade or Finish out of the Furnishing Crate
        /// (item id 0: back to the faction's standard one). Answered through <see cref="LooksChanged"/> or
        /// <see cref="Refused"/>.
        /// </summary>
        ushort SubmitLook(HomeLook slot, ushort itemId);

        /// <summary>An edit was made: the area, the number its asker was given (0 for someone else's), what it did.</summary>
        event Action<HomeArea, ushort, IReadOnlyList<HomeChange>> Changed;
        /// <summary>One of the Hall's four looks was changed.</summary>
        event Action<HomeLook> LooksChanged;
        /// <summary>An edit was turned down, with the reason in words.</summary>
        event Action<ushort, string> Refused;
    }
}
