using System;
using System.Collections.Generic;
using KryticalNova.Factions;
using UnityEngine;

namespace KryticalNova.Housing
{
    /// <summary>
    /// Every furnishing in the game and every look a Hall can wear (Facades and Finishes), found by item id.
    /// Filled by the catalog builders.
    /// </summary>
    [CreateAssetMenu(menuName = "Krytical Nova/Housing/Furnishing Catalog")]
    public sealed class FurnishingCatalog : ScriptableObject
    {
        [SerializeField] private List<FurnishingDefinition> furnishings = new List<FurnishingDefinition>();
        [SerializeField] private List<HallLookDefinition> looks = new List<HallLookDefinition>();

        [NonSerialized] private Dictionary<ushort, FurnishingDefinition> byId;
        [NonSerialized] private Dictionary<ushort, HallLookDefinition> lookById;

        public IReadOnlyList<FurnishingDefinition> Furnishings => furnishings;
        public IReadOnlyList<HallLookDefinition> Looks => looks;

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

        public HallLookDefinition GetLook(ushort itemId)
        {
            if (itemId == 0) return null;
            if (lookById == null)
            {
                lookById = new Dictionary<ushort, HallLookDefinition>(looks.Count);
                foreach (HallLookDefinition look in looks)
                    if (look != null && look.itemId != 0) lookById[look.itemId] = look;
            }
            return lookById.TryGetValue(itemId, out HallLookDefinition found) ? found : null;
        }

        public bool IsFurnishing(ushort itemId) => Get(itemId) != null;

        /// <summary>Whether an item is kept in a Furnishing Crate: a furnishing, or a look that is not its faction's standard one.</summary>
        public bool HeldInCrate(ushort itemId)
        {
            if (Get(itemId) != null) return true;
            HallLookDefinition look = GetLook(itemId);
            return look != null && !look.standard;
        }

        /// <summary>
        /// Whether a character of a faction may keep a Crate item: anyone may keep a furnishing or a Finish, and a
        /// Facade only its own faction.
        /// </summary>
        public bool MayKeep(ushort itemId, FactionId faction)
        {
            HallLookDefinition look = GetLook(itemId);
            return look == null || look.slot != HomeLook.Facade || look.faction == FactionId.None || look.faction == faction;
        }

        /// <summary>The furnishings of one faction, in catalog order.</summary>
        public void ForFaction(FactionId faction, List<FurnishingDefinition> into)
        {
            into.Clear();
            foreach (FurnishingDefinition furnishing in furnishings)
                if (furnishing != null && furnishing.faction == faction) into.Add(furnishing);
        }

        /// <summary>The looks of one faction for one of a Hall's four, in catalog order, the standard one first.</summary>
        public void LooksFor(FactionId faction, HomeLook slot, List<HallLookDefinition> into)
        {
            into.Clear();
            foreach (HallLookDefinition look in looks)
                if (look != null && look.faction == faction && look.slot == slot && look.standard) into.Add(look);
            foreach (HallLookDefinition look in looks)
                if (look != null && look.faction == faction && look.slot == slot && !look.standard) into.Add(look);
        }

        /// <summary>The look a new Hall of a faction wears, or null when the faction has none.</summary>
        public HallLookDefinition StandardLook(FactionId faction, HomeLook slot)
        {
            foreach (HallLookDefinition look in looks)
                if (look != null && look.faction == faction && look.slot == slot && look.standard) return look;
            return null;
        }

        /// <summary>What a Hall wears: the look it names, or its faction's standard one.</summary>
        public HallLookDefinition WornLook(FactionId faction, HomeLook slot, ushort itemId)
        {
            HallLookDefinition look = GetLook(itemId);
            return look != null && look.slot == slot ? look : StandardLook(faction, slot);
        }

        /// <summary>Editor builder entry point.</summary>
        public void SetContents(List<FurnishingDefinition> list)
        {
            furnishings = list;
            byId = null;
        }

        /// <summary>Editor builder entry point.</summary>
        public void SetLooks(List<HallLookDefinition> list)
        {
            looks = list;
            lookById = null;
        }

        private void OnEnable() { byId = null; lookById = null; }
        private void OnValidate() { byId = null; lookById = null; }
    }
}
