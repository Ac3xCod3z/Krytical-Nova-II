using System;
using System.Collections.Generic;
using KryticalNova.Factions;
using KryticalNova.Items.Visuals;
using KryticalNova.Progression;
using UnityEngine;

namespace KryticalNova.Items
{
    /// <summary>
    /// The gear table. Two lists:
    ///  - <b>lines</b> are what a designer edits: one row per run of level tiers that share a look (all Common
    ///    soldier armor, the first Rare look of the Glaive...). A row says who may use it (faction, body), which
    ///    look template it wears and, for weapons, which class it belongs to.
    ///  - <b>entries</b> are baked from the lines by the editor baker, one per item, and are what the game reads:
    ///    the equip rule, the GM gear commands and the starter kit all look gear up here by item ID.
    /// Only the item ID travels over the network; every peer resolves the rest from this asset.
    /// </summary>
    public sealed class GearDictionary : ScriptableObject
    {
        public const string ReasonWrongFaction = "Your faction cannot use this.";
        public const string ReasonWrongBody = "This does not fit your body.";

        /// <summary>First item ID of the ladder. Everything below is a legacy or hand-made item.</summary>
        public const ushort LadderIdBase = 10000;
        /// <summary>IDs reserved per line, so a band can grow without renumbering anything.</summary>
        public const int IdsPerLine = 16;

        /// <summary>One run of level tiers sharing a look. Hand-editable; the generator only adds missing rows.</summary>
        [Serializable]
        public struct Line
        {
            [Tooltip("Stable ID. Never change or reuse: item IDs are derived from it.")]
            public ushort lineId;
            [Tooltip("Asset-name token for this line's group: a pack tag (SOLD), a class (Glaive) or Ring / Amulet.")]
            public string tag;
            public EquipSlot slot;
            public ItemRarity rarity;
            [Tooltip("Index into GearStatTable.bands: which levels this line covers.")]
            public byte band;
            [Tooltip("Outfit pieces: the Sidekick pack this line belongs to (who drops it, whose starter set it is).")]
            public string pack;
            [Tooltip("Factions that may use this gear.")]
            public FactionMask factions;
            [Tooltip("Bodies this gear is modelled for.")]
            public BodyFit bodies;
            [Tooltip("Weapons: the class of this weapon.")]
            public WeaponClassId weaponClass;
            [Tooltip("The look every tier of this line wears: a look template item (its parts, icon and weapon model are copied).")]
            public ItemDefinition look;
            [Tooltip("Name shown on every tier of this line.")]
            public string displayName;
            [TextArea] public string description;

            public bool IsValid => lineId != 0;
            public ushort FirstItemId => (ushort)(LadderIdBase + (lineId - 1) * IdsPerLine);
        }

        /// <summary>One baked item. Generated; edit the line instead.</summary>
        [Serializable]
        public struct Entry
        {
            [Tooltip("The item in the ItemCatalog this row describes.")]
            public ushort itemId;
            public string name;
            public EquipSlot slot;
            public ItemRarity rarity;
            [Tooltip("Factions that may use this gear.")]
            public FactionMask factions;
            [Tooltip("Bodies this gear is modelled for.")]
            public BodyFit bodies;
            [Tooltip("Source pack code and variant number (which Sidekick outfit this is).")]
            public string pack;
            public byte variant;
            [Tooltip("Part IDs from the Sidekick part catalog that make up this piece.")]
            public ushort[] partIds;
            public byte itemLevel;
            public ushort lineId;
            public WeaponClassId weaponClass;

            public bool IsValid => itemId != 0;
        }

        [SerializeField] private SidekickPartCatalog parts;
        [SerializeField] private List<Line> lines = new List<Line>();
        [SerializeField] private List<Entry> entries = new List<Entry>();

        [NonSerialized] private Dictionary<ushort, int> byItem;

        public SidekickPartCatalog Parts => parts;
        public IReadOnlyList<Line> Lines => lines;
        public IReadOnlyList<Entry> Entries => entries;

        public bool TryGet(ushort itemId, out Entry entry)
        {
            entry = default;
            if (itemId == 0) return false;
            if (byItem == null)
            {
                byItem = new Dictionary<ushort, int>(entries.Count);
                for (int i = 0; i < entries.Count; i++) byItem[entries[i].itemId] = i;
            }
            if (!byItem.TryGetValue(itemId, out int index)) return false;
            entry = entries[index];
            return true;
        }

        public bool TryGetLine(ushort lineId, out Line line)
        {
            foreach (var candidate in lines)
            {
                if (candidate.lineId != lineId) continue;
                line = candidate;
                return true;
            }
            line = default;
            return false;
        }

        /// <summary>
        /// May this wearer use the item? Items that are not in the dictionary (consumables, pets) are open to
        /// everyone. A wearer with no faction (the default look in a test scene) is not restricted either.
        /// </summary>
        public bool CanUse(ushort itemId, FactionId faction, BodyFit body, out string reason)
        {
            reason = null;
            if (!TryGet(itemId, out var entry)) return true;
            if (faction != FactionId.None && !FactionRules.Includes(entry.factions, faction)) { reason = ReasonWrongFaction; return false; }
            if (body != BodyFit.None && (entry.bodies & body) == 0) { reason = ReasonWrongBody; return false; }
            return true;
        }

        /// <summary>The pieces for <paramref name="slot"/> this faction and body may use, in dictionary order.</summary>
        public void GetUsable(FactionId faction, BodyFit body, EquipSlot slot, List<Entry> results)
        {
            results.Clear();
            foreach (var entry in entries)
            {
                if (entry.slot != slot) continue;
                if (!FactionRules.Includes(entry.factions, faction) || (entry.bodies & body) == 0) continue;
                results.Add(entry);
            }
        }

        /// <summary>
        /// The item of one slot, rarity and level a wearer can use. Outfit pieces are matched by
        /// <paramref name="pack"/>, weapons by <paramref name="weaponClass"/>; rings and amulets by neither.
        /// <paramref name="level"/> snaps down to the nearest tier that exists (and up, when there is none below).
        /// </summary>
        public bool TryFindItem(EquipSlot slot, ItemRarity rarity, int level, FactionId faction, BodyFit body,
            string pack, WeaponClassId weaponClass, out Entry found)
        {
            found = default;
            bool any = false;
            int bestBelow = int.MinValue, bestAbove = int.MaxValue;
            Entry below = default, above = default;
            foreach (var entry in entries)
            {
                if (entry.slot != slot || entry.rarity != rarity) continue;
                if (faction != FactionId.None && !FactionRules.Includes(entry.factions, faction)) continue;
                if (body != BodyFit.None && (entry.bodies & body) == 0) continue;
                if (slot == EquipSlot.Weapon)
                {
                    if (weaponClass != WeaponClassId.None && entry.weaponClass != weaponClass) continue;
                }
                else if (!string.IsNullOrEmpty(pack) && !string.IsNullOrEmpty(entry.pack) && entry.pack != pack) continue;

                any = true;
                if (entry.itemLevel <= level && entry.itemLevel > bestBelow) { bestBelow = entry.itemLevel; below = entry; }
                if (entry.itemLevel > level && entry.itemLevel < bestAbove) { bestAbove = entry.itemLevel; above = entry; }
            }
            if (!any) return false;
            found = bestBelow != int.MinValue ? below : above;
            return true;
        }

        /// <summary>The lowest Common tier of a slot for this wearer: the piece a new character starts with.</summary>
        public bool TryFindStarter(EquipSlot slot, FactionId faction, BodyFit body, string pack, WeaponClassId weaponClass, out Entry found) =>
            TryFindItem(slot, ItemRarity.Common, 0, faction, body, pack, weaponClass, out found);

        /// <summary>The next tier up in the same line (the future "rank up"), or false at the top of the line.</summary>
        public bool TryGetNextTier(ushort itemId, out Entry next)
        {
            next = default;
            if (!TryGet(itemId, out var current) || current.lineId == 0) return false;
            return TryGet((ushort)(itemId + 1), out next) && next.lineId == current.lineId;
        }

        /// <summary>Editor generator entry point: the hand-editable rows.</summary>
        public void SetLines(SidekickPartCatalog partCatalog, List<Line> list)
        {
            parts = partCatalog;
            lines = list;
        }

        /// <summary>Editor baker entry point: the baked items.</summary>
        public void SetContents(SidekickPartCatalog partCatalog, List<Entry> list)
        {
            parts = partCatalog;
            entries = list;
            byItem = null;
        }

        private void OnEnable() => byItem = null;
        private void OnValidate() => byItem = null;
    }
}
