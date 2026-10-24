using UnityEngine;

namespace KryticalNova.GameFeel
{
    /// <summary>
    /// Every number of the beat glow (owner, 24 Oct 2026: the halo, pets' eyes and bodies, the town's glowing orb and
    /// signs and the lights round the map move to the beat of the song, only in a fight; not intrusive, but noticed).
    /// </summary>
    [CreateAssetMenu(menuName = "Krytical Nova/Game Feel/Beat Glow Settings", fileName = "BeatGlowSettings")]
    public sealed class BeatGlowSettings : ScriptableObject
    {
        [Header("The beat")]
        [Tooltip("How sharply the pulse falls away after each beat (higher: a shorter flash).")]
        [Range(1f, 16f)] public float sharpness = 5f;
        [Tooltip("The first beat of a bar pulses this much more.")]
        [Range(1f, 2f)] public float barLift = 1.4f;

        [Header("How far it swings (owner, 24 Oct 2026: more noticeable)")]
        [Tooltip("In a fight everything glowing settles this share lower between beats, so the beat rises out of a dip " +
                 "instead of only flashing up: the swing is the dip plus the boost.")]
        [Range(0f, 0.8f)] public float dip = 0.3f;
        [Tooltip("Glowing surfaces themselves (signs, the orb, neon, windows: every emissive material in the map) on the beat, as a share of their own glow.")]
        [Range(0f, 3f)] public float emissionBoost = 1f;

        [Header("What glows")]
        [Tooltip("The screen's bloom on the beat, as a share of the zone's own (0.5 = half again): every emissive thing (signs, " +
                 "the orb, pets' eyes, glows) brightens with it, for one shared cost.")]
        [Range(0f, 2f)] public float bloomBoost = 0.6f;
        [Tooltip("Point and spot lights near the player on the beat, as a share of their own intensity.")]
        [Range(0f, 1f)] public float lightBoost = 0.7f;
        [Tooltip("Lights further than this from the player (metres) are left alone.")]
        [Min(5f)] public float lightRadius = 45f;
        [Tooltip("Seconds between looks for the lights round the player.")]
        [Min(0.5f)] public float lightRescanSeconds = 4f;
        [Tooltip("Our own glowing shaders (the Nova) on the beat, as a share of their brightness (_KN_BeatPulse).")]
        [Range(0f, 1f)] public float shaderBoost = 0.4f;
    }
}
