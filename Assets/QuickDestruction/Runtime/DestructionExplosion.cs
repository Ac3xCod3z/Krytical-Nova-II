using System.Collections.Generic;
using UnityEngine;

namespace RipVertices.QuickDestruction
{
    /// <summary>Distance curve used by a scene-wide destruction explosion.</summary>
    public enum ExplosionFalloff
    {
        /// <summary>Every overlapping destructible receives full damage.</summary>
        None = 0,
        /// <summary>Damage decreases evenly from the centre to zero at the radius.</summary>
        Linear = 1,
        /// <summary>Concentrated damage: strong near the centre and much softer at the edge.</summary>
        Quadratic = 2
    }

    /// <summary>
    /// Scene-wide explosion API for Quick Destruction. A single non-allocating physics query
    /// discovers nearby colliders, then each DestructibleRoot receives at most one damage call.
    /// The radial direction is derived from the explosion origin, not from a surface normal.
    /// </summary>
    public static class DestructionExplosion
    {
        const int ColliderCapacity = 512;
        static readonly Collider[] s_colliders = new Collider[ColliderCapacity];
        sealed class ExplosionState
        {
            public bool ReportedCapacity;
        }

        static readonly ExplosionState s_state = new ExplosionState();

        /// <summary>Upward lift used for bodies that carry no profile of their own.</summary>
        internal const float DefaultUpwardShare = 0.25f;

        /// <summary>
        /// Vertical offset handed to Rigidbody.AddExplosionForce, resolved from the authored
        /// lift and the blast radius. Unity lowers the origin by this offset and pushes each
        /// body along the line from there to its centre of mass, so a fixed distance in metres
        /// yields less and less lift as the radius grows: at a 3 m radius, 0.25 m tilts a piece
        /// two metres away by about seven degrees, which reads as a flat sideways shove rather
        /// than a blast. Taken as a share of the radius the launch angle depends only on how
        /// close a piece is relative to the blast, so a small prop and a building-sized
        /// detonation throw material alike. A radius of zero means "no distance falloff" and
        /// offers no length to scale against, so the authored value is used unchanged.
        /// A negative lift still drives material down, which is what authoring it that way
        /// asks for.
        /// </summary>
        public static float ResolveUpwardOffset(float authoredLift, float radius) =>
            radius > 0f ? authoredLift * radius : authoredLift;

        struct Target
        {
            public DestructibleRoot Root;
            public float Distance;
            public Vector3 Direction;
        }

        /// <summary>
        /// Damages and pushes all destructibles overlapping a sphere. This overload uses
        /// linear falloff, all layers, ignores triggers, affects ordinary Rigidbodies, and
        /// returns the number of unique scene objects influenced.
        /// </summary>
        public static int Detonate(Vector3 origin, float radius, float damage, float force)
        {
            return Detonate(origin, radius, damage, force, ExplosionFalloff.Linear,
                ~0, QueryTriggerInteraction.Ignore, 0, 0);
        }

        /// <summary>
        /// Damages and pushes all destructibles overlapping a sphere. Multiple colliders on
        /// one baked object are deduplicated, so one bomb calls ApplyDamage only once per root.
        /// Surviving Dynamic intact bodies receive the physical shockwave; a broken target
        /// transfers the same radial force to the fragments through its normal break path.
        /// </summary>
        public static int Detonate(Vector3 origin, float radius, float damage, float force,
            ExplosionFalloff falloff, int layerMask = ~0,
            QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.Ignore,
            int damageTypeId = 0, int sourceId = 0,
            bool affectOtherRigidbodies = true)
        {
            return DetonateCore(origin, radius, damage, force, falloff, layerMask,
                triggerInteraction, damageTypeId, sourceId, affectOtherRigidbodies,
                null, null);
        }

        internal static int SpreadImpact(DestructibleRoot struckRoot,
            in DamageContext context, float radius, float impulse,
            Transform ignoredHierarchy) => DestructionImpactSpread.Dispatch(
                struckRoot, context, radius, impulse, ignoredHierarchy);

        static int DetonateCore(Vector3 origin, float radius, float damage, float force,
            ExplosionFalloff falloff, int layerMask,
            QueryTriggerInteraction triggerInteraction, int damageTypeId, int sourceId,
            bool affectOtherRigidbodies, DestructibleRoot excludedRoot,
            Transform ignoredHierarchy)
        {
            radius = Mathf.Max(0f, radius);
            damage = Mathf.Max(0f, damage);
            force = Mathf.Max(0f, force);
            if (radius <= 0f || (damage <= 0f && force <= 0f))
                return 0;

            // A zero-damage impact wave must not enter the post-fracture damage path:
            // that path can activate attached shell pieces even when Amount is zero.
            bool pushOnlyImpact = excludedRoot != null && damage <= 0f;
            int colliderCount = Physics.OverlapSphereNonAlloc(origin, radius, s_colliders,
                layerMask, triggerInteraction);
            if (colliderCount >= ColliderCapacity && !s_state.ReportedCapacity)
            {
                s_state.ReportedCapacity = true;
                Debug.LogWarning("Quick Destruction explosion found at least " +
                    ColliderCapacity + " colliders. The bounded query was truncated; " +
                    "split extremely dense blast zones or reduce their collision layers.");
            }

            // A local list keeps chained/nested detonations safe. The expensive physics query
            // itself is allocation-free and the expected target count is normally very small.
            var targets = new List<Target>(Mathf.Min(colliderCount, 32));
            var otherBodies = affectOtherRigidbodies && force > 0f
                ? new List<Rigidbody>(Mathf.Min(colliderCount, 32))
                : null;
            for (int i = 0; i < colliderCount; i++)
            {
                Collider hit = s_colliders[i];
                s_colliders[i] = null;
                if (hit == null) continue;

                DestructibleRoot root = hit.GetComponentInParent<DestructibleRoot>();
                if (excludedRoot != null)
                {
                    if (root == excludedRoot ||
                        IsImpactSource(hit.transform, sourceId) ||
                        (ignoredHierarchy != null &&
                         (hit.transform.IsChildOf(ignoredHierarchy) ||
                          (root != null && ignoredHierarchy.IsChildOf(root.transform)))))
                        continue;
                }
                if (root == null || pushOnlyImpact)
                {
                    if (root != null && (!root.isActiveAndEnabled || root.Profile == null ||
                        root.State == DestructibleState.Recycled)) continue;
                    Rigidbody body = otherBodies != null ? hit.attachedRigidbody : null;
                    if (body != null && !body.isKinematic && !otherBodies.Contains(body))
                        otherBodies.Add(body);
                    continue;
                }
                if (!root.isActiveAndEnabled || root.Profile == null ||
                    root.State == DestructibleState.Recycled) continue;

                Vector3 closest = hit.ClosestPoint(origin);
                Vector3 radial = closest - origin;
                float distance = radial.magnitude;
                if (radial.sqrMagnitude < 0.000001f)
                {
                    radial = root.transform.position - origin;
                    if (radial.sqrMagnitude < 0.000001f) radial = Vector3.up;
                }
                Vector3 direction = radial.normalized;

                int existing = -1;
                for (int t = 0; t < targets.Count; t++)
                {
                    if (targets[t].Root == root)
                    {
                        existing = t;
                        break;
                    }
                }

                if (existing < 0)
                {
                    targets.Add(new Target
                    {
                        Root = root,
                        Distance = distance,
                        Direction = direction
                    });
                }
                else if (distance < targets[existing].Distance)
                {
                    Target target = targets[existing];
                    target.Distance = distance;
                    target.Direction = direction;
                    targets[existing] = target;
                }
            }

            int affected = 0;
            for (int i = 0; i < targets.Count; i++)
            {
                Target target = targets[i];
                float attenuation = EvaluateFalloff(target.Distance, radius, falloff);
                if (attenuation <= 0f) continue;

                float appliedDamage = damage * attenuation;
                // Unity's AddExplosionForce performs its own linear distance attenuation.
                // Passing a pre-attenuated value would accidentally apply falloff twice.
                float appliedForce = force;
                DestructibleRoot root = target.Root;
                if (root == null || !root.isActiveAndEnabled) continue;
                bool influenced = false;
                bool wasFractured = root.State >= DestructibleState.Fractured;
                bool willBreakFromDamage = !wasFractured &&
                                           appliedDamage >= root.Health &&
                                           (root.Profile == null ||
                                            root.Profile.AcceptsHitDamage(appliedDamage));
                // A surviving Dynamic object receives one wave below through
                // AddExplosionForce. Passing the same force as BreakEnergy here pushed it
                // once through ApplyDamage and then a second time through that wave.
                // Breaking and fractured targets still need the energy in their normal
                // fragment path.
                float damageBreakEnergy = wasFractured || willBreakFromDamage
                    ? appliedForce
                    : 0f;

                // ImpactDirection is a projectile's travel vector, and DamageContext documents
                // Vector3.zero as the radial response. A blast has no travel vector: passing
                // one root-level bearing here made every downstream launch follow that single
                // line, so the whole object left sideways together instead of each piece
                // leaving along its own outward vector. The bearing listeners want is still on
                // Normal, and DirectionalForce stays explicitly zero because the radial push
                // below already carries the shockwave.
                if (appliedDamage > 0f || wasFractured)
                {
                    // An area hit is already propagated. Calling the local receiver also
                    // prevents neighbouring profiles from recursively emitting new waves.
                    root.ApplyDamageWithoutSpread(new DamageContext(
                        appliedDamage, origin, target.Direction, radius,
                        damageTypeId, sourceId, damageBreakEnergy,
                        Vector3.zero, 0f));
                    influenced = true;
                }

                // If Health survived, the target did not enter the fragment activation path.
                // A genuinely dynamic intact prop should still be pushed by the blast wave.
                if (appliedForce > 0f && root.State < DestructibleState.Fractured &&
                    root.IntactBody != null && !root.IntactBody.isKinematic)
                {
                    DestructionProfile profile = root.Profile;
                    float upward = ResolveUpwardOffset(profile != null
                        ? profile.ExplosionUpwardModifier : DefaultUpwardShare, radius);
                    root.IntactBody.AddExplosionForce(appliedForce, origin, radius, upward,
                        ForceMode.Impulse);
                    root.IntactBody.WakeUp();
                    influenced = true;
                }

                if (influenced) affected++;
            }

            if (otherBodies != null)
            {
                for (int i = 0; i < otherBodies.Count; i++)
                {
                    Rigidbody body = otherBodies[i];
                    if (body == null || body.isKinematic) continue;
                    // Ordinary props keep the plain radial impulse. Upward Lift is authored
                    // per destructible profile, and a Rigidbody that owns no profile has
                    // nothing to read it from; Detonate_PushesMultiColliderOrdinaryRigidbody-
                    // ExactlyOnce pins this wave as purely radial.
                    float bodyForce = force;
                    float upward = 0f;
                    DestructibleRoot root = pushOnlyImpact ? body.GetComponentInParent<DestructibleRoot>() : null;
                    if (root != null && root.Profile != null)
                    {
                        DestructionProfile profile = root.Profile;
                        upward = ResolveUpwardOffset(profile.ExplosionUpwardModifier, radius);
                        if (root.State >= DestructibleState.Fractured)
                        {
                            bodyForce *= Mathf.Max(0f, profile.FragmentPushMultiplier);
                            bodyForce = Mathf.Min(bodyForce,
                                Mathf.Max(0.001f, body.mass) * Mathf.Max(0.01f, profile.MaxFragmentLaunchSpeed));
                        }
                    }
                    body.AddExplosionForce(bodyForce, origin, radius, upward, ForceMode.Impulse);
                    body.WakeUp();
                    affected++;
                }
            }

            return affected;
        }

        static bool IsImpactSource(Transform target, int sourceId)
        {
            if (sourceId == 0) return false;
            for (Transform current = target; current != null; current = current.parent)
                if (current.gameObject.GetHashCode() == sourceId) return true;
            return false;
        }

        /// <summary>Returns the normalized influence at a distance for the selected curve.</summary>
        public static float EvaluateFalloff(float distance, float radius,
            ExplosionFalloff falloff)
        {
            if (radius <= 0f || distance >= radius) return 0f;
            if (falloff == ExplosionFalloff.None) return 1f;

            float linear = 1f - Mathf.Clamp01(Mathf.Max(0f, distance) / radius);
            return falloff == ExplosionFalloff.Quadratic ? linear * linear : linear;
        }

        /// <summary>
        /// Plays one blast particle at a world position and cleans it up afterwards. The
        /// destructibles a blast hits already raise their own break particles through their
        /// Break Effects component; this is the separate effect that belongs to the explosion
        /// itself — the fireball, not the dust off the wall.
        ///
        /// Deliberately not pooled: a blast is a rare one-shot, unlike the per-hit effects the
        /// break-effect pool exists for.
        /// </summary>
        /// <param name="prefab">Particle prefab. Null does nothing, so the effect stays optional.</param>
        /// <param name="position">World position to play it at.</param>
        /// <param name="scale">Uniform scale applied on top of the prefab's own.</param>
        /// <param name="lifetime">Seconds before cleanup. 0 reads it from the prefab.</param>
        public static ParticleSystem SpawnBlastParticle(ParticleSystem prefab,
            Vector3 position, float scale = 1f, float lifetime = 0f)
        {
            if (prefab == null || !Application.isPlaying) return null;

            ParticleSystem instance = Object.Instantiate(prefab, position,
                Quaternion.identity);
            instance.transform.localScale =
                prefab.transform.localScale * Mathf.Max(0.01f, scale);
            instance.Play(true);
            Object.Destroy(instance.gameObject,
                lifetime > 0f ? lifetime : ResolveParticleDuration(instance));
            return instance;
        }

        /// <summary>
        /// How long a system needs before every particle it emitted is gone: its own emission
        /// duration plus the longest lifetime any of those particles can have.
        /// </summary>
        static float ResolveParticleDuration(ParticleSystem system)
        {
            float longest = 0f;
            ParticleSystem[] all = system.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == null) continue;
                ParticleSystem.MainModule main = all[i].main;
                longest = Mathf.Max(longest,
                    main.duration + main.startLifetime.constantMax);
            }
            return Mathf.Max(0.25f, longest);
        }
    }
}
