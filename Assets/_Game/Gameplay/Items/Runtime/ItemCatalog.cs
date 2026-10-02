using System;
using System.Collections.Generic;
using UnityEngine;

namespace KryticalNova.Items
{
    public interface IItemLookup
    {
        ItemDefinition Get(ushort itemId);
    }

    [Serializable]
    public struct StarterItem
    {
        public ItemDefinition item;
        [Min(1)] public ushort count;
    }

    /// <summary>
    /// Every item in the game, keyed by its stable <see cref="ItemDefinition.itemId"/> (that ID, not the list index,
    /// is what goes over the network). Also holds the per-session starter loadout until a save system exists.
    /// </summary>
    [CreateAssetMenu(menuName = "Krytical Nova/Items/Item Catalog")]
    public sealed class ItemCatalog : ScriptableObject, IItemLookup
    {
        [SerializeField] private List<ItemDefinition> items = new List<ItemDefinition>();
        [Header("Starter Loadout (granted on spawn)")]
        [SerializeField] private List<ItemDefinition> starterEquipped = new List<ItemDefinition>();
        [SerializeField] private List<StarterItem> starterBag = new List<StarterItem>();

        [NonSerialized] private Dictionary<ushort, ItemDefinition> byId;

        public IReadOnlyList<ItemDefinition> Items => items;
        public IReadOnlyList<ItemDefinition> StarterEquipped => starterEquipped;
        public IReadOnlyList<StarterItem> StarterBag => starterBag;

        public ItemDefinition Get(ushort itemId)
        {
            if (itemId == 0) return null;
            if (byId == null) Rebuild();
            return byId.TryGetValue(itemId, out var def) ? def : null;
        }

        /// <summary>Lookup by numeric ID, asset name, or display name (case-insensitive). Dev tooling only.</summary>
        public ItemDefinition Find(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return null;
            if (ushort.TryParse(query, out ushort id)) return Get(id);
            foreach (var def in items)
            {
                if (def == null) continue;
                if (string.Equals(def.name, query, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(def.displayName, query, StringComparison.OrdinalIgnoreCase))
                    return def;
            }
            return null;
        }

        public void SetContents(List<ItemDefinition> list, List<ItemDefinition> equipped, List<StarterItem> bag)
        {
            items = list;
            starterEquipped = equipped;
            starterBag = bag;
            byId = null;
        }

        private void Rebuild()
        {
            byId = new Dictionary<ushort, ItemDefinition>(items.Count);
            foreach (var def in items)
            {
                if (def == null || def.itemId == 0) continue;
                if (!byId.TryAdd(def.itemId, def))
                    Debug.LogError($"[ItemCatalog] Duplicate itemId {def.itemId}: '{def.name}' and '{byId[def.itemId].name}'.", this);
            }
        }

        private void OnEnable() => byId = null;

        private void OnValidate()
        {
            byId = null;
            var seen = new HashSet<ushort>();
            foreach (var def in items)
            {
                if (def == null) continue;
                if (def.itemId == 0) Debug.LogWarning($"[ItemCatalog] '{def.name}' has itemId 0 (reserved for empty).", def);
                else if (!seen.Add(def.itemId)) Debug.LogError($"[ItemCatalog] Duplicate itemId {def.itemId} on '{def.name}'.", def);
            }
        }
    }
}
