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
            ApplyAll();
        }

        private void OnDisable()
        {
            equipment.SlotChanged -= OnSlotChanged;
            equipment.TemperChanged -= OnTemperChanged;
        }

        private void ApplyAll()
        {
            foreach (var slot in EquipSlots.All) Apply(slot, equipment.GetDefinition(slot));
            OnTemperChanged(EquipSlot.Weapon);
        }

        private void OnSlotChanged(EquipSlot slot, ushort previous, ushort next) => Apply(slot, equipment.Catalog?.Get(next));

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

        private void Apply(EquipSlot slot, ItemDefinition def)
        {
            if (equipment.Catalog == null) return; // not set up yet: leave the authored prefab look alone
            switch (slot)
            {
                case EquipSlot.Weapon:
                    if (weaponMount != null) weaponMount.SetWeapon(def);
                    break;
                case EquipSlot.Pet:
                    if (petFollower != null) petFollower.SetPet(def);
                    break;
                case EquipSlot.Ring:
                case EquipSlot.Amulet:
                    break; // stats only
                default:
                    if (appearance != null) appearance.SetSlot(slot, def);
                    if (slot == EquipSlot.Cape && weaponMount != null) weaponMount.SetCapeWorn(def != null);
                    break;
            }
        }
    }
}
