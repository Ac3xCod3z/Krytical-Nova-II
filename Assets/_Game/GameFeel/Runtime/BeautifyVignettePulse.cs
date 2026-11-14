using MoreMountains.Feedbacks;
using UnityEngine;
using UnityEngine.Rendering;

namespace KryticalNova.GameFeel
{
    /// <summary>
    /// Pulses Beautify's vignette (rings + colour) on the global volume's runtime profile instance — the hurt flash now
    /// that Beautify owns the vignette (FEEL only ships URP vignette shakers). Lives on the Global Volume; triggered by
    /// <see cref="MMF_BeautifyVignette"/>. Never touches the profile asset.
    /// The tint is a wide, soft gradient coming in from the corners: Beautify draws the vignette colour from the
    /// outer ring outward and fades it to nothing at the inner ring, both measured as squared distance from the
    /// screen centre (0.5 at the corners) and both stored inverted (ring = 1 - distance). The pulse parks the outer
    /// ring just past the corners, so the colour is never solid, and sweeps the inner ring toward the centre.
    /// Over that wash the comic pass draws Ben-Day dots in the same colour, growing toward the corners across the
    /// same band (ComicPost.shader reads the two globals set here).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Volume))]
    public sealed class BeautifyVignettePulse : MonoBehaviour
    {
        public static BeautifyVignettePulse Instance { get; private set; }

        private const float CornerDistance = 0.5f;        // squared distance of the screen corners from the centre
        private const float OuterDistance = 0.62f;        // past the corners: full colour is never reached on screen
        private static readonly int HurtDotsId = Shader.PropertyToID("_KN_HurtDots");
        private static readonly int HurtDotColorId = Shader.PropertyToID("_KN_HurtDotColor");

        private Beautify.Universal.Beautify beautify;
        private float baseRing, baseInnerRing;
        private Color baseColor;
        private float elapsed = -1f, duration, reach, dotOpacity;
        private Color pulseColor;
        private AnimationCurve curve;

        private void OnEnable()
        {
            Instance = this;
            Shader.SetGlobalVector(HurtDotsId, Vector4.zero); // globals outlive a Play session when the domain is kept
            var volume = GetComponent<Volume>();
            // .profile returns the runtime instance (EnvironmentPresenter / FEEL shakers share it).
            if (volume.profile.TryGet(out beautify))
            {
                baseRing = beautify.vignettingOuterRing.value;
                baseInnerRing = beautify.vignettingInnerRing.value;
                baseColor = beautify.vignettingColor.value;
            }
        }

        private void OnDisable()
        {
            if (Instance == this) Instance = null;
            Restore();
        }

        /// <param name="tintReach">How far the tint comes in from the corners at the peak, as squared screen distance (0 - 0.5).</param>
        /// <param name="dots">Opacity of the Ben-Day dots drawn in the tint colour at the peak (0 = wash only).</param>
        public void Pulse(float seconds, float tintReach, Color color, AnimationCurve shape, float dots = 0f)
        {
            dotOpacity = Mathf.Clamp01(dots);
            if (beautify == null) return;
            duration = Mathf.Max(0.01f, seconds);
            reach = Mathf.Clamp(tintReach, 0f, CornerDistance);
            pulseColor = color;
            curve = shape;
            elapsed = 0f;
        }

        private void Update()
        {
            if (elapsed < 0f || beautify == null) return;
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float k = curve != null ? curve.Evaluate(t) : Mathf.Sin(t * Mathf.PI);
            // With the fade starting on the corners (k = 0) nothing shows, so the pulse begins and ends invisibly.
            beautify.vignettingOuterRing.Override(1f - OuterDistance);
            float inner = CornerDistance - reach * Mathf.Clamp01(k);
            beautify.vignettingInnerRing.Override(1f - inner);
            beautify.vignettingColor.Override(Color.Lerp(baseColor, pulseColor, k));
            Shader.SetGlobalColor(HurtDotColorId, pulseColor);
            Shader.SetGlobalVector(HurtDotsId, new Vector4(inner, OuterDistance, dotOpacity * Mathf.Clamp01(k * 3f), 0f));
            if (t >= 1f) Restore();
        }

        private void Restore()
        {
            if (beautify != null && elapsed >= 0f)
            {
                beautify.vignettingOuterRing.Override(baseRing);
                beautify.vignettingInnerRing.Override(baseInnerRing);
                Shader.SetGlobalVector(HurtDotsId, Vector4.zero);
                beautify.vignettingColor.Override(baseColor);
            }
            elapsed = -1f;
        }
    }

    /// <summary>FEEL feedback: pulse Beautify's vignette (requires a <see cref="BeautifyVignettePulse"/> on the global volume).</summary>
    [AddComponentMenu("")]
    [System.Serializable]
    [FeedbackPath("PostProcess/Beautify Vignette Pulse")]
    [FeedbackHelp("Pulses a soft tint in from the screen corners through Beautify's vignette. Needs a BeautifyVignettePulse on the global volume.")]
    public class MMF_BeautifyVignette : MMF_Feedback
    {
        public static bool FeedbackTypeAuthorized = true;
#if UNITY_EDITOR
        public override Color FeedbackColor => MMFeedbacksInspectorColors.PostProcessColor;
#endif
        public override float FeedbackDuration { get => ApplyTimeMultiplier(Duration); set => Duration = value; }

        [MMFInspectorGroup("Beautify Vignette", true, 28)]
        public float Duration = 0.6f;
        [Tooltip("How far the tint comes in from the corners at the peak, as squared screen distance: 0 = not at all, " +
                 "0.5 = to the centre (scaled by feedback intensity).")]
        [Range(0f, 0.5f)] public float Reach = 0.3f;
        [Tooltip("Tint colour. Alpha is how strong the soft wash gets in the corners at the peak; keep it low for a hint.")]
        public Color PulseColor = new Color(0.75f, 0.05f, 0.08f, 0.22f);
        [Tooltip("Opacity of the Ben-Day dots drawn over the wash in the tint colour (comic pass). 0 = wash only.")]
        [Range(0f, 1f)] public float DotOpacity = 0.5f;
        public AnimationCurve Shape = new AnimationCurve(new Keyframe(0f, 0f, 0f, 0f), new Keyframe(0.25f, 1f, 0f, 0f), new Keyframe(1f, 0f, 0f, 0f));

        protected override void CustomPlayFeedback(Vector3 position, float feedbacksIntensity = 1f)
        {
            if (!Active || !FeedbackTypeAuthorized || BeautifyVignettePulse.Instance == null) return;
            float k = ComputeIntensity(feedbacksIntensity, position);
            BeautifyVignettePulse.Instance.Pulse(FeedbackDuration, Reach * k, PulseColor, Shape, DotOpacity);
        }
    }
}
