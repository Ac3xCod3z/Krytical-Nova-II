using UnityEngine;

namespace KryticalNova.Audio
{
    /// <summary>
    /// A piece of music the game keeps time to: the clip and where its beats are. The beat is measured outside the
    /// game (the sound-scout skill's tempo.py) and checked by ear; the game never listens, it counts: beat n is at
    /// <see cref="firstBeatSeconds"/> + n * <see cref="BeatSeconds"/>. Only a track with a steady tempo will do.
    /// </summary>
    [CreateAssetMenu(menuName = "Krytical Nova/Audio/Music Track")]
    public sealed class MusicTrack : ScriptableObject
    {
        public AudioClip clip;
        [Tooltip("What the track is called in GM tools.")]
        public string title;
        public double beatsPerMinute = 120.0;
        [Tooltip("Seconds into the clip of any one beat. Every other beat is counted from it.")]
        public double firstBeatSeconds;
        [Min(1)] public int beatsPerBar = 4;
        [Tooltip("Which beat is the first of a bar, counted from the beat at First Beat Seconds (0 = that beat itself).")]
        [Min(0)] public int barBeat;
        [Range(0f, 1f)] public float volume = 0.6f;

        public double BeatSeconds => 60.0 / System.Math.Max(30.0, beatsPerMinute);

        /// <summary>The clip's length in seconds, to the sample.</summary>
        public double Length => clip != null && clip.frequency > 0 ? (double)clip.samples / clip.frequency : 0.0;

        public string Title => string.IsNullOrEmpty(title) ? name : title;
    }
}
