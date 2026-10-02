using UnityEngine;

namespace RipVertices.QuickDestruction
{
    /// <summary>
    /// Tiny pooled-fragment transition. It deliberately uses a fixed duration so the
    /// beginner workflow stays free of another timing slider.
    /// </summary>
    [AddComponentMenu("")]
    internal sealed class FragmentDespawnTransition : MonoBehaviour
    {
        internal const float DefaultDuration = 0.35f;
        internal const float SinkDistance = 0.15f;

        bool m_running;
        float m_startedAt;
        float m_duration = DefaultDuration;
        Vector3 m_startScale;
        Vector3 m_startPosition;
        DebrisDespawnMode m_mode;

        internal static void Begin(Transform target, float duration = DefaultDuration) =>
            Begin(target, DebrisDespawnMode.SmoothShrink, duration);

        internal static void Begin(Transform target, DebrisDespawnMode mode,
            float duration = DefaultDuration)
        {
            if (target == null) return;
            if (mode == DebrisDespawnMode.Instant)
            {
                Cancel(target);
                target.gameObject.SetActive(false);
                return;
            }
            var transition = target.GetComponent<FragmentDespawnTransition>();
            if (transition == null)
                transition = target.gameObject.AddComponent<FragmentDespawnTransition>();
            transition.StartTransition(duration, mode);
        }

        internal static void Cancel(Transform target)
        {
            if (target == null) return;
            var transition = target.GetComponent<FragmentDespawnTransition>();
            if (transition != null) transition.StopTransition();
        }

        void StartTransition(float duration, DebrisDespawnMode mode)
        {
            m_duration = Mathf.Max(0.01f, duration);
            m_startScale = transform.localScale;
            m_startPosition = transform.position;
            m_mode = mode;
            m_startedAt = Time.time;
            m_running = true;
            enabled = true;
        }

        void StopTransition()
        {
            m_running = false;
            enabled = false;
        }

        void Update()
        {
            if (!m_running) return;

            float t = Mathf.Clamp01((Time.time - m_startedAt) / m_duration);
            transform.localScale = Vector3.Lerp(m_startScale, Vector3.zero, t);
            if (m_mode == DebrisDespawnMode.ShrinkAndSink)
                transform.position = m_startPosition + Vector3.down * (SinkDistance * t);
            if (t < 1f) return;

            m_running = false;
            gameObject.SetActive(false);
        }
    }
}
