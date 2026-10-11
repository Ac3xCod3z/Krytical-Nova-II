using KryticalNova.Factions;
using KryticalNova.Items;
using UnityEngine;

namespace KryticalNova.Housing
{
    /// <summary>
    /// What an item is as a furnishing: the model that is set down, how big it is, what it stands on. One per
    /// furnishing item, made by the catalog builder from a vendor prefab (which is never changed); the numbers
    /// here are measured from the model, with a table of hand corrections for what its name gets wrong.
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
        [Tooltip("The vendor prefab set down in the world. Its colliders are not used.")]
        public GameObject prefab;
        [Tooltip("The model's box in its own space, measured by the builder.")]
        public Vector3 boundsCenter;
        public Vector3 boundsSize = Vector3.one;
        [Tooltip("Other pieces may stand on its top (a table, a shelf, a crate).")]
        public bool hasTop;
        [Tooltip("Small pieces cast no shadow.")]
        public bool castsShadow = true;
        [Tooltip("A lamp, a candle, a brazier: it carries a light once Halls have lights.")]
        public bool lights;

        [Header("Budget")]
        [Tooltip("What it takes of a lawn's or a room's budget.")]
        [Min(1)] public int budgetCost = 1;

        public Bounds LocalBounds => new Bounds(boundsCenter, boundsSize);
        /// <summary>Half the width and depth of its footprint.</summary>
        public Vector2 FootprintHalf => new Vector2(boundsSize.x, boundsSize.z) * 0.5f;
        /// <summary>The offset of its footprint's middle from its pivot.</summary>
        public Vector2 FootprintCenter => new Vector2(boundsCenter.x, boundsCenter.z);
        /// <summary>How far its lowest point is below its pivot (0 for a model that stands on its pivot).</summary>
        public float Bottom => boundsCenter.y - boundsSize.y * 0.5f;
        /// <summary>The height of its top above its pivot.</summary>
        public float Top => boundsCenter.y + boundsSize.y * 0.5f;
        public string DisplayName => item != null ? item.displayName : name;
    }
}
