using UnityEngine;

namespace KryticalNova.Hostiles
{
    /// <summary>
    /// One hunting ground inside a map: a circle that holds one species at one level and nothing else. There is no
    /// spawn table on purpose, so a zone cannot mix levels.
    /// </summary>
    [CreateAssetMenu(menuName = "Krytical Nova/Hostiles/Hostile Zone")]
    public sealed class HostileZoneDefinition : ScriptableObject
    {
        [Tooltip("Stable id, also the name of its teleport point (mob-nw).")]
        public string id;
        public string displayName = "Hostile Zone";
        [Tooltip("Map position of the centre, metres: x east, y north, the town centre at (0, 0).")]
        public Vector2 centre;
        [Min(5f)] public float radius = 30f;
        [Tooltip("The one species that lives here.")]
        public HostileDefinition hostile;
        [Min(1)] public int maxAlive = 12;
        [Tooltip("The zone wakes while a player is within its radius plus this many metres.")]
        [Min(0f)] public float wakeMargin = 40f;

        [Header("Bruiser (optional)")]
        [Tooltip("At most 2 levels above the zone's species.")]
        public HostileDefinition bruiser;
    }
}
