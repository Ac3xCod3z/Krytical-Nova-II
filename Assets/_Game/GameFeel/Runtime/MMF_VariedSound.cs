using MoreMountains.Feedbacks;
using UnityEngine;

namespace KryticalNova.GameFeel
{
    /// <summary>
    /// FEEL's sound feedback for a moment that comes round again and again (a swing, a hit, a click): of its Random
    /// Sfx it never plays the one it played last, so with the pitch and volume ranges no two plays in a row sound the
    /// same. With one clip, or with Sfx set, it is the plain sound feedback.
    /// </summary>
    [AddComponentMenu("")]
    [System.Serializable]
    [FeedbackPath("Audio/Varied Sound (Krytical Nova)")]
    [FeedbackHelp("Plays one of Random Sfx, never the same one twice in a row, at a random pitch and volume within the ranges.")]
    public class MMF_VariedSound : MMF_Sound
    {
        private int lastIndex = -1;

        protected override void CustomPlayFeedback(Vector3 position, float feedbacksIntensity = 1.0f)
        {
            if (Sfx != null || RandomSfx == null || RandomSfx.Length < 2)
            {
                base.CustomPlayFeedback(position, feedbacksIntensity);
                return;
            }
            if (!Active || !FeedbackTypeAuthorized) return;

            // One of the others: a draw among all but the last, stepped over it.
            int index = Random.Range(0, lastIndex < 0 ? RandomSfx.Length : RandomSfx.Length - 1);
            if (lastIndex >= 0 && index >= lastIndex) index++;
            lastIndex = index;

            AudioClip clip = RandomSfx[index];
            if (clip == null) return;
            _duration = clip.length;
            PlaySound(clip, position, ComputeIntensity(feedbacksIntensity, position));
        }
    }
}
