using UnityEngine;

namespace KryticalNova.Autopilot
{
    /// <summary>
    /// The limits of Autopilot (hunting by itself) and Magnet (picking up by itself). What a player chooses within
    /// them is that player's own <see cref="AutopilotPreferences"/>. Change a number here, never in code.
    /// </summary>
    [CreateAssetMenu(menuName = "Krytical Nova/Autopilot/Autopilot Settings")]
    public sealed class AutopilotSettings : ScriptableObject
    {
        [Header("Access")]
        [Tooltip("Hours of Autopilot one Autopilot Chip adds.")]
        [Min(0.01f)] public float hoursPerChip = 5f;
        [Tooltip("On: the time only runs down while Autopilot is on. Off: it runs from the moment a Chip is used.")]
        public bool timeRunsOnlyWhileOn = true;
        [Tooltip("The most Autopilot time a character can hold, hours.")]
        [Min(1f)] public float maxHours = 720f;
        [Tooltip("Off: Autopilot only starts inside a Hostile Zone and stops on leaving it.")]
        public bool allowedOutsideHostileZones;

        [Header("Pace: never faster than a person")]
        [Tooltip("Seconds between two things Autopilot does: a random time between these.")]
        public Vector2 actionDelaySeconds = new Vector2(0.3f, 0.5f);
        [Tooltip("Seconds between Magnet's looks at the ground.")]
        [Min(0.05f)] public float magnetScanSeconds = 0.25f;

        [Header("What a player may choose")]
        [Tooltip("Hunt radius round the spot Autopilot was switched on, metres: the least (x) and the most (y).")]
        public Vector2 huntRadius = new Vector2(10f, 40f);
        [Tooltip("How many Hostiles to gather before the area skill: the most a player may ask for.")]
        [Range(1, 8)] public int maxGatherCount = 8;
        [Tooltip("Hostiles count as gathered within this many metres of the player.")]
        [Min(1f)] public float gatherRadius = 5f;
        [Tooltip("Metres past the Hostile Zone's edge at which the server switches Autopilot off.")]
        [Min(0f)] public float zoneExitMargin = 20f;

        public float SecondsPerChip => hoursPerChip * 3600f;
        public float MaxSeconds => maxHours * 3600f;
    }
}
