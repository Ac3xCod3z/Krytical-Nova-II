using System.Collections.Generic;
using UnityEngine;

namespace RipVertices.QuickDestruction
{
    // Impact propagation is scoped separately from scene-wide explosions.
    internal static class DestructionImpactSpread
    {
        internal static bool DamageFades(ImpactSpreadFalloff mode) =>
            mode == ImpactSpreadFalloff.DamageAndImpulse || mode == ImpactSpreadFalloff.DamageOnly;
        internal static bool ImpulseFades(ImpactSpreadFalloff mode) =>
            mode == ImpactSpreadFalloff.DamageAndImpulse || mode == ImpactSpreadFalloff.ImpulseOnly;
        internal static float Influence(float distance, float radius, bool fades) =>
            radius <= 0f || distance >= radius ? 0f : (fades ? 1f - distance / radius : 1f);

        internal static bool Excluded(Transform item, int sourceId, Transform ignored)
        {
            if (ignored != null && item.IsChildOf(ignored)) return true;
            if (sourceId != 0)
                for (Transform t = item; t != null; t = t.parent)
                    if (t.gameObject.GetHashCode() == sourceId) return true;
            return false;
        }

        internal static int Dispatch(DestructibleRoot struck, in DamageContext context,
            float radius, float impulse, Transform ignored)
        {
            var profile = struck.Profile;
            float damage = Mathf.Max(0f, context.Amount) * Mathf.Clamp(profile.ImpactSpreadDamagePercent, 0f, 200f) * 0.01f;
            float force = Mathf.Max(0f, impulse) * Mathf.Clamp(profile.ImpactSpreadImpulsePercent, 0f, 200f) * 0.01f;
            if (radius <= 0f || (damage <= 0f && force <= 0f)) return 0;
            Transform scope = struck.ResolvedImpactSpreadParent;
            var roots = scope.GetComponentsInChildren<DestructibleRoot>();
            // Snapshot existing movable bodies. Newly activated pieces get their impulse
            // through BreakContext, including delayed activation, so never push them twice.
            var bodies = force > 0f ? scope.GetComponentsInChildren<Rigidbody>() : null;
            var movable = new List<Rigidbody>();
            if (bodies != null)
                foreach (var body in bodies)
                    if (!body.isKinematic) movable.Add(body);
            int affected = 0;
            foreach (var root in roots)
            {
                if (root.Profile == null || !root.isActiveAndEnabled ||
                    root.State == DestructibleState.Recycled ||
                    Excluded(root.transform, context.SourceId, ignored) ||
                    (ignored != null && ignored.IsChildOf(root.transform))) continue;
                if (root.ReceiveImpactSpread(context, radius, damage, force,
                    profile.SpreadFalloff, profile.CollisionLayers, root == struck, ignored)) affected++;
            }
            foreach (var body in movable)
            {
                if (body == null || body.isKinematic || !body.gameObject.activeInHierarchy ||
                    Excluded(body.transform, context.SourceId, ignored)) continue;
                var root = body.GetComponentInParent<DestructibleRoot>();
                if (root != null && (root.Profile == null || !root.isActiveAndEnabled ||
                    root.State == DestructibleState.Recycled ||
                    (ignored != null && ignored.IsChildOf(root.transform)))) continue;
                float distance = float.PositiveInfinity;
                foreach (var collider in body.GetComponentsInChildren<Collider>())
                {
                    if (collider.attachedRigidbody != body || !collider.enabled || collider.isTrigger ||
                        !collider.gameObject.activeInHierarchy ||
                        (profile.CollisionLayers.value & (1 << collider.gameObject.layer)) == 0 ||
                        Excluded(collider.transform, context.SourceId, ignored)) continue;
                    distance = Mathf.Min(distance, Vector3.Distance(context.Point, collider.ClosestPoint(context.Point)));
                }
                if (distance >= radius || (root == struck && root.IsDirectImpactBody(body, context))) continue;
                // Unity owns linear attenuation exactly once. Zero disables attenuation
                // only after the hard distance check above has admitted this body.
                float physicsRadius = ImpulseFades(profile.SpreadFalloff) ? radius : 0f;
                float bodyForce = force;
                float upward = 0f;
                if (root != null)
                {
                    upward = DestructionExplosion.ResolveUpwardOffset(root.Profile.ExplosionUpwardModifier, radius);
                    if (root.State >= DestructibleState.Fractured)
                    {
                        bodyForce *= Mathf.Max(0f, root.Profile.FragmentPushMultiplier);
                        bodyForce = Mathf.Min(bodyForce, Mathf.Max(0.001f, body.mass) *
                            Mathf.Max(0.01f, root.Profile.MaxFragmentLaunchSpeed));
                    }
                }
                body.AddExplosionForce(bodyForce, context.Point, physicsRadius, upward, ForceMode.Impulse);
                body.WakeUp();
                affected++;
            }
            return affected;
        }
    }
}
