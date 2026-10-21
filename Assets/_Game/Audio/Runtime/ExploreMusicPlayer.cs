using UnityEngine;

namespace KryticalNova.Audio
{
    /// <summary>
    /// Plays the zone's walking-about music (<see cref="ZoneMusic.explore"/>) on this machine: its tracks one after
    /// another, round and round, each player on their own (it keeps no shared clock and no beat). It steps aside while
    /// the fight music plays (<see cref="CombatMusicPlayer.Live"/>) and comes back slowly after the fight. A zone with
    /// no walking-about music is quiet out of a fight, as before. Client-only: a dedicated server has no ears.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ExploreMusicPlayer : MonoBehaviour
    {
        // How quickly the walking-about music ducks when a fight starts: well before the fight music is fully in.
        private const float DuckSeconds = 0.4f;

        [SerializeField] private MusicConductor conductor;
        [SerializeField] private AudioSource source;

        private int index = -1;
        private float level;

        private void Awake()
        {
            if (Application.isBatchMode) enabled = false;
            if (conductor == null) conductor = GetComponentInParent<MusicConductor>();
        }

        private void OnDisable()
        {
            if (source != null) source.Stop();
            index = -1;
            level = 0f;
        }

        private void Update()
        {
            ZoneMusic music = conductor != null ? conductor.Music : null;
            if (music == null || source == null || music.explore == null || music.explore.Length == 0)
            {
                if (source != null && source.isPlaying) source.Stop();
                return;
            }

            // Out of a fight the music comes up slowly; a fight pushes it down quickly.
            float want = 1f - CombatMusicPlayer.Live;
            float seconds = want < level ? DuckSeconds : music.exploreFadeSeconds;
            level = Mathf.MoveTowards(level, want, Time.unscaledDeltaTime / Mathf.Max(0.05f, seconds));

            if (!source.isPlaying)
            {
                if (level <= 0f) return;   // nothing to hear: start the next track only when it can be heard
                PlayNext(music);
            }
            MusicTrack track = index >= 0 && index < music.explore.Length ? music.explore[index] : null;
            source.volume = (track != null ? track.volume : 1f) * music.exploreVolume * level;
        }

        private void PlayNext(ZoneMusic music)
        {
            for (int tries = 0; tries < music.explore.Length; tries++)
            {
                index = (index + 1) % music.explore.Length;
                MusicTrack track = music.explore[index];
                if (track == null || track.clip == null) continue;
                source.clip = track.clip;
                source.loop = music.explore.Length == 1;
                source.Play();
                return;
            }
        }
    }
}
