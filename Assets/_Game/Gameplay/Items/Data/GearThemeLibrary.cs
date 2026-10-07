using System;
using System.Collections.Generic;
using UnityEngine;

namespace KryticalNova.Items
{
    /// <summary>How a theme spreads its colours over the body.</summary>
    public enum GearThemeStyle : byte
    {
        /// <summary>Jacket and plates in the primary colour over trousers, gloves and boots in the under colour.</summary>
        TwoPiece = 0,
        /// <summary>The primary colour leads from collar to boots; plates and the cape take the secondary colour.</summary>
        FullSuit = 1
    }

    /// <summary>
    /// The colour themes of the gear sets. A theme is a handful of role colours (edit these) and the 16x16 Sidekick
    /// colour grid worked out from them (baked: "Krytical Nova/Items/1. Generate Gear Looks From Synty" rebuilds it
    /// after a colour is changed). A worn piece writes its theme into the cells its own meshes paint, so every piece
    /// of a set wears the set's colours on any character, and a colour never lands on skin. IDs are append-only.
    /// </summary>
    public sealed class GearThemeLibrary : ScriptableObject
    {
        public const int Grid = 16;
        public const int CellCount = Grid * Grid;
        public const int MetalShades = 4;

        [Serializable]
        public struct Theme
        {
            [Tooltip("Stable ID baked into items. Never reuse or renumber; 0 means none.")]
            public ushort id;
            [Tooltip("The name a gear line asks for (ELVN_Sunlord).")]
            public string name;
            public GearThemeStyle style;
            [Tooltip("The set's main colour.")]
            public Color32 primary;
            [Tooltip("The second colour: panels, linings and, on a full suit, the plates and the cape.")]
            public Color32 secondary;
            [Tooltip("Under-suit, trousers, gloves and boots: usually the darkest of the three.")]
            public Color32 under;
            [Tooltip("A small pop of colour on the smallest details.")]
            public Color32 accent;
            [Tooltip("Metal, lightest to darkest. On machines and backpacks this is the plating itself: the second shade covers the most.")]
            public Color32[] metal;
            [Tooltip("Buckles, studs and jewellery.")]
            public Color32 trim;
            [Tooltip("Straps, rope, fur and stitching.")]
            public Color32 leather;
            [Tooltip("Lights, screens and gems.")]
            public Color32 glow;
            [Tooltip("Baked from the colours above: index = v * 16 + u, from the bottom-left.")]
            [HideInInspector] public Color32[] cells;

            public bool IsValid => id != 0 && cells != null && cells.Length == CellCount;
        }

        [SerializeField] private List<Theme> themes = new List<Theme>();
        [SerializeField] private ushort nextId = 1;

        [NonSerialized] private Dictionary<ushort, int> byId;

        public IReadOnlyList<Theme> Themes => themes;

        public bool TryGet(ushort id, out Theme theme)
        {
            theme = default;
            if (id == 0) return false;
            if (byId == null)
            {
                byId = new Dictionary<ushort, int>(themes.Count);
                for (int i = 0; i < themes.Count; i++) byId[themes[i].id] = i;
            }
            if (!byId.TryGetValue(id, out int index)) return false;
            theme = themes[index];
            return theme.IsValid;
        }

        public ushort IdOf(string themeName)
        {
            if (string.IsNullOrEmpty(themeName)) return 0;
            foreach (var theme in themes)
                if (string.Equals(theme.name, themeName, StringComparison.Ordinal)) return theme.id;
            return 0;
        }

        /// <summary>Editor bake: adds a theme (it gets the next ID) and returns its index.</summary>
        public int Add(Theme theme)
        {
            theme.id = nextId++;
            themes.Add(theme);
            byId = null;
            return themes.Count - 1;
        }

        /// <summary>Editor bake: writes a theme back after its cells were worked out.</summary>
        public void Set(int index, Theme theme)
        {
            themes[index] = theme;
            byId = null;
        }

        private void OnEnable() => byId = null;
        private void OnValidate() => byId = null;
    }
}
