using MoreMountains.Feedbacks;
using UnityEngine;

namespace KryticalNova.GameFeel
{
    /// <summary>
    /// FEEL's sound feedback for a moment that comes round again and again (a swing, a hit, a click): of its Random
    /// Sfx it never plays the one it played last, so with the pitch and volume ranges no two plays in a row sound the
    /// same. A moment that sounds different for each faction (a death) carries a set of clips per faction and is told
    /// which to use before it plays; a sound that must last as long as something else (a charge) is told how long.
    /// </summary>
    [AddComponentMenu("")]
    [System.Serializable]
    [FeedbackPath("Audio/Varied Sound (Krytical Nova)")]
    [FeedbackHelp("Plays one of Random Sfx (or of the faction's own clips), never the same one twice in a row, at a random pitch and volume within the ranges.")]
    public class MMF_VariedSound : MMF_Sound
    {
        [MMFInspectorGroup("Faction Sets", true, 30)]
        [Tooltip("Played instead of Random Sfx when the moment belongs to this faction. Empty: Random Sfx.")]
        public AudioClip[] HegemonySfx;
        public AudioClip[] NullSfx;
        public AudioClip[] CovenantSfx;

        [Tooltip("Slowest and fastest a clip may be played to make it last the time asked for.")]
        public Vector2 FitPitch = new Vector2(0.5f, 2f);

        private int lastIndex = -1;
        private AudioClip[] lastSet;
        private int faction;
        private float fitSeconds;

        /// <summary>Which faction's clips the next plays use (1 Hegemony, 2 Null, 3 Covenant; anything else: Random Sfx).</summary>
        public void UseFaction(int factionId) => faction = factionId;

        /// <summary>The next plays are slowed or sped so the clip lasts this long (0: as recorded, within the pitch range).</summary>
        public void LastFor(float seconds) => fitSeconds = seconds;

        private AudioClip[] Clips()
        {
            AudioClip[] own = faction == 1 ? HegemonySfx : faction == 2 ? NullSfx : faction == 3 ? CovenantSfx : null;
            return own != null && own.Length > 0 ? own : RandomSfx;
        }

        protected override void CustomPlayFeedback(Vector3 position, float feedbacksIntensity = 1.0f)
        {
            if (!Active || !FeedbackTypeAuthorized) return;
            AudioClip[] clips = Sfx != null ? null : Clips();
            AudioClip clip = Sfx;
            if (clips != null && clips.Length > 0)
            {
                if (clips != lastSet) { lastSet = clips; lastIndex = -1; }
                // One of the others: a draw among all but the last, stepped over it.
                int index = Random.Range(0, lastIndex < 0 || clips.Length == 1 ? clips.Length : clips.Length - 1);
                if (lastIndex >= 0 && clips.Length > 1 && index >= lastIndex) index++;
                lastIndex = index;
                clip = clips[index];
            }
            if (clip == null) return;

            _duration = clip.length;
            float intensity = ComputeIntensity(feedbacksIntensity, position);
            if (fitSeconds > 0f)
            {
                float low = MinPitch, high = MaxPitch;
                MinPitch = MaxPitch = Mathf.Clamp(clip.length / fitSeconds, FitPitch.x, FitPitch.y);
                PlaySound(clip, position, intensity);
                MinPitch = low;
                MaxPitch = high;
            }
            else PlaySound(clip, position, intensity);
        }
    }
}
