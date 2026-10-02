using UnityEngine;
using VolumetricFogAndMist2;

namespace KryticalNova.World
{
    /// <summary>
    /// Feeds the weather + day cycle into the Volumetric Fog &amp; Mist 2 ground-mist volume: density follows the
    /// weather's fog density (relative to a clear day) and the mist albedo leans toward the presented fog colour.
    /// Sun/moon colour and direction come from the VolumetricFogManager's light sync, so this only owns density/tint.
    /// Play mode works on an instanced profile (<see cref="VolumetricFog.settings"/>), so the profile asset is never
    /// written at runtime. Disabled on headless servers and on low quality tiers (the PC renderer owns the feature).
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(60)] // after EnvironmentPresenter (50) has published this frame's atmosphere
    public sealed class VolumetricAtmosphereDriver : MonoBehaviour
    {
        [SerializeField] private VolumetricFog fog;
        [Tooltip("Weather fog density that maps to the profile's authored mist density (WP_Clear is ~0.0022).")]
        [SerializeField] private float clearFogDensity = 0.0024f;
        [Tooltip("Clamp on the weather multiplier: thin on a clear noon, thick in WP_Fog / blizzards.")]
        [SerializeField] private Vector2 densityMultiplierRange = new Vector2(0.6f, 3.5f);
        [Range(0f, 1f)] [SerializeField] private float albedoFromFogColor = 0.65f;
        [Tooltip("Seconds to ease density changes (weather blends are already smooth; this hides quality-tier pops).")]
        [Min(0.01f)] [SerializeField] private float smoothingSeconds = 1.5f;
        [SerializeField] private string[] disabledOnQualityLevels = { "Mobile" };

        private static Color publishedFogColor = Color.gray;
        private static float publishedFogDensity = -1f;

        private VolumetricFogProfile runtimeProfile;
        private float authoredDensity;
        private Color authoredAlbedo;
        private float density;
        private int lastQualityLevel = -1;

        /// <summary>Called by <see cref="EnvironmentPresenter"/> every presented frame.</summary>
        public static void Publish(Color fogColor, float fogDensity)
        {
            publishedFogColor = fogColor;
            publishedFogDensity = fogDensity;
        }

        private void OnEnable()
        {
#if UNITY_SERVER
            enabled = false;
            return;
#else
            if (Application.isBatchMode || fog == null || fog.profile == null) { enabled = false; return; }
            authoredDensity = fog.profile.density;
            authoredAlbedo = fog.profile.albedo;
            density = authoredDensity;
            runtimeProfile = fog.settings; // instanced copy from here on
            lastQualityLevel = -1;
#endif
        }

        private void LateUpdate()
        {
            int quality = QualitySettings.GetQualityLevel();
            if (quality != lastQualityLevel)
            {
                lastQualityLevel = quality;
                fog.gameObject.SetActive(!IsDisabledTier(quality));
            }
            if (!fog.isActiveAndEnabled || publishedFogDensity < 0f) return;

            float mult = Mathf.Clamp(publishedFogDensity / Mathf.Max(1e-5f, clearFogDensity),
                densityMultiplierRange.x, densityMultiplierRange.y);
            float k = 1f - Mathf.Exp(-Time.deltaTime / smoothingSeconds);
            density = Mathf.Lerp(density, authoredDensity * mult, k);

            Color albedo = Color.Lerp(authoredAlbedo, publishedFogColor, albedoFromFogColor);
            albedo.a = authoredAlbedo.a;

            if (Mathf.Abs(runtimeProfile.density - density) > 0.002f || !Approximately(runtimeProfile.albedo, albedo))
            {
                runtimeProfile.density = density;
                runtimeProfile.albedo = albedo;
                fog.UpdateMaterialProperties();
            }
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
