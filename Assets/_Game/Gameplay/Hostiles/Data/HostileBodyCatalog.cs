using System;
using UnityEngine;

namespace KryticalNova.Hostiles
{
    /// <summary>
    /// The hidden server body of each Hostile species for the entity netcode's server (M5.5d): the species' own prefab without its look
    /// or Emerald, with the entity brain instead. Written by Krytical Nova ▸ Hostiles ▸ Build DOTS Assets; used only where the server
    /// sets <see cref="HostilePool.BodyOf"/>, so the NGO game never sees it. The asset is <see cref="AssetPath"/>.
    /// </summary>
    [CreateAssetMenu(menuName = "Krytical Nova/Hostiles/Hostile Body Catalog", fileName = "HostileBodyCatalog")]
    public sealed class HostileBodyCatalog : ScriptableObject
    {
        public const string AssetPath = "Assets/_Game/Gameplay/Hostiles/Data/Assets/HostileBodyCatalog.asset";   // reached at run time through HostileWorldAssets (Resources)

        [Serializable]
        public struct Entry
        {
            public HostileNet species;
            public HostileNet body;
        }

        public Entry[] entries = Array.Empty<Entry>();

        /// <summary>The server body of a species prefab, or null when the catalog has none.</summary>
        public HostileNet BodyOf(HostileNet species)
        {
            if (species == null) return null;
            foreach (Entry e in entries)
                if (e.species == species) return e.body;
            return null;
        }
    }
}
