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
                someoneNear = false;
                var bodies = PlayerBodies.All;
                for (int i = 0; i < bodies.Count; i++)
                {
                    if (bodies[i] == null || bodies[i].Transform == null) continue;
                    Vector3 d = bodies[i].Transform.position - centre;
                    if (Mathf.Abs(d.y) > heightRange) continue;
                    d.y = 0f;
                    if (d.sqrMagnitude <= openDistance * openDistance) { someoneNear = true; break; }
                }
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
