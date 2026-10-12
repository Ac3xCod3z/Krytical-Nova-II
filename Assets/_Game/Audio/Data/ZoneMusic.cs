using UnityEngine;

namespace KryticalNova.Audio
{
    /// <summary>
    /// The music of one zone. For now that is the fight's: the tracks a player hears, and keeps time to, while in
    /// combat there (Cadence, design 17). They run one after another in a round whether or not anyone is fighting,
    /// so that everyone in the zone who is fighting hears the same beat.
    /// </summary>
    [CreateAssetMenu(menuName = "Krytical Nova/Audio/Zone Music")]
    public sealed class ZoneMusic : ScriptableObject
    {
        [Tooltip("Played in this order, round and round. Each needs a steady beat.")]
        public MusicTrack[] combat = new MusicTrack[0];
        [Tooltip("Silence between one track and the next. There is no beat in it.")]
        [Min(0f)] public float gapSeconds = 2f;
        [Tooltip("Seconds for the music to come in when the player enters a fight.")]
        [Min(0.05f)] public float fadeInSeconds = 0.6f;
        [Tooltip("Seconds for the music to go out when the fight is over.")]
        [Min(0.05f)] public float fadeOutSeconds = 2.5f;
    }
}
