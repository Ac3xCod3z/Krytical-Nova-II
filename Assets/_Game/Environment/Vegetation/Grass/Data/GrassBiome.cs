using System;
using System.Collections.Generic;
using UnityEngine;

namespace KryticalNova.Vegetation.Grass
{
    /// <summary>
    /// A mix of grass types for one kind of ground (meadow, mowed lawn, ...). The grass mask says which biome a spot
    /// belongs to; each grid candidate then picks one entry by weight, with optional patchy clumping.
    /// </summary>
    [CreateAssetMenu(menuName = "Krytical Nova/World/Grass/Grass Biome")]
    public sealed class GrassBiome : ScriptableObject
    {
        public const int MaxEntries = 6;

        [Serializable]
        public sealed class Entry
        {
            public GrassType type;
            [Min(0f)] public float weight = 1f;
            [Tooltip("0 = evenly mixed, 1 = grows in distinct stands.")]
            [Range(0f, 1f)] public float patchiness = 0.4f;
            [Tooltip("Size of those stands in metres.")]
            [Min(1f)] public float patchSize = 12f;
        }

        [Tooltip("Overall density multiplier for this biome.")]
        [Range(0f, 1.5f)] public float density = 1f;
        public List<Entry> entries = new List<Entry>();
    }
}
