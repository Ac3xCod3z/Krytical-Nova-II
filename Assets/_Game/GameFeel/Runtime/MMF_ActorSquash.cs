using MoreMountains.Feedbacks;
using UnityEngine;

namespace KryticalNova.GameFeel
{
    /// <summary>
    /// FEEL feedback: squashes or stretches the body of the character the moment belongs to, and lets the spring that
    /// carries a hit's squash (<see cref="HitReactor"/>) bring it back, so the two never fight over the scale. A jump's
    /// crouch squashes a little, its take-off stretches a little. <see cref="GameFeelPresenter"/> hands it the character
    /// before each play.
    /// </summary>
    [AddComponentMenu("")]
    [System.Serializable]
    [FeedbackPath("Transform/Actor Squash (Krytical Nova)")]
    [FeedbackHelp("Squashes or stretches the character this moment belongs to, then springs back (the hit-reaction spring). Width and Height are fractions: 0.05 is 5%.")]
    public class MMF_ActorSquash : MMF_Feedback
    {
        public static bool FeedbackTypeAuthorized = true;
#if UNITY_EDITOR
        public override Color FeedbackColor => MMFeedbacksInspectorColors.TransformColor;
#endif

        [MMFInspectorGroup("Squash", true, 31)]
        [Tooltip("How much wider the body gets (negative: narrower). 0.03 is 3%.")]
        public float Width = 0.03f;
        [Tooltip("How much taller the body gets (negative: shorter). -0.05 is 5% shorter.")]
        public float Height = -0.05f;
        [Tooltip("How hard the spring is kicked back towards the rest shape (higher: a quicker, livelier return).")]
        public float Kick = 0.4f;

        /// <summary>Character the next play belongs to; set by the presenter.</summary>
        [System.NonSerialized] public Transform Target;

        protected override void CustomPlayFeedback(Vector3 position, float feedbacksIntensity = 1f)
        {
            if (!Active || !FeedbackTypeAuthorized || Target == null) return;
            HitReactor reactor = HitReactor.For(Target);
            if (reactor == null) return;
            float k = feedbacksIntensity;
            reactor.Pose(new Vector3(1f + Width * k, 1f + Height * k, 1f + Width * k),
                new Vector3(-Width, -Height, -Width) * (Kick * 10f * k));
        }
    }
}
