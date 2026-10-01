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
        public ItemRarity rarity;
        public EquipSlot equipSlot;
        [Min(1)] public ushort maxStack = 1;

        [Header("Stats (applied while equipped)")]
        public List<ItemStatModifier> statModifiers = new List<ItemStatModifier>();

        [Header("Visuals")]
        [Tooltip("Armor / Gloves / Boots / Cape: Sidekick parts that replace the slot's body regions.")]
        public List<SidekickPartRef> appearanceParts = new List<SidekickPartRef>();
        [Tooltip("Weapon: model parented under the weapon mount (local pose identity).")]
        public GameObject weaponPrefab;
        public Vector3 weaponLocalPosition;
        public Vector3 weaponLocalEuler;
        public Vector3 weaponLocalScale = Vector3.one;
        public Vector3 hitboxCenter = new Vector3(0f, 0.01f, 0.55f);
        public Vector3 hitboxSize = new Vector3(0.18f, 0.18f, 1.15f);
        [Tooltip("Pet: cosmetic follower spawned client-side.")]
        public GameObject petPrefab;
        public float petScale = 1f;
        [Tooltip("Hover point relative to the owner (x right, y up, z forward).")]
        public Vector3 petOffset = new Vector3(0.7f, 0.95f, -0.5f); // player root is at capsule centre (~1 m)

        public bool IsEquippable => equipSlot != EquipSlot.None;
        public bool IsStackable => maxStack > 1;
    }
}
