using MoreMountains.Feedbacks;
using UnityEngine;
using UnityEngine.Rendering;

namespace KryticalNova.GameFeel
{
    /// <summary>
    /// Pulses Beautify's vignette (ring + colour) on the global volume's runtime profile instance — the hurt flash now
    /// that Beautify owns the vignette (FEEL only ships URP vignette shakers). Lives on the Global Volume; triggered by
    /// <see cref="MMF_BeautifyVignette"/>. Never touches the profile asset.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Volume))]
    public sealed class BeautifyVignettePulse : MonoBehaviour
    {
        public static BeautifyVignettePulse Instance { get; private set; }

        private Beautify.Universal.Beautify beautify;
        private float baseRing;
        private Color baseColor;
        private float elapsed = -1f, duration, ringBoost;
        private Color pulseColor;
        private AnimationCurve curve;

        private void OnEnable()
        {
            Instance = this;
            var volume = GetComponent<Volume>();
            // .profile returns the runtime instance (EnvironmentPresenter / FEEL shakers share it).
            if (volume.profile.TryGet(out beautify))
            {
                baseRing = beautify.vignettingOuterRing.value;
                baseColor = beautify.vignettingColor.value;
            }
        }

        private void OnDisable()
        {
            if (Instance == this) Instance = null;
            Restore();
        }

        public void Pulse(float seconds, float extraRing, Color color, AnimationCurve shape)
        {
            if (beautify == null) return;
            duration = Mathf.Max(0.01f, seconds);
            ringBoost = extraRing;
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
            beautify.vignettingOuterRing.Override(Mathf.Clamp(baseRing + ringBoost * k, -2f, 1f));
            beautify.vignettingColor.Override(Color.Lerp(baseColor, pulseColor, k));
            if (t >= 1f) Restore();
        }

        private void Restore()
        {
            if (beautify != null && elapsed >= 0f)
            {
                beautify.vignettingOuterRing.Override(baseRing);
                beautify.vignettingColor.Override(baseColor);
            }
            elapsed = -1f;
        }
    }

    /// <summary>FEEL feedback: pulse Beautify's vignette (requires a <see cref="BeautifyVignettePulse"/> on the global volume).</summary>
    [AddComponentMenu("")]
    [System.Serializable]
    [FeedbackPath("PostProcess/Beautify Vignette Pulse")]
    [FeedbackHelp("Pulses Beautify's vignette ring and colour. Needs a BeautifyVignettePulse on the global volume.")]
    public class MMF_BeautifyVignette : MMF_Feedback
    {
        public static bool FeedbackTypeAuthorized = true;
#if UNITY_EDITOR
        public override Color FeedbackColor => MMFeedbacksInspectorColors.PostProcessColor;
#endif
        public override float FeedbackDuration { get => ApplyTimeMultiplier(Duration); set => Duration = value; }

        [MMFInspectorGroup("Beautify Vignette", true, 28)]
        public float Duration = 0.45f;
        [Tooltip("Added to the vignette outer ring at the peak (scaled by feedback intensity).")]
        public float RingBoost = 0.35f;
        public Color PulseColor = new Color(0.75f, 0.05f, 0.08f, 1f);
        public AnimationCurve Shape = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.15f, 1f), new Keyframe(1f, 0f));

        protected override void CustomPlayFeedback(Vector3 position, float feedbacksIntensity = 1f)
        {
            if (!Active || !FeedbackTypeAuthorized || BeautifyVignettePulse.Instance == null) return;
            float k = ComputeIntensity(feedbacksIntensity, position);
            BeautifyVignettePulse.Instance.Pulse(FeedbackDuration, RingBoost * k, PulseColor, Shape);
        }
    }
}
