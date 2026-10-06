using KryticalNova.Items;
using KryticalNova.Pets;
using KryticalNova.Progression;
using UnityEngine.UIElements;

namespace KryticalNova.UI
{
    /// <summary>
    /// The pet part of an item tooltip: stage pips, growth and hunger meters and the bonus the pet gives. Everything
    /// is read from the pet item itself (<see cref="PetItemState"/>), so the same block describes a pet in the Pet
    /// slot, in the Pack, or anywhere else its tooltip is shown. Hidden for every other item.
    /// </summary>
    public sealed class PetTooltip
    {
        private const string PipClass = "pet-pip";
        private const string PipReachedClass = "pet-pip--reached";
        private const string LowClass = "pet-meter-fill--low";
        private const string EmptyClass = "pet-meter-fill--empty";
        private const string StarvingClass = "pet-panel--starving";
        private const float LowHungerPercent = 20f;

        private readonly VisualElement block, pips, growthFill, hungerFill;
        private readonly Label stageLabel, bonusLabel, growthValue, hungerValue;

        /// <param name="tooltip">The item tooltip that holds the "tooltip-pet" block (ItemWindows.uxml).</param>
        public PetTooltip(VisualElement tooltip)
        {
            block = tooltip?.Q("tooltip-pet");
            if (block == null) return;
            pips = block.Q("tooltip-pet-pips");
            stageLabel = block.Q<Label>("tooltip-pet-stage");
            bonusLabel = block.Q<Label>("tooltip-pet-bonus");
            growthFill = block.Q("tooltip-pet-growth-fill");
            hungerFill = block.Q("tooltip-pet-hunger-fill");
            growthValue = block.Q<Label>("tooltip-pet-growth-value");
            hungerValue = block.Q<Label>("tooltip-pet-hunger-value");
            BuildPips(pips);
            Hide();
        }

        public void Hide()
        {
            if (block != null) block.style.display = DisplayStyle.None;
        }

        /// <summary>
        /// Shows the block for a pet item and returns the line that goes under it (what the pet gives per percent
        /// and at full growth). Returns null, with the block hidden, when the item is not a pet.
        /// </summary>
        public string Show(ItemDefinition item, in ItemStack stack)
        {
            Hide();
            if (block == null || item == null || item.equipSlot != EquipSlot.Pet) return null;
            PetCatalog pets = PlayerPet.Local != null ? PlayerPet.Local.Catalog : null;
            PetDefinition pet = pets != null ? pets.Get(item.itemId) : null;
            if (pet == null) return null;

            PetTuning tuning = pets.Tuning;
            PetItemState state = PetItemState.Of(stack);
            int growth = PetRules.GrowthPercent(state, tuning);
            int stage = PetRules.Stage(growth);
            float exact = PetRules.GrowthExact(state, tuning);
            float hunger = state.HungerPercent;

            SetPips(pips, stage);
            stageLabel.text = pet.StageLabel(stage).ToUpperInvariant();
            bonusLabel.text = state.IsFed
                ? StatDisplay.FormatModifier(pet.stat, ModifierType.Flat, PetRules.StatBonus(pet.maxValue, growth, true))
                : "STARVING: BONUS OFF";
            block.EnableInClassList(StarvingClass, !state.IsFed);

            growthFill.style.width = Length.Percent(exact);
            growthValue.text = $"{exact:0.00}%";
            hungerFill.style.width = Length.Percent(hunger);
            hungerFill.EnableInClassList(LowClass, hunger > 0f && hunger <= LowHungerPercent);
            hungerFill.EnableInClassList(EmptyClass, hunger <= 0f);
            hungerValue.text = $"{hunger:0.0}%";
            block.style.display = DisplayStyle.Flex;

            return $"{StatDisplay.FormatModifier(pet.stat, ModifierType.Flat, pet.maxValue / PetRules.GrowthSteps)} per 1% growth  ·  " +
                   $"{StatDisplay.FormatModifier(pet.stat, ModifierType.Flat, pet.maxValue)} at 100%" +
                   (state.IsFed ? string.Empty : "\nFeed it a Pet Ration: a starving pet gives no bonus and earns no XP.");
        }

        public static void BuildPips(VisualElement host)
        {
            if (host == null) return;
            host.Clear();
            for (int i = 0; i < PetRules.StageCount; i++)
            {
                var pip = new VisualElement { pickingMode = PickingMode.Ignore };
                pip.AddToClassList(PipClass);
                host.Add(pip);
            }
        }

        public static void SetPips(VisualElement host, int stage)
        {
            if (host == null) return;
            for (int i = 0; i < host.childCount; i++) host[i].EnableInClassList(PipReachedClass, i <= stage);
        }
    }
}
