using UnityEngine;
using VolumetricLights;

namespace KryticalNova.World
{
    /// <summary>
    /// A lit street lamp (prefab variant SM_Prop_Streetlight_01_Lit). Its Light is **Baked**: the street pool and the
    /// glow in the volumetric mist come from the APV "Night" lighting scenario, at no runtime cost. This only drives the
    /// visible light cone (Kronnect VolumetricLight): on from dusk to dawn, faint in clear air and strong in fog
    /// weather (<see cref="VolumetricAtmosphereDriver.FogAmount"/>). The cone also distance-toggles itself.
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

        private float nextCheck;
        private float appliedBrightness = -1f;

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
            bool on = hours >= onOffHours.x || hours < onOffHours.y;
            if (cone.enabled != on) cone.enabled = on;
            if (!on) return;

            float brightness = Mathf.Lerp(clearAirBrightness, fogBrightness, VolumetricAtmosphereDriver.FogAmount);
            if (Mathf.Abs(brightness - appliedBrightness) > 0.02f)
            {
                appliedBrightness = brightness;
                cone.brightness = brightness;
                cone.UpdateMaterialProperties();
            }
        }
    }
}
