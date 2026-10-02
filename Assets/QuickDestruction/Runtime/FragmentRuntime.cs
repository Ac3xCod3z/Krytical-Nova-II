using UnityEngine;
using RipVertices.QuickDestruction.Runtime;

namespace RipVertices.QuickDestruction
{
    /// <summary>
    /// Lightweight per-fragment component. It keeps ground dust separate from ordinary
    /// damage hits: one primary fragment can raise it once, only after a real fall onto
    /// static upward-facing ground.
    /// </summary>
    [AddComponentMenu("")]
    public class FragmentRuntime : MonoBehaviour
    {
        internal const float MaxGroundReboundSpeed = 0.2f;
        internal const float MaxGroundSettlingSpeed = 1.5f;
        internal const float GroundTangentialDampingPerSecond = 3f;
        internal const float GroundAngularDampingPerSecond = 2f;
        /// <summary>Share of its own height one fixed step must be able to carry a piece
        /// through before the anti-tunnelling guard may touch it. See
        /// <see cref="GuardStaticGroundCrossing"/>.</summary>
        internal const float MinimumTunnelDropShare = 0.25f;
        /// <summary>How many fixed steps a ground contact stays valid for. Collision
        /// callbacks are raised after the step that produced them, so the freshest stamp a
        /// FixedUpdate can read is already one step old.</summary>
        internal const float SustainedGroundContactSteps = 2.5f;
        [HideInInspector] public DestructibleRoot Root;
        [HideInInspector] public int FragmentIndex;
        /// <summary>Baked generation of this piece: 0 = primary fragment,
        /// 1 = level-two (secondary) piece. Set during Bake.</summary>
        [HideInInspector] public int Level;
        /// <summary>Dominant visible material colour sampled during Bake. Procedural hit
        /// particles use it so wood, stone, roof and painted pieces match their surface.</summary>
        [HideInInspector] public Color EffectColor = Color.white;
        [HideInInspector] public bool HasEffectColor;

        /// <summary>
        /// Fixed time of the last confirmed external static ground contact, or negative
        /// infinity while the piece has never landed. The anti-tunnelling guard reads it
        /// through <see cref="HasSustainedGroundContact"/>.
        /// </summary>
        internal float LastStaticGroundContactTime { get; private set; } =
            float.NegativeInfinity;

        /// <summary>True while PhysX is already holding this piece against static ground.</summary>
        internal bool HasSustainedGroundContact => IsSustainedGroundContact(
            LastStaticGroundContactTime, Time.fixedTime, Time.fixedDeltaTime);

        internal static bool IsSustainedGroundContact(float lastContactTime,
            float fixedTime, float fixedDeltaTime) =>
            fixedTime - lastContactTime <=
            Mathf.Max(0.0001f, fixedDeltaTime) * SustainedGroundContactSteps;

        Rigidbody m_body;
        Renderer m_mainRenderer;
        Renderer[] m_scatterRenderers;
        float m_highestWorldY;
        bool m_groundDustPlayed;
        bool m_scatterVisible;
        Vector3 m_scatterStartPosition;
        Quaternion m_scatterStartRotation;
        float m_scatterRevealDistance;

        void OnEnable()
        {
            EnsureComponentCache();
            m_highestWorldY = transform.position.y;
            m_groundDustPlayed = false;
            LastStaticGroundContactTime = float.NegativeInfinity;
            PrepareInteriorScatterVisibility();
        }

        /// <summary>
        /// Resolved lazily rather than only in OnEnable, so nothing here depends on having
        /// been enabled first. A piece added or re-parented by tooling outside play mode
        /// otherwise sits with null caches and silently skips every check below.
        /// </summary>
        void EnsureComponentCache()
        {
            if (m_body == null) m_body = GetComponent<Rigidbody>();
            if (m_mainRenderer == null) m_mainRenderer = GetComponent<Renderer>();
        }

        void Update()
        {
            EnsureComponentCache();
            if (m_body != null && !m_body.isKinematic)
            {
                m_highestWorldY = Mathf.Max(m_highestWorldY, transform.position.y);
                TryRevealInteriorScatter();
            }
        }

        void PrepareInteriorScatterVisibility()
        {
            if (!Application.isPlaying) return;
            Renderer[] children = GetComponentsInChildren<Renderer>(true);
            int count = 0;
            for (int i = 0; i < children.Length; i++)
                if (children[i] != null && children[i] != m_mainRenderer &&
                    children[i].gameObject.name == "Interior Scatter")
                    count++;

            m_scatterRenderers = new Renderer[count];
            int write = 0;
            for (int i = 0; i < children.Length; i++)
            {
                Renderer child = children[i];
                if (child == null || child == m_mainRenderer ||
                    child.gameObject.name != "Interior Scatter")
                    continue;
                MatchInteriorScatterMaterial(m_mainRenderer, child);
                m_scatterRenderers[write++] = child;
                child.enabled = false;
            }

            m_scatterVisible = count == 0;
            m_scatterStartPosition = transform.position;
            m_scatterStartRotation = transform.rotation;
            Vector3 size = m_mainRenderer != null
                ? m_mainRenderer.bounds.size : Vector3.one;
            float smallest = Mathf.Min(size.x, Mathf.Min(size.y, size.z));
            m_scatterRevealDistance = Mathf.Max(0.004f, smallest * 0.08f);
        }

        void TryRevealInteriorScatter()
        {
            if (m_scatterVisible || m_scatterRenderers == null ||
                m_scatterRenderers.Length == 0)
                return;

            float moved = Vector3.Distance(transform.position, m_scatterStartPosition);
            float rotated = Quaternion.Angle(transform.rotation, m_scatterStartRotation);
            if (moved < m_scatterRevealDistance && rotated < 2f) return;

            for (int i = 0; i < m_scatterRenderers.Length; i++)
                if (m_scatterRenderers[i] != null)
                {
                    // A user or pipeline converter may have replaced the fragment's
                    // material since OnEnable. Bind again at the moment scatter appears.
                    MatchInteriorScatterMaterial(
                        m_mainRenderer, m_scatterRenderers[i]);
                    m_scatterRenderers[i].enabled = true;
                }
            m_scatterVisible = true;
        }

        internal static void MatchInteriorScatterMaterial(
            Renderer mainRenderer, Renderer scatterRenderer)
        {
            if (mainRenderer == null || scatterRenderer == null) return;
            Material cutMaterial = FragmentMaterialUtility.GetCutSurfaceMaterial(
                mainRenderer.sharedMaterials);
            if (cutMaterial != null)
                scatterRenderer.sharedMaterial = cutMaterial;
        }

        /// <summary>
        /// Removes only the artificial separating part of a ground response. Convex fracture
        /// hulls land on changing facets, so PhysX can turn spin/depenetration into a large
        /// upward velocity even when their material has zero bounciness. Initial tangential
        /// motion is preserved; only a later sustained contact applies the low-speed brake.
        /// </summary>
        internal static Vector3 StabilizeStaticGroundVelocity(Vector3 velocity,
            Vector3 normal, float maxReboundSpeed = MaxGroundReboundSpeed)
        {
            if (normal.sqrMagnitude <= 0.000001f) return velocity;
            normal.Normalize();
            if (Vector3.Dot(normal, Vector3.up) < 0.55f) return velocity;

            float separatingSpeed = Vector3.Dot(velocity, normal);
            float allowed = Mathf.Max(0f, maxReboundSpeed);
            return separatingSpeed > allowed
                ? velocity - normal * (separatingSpeed - allowed)
                : velocity;
        }

        /// <summary>
        /// Adds a small low-speed settling brake after PhysX has already handled a sustained
        /// ground contact. Convex fracture hulls continually change their contact facet while
        /// rolling, which can keep a tiny tangential velocity alive indefinitely even with a
        /// high-friction material. Fast deliberate slides are left entirely to PhysX.
        /// </summary>
        internal static Vector3 DampStaticGroundTangentialVelocity(Vector3 velocity,
            Vector3 normal, float fixedDeltaTime,
            float maxSettlingSpeed = MaxGroundSettlingSpeed,
            float dampingPerSecond = GroundTangentialDampingPerSecond)
        {
            if (normal.sqrMagnitude <= 0.000001f) return velocity;
            normal.Normalize();
            if (Vector3.Dot(normal, Vector3.up) < 0.55f) return velocity;

            float normalSpeed = Vector3.Dot(velocity, normal);
            Vector3 tangential = velocity - normal * normalSpeed;
            float tangentialSpeed = tangential.magnitude;
            if (tangentialSpeed <= 0.000001f ||
                tangentialSpeed > Mathf.Max(0f, maxSettlingSpeed))
                return velocity;

            float damping = Mathf.Max(0f, dampingPerSecond);
            float factor = Mathf.Clamp01(1f - damping *
                Mathf.Max(0f, fixedDeltaTime));
            tangential *= factor;
            if (tangential.sqrMagnitude < 0.000625f)
                tangential = Vector3.zero;
            return normal * normalSpeed + tangential;
        }

        internal static Vector3 DampStaticGroundAngularVelocity(Vector3 angularVelocity,
            float tangentialSpeed, float fixedDeltaTime,
            float maxSettlingSpeed = MaxGroundSettlingSpeed,
            float dampingPerSecond = GroundAngularDampingPerSecond)
        {
            if (tangentialSpeed > Mathf.Max(0f, maxSettlingSpeed))
                return angularVelocity;
            float factor = Mathf.Clamp01(1f - Mathf.Max(0f, dampingPerSecond) *
                Mathf.Max(0f, fixedDeltaTime));
            angularVelocity *= factor;
            return angularVelocity.sqrMagnitude < 0.01f
                ? Vector3.zero : angularVelocity;
        }

        /// <summary>
        /// Classifies the other side of a contact without depending on which fragment type
        /// raised it. Only external static or kinematic geometry with an upward-facing normal
        /// is ground. Dynamic bodies, another piece of the same destructible and ceilings are
        /// deliberately excluded.
        /// </summary>
        internal static bool IsExternalStaticUpwardGround(Rigidbody otherBody,
            DestructibleRoot otherRoot, DestructibleRoot ownRoot, Vector3 normal,
            float minimumUpwardDot = 0.55f)
        {
            if (otherBody != null && !otherBody.isKinematic) return false;
            if (ownRoot != null && otherRoot == ownRoot) return false;
            if (normal.sqrMagnitude <= 0.000001f) return false;
            return Vector3.Dot(normal.normalized, Vector3.up) >=
                   Mathf.Clamp01(minimumUpwardDot);
        }

        /// <summary>
        /// Finds the most upward-facing valid contact without allocating Collision.contacts.
        /// A convex shard often lands on an edge and PhysX may report a side contact first;
        /// selecting contact zero made rebound suppression intermittent.
        /// </summary>
        internal static bool TryGetExternalStaticGroundContact(Collision collision,
            DestructibleRoot ownRoot, out ContactPoint groundContact)
        {
            groundContact = default(ContactPoint);
            if (collision == null || collision.collider == null ||
                collision.contactCount <= 0)
                return false;

            Rigidbody otherBody = collision.rigidbody;
            DestructibleRoot otherRoot =
                collision.collider.GetComponentInParent<DestructibleRoot>();

            float bestUpwardDot = 0.55f;
            bool found = false;
            for (int i = 0; i < collision.contactCount; i++)
            {
                ContactPoint candidate = collision.GetContact(i);
                Vector3 normal = candidate.normal;
                if (!IsExternalStaticUpwardGround(otherBody, otherRoot, ownRoot,
                        normal, 0.55f))
                    continue;
                float upwardDot = Vector3.Dot(normal.normalized, Vector3.up);
                if (upwardDot < bestUpwardDot) continue;
                bestUpwardDot = upwardDot;
                groundContact = candidate;
                found = true;
            }
            return found;
        }

        /// <summary>
        /// Sweep CCD does not cover convex MeshColliders in the Unity versions supported by
        /// this package. It is still the correct inexpensive mode for primitive fallbacks, but
        /// a mesh-only body needs the small vertical safety query driven by its owner.
        /// </summary>
        internal static bool RequiresMeshGroundGuard(Rigidbody body)
        {
            if (body == null || body.isKinematic || !body.detectCollisions)
                return false;

            bool convexMesh = false;
            Collider[] colliders = body.GetComponents<Collider>();
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider collider = colliders[i];
                if (collider == null || !collider.enabled || collider.isTrigger) continue;
                if (collider is BoxCollider || collider is SphereCollider ||
                    collider is CapsuleCollider)
                    return false;
                if (collider is MeshCollider mesh && mesh.convex)
                    convexMesh = true;
            }
            return convexMesh;
        }

        /// <summary>
        /// Whether this profile's debris is watched for crossing a zero-thickness floor at
        /// all. Deliberately scoped to Dynamic intact bodies: the per-step query is a cost
        /// every other setup would pay for a case it rarely meets. Debris from Static and
        /// Release When Unsupported objects is therefore left entirely to PhysX, which can
        /// let a piece travelling more than its own height in one fixed step pass through a
        /// floor modelled as a Plane.
        /// </summary>
        internal static bool UsesMeshGroundGuard(DestructionProfile profile) =>
            profile != null && profile.IntactBody == IntactBodyMode.Dynamic;

        /// <summary>Maximum safe downward speed for a remaining vertical clearance.</summary>
        internal static float ResolveSafeDownwardSpeed(float clearance,
            float fixedDeltaTime, float skin = 0.002f)
        {
            float dt = Mathf.Max(0.0001f, fixedDeltaTime);
            return Mathf.Max(0f, (Mathf.Max(0f, clearance) -
                Mathf.Max(0f, skin)) / dt);
        }

        /// <summary>
        /// Allocation-free mesh-debris guard for zero-thickness static floors. It runs only
        /// on awake, descending convex-mesh bodies whose next fixed step can reach the queried
        /// surface. No proxy collider is added, so holes and concave openings stay shootable.
        ///
        /// Both corrections below rewrite state the solver owns — the pose and the vertical
        /// velocity — so neither may reach a piece PhysX already holds in contact. Friction
        /// is accumulated per contact patch across steps; moving a resting or sliding piece
        /// every step drops that patch, the tangential impulse never builds up, and the piece
        /// keeps its full horizontal speed until linear drag alone removes it. That reads in
        /// the scene as debris skating across the floor and never stopping.
        ///
        /// Tunnelling is a fast-fall problem, so the guard engages only while one step could
        /// genuinely carry the piece through, and <paramref name="restingOnGround"/> takes it
        /// out of the way entirely once its owner has seen a sustained ground contact.
        /// </summary>
        internal static void GuardStaticGroundCrossing(Rigidbody body, Bounds bounds,
            Transform ownRoot, RaycastHit[] hits, int layerMask, float fixedDeltaTime,
            bool meshGuardConfirmed = false, bool restingOnGround = false)
        {
            if (body == null || body.isKinematic || !body.detectCollisions ||
                body.IsSleeping() || restingOnGround ||
                hits == null || hits.Length == 0 ||
                (!meshGuardConfirmed && !RequiresMeshGroundGuard(body)) ||
                bounds.size.sqrMagnitude <= 0.000001f)
                return;

#if UNITY_6000_0_OR_NEWER
            Vector3 velocity = body.linearVelocity;
#else
            Vector3 velocity = body.velocity;
#endif
            if (velocity.y >= -0.01f) return;

            float dt = Mathf.Max(0.0001f, fixedDeltaTime);
            float predictedDrop = -velocity.y * dt +
                                  Mathf.Max(0f, -Physics.gravity.y) * dt * dt * 0.5f;
            // Passing through a surface with no thickness takes a step that carries the piece
            // beyond its own full height. Requiring a quarter of that keeps a wide margin
            // while excluding every piece merely settling, rolling or grinding along the
            // floor at contact speed — those are the ones the corrections below ruin.
            if (predictedDrop < bounds.size.y * MinimumTunnelDropShare) return;
            const float skin = 0.002f;
            float rayLength = bounds.extents.y + predictedDrop + skin * 2f;
            int count = Physics.RaycastNonAlloc(bounds.center + Vector3.up * skin,
                Vector3.down, hits, rayLength, layerMask,
                QueryTriggerInteraction.Ignore);

            float bestClearance = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                RaycastHit candidate = hits[i];
                Collider collider = candidate.collider;
                hits[i] = default(RaycastHit);
                if (collider == null || !collider.enabled || collider.isTrigger ||
                    collider.attachedRigidbody == body ||
                    (ownRoot != null && (collider.transform == ownRoot ||
                                         collider.transform.IsChildOf(ownRoot))))
                    continue;
                Rigidbody supportBody = collider.attachedRigidbody;
                if (supportBody != null && !supportBody.isKinematic) continue;
                if (Vector3.Dot(candidate.normal, Vector3.up) < 0.55f) continue;

                float clearance = bounds.min.y - candidate.point.y;
                if (clearance < bestClearance) bestClearance = clearance;
            }

            if (float.IsPositiveInfinity(bestClearance) ||
                bestClearance > predictedDrop + skin)
                return;

            // A previous discrete step can leave a very small overlap. Recover only that
            // overlap before the next simulation; larger corrections are left to PhysX so a
            // fragment cannot teleport out of authored geometry. An overlap inside the
            // contact offset is not a missed step at all — it is where PhysX parks a body it
            // is resolving — and lifting the pose out of it costs the piece its friction.
            float restingOverlap = Mathf.Max(skin, Physics.defaultContactOffset);
            if (bestClearance < -restingOverlap)
            {
                float correction = Mathf.Min(-bestClearance + skin,
                    Mathf.Max(0.01f, bounds.size.y * 0.25f));
                body.position += Vector3.up * correction;
                bestClearance += correction;
            }

            float safeSpeed = ResolveSafeDownwardSpeed(bestClearance, dt, skin);
            if (-velocity.y <= safeSpeed) return;
            velocity.y = -safeSpeed;
#if UNITY_6000_0_OR_NEWER
            body.linearVelocity = velocity;
#else
            body.velocity = velocity;
#endif
        }

        void OnCollisionEnter(Collision collision)
        {
            if (Root == null) return;

            int resolvedFragmentIndex = Root.ResolvePrimaryFragmentIndex(this);
            if (resolvedFragmentIndex < 0)
            {
                // Never reinterpret an unknown component identity as a radius hit. That
                // could activate another wall even though the reporting collider does not
                // belong to any serialized FragmentRecord on this root.
                return;
            }
            if (FragmentIndex != resolvedFragmentIndex)
                FragmentIndex = resolvedFragmentIndex;

            EnsureComponentCache();

            ContactPoint groundContact = default(ContactPoint);
            bool staticGround = m_body != null && !m_body.isKinematic &&
                TryGetExternalStaticGroundContact(collision, Root,
                    out groundContact);
            Vector3 groundNormal = staticGround
                ? groundContact.normal : Vector3.up;
            // This must run before the one-shot landing-effect return below. Repeated contacts
            // still need stabilization even though dust is allowed only on the first landing.
            if (staticGround && Vector3.Dot(groundNormal.normalized, Vector3.up) >= 0.55f)
            {
#if UNITY_6000_0_OR_NEWER
                m_body.linearVelocity = StabilizeStaticGroundVelocity(
                    m_body.linearVelocity, groundNormal);
#else
                m_body.velocity = StabilizeStaticGroundVelocity(
                    m_body.velocity, groundNormal);
#endif
            }

            // After the intact object has fractured, projectiles collide with these
            // fragment objects instead of DestructibleCollisionRelay. Forward primary
            // impacts so further shots can wake the remaining kinematic shell locally.
            if (Level == 0)
                Root.ReportPrimaryFragmentCollision(resolvedFragmentIndex, collision);

            // A loose fragment slamming into a DIFFERENT destructible is a Rigidbody
            // impact on that object: flying concrete must be able to shatter a window.
            // This fragment always receives the event on its own dynamic body, so
            // forwarding from here works no matter how the struck object's intact
            // receiver is hosted; its damage cooldown absorbs any double report. The
            // speed pre-filter keeps settling piles from paying a component search
            // per grinding contact.
            if (Root.IsFragmentLoose(resolvedFragmentIndex) &&
                collision.collider != null &&
                collision.relativeVelocity.sqrMagnitude > 1f)
            {
                var struckRoot =
                    collision.collider.GetComponentInParent<DestructibleRoot>();
                if (struckRoot != null && struckRoot != Root)
                    struckRoot.ReportExternalDebrisImpact(collision, gameObject, m_body);
            }

            // Ground dust belongs only to the first real landing; ordinary side hits,
            // collisions with the same destructible and a settling pile never emit it.
            if (m_groundDustPlayed || !staticGround) return;

            Vector3 point = groundContact.point;
            Vector3 normal = groundContact.normal;

#if UNITY_6000_0_OR_NEWER
            float downwardSpeed = -m_body.linearVelocity.y;
#else
            float downwardSpeed = -m_body.velocity.y;
#endif
            DestructionProfile profile = Root.Profile;
            float minSpeed = profile != null
                ? Mathf.Max(0f, profile.FragmentImpactMinSpeed) : 1.5f;
            float minDrop = profile != null
                ? Mathf.Max(0f, profile.FragmentImpactMinDropHeight) : 0.75f;
            float fallenDistance = m_highestWorldY - point.y;
            if (downwardSpeed < minSpeed || fallenDistance < minDrop) return;
            m_groundDustPlayed = true;

            float size = 1.0f;
            Renderer rend = GetComponent<Renderer>();
            if (rend != null)
            {
                Vector3 sz = rend.bounds.size;
                size = Mathf.Max(0.3f, (sz.x + sz.y + sz.z) / 3f);
            }

            Root.ReportFragmentImpact(resolvedFragmentIndex, point, downwardSpeed, size);

            if (Level == 0 && Root.Profile != null &&
                Root.Profile.RuntimeFragmentSplitting)
            {
                Root.TrySplitRuntimeFragmentOnGroundLanding(
                    resolvedFragmentIndex, point, normal, downwardSpeed);
                return;
            }

            // Pre-baked secondary breakup is intentionally tied to this same strict
            // landing test as dust.  A loose chunk therefore cannot crumble from a
            // side bump or from settling in a pile; it may only swap to its already
            // baked children after a real fall onto static ground.  Dynamic runtime
            // slicing remains a separate, explicit-hit-only path.
            if (Level == 0)
                Root.TryBreakSecondaryOnGroundLanding(
                    resolvedFragmentIndex, point, normal);
        }

        void OnCollisionStay(Collision collision)
        {
            EnsureComponentCache();
            if (m_body == null || m_body.isKinematic || m_body.IsSleeping()) return;
            if (!TryGetExternalStaticGroundContact(collision, Root,
                    out ContactPoint groundContact))
                return;

            // Stamped before the owner check: the anti-tunnelling guard must leave a resting
            // piece alone whether or not it still belongs to a live destructible.
            LastStaticGroundContactTime = Time.fixedTime;
            if (Root == null) return;

#if UNITY_6000_0_OR_NEWER
            Vector3 velocity = StabilizeStaticGroundVelocity(
                m_body.linearVelocity, groundContact.normal);
            m_body.linearVelocity = DampStaticGroundTangentialVelocity(
                velocity, groundContact.normal, Time.fixedDeltaTime);
#else
            Vector3 velocity = StabilizeStaticGroundVelocity(
                m_body.velocity, groundContact.normal);
            m_body.velocity = DampStaticGroundTangentialVelocity(
                velocity, groundContact.normal, Time.fixedDeltaTime);
#endif
            float tangentialSpeed = Vector3.ProjectOnPlane(
                velocity, groundContact.normal).magnitude;
            m_body.angularVelocity = DampStaticGroundAngularVelocity(
                m_body.angularVelocity, tangentialSpeed, Time.fixedDeltaTime);
        }

    }
}
