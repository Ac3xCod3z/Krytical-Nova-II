using System;
using System.Collections.Generic;
using UnityEngine;

namespace KryticalNova.World
{
    public enum PrecipitationType : byte { None = 0, Rain = 1, Snow = 2 }

    /// <summary>
    /// One weather state. The world blends numerically between two profiles during a transition,
    /// so any in-between look (e.g. half-way from Partly Cloudy to Overcast) comes for free.
    /// </summary>
    [CreateAssetMenu(menuName = "Krytical Nova/World/Weather Profile")]
    public sealed class WeatherProfile : ScriptableObject
    {
        [Serializable]
        public struct NextWeather
        {
            public WeatherProfile profile;
            [Min(0f)] public float weight;
        }

        [Header("Clouds")]
        [Tooltip("Sky fraction covered by cloud. Actual coverage drifts slowly inside this range.")]
        [Range(0f, 1f)] public float coverageMin = 0.2f;
        [Range(0f, 1f)] public float coverageMax = 0.4f;
        [Tooltip("Cloud opacity.")]
        [Range(0f, 1f)] public float cloudDensity = 0.9f;
        [Tooltip("Edge softness of cloud shapes.")]
        [Range(0.02f, 0.5f)] public float cloudSoftness = 0.18f;
        [Tooltip("Darkens thick cloud cores (storm clouds).")]
        [Range(0f, 1f)] public float cloudDarkness = 0f;
        [Tooltip("Multiplied into cloud colours.")]
        public Color cloudTint = Color.white;
        [Range(0f, 1f)] public float cirrusAmount = 0.3f;
        [Tooltip("How much the cloud deck diffuses the sun/moon: 0 = crisp stylized art, 1 = only a pale glow through cloud.")]
        [Range(0f, 1f)] public float celestialDiffusion = 0f;

        [Header("Sky & light")]
        [Range(0f, 1f)] public float skyDesaturation = 0f;
        [Range(0f, 1f)] public float skyDarkening = 0f;
        [Range(0f, 2f)] public float sunIntensityMultiplier = 1f;
        [Range(0f, 2f)] public float ambientMultiplier = 1f;
        [Tooltip("Scales cast shadows. Near 0 also turns shadow rendering off (saves the shadow pass).")]
        [Range(0f, 1f)] public float shadowMultiplier = 1f;
        [Tooltip("Soft cloud shadows sweeping the ground. Keep ~0 when overcast so the whole screen isn't shadowed.")]
        [Range(0f, 1f)] public float cloudShadowStrength = 0.5f;

        [Header("Fog")]
        [Tooltip("Exp2 fog density.")]
        [Range(0f, 0.05f)] public float fogDensity = 0.0015f;
        [Tooltip("Blended into the time-of-day fog colour (alpha = weight).")]
        public Color fogTint = new Color(0.7f, 0.72f, 0.75f, 0f);

        [Header("Wind")]
        [Tooltip("Compass direction the wind blows toward, in degrees.")]
        [Range(0f, 360f)] public float windHeading = 60f;
        [Tooltip("Metres per second. Drives cloud drift and precipitation slant.")]
        [Range(0f, 40f)] public float windSpeed = 4f;
        [Tooltip("Roll a random heading each time this weather starts. It is held for the whole cycle and only changes " +
                 "when the next weather begins. Off = always use Wind Heading.")]
        public bool randomWindDirection = true;
        [Tooltip("0 = steady wind, 1 = violent gusts (speed surges up to ~+80%). Also drives gust-streak VFX.")]
        [Range(0f, 1f)] public float gustiness = 0.2f;

        [Header("Precipitation & effects")]
        public PrecipitationType precipitation = PrecipitationType.None;
        [Range(0f, 1f)] public float precipitationIntensity = 0f;
        [Tooltip("Ground mist particles around the camera.")]
        [Range(0f, 1f)] public float mist = 0f;
        [Tooltip("Average lightning strikes per minute. 0 = none.")]
        [Range(0f, 30f)] public float lightningPerMinute = 0f;

        [Header("Post")]
        [Range(-100f, 100f)] public float saturation = 0f;
        [Range(-100f, 100f)] public float contrast = 0f;

        [Header("Audio")]
        public AudioClip ambienceLoop;
        [Range(0f, 1f)] public float ambienceVolume = 0.6f;

        [Header("Scheduling (server)")]
        [Tooltip("Seconds to blend into this weather.")]
        [Min(1f)] public float transitionSeconds = 90f;
        [Tooltip("Real seconds this weather lasts before the next is chosen.")]
        [Min(10f)] public float durationMin = 240f;
        [Min(10f)] public float durationMax = 600f;
        [Tooltip("Weighted choices for what comes next. Empty = stay.")]
        public List<NextWeather> next = new List<NextWeather>();

        public float RainIntensity => precipitation == PrecipitationType.Rain ? precipitationIntensity : 0f;
        public float SnowIntensity => precipitation == PrecipitationType.Snow ? precipitationIntensity : 0f;

        public Vector2 WindVector => WindVectorAt(windHeading);

        /// <summary>Wind vector at this profile's speed but an explicit heading (the per-cycle rolled one).</summary>
        public Vector2 WindVectorAt(float heading)
        {
            if (float.IsNaN(heading)) heading = windHeading;
            float r = heading * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(r), Mathf.Cos(r)) * windSpeed;
        }

        private void OnValidate()
        {
            coverageMax = Mathf.Max(coverageMin, coverageMax);
            durationMax = Mathf.Max(durationMin, durationMax);
        }
    }
}
