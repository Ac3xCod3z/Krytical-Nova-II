using UnityEngine;

namespace KryticalNova.World
{
    /// <summary>
    /// Flattened, numerically blended weather parameters for the current frame.
    /// Computed once per frame by <see cref="WorldEnvironmentNetwork"/>; presentation only reads it.
    /// </summary>
    public struct WeatherBlend
    {
        public float coverage;
        public float cloudDensity;
        public float cloudSoftness;
        public float cloudDarkness;
        public Color cloudTint;
        public float cirrus;
        public float celestialDiffusion;

        public float skyDesaturation;
        public float skyDarkening;
        public float sunMultiplier;
        public float ambientMultiplier;
        public float shadowMultiplier;
        public float cloudShadow;

        public float fogDensity;
        public Color fogTint;

        public Vector2 wind;          // metres per second, world XZ (includes the current gust)
        public float gustiness;       // 0-1, blended from the profiles
        public float gust;            // 0-1, how hard it is gusting right now (gustiness × shared gust noise)

        public float rain;
        public float snow;
        public float mist;
        public float lightningPerMinute;

        public float saturation;
        public float contrast;

        /// <summary>
        /// Blends two profiles. <paramref name="t"/> is already eased. <paramref name="coverageNoise"/> (0-1)
        /// picks where inside each profile's coverage range the sky currently sits.
        /// </summary>
        /// <paramref name="fromHeading"/> / <paramref name="toHeading"/> are the per-cycle rolled wind headings (NaN = profile heading).
        public static WeatherBlend Evaluate(WeatherProfile from, WeatherProfile to, float t, float coverageNoise,
            float fromHeading = float.NaN, float toHeading = float.NaN)
        {
            if (from == null) from = to;
            if (to == null) to = from;
            var b = new WeatherBlend();
            if (from == null) return Clear();

            float covFrom = Mathf.Lerp(from.coverageMin, from.coverageMax, coverageNoise);
            float covTo = Mathf.Lerp(to.coverageMin, to.coverageMax, coverageNoise);
            b.coverage = Mathf.Lerp(covFrom, covTo, t);
            b.cloudDensity = Mathf.Lerp(from.cloudDensity, to.cloudDensity, t);
            b.cloudSoftness = Mathf.Lerp(from.cloudSoftness, to.cloudSoftness, t);
            b.cloudDarkness = Mathf.Lerp(from.cloudDarkness, to.cloudDarkness, t);
            b.cloudTint = Color.Lerp(from.cloudTint, to.cloudTint, t);
            b.cirrus = Mathf.Lerp(from.cirrusAmount, to.cirrusAmount, t);
            b.celestialDiffusion = Mathf.Lerp(from.celestialDiffusion, to.celestialDiffusion, t);

            b.skyDesaturation = Mathf.Lerp(from.skyDesaturation, to.skyDesaturation, t);
            b.skyDarkening = Mathf.Lerp(from.skyDarkening, to.skyDarkening, t);
            b.sunMultiplier = Mathf.Lerp(from.sunIntensityMultiplier, to.sunIntensityMultiplier, t);
            b.ambientMultiplier = Mathf.Lerp(from.ambientMultiplier, to.ambientMultiplier, t);
            b.shadowMultiplier = Mathf.Lerp(from.shadowMultiplier, to.shadowMultiplier, t);
            b.cloudShadow = Mathf.Lerp(from.cloudShadowStrength, to.cloudShadowStrength, t);

            b.fogDensity = Mathf.Lerp(from.fogDensity, to.fogDensity, t);
            b.fogTint = Color.Lerp(from.fogTint, to.fogTint, t);

            b.wind = Vector2.Lerp(from.WindVectorAt(fromHeading), to.WindVectorAt(toHeading), t);
            b.gustiness = Mathf.Lerp(from.gustiness, to.gustiness, t);

            b.rain = Mathf.Lerp(from.RainIntensity, to.RainIntensity, t);
            b.snow = Mathf.Lerp(from.SnowIntensity, to.SnowIntensity, t);
            b.mist = Mathf.Lerp(from.mist, to.mist, t);
            b.lightningPerMinute = Mathf.Lerp(from.lightningPerMinute, to.lightningPerMinute, t);

            b.saturation = Mathf.Lerp(from.saturation, to.saturation, t);
            b.contrast = Mathf.Lerp(from.contrast, to.contrast, t);
            return b;
        }

        /// <summary>Fallback used when no catalog is assigned.</summary>
        public static WeatherBlend Clear()
        {
            return new WeatherBlend
            {
                coverage = 0.25f, cloudDensity = 0.9f, cloudSoftness = 0.18f, cloudTint = Color.white, cirrus = 0.3f,
                sunMultiplier = 1f, ambientMultiplier = 1f, shadowMultiplier = 1f, cloudShadow = 0.4f,
                fogDensity = 0.0015f, wind = new Vector2(2f, 3f),
            };
        }
    }
}
