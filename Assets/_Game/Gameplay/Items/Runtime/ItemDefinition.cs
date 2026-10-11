using System;
using System.Collections.Generic;
using KryticalNova.Progression;
using UnityEngine;

namespace KryticalNova.Items
{
    [Serializable]
    public struct ItemStatModifier
    {
        public StatType stat;
        public ModifierType modType;
        public float value;
    }

    /// <summary>"+N levels" to the skills of one weapon class (None = whichever class the wearer is using).</summary>
    [Serializable]
    public struct ItemSkillModifier
    {
        public WeaponClassId line;
        public byte levels;
    }

    /// <summary>
    /// One Sidekick skinned part. Bone names are extracted at edit time so runtime never instantiates the source FBX:
    /// the mesh's bone indices are remapped onto the character's skeleton by name.
    /// </summary>
    [Serializable]
    public struct SidekickPartRef
    {
        public BodyRegion region;
        public Mesh mesh;
        public string[] boneNames;
        [Tooltip("Optional dangle bones (*_dyn_*) a pruned character skeleton may lack, parent-first; created on demand.")]
        public ExtraBone[] extraBones;
        [Tooltip("Bare-chested torso: a feminine body also wears its species' chest wrap (Sidekick's uses_wrap flag).")]
        public bool usesWrap;

        public bool IsValid => mesh != null && boneNames != null && boneNames.Length > 0;
    }

    /// <summary>A bone's bind-pose placement under its parent, so it can be added to a skeleton that lacks it.</summary>
    [Serializable]
    public struct ExtraBone
    {
        public string name;
        public string parent;
        public Vector3 localPosition;
        public Quaternion localRotation;
        public Vector3 localScale;
    }

    [CreateAssetMenu(menuName = "Krytical Nova/Items/Item")]
    public sealed class ItemDefinition : ScriptableObject
    {
        [Tooltip("Stable network ID. Never reuse or renumber; 0 means empty.")]
        public ushort itemId;
        public string displayName = "New Item";
        [TextArea] public string description;
        public Sprite icon;
        [Tooltip("An item of a family that differs only in what it says shows this item's icon and has none of its own " +
                 "(the Skill Chips: one picture for the attack chips, one for the buff chips; owner, 11 Oct 2026).")]
        public ItemDefinition sharesIconOf;
        public ItemRarity rarity;
        public EquipSlot equipSlot;
        [Min(1)] public ushort maxStack = 1;
        [Tooltip("A faction's own item (a furnishing): only its characters may have it. It is never traded or sold by a Kiosk " +
                 "to another faction, and it goes in the Vault, never the Deep Vault an account's characters share. None = anyone's.")]
        public KryticalNova.Factions.FactionId boundFaction;

        [Header("Gear ladder (baked by GearStatBaker: edit GearDictionary lines and GearStatTable, not these)")]
        [Tooltip("Level of this piece, and the level a character needs to equip it. 0 = no requirement.")]
        public byte itemLevel;
        [Tooltip("The GearDictionary line this tier belongs to. 0 = not ladder gear.")]
        public ushort lineId;
        [Tooltip("Weapons: the class this weapon makes its wielder. None for everything else.")]
        public WeaponClassId weaponClass;
        [Tooltip("Primary stats (Attack Power on weapons, Defense on the rest). Tempering will scale these.")]
        public ItemStatModifier[] baseStats = Array.Empty<ItemStatModifier>();
        [Tooltip("Secondary stats from the ladder table.")]
        public ItemStatModifier[] secondaryStats = Array.Empty<ItemStatModifier>();
        [Tooltip("Bonus skill levels. Stored and shown; not applied to skill ranks yet.")]
        public ItemSkillModifier[] skillModifiers = Array.Empty<ItemSkillModifier>();

        [Header("Stats (applied while equipped; hand-made items)")]
        public List<ItemStatModifier> statModifiers = new List<ItemStatModifier>();

        [Header("Consumable (used from the bag; one is removed per use)")]
        [Min(0f)] public float restoreHealth;
        [Min(0f)] public float restoreChi;
        [Tooltip("What using this item does besides restoring HP / Flux. Carried out by the system that owns the effect.")]
        public ItemUseEffect useEffect;
        [Tooltip("Seconds before this player can use another consumable.")]
        [Min(0f)] public float useCooldown = 1f;
        [Tooltip("The item can be dragged onto a hotbar slot and used with that slot's key. Leave off for items that only live in the bag.")]
        public bool usableFromHotbar;
        [Tooltip("Short text drawn in the slot while the item has no icon sprite.")]
        public string placeholderGlyph;

        [Header("Visuals")]
        [Tooltip("Armor / Gloves / Boots / Cape: Sidekick part catalog IDs that replace the slot's body regions. " +
                 "The meshes are loaded on first use, so the catalog never holds every armor mesh in memory.")]
        public ushort[] appearancePartIds = Array.Empty<ushort>();
        [Tooltip("The catalog the part IDs above belong to.")]
        public KryticalNova.Items.Visuals.SidekickPartCatalog partCatalog;
        [Tooltip("Direct part references. Used only when no part IDs are set (hand-made items).")]
        public List<SidekickPartRef> appearanceParts = new List<SidekickPartRef>();
        [Tooltip("Outfit pieces: the colour theme of the set this piece belongs to. 0 = it takes its wearer's own colours.")]
        public ushort colorTheme;
        [Tooltip("The library the theme ID above belongs to.")]
        public GearThemeLibrary colorThemes;
        [Tooltip("Cells of the 16x16 colour grid this piece's meshes paint (index = v * 16 + u), skin and bone left out. Baked.")]
        public byte[] colorCells = Array.Empty<byte>();
        [Tooltip("Weapon: model parented under the weapon mount (local pose identity).")]
        public GameObject weaponPrefab;
        public Vector3 weaponLocalPosition;
        public Vector3 weaponLocalEuler;
        public Vector3 weaponLocalScale = Vector3.one;
        public Vector3 hitboxCenter = new Vector3(0f, 0.01f, 0.55f);
        public Vector3 hitboxSize = new Vector3(0.18f, 0.18f, 1.15f);
        [Tooltip("Weapon: worn as a pair. A second, mirrored copy sits in the left hand (sidearms, claws). Still one item with one set of stats.")]
        public bool paired;
        [Tooltip("Paired weapons: the left copy's pose in the off-hand socket. Both zero = the mirror image of the right hand's.")]
        public Vector3 offHandLocalPosition;
        public Vector3 offHandLocalEuler;
        [Tooltip("Weapon: held in the left hand while drawn (bows: the right hand draws the string). The pose above is then in the off-hand socket.")]
        public bool leftHanded;
        [Tooltip("Weapon: where it rides while it is not drawn.")]
        public WeaponCarry carry;
        [Tooltip("Weapon: its pose while carried, in the carry socket (the back sheath or the right hip). A pair's second copy mirrors it.")]
        public Vector3 carryLocalPosition;
        public Vector3 carryLocalEuler;
        [Tooltip("Pet: cosmetic follower spawned client-side.")]
        public GameObject petPrefab;
        public float petScale = 1f;
        [Tooltip("Where the pet hovers at its base size, relative to the owner (x right, y up, z forward). On a player, x and z only pick the side: how far out it sits comes from the pet settings' follow gap, and it rises as it grows.")]
        public Vector3 petOffset = new Vector3(0.45f, 0.75f, -0.15f); // player root is at capsule centre (~1 m)

        [NonSerialized] private List<SidekickPartRef> loadedParts;

        /// <summary>The Sidekick parts this item shows when worn. The same list instance is returned every call.</summary>
        public IReadOnlyList<SidekickPartRef> GetAppearanceParts()
        {
            if (appearancePartIds == null || appearancePartIds.Length == 0 || partCatalog == null) return appearanceParts;
            if (loadedParts == null)
            {
                loadedParts = new List<SidekickPartRef>(appearancePartIds.Length);
                KryticalNova.Items.Visuals.PartLibrary.LoadAll(partCatalog, appearancePartIds, loadedParts);
            }
            return loadedParts;
        }

        private void OnEnable() => loadedParts = null;
        private void OnValidate() => loadedParts = null;

        /// <summary>True for a baked tier of the gear ladder (as opposed to a consumable, a pet or a look template).</summary>
        public bool IsLadderGear => lineId != 0;
        public bool HasStats => (baseStats != null && baseStats.Length > 0) || (secondaryStats != null && secondaryStats.Length > 0)
                                || (statModifiers != null && statModifiers.Count > 0);

        public bool IsEquippable => equipSlot != EquipSlot.None;
        public bool IsStackable => maxStack > 1;
        /// <summary>Used up from the Pack: it restores something, or it has a use effect.</summary>
        public bool IsConsumable => restoreHealth > 0f || restoreChi > 0f || useEffect != ItemUseEffect.None;
        /// <summary>Only usable items can go on the hotbar, and only when flagged for it.</summary>
        public bool IsHotbarUsable => usableFromHotbar && IsConsumable;
    }
}
