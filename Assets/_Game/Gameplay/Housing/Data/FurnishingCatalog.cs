using System;
using System.Collections.Generic;
using KryticalNova.Factions;
using UnityEngine;

namespace KryticalNova.Housing
{
    /// <summary>Every furnishing in the game, found by its item id. Filled by the catalog builder.</summary>
    [CreateAssetMenu(menuName = "Krytical Nova/Housing/Furnishing Catalog")]
    public sealed class FurnishingCatalog : ScriptableObject
    {
        [SerializeField] private List<FurnishingDefinition> furnishings = new List<FurnishingDefinition>();

        [NonSerialized] private Dictionary<ushort, FurnishingDefinition> byId;

        public IReadOnlyList<FurnishingDefinition> Furnishings => furnishings;

        public FurnishingDefinition Get(ushort itemId)
        {
            if (itemId == 0) return null;
            if (byId == null)
            {
                byId = new Dictionary<ushort, FurnishingDefinition>(furnishings.Count);
                foreach (FurnishingDefinition furnishing in furnishings)
                    if (furnishing != null && furnishing.itemId != 0) byId[furnishing.itemId] = furnishing;
            }
            return byId.TryGetValue(itemId, out FurnishingDefinition found) ? found : null;
        }

        public bool IsFurnishing(ushort itemId) => Get(itemId) != null;

        /// <summary>The furnishings of one faction, in catalog order.</summary>
        public void ForFaction(FactionId faction, List<FurnishingDefinition> into)
        {
            into.Clear();
            foreach (FurnishingDefinition furnishing in furnishings)
                if (furnishing != null && furnishing.faction == faction) into.Add(furnishing);
        }

        /// <summary>Editor builder entry point.</summary>
        public void SetContents(List<FurnishingDefinition> list)
        {
            furnishings = list;
            byId = null;
        }

        private void OnEnable() => byId = null;
        private void OnValidate() => byId = null;
    }
}
