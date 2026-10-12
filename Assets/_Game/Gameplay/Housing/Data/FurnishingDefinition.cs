using KryticalNova.Factions;
using KryticalNova.Items;
using UnityEngine;

namespace KryticalNova.Housing
{
    /// <summary>
    /// What an item is as a furnishing: the model that is set down, how big it is, what it stands on, and what of
    /// it stands in a walker's way. One per furnishing item, made by the catalog builder from a vendor prefab
    /// (which is never changed); the numbers here are measured from the model and held to the world's measure
    /// (`_Docs/Design/16_world_scale.md`), with a table of hand corrections for what its name gets wrong.
    /// </summary>
    public sealed class FurnishingDefinition : ScriptableObject
    {
        [Tooltip("The item this furnishing is carried, stored and traded as.")]
        public ItemDefinition item;
        [Tooltip("The item's id, kept here so a placed piece finds its furnishing without the item catalog.")]
        public ushort itemId;
        [Tooltip("The faction whose furnishing this is. Only its characters may have it, set it down or trade it.")]
        public FactionId faction;
        public FurnishingMount mount;
        public FurnishingPlace place;
        [Tooltip("What the palette files it under (Seating, Tables, Storage, Lighting, Decor, Plants, Rugs, Walls).")]
        public string category = "Decor";
        public FurnishingSource source;

        [Header("Model")]
        [Tooltip("The prefab set down in the world: the vendor's, or a variant of it made for this faction. Its colliders are not used.")]
        public GameObject prefab;
        [Tooltip("What kind of thing it is by the world's measure (chair, table, tree): the builder holds its size to that kind.")]
        public string kind;
        [Tooltip("What the vendor's model is multiplied by to hold it to the world's measure. 1 for a model that is already true.")]
        [Min(0.01f)] public float modelScale = 1f;
        [Tooltip("The turn the model is given inside the piece (what hangs on a wall is turned to face out from it).")]
        public Vector3 modelEuler;
        [Tooltip("The piece's box in its own space, measured by the builder after the scale and the turn above.")]
        public Vector3 boundsCenter;
        public Vector3 boundsSize = Vector3.one;
        [Tooltip("Other pieces may stand on its top (a table, a shelf, a crate).")]
        public bool hasTop;
        [Tooltip("Small pieces cast no shadow.")]
        public bool castsShadow = true;
        [Tooltip("A lamp, a candle, a brazier: it carries a light.")]
        public bool lights;
        [Tooltip("Where its light sits, in the piece's own space.")]
        public Vector3 lightOffset;

        [Header("Standing in the way")]
        [Tooltip("It stops a walker: a table, a wardrobe, a tree's trunk. A rug, a tuft of flowers or a vase does not.")]
        public bool blocks;
        [Tooltip("The part of it that is in a walker's way, measured over the height a person takes up: a tree's trunk, not its crown.")]
        public Vector3 solidCenter;
        public Vector3 solidSize;

        [Header("Budget")]
        [Tooltip("What it takes of a lawn's or a room's budget.")]
        [Min(1)] public int budgetCost = 1;

        public Bounds LocalBounds => new Bounds(boundsCenter, boundsSize);
        /// <summary>The box that stands in the way: its measured solid part, or the whole of it when none was measured.</summary>
        public Bounds Solid => solidSize.x > 0f && solidSize.z > 0f ? new Bounds(solidCenter, solidSize) : LocalBounds;
        /// <summary>Half the width and depth of the ground it takes up.</summary>
        public Vector2 FootprintHalf { get { Vector3 size = Solid.size; return new Vector2(size.x, size.z) * 0.5f; } }
        /// <summary>The offset of its footprint's middle from its pivot.</summary>
        public Vector2 FootprintCenter { get { Vector3 center = Solid.center; return new Vector2(center.x, center.z); } }
        /// <summary>How far its lowest point is below its pivot (0 for a model that stands on its pivot).</summary>
        public float Bottom => boundsCenter.y - boundsSize.y * 0.5f;
        /// <summary>The height of its top above its pivot.</summary>
        public float Top => boundsCenter.y + boundsSize.y * 0.5f;
        /// <summary>How far its back is behind its pivot: what touches the wall of a piece that hangs on one.</summary>
        public float Back => boundsCenter.z - boundsSize.z * 0.5f;
        public string DisplayName => item != null ? item.displayName : name;
    }
}
