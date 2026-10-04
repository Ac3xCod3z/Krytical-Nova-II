using System;
using System.Collections.Generic;
using UnityEngine;

namespace KryticalNova.CharacterCreation
{
    /// <summary>
    /// Base colour palettes for Sidekick characters. A palette is the whole 16x16 colour grid (skin, hair, eyes, every
    /// outfit, attachment and material cell); a character starts from one and applies its own overrides on top.
    /// Baked from Sidekick's premade characters. IDs are append-only.
    /// </summary>
    public sealed class CharacterPaletteLibrary : ScriptableObject
    {
        public const int Grid = 16;
        public const int CellCount = Grid * Grid;

        [Serializable]
        public struct Palette
        {
            [Tooltip("Stable ID. Never reuse or renumber; 0 means none.")]
            public ushort id;
            public string name;
            [Tooltip("Lineage this palette was made for (0 = any).")]
            public byte lineage;
            [Tooltip("16x16 colour cells, row-major from the bottom-left: index = v * 16 + u.")]
            public Color32[] cells;

            public bool IsValid => id != 0 && cells != null && cells.Length == CellCount;
        }

        [SerializeField] private List<Palette> palettes = new List<Palette>();
        [SerializeField] private ushort nextId = 1;

        [NonSerialized] private Dictionary<ushort, int> byId;

        public IReadOnlyList<Palette> Palettes => palettes;

        public bool TryGet(ushort id, out Palette palette)
        {
            palette = default;
            if (id == 0) return false;
            if (byId == null)
            {
                byId = new Dictionary<ushort, int>(palettes.Count);
                for (int i = 0; i < palettes.Count; i++) byId[palettes[i].id] = i;
            }
            if (!byId.TryGetValue(id, out int index)) return false;
            palette = palettes[index];
            return palette.IsValid;
        }

        public ushort IdOf(string paletteName)
        {
            foreach (var p in palettes)
                if (string.Equals(p.name, paletteName, StringComparison.Ordinal)) return p.id;
            return 0;
        }

        /// <summary>Editor bake: adds a palette or refreshes an existing one (matched by name), keeping its ID.</summary>
        public ushort Upsert(Palette palette)
        {
            for (int i = 0; i < palettes.Count; i++)
            {
                if (!string.Equals(palettes[i].name, palette.name, StringComparison.Ordinal)) continue;
                palette.id = palettes[i].id;
                palettes[i] = palette;
                byId = null;
                return palette.id;
            }
            palette.id = nextId++;
            palettes.Add(palette);
            byId = null;
            return palette.id;
        }

        private void OnEnable() => byId = null;
        private void OnValidate() => byId = null;
    }
}
