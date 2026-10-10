using System;
using System.Collections.Generic;
using UnityEngine;

namespace KryticalNova.Items
{
    /// <summary>
    /// The glow rims of item icons: for each weapon icon, a second picture that is only the icon's outline, in
    /// white. An icon of a tempered weapon is shown with its rim over it, tinted with the colour of the glow band
    /// the piece's Temper % is in, so one rim serves every band and follows the bands if they are retuned. Baked by
    /// Krytical Nova ▸ Items ▸ 6. Bake Weapon Glow Rims; nothing here is edited by hand.
    /// </summary>
    [CreateAssetMenu(menuName = "Krytical Nova/Items/Item Glow Icons", fileName = "ItemGlowIcons")]
    public sealed class ItemGlowIcons : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            public Sprite icon;
            public Sprite rim;
        }

        [SerializeField] private Entry[] entries = Array.Empty<Entry>();

        private Dictionary<Sprite, Sprite> lookup;

        public int Count => entries.Length;
        public IReadOnlyList<Entry> Entries => entries;

        /// <summary>The rim of an icon, or null when it has none (not a weapon's icon, or not baked yet).</summary>
        public Sprite RimOf(Sprite icon)
        {
            if (icon == null) return null;
            if (lookup == null)
            {
                lookup = new Dictionary<Sprite, Sprite>(entries.Length);
                foreach (Entry entry in entries)
                    if (entry.icon != null && entry.rim != null) lookup[entry.icon] = entry.rim;
            }
            return lookup.TryGetValue(icon, out Sprite rim) ? rim : null;
        }

        /// <summary>Editor baker entry point.</summary>
        public void SetContents(Entry[] baked)
        {
            entries = baked ?? Array.Empty<Entry>();
            lookup = null;
        }

        private void OnEnable() => lookup = null;
    }
}
