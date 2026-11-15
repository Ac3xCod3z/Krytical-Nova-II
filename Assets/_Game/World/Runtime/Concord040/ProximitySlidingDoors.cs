using KryticalNova.Core;
using UnityEngine;

namespace KryticalNova.World
{
    /// <summary>
    /// Automatic sliding doors (Concord 040's Accord Hall glass, owner 9 Nov 2026): the two leaves slide apart along
    /// their local X when any player comes within <see cref="openDistance"/> metres of the doorway, and close again
    /// when everyone has left. Purely a look: every machine moves its own copy from the players it sees, nothing is
    /// sent over the network, and the leaves carry no collider.
    /// </summary>
    public sealed class ProximitySlidingDoors : MonoBehaviour
    {
        [SerializeField] private Transform leftLeaf;
        [SerializeField] private Transform rightLeaf;
        [Tooltip("How close (metres, flat distance to the doorway's centre) a player must be for the doors to open.")]
        [SerializeField, Min(0.5f)] private float openDistance = 7f;
        [Tooltip("How far each leaf slides; 0 = its own width.")]
        [SerializeField, Min(0f)] private float slideDistance = 0f;
        [SerializeField, Min(0.1f)] private float speed = 3f;
        [Tooltip("Players further above or below the doorway than this are ignored.")]
        [SerializeField, Min(0.5f)] private float heightRange = 5f;

        private Vector3 leftClosed, rightClosed, leftDir, rightDir, centre;
        private float leftSlide, rightSlide, open, nextScan;
        private bool someoneNear;

        /// <summary>Wires the leaves (the scene builder calls this).</summary>
        public void Setup(Transform left, Transform right)
        {
            leftLeaf = left;
            rightLeaf = right;
        }

        private void Awake()
        {
            if (leftLeaf == null || rightLeaf == null) { enabled = false; return; }
            leftClosed = leftLeaf.localPosition;
            rightClosed = rightLeaf.localPosition;
            // Each leaf moves away from the other one, along the line between them.
            Vector3 across = rightLeaf.position - leftLeaf.position;
            across.y = 0f;
            across = across.sqrMagnitude > 0.0001f ? across.normalized : transform.right;
            leftDir = leftLeaf.parent != null ? leftLeaf.parent.InverseTransformDirection(-across) : -across;
            rightDir = rightLeaf.parent != null ? rightLeaf.parent.InverseTransformDirection(across) : across;
            leftSlide = slideDistance > 0f ? slideDistance : Width(leftLeaf, across);
            rightSlide = slideDistance > 0f ? slideDistance : Width(rightLeaf, across);
            centre = (leftLeaf.position + rightLeaf.position) * 0.5f;
        }

        /// <summary>Anyone within <paramref name="openDistance"/> (flat) and <paramref name="heightRange"/> (up or down) of the doorway.</summary>
        public static bool AnyNear(System.Collections.Generic.List<Vector3> places, Vector3 centre, float openDistance, float heightRange)
        {
            for (int i = 0; i < places.Count; i++)
            {
                Vector3 d = places[i] - centre;
                if (Mathf.Abs(d.y) > heightRange) continue;
                d.y = 0f;
                if (d.sqrMagnitude <= openDistance * openDistance) return true;
            }
            return false;
        }

        /// <summary>
        /// Every player body's place, gathered once per frame by the first door that scans and reused by the others (perf clean-ups
        /// round 2, chunk 11: each door walked every body's transform every 0.2 s). Bodies without a transform are left out, as before.
        /// </summary>
        public static class PlayerPlaces
        {
            static readonly System.Collections.Generic.List<Vector3> s_Places = new System.Collections.Generic.List<Vector3>(64);
            static int s_Frame = -1;

            [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
            static void ResetStatics() { s_Places.Clear(); s_Frame = -1; }

            public static System.Collections.Generic.List<Vector3> ThisFrame()
            {
                if (s_Frame == Time.frameCount) return s_Places;
                s_Frame = Time.frameCount;
                Gather(PlayerBodies.All, s_Places);
                return s_Places;
            }

            /// <summary>The places of <paramref name="bodies"/> in their order, skipping missing bodies and transforms.</summary>
            public static void Gather(System.Collections.Generic.IReadOnlyList<IPlayerBody> bodies, System.Collections.Generic.List<Vector3> into)
            {
                into.Clear();
                if (bodies == null) return;
                for (int i = 0; i < bodies.Count; i++)
                    if (bodies[i] != null && bodies[i].Transform != null) into.Add(bodies[i].Transform.position);
            }
        }

        private static float Width(Transform leaf, Vector3 across)
        {
            var r = leaf.GetComponentInChildren<Renderer>();
            if (r == null) return 1.5f;
            Vector3 s = r.bounds.size;
            return Mathf.Max(0.5f, Mathf.Abs(Vector3.Dot(s, new Vector3(Mathf.Abs(across.x), 0f, Mathf.Abs(across.z)))));
        }

        private void Update()
        {
            if (Time.time >= nextScan)
            {
                nextScan = Time.time + 0.2f;
                someoneNear = AnyNear(PlayerPlaces.ThisFrame(), centre, openDistance, heightRange);
            }
            float target = someoneNear ? 1f : 0f;
            if (Mathf.Approximately(open, target)) return;
            float maxSlide = Mathf.Max(leftSlide, rightSlide, 0.1f);
            open = Mathf.MoveTowards(open, target, speed / maxSlide * Time.deltaTime);
            float eased = Mathf.SmoothStep(0f, 1f, open);
            leftLeaf.localPosition = leftClosed + leftDir * (leftSlide * eased);
            rightLeaf.localPosition = rightClosed + rightDir * (rightSlide * eased);
        }
    }
}
