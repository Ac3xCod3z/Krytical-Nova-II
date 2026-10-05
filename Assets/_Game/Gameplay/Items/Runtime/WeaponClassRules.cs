using KryticalNova.Factions;
using KryticalNova.Progression;

namespace KryticalNova.Items
{
    /// <summary>
    /// Which weapon classes belong to which faction. Each faction has three (offense, defense, ranged) and a
    /// character may use any of its own faction's three; the class it is using is the class of the weapon it holds.
    /// </summary>
    public static class WeaponClassRules
    {
        public const string ReasonWrongDiscipline = "That discipline does not belong to this faction.";

        public static FactionId FactionOf(WeaponClassId id)
        {
            int index = WeaponClasses.FactionIndexOf(id);
            return index < 0 ? FactionId.None : (FactionId)(index + 1);
        }

        public static FactionMask MaskOf(WeaponClassId id) => FactionRules.MaskOf(FactionOf(id));

        public static bool BelongsTo(WeaponClassId id, FactionId faction) =>
            faction != FactionId.None && FactionOf(id) == faction;

        /// <summary>The faction's class for a role, or None for no faction.</summary>
        public static WeaponClassId ClassOf(FactionId faction, WeaponClassRole role)
        {
            if (faction == FactionId.None) return WeaponClassId.None;
            return (WeaponClassId)(((int)faction - 1) * WeaponClasses.PerFaction + (int)role + 1);
        }

        /// <summary>The class a character of this faction has when none was chosen: its offense class.</summary>
        public static WeaponClassId DefaultFor(FactionId faction) => ClassOf(faction, WeaponClassRole.Offense);

        /// <summary>A class this faction can actually have: the given one when it belongs, else the default.</summary>
        public static WeaponClassId Normalize(WeaponClassId id, FactionId faction) =>
            BelongsTo(id, faction) ? id : DefaultFor(faction);
    }
}
