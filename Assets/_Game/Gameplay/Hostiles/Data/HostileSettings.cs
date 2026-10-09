using UnityEngine;

namespace KryticalNova.Hostiles
{
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
