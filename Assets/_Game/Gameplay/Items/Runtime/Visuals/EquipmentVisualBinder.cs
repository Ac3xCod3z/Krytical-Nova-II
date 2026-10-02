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
            ApplyAll();
        }

        private void OnDisable()
        {
            equipment.SlotChanged -= OnSlotChanged;
        }

        private void ApplyAll()
        {
            foreach (var slot in EquipSlots.All) Apply(slot, equipment.GetDefinition(slot));
        }

        private void OnSlotChanged(EquipSlot slot, ushort previous, ushort next) => Apply(slot, equipment.Catalog?.Get(next));

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
                    break;
            }
        }
    }
}
