using System;
using KryticalNova.Factions;
using KryticalNova.Progression;
using UnityEngine;

namespace KryticalNova.Items
{
    /// <summary>
    /// Which weapon class a character is using. The weapon in hand decides: equip another class's weapon and the
    /// class changes with it, at any time. With no weapon the class is the Discipline chosen in the creator.
    /// Nothing here is networked: every peer works the class out from the equipment and the identity it already
    /// receives, so a class change costs no traffic of its own. It also hands the skill system the skills this
    /// character can have (shared skills plus its faction's three kits) once the faction is known.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerEquipment))]
    public sealed class PlayerWeaponClass : MonoBehaviour, IWeaponClassSource
    {
        [SerializeField] private WeaponClassCatalog catalog;

        private PlayerEquipment equipment;
        private IGearWearer wearer;
        private PlayerCombatSkills skills;
        private WeaponClassId active;
        private FactionId skillsFaction;

        public event Action<WeaponClassId, WeaponClassId> ActiveClassChanged;

        public WeaponClassCatalog Catalog => catalog;
        public WeaponClassId ActiveClass => active;

        private void Awake()
        {
            equipment = GetComponent<PlayerEquipment>();
            wearer = GetComponent<IGearWearer>();
            skills = GetComponent<PlayerCombatSkills>();
        }

        private void OnEnable()
        {
            if (equipment != null) equipment.SlotChanged += OnSlotChanged;
            if (wearer != null) wearer.WearerChanged += Refresh;
            Refresh();
        }

        private void OnDisable()
        {
            if (equipment != null) equipment.SlotChanged -= OnSlotChanged;
            if (wearer != null) wearer.WearerChanged -= Refresh;
        }

        private void OnSlotChanged(EquipSlot slot, ushort previous, ushort next)
        {
            if (slot == EquipSlot.Weapon) Refresh();
        }

        /// <summary>Works the class out again from the weapon in hand and the character's identity.</summary>
        public void Refresh()
        {
            var faction = wearer != null ? wearer.Faction : FactionId.None;
            // The faction's skills, once it is known. Until then the prefab's own list stands (a test avatar).
            if (faction != FactionId.None && faction != skillsFaction && catalog != null && skills != null)
            {
                skillsFaction = faction;
                skills.SetAvailableSkills(catalog.SkillsFor(faction));
            }

            var weapon = equipment != null ? equipment.GetDefinition(EquipSlot.Weapon) : null;
            WeaponClassId next = weapon != null ? weapon.weaponClass : WeaponClassId.None;
            if (next == WeaponClassId.None && wearer != null) next = wearer.Discipline;
            if (next == active) return;

            var previous = active;
            active = next;
            ActiveClassChanged?.Invoke(previous, next);
        }
    }
}
