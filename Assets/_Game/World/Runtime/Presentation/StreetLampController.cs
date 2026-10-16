using UnityEngine;
using VolumetricFogAndMist2;
using VolumetricLights;

namespace KryticalNova.World
{
    /// <summary>
    /// A lit street lamp (prefab variant SM_Prop_Streetlight_01_Lit). Its Light is **Baked**: the street pool and the
    /// glow in the volumetric mist come from the APV "Night" lighting scenario, at no runtime cost. This only drives the
    /// visible light cone (Kronnect VolumetricLight): on from dusk to dawn, faint in clear air and strong in fog
    /// weather (<see cref="VolumetricAtmosphereDriver.FogAmount"/>). The cone also distance-toggles itself. It follows
    /// the player's fog quality (<see cref="VolumetricQualityRuntime"/>): off with the fog, and drawn more finely and
    /// seen further away the higher the quality. On Ultra two extras: the cone of a lamp near the camera is cut by the
    /// walls and props in its way (its occlusion map is drawn once when it comes near), and the mist glows round the
    /// lamp (a fog point light; its Unity light is a few centimetres across, so it lights nothing but the fog).
    /// Never runs on a headless server.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StreetLampController : MonoBehaviour
    {
        [SerializeField] private VolumetricLight cone;
        [Tooltip("Hours (24h) the lamps switch on at dusk and off at dawn.")]
        [SerializeField] private Vector2 onOffHours = new Vector2(18.25f, 6f);
        // Relative to the lamp's intensity: cones are additive and dozens overlap at night, so keep them faint.
        [SerializeField] private float clearAirBrightness = 0.04f;
        [SerializeField] private float fogBrightness = 0.22f;
        [Tooltip("Seconds between checks; lamps don't need per-frame updates.")]
        [SerializeField] private float checkInterval = 0.5f;
        [Header("Ultra")]
        [Tooltip("Metres from the camera within which the cone is cut by what stands in its way.")]
        [SerializeField] private float occlusionRange = 25f;
        [Tooltip("Metres the mist glows round the lamp.")]
        [SerializeField] private float glowRadius = 4f;
        [Tooltip("How bright the glow in the mist is, relative to the lamp's light.")]
        [SerializeField] private float glowStrength = 0.12f; // the owner's pick of 16 Oct 2026 (option C)

        // The glow's Unity light: this small, it lights nothing in the scene (the lamp's own light is baked).
        private const float GlowLightRange = 0.05f;
        // VolumetricFog2's point-light falloff divides the range by 25 (PointLightManager).
        private const float FogRangeDivisor = 25f;

        private float nextCheck;
        private float appliedBrightness = -1f;
        private int appliedQuality = -1;
        private FogPointLight glow;
        private static Camera viewer;

        private void OnEnable()
        {
#if UNITY_SERVER
            enabled = false;
#else
            if (Application.isBatchMode) { enabled = false; return; }
            // Spread checks so hundreds of lamps don't all update on the same frame.
            nextCheck = Time.time + Random.value * checkInterval;
            if (cone != null) cone.enabled = false;
#endif
        }

        private void Update()
        {
            if (cone == null || Time.time < nextCheck) return;
            nextCheck = Time.time + checkInterval;

            float hours = EnvironmentPresenter.PresentedTimeOfDay01 * 24f;
            bool on = (hours >= onOffHours.x || hours < onOffHours.y) && VolumetricQualityRuntime.Quality != VolumetricQuality.Off;
            if (cone.enabled != on) cone.enabled = on;
            bool ultra = on && VolumetricQualityRuntime.Quality == VolumetricQuality.Ultra;
            SetGlow(ultra);
            if (!on) return;
            if (appliedQuality != VolumetricQualityRuntime.Version) ApplyQuality();
            SetOcclusion(ultra && NearCamera());

            float brightness = Mathf.Lerp(clearAirBrightness, fogBrightness, VolumetricAtmosphereDriver.FogAmount);
            if (Mathf.Abs(brightness - appliedBrightness) > 0.02f)
            {
                appliedBrightness = brightness;
                cone.brightness = brightness;
                cone.UpdateMaterialProperties();
            }
        }

        private bool NearCamera()
        {
            if (viewer == null || !viewer.isActiveAndEnabled) viewer = Camera.main;
            return viewer != null && (viewer.transform.position - cone.transform.position).sqrMagnitude < occlusionRange * occlusionRange;
        }

        /// <summary>The cone cut by what stands in its way. The world near a lamp stands still, so its map is drawn once, when it comes on.</summary>
        private void SetOcclusion(bool occlude)
        {
            if (cone.enableShadows == occlude) return;
            cone.enableShadows = occlude;
            cone.shadowBakeInterval = ShadowBakeInterval.OnStart;
            cone.UpdateMaterialProperties();
        }

        private void SetGlow(bool shown)
        {
            if (shown && glow == null)
            {
                Light lamp = cone.GetComponent<Light>();
                var go = new GameObject("Fog Glow");
                go.transform.SetParent(cone.transform, false);
                var light = go.AddComponent<Light>();
                light.type = LightType.Point;
                light.range = GlowLightRange;
                light.shadows = LightShadows.None;
                light.color = lamp != null ? lamp.color : Color.white;
                light.intensity = lamp != null ? lamp.intensity : 10f;
                glow = go.AddComponent<FogPointLight>();
                glow.inscattering = glowRadius * FogRangeDivisor / GlowLightRange;
                glow.intensity = glowStrength;
            }
            if (glow != null && glow.gameObject.activeSelf != shown) glow.gameObject.SetActive(shown);
        }

        private void ApplyQuality()
        {
            appliedQuality = VolumetricQualityRuntime.Version;
            VolumetricQualityRuntime.Tier tier = VolumetricQualityRuntime.Current;
            cone.raymarchPreset = RaymarchPresets.UserDefined;
            cone.raymarchQuality = tier.ConeRaymarch;
            cone.raymarchMinStep = tier.ConeMinStep;
            cone.jittering = tier.ConeJitter;
            cone.distanceStartDimming = tier.ConeFadeFrom;
            cone.distanceDeactivation = tier.ConeGoneAt;
            cone.UpdateMaterialProperties();
        }
    }
}
