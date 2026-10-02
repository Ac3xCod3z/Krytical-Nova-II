using UnityEngine;

namespace RipVertices.QuickDestruction
{
    /// <summary>
    /// Drop-in projectile / melee hit helper. Attach this component to a bullet, thrown object
    /// or trigger volume to send a local damage request to the first DestructibleRoot it hits.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Quick Destruction/Destruction Damage Dealer")]
    public sealed class DestructionDamageDealer : MonoBehaviour
    {
        [Header("Damage")]
        [Tooltip("Health damage sent to the hit destructible. Ignored when Break Immediately is enabled.")]
        [Min(0.01f)] public float Damage = 35f;
        [Tooltip("Only fragments near the contact point are activated after a break. Set to 0 to activate all baked fragments.")]
        [Min(0f)] public float Radius = 0.75f;
        [Tooltip("Skip Health and fracture immediately at the hit point.")]
        public bool BreakImmediately;
        [Tooltip("Launch energy applied to fragments when this hit breaks the object.")]
        [Min(0f)] public float BreakForce = 15f;
        [Tooltip("Directional impulse in N s for this hit. -1 uses the target profile, 0 disables the directional component, and positive values override it. Heavier bodies gain less speed. Radius controls how many local fragments may react.")]
        [Min(-1f)] public float DirectionalForce = -1f;
        [Tooltip("Optional gameplay category forwarded in DamageContext.")]
        public int DamageTypeId;

        [Header("Hit Filtering")]
        [Tooltip("The projectile will not damage a destructible that contains this transform. Assign the shooter root when needed.")]
        public Transform Owner;
        [Tooltip("A simple bullet damages one destructible, then ignores further contacts.")]
        public bool SingleHit = true;

        [Header("Bullet Lifecycle")]
        [Tooltip("Remove this bullet after it damages a destructible.")]
        public bool DestroyAfterHit = true;
        [Tooltip("Small delay lets collision VFX or audio play before the bullet is removed.")]
        [Min(0f)] public float DestroyDelay = 0.02f;

        bool m_hasHit;

        void OnCollisionEnter(Collision collision)
        {
            if (collision == null || collision.collider == null) return;

            ContactPoint contact = collision.contactCount > 0
                ? collision.GetContact(0)
                : default;
            Vector3 point = collision.contactCount > 0
                ? contact.point
                : collision.collider.ClosestPoint(transform.position);
            Vector3 normal = collision.contactCount > 0
                ? contact.normal
                : -transform.forward;
            DealTo(collision.collider, point, normal);
        }

        void OnTriggerEnter(Collider other)
        {
            if (other == null) return;
            Vector3 point = other.ClosestPoint(transform.position);
            Vector3 normal = point - transform.position;
            if (normal.sqrMagnitude < 0.0001f) normal = -transform.forward;
            else normal.Normalize();
            DealTo(other, point, normal);
        }

        void DealTo(Collider hitCollider, Vector3 point, Vector3 normal)
        {
            if (SingleHit && m_hasHit) return;

            DestructibleRoot root = hitCollider.GetComponentInParent<DestructibleRoot>();
            if (root == null) return;
            if (Owner != null && root.transform.IsChildOf(Owner)) return;

            if (SingleHit) m_hasHit = true;
            int sourceId = gameObject.GetHashCode();
            Vector3 impactDirection = ResolveImpactDirection();
            int targetStructuralGroup = root.ResolveStructuralGroup(hitCollider);
            if (BreakImmediately)
            {
                root.BreakNow(new BreakContext(point, normal, Radius, BreakForce,
                    sourceId, impactDirection, DirectionalForce,
                    targetStructuralGroup), Owner);
            }
            else
            {
                root.ApplyDamage(new DamageContext(
                    Damage, point, normal, Radius, DamageTypeId, sourceId,
                    BreakForce, impactDirection, DirectionalForce,
                    targetStructuralGroup), Owner);
            }

            if (DestroyAfterHit)
                Destroy(gameObject, DestroyDelay);
        }

        Vector3 ResolveImpactDirection()
        {
            Rigidbody body = GetComponent<Rigidbody>();
            if (body != null)
            {
#if UNITY_6000_0_OR_NEWER
                Vector3 velocity = body.linearVelocity;
#else
                Vector3 velocity = body.velocity;
#endif
                if (velocity.sqrMagnitude > 0.000001f)
                    return velocity.normalized;
            }
            return transform.forward;
        }
    }
}
