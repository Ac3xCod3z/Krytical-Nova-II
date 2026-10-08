using System.Collections.Generic;
using Animancer;
using Unity.Netcode;
using UnityEngine;

namespace KryticalNova.GameFeel
{
    /// <summary>
    /// A blink of slow motion on one character, on this screen only: for a moment the character is shown where it
    /// was a little while ago, falling further behind its real place, and then it catches up. Its animation runs at
    /// the same slowed and then quickened pace, so pose and place stay together. Nothing real is touched: the
    /// character's root, the game's clock and the network all carry on, so it is safe on the host as well as on a
    /// client. Used for the body a critical kill throws, on the killer's screen.
    /// </summary>
    // After Motion Warping (order 100), which rewrites a thrown body's root every LateUpdate.
    [DefaultExecutionOrder(200)]
    [DisallowMultipleComponent]
    public sealed class ActorTimeSlow : MonoBehaviour
    {
        /// <summary>The slowest and the fastest a slowed character's animation is ever set to run.</summary>
        public const float SlowestPace = 0.02f, FastestPace = 4f;
        private const int HistoryLength = 160;

        private struct Shown
        {
            public Transform part;
            public Vector3 cleanLocal;  // where the part sat before it was held back
            public Vector3 heldLocal;   // where holding it back put it: if it is still there, nothing else has moved it
        }

        private readonly Vector3[] points = new Vector3[HistoryLength];
        private readonly float[] times = new float[HistoryLength];
        private int newest, count;
        private readonly List<Shown> held = new List<Shown>(4);

        private AnimancerComponent animancer;
        private float began, slowSeconds, slowRate, catchUpSeconds, lagBefore;
        private bool playing;

        /// <summary>How far behind its real place the character is being shown right now, in seconds (0 when not playing).</summary>
        public float Lag { get; private set; }

        /// <summary>The character's slow motion, created on first use. Moments reported from a child land on the root.</summary>
        public static ActorTimeSlow For(Transform actor)
        {
            if (actor == null) return null;
            var net = actor.GetComponentInParent<NetworkObject>();
            if (net != null) actor = net.transform;
            return actor.TryGetComponent(out ActorTimeSlow slow) ? slow : actor.gameObject.AddComponent<ActorTimeSlow>();
        }

        /// <summary>
        /// How far behind its real place a character is shown, in seconds, <paramref name="t"/> seconds into a slow:
        /// falling behind at a steady pace while it is shown at <paramref name="rate"/> of real speed, then easing
        /// back to nothing over the catch-up.
        /// </summary>
        public static float LagAt(float t, float slowSeconds, float rate, float catchUpSeconds)
        {
            if (t <= 0f || t >= slowSeconds + catchUpSeconds) return 0f;
            float furthest = slowSeconds * (1f - rate);
            return t < slowSeconds
                ? t * (1f - rate)
                : furthest * (1f - Mathf.SmoothStep(0f, 1f, (t - slowSeconds) / Mathf.Max(catchUpSeconds, 0.0001f)));
        }

        /// <summary>
        /// Shows the character at <paramref name="rate"/> of real speed for <paramref name="seconds"/>, then lets it
        /// catch up with where it really is over <paramref name="catchUp"/> seconds.
        /// </summary>
        public void Play(float seconds, float rate, float catchUp)
        {
            if (seconds <= 0f || rate >= 1f || !isActiveAndEnabled) return;
            if (playing) Finish();
            slowSeconds = seconds;
            slowRate = Mathf.Clamp01(rate);
            catchUpSeconds = Mathf.Max(0.01f, catchUp);
            began = Time.time;
            lagBefore = 0f;
            Lag = 0f;
            count = 0;
            animancer = GetComponentInChildren<AnimancerComponent>();
            playing = true;
        }

        private void LateUpdate()
        {
            if (!playing) return;
            Release();
            Remember(transform.position);

            float t = Time.time - began;
            if (t >= slowSeconds + catchUpSeconds) { Finish(); return; }
            Lag = LagAt(t, slowSeconds, slowRate, catchUpSeconds);

            // The animation keeps the same pace as the picture: slow while it falls behind, quick while it catches up.
            if (animancer != null && animancer.IsGraphInitialized && Time.deltaTime > 0f)
                animancer.Graph.Speed = Mathf.Clamp(1f - (Lag - lagBefore) / Time.deltaTime, SlowestPace, FastestPace);
            lagBefore = Lag;

            Vector3 back = Recall(Time.time - Lag) - transform.position;
            if (back.sqrMagnitude < 1e-8f) return;
            // Everything that is seen of the character rides back with it: the body, and effects attached to it.
            foreach (Transform part in transform)
            {
                if (part.GetComponentInChildren<Renderer>(false) == null) continue;
                Vector3 clean = part.localPosition;
                part.position += back;
                held.Add(new Shown { part = part, cleanLocal = clean, heldLocal = part.localPosition });
            }
        }

        private void OnDisable()
        {
            if (playing) Finish();
        }

        private void Finish()
        {
            playing = false;
            Lag = 0f;
            Release();
            if (animancer != null && animancer.IsGraphInitialized) animancer.Graph.Speed = 1f;
        }

        /// <summary>Puts every part back where it sat, unless something else has moved it since.</summary>
        private void Release()
        {
            foreach (Shown shown in held)
                if (shown.part != null && shown.part.parent == transform && shown.part.localPosition == shown.heldLocal)
                    shown.part.localPosition = shown.cleanLocal;
            held.Clear();
        }

        private void Remember(Vector3 point)
        {
            newest = count == 0 ? 0 : (newest + 1) % HistoryLength;
            points[newest] = point;
            times[newest] = Time.time;
            if (count < HistoryLength) count++;
        }

        /// <summary>Where the root was at a moment in the past (the oldest place known when that is before it all began).</summary>
        private Vector3 Recall(float when)
        {
            int index = newest;
            for (int i = 1; i < count; i++)
            {
                int older = (newest - i + HistoryLength) % HistoryLength;
                if (times[older] <= when)
                    return Vector3.Lerp(points[older], points[index], Mathf.InverseLerp(times[older], times[index], when));
                index = older;
            }
            return points[index];
        }
    }
}
