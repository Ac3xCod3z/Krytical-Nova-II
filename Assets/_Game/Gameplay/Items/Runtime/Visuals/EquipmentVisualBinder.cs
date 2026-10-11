using UnityEngine;

namespace KryticalNova.Items.Visuals
{
    /// <summary>Routes replicated equipment changes to the body, weapon and pet visuals. Runs identically on every peer.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerEquipment))]
    public sealed class EquipmentVisualBinder : MonoBehaviour
    {
        [SerializeField] private CharacterAppearance appearance;
        [SerializeField] private WeaponVisualMount weaponMount;
        [SerializeField] private PetFollower petFollower;

        private PlayerEquipment equipment;

        public CharacterAppearance Appearance => appearance;

        private void Awake()
        {
            equipment = GetComponent<PlayerEquipment>();
            if (appearance == null) appearance = GetComponentInChildren<CharacterAppearance>(true);
            if (weaponMount == null) weaponMount = GetComponentInChildren<WeaponVisualMount>(true);
            if (petFollower == null) petFollower = GetComponent<PetFollower>();
        }

        private void OnEnable()
        {
            equipment.SlotChanged += OnSlotChanged;
            equipment.TemperChanged += OnTemperChanged;
            equipment.LookChanged += Apply;
            ApplyAll();
        }

        private void OnDisable()
        {
            equipment.SlotChanged -= OnSlotChanged;
            equipment.TemperChanged -= OnTemperChanged;
            equipment.LookChanged -= Apply;
        }

        private void ApplyAll()
        {
            foreach (var slot in EquipSlots.All) Apply(slot);
            OnTemperChanged(EquipSlot.Weapon);
        }

        private void OnSlotChanged(EquipSlot slot, ushort previous, ushort next) => Apply(slot);

        /// <summary>The weapon glows in the colour of its Temper band, as strongly as its rarity allows.</summary>
        private void OnTemperChanged(EquipSlot slot)
        {
            if (slot != EquipSlot.Weapon || weaponMount == null) return;
            ItemDefinition weapon = equipment.GetDefinition(EquipSlot.Weapon);
            GearProgressionSettings settings = equipment.Progression;
            int temper = equipment.GetTemper(EquipSlot.Weapon);
            Color color = default;
            TemperGlowLook look = default;
            bool glows = weapon != null && settings != null
                && GearProgressionRules.TryGetGlow(settings.temper, temper, weapon.rarity, out color, out look);
            weaponMount.SetGlow(glows, color, look);
        }

        /// <summary>Draws a slot: the piece worn in it, or the Weave over that piece, in the slot's dye.</summary>
        private void Apply(EquipSlot slot)
        {
            if (equipment.Catalog == null) return; // not set up yet: leave the authored prefab look alone
            ItemDefinition worn = equipment.GetDefinition(slot);
            switch (slot)
            {
                case EquipSlot.Weapon:
                    // The look of the Weave, the reach of the weapon really held.
                    if (weaponMount != null)
                    {
                        weaponMount.SetWeapon(equipment.GetShownDefinition(slot), worn);
                        weaponMount.SetDye(equipment.Colourways, equipment.GetDye(slot));
                    }
                    break;
                case EquipSlot.Pet:
                    if (petFollower != null) petFollower.SetPet(worn);
                    break;
                case EquipSlot.Ring:
                case EquipSlot.Amulet:
                    break; // stats only
                default:
                    if (appearance != null) appearance.SetSlot(slot, equipment.GetShownDefinition(slot), equipment.GetDyeCells(slot));
                    if (slot == EquipSlot.Cape && weaponMount != null) weaponMount.SetCapeWorn(worn != null);
                    break;
            }
        }
    }
}
