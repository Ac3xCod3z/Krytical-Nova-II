using MoreMountains.Feedbacks;
using UnityEngine;
using UnityEngine.UIElements;

namespace KryticalNova.GameFeel
{
    /// <summary>
    /// FEEL feedback: moves what a menu moment happened on: a swell, a squash, a nudge, a shake, a turn. It works
    /// on a menu element (the title flow) and on an element of a UI Toolkit window (Settings) alike, so one player
    /// answers a moment wherever it happens. <see cref="MenuFeelPresenter"/> hands it the element before each play;
    /// <see cref="MenuPunchDriver"/> does the moving. It never waits and never holds input: the thing was already
    /// done when it plays.
    /// </summary>
    [AddComponentMenu("")]
    [System.Serializable]
    [FeedbackPath("UI/Menu Punch (Krytical Nova)")]
    [FeedbackHelp("Moves what a menu moment happened on (a menu element, or an element of a UI Toolkit window): a swell, a squash, a nudge, a shake. The Menu Feel Presenter hands it the element before each play.")]
    public class MMF_MenuPunch : MMF_Feedback
    {
        public static bool FeedbackTypeAuthorized = true;
#if UNITY_EDITOR
        public override Color FeedbackColor => MMFeedbacksInspectorColors.UIColor;
        public override string RequiredTargetText => Seconds.ToString("0.00") + " s";
#endif
        public override float FeedbackDuration { get => ApplyTimeMultiplier(Seconds); set => Seconds = value; }

        [MMFInspectorGroup("Menu Punch", true, 32)]
        [Tooltip("How long the movement takes, in seconds of real time.")]
        [Min(0.01f)] public float Seconds = 0.14f;
        [Tooltip("How much it grows across and up at the shape's 1 (0.05 is 5%). Opposite signs squash it.")]
        public Vector2 Swell;
        [Tooltip("How far it moves at the shape's 1, in the interface's own pixels: x to the right, y up.")]
        public Vector2 Nudge;
        [Tooltip("How far it turns at the shape's 1, in degrees, anticlockwise.")]
        public float Turn;
        [Tooltip("The movement over its time (0 to 1). 0 is at rest, 1 is all of the amounts above, below 0 is the other way.")]
        public AnimationCurve Shape = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.3f, 1f), new Keyframe(1f, 0f));

        /// <summary>What the next play moves; set by the presenter. A menu element wins over a window element.</summary>
        [System.NonSerialized] public Transform Target;
        [System.NonSerialized] public VisualElement Element;
        [System.NonSerialized] public MenuPunchDriver Driver;

        protected override void CustomPlayFeedback(Vector3 position, float feedbacksIntensity = 1f)
        {
            if (!Active || !FeedbackTypeAuthorized || Driver == null) return;
            if (Target == null && Element == null) return;
            Driver.Play(Target, Element, Seconds, Swell * feedbacksIntensity, Nudge * feedbacksIntensity, Turn * feedbacksIntensity, Shape);
        }
    }
}
