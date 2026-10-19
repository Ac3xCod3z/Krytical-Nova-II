using UnityEngine;

namespace KryticalNova.Items.Visuals
{
    /// <summary>
    /// The part of a weapon that hits, sized to the weapon held (its <c>hitboxCenter</c> and <c>hitboxSize</c>). The mount
    /// sizes it when the weapon changes; <c>WeaponHitbox</c> (Combat) is the one there is.
    /// </summary>
    public interface IWeaponReach
    {
        void Configure(Vector3 center, Vector3 size);
    }
}
