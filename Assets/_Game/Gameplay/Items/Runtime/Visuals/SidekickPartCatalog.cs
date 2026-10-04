using System;
using System.Collections.Generic;
using UnityEngine;

namespace KryticalNova.Items.Visuals
{
    /// <summary>
    /// Every Sidekick part a character or a gear piece can be built from, keyed by a stable ID (that ID, not the list
    /// index, is what is saved and sent over the network). Holds no mesh references: parts live in Sidekick's
    /// Resources folder and are loaded by path on first use (<see cref="PartLibrary"/>), so a zone never keeps every
    /// mesh in memory. Baked by the editor tool; IDs are append-only.
    /// </summary>
    public sealed class SidekickPartCatalog : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            [Tooltip("Stable ID. Never reuse or renumber; 0 means none.")]
            public ushort id;
            [Tooltip("Sidekick part name, e.g. SK_SCFI_SOLD_01_10TORS_HU01.")]
            public string name;
            [Tooltip("Path under a Resources folder, without extension.")]
            public string resourcePath;
            public BodyRegion region;
            [Tooltip("Pack code from the part name, e.g. SCFI_SOLD or HUMN_BASE.")]
            public string pack;
            [Tooltip("Variant number inside the pack (the VV in the part name).")]
            public byte variant;
            [Tooltip("Species letters of the rig suffix: HU, EV, SN or RO.")]
            public string species;
            public bool usesWrap;

            public bool IsValid => id != 0;
        }

        [SerializeField] private List<Entry> parts = new List<Entry>();
        [SerializeField] private ushort nextId = 1;

        [NonSerialized] private Dictionary<ushort, int> byId;
        [NonSerialized] private Dictionary<string, int> byName;
        [NonSerialized] private Dictionary<(string pack, BodyRegion region, byte variant), int> byKey;

        public IReadOnlyList<Entry> Parts => parts;

        public bool TryGet(ushort id, out Entry entry)
        {
            entry = default;
            if (id == 0) return false;
            EnsureIndex();
            if (!byId.TryGetValue(id, out int index)) return false;
            entry = parts[index];
            return true;
        }

        public bool TryGetByName(string partName, out Entry entry)
        {
            entry = default;
            if (string.IsNullOrEmpty(partName)) return false;
            EnsureIndex();
            if (!byName.TryGetValue(partName, out int index)) return false;
            entry = parts[index];
            return true;
        }

        public ushort IdOf(string partName) => TryGetByName(partName, out var entry) ? entry.id : (ushort)0;

        /// <summary>The part of <paramref name="pack"/> in <paramref name="region"/> with the given variant number.</summary>
        public bool TryFind(string pack, BodyRegion region, byte variant, out Entry entry)
        {
            entry = default;
            if (string.IsNullOrEmpty(pack)) return false;
            EnsureIndex();
            if (!byKey.TryGetValue((pack, region, variant), out int index)) return false;
            entry = parts[index];
            return true;
        }

        /// <summary>Editor bake: adds a part or refreshes an existing one (matched by name), keeping its ID.</summary>
        public ushort Upsert(Entry entry)
        {
            EnsureIndex();
            if (byName.TryGetValue(entry.name, out int index))
            {
                entry.id = parts[index].id;
                parts[index] = entry;
            }
            else
            {
                entry.id = nextId++;
                parts.Add(entry);
            }
            byId = null;
            return entry.id;
        }

        private void EnsureIndex()
        {
            if (byId != null) return;
            byId = new Dictionary<ushort, int>(parts.Count);
            byName = new Dictionary<string, int>(parts.Count, StringComparer.Ordinal);
            byKey = new Dictionary<(string, BodyRegion, byte), int>(parts.Count);
            for (int i = 0; i < parts.Count; i++)
            {
                var p = parts[i];
                if (p.id == 0 || string.IsNullOrEmpty(p.name)) continue;
                byId[p.id] = i;
                byName[p.name] = i;
                // Left and right parts share pack + variant but differ by region, so the key stays unique.
                if (!string.IsNullOrEmpty(p.pack)) byKey[(p.pack, p.region, p.variant)] = i;
            }
        }

        private void OnEnable() => byId = null;
        private void OnValidate() => byId = null;
    }
}
