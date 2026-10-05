using System;

namespace KryticalNova.Progression
{
    /// <summary>
    /// Tells progression which weapon class a character is using right now. The class is the class of the weapon it
    /// holds (its Discipline while unarmed), so it lives with the equipment; this interface keeps the skill system
    /// from referencing item code.
    /// </summary>
    public interface IWeaponClassSource
    {
        WeaponClassId ActiveClass { get; }
        /// <summary>Raised on every peer when the active class changes: (previous, current).</summary>
        event Action<WeaponClassId, WeaponClassId> ActiveClassChanged;
    }
}
