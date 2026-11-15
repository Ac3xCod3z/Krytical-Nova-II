using System;
using System.Collections.Generic;
using UnityEngine;

namespace KryticalNova.Items
{
    /// <summary>
    /// Which 3D model an item shows while it lies on the ground, and how big it is (owner, 14 Nov 2026: the real drop
    /// models replace the stand-in parcel; small things smaller, big things bigger). Each model row names a ready prefab
    /// (fit, turned and lifted by DropModelLibraryBuilder from <see cref="Model.size"/> and <see cref="Model.tilt"/>);
    /// the item rows say which model each item uses. An item with no row keeps the stand-in parcel.
    /// Presentation only: the server never reads it to decide anything.
    /// </summary>
    [CreateAssetMenu(menuName = "Krytical Nova/Items/Drop Model Library", fileName = "DropModelLibrary")]
    public sealed class DropModelLibrary : ScriptableObject
    {
        [Serializable]
        public struct Model
        {
            [Tooltip("The model's name (its folder under Items/Art/DropModels).")]
            public string name;
            [Tooltip("The ready drop prefab the builder makes from the model: centred on the drop, at its size, turned and lifted.")]
            public GameObject prefab;
            [Tooltip("Largest side in metres as it lies on the ground. The stand-in parcel is 0.42.")]
            [Min(0.05f)] public float size;
            [Tooltip("For a long thing: degrees off upright it lies at (0 = keep the pose it was modelled in). The builder first lays it flat from its own shape, long side along X, then raises one end to this angle; 70 = a 20-degree rise.")]
            [Range(0f, 90f)] public float tilt;
        }

        [Serializable]
        public struct ItemRow
        {
            public ushort itemId;
            public byte model;
        }

        [Tooltip("The stand-in parcel's size in metres: the size a typical item reads at.")]
        [Min(0.05f)] public float referenceSize = 0.42f;
        [Tooltip("The largest any drop may be, so nothing on the ground blocks the view.")]
        [Min(0.1f)] public float maxSize = 0.9f;
        [Tooltip("The centre height of a drop above the ground, in metres (the stand-in's hover).")]
        [Min(0f)] public float hover = 0.28f;
        [Tooltip("The lowest point of a model, bobbing down, stays this far above the ground.")]
        [Min(0f)] public float groundClearance = 0.04f;
        [Tooltip("How much of the rarity colour a real model takes (0 = its own colours, 1 = the full tint the parcel gets).")]
        [Range(0f, 1f)] public float rarityTint = 0.3f;

        public Model[] models = Array.Empty<Model>();
        public ItemRow[] items = Array.Empty<ItemRow>();

        [NonSerialized] private Dictionary<ushort, int> lookup;

        /// <summary>The model row an item uses, or -1 for the stand-in parcel.</summary>
        public int ModelIndexFor(ushort itemId)
        {
            if (lookup == null)
            {
                lookup = new Dictionary<ushort, int>(items.Length);
                foreach (ItemRow row in items)
                    if (row.model < models.Length && models[row.model].prefab != null) lookup[row.itemId] = row.model;
            }
            return lookup.TryGetValue(itemId, out int index) ? index : -1;
        }

        private void OnValidate() => lookup = null;
    }
}
