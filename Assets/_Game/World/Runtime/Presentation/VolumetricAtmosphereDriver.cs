using UnityEngine;
using VolumetricFogAndMist2;

namespace KryticalNova.World
{
    /// <summary>
    /// Ties Volumetric Fog &amp; Mist 2 to the weather, like real air: it either rains or it fogs.
    ///  - Ground mist (terrain-fitted, whole zone) shows only as much as the weather's <c>volumetricFog</c> asks for —
    ///    full in WP_Fog, a thin haze in Overcast, a whiteout share in Blizzard, and never while it rains.
    ///  - On dry nights the same ground volume runs at a small fraction (night haze), so moonlight has air to scatter
    ///    in and buildings cut soft shafts through it.
    ///  - Water mist (low local volumes over the lakes and rivers) rises at dusk on dry evenings and lifts after.
    /// Volumes at ~0 are switched off entirely, so clear or rainy weather costs nothing. Sun/moon colour comes from
    /// the VolumetricFogManager's light sync; this owns density, albedo and on/off. Play mode works on instanced
    /// profiles (<see cref="VolumetricFog.settings"/>), never the assets. Headless servers and low tiers skip it.
    /// <see cref="FogAmount"/> tells other presentation (street-lamp light cones) how foggy the air is in fog weather.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(60)] // after EnvironmentPresenter (50) has published this frame's atmosphere
    public sealed class VolumetricAtmosphereDriver : MonoBehaviour
    {
        [SerializeField] private VolumetricFog fog;
        [SerializeField] private VolumetricFog[] waterMists = new VolumetricFog[0];
        [Tooltip("Dusk window in hours (start, end) when water mist exists. It builds over Dusk Fade In Hours from the start, "
            + "then thins away slowly over Dusk Fade Out Hours before the end.")]
        [SerializeField] private Vector2 duskHours = new Vector2(17.75f, 22f);
        [Min(0.05f)] [SerializeField] private float duskFadeInHours = 0.75f;
        [Min(0.05f)] [SerializeField] private float duskFadeOutHours = 2f;
        [Range(0f, 1f)] [SerializeField] private float albedoFromFogColor = 0.65f;
        [Tooltip("Seconds to ease weight changes (weather blends are already smooth; this hides quality-tier pops).")]
        [Min(0.01f)] [SerializeField] private float smoothingSeconds = 2f;
        [SerializeField] private string[] disabledOnQualityLevels = { "Mobile" };

        private const float MinMistLight = 0.42f;

        private static Color publishedFogColor = Color.gray;
        private static float publishedVolumetric = -1f;
        private static float publishedRain;
        private static float publishedHours = 12f;
        private static float publishedNightHaze;

        /// <summary>How foggy the air is town-wide, 0-1 (fog weather only; local water mist excluded). Presentation only.</summary>
        public static float FogAmount { get; private set; }

        private Layer ground;
        private Layer[] water;
        private float weatherWeight; // the fog-weather share of the ground weight (excludes night haze)
        private int lastQualityLevel = -1;
        private bool tierDisabled;

        private struct Layer
        {
            public VolumetricFog Fog;
            public VolumetricFogProfile Profile;
            public float AuthoredDensity;
            public Color AuthoredAlbedo;
            public float Weight;

            public static Layer Create(VolumetricFog fog)
            {
                var l = new Layer { Fog = fog };
                if (fog == null || fog.profile == null) return l;
                l.AuthoredDensity = fog.profile.density;
                l.AuthoredAlbedo = fog.profile.albedo;
                l.Profile = fog.settings; // instanced copy from here on
                return l;
            }
        }

        /// <summary>Called by <see cref="EnvironmentPresenter"/> every presented frame.</summary>
        /// <param name="nightHaze">Thin ground haze for moonlit nights, 0-1 share of the ground mist (not fog weather).</param>
        public static void Publish(Color fogColor, float volumetricFog, float rain, float timeOfDay01, float nightHaze = 0f)
        {
            publishedNightHaze = nightHaze;
            publishedFogColor = fogColor;
            publishedVolumetric = volumetricFog;
            publishedRain = rain;
            publishedHours = timeOfDay01 * 24f;
        }

        private void OnEnable()
        {
#if UNITY_SERVER
            enabled = false;
            return;
#else
            if (Application.isBatchMode || fog == null || fog.profile == null) { enabled = false; return; }
            ground = Layer.Create(fog);
            water = new Layer[waterMists.Length];
            for (int i = 0; i < waterMists.Length; i++) water[i] = Layer.Create(waterMists[i]);
            lastQualityLevel = -1;
            weatherWeight = 0f;
            FogAmount = 0f;
#endif
        }

        private void OnDisable() => FogAmount = 0f;

        private void LateUpdate()
        {
            int quality = QualitySettings.GetQualityLevel();
            if (quality != lastQualityLevel)
            {
                lastQualityLevel = quality;
                tierDisabled = IsDisabledTier(quality);
            }
            if (publishedVolumetric < 0f) return;

            float k = 1f - Mathf.Exp(-Time.deltaTime / smoothingSeconds);
            float weatherTarget = tierDisabled ? 0f : Mathf.Clamp01(publishedVolumetric);
            float dry = 1f - Mathf.Clamp01(publishedRain * 4f);
            // Night haze is the same ground volume at a fraction of its density, on dry nights only.
            float groundTarget = tierDisabled ? 0f : Mathf.Max(weatherTarget, Mathf.Clamp01(publishedNightHaze) * dry);
            float waterTarget = tierDisabled ? 0f : DuskWeight(publishedHours) * dry * (1f - weatherTarget);
            weatherWeight = Mathf.Lerp(weatherWeight, weatherTarget, k);

            Apply(ref ground, groundTarget, k, false);
            for (int i = 0; i < water.Length; i++) Apply(ref water[i], waterTarget, k, true);

            // Only fog weather thickens the air town-wide. Water mist is local to the lakes and rivers, so it must not
            // brighten every lamp cone in the city at dusk. Neither must the thin night haze.
            FogAmount = Mathf.Min(ground.Weight, weatherWeight);
        }

        /// <param name="ambientLit">Water mist: lit by the sky ambient, because the key light is ~0 through dusk.</param>
        private void Apply(ref Layer layer, float target, float k, bool ambientLit)
        {
            if (layer.Fog == null || layer.Profile == null) return;
            layer.Weight = Mathf.Lerp(layer.Weight, target, k);
            if (target <= 0f && layer.Weight < 0.005f) layer.Weight = 0f;

            bool on = layer.Weight > 0f;
            if (layer.Fog.gameObject.activeSelf != on) layer.Fog.gameObject.SetActive(on);
            if (!on) return;

            float density = layer.AuthoredDensity * layer.Weight;
            // Water mist keeps its pale albedo: tinted toward the dusk fog colour it vanishes into the background.
            Color albedo = ambientLit ? layer.AuthoredAlbedo : Color.Lerp(layer.AuthoredAlbedo, publishedFogColor, albedoFromFogColor);
            albedo.a = layer.AuthoredAlbedo.a;
            Color lit = layer.Profile.sunColor;
            if (ambientLit)
            {
                // Sky ambient hue with a brightness floor, so the mist still reads as the evening darkens.
                lit = (RenderSettings.ambientSkyColor + RenderSettings.ambientEquatorColor) * 0.5f;
                float peak = lit.maxColorComponent;
                if (peak < MinMistLight) lit *= MinMistLight / Mathf.Max(peak, 0.01f);
                lit.a = 1f;
            }
            if (Mathf.Abs(layer.Profile.density - density) > 0.0005f || !Approximately(layer.Profile.albedo, albedo)
                || !Approximately(layer.Profile.sunColor, lit))
            {
                layer.Profile.density = density;
                layer.Profile.albedo = albedo;
                layer.Profile.sunColor = lit;
                layer.Fog.UpdateMaterialProperties();
            }
        }

        /// <summary>0 outside the dusk window: builds quickly at sunset, then lifts slowly as the evening goes on.</summary>
        private float DuskWeight(float hours)
        {
            if (hours < duskHours.x || hours > duskHours.y) return 0f;
            float fadeIn = Mathf.InverseLerp(duskHours.x, duskHours.x + duskFadeInHours, hours);
            float fadeOut = 1f - Mathf.InverseLerp(duskHours.y - duskFadeOutHours, duskHours.y, hours);
            return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(Mathf.Min(fadeIn, fadeOut)));
        }

        private bool IsDisabledTier(int level)
        {
            var names = QualitySettings.names;
            if (level < 0 || level >= names.Length) return false;
            foreach (var n in disabledOnQualityLevels)
                if (string.Equals(n, names[level], System.StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static bool Approximately(Color a, Color b) =>
            Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) < 0.01f;
    }
}
