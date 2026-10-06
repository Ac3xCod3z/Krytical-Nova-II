using System;
using System.Collections.Generic;
using UnityEngine;

namespace KryticalNova.Pets
{
    /// <summary>Every pet in the game, looked up by the id of its item.</summary>
    [CreateAssetMenu(menuName = "Krytical Nova/Pets/Pet Catalog")]
    public sealed class PetCatalog : ScriptableObject
    {
        [SerializeField] private List<PetDefinition> pets = new List<PetDefinition>();
        [SerializeField] private PetSettings settings;

        [NonSerialized] private Dictionary<ushort, PetDefinition> byItem;

        public IReadOnlyList<PetDefinition> Pets => pets;
        public PetSettings Settings => settings;
        public PetTuning Tuning => settings != null ? settings.Tuning : PetTuning.Default;

        /// <summary>The pet an item is, or null when the item is not a pet.</summary>
        public PetDefinition Get(ushort itemId)
        {
            if (itemId == 0) return null;
            if (byItem == null)
            {
                byItem = new Dictionary<ushort, PetDefinition>(pets.Count);
                foreach (PetDefinition pet in pets)
                    if (pet != null && pet.itemId != 0) byItem[pet.itemId] = pet;
            }
            return byItem.TryGetValue(itemId, out PetDefinition found) ? found : null;
        }

        public void SetContents(List<PetDefinition> list, PetSettings petSettings)
        {
            pets = list;
            settings = petSettings;
            byItem = null;
        }

        private void OnEnable() => byItem = null;
        private void OnValidate() => byItem = null;
    }
}
