using System;
using UnityEngine;

namespace KryticalNova.Audio
{
    /// <summary>When an ambience bed plays: all day, in daylight, or after dark.</summary>
    public enum AmbienceTime { Always, Day, Night }

    /// <summary>
    /// The sound of one zone out of the music (Sound Bible, section 5): its beds, 2D loops under everything on the
    /// Ambience slider, each for the whole day, daylight or the dark. The zone's ambience player crossfades day and
    /// night with the sun's height, so dawn and dusk blend. Sounds on things in the zone (water, signs, a fallen
    /// rail bridge) are 3D emitters placed in the scene beside it (<see cref="AmbienceEmitter"/>), not listed here.
    /// </summary>
    [CreateAssetMenu(menuName = "Krytical Nova/Audio/Zone Ambience")]
    public sealed class ZoneAmbience : ScriptableObject
    {
        [Serializable]
        public struct Bed
        {
            public AudioClip clip;
            [Range(0f, 1f)] public float volume;
            public AmbienceTime when;
        }

        public Bed[] beds = new Bed[0];

        [Tooltip("How high the sun is (its direction's height, -1 to 1) where day and night are half and half; " +
                 "they blend over Twilight either side of it.")]
        [Range(-0.3f, 0.3f)] public float dawnHeight = 0f;
        [Tooltip("How far either side of Dawn Height the day and night beds blend (sun height). 0.15 is roughly " +
                 "the half hour of in-game twilight.")]
        [Range(0.01f, 0.5f)] public float twilight = 0.15f;
        [Tooltip("Seconds for the zone's ambience to come in when the zone loads.")]
        [Min(0.05f)] public float fadeInSeconds = 4f;
    }
}
