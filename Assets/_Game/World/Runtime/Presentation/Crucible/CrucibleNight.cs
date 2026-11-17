using UnityEngine;

namespace KryticalNova.World
{
    /// <summary>
    /// The Crucible is always night (owner's list, 16 Nov 2026, section 4): while the camera is inside the map's sky dome the world's
    /// one directional light, sky, ambient and APV scenario show midnight (<see cref="EnvironmentPresenter.LocalTimeOfDay01"/>), the
    /// moon as in Zone01, and the camera sees out to the dome (far clip at least 600 m). Leaving the dome gives the world clock back.
    /// Client presentation only (World.Presentation is left out of the server); it decides nothing.
    /// </summary>
    public sealed class CrucibleNight : MonoBehaviour
    {
        [Tooltip("Time of day shown inside the map: 0 = midnight (the moon highest).")]
        [Range(0f, 1f)] public float hour01 = 0f;
        [Tooltip("Inside this distance from the map's heart (horizontal, metres) the night holds: the sky dome's radius.")]
        public float radius = 500f;
        [Tooltip("The camera's far clip while inside (the dome is 500 m out).")]
        public float farClip = 650f;

        bool m_Inside;
        float m_FarClipBefore;
        Camera m_Camera;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => EnvironmentPresenter.LocalTimeOfDay01 = -1f;

        void LateUpdate()
        {
            Camera cam = Camera.main;
            if (cam != m_Camera) { Leave(); m_Camera = cam; }
            if (cam == null) return;
            Vector3 d = cam.transform.position - transform.position;
            d.y = 0f;
            bool inside = d.sqrMagnitude <= radius * radius;
            if (inside && !m_Inside)
            {
                m_Inside = true;
                m_FarClipBefore = cam.farClipPlane;
                cam.farClipPlane = Mathf.Max(cam.farClipPlane, farClip);
            }
            else if (!inside && m_Inside) Leave();
            if (m_Inside) EnvironmentPresenter.LocalTimeOfDay01 = hour01;
        }

        void OnDisable() => Leave();

        void Leave()
        {
            if (!m_Inside) return;
            m_Inside = false;
            EnvironmentPresenter.LocalTimeOfDay01 = -1f;
            if (m_Camera != null) m_Camera.farClipPlane = m_FarClipBefore;
        }
    }
}
