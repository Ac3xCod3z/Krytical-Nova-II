using KryticalNova.Items;
using UnityEngine;

namespace KryticalNova.Hostiles
{
    /// <summary>One item a Hostile may drop besides its usual loot, and how often.</summary>
    [System.Serializable]
    public struct MaterialDrop
    {
        public ItemDefinition item;
        [Tooltip("Chance that a kill drops one, percent, before the level gap and the server drop rate.")]
        [Range(0f, 100f)] public float chancePercent;
        [Tooltip("Only Hostiles of this level and above drop it. 0 = any.")]
        [Min(0)] public int minLevel;
        [Tooltip("Only Hostiles up to this level drop it. 0 = no upper limit.")]
        [Min(0)] public int maxLevel;
    }

    /// <summary>
    /// The numbers every Hostile shares that are not its stats: how long a corpse lies and how it burns away,
    /// how far one may be pulled from its Hostile Zone, how many a map may hold. Change them here, never in code.
    /// </summary>
    [CreateAssetMenu(menuName = "Krytical Nova/Hostiles/Hostile Settings")]
    public sealed class HostileSettings : ScriptableObject
    {
        [Header("Corpse")]
        [Tooltip("Seconds a dead Hostile lies before it starts to burn away.")]
        [Min(0f)] public float corpseSeconds = 3f;
        [Tooltip("Seconds the burning away takes. After it the Hostile goes back to the pool.")]
        [Min(0.1f)] public float dissolveSeconds = 1.4f;
        [Tooltip("KryticalNova/Hostiles/Corpse Dissolve. Kept here so that a build includes it.")]
        public Shader dissolveShader;
        [Tooltip("The noise the body burns away along.")]
        public Texture2D dissolveNoise;
        [Tooltip("The glowing edge of the burn.")]
        [ColorUsage(false, true)] public Color burnColor = new Color(0.2f, 3f, 3.4f, 1f);
        [Tooltip("How wide the glowing edge is, as a share of the noise.")]
        [Range(0f, 0.3f)] public float burnWidth = 0.07f;
        [Tooltip("Repeats of the noise per metre: higher burns away in smaller flecks.")]
        [Min(0.05f)] public float noiseTiling = 0.8f;

        [Header("Drops (Creds are on the stat curve)")]
        [Tooltip("Where gear drops are picked from.")]
        public ItemCatalog catalog;
        [Tooltip("The Stims a Hostile may drop. One is picked at random.")]
        public ItemDefinition[] stims = System.Array.Empty<ItemDefinition>();
        [Tooltip("Chance that a kill drops a Stim, percent.")]
        [Range(0f, 100f)] public float stimChancePercent = 6f;
        [Tooltip("Chance that a Normal Hostile drops a piece of gear, percent.")]
        [Range(0f, 100f)] public float gearChancePercent = 2.5f;
        [Tooltip("Chance that a Bruiser drops a piece of gear, percent. It is of the best rarity its level has.")]
        [Range(0f, 100f)] public float bruiserGearChancePercent = 25f;
        [Tooltip("Gear drops are of an item level from this many levels under the Hostile up to its own.")]
        [Min(0)] public int gearLevelsBelow = 3;
        [Tooltip("What a kill may drop besides Creds, a Stim and gear: crafting materials (colour cells, Dossier Pages) and, " +
                 "rarely, what they make. Each row is rolled on its own for every kill. Placeholder chances of 11 Oct 2026.")]
        public MaterialDrop[] materialDrops = System.Array.Empty<MaterialDrop>();

        [Header("Hostile Zones")]
        [Tooltip("Hostiles a map may hold at once, in zones and out of them.")]
        [Min(1)] public int maxActiveHostiles = 300;
        [Tooltip("Seconds between a zone's checks: who is near, what is due, who has strayed.")]
        [Min(0.1f)] public float zoneTickSeconds = 0.5f;
        [Tooltip("A Hostile pulled this many metres past its zone's edge gives up, goes back and is healed.")]
        [Min(1f)] public float leashMetres = 15f;
        [Tooltip("A Hostile never appears closer than this to a player, metres.")]
        [Min(0f)] public float spawnClearance = 3f;
        [Tooltip("How far a Hostile wanders from where it appeared, metres.")]
        [Min(1)] public int wanderRadius = 8;
        [Tooltip("Hostiles a zone may bring back in one check, so a zone that wakes does not fill in a single frame.")]
        [Min(1)] public int spawnsPerTick = 4;

        /// <summary>Seconds from a death until the body has gone and the Hostile is back in the pool.</summary>
        public float CorpseLifetime => corpseSeconds + dissolveSeconds;
    }
}
