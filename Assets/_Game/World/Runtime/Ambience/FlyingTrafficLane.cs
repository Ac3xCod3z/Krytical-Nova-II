using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

namespace KryticalNova.World.Ambience
{
    /// <summary>
    /// Ambient flying traffic: moves its child vehicles along a closed spline, banking into turns with a gentle bob.
    /// Positions come from the replicated world clock (falls back to local time offline), so every client sees the
    /// cars in roughly the same place with no network traffic. Pure presentation: no colliders, no physics, and the
    /// lane switches itself (and its cars) off on a headless server.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FlyingTrafficLane : MonoBehaviour
    {
        [SerializeField] private SplineContainer spline;
        [Tooltip("Vehicles driven along the lane (children placed by Krytical Nova > Zone > Build City Traffic).")]
        [SerializeField] private Transform[] vehicles = System.Array.Empty<Transform>();
        [Tooltip("Where each vehicle starts along the loop (0-1).")]
        [SerializeField] private float[] startOffsets = System.Array.Empty<float>();
        [Tooltip("Sideways offset of each vehicle from the spline, in metres (two-lane traffic).")]
        [SerializeField] private float[] laneOffsets = System.Array.Empty<float>();
        [Tooltip("Cruise speed in metres per second.")]
        [Min(0.1f)] [SerializeField] private float speed = 22f;
        [SerializeField] private bool reverse;
        [Tooltip("Degrees of bank per degree of heading change over the look-ahead distance.")]
        [Range(0f, 3f)] [SerializeField] private float bankPerTurnDegree = 1.2f;
        [Range(0f, 45f)] [SerializeField] private float maxBank = 25f;
        [SerializeField] private float lookAheadMetres = 12f;
        [SerializeField] private float bobAmplitude = 0.35f;
        [SerializeField] private float bobFrequency = 0.6f;

        private float length;
        private float[] bank;

        private void OnEnable()
        {
            if (Application.isBatchMode || IsServerBuild)
            {
                foreach (var v in vehicles) if (v != null) v.gameObject.SetActive(false);
                enabled = false;
                return;
            }
            if (spline == null) spline = GetComponent<SplineContainer>();
            length = spline != null ? spline.CalculateLength() : 0f;
            if (bank == null || bank.Length != vehicles.Length) bank = new float[vehicles.Length];
        }

        private static bool IsServerBuild
        {
            get
            {
#if UNITY_SERVER
                return true;
#else
                return false;
#endif
            }
        }

        private void LateUpdate()
        {
            if (spline == null || length <= 1f) return;

            var env = WorldEnvironmentNetwork.Instance;
            double now = env != null ? env.Now : Time.timeAsDouble;
            double loops = now * speed / length;
            float ahead = lookAheadMetres / length;
            float dt = Time.deltaTime;

            for (int i = 0; i < vehicles.Length; i++)
            {
                var v = vehicles[i];
                if (v == null) continue;
                float start = i < startOffsets.Length ? startOffsets[i] : (float)i / vehicles.Length;
                float u = (float)((loops + start) % 1.0);
                if (reverse) u = 1f - u;
                float uAhead = Mathf.Repeat(u + (reverse ? -ahead : ahead), 1f);

                spline.Evaluate(u, out float3 pos, out float3 tan, out float3 up);
                spline.Evaluate(uAhead, out _, out float3 tanAhead, out _);
                Vector3 forward = math.normalizesafe(reverse ? -tan : tan, new float3(0f, 0f, 1f));
                Vector3 forwardAhead = math.normalizesafe(reverse ? -tanAhead : tanAhead, new float3(0f, 0f, 1f));

                // Bank into the turn coming up (smoothed so knots never snap the roll).
                float turn = Vector3.SignedAngle(Flat(forward), Flat(forwardAhead), Vector3.up);
                float targetBank = Mathf.Clamp(-turn * bankPerTurnDegree, -maxBank, maxBank);
                bank[i] = Mathf.Lerp(bank[i], targetBank, 1f - Mathf.Exp(-dt * 3f));

                Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
                float lane = i < laneOffsets.Length ? laneOffsets[i] : 0f;
                float bob = Mathf.Sin((float)(now * bobFrequency) + i * 1.7f) * bobAmplitude;
                v.SetPositionAndRotation((Vector3)pos + right * lane + Vector3.up * bob,
                                         Quaternion.LookRotation(forward, Vector3.up) * Quaternion.Euler(0f, 0f, bank[i]));
            }
        }

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

#if UNITY_EDITOR
        /// <summary>Editor builder hook: wires the lane in one call.</summary>
        public void Configure(SplineContainer container, Transform[] cars, float[] starts, float[] lanes, float cruiseSpeed, bool backwards)
        {
            spline = container;
            vehicles = cars;
            startOffsets = starts;
            laneOffsets = lanes;
            speed = cruiseSpeed;
            reverse = backwards;
        }
#endif
    }
}
