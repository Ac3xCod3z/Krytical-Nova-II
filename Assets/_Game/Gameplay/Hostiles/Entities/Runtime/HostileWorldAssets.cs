using KryticalNova.Brains;
using UnityEngine;

namespace KryticalNova.Hostiles.Entities
{
    /// <summary>
    /// What the Hostiles' entity brains need in a built server (Resources: <see cref="ResourcePath"/>; written by Krytical Nova ▸
    /// Hostiles ▸ Build DOTS Assets): the Brain's and the Hostiles mind's settings and the server body catalog.
    /// </summary>
    public sealed class HostileWorldAssets : ScriptableObject
    {
        public const string ResourcePath = "HostileWorldAssets";
        public BrainSettings brain;
        public HostileBrainSettings hostiles;
        public HostileBodyCatalog bodies;
    }
}
