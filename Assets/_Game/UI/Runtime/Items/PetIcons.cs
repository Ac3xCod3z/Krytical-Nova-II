using KryticalNova.Items;
using KryticalNova.Pets;
using UnityEngine;

namespace KryticalNova.UI
{
    /// <summary>
    /// The picture of a pet item as the pet is now. A pet that changes its model as it grows has an icon for each
    /// evolution (<see cref="PetStageLook.icon"/>, baked by PetStageIconBaker); the item's own icon is the first
    /// form's, and stands for any stage that has none of its own. Everything that is not a pet shows its own icon.
    /// </summary>
    public static class PetIcons
    {
        public static Sprite For(ItemDefinition item, in ItemStack stack)
        {
            if (item == null) return null;
            if (item.equipSlot != EquipSlot.Pet || stack.IsEmpty) return item.icon;
            PetCatalog pets = PlayerPet.Local != null ? PlayerPet.Local.Catalog : null;
            PetDefinition pet = pets != null ? pets.Get(item.itemId) : null;
            if (pet == null || pet.stages == null) return item.icon;
            int stage = PetRules.Stage(PetRules.GrowthPercent(PetItemState.Of(stack), pets.Tuning));
            Sprite own = stage >= 0 && stage < pet.stages.Length ? pet.stages[stage].icon : null;
            return own != null ? own : item.icon;
        }
    }
}
