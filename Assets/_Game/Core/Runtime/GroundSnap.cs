using UnityEngine;
using UnityEngine.AI;

namespace KryticalNova.Core
{
    /// <summary>
    /// Finds the visible surface under a point for client presentation (ground rings, dust, click VFX). Character pivots
    /// and NavMesh positions sit a few centimetres below the paving and tiles, so anything placed there ends up under
    /// the ground. This casts down onto real colliders and skips characters (anything with a NavMeshAgent,
    /// CharacterController or non-kinematic Rigidbody) and an optional root to ignore, e.g. the selected target.
    /// </summary>
    public static class GroundSnap
    {
        private const float Above = 1.2f;
        private const float Below = 1.5f;
        private static readonly RaycastHit[] Hits = new RaycastHit[16];
        private static int mask = -1;

        private static int Mask
        {
            get
            {
                if (mask == -1)
                    mask = Physics.DefaultRaycastLayers & ~LayerMask.GetMask("Water", "UI", "UIPreview", "Interactable");
                return mask;
            }
        }

        /// <summary>The visible ground point (and its normal) under <paramref name="point"/>; the input if none is found.</summary>
        public static Vector3 Snap(Vector3 point, out Vector3 normal, Transform ignoreRoot = null)
        {
            if (TryGetGround(point, out RaycastHit ground, ignoreRoot))
            {
                normal = ground.normal;
                return ground.point;
            }
            normal = Vector3.up;
            return point;
        }

        /// <summary>The visible ground under <paramref name="point"/> as a raycast hit, for callers that need to know what it is.</summary>
        public static bool TryGetGround(Vector3 point, out RaycastHit ground, Transform ignoreRoot = null)
        {
            ground = default;
            int count = Physics.RaycastNonAlloc(point + Vector3.up * Above, Vector3.down, Hits, Above + Below, Mask,
                QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                var hit = Hits[i];
                if (hit.distance >= best || IsCharacter(hit.collider, ignoreRoot)) continue;
                best = hit.distance;
                ground = hit;
            }
            return best < float.MaxValue;
        }

        public static Vector3 Snap(Vector3 point, Transform ignoreRoot = null) => Snap(point, out _, ignoreRoot);

        private static bool IsCharacter(Collider c, Transform ignoreRoot)
        {
            if (ignoreRoot != null && c.transform.IsChildOf(ignoreRoot)) return true;
            var body = c.attachedRigidbody;
            if (body != null && !body.isKinematic) return true;
            return c.GetComponentInParent<NavMeshAgent>() != null || c.GetComponentInParent<CharacterController>() != null;
        }
    }
}
