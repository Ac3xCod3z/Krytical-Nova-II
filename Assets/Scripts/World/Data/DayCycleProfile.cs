using UnityEngine;

namespace KryticalNova.World
{
    /// <summary>
    /// Art direction for the day/night cycle. Every gradient and curve is sampled over the day,
    /// where 0 = midnight, 0.25 = 06:00 sunrise, 0.5 = noon, 0.75 = 18:00 sunset.
    /// Default day colours are picked from Synty's PolygonSciFiCity SimpleSky_01 texture.
    /// </summary>
    [CreateAssetMenu(menuName = "Krytical Nova/World/Day Cycle Profile")]
    public sealed class DayCycleProfile : ScriptableObject
    {
        private const float MinimumDayLength = 10f;

        // TODO(GM): 24 real minutes is a testing value. Lengthen before release and expose
        // SetDayLength / SetTimeOfDay / pause through the GM mod (WorldEnvironmentNetwork API).
        [Header("Clock")]
        [Tooltip("Real seconds per in-game day. 1440 = 24 min (TESTING VALUE — lengthen later; GM mod will control this).")]
        [Min(MinimumDayLength)] public float dayLengthSeconds = 1440f;
        [Tooltip("Time of day (0-1) when the world first starts. 0.3 ≈ 07:12.")]
        [Range(0f, 1f)] public float startTimeOfDay01 = 0.3f;

        [Header("Sun & moon path")]
        [Tooltip("Compass heading of sunrise in degrees (rotates the whole sun path).")]
        [Range(-180f, 180f)] public float sunAzimuth = -30f;
        [Tooltip("Tilts the sun path away from the zenith. Noon elevation = 90 - tilt.")]
        [Range(0f, 80f)] public float sunTilt = 35f;
        [Tooltip("Moon sits roughly opposite the sun; this offsets it so it isn't exactly opposite.")]
        [Range(-60f, 60f)] public float moonOffset = 20f;
        [Tooltip("Sun elevation (sin) below which the single directional light switches to the moon.")]
        [Range(-0.2f, 0.1f)] public float lightSwapElevation = -0.035f;

        [Header("Sky gradient")]
        public Gradient skyZenith = Grad(
            (0f, C(0.02f, 0.04f, 0.11f)), (0.21f, C(0.05f, 0.07f, 0.20f)), (0.25f, C(0.22f, 0.30f, 0.55f)),
            (0.31f, C(0.12f, 0.43f, 0.80f)), (0.69f, C(0.12f, 0.43f, 0.80f)), (0.75f, C(0.25f, 0.27f, 0.52f)),
            (0.80f, C(0.02f, 0.04f, 0.11f)));
        public Gradient skyHorizon = Grad(
            (0f, C(0.06f, 0.10f, 0.20f)), (0.21f, C(0.20f, 0.17f, 0.32f)), (0.25f, C(0.98f, 0.66f, 0.42f)),
            (0.31f, C(0.68f, 0.88f, 0.95f)), (0.69f, C(0.68f, 0.88f, 0.95f)), (0.75f, C(0.95f, 0.52f, 0.50f)),
            (0.80f, C(0.06f, 0.10f, 0.20f)));
        public Gradient skyGround = Grad(
            (0f, C(0.03f, 0.04f, 0.08f)), (0.25f, C(0.30f, 0.25f, 0.28f)), (0.31f, C(0.42f, 0.50f, 0.56f)),
            (0.69f, C(0.42f, 0.50f, 0.56f)), (0.75f, C(0.30f, 0.22f, 0.26f)), (0.80f, C(0.03f, 0.04f, 0.08f)));
        [Tooltip("Shapes the zenith→horizon blend. Higher = thinner horizon band.")]
        [Range(0.5f, 8f)] public float horizonFalloff = 3f;

        [Header("Sun & moon")]
        public Gradient sunColor = Grad(
            (0.23f, C(1f, 0.45f, 0.25f)), (0.27f, C(1f, 0.70f, 0.45f)), (0.35f, C(1f, 0.95f, 0.85f)),
            (0.65f, C(1f, 0.95f, 0.85f)), (0.73f, C(1f, 0.62f, 0.40f)), (0.77f, C(1f, 0.40f, 0.30f)));
        public Gradient moonColor = Grad((0f, C(0.55f, 0.65f, 1f)), (1f, C(0.55f, 0.65f, 1f)));
        [Tooltip("Directional light intensity while it is the sun.")]
        public AnimationCurve sunIntensity = Curve((0.23f, 0f), (0.27f, 0.8f), (0.35f, 1.6f), (0.5f, 2f), (0.65f, 1.6f), (0.73f, 0.8f), (0.77f, 0f));
        [Tooltip("Directional light intensity while it is the moon.")]
        public AnimationCurve moonIntensity = Curve((0f, 0.4f), (0.2f, 0.32f), (0.235f, 0f), (0.765f, 0f), (0.8f, 0.32f), (1f, 0.4f));
        [Tooltip("Cast shadow amount (0-1). Fades the comic shadow layer linearly.")]
        public AnimationCurve shadowStrength = Curve((0f, 0.55f), (0.24f, 0.55f), (0.3f, 1f), (0.7f, 1f), (0.76f, 0.55f), (1f, 0.55f));
        [Range(0.001f, 0.2f)] public float sunDiscSize = 0.035f;
        [Range(0.001f, 0.2f)] public float moonDiscSize = 0.03f;
        [Tooltip("HDR multiplier on the sun disc so Bloom gives it a soft halo.")]
        [Range(0f, 20f)] public float sunDiscIntensity = 6f;
        [Range(1f, 256f)] public float sunGlowPower = 24f;
        [Range(0f, 2f)] public float sunGlowIntensity = 0.45f;

        [Header("Night")]
        public AnimationCurve starVisibility = Curve((0f, 1f), (0.2f, 1f), (0.255f, 0f), (0.745f, 0f), (0.8f, 1f), (1f, 1f));

        [Header("Ambient (trilight → SH probe)")]
        public Gradient ambientSky = Grad(
            (0f, C(0.16f, 0.20f, 0.38f)), (0.25f, C(0.55f, 0.45f, 0.50f)), (0.31f, C(0.62f, 0.72f, 0.88f)),
            (0.69f, C(0.62f, 0.72f, 0.88f)), (0.75f, C(0.55f, 0.40f, 0.48f)), (0.80f, C(0.16f, 0.20f, 0.38f)));
        public Gradient ambientEquator = Grad(
            (0f, C(0.10f, 0.12f, 0.24f)), (0.25f, C(0.55f, 0.40f, 0.35f)), (0.31f, C(0.55f, 0.60f, 0.65f)),
            (0.69f, C(0.55f, 0.60f, 0.65f)), (0.75f, C(0.55f, 0.36f, 0.36f)), (0.80f, C(0.10f, 0.12f, 0.24f)));
        public Gradient ambientGround = Grad(
            (0f, C(0.05f, 0.06f, 0.10f)), (0.31f, C(0.32f, 0.30f, 0.28f)), (0.69f, C(0.32f, 0.30f, 0.28f)),
            (1f, C(0.05f, 0.06f, 0.10f)));

        [Header("Clouds")]
        [Tooltip("Sun-facing cloud colour.")]
        public Gradient cloudLit = Grad(
            (0f, C(0.20f, 0.24f, 0.36f)), (0.21f, C(0.30f, 0.28f, 0.42f)), (0.25f, C(1f, 0.72f, 0.55f)),
            (0.31f, C(1f, 1f, 1f)), (0.69f, C(1f, 1f, 1f)), (0.75f, C(1f, 0.62f, 0.55f)), (0.80f, C(0.20f, 0.24f, 0.36f)));
        [Tooltip("Cloud underside / shadow colour.")]
        public Gradient cloudShade = Grad(
            (0f, C(0.08f, 0.10f, 0.18f)), (0.25f, C(0.45f, 0.38f, 0.50f)), (0.31f, C(0.62f, 0.70f, 0.82f)),
            (0.69f, C(0.62f, 0.70f, 0.82f)), (0.75f, C(0.48f, 0.36f, 0.48f)), (0.80f, C(0.08f, 0.10f, 0.18f)));

        [Header("Fog & post")]
        public Gradient fogColor = Grad(
            (0f, C(0.06f, 0.09f, 0.18f)), (0.25f, C(0.80f, 0.62f, 0.50f)), (0.31f, C(0.66f, 0.80f, 0.88f)),
            (0.69f, C(0.66f, 0.80f, 0.88f)), (0.75f, C(0.80f, 0.52f, 0.50f)), (0.80f, C(0.06f, 0.09f, 0.18f)));
        [Tooltip("Post-process colour filter (Color Adjustments). White = no change.")]
        public Gradient postColorFilter = Grad(
            (0f, C(0.80f, 0.85f, 1f)), (0.24f, C(0.85f, 0.88f, 1f)), (0.27f, C(1f, 0.94f, 0.88f)), (0.33f, C(1f, 1f, 1f)),
            (0.67f, C(1f, 1f, 1f)), (0.74f, C(1f, 0.92f, 0.88f)), (0.78f, C(0.80f, 0.85f, 1f)));

        [Header("Comic style")]
        [Tooltip("Tint blended into the comic shadow colour (alpha = blend weight). Night = deeper blue ink.")]
        public Gradient comicShadowTint = Grad(
            new[] { (0f, C(0.30f, 0.32f, 0.75f)), (1f, C(0.30f, 0.32f, 0.75f)) },
            new[] { (0.2f, 0.6f), (0.3f, 0f), (0.7f, 0f), (0.8f, 0.6f) });
        [Tooltip("Added to the toon shader's dot start (more dots when positive).")]
        public AnimationCurve comicDotStartOffset = Curve((0f, 0.12f), (0.22f, 0.12f), (0.3f, 0f), (0.7f, 0f), (0.78f, 0.12f), (1f, 0.12f));

        // ---------------------------------------------------------------- queries

        /// <summary>Unit vector from the ground toward the sun.</summary>
        public Vector3 SunDirection(float timeOfDay01)
        {
            float a = (timeOfDay01 - 0.25f) * Mathf.PI * 2f;
            float tilt = sunTilt * Mathf.Deg2Rad;
            var dir = new Vector3(Mathf.Cos(a), Mathf.Sin(a) * Mathf.Cos(tilt), Mathf.Sin(a) * Mathf.Sin(tilt));
            return Quaternion.Euler(0f, sunAzimuth, 0f) * dir;
        }

        /// <summary>Unit vector from the ground toward the moon (roughly opposite the sun).</summary>
        public Vector3 MoonDirection(float timeOfDay01)
        {
            return Quaternion.Euler(0f, moonOffset, 0f) * -SunDirection(timeOfDay01);
        }

        private void OnValidate()
        {
            dayLengthSeconds = Mathf.Max(MinimumDayLength, dayLengthSeconds);
        }

        // ---------------------------------------------------------------- default helpers

        private static Color C(float r, float g, float b) => new Color(r, g, b, 1f);

        private static Gradient Grad(params (float t, Color c)[] keys)
        {
            return Grad(keys, new[] { (0f, 1f), (1f, 1f) });
        }

        private static Gradient Grad((float t, Color c)[] colors, (float t, float a)[] alphas)
        {
            var colorKeys = new GradientColorKey[colors.Length];
            for (int i = 0; i < colors.Length; i++) colorKeys[i] = new GradientColorKey(colors[i].c, colors[i].t);
            var alphaKeys = new GradientAlphaKey[alphas.Length];
            for (int i = 0; i < alphas.Length; i++) alphaKeys[i] = new GradientAlphaKey(alphas[i].a, alphas[i].t);
            var g = new Gradient();
            g.SetKeys(colorKeys, alphaKeys);
            return g;
        }

        private static AnimationCurve Curve(params (float t, float v)[] keys)
        {
            var frames = new Keyframe[keys.Length];
            for (int i = 0; i < keys.Length; i++) frames[i] = new Keyframe(keys[i].t, keys[i].v);
            var curve = new AnimationCurve(frames);
            for (int i = 0; i < frames.Length; i++) curve.SmoothTangents(i, 0f);
            return curve;
        }
    }
}
