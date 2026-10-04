using KryticalNova.Factions;

namespace KryticalNova.Items
{
    /// <summary>Who is trying to wear gear: the inventory asks this before an equip, the gear dictionary decides.</summary>
    public interface IGearWearer
    {
        FactionId Faction { get; }
        BodyFit Body { get; }
        /// <summary>Pack code of the wearer's own lineage (e.g. SCFI_ROBO). Starter gear comes from this pack.</summary>
        string GearPack { get; }
    }
}
