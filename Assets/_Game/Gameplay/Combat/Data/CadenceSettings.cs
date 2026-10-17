using UnityEngine;

namespace KryticalNova.Combat
{
    /// <summary>
    /// Every number of Cadence (design 17): an attack skill whose key is pressed on the beat of the fight music hits
    /// a little harder, once. All of it is tuned here and nowhere in code.
    /// </summary>
    [CreateAssetMenu(menuName = "Krytical Nova/Combat/Cadence Settings")]
    public sealed class CadenceSettings : ScriptableObject
    {
        [Tooltip("Off: no press earns anything and the game plays as it did before Cadence.")]
        public bool enabled = true;

        [Header("The press")]
        [Tooltip("A press counts as on the beat when it is no further than this from one, either side, in milliseconds.")]
        [Range(20f, 250f)] public float windowMs = 90f;
        [Tooltip("Seconds an on-beat press is kept for its skill while that skill waits to be cast (a swing still playing, a walk into reach).")]
        [Min(0f)] public float keepSeconds = 1.5f;
        [Tooltip("The oldest a press may be when the server hears of it, in seconds: connection delay plus the player's own beat offset. Older is refused.")]
        [Range(0.1f, 2f)] public float maxPressAgeSeconds = 0.6f;

        [Header("The reward")]
        [Tooltip("Extra damage of the cast, in percent.")]
        [Range(0f, 50f)] public float bonusPercent = 6f;
        [Tooltip("Whether a cast on Cadence also hits other players harder (a Callout, a war).")]
        public bool againstPlayers = true;

        public float WindowSeconds => windowMs / 1000f;
        public float Multiplier => 1f + bonusPercent / 100f;
    }
}
