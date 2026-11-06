using UnityEngine;

namespace KryticalNova.Brains
{
    /// <summary>
    /// The numbers of the Brain shared by every mind (implementation plan s1.4). Read once at server start into
    /// <see cref="BrainConfig"/>. The asset is <see cref="BrainPaths.SettingsAsset"/>.
    /// </summary>
    [CreateAssetMenu(menuName = "Krytical Nova/Brains/Brain Settings", fileName = "BrainSettings")]
    public sealed class BrainSettings : ScriptableObject
    {
        [Tooltip("Ticks between thinks for tier 0 (near or fighting), 1 and 2. Tier 3 never thinks.")]
        public int[] tierIntervalTicks = { 1, 3, 15 };
        [Tooltip("Metres to the nearest player within which a brain is tier 0, 1, 2; farther is tier 3.")]
        public float[] tierRadius = { 12f, 40f, 120f };
        [Tooltip("Seconds a brain stays at its tier after players moved away (it goes up at once).")]
        public float tierDownHysteresisSeconds = 2f;
        [Tooltip("Seconds a brain counts as fighting after its last fight event.")]
        public float engagedLingerSeconds = 5f;
        [Tooltip("Cell size of the player grid, metres.")]
        public float playerCell = 16f;
        [Tooltip("Cell size of the brain grid, metres.")]
        public float brainCell = 4f;
        [Tooltip("Metres brains keep apart.")]
        public float separationRadius = 1.2f;
        [Tooltip("Path searches started per tick.")]
        public int pathSearchesPerTick = 8;
        [Tooltip("Most NavMesh nodes one path search may visit.")]
        public int pathNodeBudget = 2000;
        [Tooltip("Fewest seconds between two path searches of one brain.")]
        public float repathMinSeconds = 0.3f;
        [Tooltip("Metres the target must have moved before a brain searches again.")]
        public float repathTargetMoved = 1.5f;
        [Tooltip("Most waypoints a path keeps.")]
        public int maxWaypoints = 16;
        [Tooltip("Turn speed while fighting, degrees per second.")]
        public float engagedTurnDegPerSec = 540f;
        [Tooltip("Turn speed otherwise, degrees per second.")]
        public float idleTurnDegPerSec = 270f;
        [Tooltip("Seed of every brain's dice (with its stable id).")]
        public uint worldSeed = 0x4B4E;
    }

    public static class BrainPaths
    {
        public const string SettingsAsset = "Assets/_Game/Gameplay/Brains/Data/Assets/BrainSettings.asset";
    }
}
