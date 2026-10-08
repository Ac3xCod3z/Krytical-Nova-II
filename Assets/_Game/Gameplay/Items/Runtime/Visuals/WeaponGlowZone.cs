using KryticalNova.Rendering;
using UnityEngine;

namespace KryticalNova.Items.Visuals
{
    /// <summary>
    /// Which part of a weapon model glows when it is tempered: the blade and not the hilt, the head of an axe and
    /// not its shaft, a gun but not its grip, a bow but not its string. Sits on the weapon's model prefab
    /// (Items/Data/Assets/Prefabs/Weapons), in the model's own space where z runs from the grip to the tip.
    /// Worked out from the mesh by Krytical Nova ▸ Items ▸ 8. Bake Weapon Glow Zones; tick
    /// <see cref="keepAsSet"/> to keep a zone tuned by hand. A model without one glows all over.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WeaponGlowZone : MonoBehaviour
    {
        public GlowZone zone;
        [Tooltip("Meshes whose name holds one of these never glow, wherever they are (a bow's string).")]
        public string[] exclude;
        [Tooltip("Tuned by hand: the baker leaves this zone as it is.")]
        public bool keepAsSet;

        // The glowing part in amber, the part kept dark in red.
        private static readonly Color GlowColor = new Color(1f, 0.79f, 0.24f);
        private static readonly Color DarkColor = new Color(0.9f, 0.2f, 0.25f);

        private void OnDrawGizmosSelected()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            if (zone.mode == GlowZoneMode.Only)
            {
                Gizmos.color = GlowColor;
                Gizmos.DrawWireCube(zone.box.center, zone.box.size);
            }
            if (zone.mode != GlowZoneMode.None && zone.HasDark)
            {
                Gizmos.color = DarkColor;
                Gizmos.DrawWireCube(zone.dark.center, zone.dark.size);
            }
        }
    }
}
