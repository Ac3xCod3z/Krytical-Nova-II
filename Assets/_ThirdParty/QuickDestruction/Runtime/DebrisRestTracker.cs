using System.Collections.Generic;
using UnityEngine;

namespace RipVertices.QuickDestruction
{
    /// <summary>Tracks continuous supported rest, independent of a fragment's release age.</summary>
    internal sealed class DebrisRestTracker
    {
        sealed class Rest
        {
            public float Since, ActivatedTime;
            public Vector3 Position;
            public Quaternion Rotation;
            public int Scan;
        }

        readonly Dictionary<Transform, Rest> m_rest = new Dictionary<Transform, Rest>();
        readonly List<Transform> m_stale = new List<Transform>();
        readonly RaycastHit[] m_hits = new RaycastHit[32];
        int m_scan;

        internal void Clear() => m_rest.Clear();
        internal void Forget(Transform transform) => m_rest.Remove(transform);
        internal void BeginScan() => m_scan++;
        internal void EndScan()
        {
            m_stale.Clear();
            foreach (var pair in m_rest)
                if (pair.Key == null || pair.Value.Scan != m_scan) m_stale.Add(pair.Key);
            foreach (Transform transform in m_stale) m_rest.Remove(transform);
            m_stale.Clear();
        }

        internal bool Observe(DestructionBudgetManager.SceneDebrisCandidate piece, float now, float delay)
        {
            Transform transform = piece.Transform;
            if (transform == null) return false;
            if (!piece.CanRemove || !HasSupport(piece))
            {
                m_rest.Remove(transform);
                return false;
            }
            if (!m_rest.TryGetValue(transform, out Rest rest))
            {
                rest = new Rest { Since = now, ActivatedTime = piece.ActivatedTime,
                    Position = transform.position, Rotation = transform.rotation };
                m_rest.Add(transform, rest);
            }
            if (rest.ActivatedTime != piece.ActivatedTime ||
                (rest.Position - transform.position).sqrMagnitude > 0.0009f ||
                Quaternion.Angle(rest.Rotation, transform.rotation) > 3f)
            {
                rest.Since = now;
                rest.ActivatedTime = piece.ActivatedTime;
            }
            rest.Position = transform.position;
            rest.Rotation = transform.rotation;
            rest.Scan = m_scan;
            return now - rest.Since >= Mathf.Max(1f, delay);
        }

        static bool IsSlow(Rigidbody body, float radius)
        {
            if (body == null || body.isKinematic) return true;
#if UNITY_6000_0_OR_NEWER
            Vector3 velocity = body.linearVelocity;
#else
            Vector3 velocity = body.velocity;
#endif
            return velocity.sqrMagnitude <= 0.0225f &&
                body.angularVelocity.sqrMagnitude * radius * radius <= 0.0625f;
        }

        bool HasSupport(DestructionBudgetManager.SceneDebrisCandidate piece)
        {
            Collider collider = piece.Collider;
            if (collider == null || !collider.enabled || collider.isTrigger) return false;
            Bounds bounds = collider.bounds;
            if (!IsSlow(piece.Body, bounds.extents.magnitude)) return false;
            // Probe below the centre and four corners. A sleeping body still needs actual
            // support: zero velocity at an apex or a frozen proxy in mid-air is not rest.
            // Include other resting rubble so a pile can clean up from its top as well.
            for (int sample = 0; sample < 5; sample++)
            {
                Vector3 origin = bounds.center;
                if (sample > 0)
                {
                    origin.x += bounds.extents.x * ((sample & 1) == 0 ? 0.6f : -0.6f);
                    origin.z += bounds.extents.z * (sample <= 2 ? 0.6f : -0.6f);
                }
                int count = Physics.RaycastNonAlloc(origin, Vector3.down, m_hits,
                    bounds.extents.y + 0.06f, ~0, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < count; i++)
                {
                    Collider support = m_hits[i].collider;
                    if (support == collider || support.transform.IsChildOf(piece.Transform) ||
                        (piece.Body != null && support.attachedRigidbody == piece.Body) ||
                        Physics.GetIgnoreLayerCollision(collider.gameObject.layer, support.gameObject.layer) ||
                        Physics.GetIgnoreCollision(collider, support)) continue;
                    if (m_hits[i].normal.y >= 0.5f &&
                        IsSlow(support.attachedRigidbody, support.bounds.extents.magnitude)) return true;
                }
            }
            return false;
        }
    }
}
