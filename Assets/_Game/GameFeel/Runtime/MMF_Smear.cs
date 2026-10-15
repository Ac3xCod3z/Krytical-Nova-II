using MoreMountains.Feedbacks;
using UnityEngine;

namespace KryticalNova.GameFeel
{
    /// <summary>Which of a <see cref="SmearSettings"/>' smears a feedback plays.</summary>
    public enum SmearKind
    {
        TakeOff = 0,
        DeathThrow = 1,
        /// <summary>The feet leaving the ground, and lighter through the air.</summary>
        Jump = 2,
    }

    /// <summary>
    /// FEEL feedback: plays the comic motion smear on the character the moment belongs to (<see cref="ActorSmear"/>),
    /// so the smear sits in the same MMF_Player as the moment's dust, sparks and comic text and is switched on, off
    /// and timed with them. <see cref="GameFeelPresenter"/> hands it the character before each play. How the smear
    /// looks is not set here but in the <see cref="SmearSettings"/> asset.
    /// </summary>
    [AddComponentMenu("")]
    [System.Serializable]
    [FeedbackPath("Renderer/Smear (Krytical Nova)")]
    [FeedbackHelp("Plays the comic motion smear on the character this moment belongs to. Tuned in the Smear Settings asset.")]
    public class MMF_Smear : MMF_Feedback
    {
        public static bool FeedbackTypeAuthorized = true;
#if UNITY_EDITOR
        public override Color FeedbackColor => MMFeedbacksInspectorColors.RendererColor;
        public override bool EvaluateRequiresSetup() => Settings == null;
        public override string RequiredTargetText => Settings != null ? Kind.ToString() : string.Empty;
#endif

        [MMFInspectorGroup("Smear", true, 31)]
        [Tooltip("Every number of the smear lives in this asset.")]
        public SmearSettings Settings;
        [Tooltip("Which of the asset's smears to play.")]
        public SmearKind Kind;

        /// <summary>Character the next play belongs to; set by the presenter.</summary>
        [System.NonSerialized] public Transform Target;

        protected override void CustomPlayFeedback(Vector3 position, float feedbacksIntensity = 1f)
        {
            if (!Active || !FeedbackTypeAuthorized || Settings == null || Target == null) return;
            ActorSmear smear = ActorSmear.For(Target);
            if (smear == null) return;
            smear.Play(Settings, Kind == SmearKind.DeathThrow ? Settings.deathThrow : Kind == SmearKind.Jump ? Settings.jump : Settings.takeOff);
        }
    }
}
