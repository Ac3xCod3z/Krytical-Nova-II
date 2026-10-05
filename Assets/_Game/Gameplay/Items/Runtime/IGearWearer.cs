using System;
using KryticalNova.Factions;
using KryticalNova.Progression;

namespace KryticalNova.Items
{
    /// <summary>Who is trying to wear gear: the inventory asks this before an equip, the gear dictionary decides.</summary>
    public interface IGearWearer
    {
        FactionId Faction { get; }
        BodyFit Body { get; }
        /// <summary>Pack code of the wearer's own lineage (e.g. SCFI_ROBO). Starter gear comes from this pack.</summary>
        string GearPack { get; }
        /// <summary>The weapon class chosen in the creator: the starting weapon, and the class while unarmed.</summary>
        WeaponClassId Discipline { get; }
        /// <summary>Raised on every peer when any of the above changes (the character's identity arrived).</summary>
        event Action WearerChanged;
    }
}
