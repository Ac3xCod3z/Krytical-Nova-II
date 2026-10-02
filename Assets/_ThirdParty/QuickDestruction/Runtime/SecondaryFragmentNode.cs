using System.Collections.Generic;
using UnityEngine;

namespace RipVertices.QuickDestruction
{
    /// <summary>
    /// Runtime controller for one pre-baked second-level fracture. The parent primary fragment
    /// remains a normal Rigidbody until impact damage reaches SecondaryStrength, then it is
    /// replaced by its inactive baked children without runtime mesh slicing.
    /// </summary>
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    public class SecondaryFragmentNode : MonoBehaviour
    {
        [Header("Baked References")]
        public DestructibleRoot Root;
        public int ParentFragmentIndex;
        public Rigidbody ParentBody;
        public Collider ParentCollider;
        public Collider ParentDynamicCollider;
        public List<Collider> ParentDynamicColliders = new List<Collider>();
        public Renderer ParentRenderer;
        public GameObject SecondaryRoot;
        public List<FragmentRecord> Children = new List<FragmentRecord>();

        public bool IsBroken => m_broken;
        public float Health => m_health;
        public float HealthNormalized => Root != null && Root.Profile != null &&
                                         Root.Profile.SecondaryStrength > 0f
            ? Mathf.Clamp01(m_health / Root.Profile.SecondaryStrength)
            : 0f;
        public int ActiveChildCount => m_activeCount;
        public int SettledChildCount => m_settledCount;

        float m_health;
        float m_lastDamageTime = -999f;
        int m_activeCount;
        int m_settledCount;
        bool m_broken;
        bool m_allChildDebrisDespawned;
        readonly RaycastHit[] m_groundGuardHits = new RaycastHit[8];

        // Interior scatter chips live on their own child Renderer, not on ParentRenderer.
        // Turning off only the parent's own renderer left those chips hanging in mid-air
        // once the piece was replaced by its children — scatter visibly belonging to
        // nothing. Gathered at runtime rather than baked into a field so prefabs baked
        // before this fix are corrected too.
        readonly List<Renderer> m_parentDetailRenderers = new List<Renderer>();

        void Awake()
        {
            m_health = Root != null && Root.Profile != null
                ? Root.Profile.SecondaryStrength : 35f;
            if (SecondaryRoot != null && SecondaryRoot.activeSelf)
                SecondaryRoot.SetActive(false);
            CollectParentDetailRenderers();
        }

        void CollectParentDetailRenderers()
        {
            m_parentDetailRenderers.Clear();
            Renderer[] found = GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < found.Length; i++)
            {
                Renderer candidate = found[i];
                if (candidate == null || candidate == ParentRenderer) continue;
                if (SecondaryRoot != null &&
                    candidate.transform.IsChildOf(SecondaryRoot.transform))
                    continue;
                m_parentDetailRenderers.Add(candidate);
            }
        }

        void OnCollisionEnter(Collision collision)
        {
            var profile = Root != null ? Root.Profile : null;
            StabilizeStaticGroundContact(collision);
            if (profile == null || m_broken ||
                profile.SecondaryBreakup == SecondaryBreakupLevel.Off ||
                !Root.CanSecondaryBreak(ParentFragmentIndex))
                return;
            if (((1 << collision.gameObject.layer) & profile.CollisionLayers.value) == 0)
                return;
            if (Time.time - m_lastDamageTime < profile.SecondaryDamageCooldown)
                return;

            float minVel = Mathf.Max(profile.SecondaryMinImpactVelocity, 3.5f);
            float velocity = collision.relativeVelocity.magnitude;
            if (velocity < minVel) return;
            // A fragment that has only just come loose is still pushing itself clear of the
            // neighbours it was cut from, and those depenetration contacts carry genuine
            // impulses. Without a settling window the collapse breaks every pre-baked
            // fragment in the same instant, which reads as everything bursting at a touch.
            if (!IsPastSettlingGrace(profile)) return;
            m_lastDamageTime = Time.time;

            // Impulse scales with mass, so a heavy block reached maximum damage simply by
            // coming to rest on the ground while a light chip never reached any. Dividing
            // by mass turns it into the impact speed, so the same blow is needed either way.
            float impulse = collision.impulse.magnitude;
            if (collision.rigidbody != null)
            {
                float strikingMass = collision.rigidbody.mass;
                float massThreshold = profile.SecondaryStrength * 0.5f;
                if (strikingMass < massThreshold) impulse *= Mathf.Max(0.1f, strikingMass / massThreshold);
            }

            float mass = ParentBody != null ? Mathf.Max(ParentBody.mass, 0.0001f) : 1f;
            float impactSpeed = impulse / mass;
            float damage = impactSpeed;
            if (profile.SecondaryImpulseToDamage != null && profile.SecondaryImpulseToDamage.length > 0)
            {
                Keyframe[] keys = profile.SecondaryImpulseToDamage.keys;
                float maxTime = keys[keys.Length - 1].time;
                float maxVal = keys[keys.Length - 1].value;
                if (impactSpeed > maxTime && maxTime > 0.01f)
                    damage = maxVal * Mathf.Sqrt(impactSpeed / maxTime);
                else
                    damage = profile.SecondaryImpulseToDamage.Evaluate(impactSpeed);
            }
            
            damage *= Mathf.Lerp(0.35f, 2.5f, profile.Brittleness);
            if (damage <= 0f) return;

            Vector3 point = transform.position;
            Vector3 normal = Vector3.up;
            if (collision.contactCount > 0)
            {
                ContactPoint contact = collision.GetContact(0);
                point = contact.point;
                normal = contact.normal;
            }

            ApplyImpactDamage(damage, point, normal);
        }

        void StabilizeStaticGroundContact(Collision collision)
        {
            if (ParentBody == null || ParentBody.isKinematic) return;
            if (!FragmentRuntime.TryGetExternalStaticGroundContact(
                    collision, Root, out ContactPoint contact))
                return;
#if UNITY_6000_0_OR_NEWER
            ParentBody.linearVelocity = FragmentRuntime.StabilizeStaticGroundVelocity(
                ParentBody.linearVelocity, contact.normal);
#else
            ParentBody.velocity = FragmentRuntime.StabilizeStaticGroundVelocity(
                ParentBody.velocity, contact.normal);
#endif
        }

        /// <summary>
        /// True once this fragment has had its settling window since coming loose. Only the
        /// collision path consults it: a deliberate hit routed through ApplyImpactDamage is
        /// the player's doing and should land whenever it is aimed.
        /// </summary>
        bool IsPastSettlingGrace(DestructionProfile profile)
        {
            if (profile.SecondaryImpactGrace <= 0f) return true;
            if (Root == null || ParentFragmentIndex < 0 ||
                ParentFragmentIndex >= Root.Fragments.Count) return true;
            FragmentRecord parent = Root.Fragments[ParentFragmentIndex];
            if (parent == null || !parent.Activated) return true;
            return Time.time - parent.ActivatedTime >= profile.SecondaryImpactGrace;
        }

        /// <summary>Applies damage to this primary fragment's second-level fracture.</summary>
        public bool ApplyImpactDamage(float amount, Vector3 point, Vector3 normal)
        {
            if (m_broken || Root == null ||
                !Root.CanSecondaryBreak(ParentFragmentIndex) ||
                !Root.Profile.AcceptsHitDamage(amount))
                return false;

            m_health -= Mathf.Clamp(amount, 0f, Root.Profile.SecondaryStrength * 1.1f);
            if (m_health > 0f) return false;
            return BreakSecondary(point, normal);
        }

        /// <summary>Immediately attempts to replace the parent with its baked child pieces.</summary>
        public bool BreakSecondary(Vector3 point, Vector3 normal)
        {
            var profile = Root != null ? Root.Profile : null;
            if (profile == null || m_broken ||
                !Root.CanSecondaryBreak(ParentFragmentIndex) ||
                SecondaryRoot == null)
                return false;

            int physicalChildren = 0;
            for (int i = 0; i < Children.Count; i++)
            {
                FragmentRecord child = Children[i];
                if (child == null) continue;
                if (child.Body != null) physicalChildren++;
            }
            int desired = Mathf.Min(physicalChildren,
                Root.GetSecondaryActivationAllowance(physicalChildren));
            if (physicalChildren > 0 && desired == 0)
                return false;

            int granted = desired;
            if (profile.EnforceActiveBodyBudget && desired > 0)
            {
                granted = DestructionBudgetManager.Instance.RequestReplacement(
                    profile.BudgetGroup, profile.GroupMaxActiveBodies, 1, desired);
            }
            if (desired > 0 && granted == 0)
                return false;

            Vector3 inheritedLinear = Vector3.zero;
            Vector3 inheritedAngular = Vector3.zero;
            if (ParentBody != null && !ParentBody.isKinematic &&
                profile.SecondaryInheritVelocity)
            {
#if UNITY_6000_0_OR_NEWER
                inheritedLinear = ParentBody.linearVelocity;
#else
                inheritedLinear = ParentBody.velocity;
#endif
                inheritedAngular = ParentBody.angularVelocity;
            }

            DisableParentBody();
            SecondaryRoot.SetActive(true);

            int activated = 0;
            float separationRadius = ParentRenderer != null
                ? Mathf.Max(0.25f, ParentRenderer.bounds.extents.magnitude * 2f)
                : 1f;

            for (int i = 0; i < Children.Count; i++)
            {
                FragmentRecord child = Children[i];
                if (child == null || child.Transform == null) continue;
                if (!child.Transform.gameObject.activeSelf)
                    child.Transform.gameObject.SetActive(true);
                FragmentDespawnTransition.Cancel(child.Transform);

                child.Activated = false;
                child.Settled = false;
                child.SleepTimer = 0f;

                if (activated >= granted)
                    continue;

                Rigidbody childBody = DestructibleRoot.EnsureFragmentPhysics(
                    child, profile);
                if (childBody == null) continue;

                child.Activated = true;
                child.ActivatedTime = Time.time;
                DestructibleRoot.SetFragmentProbeAnchor(child, null);
                DestructibleRoot.SetFragmentShadowMode(child, true);
                UseDynamicCollider(child);
                DestructibleRoot.ApplyDefaultDebrisContactMaterial(child);
                childBody.detectCollisions = true;
                childBody.isKinematic = false;
                childBody.maxDepenetrationVelocity = 2.5f;
                // Sweep-based against static geometry, for the same reason as primary debris:
                // Continuous Speculative still lets a thin piece cross a floor in one step,
                // and these children are thinner than the parent they replace. Deliberately
                // not ContinuousDynamic, which would also sweep them against each other and
                // report a stream of contacts from inside the pile. Released fragments
                // always sweep against static floors; authoring-time Discrete remains useful
                // while the complete baked shell is kinematic, not once a shard is flying.
                childBody.collisionDetectionMode = CollisionDetectionMode.Continuous;
                child.NeedsMeshGroundGuard =
                    FragmentRuntime.RequiresMeshGroundGuard(childBody);
                child.GroundGuardResolved = true;
                // Level-two children are smaller than the parent they replace, so the same
                // push spins them harder. Derive the limit from the profile rather than
                // inheriting the project-wide Default Max Angular Speed.
                childBody.maxAngularVelocity = profile.ResolveMaxAngularSpeed(
                    child.Renderer != null
                        ? child.Renderer.bounds.extents.magnitude
                        : 0f);
                // Same reasoning as the primary fragments: inherited motion is written straight
                // onto the body and is the one speed the profile never bounded, with the
                // tangential term carrying the parent's spin out to this child's distance.
                Vector3 inheritedMotion = profile.ClampInheritedVelocity(
                    inheritedLinear + Vector3.Cross(inheritedAngular,
                        child.Transform.position - transform.position));
#if UNITY_6000_0_OR_NEWER
                childBody.linearVelocity = inheritedMotion;
#else
                childBody.velocity = inheritedMotion;
#endif
                childBody.angularVelocity = inheritedAngular;
                childBody.WakeUp();
                // Held to the same speed budget as every other launch: these children are
                // smaller than the piece they replace, and an impulse divided by mass is a
                // speed, so an unclamped separation kick reaches whatever speed their mass
                // happens to allow.
                float separationForce = RuntimeFragmentSplitter.ClampImpulseToLaunchSpeed(
                    profile.SecondarySeparationForce *
                    Mathf.Max(0f, profile.FragmentPushMultiplier),
                    child.Body.mass, profile.MaxFragmentLaunchSpeed);
                if (separationForce > 0f)
                {
                    child.Body.AddExplosionForce(
                        separationForce, point,
                        separationRadius, 0f, ForceMode.Impulse);
                }
                activated++;
            }

            m_broken = true;
            m_activeCount = activated;
            m_settledCount = 0;
            Root.NotifyPrimaryReplaced(
                ParentFragmentIndex, desired > 0);
            Root.NotifySecondaryBreak(ParentFragmentIndex, activated);
            return true;
        }

        void DisableParentBody()
        {
            if (ParentBody != null)
            {
                if (!ParentBody.isKinematic)
                {
#if UNITY_6000_0_OR_NEWER
                    ParentBody.linearVelocity = Vector3.zero;
#else
                    ParentBody.velocity = Vector3.zero;
#endif
                    ParentBody.angularVelocity = Vector3.zero;
                }
                ParentBody.isKinematic = true;
                ParentBody.detectCollisions = false;
            }
            if (ParentCollider != null) ParentCollider.enabled = false;
            SetCollidersEnabled(ParentDynamicColliders, false);
            if (ParentDynamicCollider != null) ParentDynamicCollider.enabled = false;
            if (ParentRenderer != null) ParentRenderer.enabled = false;
            SetRenderersEnabled(m_parentDetailRenderers, false);
        }

        void FixedUpdate()
        {
            var profile = Root != null ? Root.Profile : null;
            if (!m_broken || profile == null) return;
            // The debris auto-despawn sweep keeps running after every child settled,
            // because frozen children still pay renderer and collider cost.
            bool sceneCleanup = DestructionBudgetManager.OverridesProfileRemoval(Root);
            bool debrisSweep = !sceneCleanup && profile.DespawnDebris && !m_allChildDebrisDespawned;
            if (m_activeCount <= 0 && !debrisSweep) return;

            float velocitySqr = profile.SleepVelocity * profile.SleepVelocity;
            float dt = Time.fixedDeltaTime;
            bool debrisRemaining = false;
            for (int i = 0; i < Children.Count; i++)
            {
                FragmentRecord child = Children[i];
                if (child == null || child.Body == null) continue;

                if (!sceneCleanup && profile.DespawnDebris && !child.DebrisDespawned &&
                    (child.Activated || child.Settled))
                {
                    if (Time.time - child.ActivatedTime > profile.DespawnDebrisAfter)
                    {
                        DespawnChildDebris(child);
                        continue;
                    }
                    debrisRemaining = true;
                }

                if (!child.Activated || child.Settled) continue;

                if (child.Renderer != null &&
                    FragmentRuntime.UsesMeshGroundGuard(profile) &&
                    (!child.GroundGuardResolved || child.NeedsMeshGroundGuard))
                {
                    if (!child.GroundGuardResolved)
                    {
                        child.NeedsMeshGroundGuard =
                            FragmentRuntime.RequiresMeshGroundGuard(child.Body);
                        child.Runtime = child.Body.GetComponent<FragmentRuntime>();
                        child.GroundGuardResolved = true;
                    }
                    if (child.NeedsMeshGroundGuard)
                        FragmentRuntime.GuardStaticGroundCrossing(child.Body,
                            child.Renderer.bounds, Root.transform, m_groundGuardHits,
                            profile.CollisionLayers, dt, true,
                            child.Runtime != null &&
                            child.Runtime.HasSustainedGroundContact);
                }

#if UNITY_6000_0_OR_NEWER
                float speedSqr = child.Body.linearVelocity.sqrMagnitude;
#else
                float speedSqr = child.Body.velocity.sqrMagnitude;
#endif
                if (child.Body.IsSleeping() || speedSqr < velocitySqr)
                    child.SleepTimer += dt;
                else
                    child.SleepTimer = 0f;

                bool lifetimeExpired = profile.LimitActiveTime &&
                    profile.CleanupLifetime > 0f &&
                    Time.time - child.ActivatedTime > profile.CleanupLifetime;
                bool settledByRest = profile.CleanupSettledFragments &&
                    child.SleepTimer >= profile.SleepDuration;
                if (settledByRest || lifetimeExpired)
                    SettleChild(child);
            }

            if (!sceneCleanup && profile.DespawnDebris)
                m_allChildDebrisDespawned = !debrisRemaining;
        }

        void SettleChild(FragmentRecord child) =>
            SettleChild(child, Root.AutomaticCleanupAction, Root.Profile.SmoothDespawn);

        void SettleChild(FragmentRecord child, CleanupAction action, bool smoothDespawn,
            DebrisDespawnMode despawnMode = DebrisDespawnMode.SmoothShrink)
        {
            child.Settled = true;
            m_activeCount = Mathf.Max(0, m_activeCount - 1);
            m_settledCount++;

            switch (action)
            {
                case CleanupAction.None:
                    break;
                case CleanupAction.FreezeAsProxy:
                    if (child.Body != null) child.Body.isKinematic = true;
                    UseExactCollider(child);
                    break;
                case CleanupAction.Recycle:
                    if (child.Body != null) child.Body.isKinematic = true;
                    if (child.Collider != null) child.Collider.enabled = false;
                    SetCollidersEnabled(child.DynamicColliders, false);
                    if (child.DynamicCollider != null) child.DynamicCollider.enabled = false;
                    if (child.Transform != null)
                    {
                        if (smoothDespawn)
                            FragmentDespawnTransition.Begin(child.Transform, despawnMode);
                        else
                            child.Transform.gameObject.SetActive(false);
                    }
                    break;
            }

            Root.NotifySecondarySettled(1, action);
        }

        /// <summary>
        /// Retires one child for good, mirroring the root's debris despawn: frozen,
        /// collider-free and faded, without double-counting a child the cleanup already
        /// settled.
        /// </summary>
        void DespawnChildDebris(FragmentRecord child, DebrisDespawnMode? mode = null)
        {
            if (child == null || child.DebrisDespawned) return;
            child.DebrisDespawned = true;
            DebrisDespawnMode despawnMode = mode ?? (Root.Profile.SmoothDespawn
                ? DebrisDespawnMode.SmoothShrink : DebrisDespawnMode.Instant);

            if (!child.Settled)
            {
                SettleChild(child, CleanupAction.Recycle,
                    despawnMode != DebrisDespawnMode.Instant, despawnMode);
                return;
            }
            Root.ReleaseSettledBodyBudget(1);
            if (child.Body != null) child.Body.isKinematic = true;
            if (child.Collider != null) child.Collider.enabled = false;
            SetCollidersEnabled(child.DynamicColliders, false);
            if (child.DynamicCollider != null) child.DynamicCollider.enabled = false;
            if (child.Transform != null && child.Transform.gameObject.activeSelf)
            {
                FragmentDespawnTransition.Begin(child.Transform, despawnMode);
            }
        }

        internal void DespawnChildForSceneBudget(FragmentRecord child, DebrisDespawnMode mode)
        {
            DespawnChildDebris(child, mode);
        }

        /// <summary>Despawns every loose or settled child immediately. Returns the count.</summary>
        internal int DespawnAllChildDebris()
        {
            int despawned = 0;
            for (int i = 0; i < Children.Count; i++)
            {
                FragmentRecord child = Children[i];
                if (child == null || child.Body == null || child.DebrisDespawned) continue;
                if (!child.Activated && !child.Settled) continue;
                DespawnChildDebris(child);
                despawned++;
            }
            if (despawned > 0) m_allChildDebrisDespawned = true;
            return despawned;
        }

        internal void ResetFromRoot()
        {
            for (int i = 0; i < Children.Count; i++)
            {
                FragmentRecord child = Children[i];
                if (child == null || child.Transform == null) continue;

                if (child.Body != null)
                {
                    if (!child.Body.isKinematic)
                    {
#if UNITY_6000_0_OR_NEWER
                        child.Body.linearVelocity = Vector3.zero;
#else
                        child.Body.velocity = Vector3.zero;
#endif
                        child.Body.angularVelocity = Vector3.zero;
                    }
                    child.Body.isKinematic = true;
                    child.Body.detectCollisions = true;
                }

                child.Transform.localPosition = child.InitialLocalPosition;
                child.Transform.localRotation = child.InitialLocalRotation;
                child.Transform.localScale = child.InitialLocalScale;
                if (!child.Transform.gameObject.activeSelf)
                    child.Transform.gameObject.SetActive(true);
                FragmentDespawnTransition.Cancel(child.Transform);
                DestructibleRoot.SetFragmentProbeAnchor(
                    child, child.AttachedProbeAnchor);
                DestructibleRoot.SetFragmentShadowMode(child, false);
                UseExactCollider(child);
                child.Activated = false;
                child.Settled = false;
                child.DebrisDespawned = false;
                child.SleepTimer = 0f;
                child.GroundGuardResolved = false;
                child.NeedsMeshGroundGuard = false;
            }

            if (SecondaryRoot != null) SecondaryRoot.SetActive(false);
            if (ParentCollider != null) ParentCollider.enabled = true;
            if ((ParentDynamicColliders != null && ParentDynamicColliders.Count > 0) ||
                ParentDynamicCollider != null)
            {
                bool parentDynamic = ParentBody != null && !ParentBody.isKinematic;
                ParentCollider.enabled = !parentDynamic;
                SetCollidersEnabled(ParentDynamicColliders, parentDynamic);
                if (ParentDynamicCollider != null)
                    ParentDynamicCollider.enabled = parentDynamic;
            }
            if (ParentRenderer != null) ParentRenderer.enabled = true;
            SetRenderersEnabled(m_parentDetailRenderers, true);
            if (ParentBody != null) ParentBody.detectCollisions = true;

            m_health = Root != null && Root.Profile != null
                ? Root.Profile.SecondaryStrength : 35f;
            m_lastDamageTime = -999f;
            m_activeCount = 0;
            m_settledCount = 0;
            m_allChildDebrisDespawned = false;
            m_broken = false;
        }

        static void UseDynamicCollider(FragmentRecord fragment)
        {
            if (fragment == null) return;
            bool hasCompound = fragment.DynamicColliders != null &&
                               fragment.DynamicColliders.Count > 0;
            if (!hasCompound && fragment.DynamicCollider == null) return;
            if (fragment.Collider != null) fragment.Collider.enabled = false;
            SetCollidersEnabled(fragment.DynamicColliders, true);
            if (fragment.DynamicCollider != null)
                fragment.DynamicCollider.enabled = true;
        }

        static void UseExactCollider(FragmentRecord fragment)
        {
            if (fragment == null) return;
            SetCollidersEnabled(fragment.DynamicColliders, false);
            if (fragment.DynamicCollider != null) fragment.DynamicCollider.enabled = false;
            if (fragment.Collider != null) fragment.Collider.enabled = true;
        }

        static void SetRenderersEnabled(List<Renderer> renderers, bool enabled)
        {
            if (renderers == null) return;
            for (int i = 0; i < renderers.Count; i++)
                if (renderers[i] != null) renderers[i].enabled = enabled;
        }

        static void SetCollidersEnabled(List<Collider> colliders, bool enabled)
        {
            if (colliders == null) return;
            for (int i = 0; i < colliders.Count; i++)
                if (colliders[i] != null) colliders[i].enabled = enabled;
        }
    }
}
