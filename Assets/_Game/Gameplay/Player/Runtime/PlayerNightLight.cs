using KryticalNova.World;
using UnityEngine;

namespace KryticalNova.Player
{
    /// <summary>
    /// Keeps the local player readable at night: a soft, shadowless point light above the character that fades in at
    /// dusk and out at dawn (the classic RPG "hero light"). Only one exists: it follows whichever avatar the local camera
    /// is on (offline avatar, then the owned network player). Remote players never get one. Presentation only; never
    /// runs on a headless server.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerNightLight : MonoBehaviour
    {
        private static PlayerNightLight current;

        // Hours (24h): fade in over the first pair, fade out over the second.
        private static readonly Vector2 DuskHours = new Vector2(18.5f, 20f);
        private static readonly Vector2 DawnHours = new Vector2(5f, 6.5f);
        private const float MaxIntensity = 2.2f;
        private static readonly Color FillColor = new Color(0.78f, 0.84f, 1f); // cool moonlit white, reads against warm lamps

        private Light fill;

        /// <summary>Moves the single night light onto <paramref name="avatar"/>.</summary>
        public static void AttachTo(Transform avatar)
        {
#if UNITY_SERVER
            return;
#else
            if (avatar == null || Application.isBatchMode) return;
            if (current != null)
            {
                if (current.transform == avatar) return;
                Destroy(current);
            }
            current = avatar.gameObject.AddComponent<PlayerNightLight>();
#endif
        }

        private void Awake()
        {
            var go = new GameObject("Night Fill Light");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 2.6f, -0.6f);
            fill = go.AddComponent<Light>();
            fill.type = LightType.Point;
            fill.range = 6.5f;
            fill.color = FillColor;
            fill.shadows = LightShadows.None;
#if UNITY_EDITOR
            fill.lightmapBakeType = LightmapBakeType.Realtime; // Editor-only property; a light made at run time is realtime anyway
#endif
            fill.renderMode = LightRenderMode.ForcePixel;
            fill.intensity = 0f;
            fill.enabled = false;
        }

        private void OnDestroy()
        {
            if (fill != null) Destroy(fill.gameObject);
            if (current == this) current = null;
        }

        private void LateUpdate()
        {
            float hours = EnvironmentPresenter.PresentedTimeOfDay01 * 24f;
            float night = hours > 12f
                ? Mathf.InverseLerp(DuskHours.x, DuskHours.y, hours)
                : 1f - Mathf.InverseLerp(DawnHours.x, DawnHours.y, hours);

            float intensity = MaxIntensity * night;
            bool on = intensity > 0.01f;
            if (fill.enabled != on) fill.enabled = on;
            if (on) fill.intensity = intensity;
        }
    }
}
