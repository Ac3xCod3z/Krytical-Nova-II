using System;
using System.Collections.Generic;
using UnityEngine;
using RipVertices.QuickDestruction.Runtime;

namespace RipVertices.QuickDestruction
{
    /// <summary>
    /// Optional bounded runtime re-fracture. It processes explicit ApplyDamage hits and
    /// intentional impacts from an external simulated Rigidbody. Static-world contacts and
    /// collisions between pieces of the same destructible are ignored.
    /// Generated meshes live until Reset so reset remains deterministic.
    /// </summary>
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(DestructibleRoot))]
    public sealed class RuntimeFragmentSplitter : MonoBehaviour
    {
        const int MaxAutomaticGroundSplitsPerCollapse = 8;

        struct Candidate
        {
            public GameObject GameObject;
            public MeshFilter Filter;
            public Renderer Renderer;
            public Rigidbody Body;
            public RuntimeSplitPiece RuntimePiece;
            public FragmentRuntime BakedPiece;
            public int Depth;
            public Color EffectColor;
            public bool HasEffectColor;
        }

        DestructibleRoot m_root;
        readonly List<RuntimeSplitPiece> m_runtimePieces = new List<RuntimeSplitPiece>();
        readonly List<GameObject> m_disabledOriginals = new List<GameObject>();
        readonly RaycastHit[] m_groundProbeHits = new RaycastHit[8];
        readonly RaycastHit[] m_groundGuardHits = new RaycastHit[8];
        Transform m_runtimeRoot;
        int m_splitSerial;
        int m_groundSplitsRemaining = MaxAutomaticGroundSplitsPerCollapse;

        struct PendingGroundSplit
        {
            public Candidate Candidate;
            public DamageContext Context;
        }

        readonly Queue<PendingGroundSplit> m_groundSplitQueue =
            new Queue<PendingGroundSplit>();
        readonly HashSet<GameObject> m_queuedGroundObjects =
            new HashSet<GameObject>();

        int m_splitsThisFrame;

        void Awake() => ResolveRoot();

        void OnEnable()
        {
            ResolveRoot();
            if (m_root == null) return;
            m_root.WasReset += OnReset;
        }

        void OnDisable()
        {
            if (m_root != null)
            {
                m_root.WasReset -= OnReset;
            }
        }

        void ResolveRoot()
        {
            if (m_root == null) m_root = GetComponent<DestructibleRoot>();
        }

        void Update()
        {
            m_splitsThisFrame = 0;
            // One loop for every runtime piece instead of one MonoBehaviour Update each.
            // Backwards, because a piece that finishes disappearing removes itself.
            float deltaTime = Time.deltaTime;
            for (int i = m_runtimePieces.Count - 1; i >= 0; i--)
            {
                RuntimeSplitPiece piece = m_runtimePieces[i];
                if (piece == null || !piece.gameObject.activeInHierarchy) continue;
                piece.Tick(deltaTime);
            }

            // Building collapses can produce dozens of valid landings in one physics
            // step. Process at most one automatic breakup per rendered frame.
            if (m_groundSplitQueue.Count == 0) return;
            PendingGroundSplit request = m_groundSplitQueue.Dequeue();
            if (request.Candidate.GameObject != null)
                m_queuedGroundObjects.Remove(request.Candidate.GameObject);
            if (request.Candidate.GameObject == null ||
                !request.Candidate.GameObject.activeInHierarchy)
                return;

            DestructionProfile profile = m_root != null ? m_root.Profile : null;
            if (CanRuntimeSplit(profile))
            {
                // The collapse allowance counts completed splits, not requests that
                // became invalid or a cut the bounded slicer safely rejected.
                if (ProcessCandidateHit(request.Candidate, request.Context, profile))
                    m_groundSplitsRemaining = Mathf.Max(0,
                        m_groundSplitsRemaining - 1);
            }
        }

        void FixedUpdate()
        {
            if (m_runtimePieces.Count == 0) return;
            DestructionProfile profile = m_root != null ? m_root.Profile : null;
            if (!FragmentRuntime.UsesMeshGroundGuard(profile)) return;
            int layers = profile != null ? profile.CollisionLayers : Physics.AllLayers;
            Transform ownRoot = m_root != null ? m_root.transform : transform;
            for (int i = 0; i < m_runtimePieces.Count; i++)
            {
                RuntimeSplitPiece piece = m_runtimePieces[i];
                if (piece == null || !piece.gameObject.activeInHierarchy ||
                    piece.IsDisappearing || piece.CachedBody == null ||
                    piece.CachedRenderer == null || !piece.NeedsMeshGroundGuard)
                    continue;
                FragmentRuntime.GuardStaticGroundCrossing(piece.CachedBody,
                    piece.CachedRenderer.bounds, ownRoot, m_groundGuardHits,
                    layers, Time.fixedDeltaTime, true,
                    piece.HasSustainedGroundContact);
            }
        }

        internal bool ProcessHit(DamageContext context)
        {
            DestructionProfile profile = m_root != null ? m_root.Profile : null;
            if (!CanRuntimeSplit(profile) ||
                m_root.State < DestructibleState.Fractured) return false;

            if (!TryFindCandidate(context.Point, context.Radius, profile,
                    out Candidate candidate))
                return false;
            return ProcessCandidateHit(candidate, context, profile);
        }

        bool ProcessCandidateHit(Candidate candidate, DamageContext context,
            DestructionProfile profile)
        {
            if (!profile.AcceptsHitDamage(context.Amount)) return false;
            if (candidate.GameObject == null || candidate.Depth >=
                Mathf.Clamp(profile.RuntimeSplitMaxDepth, 1, 4))
                return false;
            if (LocalBoundsVolume(candidate.Filter.sharedMesh.bounds) <
                EffectiveMinimumSplitVolume(profile)) return false;

            int limit = profile != null ? profile.MaxSplitsPerFrame : 1;
            if (m_splitsThisFrame >= limit)
            {
                if (!m_queuedGroundObjects.Contains(candidate.GameObject))
                {
                    m_queuedGroundObjects.Add(candidate.GameObject);
                    m_groundSplitQueue.Enqueue(new PendingGroundSplit
                    {
                        Candidate = candidate,
                        Context = context
                    });
                }
                return true;
            }

            bool success = TrySplit(candidate, context, profile);
            if (success)
            {
                m_splitsThisFrame++;
            }
            return success;
        }

        /// <summary>Starts the disappear shrink on every live runtime piece. Returns the count.</summary>
        internal int DespawnAllPieces()
        {
            int despawned = 0;
            for (int i = 0; i < m_runtimePieces.Count; i++)
            {
                RuntimeSplitPiece piece = m_runtimePieces[i];
                if (piece == null || piece.IsDisappearing) continue;
                piece.StartDisappearing();
                despawned++;
            }
            return despawned;
        }

        internal bool ProcessCollision(GameObject hitObject, Collision collision)
        {
            DestructionProfile profile = m_root != null ? m_root.Profile : null;
            if (!CanRuntimeSplit(profile) ||
                m_root.State < DestructibleState.Fractured || hitObject == null ||
                collision == null)
                return false;
            if (!IsExternalDynamicCollision(collision)) return false;

            float velocity = collision.relativeVelocity.magnitude;
            if (velocity < profile.MinCollisionVelocity) return false;

            // The shared energy model, same as the intact receiver and the fragment
            // receiver use. This was a third private copy of the old impulse formula: it
            // fed raw impulse into a curve whose axis is now kilojoules, sized a mass
            // threshold from a strength value, and then — decisively — raised the result
            // to the split threshold no matter what. Every qualifying contact therefore
            // cut the piece apart, however gently it arrived.
            float damage = m_root.CollisionImpactDamage(collision, out _);
            if (!profile.AcceptsHitDamage(damage)) return false;

            bool debrisSource = collision.rigidbody != null &&
                                !collision.rigidbody.isKinematic &&
                                (collision.rigidbody.TryGetComponent(out FragmentRuntime _) ||
                                 collision.rigidbody.TryGetComponent(out RuntimeSplitPiece _));
            if (debrisSource)
            {
                // Rubble knocking against rubble is not a cutting blow. Held to the same
                // share of the material's strength as every other debris impact.
                if (damage < profile.Health *
                    Mathf.Max(0f, profile.DebrisImpactSignificance))
                    return false;
            }
            else
            {
                // An aimed projectile is an explicit request to split the exact piece it
                // hit, so a light but fast bullet still counts. Collision Damage governs
                // intact and shell health, not this opt-in runtime feature.
                damage = Mathf.Max(damage, profile.RuntimeSplitStrength);
            }

            Vector3 point = hitObject.transform.position;
            Vector3 normal = Vector3.up;
            if (collision.contactCount > 0)
            {
                ContactPoint contact = collision.GetContact(0);
                point = contact.point;
                normal = contact.normal;
            }

            if (!TryBuildCandidate(hitObject, out Candidate candidate)) return false;
            DestructionImpactSource impactSource = collision.gameObject != null
                ? collision.gameObject.GetComponentInParent<DestructionImpactSource>()
                : null;
            float impactRadius = impactSource != null &&
                                 impactSource.OverrideImpactRadius
                ? Mathf.Max(0.01f, impactSource.ImpactRadius)
                : Mathf.Max(0.05f, profile.PostFractureImpactRadius);
            float directionalForce = impactSource != null
                ? impactSource.DirectionalForce : -1f;
            var context = new DamageContext(damage, point, normal,
                impactRadius, 0,
                collision.gameObject.GetHashCode(), collision.impulse.magnitude,
                IncomingDirection(collision), directionalForce);
            m_root.ReportRuntimeFragmentHit(context);
            return ProcessCandidateHit(candidate, context, profile);
        }

        internal bool ProcessGroundLanding(GameObject hitObject, Vector3 point,
            Vector3 normal, float energy)
        {
            DestructionProfile profile = m_root != null ? m_root.Profile : null;
            if (!CanRuntimeSplit(profile) ||
                !profile.AcceptsHitDamage(Mathf.Max(0.01f, energy)) ||
                m_root.State < DestructibleState.Fractured || hitObject == null ||
                !TryBuildCandidate(hitObject, out Candidate candidate))
                return false;

            if (candidate.Depth >= Mathf.Clamp(profile.RuntimeSplitMaxDepth, 1, 4) ||
                LocalBoundsVolume(candidate.Filter.sharedMesh.bounds) <
                EffectiveMinimumSplitVolume(profile))
                return false;

            // Keep accepted plus pending work within the eight-split collapse cap.
            // A failed queued cut frees its reservation automatically.
            if (m_groundSplitsRemaining <= m_groundSplitQueue.Count ||
                m_queuedGroundObjects.Contains(candidate.GameObject))
                return false;

            var context = new DamageContext(Mathf.Max(0.01f, energy),
                point, normal, Mathf.Max(0.05f, profile.PostFractureImpactRadius),
                0, hitObject.GetHashCode(), energy);
            m_queuedGroundObjects.Add(candidate.GameObject);
            m_groundSplitQueue.Enqueue(new PendingGroundSplit
            {
                Candidate = candidate,
                Context = context
            });
            return true;
        }

        bool IsExternalDynamicCollision(Collision collision)
        {
            // Runtime re-fracture is deliberately driven by projectiles/vehicles and
            // other simulated objects. Ignoring static ground and this destructible's
            // own children prevents rubble piles from recursively slicing themselves.
            if (collision == null || collision.rigidbody == null ||
                collision.rigidbody.isKinematic)
                return false;
            Transform other = collision.collider != null
                ? collision.collider.transform : null;
            if (other == null) return false;
            if (m_runtimeRoot != null && other.IsChildOf(m_runtimeRoot)) return false;
            DestructibleRoot otherRoot = other.GetComponentInParent<DestructibleRoot>();
            return otherRoot != m_root;
        }

        bool TryBuildCandidate(GameObject go, out Candidate candidate)
        {
            candidate = default;
            if (go == null) return false;
            MeshFilter filter = go.GetComponent<MeshFilter>();
            Renderer renderer = go.GetComponent<Renderer>();
            if (filter == null || filter.sharedMesh == null || renderer == null ||
                !renderer.enabled) return false;
            RuntimeSplitPiece runtimePiece = go.GetComponent<RuntimeSplitPiece>();
            if (runtimePiece != null && runtimePiece.IsDisappearing) return false;
            FragmentRuntime bakedPiece = runtimePiece == null
                ? go.GetComponent<FragmentRuntime>() : null;
            candidate = new Candidate
            {
                GameObject = go,
                Filter = filter,
                Renderer = renderer,
                Body = go.GetComponent<Rigidbody>(),
                RuntimePiece = runtimePiece,
                BakedPiece = bakedPiece,
                Depth = runtimePiece != null ? runtimePiece.Depth : 0,
                EffectColor = runtimePiece != null ? runtimePiece.EffectColor :
                    bakedPiece != null ? bakedPiece.EffectColor : Color.white,
                HasEffectColor = runtimePiece != null ? runtimePiece.HasEffectColor :
                    bakedPiece != null && bakedPiece.HasEffectColor
            };
            return true;
        }

        /// <summary>
        /// Shoves runtime pieces inside the hit radius. A piece that has spent its split
        /// iterations is still ordinary debris and must react to being shot.
        ///
        /// <paramref name="radial"/> throws each piece away from the origin along its own
        /// outward vector, which is what a blast does; a weapon hit keeps the single supplied
        /// travel direction.
        /// </summary>
        internal void PushLoosePieces(Vector3 point, float radius,
            Vector3 direction, float impulse, Vector3 impactDirection,
            float directionalImpulse, bool radial = false)
        {
            float radiusSqr = radius * radius;
            for (int i = 0; i < m_runtimePieces.Count; i++)
            {
                RuntimeSplitPiece piece = m_runtimePieces[i];
                if (piece == null || !piece.gameObject.activeInHierarchy ||
                    piece.IsDisappearing) continue;
                Renderer renderer = piece.CachedRenderer;
                if (renderer == null) continue;
                Vector3 closest = renderer.bounds.ClosestPoint(point);
                if ((closest - point).sqrMagnitude > radiusSqr) continue;
                Rigidbody body = piece.CachedBody;
                if (body == null || body.isKinematic) continue;

                Vector3 pushDirection = direction;
                Vector3 travelReference = impactDirection;
                if (radial)
                {
                    Vector3 outward = body.worldCenterOfMass - point;
                    if (outward.sqrMagnitude < 0.000001f)
                        outward = Vector3.up;
                    // Runtime pieces are the smallest debris and lie flat on the floor, so an
                    // uncorrected outward vector from an aimed blast points down through it.
                    pushDirection = DestructibleRoot.ResolveGroundSafeEjectDirection(
                        outward, IsRestingOnStaticGround(renderer.bounds));
                    travelReference = pushDirection;
                }
                body.WakeUp();
                // Runtime pieces are the lightest debris of all, so an unclamped impulse
                // (a speed once divided by mass) launches them hardest of anything.
                DestructionProfile pushProfile = m_root != null ? m_root.Profile : null;
                float applied = pushProfile != null
                    ? ClampImpulseToLaunchSpeed(impulse, body.mass,
                        pushProfile.MaxFragmentLaunchSpeed)
                    : impulse;
                if (applied > 0f)
                    body.AddForceAtPosition(pushDirection * applied, closest,
                        ForceMode.Impulse);

                if (directionalImpulse <= 0f ||
                    travelReference.sqrMagnitude <= 0.000001f)
                    continue;
                Vector3 travelDirection = travelReference.normalized;
#if UNITY_6000_0_OR_NEWER
                Vector3 velocity = body.linearVelocity;
#else
                Vector3 velocity = body.velocity;
#endif
                float maxForwardSpeed = pushProfile != null
                    ? Mathf.Max(0.01f, pushProfile.MaxFragmentLaunchSpeed)
                    : float.PositiveInfinity;
                float remainingForwardSpeed = Mathf.Max(0f,
                    maxForwardSpeed - Mathf.Max(0f,
                        Vector3.Dot(velocity, travelDirection)));
                float directionalApplied = Mathf.Min(directionalImpulse,
                    remainingForwardSpeed * body.mass);
                if (directionalApplied > 0f)
                    body.AddForceAtPosition(travelDirection * directionalApplied,
                        closest, ForceMode.Impulse);
            }
        }

        /// <summary>
        /// Trims an impulse so it cannot exceed the profile's launch-speed cap on this body.
        /// An impulse divided by mass IS a speed, and runtime pieces are by far the lightest
        /// bodies the system produces — a piece cut twice out of a low-density material can be
        /// a few grams, where an impulse authored for whole fragments becomes tens of metres
        /// per second. Clamping the impulse rather than correcting the velocity afterwards
        /// keeps it a single multiply and leaves the direction untouched.
        /// </summary>
        internal static float ClampImpulseToLaunchSpeed(float impulse, float mass,
            float maxLaunchSpeed)
        {
            if (impulse <= 0f) return 0f;
            float maxSpeed = Mathf.Max(0.01f, maxLaunchSpeed);
            float impliedSpeed = impulse / Mathf.Max(0.0001f, mass);
            return impliedSpeed <= maxSpeed
                ? impulse
                : impulse * (maxSpeed / impliedSpeed);
        }

        /// <summary>
        /// True when a static or kinematic surface outside this destructible sits directly
        /// under the piece. Only then does an outward blast vector need its downward part
        /// removed; airborne debris keeps the authored arc.
        /// </summary>
        bool IsRestingOnStaticGround(Bounds worldBounds)
        {
            if (worldBounds.size.sqrMagnitude <= 0.000001f) return false;
            DestructionProfile profile = m_root != null ? m_root.Profile : null;
            const float lift = 0.02f;
            float clearance = Mathf.Clamp(worldBounds.extents.y * 0.15f, 0.025f, 0.1f);
            int hits = Physics.RaycastNonAlloc(
                worldBounds.center + Vector3.up * lift, Vector3.down,
                m_groundProbeHits, worldBounds.extents.y + lift + clearance,
                profile != null ? profile.SupportLayers : Physics.AllLayers,
                QueryTriggerInteraction.Ignore);
            Transform rootTransform = m_root != null ? m_root.transform : transform;
            for (int i = 0; i < hits; i++)
            {
                Collider hit = m_groundProbeHits[i].collider;
                if (hit == null || !hit.enabled ||
                    !hit.gameObject.activeInHierarchy ||
                    hit.transform == rootTransform ||
                    hit.transform.IsChildOf(rootTransform))
                    continue;
                Rigidbody supportBody = hit.attachedRigidbody;
                if (supportBody != null && !supportBody.isKinematic) continue;
                return true;
            }
            return false;
        }

        static Vector3 IncomingDirection(Collision collision)
        {
            Rigidbody body = collision != null ? collision.rigidbody : null;
            if (body == null || body.isKinematic) return Vector3.zero;
#if UNITY_6000_0_OR_NEWER
            Vector3 velocity = body.linearVelocity;
#else
            Vector3 velocity = body.velocity;
#endif
            return velocity.sqrMagnitude > 0.000001f
                ? velocity.normalized : Vector3.zero;
        }

        bool TryFindCandidate(Vector3 point, float hitRadius,
            DestructionProfile profile, out Candidate best)
        {
            best = default;
            float bestDistance = float.PositiveInfinity;
            bool terminalAtPoint = false;
            int maxDepth = Mathf.Clamp(profile.RuntimeSplitMaxDepth, 1, 4);
            float minimumVolume = EffectiveMinimumSplitVolume(profile);
            float tolerance = Mathf.Max(1.0f, hitRadius);
            float toleranceSqr = tolerance * tolerance;

            for (int i = 0; i < m_runtimePieces.Count; i++)
            {
                RuntimeSplitPiece piece = m_runtimePieces[i];
                if (piece == null || !piece.gameObject.activeInHierarchy ||
                    piece.IsDisappearing) continue;
                Consider(piece.gameObject, piece.CachedFilter,
                    piece.CachedRenderer, piece.CachedBody,
                    piece, piece.Depth, point, maxDepth, minimumVolume, toleranceSqr,
                    ref terminalAtPoint, ref bestDistance, ref best);
            }

            if (m_root.Fragments != null)
            {
                for (int i = 0; i < m_root.Fragments.Count; i++)
                {
                    FragmentRecord record = m_root.Fragments[i];
                    if (record?.Transform == null || !record.Transform.gameObject.activeInHierarchy)
                        continue;
                    // Attached shell geometry must follow the normal baked break path
                    // first. Runtime cutting is only for already loose fragments.
                    if (!m_root.IsFragmentLoose(i)) continue;
                    GameObject go = record.Transform.gameObject;
                    Consider(go, go.GetComponent<MeshFilter>(), record.Renderer,
                        record.Body, null, 0, point, maxDepth,
                        minimumVolume, toleranceSqr,
                        ref terminalAtPoint, ref bestDistance, ref best);
                }
            }

            if (best.GameObject == null || bestDistance > toleranceSqr) return false;

            // Max Depth is spent by runtime children only, so only a spent runtime child
            // absorbs the hits that land on it, and only when nothing splittable sits at
            // the same point. A baked fragment never counts as spent, so no amount of
            // finished debris can make it stop reacting.
            return !terminalAtPoint || bestDistance <= AtPointSqr;
        }

        /// <summary>
        /// Squared radius that counts as "the hit landed on this piece". It absorbs the
        /// gap PhysX leaves between a contact point and the surface it belongs to.
        /// </summary>
        const float AtPointSqr = 0.0009f; // 3 cm

        static void Consider(GameObject go, MeshFilter filter, Renderer renderer,
            Rigidbody body, RuntimeSplitPiece runtimePiece,
            int depth, Vector3 point,
            int maxDepth, float minimumVolume, float toleranceSqr,
            ref bool terminalAtPoint, ref float bestDistance, ref Candidate best)
        {
            if (go == null || filter == null || filter.sharedMesh == null ||
                renderer == null || !renderer.enabled) return;
            // Cheap reject first, then the exact shape distance. Renderer bounds are
            // world AABBs: in a rubble pile a dozen of them contain the same hit point,
            // so ranking by bounds alone cannot tell which shard was actually hit.
            if (renderer.bounds.SqrDistance(point) > toleranceSqr) return;
            float distance = ShapeDistanceSqr(go.transform, filter.sharedMesh,
                renderer, point);
            // The iteration counter advances only through a real dynamic split, so only a
            // runtime child can ever run out of iterations. A baked fragment is always a
            // valid target: it has not split even once.
            if (runtimePiece != null && depth >= maxDepth)
            {
                if (distance <= AtPointSqr) terminalAtPoint = true;
                return;
            }
            // Too small to cut, but it never used an iteration: it must not shield the
            // piece behind it, or a chip of debris would make a whole chunk inert.
            if (LocalBoundsVolume(filter.sharedMesh.bounds) < minimumVolume) return;
            if (distance >= bestDistance) return;
            FragmentRuntime bakedPiece = runtimePiece == null
                ? go.GetComponent<FragmentRuntime>() : null;
            bestDistance = distance;
            best = new Candidate
            {
                GameObject = go,
                Filter = filter,
                Renderer = renderer,
                Body = body,
                RuntimePiece = runtimePiece,
                BakedPiece = bakedPiece,
                Depth = depth,
                EffectColor = runtimePiece != null ? runtimePiece.EffectColor :
                    bakedPiece != null ? bakedPiece.EffectColor : Color.white,
                HasEffectColor = runtimePiece != null ? runtimePiece.HasEffectColor :
                    bakedPiece != null && bakedPiece.HasEffectColor
            };
        }

        /// <summary>
        /// Squared distance from a hit point to the oriented box of the piece itself.
        /// The same approximation is used for baked and runtime pieces so neither can win
        /// a tie through the kind of collider it happens to carry: runtime children ride
        /// on a deliberately small inset tetrahedron, baked shards on a fitted hull.
        /// </summary>
        static float ShapeDistanceSqr(Transform pieceTransform, Mesh mesh,
            Renderer renderer, Vector3 point)
        {
            if (pieceTransform == null || mesh == null)
                return renderer.bounds.SqrDistance(point);
            Vector3 local = pieceTransform.InverseTransformPoint(point);
            float distance = mesh.bounds.SqrDistance(local);
            // Local units become world units again through the uniform part of the scale,
            // so pieces baked at different scales stay comparable.
            Vector3 scale = pieceTransform.lossyScale;
            float uniform = Mathf.Max(0.0001f,
                (Mathf.Abs(scale.x) + Mathf.Abs(scale.y) + Mathf.Abs(scale.z)) / 3f);
            return distance * uniform * uniform;
        }

        bool TrySplit(Candidate candidate, DamageContext context, DestructionProfile profile)
        {
            if (!CanRuntimeSplit(profile)) return false;
            if (candidate.GameObject == null || !candidate.GameObject.activeInHierarchy) return false;
            if (candidate.Depth >= Mathf.Clamp(profile.RuntimeSplitMaxDepth, 1, 4)) return false;
            if (LocalBoundsVolume(candidate.Filter.sharedMesh.bounds) <
                EffectiveMinimumSplitVolume(profile))
            {
                return false;
            }

            // The clip path is strictly bounded to at most four children and a
            // small global plane budget. Execute explicit hits immediately; failed
            // topology is rejected instead of being replaced with proxy geometry.
            return TrySplitNow(candidate, context, profile);
        }

        static bool CanRuntimeSplit(DestructionProfile profile) =>
            profile != null && profile.RuntimeFragmentSplitting &&
            profile.Pattern != FracturePattern.RadialGlass;

        bool TrySplitNow(Candidate candidate, DamageContext context, DestructionProfile profile)
        {
            int requested = Mathf.Clamp(profile.RuntimeSplitPieces, 2,
                Mathf.Min(4, profile.RuntimeSplitMaxActivePieces));
            Mesh sourceMesh = candidate.Filter.sharedMesh;
            int sourceTriangles = GetTriangleCount(sourceMesh);
            if (sourceMesh == null || sourceTriangles < 1 || sourceTriangles > profile.RuntimeSplitMaxSourceTriangles)
                return false;

            int seed = profile.Seed ^ candidate.GameObject.GetHashCode() ^
                       (++m_splitSerial * 486187739);
            Transform sourceTransform = candidate.GameObject.transform;
            List<Mesh> pieces = RuntimeMeshSlicer.SplitFast(
                sourceMesh, requested, seed,
                Mathf.Max(0.01f, profile.InteriorUVScale),
                sourceTransform.lossyScale);
            if (pieces.Count < 2)
            {
                pieces = RuntimeMeshSlicer.SplitInto(sourceMesh, requested, seed,
                    Mathf.Max(0.01f, profile.InteriorUVScale), CutSurfaceUVMode.PlanarProjection);
            }
            if (pieces.Count < 2)
            {
                DestroyGeneratedMeshes(pieces, sourceMesh);
                return false;
            }

            // Prepare every physics shape before touching the live hierarchy. A
            // failed convex cook used to leave a visible child with no usable
            // collider, so later shots appeared to ignore an otherwise valid
            // runtime split. Each tetrahedron is selected from the actual piece
            // vertices and inset, keeping this validation linear and bounded.
            var colliderMeshes = new List<Mesh>(pieces.Count);
            for (int i = 0; i < pieces.Count; i++)
            {
                Mesh colliderMesh = BuildInsetTetrahedronCollider(pieces[i]);
                if (colliderMesh == null)
                    colliderMesh = BuildFallbackConvexCollider(pieces[i]);
                if (colliderMesh != null)
                {
                    colliderMeshes.Add(colliderMesh);
                    continue;
                }

                DestroyGeneratedMeshes(colliderMeshes, null);
                DestroyGeneratedMeshes(pieces, sourceMesh);
                return false;
            }

            PrebakeColliderMeshes(colliderMeshes);

            Vector3 inheritedLinear = Vector3.zero;
            Vector3 inheritedAngular = Vector3.zero;
            float parentMass = 1f;
            if (candidate.Body != null)
            {
#if UNITY_6000_0_OR_NEWER
                inheritedLinear = candidate.Body.linearVelocity;
#else
                inheritedLinear = candidate.Body.velocity;
#endif
                inheritedAngular = candidate.Body.angularVelocity;
                parentMass = Mathf.Max(0.01f, candidate.Body.mass);
            }

            if (m_runtimeRoot == null)
            {
                var rootObject = new GameObject("Runtime Split Pieces");
                rootObject.transform.SetParent(m_root.FracturedRoot != null
                    ? m_root.FracturedRoot.transform : transform, false);
                m_runtimeRoot = rootObject.transform;
            }

            float totalVolume = 0f;
            for (int i = 0; i < pieces.Count; i++)
                totalVolume += Mathf.Max(0.000001f, LocalBoundsVolume(pieces[i].bounds));

            Material[] sourceMaterials = candidate.Renderer.sharedMaterials;
            var createdChildren = new List<GameObject>(pieces.Count);
            for (int i = 0; i < pieces.Count; i++)
            {
                Mesh mesh = pieces[i];
                var child = new GameObject(candidate.GameObject.name + $"_Runtime_{m_splitSerial:000}_{i:00}");
                child.layer = candidate.GameObject.layer;
                child.tag = candidate.GameObject.tag;
                child.transform.SetParent(m_runtimeRoot, false);
                child.transform.position = sourceTransform.position;
                child.transform.rotation = sourceTransform.rotation;
                Vector3 parentScale = m_runtimeRoot.lossyScale;
                Vector3 worldScale = sourceTransform.lossyScale;
                child.transform.localScale = new Vector3(
                    SafeScaleRatio(worldScale.x, parentScale.x),
                    SafeScaleRatio(worldScale.y, parentScale.y),
                    SafeScaleRatio(worldScale.z, parentScale.z));

                var filter = child.AddComponent<MeshFilter>();
                filter.sharedMesh = mesh;
                var renderer = child.AddComponent<MeshRenderer>();
                renderer.sharedMaterials = BuildRuntimeMaterials(sourceMaterials,
                    mesh != null ? mesh.subMeshCount : 1);

                AddSimpleConvexCollider(child, colliderMeshes[i]);
                var body = child.AddComponent<Rigidbody>();
                ConfigureRuntimeDebrisBody(body, profile);
                body.maxDepenetrationVelocity = 1.0f;
                float ratio = Mathf.Max(0.000001f, LocalBoundsVolume(mesh.bounds)) / totalVolume;
                body.mass = Mathf.Max(0.01f, parentMass * ratio);
                body.interpolation = candidate.Body != null
                    ? candidate.Body.interpolation : RigidbodyInterpolation.Interpolate;
                // Sweep-based against static geometry, not speculative: these are the thinnest
                // pieces in the whole system and therefore the most likely to pass straight
                // through a floor, especially one modelled as a zero-thickness plane. Not
                // ContinuousDynamic — sweeping these against each other as well only buys a
                // flood of extra collision reports inside the rubble pile.
                body.collisionDetectionMode = CollisionDetectionMode.Continuous;
                // Runtime pieces are the smallest debris of all, so they have the lowest
                // inertia and take the most spin from the same off-centre push. Derive their
                // limit from the profile instead of inheriting the project-wide default.
                body.maxAngularVelocity = profile.ResolveMaxAngularSpeed(
                    renderer.bounds.extents.magnitude);
                // Same reasoning as the primary fragments: inherited motion is written
                // straight onto the body and is whatever Unity's collision response left on
                // the parent, so it is the one speed the profile never bounded.
                Vector3 inheritedMotion = profile.ClampInheritedVelocity(inheritedLinear);
#if UNITY_6000_0_OR_NEWER
                body.linearVelocity = inheritedMotion;
#else
                body.velocity = inheritedMotion;
#endif
                body.angularVelocity = inheritedAngular;
                Vector3 separation = (child.transform.TransformPoint(mesh.bounds.center) -
                    context.Point).normalized;
                if (separation.sqrMagnitude < 0.01f)
                    separation = UnityEngine.Random.onUnitSphere;
                // Held to the same speed budget as every other launch in the system. This
                // separation kick was the one place applying a raw impulse, and it lands on
                // the lightest bodies that exist here: a piece cut twice from a low-density
                // material weighs a few grams, so a 1.5 N·s nudge is tens of metres per
                // second. A piece cut off the underside of a prop is pushed away from the hit
                // point, which points down, and at that speed it crosses a floor between two
                // physics steps before any collision detection can answer for it.
                float separationImpulse = ClampImpulseToLaunchSpeed(
                    profile.RuntimeSplitSeparationForce, body.mass,
                    profile.MaxFragmentLaunchSpeed);
                if (separationImpulse > 0f)
                    body.AddForce(separation * separationImpulse, ForceMode.Impulse);

                var piece = child.AddComponent<RuntimeSplitPiece>();
                int tris = GetTriangleCount(mesh);
                float vol = mesh != null ? LocalBoundsVolume(mesh.bounds) : 0.001f;
                // Runtime pieces are the smallest debris of all, so they join the global
                // debris auto-despawn. The hidden legacy per-split disappear remains as
                // the fallback for profiles that relied on it.
                bool pieceDespawn = profile.DespawnDebris ||
                                    profile.RuntimeSplitPiecesDisappear;
                float pieceLifetime = profile.DespawnDebris
                    ? profile.DespawnDebrisAfter
                    : profile.RuntimeSplitDisappearAfter;
                piece.Initialize(this, candidate.Depth + 1,
                    pieceDespawn, pieceLifetime,
                    candidate.EffectColor, candidate.HasEffectColor, tris, vol);
                m_runtimePieces.Add(piece);
                createdChildren.Add(child);
            }

            TransferInteriorScatter(candidate.GameObject, sourceTransform,
                createdChildren, pieces);

            if (candidate.RuntimePiece != null)
            {
                candidate.RuntimePiece.DeactivateAfterSplit();
            }
            else
            {
                // Baked primaries can also be members of the one-body Dynamic residual
                // cluster. Remove the replaced primary from that graph before hiding it;
                // otherwise its disabled transform/collider remains a ghost bridge and
                // visibly loose shards keep rotating with the distant carrier.
                if (candidate.BakedPiece != null &&
                    candidate.BakedPiece.Level == 0 &&
                    candidate.BakedPiece.Root == m_root)
                {
                    m_root.NotifyPrimaryReplaced(
                        candidate.BakedPiece.FragmentIndex, false);
                }
                candidate.GameObject.SetActive(false);
                if (!m_disabledOriginals.Contains(candidate.GameObject))
                    m_disabledOriginals.Add(candidate.GameObject);
            }
            return true;
        }

        bool CanReserveSplitBudget(DestructionProfile profile,
            RuntimeSplitPiece replacingPiece, int childCount, int childTriangles)
        {
            int currentActiveCount = 0;
            int totalTriangles = 0;

            for (int i = 0; i < m_runtimePieces.Count; i++)
            {
                RuntimeSplitPiece p = m_runtimePieces[i];
                if (p == null || !p.gameObject.activeInHierarchy || p == replacingPiece)
                    continue;
                totalTriangles += p.TriangleCount;
                currentActiveCount++;
            }

            int maxActive = Mathf.Clamp(profile.RuntimeSplitMaxActivePieces, 2, 64);
            int maxTriangles = Mathf.Max(10000, profile.MaxActiveTriangles);
            return currentActiveCount + childCount <= maxActive &&
                   totalTriangles + Mathf.Max(0, childTriangles) <= maxTriangles;
        }

        /// <summary>
        /// Keeps direct hits responsive after the per-object runtime budget fills up.
        /// The old behaviour silently refused every later split (with three children
        /// and the default cap this happened after only about ten primary fragments).
        /// Recycle the smallest existing generated debris until the requested split
        /// fits; baked fragments are never removed and the hard performance cap still
        /// remains in force.
        /// </summary>
        bool EnsureSplitBudget(DestructionProfile profile,
            RuntimeSplitPiece replacingPiece, int childCount, int childTriangles)
        {
            // Do not evict retained debris behind the scene rule's back. When the local
            // mesh-generation cap is full, further subdivision waits for scene cleanup.
            if (DestructionBudgetManager.OverridesProfileRemoval(m_root))
                return CanReserveSplitBudget(profile, replacingPiece, childCount, childTriangles);
            while (!CanReserveSplitBudget(profile, replacingPiece, childCount,
                       childTriangles))
            {
                RuntimeSplitPiece smallest = null;
                float smallestVolume = float.PositiveInfinity;
                for (int i = 0; i < m_runtimePieces.Count; i++)
                {
                    RuntimeSplitPiece piece = m_runtimePieces[i];
                    if (piece == null || piece == replacingPiece ||
                        !piece.gameObject.activeInHierarchy)
                        continue;
                    if (piece.Volume >= smallestVolume) continue;
                    smallest = piece;
                    smallestVolume = piece.Volume;
                }

                if (smallest == null) return false;
                ReleaseRuntimePiece(smallest);
            }
            return true;
        }

        static Material[] BuildRuntimeMaterials(Material[] sourceMaterials,
            int targetSubmeshCount)
        {
            targetSubmeshCount = Mathf.Max(1, targetSubmeshCount);
            var result = new Material[targetSubmeshCount];
            if (sourceMaterials == null || sourceMaterials.Length == 0)
                return result;

            // WorkMesh adds one new final submesh for the fresh cap. Reuse the
            // parent's current cut material for it, so runtime cuts do not gain an
            // unassigned/pink material slot after the first split.
            Material cutMaterial = sourceMaterials[sourceMaterials.Length - 1];
            for (int i = 0; i < result.Length; i++)
                result[i] = i < sourceMaterials.Length
                    ? sourceMaterials[i]
                    : cutMaterial;
            return result;
        }

        static void TransferInteriorScatter(GameObject sourceObject,
            Transform sourceTransform, List<GameObject> children,
            List<Mesh> pieceMeshes)
        {
            if (sourceObject == null || sourceTransform == null || children == null ||
                pieceMeshes == null || children.Count != pieceMeshes.Count ||
                children.Count == 0) return;

            MeshRenderer[] renderers =
                sourceObject.GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                MeshRenderer sourceRenderer = renderers[i];
                if (sourceRenderer == null || sourceRenderer.gameObject == sourceObject ||
                    sourceRenderer.gameObject.name != "Interior Scatter") continue;
                MeshFilter sourceFilter = sourceRenderer.GetComponent<MeshFilter>();
                if (sourceFilter == null || sourceFilter.sharedMesh == null) continue;

                Matrix4x4 scatterToSource = sourceTransform.worldToLocalMatrix *
                                             sourceRenderer.transform.localToWorldMatrix;
                Mesh[] partitions = PartitionScatterMesh(sourceFilter.sharedMesh,
                    scatterToSource, pieceMeshes);
                for (int childIndex = 0; childIndex < partitions.Length; childIndex++)
                {
                    Mesh partition = partitions[childIndex];
                    if (partition == null) continue;
                    var detail = new GameObject("Interior Scatter");
                    detail.layer = children[childIndex].layer;
                    detail.transform.SetParent(children[childIndex].transform, false);
                    var filter = detail.AddComponent<MeshFilter>();
                    filter.sharedMesh = partition;
                    var renderer = detail.AddComponent<MeshRenderer>();
                    MeshRenderer childRenderer =
                        children[childIndex].GetComponent<MeshRenderer>();
                    Material cutMaterial = FragmentMaterialUtility.GetCutSurfaceMaterial(
                        childRenderer != null ? childRenderer.sharedMaterials : null,
                        sourceRenderer.sharedMaterial);
                    renderer.sharedMaterial = cutMaterial;
                    renderer.shadowCastingMode = sourceRenderer.shadowCastingMode;
                    renderer.receiveShadows = sourceRenderer.receiveShadows;
                    renderer.lightProbeUsage = sourceRenderer.lightProbeUsage;
                    renderer.reflectionProbeUsage = sourceRenderer.reflectionProbeUsage;
                    renderer.probeAnchor = sourceRenderer.probeAnchor;
                    renderer.enabled = true;
                }
            }
        }

        struct ScatterTriangle
        {
            public int Submesh;
            public int A;
            public int B;
            public int C;
        }

        static Mesh[] PartitionScatterMesh(Mesh source, Matrix4x4 scatterToSource,
            List<Mesh> pieceMeshes)
        {
            int pieceCount = pieceMeshes.Count;
            var output = new Mesh[pieceCount];
            if (source == null || source.vertexCount < 3 || pieceCount == 0)
                return output;

            Vector3[] sourcePositions = source.vertices;
            Vector3[] sourceNormals = source.normals;
            Vector2[] sourceUVs = source.uv;
            Vector4[] sourceTangents = source.tangents;
            var positions = new Vector3[sourcePositions.Length];
            var normals = new Vector3[sourcePositions.Length];
            var tangents = new Vector4[sourcePositions.Length];
            Matrix4x4 normalMatrix = scatterToSource.inverse.transpose;
            bool hasNormals = sourceNormals != null &&
                              sourceNormals.Length == sourcePositions.Length;
            bool hasUVs = sourceUVs != null &&
                          sourceUVs.Length == sourcePositions.Length;
            bool hasTangents = sourceTangents != null &&
                               sourceTangents.Length == sourcePositions.Length;
            for (int i = 0; i < sourcePositions.Length; i++)
            {
                positions[i] = scatterToSource.MultiplyPoint3x4(sourcePositions[i]);
                if (hasNormals)
                    normals[i] = normalMatrix.MultiplyVector(sourceNormals[i]).normalized;
                if (hasTangents)
                {
                    Vector3 tangent = scatterToSource.MultiplyVector(
                        new Vector3(sourceTangents[i].x, sourceTangents[i].y,
                            sourceTangents[i].z)).normalized;
                    tangents[i] = new Vector4(tangent.x, tangent.y, tangent.z,
                        sourceTangents[i].w);
                }
            }

            int submeshCount = Mathf.Max(1, source.subMeshCount);
            var maps = new Dictionary<int, int>[pieceCount];
            var outPositions = new List<Vector3>[pieceCount];
            var outNormals = new List<Vector3>[pieceCount];
            var outUVs = new List<Vector2>[pieceCount];
            var outTangents = new List<Vector4>[pieceCount];
            var outTriangles = new List<int>[pieceCount][];
            for (int piece = 0; piece < pieceCount; piece++)
            {
                maps[piece] = new Dictionary<int, int>();
                outPositions[piece] = new List<Vector3>();
                outNormals[piece] = new List<Vector3>();
                outUVs[piece] = new List<Vector2>();
                outTangents[piece] = new List<Vector4>();
                outTriangles[piece] = new List<int>[submeshCount];
                for (int submesh = 0; submesh < submeshCount; submesh++)
                    outTriangles[piece][submesh] = new List<int>();
            }

            var transformedBounds = new Bounds(positions[0], Vector3.zero);
            for (int i = 1; i < positions.Length; i++)
                transformedBounds.Encapsulate(positions[i]);
            float weldCell = Mathf.Max(transformedBounds.size.magnitude * 1e-5f,
                1e-6f);
            var weldedIds = new int[positions.Length];
            var welded = new Dictionary<Vector3Int, int>(positions.Length);
            int nextWelded = 0;
            for (int i = 0; i < positions.Length; i++)
            {
                Vector3 point = positions[i];
                var cell = new Vector3Int(
                    Mathf.RoundToInt(point.x / weldCell),
                    Mathf.RoundToInt(point.y / weldCell),
                    Mathf.RoundToInt(point.z / weldCell));
                if (!welded.TryGetValue(cell, out int id))
                {
                    id = nextWelded++;
                    welded.Add(cell, id);
                }
                weldedIds[i] = id;
            }

            var scatterTriangles = new List<ScatterTriangle>();
            var weldedToTriangles = new Dictionary<int, List<int>>();
            for (int submesh = 0; submesh < submeshCount; submesh++)
            {
                int[] triangles = source.GetTriangles(submesh);
                for (int i = 0; i + 2 < triangles.Length; i += 3)
                {
                    int triangleIndex = scatterTriangles.Count;
                    var triangle = new ScatterTriangle
                    {
                        Submesh = submesh,
                        A = triangles[i],
                        B = triangles[i + 1],
                        C = triangles[i + 2]
                    };
                    scatterTriangles.Add(triangle);
                    int[] corners = { triangle.A, triangle.B, triangle.C };
                    for (int corner = 0; corner < 3; corner++)
                    {
                        int weldedId = weldedIds[corners[corner]];
                        if (!weldedToTriangles.TryGetValue(weldedId,
                                out List<int> neighbours))
                        {
                            neighbours = new List<int>();
                            weldedToTriangles.Add(weldedId, neighbours);
                        }
                        neighbours.Add(triangleIndex);
                    }
                }
            }

            bool[] visited = new bool[scatterTriangles.Count];
            var queue = new Queue<int>();
            var component = new List<int>();
            for (int start = 0; start < scatterTriangles.Count; start++)
            {
                if (visited[start]) continue;
                visited[start] = true;
                queue.Enqueue(start);
                component.Clear();
                Vector3 centroid = Vector3.zero;
                int centroidSamples = 0;
                while (queue.Count > 0)
                {
                    int triangleIndex = queue.Dequeue();
                    component.Add(triangleIndex);
                    ScatterTriangle triangle = scatterTriangles[triangleIndex];
                    int[] corners = { triangle.A, triangle.B, triangle.C };
                    for (int corner = 0; corner < 3; corner++)
                    {
                        int sourceIndex = corners[corner];
                        centroid += positions[sourceIndex];
                        centroidSamples++;
                        List<int> neighbours =
                            weldedToTriangles[weldedIds[sourceIndex]];
                        for (int n = 0; n < neighbours.Count; n++)
                        {
                            int neighbour = neighbours[n];
                            if (visited[neighbour]) continue;
                            visited[neighbour] = true;
                            queue.Enqueue(neighbour);
                        }
                    }
                }

                if (centroidSamples == 0) continue;
                int owner = ClosestPieceBounds(pieceMeshes,
                    centroid / centroidSamples);
                for (int componentIndex = 0;
                     componentIndex < component.Count; componentIndex++)
                {
                    ScatterTriangle triangle =
                        scatterTriangles[component[componentIndex]];
                    int[] corners = { triangle.A, triangle.B, triangle.C };
                    for (int corner = 0; corner < 3; corner++)
                    {
                        int sourceIndex = corners[corner];
                        if (!maps[owner].TryGetValue(sourceIndex,
                                out int targetIndex))
                        {
                            targetIndex = outPositions[owner].Count;
                            maps[owner].Add(sourceIndex, targetIndex);
                            outPositions[owner].Add(positions[sourceIndex]);
                            if (hasNormals)
                                outNormals[owner].Add(normals[sourceIndex]);
                            if (hasUVs) outUVs[owner].Add(sourceUVs[sourceIndex]);
                            if (hasTangents)
                                outTangents[owner].Add(tangents[sourceIndex]);
                        }
                        outTriangles[owner][triangle.Submesh].Add(targetIndex);
                    }
                }
            }

            for (int piece = 0; piece < pieceCount; piece++)
            {
                if (outPositions[piece].Count < 3) continue;
                var mesh = new Mesh
                {
                    name = source.name + $" Runtime Scatter {piece:00}",
                    hideFlags = HideFlags.DontSave
                };
                if (outPositions[piece].Count > 65535)
                    mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.SetVertices(outPositions[piece]);
                if (hasNormals) mesh.SetNormals(outNormals[piece]);
                if (hasUVs) mesh.SetUVs(0, outUVs[piece]);
                if (hasTangents) mesh.SetTangents(outTangents[piece]);
                mesh.subMeshCount = submeshCount;
                for (int submesh = 0; submesh < submeshCount; submesh++)
                    mesh.SetTriangles(outTriangles[piece][submesh], submesh, false);
                SealScatterOpenBoundaries(mesh);
                mesh.RecalculateBounds();
                output[piece] = mesh;
            }
            return output;
        }

        struct ScatterBoundaryUse
        {
            public int Count;
            public int A;
            public int B;
        }

        readonly struct ScatterEdgeKey : System.IEquatable<ScatterEdgeKey>
        {
            readonly int m_low;
            readonly int m_high;

            public ScatterEdgeKey(int a, int b)
            {
                m_low = Mathf.Min(a, b);
                m_high = Mathf.Max(a, b);
            }

            public bool Equals(ScatterEdgeKey other) =>
                m_low == other.m_low && m_high == other.m_high;
            public override bool Equals(object obj) =>
                obj is ScatterEdgeKey other && Equals(other);
            public override int GetHashCode() => (m_low * 397) ^ m_high;
        }

        static void SealScatterOpenBoundaries(Mesh mesh)
        {
            if (mesh == null || mesh.vertexCount < 3) return;
            Vector3[] sourcePositions = mesh.vertices;
            Vector3[] sourceNormals = mesh.normals;
            Vector2[] sourceUVs = mesh.uv;
            Vector4[] sourceTangents = mesh.tangents;
            bool hasNormals = sourceNormals != null &&
                              sourceNormals.Length == sourcePositions.Length;
            bool hasUVs = sourceUVs != null &&
                          sourceUVs.Length == sourcePositions.Length;
            bool hasTangents = sourceTangents != null &&
                               sourceTangents.Length == sourcePositions.Length;

            Bounds bounds = mesh.bounds;
            float weldCell = Mathf.Max(bounds.size.magnitude * 1e-5f, 1e-6f);
            var welded = new Dictionary<Vector3Int, int>(sourcePositions.Length);
            var remap = new int[sourcePositions.Length];
            var representativePosition = new List<Vector3>();
            var representativeUV = new List<Vector2>();
            for (int i = 0; i < sourcePositions.Length; i++)
            {
                Vector3 point = sourcePositions[i];
                var cell = new Vector3Int(
                    Mathf.RoundToInt(point.x / weldCell),
                    Mathf.RoundToInt(point.y / weldCell),
                    Mathf.RoundToInt(point.z / weldCell));
                if (!welded.TryGetValue(cell, out int id))
                {
                    id = representativePosition.Count;
                    welded.Add(cell, id);
                    representativePosition.Add(point);
                    representativeUV.Add(hasUVs ? sourceUVs[i] : Vector2.zero);
                }
                remap[i] = id;
            }

            var edgeUses = new Dictionary<ScatterEdgeKey, ScatterBoundaryUse>();
            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
            {
                int[] triangles = mesh.GetTriangles(submesh);
                for (int i = 0; i + 2 < triangles.Length; i += 3)
                {
                    int a = remap[triangles[i]];
                    int b = remap[triangles[i + 1]];
                    int c = remap[triangles[i + 2]];
                    AddScatterBoundaryEdge(edgeUses, a, b);
                    AddScatterBoundaryEdge(edgeUses, b, c);
                    AddScatterBoundaryEdge(edgeUses, c, a);
                }
            }

            var adjacency = new Dictionary<int, List<ScatterEdgeKey>>();
            var unused = new HashSet<ScatterEdgeKey>();
            foreach (var pair in edgeUses)
            {
                if (pair.Value.Count != 1) continue;
                unused.Add(pair.Key);
                AddScatterBoundaryAdjacency(adjacency, pair.Value.A, pair.Key);
                AddScatterBoundaryAdjacency(adjacency, pair.Value.B, pair.Key);
            }
            if (unused.Count == 0) return;

            var positions = new List<Vector3>(sourcePositions);
            var normals = hasNormals
                ? new List<Vector3>(sourceNormals) : new List<Vector3>();
            var uvs = hasUVs
                ? new List<Vector2>(sourceUVs) : new List<Vector2>();
            var tangents = hasTangents
                ? new List<Vector4>(sourceTangents) : new List<Vector4>();
            var submeshTriangles = new List<int>[mesh.subMeshCount];
            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
                submeshTriangles[submesh] = new List<int>(mesh.GetTriangles(submesh));

            var loop = new List<int>();
            while (unused.Count > 0)
            {
                ScatterEdgeKey firstKey = default;
                foreach (ScatterEdgeKey key in unused)
                {
                    firstKey = key;
                    break;
                }
                ScatterBoundaryUse first = edgeUses[firstKey];
                unused.Remove(firstKey);
                loop.Clear();
                loop.Add(first.A);
                loop.Add(first.B);
                int start = first.A;
                int current = first.B;
                int safety = edgeUses.Count + 1;
                while (current != start && safety-- > 0 &&
                       adjacency.TryGetValue(current,
                           out List<ScatterEdgeKey> candidates))
                {
                    bool advanced = false;
                    for (int i = 0; i < candidates.Count; i++)
                    {
                        ScatterEdgeKey key = candidates[i];
                        if (!unused.Remove(key)) continue;
                        ScatterBoundaryUse use = edgeUses[key];
                        current = use.A == current ? use.B : use.A;
                        if (current != start) loop.Add(current);
                        advanced = true;
                        break;
                    }
                    if (!advanced) break;
                }
                if (current != start || loop.Count < 3) continue;

                Vector3 center = Vector3.zero;
                Vector2 uvCenter = Vector2.zero;
                for (int i = 0; i < loop.Count; i++)
                {
                    center += representativePosition[loop[i]];
                    uvCenter += representativeUV[loop[i]];
                }
                center /= loop.Count;
                uvCenter /= loop.Count;

                for (int i = 0; i < loop.Count; i++)
                {
                    Vector3 currentPoint = representativePosition[loop[i]];
                    Vector3 nextPoint = representativePosition[
                        loop[(i + 1) % loop.Count]];
                    Vector3 normal = Vector3.Cross(currentPoint - center,
                        nextPoint - center).normalized;
                    if (normal.sqrMagnitude < 0.5f) continue;
                    Vector2 currentUV = representativeUV[loop[i]];
                    Vector2 nextUV = representativeUV[
                        loop[(i + 1) % loop.Count]];
                    AddScatterCapTriangle(positions, normals, uvs, tangents,
                        submeshTriangles[0], center, uvCenter, currentPoint,
                        currentUV, nextPoint, nextUV, normal, hasNormals,
                        hasUVs, hasTangents);
                    AddScatterCapTriangle(positions, normals, uvs, tangents,
                        submeshTriangles[0], center, uvCenter, nextPoint,
                        nextUV, currentPoint, currentUV, -normal, hasNormals,
                        hasUVs, hasTangents);
                }
            }

            mesh.SetVertices(positions);
            if (hasNormals) mesh.SetNormals(normals);
            if (hasUVs) mesh.SetUVs(0, uvs);
            if (hasTangents) mesh.SetTangents(tangents);
            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
                mesh.SetTriangles(submeshTriangles[submesh], submesh, false);
        }

        static void AddScatterBoundaryEdge(
            Dictionary<ScatterEdgeKey, ScatterBoundaryUse> edges, int a, int b)
        {
            if (a == b) return;
            var key = new ScatterEdgeKey(a, b);
            if (edges.TryGetValue(key, out ScatterBoundaryUse use))
            {
                use.Count++;
                edges[key] = use;
            }
            else
            {
                edges.Add(key, new ScatterBoundaryUse
                {
                    Count = 1,
                    A = a,
                    B = b
                });
            }
        }

        static void AddScatterBoundaryAdjacency(
            Dictionary<int, List<ScatterEdgeKey>> adjacency, int vertex,
            ScatterEdgeKey edge)
        {
            if (!adjacency.TryGetValue(vertex, out List<ScatterEdgeKey> list))
            {
                list = new List<ScatterEdgeKey>();
                adjacency.Add(vertex, list);
            }
            list.Add(edge);
        }

        static void AddScatterCapTriangle(List<Vector3> positions,
            List<Vector3> normals, List<Vector2> uvs, List<Vector4> tangents,
            List<int> triangles, Vector3 a, Vector2 uvA, Vector3 b,
            Vector2 uvB, Vector3 c, Vector2 uvC, Vector3 normal,
            bool hasNormals, bool hasUVs, bool hasTangents)
        {
            int first = positions.Count;
            positions.Add(a); positions.Add(b); positions.Add(c);
            if (hasNormals)
            {
                normals.Add(normal); normals.Add(normal); normals.Add(normal);
            }
            if (hasUVs)
            {
                uvs.Add(uvA); uvs.Add(uvB); uvs.Add(uvC);
            }
            if (hasTangents)
            {
                Vector3 tangent = (b - a).normalized;
                var value = new Vector4(tangent.x, tangent.y, tangent.z, 1f);
                tangents.Add(value); tangents.Add(value); tangents.Add(value);
            }
            triangles.Add(first);
            triangles.Add(first + 1);
            triangles.Add(first + 2);
        }

        static int ClosestPieceBounds(List<Mesh> pieces, Vector3 point)
        {
            int bestIndex = 0;
            float bestOutside = float.PositiveInfinity;
            float bestCenter = float.PositiveInfinity;
            for (int i = 0; i < pieces.Count; i++)
            {
                Bounds bounds = pieces[i].bounds;
                float outside = bounds.SqrDistance(point);
                float scale = Mathf.Max(1e-8f, bounds.extents.sqrMagnitude);
                float center = (point - bounds.center).sqrMagnitude / scale;
                if (outside < bestOutside - 1e-8f ||
                    (Mathf.Abs(outside - bestOutside) <= 1e-8f &&
                     center < bestCenter))
                {
                    bestIndex = i;
                    bestOutside = outside;
                    bestCenter = center;
                }
            }
            return bestIndex;
        }

        static int GetTriangleCount(Mesh mesh)
        {
            if (mesh == null) return 0;
            long triangleCount = 0;
            for (int i = 0; i < mesh.subMeshCount; i++)
                triangleCount += mesh.GetIndexCount(i) / 3;
            return triangleCount >= int.MaxValue ? int.MaxValue : (int)triangleCount;
        }

        static int GetTriangleCount(List<Mesh> meshes)
        {
            long triangleCount = 0;
            for (int i = 0; i < meshes.Count; i++)
            {
                triangleCount += GetTriangleCount(meshes[i]);
                if (triangleCount >= int.MaxValue) return int.MaxValue;
            }
            return (int)triangleCount;
        }

        static void DestroyGeneratedMeshes(List<Mesh> meshes, Mesh sourceMesh)
        {
            for (int i = 0; i < meshes.Count; i++)
            {
                Mesh mesh = meshes[i];
                if (mesh != null && mesh != sourceMesh) Destroy(mesh);
            }
        }

        /// <summary>
        /// Cooks every collider mesh of one split on worker threads. Assigning sharedMesh
        /// to a MeshCollider cooks the hull on the spot, one after another on the main
        /// thread; Physics.BakeMesh is explicitly safe off it, and the assignment later
        /// picks up the finished result. Same frame, same order, a fraction of the stall.
        /// </summary>
        static void PrebakeColliderMeshes(List<Mesh> colliderMeshes)
        {
            if (colliderMeshes == null || colliderMeshes.Count < 2) return;

            int count = colliderMeshes.Count;
            // BakeMesh needs the mesh's real id, so this is one of the few places that
            // cannot fall back to GetHashCode. Unity 6000.3 moved the API to EntityId.
#if UNITY_6000_3_OR_NEWER
            var ids = new EntityId[count];
            for (int i = 0; i < count; i++)
                ids[i] = colliderMeshes[i] != null ? colliderMeshes[i].GetEntityId() : EntityId.None;
#else
            var ids = new int[count];
            for (int i = 0; i < count; i++)
                ids[i] = colliderMeshes[i] != null ? colliderMeshes[i].GetInstanceID() : 0;
#endif

            try
            {
                System.Threading.Tasks.Parallel.For(0, count, i =>
                {
#if UNITY_6000_3_OR_NEWER
                    if (ids[i] != EntityId.None) Physics.BakeMesh(ids[i], true);
#else
                    if (ids[i] != 0) Physics.BakeMesh(ids[i], true);
#endif
                });
            }
            catch (Exception)
            {
                // Cooking stays correct without the prebake: the MeshCollider assignment
                // below does it anyway. Never let a threading failure lose the split.
            }
        }

        static void AddSimpleConvexCollider(GameObject child, Mesh colliderMesh)
        {
            var collider = child.AddComponent<MeshCollider>();
            collider.cookingOptions = MeshColliderCookingOptions.EnableMeshCleaning |
                                      MeshColliderCookingOptions.WeldColocatedVertices |
                                      MeshColliderCookingOptions.UseFastMidphase;
            collider.convex = true;
            collider.sharedMesh = colliderMesh;
            DestructibleRoot.ApplyDefaultDebrisContactMaterial(collider);
        }

        internal static void ConfigureRuntimeDebrisBody(Rigidbody body,
            DestructionProfile profile)
        {
            if (body == null || profile == null) return;
#if UNITY_6000_0_OR_NEWER
            body.linearDamping = profile.Drag;
            body.angularDamping = profile.AngularDrag;
#else
            body.drag = profile.Drag;
            body.angularDrag = profile.AngularDrag;
#endif
            body.useGravity = profile.UseGravity;
        }

        // Reused across every collider build: mesh.vertices hands out a fresh array each
        // time, and one split asks for it once per piece, every time a piece is cut.
        static readonly List<Vector3> s_colliderVertices = new List<Vector3>(256);

        static Mesh BuildInsetTetrahedronCollider(Mesh source)
        {
            if (source == null) return null;
            source.GetVertices(s_colliderVertices);
            List<Vector3> vertices = s_colliderVertices;
            if (vertices.Count < 4) return null;

            Bounds bounds = source.bounds;
            float diagonal = bounds.size.magnitude;
            if (float.IsNaN(diagonal) || float.IsInfinity(diagonal) ||
                diagonal < 0.00001f) return null;

            int first = 0;
            float firstDistance = (vertices[0] - bounds.center).sqrMagnitude;
            for (int i = 1; i < vertices.Count; i++)
            {
                float distance = (vertices[i] - bounds.center).sqrMagnitude;
                if (distance <= firstDistance) continue;
                firstDistance = distance;
                first = i;
            }

            int second = first == 0 ? 1 : 0;
            float pairDistance = (vertices[second] - vertices[first]).sqrMagnitude;
            for (int i = 0; i < vertices.Count; i++)
            {
                float distance = (vertices[i] - vertices[first]).sqrMagnitude;
                if (distance <= pairDistance) continue;
                pairDistance = distance;
                second = i;
            }

            float minimumHeight = Mathf.Max(0.00001f, diagonal * 0.001f);
            if (pairDistance < minimumHeight * minimumHeight) return null;
            Vector3 line = vertices[second] - vertices[first];
            float lineLengthSquared = line.sqrMagnitude;

            int third = -1;
            float lineDistanceSquared = 0f;
            for (int i = 0; i < vertices.Count; i++)
            {
                float distance = Vector3.Cross(line,
                    vertices[i] - vertices[first]).sqrMagnitude / lineLengthSquared;
                if (distance <= lineDistanceSquared) continue;
                lineDistanceSquared = distance;
                third = i;
            }
            if (third < 0 || lineDistanceSquared < minimumHeight * minimumHeight)
                return null;

            Vector3 planeNormal = Vector3.Cross(
                vertices[second] - vertices[first],
                vertices[third] - vertices[first]);
            float planeNormalLength = planeNormal.magnitude;
            if (planeNormalLength < minimumHeight * minimumHeight) return null;
            planeNormal /= planeNormalLength;

            int fourth = -1;
            float planeDistance = 0f;
            for (int i = 0; i < vertices.Count; i++)
            {
                float distance = Mathf.Abs(Vector3.Dot(planeNormal,
                    vertices[i] - vertices[first]));
                if (distance <= planeDistance) continue;
                planeDistance = distance;
                fourth = i;
            }
            if (fourth < 0 || planeDistance < minimumHeight) return null;

            var points = new[]
            {
                vertices[first], vertices[second], vertices[third], vertices[fourth]
            };
            Vector3 centroid = (points[0] + points[1] + points[2] + points[3]) * 0.25f;
            for (int i = 0; i < points.Length; i++)
                points[i] = Vector3.Lerp(centroid, points[i], 0.9f);

            var triangles = new int[12];
            int triangleIndex = 0;
            AddOutwardTetrahedronFace(points, triangles, ref triangleIndex,
                0, 1, 2, 3);
            AddOutwardTetrahedronFace(points, triangles, ref triangleIndex,
                0, 3, 1, 2);
            AddOutwardTetrahedronFace(points, triangles, ref triangleIndex,
                0, 2, 3, 1);
            AddOutwardTetrahedronFace(points, triangles, ref triangleIndex,
                1, 3, 2, 0);

            var colliderMesh = new Mesh
            {
                name = source.name + " Inset Tetra Collider",
                hideFlags = HideFlags.DontSave
            };
            colliderMesh.vertices = points;
            colliderMesh.triangles = triangles;
            colliderMesh.RecalculateBounds();
            return colliderMesh;
        }

        static Mesh BuildFallbackConvexCollider(Mesh source)
        {
            if (source == null) return null;
            Bounds b = source.bounds;
            Vector3 min = b.min;
            Vector3 max = b.max;
            Vector3[] verts = new Vector3[8]
            {
                new Vector3(min.x, min.y, min.z),
                new Vector3(max.x, min.y, min.z),
                new Vector3(max.x, max.y, min.z),
                new Vector3(min.x, max.y, min.z),
                new Vector3(min.x, min.y, max.z),
                new Vector3(max.x, min.y, max.z),
                new Vector3(max.x, max.y, max.z),
                new Vector3(min.x, max.y, max.z)
            };
            int[] tris = new int[36]
            {
                0, 2, 1, 0, 3, 2,
                4, 5, 6, 4, 6, 7,
                0, 1, 5, 0, 5, 4,
                3, 6, 2, 3, 7, 6,
                0, 4, 7, 0, 7, 3,
                1, 2, 6, 1, 6, 5
            };
            var colliderMesh = new Mesh
            {
                name = source.name + " Fallback Box Collider",
                hideFlags = HideFlags.DontSave
            };
            colliderMesh.vertices = verts;
            colliderMesh.triangles = tris;
            colliderMesh.RecalculateBounds();
            return colliderMesh;
        }

        static void AddOutwardTetrahedronFace(Vector3[] vertices,
            int[] triangles, ref int index, int a, int b, int c, int opposite)
        {
            Vector3 normal = Vector3.Cross(vertices[b] - vertices[a],
                vertices[c] - vertices[a]);
            if (Vector3.Dot(normal, vertices[opposite] - vertices[a]) > 0f)
            {
                int swap = b;
                b = c;
                c = swap;
            }
            triangles[index++] = a;
            triangles[index++] = b;
            triangles[index++] = c;
        }

        internal void NotifyPieceDisappeared(RuntimeSplitPiece piece)
        {
            ReleaseRuntimePiece(piece);
        }

        internal bool TryReportRuntimeGroundLanding(RuntimeSplitPiece piece,
            Collision collision,
            float highestWorldY, float downwardSpeed, float size)
        {
            DestructionProfile profile = m_root != null ? m_root.Profile : null;
            if (m_root == null || profile == null || piece == null ||
                profile.Pattern == FracturePattern.RadialGlass || collision == null)
                return false;

            Rigidbody otherBody = collision.rigidbody;
            if (otherBody != null && !otherBody.isKinematic) return false;
            Transform other = collision.collider != null
                ? collision.collider.transform : null;
            if (other == null || (m_runtimeRoot != null && other.IsChildOf(m_runtimeRoot)))
                return false;
            DestructibleRoot otherRoot = other.GetComponentInParent<DestructibleRoot>();
            if (otherRoot == m_root) return false;

            Vector3 point = collision.contactCount > 0
                ? collision.GetContact(0).point : other.position;
            Vector3 normal = collision.contactCount > 0
                ? collision.GetContact(0).normal : Vector3.up;
            if (Mathf.Abs(Vector3.Dot(normal.normalized, Vector3.up)) < 0.55f ||
                downwardSpeed < Mathf.Max(0f, profile.FragmentImpactMinSpeed) ||
                highestWorldY - point.y < Mathf.Max(0f, profile.FragmentImpactMinDropHeight))
                return false;

            if (profile.FragmentImpactEffects)
                m_root.ReportFragmentImpact(-1, point, downwardSpeed, size);

            // Runtime children follow the same bounded landing path as prebaked
            // fragments. The global allowance still caps the whole collapse at
            // eight accepted automatic splits and Update performs one per frame.
            ProcessGroundLanding(piece.gameObject, point, normal, downwardSpeed);
            return true;
        }

        internal void ReleaseRuntimePiece(RuntimeSplitPiece piece)
        {
            if (piece == null) return;
            m_runtimePieces.Remove(piece);
            piece.ReleaseGeneratedMeshAndObject();
        }

        internal void CollectSceneDebris(List<DestructionBudgetManager.SceneDebrisCandidate> output)
        {
            foreach (RuntimeSplitPiece piece in m_runtimePieces)
                if (piece != null && piece.gameObject.activeInHierarchy && !piece.IsDisappearing)
                    output.Add(new DestructionBudgetManager.SceneDebrisCandidate
                    {
                        Root = m_root, RuntimePiece = piece,
                        Volume = DestructionBudgetManager.SceneDebrisCandidate.WorldVolume(piece.Volume, piece.transform),
                        Triangles = piece.TriangleCount
                    });
        }

        internal bool TryGetEffectColor(Vector3 point, out Color color,
            out float squaredDistance)
        {
            color = Color.white;
            squaredDistance = float.PositiveInfinity;
            bool found = false;
            for (int i = 0; i < m_runtimePieces.Count; i++)
            {
                RuntimeSplitPiece piece = m_runtimePieces[i];
                if (piece == null || !piece.gameObject.activeInHierarchy ||
                    !piece.HasEffectColor) continue;
                Renderer renderer = piece.CachedRenderer;
                if (renderer == null || !renderer.enabled) continue;
                float distance = renderer.bounds.SqrDistance(point);
                if (distance >= squaredDistance) continue;
                squaredDistance = distance;
                color = piece.EffectColor;
                found = true;
            }
            return found;
        }

        void OnReset(ResetMode mode)
        {
            for (int i = 0; i < m_disabledOriginals.Count; i++)
                if (m_disabledOriginals[i] != null) m_disabledOriginals[i].SetActive(true);
            m_disabledOriginals.Clear();
            for (int i = 0; i < m_runtimePieces.Count; i++)
            {
                if (m_runtimePieces[i] != null)
                    m_runtimePieces[i].DisposeGeneratedMesh();
            }
            m_runtimePieces.Clear();
            m_groundSplitQueue.Clear();
            m_queuedGroundObjects.Clear();
            m_groundSplitsRemaining = MaxAutomaticGroundSplitsPerCollapse;
            if (m_runtimeRoot != null)
            {
                Destroy(m_runtimeRoot.gameObject);
                m_runtimeRoot = null;
            }
        }

        static float LocalBoundsVolume(Bounds bounds)
        {
            Vector3 size = bounds.size;
            return Mathf.Abs(size.x * size.y * size.z);
        }

        static float EffectiveMinimumSplitVolume(DestructionProfile profile)
        {
            return profile != null
                ? Mathf.Max(0.000001f, profile.RuntimeSplitMinVolume)
                : 0.000001f;
        }

        static float SafeScaleRatio(float world, float parent) =>
            Mathf.Abs(parent) > 0.000001f ? world / parent : world;
    }

    [AddComponentMenu("")]
    internal sealed class RuntimeSplitPiece : MonoBehaviour
    {
        RuntimeFragmentSplitter m_owner;
        internal DestructibleRoot Root => m_owner != null ? m_owner.GetComponent<DestructibleRoot>() : null;
        Rigidbody m_body;
        Collider m_collider;
        Renderer m_renderer;
        MeshFilter m_filter;
        bool m_disappear;
        float m_disappearAt;
        bool m_isDisappearing;
        bool m_needsMeshGroundGuard;
        float m_disappearProgress;
        DebrisDespawnMode m_disappearMode;
        float m_disappearDuration;
        float m_disappearSinkDistance;
        Vector3 m_initialScale;
        float m_highestWorldY;
        bool m_groundDustPlayed;
        float m_lastStaticGroundContactTime = float.NegativeInfinity;

        public int TriangleCount { get; private set; }
        public float Volume { get; private set; }
        internal int Depth { get; private set; }
        internal Color EffectColor { get; private set; } = Color.white;
        internal bool HasEffectColor { get; private set; }
        internal Renderer CachedRenderer => m_renderer;
        internal Rigidbody CachedBody => m_body;
        internal Collider CachedCollider => m_collider;
        internal MeshFilter CachedFilter => m_filter;
        internal bool IsDisappearing => m_isDisappearing;
        internal float ActivatedTime { get; private set; }
        internal bool NeedsMeshGroundGuard => m_needsMeshGroundGuard;
        /// <summary>True while PhysX is already holding this piece against static ground, so
        /// the anti-tunnelling guard must not rewrite its pose or vertical velocity.</summary>
        internal bool HasSustainedGroundContact =>
            FragmentRuntime.IsSustainedGroundContact(m_lastStaticGroundContactTime,
                Time.fixedTime, Time.fixedDeltaTime);

        internal void Initialize(RuntimeFragmentSplitter owner, int depth,
            bool disappear, float lifetime, Color effectColor, bool hasEffectColor, int triangles, float volume)
        {
            m_owner = owner;
            ActivatedTime = Time.time;
            Depth = depth;
            m_disappear = disappear;
            m_disappearAt = Time.time + Mathf.Max(0.25f, lifetime);
            EffectColor = effectColor;
            HasEffectColor = hasEffectColor;
            TriangleCount = triangles;
            Volume = volume;
            m_initialScale = transform.localScale;
            m_highestWorldY = transform.position.y;
            m_groundDustPlayed = false;
            m_body = GetComponent<Rigidbody>();
            m_collider = GetComponent<Collider>();
            m_renderer = GetComponent<Renderer>();
            m_filter = GetComponent<MeshFilter>();
            m_needsMeshGroundGuard =
                FragmentRuntime.RequiresMeshGroundGuard(m_body);
        }

        void OnCollisionEnter(Collision collision)
        {
            if (!m_isDisappearing)
                m_owner?.ProcessCollision(gameObject, collision);

            // Runtime pieces are debris too: one striking a DIFFERENT destructible must
            // register as a Rigidbody impact there (flying rubble breaking a window).
            // Same forwarding rationale as FragmentRuntime; the struck object's damage
            // cooldown absorbs a double report.
            if (!m_isDisappearing && m_owner != null && collision.collider != null &&
                collision.relativeVelocity.sqrMagnitude > 1f)
            {
                var struckRoot =
                    collision.collider.GetComponentInParent<DestructibleRoot>();
                if (struckRoot != null && struckRoot.gameObject != m_owner.gameObject)
                    struckRoot.ReportExternalDebrisImpact(collision, gameObject, m_body);
            }

            ContactPoint groundContact = default(ContactPoint);
            bool staticGround = m_body != null && !m_body.isKinematic &&
                FragmentRuntime.TryGetExternalStaticGroundContact(
                    collision, Root, out groundContact);
            if (staticGround)
            {
#if UNITY_6000_0_OR_NEWER
                m_body.linearVelocity = FragmentRuntime.StabilizeStaticGroundVelocity(
                    m_body.linearVelocity, groundContact.normal);
#else
                m_body.velocity = FragmentRuntime.StabilizeStaticGroundVelocity(
                    m_body.velocity, groundContact.normal);
#endif
            }
            if (m_groundDustPlayed || m_body == null || m_body.isKinematic ||
                m_owner == null) return;
#if UNITY_6000_0_OR_NEWER
            float downwardSpeed = -m_body.linearVelocity.y;
#else
            float downwardSpeed = -m_body.velocity.y;
#endif
            float size = Mathf.Max(0.3f, Mathf.Pow(Mathf.Max(0.000001f, Volume), 1f / 3f));
            if (m_owner.TryReportRuntimeGroundLanding(this, collision,
                    m_highestWorldY, downwardSpeed, size))
                m_groundDustPlayed = true;
        }

        void OnCollisionStay(Collision collision)
        {
            if (m_isDisappearing || m_body == null || m_body.isKinematic ||
                m_body.IsSleeping())
                return;
            if (!FragmentRuntime.TryGetExternalStaticGroundContact(
                    collision, Root, out ContactPoint groundContact))
                return;
            m_lastStaticGroundContactTime = Time.fixedTime;

#if UNITY_6000_0_OR_NEWER
            Vector3 velocity = FragmentRuntime.StabilizeStaticGroundVelocity(
                m_body.linearVelocity, groundContact.normal);
            m_body.linearVelocity = FragmentRuntime.DampStaticGroundTangentialVelocity(
                velocity, groundContact.normal, Time.fixedDeltaTime);
#else
            Vector3 velocity = FragmentRuntime.StabilizeStaticGroundVelocity(
                m_body.velocity, groundContact.normal);
            m_body.velocity = FragmentRuntime.DampStaticGroundTangentialVelocity(
                velocity, groundContact.normal, Time.fixedDeltaTime);
#endif
            float tangentialSpeed = Vector3.ProjectOnPlane(
                velocity, groundContact.normal).magnitude;
            m_body.angularVelocity = FragmentRuntime.DampStaticGroundAngularVelocity(
                m_body.angularVelocity, tangentialSpeed, Time.fixedDeltaTime);
        }

        public void StartDisappearing(DebrisDespawnMode mode = DebrisDespawnMode.ShrinkAndSink,
            float duration = 0.4f, float sinkDistance = 0.06f)
        {
            if (m_isDisappearing) return;
            m_isDisappearing = true;
            m_disappearProgress = 0f;
            m_initialScale = transform.localScale;
            m_disappearMode = mode;
            m_disappearDuration = Mathf.Max(0.01f, duration);
            m_disappearSinkDistance = sinkDistance;
            // Deliberately silent: despawn is housekeeping, not a gameplay event. Debris
            // fading out a minute after the break must not pop particles or sound.
            if (mode == DebrisDespawnMode.Instant) DeactivateAfterSplit();
        }

        /// <summary>
        /// Driven by the splitter's own Update instead of a per-piece MonoBehaviour one.
        /// A collapse can leave hundreds of these alive, and Unity pays the managed call
        /// overhead for every single Update every frame, whether the piece does anything
        /// or not.
        /// </summary>
        internal void Tick(float deltaTime)
        {
            if (m_body != null && !m_body.isKinematic)
                m_highestWorldY = Mathf.Max(m_highestWorldY, transform.position.y);

            if (m_isDisappearing)
            {
                float previousProgress = m_disappearProgress;
                m_disappearProgress = Mathf.Clamp01(m_disappearProgress + deltaTime / m_disappearDuration);
                transform.localScale = Vector3.Lerp(m_initialScale, Vector3.zero, m_disappearProgress);
                if (m_disappearMode == DebrisDespawnMode.ShrinkAndSink)
                    transform.position += Vector3.down * (m_disappearSinkDistance *
                        (m_disappearProgress - previousProgress));
                if (m_disappearProgress >= 1f)
                {
                    m_owner?.NotifyPieceDisappeared(this);
                }
                return;
            }

            if (m_disappear && Time.time >= m_disappearAt && !DestructionBudgetManager.OverridesProfileRemoval(Root))
            {
                StartDisappearing();
            }
        }

        internal void DeactivateAfterSplit()
        {
            if (m_owner != null) m_owner.ReleaseRuntimePiece(this);
            else gameObject.SetActive(false);
        }

        internal void DespawnForSceneBudget(DebrisDespawnMode mode)
        {
            if (m_body != null) m_body.isKinematic = true;
            foreach (Collider collider in GetComponentsInChildren<Collider>()) collider.enabled = false;
            StartDisappearing(mode, FragmentDespawnTransition.DefaultDuration,
                FragmentDespawnTransition.SinkDistance);
        }

        internal void DisposeGeneratedMesh()
        {
            MeshFilter[] childFilters = GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < childFilters.Length; i++)
            {
                MeshFilter childFilter = childFilters[i];
                if (childFilter == null || childFilter.gameObject == gameObject ||
                    childFilter.gameObject.name != "Interior Scatter") continue;
                Mesh scatterMesh = childFilter.sharedMesh;
                childFilter.sharedMesh = null;
                if (scatterMesh != null) DestroyGeneratedObject(scatterMesh);
            }

            MeshFilter filter = GetComponent<MeshFilter>();
            Mesh mesh = filter != null ? filter.sharedMesh : null;
            MeshCollider meshCollider = GetComponent<MeshCollider>();
            Mesh colliderMesh = meshCollider != null ? meshCollider.sharedMesh : null;
            if (meshCollider != null) meshCollider.sharedMesh = null;
            if (filter != null) filter.sharedMesh = null;
            if (colliderMesh != null && colliderMesh != mesh) DestroyGeneratedObject(colliderMesh);
            if (mesh != null) DestroyGeneratedObject(mesh);
        }

        internal void ReleaseGeneratedMeshAndObject()
        {
            DisposeGeneratedMesh();
            gameObject.SetActive(false);
            DestroyGeneratedObject(gameObject);
        }

        static void DestroyGeneratedObject(UnityEngine.Object target)
        {
            if (Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }
    }

    /// <summary>
    /// Bounded runtime mesh clipping. The fast path preserves the source triangles and
    /// accepts only closed manifold outputs; the shared WorkMesh/MeshSlicer path is a
    /// single bounded repair attempt when a complex contour defeats the fast path.
    /// </summary>
    internal static class RuntimeMeshSlicer
    {
        const float Epsilon = 0.00001f;

        struct Vertex
        {
            public Vector3 Position;
            public Vector3 Normal;
            public Vector2 UV;
            public Vector4 Tangent;

            public static Vertex Lerp(Vertex a, Vertex b, float t)
            {
                return new Vertex
                {
                    Position = Vector3.LerpUnclamped(a.Position, b.Position, t),
                    Normal = Vector3.LerpUnclamped(a.Normal, b.Normal, t).normalized,
                    UV = Vector2.LerpUnclamped(a.UV, b.UV, t),
                    Tangent = Vector4.LerpUnclamped(a.Tangent, b.Tangent, t)
                };
            }
        }

        sealed class Builder
        {
            public readonly List<Vector3> Positions = new List<Vector3>();
            public readonly List<Vector3> Normals = new List<Vector3>();
            public readonly List<Vector2> UVs = new List<Vector2>();
            public readonly List<Vector4> Tangents = new List<Vector4>();
            public readonly List<int>[] Indices;

            public Builder(int submeshes)
            {
                Indices = new List<int>[Mathf.Max(1, submeshes)];
                for (int i = 0; i < Indices.Length; i++) Indices[i] = new List<int>();
            }

            public void AddTriangle(Vertex a, Vertex b, Vertex c, int submesh)
            {
                Add(a, submesh); Add(b, submesh); Add(c, submesh);
            }

            void Add(Vertex vertex, int submesh)
            {
                int index = Positions.Count;
                Positions.Add(vertex.Position);
                Normals.Add(vertex.Normal);
                UVs.Add(vertex.UV);
                Tangents.Add(vertex.Tangent);
                Indices[Mathf.Clamp(submesh, 0, Indices.Length - 1)].Add(index);
            }

            public Mesh ToMesh(string name)
            {
                if (Positions.Count < 3) return null;
                var mesh = new Mesh { name = name, hideFlags = HideFlags.DontSave };
                if (Positions.Count > 65535)
                    mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.SetVertices(Positions);
                mesh.SetNormals(Normals);
                mesh.SetUVs(0, UVs);
                mesh.SetTangents(Tangents);
                mesh.subMeshCount = Indices.Length;
                for (int i = 0; i < Indices.Length; i++)
                    mesh.SetTriangles(Indices[i], i, false);
                mesh.RecalculateBounds();
                return mesh;
            }
        }

        internal static List<Mesh> SplitInto(Mesh source, int requested,
            int seed, float uvScale, CutSurfaceUVMode uvMode)
        {
            var output = new List<Mesh>();
            if (source == null || source.vertexCount < 4) return output;

            // Baked fragments are authored as closed meshes, but converting the Unity
            // Mesh back to WorkMesh may expose duplicated UV/normal seam vertices to
            // the strict manifold test. Let the topology-repairing slicer process the
            // input and validate both generated outputs below. Refusing here made many
            // perfectly usable airborne fragments fail silently before the first cut.
            WorkMesh root = WorkMesh.FromMesh(source);

            var result = new List<WorkMesh> { root };
            var random = new System.Random(seed);
            int attempts = 0;
            while (result.Count < requested && attempts++ < requested * 14)
            {
                int targetIndex = LargestWorkMeshIndex(result);
                WorkMesh target = result[targetIndex];
                Bounds bounds = target.ComputeBounds();
                Vector3 normal = LongestAxis(bounds.size);
                normal = (normal + new Vector3(
                    NextSigned(random), NextSigned(random), NextSigned(random)) *
                    0.42f).normalized;
                float offset = NextSigned(random) *
                               0.12f *
                               Mathf.Max(bounds.extents.x, bounds.extents.y, bounds.extents.z);
                var plane = new Plane(normal, bounds.center + normal * offset);
                MeshSlicer.Slice(target, plane, uvScale, uvMode,
                    out WorkMesh positive, out WorkMesh negative,
                    repairCutTopology: true,
                    bakedInteriorJaggedness: 0f,
                    bakedCutDetailStyle: BakedCutDetailStyle.None);
                if (positive == null || negative == null ||
                    !positive.IsClosedManifold() || !negative.IsClosedManifold())
                    continue;

                result[targetIndex] = positive;
                result.Add(negative);
            }
            if (result.Count < 2)
            {
                return output;
            }

            for (int i = 0; i < result.Count; i++)
            {
                WorkMesh piece = result[i];
                if (piece == null || !piece.IsClosedManifold())
                {
                    DestroyOutputMeshes(output);
                    output.Clear();
                    return output;
                }
                Mesh mesh = piece.ToMesh(source.name + $" Runtime {i:00}");
                if (mesh == null || CountTriangles(mesh) < 4)
                {
                    if (mesh != null) UnityEngine.Object.Destroy(mesh);
                    DestroyOutputMeshes(output);
                    output.Clear();
                    return output;
                }
                output.Add(mesh);
            }
            return output;
        }

        internal static List<Mesh> SplitFast(Mesh source, int requested,
            int seed, float uvScale, Vector3 worldScale)
        {
            var result = new List<Mesh>();
            if (source == null || source.vertexCount < 4) return result;
            result.Add(source);
            var random = new System.Random(seed);
            int targetCount = Mathf.Clamp(requested, 2, 4);

            while (result.Count < targetCount)
            {
                int targetIndex = LargestMeshIndex(result);
                Mesh target = result[targetIndex];
                Bounds bounds = target.bounds;

                // Directly cut along the longest local axis for a clean split
                Vector3 normal = LongestAxis(bounds.size);
                // Introduce a tiny random jitter
                normal = (normal + new Vector3(
                    NextSigned(random), NextSigned(random), NextSigned(random)) *
                    0.05f).normalized;

                var plane = new Plane(normal, bounds.center);
                Mesh positive = null;
                Mesh negative = null;

                // Try the robust cutter first (with repairCutTopology and capping)
                bool sliced = TrySingleRepairCut(target, plane, uvScale, worldScale, out positive, out negative);
                if (!sliced)
                {
                    // Fallback: simple slice without strict manifold checks
                    sliced = TrySlice(target, plane, uvScale, worldScale, out positive, out negative, validateResult: false);
                }

                if (!sliced)
                    break;

                result[targetIndex] = positive;
                result.Add(negative);
                if (target != source) UnityEngine.Object.Destroy(target);
            }

            if (result.Count < 2)
            {
                result.Clear();
                return result;
            }
            return result;
        }

        static bool TrySingleRepairCut(Mesh source, Plane plane, float uvScale,
            Vector3 worldScale, out Mesh positiveMesh, out Mesh negativeMesh)
        {
            positiveMesh = null;
            negativeMesh = null;
            if (source == null) return false;

            int interiorSubmesh = Mathf.Max(0, source.subMeshCount - 1);
            float texelDensity = EstimateMeshTexelDensity(source,
                interiorSubmesh, uvScale, worldScale);
            float localRepairScale = texelDensity * MeanAbsoluteScale(worldScale);
            WorkMesh work = WorkMesh.FromMesh(source);
            MeshSlicer.Slice(work, plane, localRepairScale,
                CutSurfaceUVMode.PlanarProjection,
                out WorkMesh positive, out WorkMesh negative,
                repairCutTopology: true,
                bakedInteriorJaggedness: 0f,
                bakedCutDetailStyle: BakedCutDetailStyle.None);
            if (positive == null || negative == null ||
                positive.TriangleCount < 4 || negative.TriangleCount < 4)
                return false;

            positiveMesh = positive.ToMesh(source.name + " Runtime Repair A");
            negativeMesh = negative.ToMesh(source.name + " Runtime Repair B");
            if (positiveMesh != null && negativeMesh != null)
            {
                MatchCutFaceTexelDensity(positiveMesh, plane, worldScale,
                    texelDensity, interiorSubmesh);
                MatchCutFaceTexelDensity(negativeMesh, plane, worldScale,
                    texelDensity, interiorSubmesh);
                positiveMesh = SanitizeClosedRuntimePiece(positiveMesh,
                    source.name + " Runtime Repair A");
                negativeMesh = SanitizeClosedRuntimePiece(negativeMesh,
                    source.name + " Runtime Repair B");
                if (positiveMesh != null && negativeMesh != null)
                    return true;
            }
            if (positiveMesh != null) UnityEngine.Object.Destroy(positiveMesh);
            if (negativeMesh != null) UnityEngine.Object.Destroy(negativeMesh);
            positiveMesh = null;
            negativeMesh = null;
            return false;
        }

        static Vector3 AxisBySizeRank(Vector3 size, int rank)
        {
            int longest = size.x >= size.y && size.x >= size.z
                ? 0 : size.y >= size.z ? 1 : 2;
            int shortest = size.x <= size.y && size.x <= size.z
                ? 0 : size.y <= size.z ? 1 : 2;
            int axis = longest == shortest ? Mathf.Clamp(rank, 0, 2) :
                rank <= 0 ? longest : rank == 1
                    ? 3 - longest - shortest : shortest;
            return axis == 0 ? Vector3.right :
                   axis == 1 ? Vector3.up : Vector3.forward;
        }

        static int LargestWorkMeshIndex(List<WorkMesh> meshes)
        {
            int index = 0;
            float best = float.NegativeInfinity;
            for (int i = 0; i < meshes.Count; i++)
            {
                WorkMesh mesh = meshes[i];
                float volume = mesh != null
                    ? Mathf.Abs(mesh.ComputeVolume())
                    : 0f;
                if (volume <= 0.0000001f && mesh != null)
                {
                    Vector3 size = mesh.ComputeBounds().size;
                    volume = Mathf.Abs(size.x * size.y * size.z);
                }
                if (volume <= best) continue;
                best = volume;
                index = i;
            }
            return index;
        }

        static int CountTriangles(Mesh mesh)
        {
            if (mesh == null) return 0;
            long count = 0;
            for (int i = 0; i < mesh.subMeshCount; i++)
                count += mesh.GetIndexCount(i) / 3;
            return count >= int.MaxValue ? int.MaxValue : (int)count;
        }

        static void DestroyOutputMeshes(List<Mesh> meshes)
        {
            for (int i = 0; i < meshes.Count; i++)
                if (meshes[i] != null) UnityEngine.Object.Destroy(meshes[i]);
        }

        struct TriRef
        {
            public int Submesh;
            public int Index0, Index1, Index2;
        }

        static List<Mesh> SeparateConnectedComponents(Mesh mesh)
        {
            var result = new List<Mesh>();
            if (mesh == null || mesh.vertexCount < 3) return result;

            Vector3[] positions = mesh.vertices;
            Vector3[] normals = mesh.normals;
            Vector2[] uvs = mesh.uv;
            Vector4[] tangents = mesh.tangents;
            int submeshCount = mesh.subMeshCount;

            var allTriangles = new List<TriRef>();
            for (int s = 0; s < submeshCount; s++)
            {
                int[] tris = mesh.GetTriangles(s);
                if (tris == null) continue;
                for (int i = 0; i + 2 < tris.Length; i += 3)
                {
                    allTriangles.Add(new TriRef
                    {
                        Submesh = s,
                        Index0 = tris[i],
                        Index1 = tris[i + 1],
                        Index2 = tris[i + 2]
                    });
                }
            }

            if (allTriangles.Count == 0) return result;

            int vertexCount = positions.Length;
            int[] welded = new int[vertexCount];
            var positionMap = new Dictionary<Vector3Int, int>();
            Bounds bounds = mesh.bounds;
            float scale = Mathf.Max(bounds.size.magnitude * 1e-5f, 1e-6f);

            for (int i = 0; i < vertexCount; i++)
            {
                Vector3 p = positions[i];
                Vector3Int cell = new Vector3Int(
                    Mathf.RoundToInt(p.x / scale),
                    Mathf.RoundToInt(p.y / scale),
                    Mathf.RoundToInt(p.z / scale));
                if (!positionMap.TryGetValue(cell, out int w))
                {
                    w = i;
                    positionMap[cell] = w;
                }
                welded[i] = w;
            }

            int triCount = allTriangles.Count;
            var vertToTris = new Dictionary<int, List<int>>();
            for (int t = 0; t < triCount; t++)
            {
                TriRef tri = allTriangles[t];
                int w0 = welded[tri.Index0];
                int w1 = welded[tri.Index1];
                int w2 = welded[tri.Index2];

                if (!vertToTris.TryGetValue(w0, out var list0)) { list0 = new List<int>(); vertToTris[w0] = list0; }
                list0.Add(t);
                if (!vertToTris.TryGetValue(w1, out var list1)) { list1 = new List<int>(); vertToTris[w1] = list1; }
                list1.Add(t);
                if (!vertToTris.TryGetValue(w2, out var list2)) { list2 = new List<int>(); vertToTris[w2] = list2; }
                list2.Add(t);
            }

            bool[] visitedTriangles = new bool[triCount];
            var queue = new Queue<int>();

            for (int t = 0; t < triCount; t++)
            {
                if (visitedTriangles[t]) continue;

                var islandTriangles = new List<TriRef>();
                queue.Enqueue(t);
                visitedTriangles[t] = true;

                while (queue.Count > 0)
                {
                    int currIdx = queue.Dequeue();
                    TriRef currTri = allTriangles[currIdx];
                    islandTriangles.Add(currTri);

                    int first = welded[currTri.Index0];
                    int second = welded[currTri.Index1];
                    int third = welded[currTri.Index2];
                    if (vertToTris.TryGetValue(first, out var firstTriangles))
                    {
                        for (int k = 0; k < firstTriangles.Count; k++)
                        {
                            int next = firstTriangles[k];
                            if (!visitedTriangles[next])
                            {
                                visitedTriangles[next] = true;
                                queue.Enqueue(next);
                            }
                        }
                    }
                    if (vertToTris.TryGetValue(second, out var secondTriangles))
                    {
                        for (int k = 0; k < secondTriangles.Count; k++)
                        {
                            int next = secondTriangles[k];
                            if (!visitedTriangles[next])
                            {
                                visitedTriangles[next] = true;
                                queue.Enqueue(next);
                            }
                        }
                    }
                    if (vertToTris.TryGetValue(third, out var thirdTriangles))
                    {
                        for (int k = 0; k < thirdTriangles.Count; k++)
                        {
                            int next = thirdTriangles[k];
                            if (!visitedTriangles[next])
                            {
                                visitedTriangles[next] = true;
                                queue.Enqueue(next);
                            }
                        }
                    }
                }

                Mesh islandMesh = ExtractIslandMesh(mesh, islandTriangles, submeshCount, positions, normals, uvs, tangents);
                if (islandMesh != null)
                    result.Add(islandMesh);
            }

            if (mesh != null && result.Count > 1)
                UnityEngine.Object.Destroy(mesh);

            return result.Count > 0 ? result : new List<Mesh> { mesh };
        }

        static Mesh ExtractIslandMesh(Mesh source, List<TriRef> islandTriangles, int submeshCount,
            Vector3[] positions, Vector3[] normals, Vector2[] uvs, Vector4[] tangents)
        {
            if (islandTriangles == null || islandTriangles.Count == 0) return null;

            var newPositions = new List<Vector3>();
            var newNormals = new List<Vector3>();
            var newUVs = new List<Vector2>();
            var newTangents = new List<Vector4>();
            var submeshTriangles = new List<int>[submeshCount];
            for (int s = 0; s < submeshCount; s++) submeshTriangles[s] = new List<int>();

            var vMap = new Dictionary<int, int>();

            for (int i = 0; i < islandTriangles.Count; i++)
            {
                TriRef tri = islandTriangles[i];
                int s = Mathf.Clamp(tri.Submesh, 0, submeshCount - 1);
                int[] verts = new int[] { tri.Index0, tri.Index1, tri.Index2 };

                for (int j = 0; j < 3; j++)
                {
                    int srcV = verts[j];
                    if (!vMap.TryGetValue(srcV, out int dstV))
                    {
                        dstV = newPositions.Count;
                        vMap[srcV] = dstV;
                        newPositions.Add(positions[srcV]);
                        if (normals != null && srcV < normals.Length) newNormals.Add(normals[srcV]);
                        if (uvs != null && srcV < uvs.Length) newUVs.Add(uvs[srcV]);
                        if (tangents != null && srcV < tangents.Length) newTangents.Add(tangents[srcV]);
                    }
                    submeshTriangles[s].Add(dstV);
                }
            }

            var mesh = new Mesh { name = source.name + "_Island", hideFlags = HideFlags.DontSave };
            mesh.SetVertices(newPositions);
            if (newNormals.Count == newPositions.Count) mesh.SetNormals(newNormals);
            if (newUVs.Count == newPositions.Count) mesh.SetUVs(0, newUVs);
            if (newTangents.Count == newPositions.Count) mesh.SetTangents(newTangents);

            mesh.subMeshCount = submeshCount;
            for (int s = 0; s < submeshCount; s++)
            {
                mesh.SetTriangles(submeshTriangles[s], s, false);
            }
            mesh.RecalculateBounds();
            return mesh;
        }

        static int LargestMeshIndex(List<Mesh> meshes, int excludedIndex = -1)
        {
            int index = -1;
            float largest = -1f;
            for (int i = 0; i < meshes.Count; i++)
            {
                if (i == excludedIndex || meshes[i] == null) continue;
                Vector3 size = meshes[i].bounds.size;
                float volume = size.x * size.y * size.z;
                if (volume > largest) { largest = volume; index = i; }
            }
            return index;
        }

        static Vector3 LongestAxis(Vector3 size)
        {
            if (size.x >= size.y && size.x >= size.z) return Vector3.right;
            return size.y >= size.z ? Vector3.up : Vector3.forward;
        }

        static float NextSigned(System.Random random) => (float)random.NextDouble() * 2f - 1f;

        struct CutSegment
        {
            public Vector3 A;
            public Vector3 B;
            public Vector2 UVA;
            public Vector2 UVB;
        }

        struct CutPlaneUVBounds
        {
            public float uMin, uMax, vMin, vMax;
            public float pXMin, pXMax, pYMin, pYMax;
            public bool valid;
        }

        static float EstimateMeshTexelDensity(Mesh mesh, int preferredSubmesh,
            float fallbackLocalScale, Vector3 worldScale)
        {
            if (mesh == null || mesh.vertexCount < 3)
                return Mathf.Max(0.001f, fallbackLocalScale /
                    MeanAbsoluteScale(worldScale));

            Vector3[] positions = mesh.vertices;
            Vector2[] uvs = mesh.uv;
            if (uvs == null || uvs.Length != positions.Length)
                return Mathf.Max(0.001f, fallbackLocalScale /
                    MeanAbsoluteScale(worldScale));

            Vector3 scale = AbsoluteSafeScale(worldScale);
            double worldArea = 0.0;
            double uvArea = 0.0;

            void AccumulateSubmesh(int submesh)
            {
                int[] triangles = mesh.GetTriangles(submesh);
                for (int i = 0; i + 2 < triangles.Length; i += 3)
                {
                    int ia = triangles[i];
                    int ib = triangles[i + 1];
                    int ic = triangles[i + 2];
                    Vector3 ab = Vector3.Scale(positions[ib] - positions[ia], scale);
                    Vector3 ac = Vector3.Scale(positions[ic] - positions[ia], scale);
                    float doubleWorldArea = Vector3.Cross(ab, ac).magnitude;
                    if (doubleWorldArea <= 1e-10f) continue;

                    Vector2 uvAB = uvs[ib] - uvs[ia];
                    Vector2 uvAC = uvs[ic] - uvs[ia];
                    float doubleUVArea = Mathf.Abs(uvAB.x * uvAC.y -
                                                   uvAB.y * uvAC.x);
                    if (doubleUVArea <= 1e-12f) continue;

                    float triangleDensity = Mathf.Sqrt(doubleUVArea /
                                                        doubleWorldArea);
                    if (triangleDensity < 0.001f || triangleDensity > 1000f)
                        continue;
                    worldArea += doubleWorldArea;
                    uvArea += doubleUVArea;
                }
            }

            int submeshCount = Mathf.Max(1, mesh.subMeshCount);
            AccumulateSubmesh(Mathf.Clamp(preferredSubmesh, 0,
                submeshCount - 1));

            // Older or imported fragments may not contain a dedicated interior island.
            // In that case derive the density from all preserved source faces.
            if (worldArea <= 1e-10 || uvArea <= 1e-12)
            {
                worldArea = 0.0;
                uvArea = 0.0;
                for (int submesh = 0; submesh < submeshCount; submesh++)
                    AccumulateSubmesh(submesh);
            }

            if (worldArea <= 1e-10 || uvArea <= 1e-12)
                return Mathf.Max(0.001f, fallbackLocalScale /
                    MeanAbsoluteScale(scale));
            return Mathf.Clamp(Mathf.Sqrt((float)(uvArea / worldArea)),
                0.001f, 1000f);
        }

        static Vector3 AbsoluteSafeScale(Vector3 scale) => new Vector3(
            Mathf.Max(1e-5f, Mathf.Abs(scale.x)),
            Mathf.Max(1e-5f, Mathf.Abs(scale.y)),
            Mathf.Max(1e-5f, Mathf.Abs(scale.z)));

        static float MeanAbsoluteScale(Vector3 scale)
        {
            Vector3 safe = AbsoluteSafeScale(scale);
            return (safe.x + safe.y + safe.z) / 3f;
        }

        static void BuildWorldTexelProjection(Vector3 localNormal,
            Vector3 worldScale, out Vector3 projectionU, out Vector3 projectionV,
            out Vector3 localTangent)
        {
            Vector3 scale = AbsoluteSafeScale(worldScale);
            Vector3 worldNormal = new Vector3(localNormal.x / scale.x,
                localNormal.y / scale.y, localNormal.z / scale.z).normalized;
            Vector3 stableAxis = Mathf.Abs(worldNormal.y) < 0.9f
                ? Vector3.up : Vector3.right;
            Vector3 worldU = Vector3.Cross(worldNormal, stableAxis).normalized;
            Vector3 worldV = Vector3.Cross(worldNormal, worldU).normalized;

            // dot(localPosition, projection) is a distance in scaled world space.
            projectionU = Vector3.Scale(worldU, scale);
            projectionV = Vector3.Scale(worldV, scale);
            localTangent = new Vector3(worldU.x / scale.x,
                worldU.y / scale.y, worldU.z / scale.z).normalized;
        }

        static void MatchCutFaceTexelDensity(Mesh mesh, Plane localPlane,
            Vector3 worldScale, float texelDensity, int interiorSubmesh)
        {
            if (mesh == null || mesh.vertexCount < 3 || mesh.subMeshCount < 1)
                return;

            Vector3[] positions = mesh.vertices;
            Vector3[] normals = mesh.normals;
            Vector2[] uvs = mesh.uv;
            Vector4[] tangents = mesh.tangents;
            if (uvs == null || uvs.Length != positions.Length) return;

            int submesh = Mathf.Clamp(interiorSubmesh, 0, mesh.subMeshCount - 1);
            int[] triangles = mesh.GetTriangles(submesh);
            float planeEpsilon = Mathf.Max(mesh.bounds.size.magnitude * 1e-5f,
                1e-6f);
            var capVertices = new HashSet<int>();
            for (int i = 0; i < triangles.Length; i++)
            {
                int index = triangles[i];
                if (index < 0 || index >= positions.Length) continue;
                if (Mathf.Abs(localPlane.GetDistanceToPoint(positions[index])) >
                    planeEpsilon) continue;
                if (normals != null && normals.Length == positions.Length &&
                    Mathf.Abs(Vector3.Dot(normals[index].normalized,
                        localPlane.normal)) < 0.98f) continue;
                capVertices.Add(index);
            }
            if (capVertices.Count < 3) return;

            BuildWorldTexelProjection(localPlane.normal, worldScale,
                out Vector3 projectionU, out Vector3 projectionV,
                out Vector3 localTangent);
            float pUMin = float.MaxValue, pUMax = float.MinValue;
            float pVMin = float.MaxValue, pVMax = float.MinValue;
            float uvUMin = float.MaxValue, uvUMax = float.MinValue;
            float uvVMin = float.MaxValue, uvVMax = float.MinValue;
            foreach (int index in capVertices)
            {
                float pU = Vector3.Dot(positions[index], projectionU);
                float pV = Vector3.Dot(positions[index], projectionV);
                pUMin = Mathf.Min(pUMin, pU); pUMax = Mathf.Max(pUMax, pU);
                pVMin = Mathf.Min(pVMin, pV); pVMax = Mathf.Max(pVMax, pV);
                uvUMin = Mathf.Min(uvUMin, uvs[index].x);
                uvUMax = Mathf.Max(uvUMax, uvs[index].x);
                uvVMin = Mathf.Min(uvVMin, uvs[index].y);
                uvVMax = Mathf.Max(uvVMax, uvs[index].y);
            }

            float pUCenter = (pUMin + pUMax) * 0.5f;
            float pVCenter = (pVMin + pVMax) * 0.5f;
            float uvUCenter = (uvUMin + uvUMax) * 0.5f;
            float uvVCenter = (uvVMin + uvVMax) * 0.5f;
            foreach (int index in capVertices)
            {
                float pU = Vector3.Dot(positions[index], projectionU);
                float pV = Vector3.Dot(positions[index], projectionV);
                uvs[index] = new Vector2(
                    uvUCenter + (pU - pUCenter) * texelDensity,
                    uvVCenter + (pV - pVCenter) * texelDensity);
                if (tangents != null && tangents.Length == positions.Length)
                    tangents[index] = new Vector4(localTangent.x,
                        localTangent.y, localTangent.z, 1f);
            }
            mesh.uv = uvs;
            if (tangents != null && tangents.Length == positions.Length)
                mesh.tangents = tangents;
        }

        static CutPlaneUVBounds ComputeCutPlaneUVBounds(List<CutSegment> segments,
            Vector3 u, Vector3 v, float texelDensity)
        {
            var b = new CutPlaneUVBounds();
            if (segments == null || segments.Count == 0) return b;

            b.uMin = float.MaxValue; b.uMax = float.MinValue;
            b.vMin = float.MaxValue; b.vMax = float.MinValue;
            b.pXMin = float.MaxValue; b.pXMax = float.MinValue;
            b.pYMin = float.MaxValue; b.pYMax = float.MinValue;

            for (int i = 0; i < segments.Count; i++)
            {
                CutSegment seg = segments[i];

                b.uMin = Mathf.Min(b.uMin, Mathf.Min(seg.UVA.x, seg.UVB.x));
                b.uMax = Mathf.Max(b.uMax, Mathf.Max(seg.UVA.x, seg.UVB.x));
                b.vMin = Mathf.Min(b.vMin, Mathf.Min(seg.UVA.y, seg.UVB.y));
                b.vMax = Mathf.Max(b.vMax, Mathf.Max(seg.UVA.y, seg.UVB.y));

                float segAx = Vector3.Dot(seg.A, u);
                float segAy = Vector3.Dot(seg.A, v);
                float segBx = Vector3.Dot(seg.B, u);
                float segBy = Vector3.Dot(seg.B, v);

                b.pXMin = Mathf.Min(b.pXMin, Mathf.Min(segAx, segBx));
                b.pXMax = Mathf.Max(b.pXMax, Mathf.Max(segAx, segBx));
                b.pYMin = Mathf.Min(b.pYMin, Mathf.Min(segAy, segBy));
                b.pYMax = Mathf.Max(b.pYMax, Mathf.Max(segAy, segBy));

            }

            texelDensity = Mathf.Clamp(texelDensity, 0.001f, 1000f);

            float pWidth = Mathf.Max(1e-4f, b.pXMax - b.pXMin);
            float pHeight = Mathf.Max(1e-4f, b.pYMax - b.pYMin);

            float targetUSpan = pWidth * texelDensity;
            float targetVSpan = pHeight * texelDensity;

            float uCenter = (b.uMin + b.uMax) * 0.5f;
            float vCenter = (b.vMin + b.vMax) * 0.5f;

            b.uMin = uCenter - targetUSpan * 0.5f;
            b.uMax = uCenter + targetUSpan * 0.5f;

            b.vMin = vCenter - targetVSpan * 0.5f;
            b.vMax = vCenter + targetVSpan * 0.5f;

            b.valid = true;
            return b;
        }

        static Vector2 SampleExtrudedUV(Vector3 point, Vector3 u, Vector3 v, CutPlaneUVBounds b)
        {
            if (!b.valid) return Vector2.zero;

            float px = Vector3.Dot(point, u);
            float py = Vector3.Dot(point, v);

            float rangeX = Mathf.Max(1e-5f, b.pXMax - b.pXMin);
            float rangeY = Mathf.Max(1e-5f, b.pYMax - b.pYMin);

            float tx = Mathf.Clamp01((px - b.pXMin) / rangeX);
            float ty = Mathf.Clamp01((py - b.pYMin) / rangeY);

            float finalU = Mathf.Lerp(b.uMin, b.uMax, tx);
            float finalV = Mathf.Lerp(b.vMin, b.vMax, ty);

            return new Vector2(finalU, finalV);
        }

        static bool TrySlice(Mesh source, Plane plane, float uvScale,
            Vector3 worldScale, out Mesh positive, out Mesh negative,
            bool validateResult = true)
        {
            positive = null;
            negative = null;
            if (source == null || source.vertexCount < 3) return false;
            int submeshes = Mathf.Max(1, source.subMeshCount);
            var pos = new Builder(submeshes);
            var neg = new Builder(submeshes);
            var cutSegments = new List<CutSegment>();
            // Intersections of the same source edge differ only by floating-point
            // roundoff. A percent-scale tolerance can jump from one nearby contour to
            // another and produces the characteristic long cap spike. Keep the weld
            // local to the mesh precision instead.
            float capWeldCell = Mathf.Max(
                source.bounds.size.magnitude * 2e-5f, 1e-6f);
            var clipInput = new List<Vertex>(3);
            var clipOutput = new List<Vertex>(4);
            var clipIntersections = new List<Vertex>(2);
            Vector3[] positions = source.vertices;
            Vector3[] normals = source.normals;
            Vector2[] uvs = source.uv;
            Vector4[] tangents = source.tangents;
            int interiorSubmesh = Mathf.Max(0, submeshes - 1);
            float texelDensity = EstimateMeshTexelDensity(source,
                interiorSubmesh, uvScale, worldScale);

            Vertex Read(int index) => new Vertex
            {
                Position = positions[index],
                Normal = normals != null && normals.Length == positions.Length
                    ? normals[index] : Vector3.up,
                UV = uvs != null && uvs.Length == positions.Length
                    ? uvs[index] : Vector2.zero,
                Tangent = tangents != null && tangents.Length == positions.Length
                    ? tangents[index] : new Vector4(1f, 0f, 0f, 1f)
            };

            for (int submesh = 0; submesh < submeshes; submesh++)
            {
                int[] triangles = source.GetTriangles(submesh);
                for (int i = 0; i + 2 < triangles.Length; i += 3)
                {
                    Vertex a = Read(triangles[i]);
                    Vertex b = Read(triangles[i + 1]);
                    Vertex c = Read(triangles[i + 2]);
                    AddClippedTriangle(a, b, c, plane, true, pos, submesh,
                        clipInput, clipOutput, clipIntersections, cutSegments);
                    AddClippedTriangle(a, b, c, plane, false, neg, submesh,
                        clipInput, clipOutput, null, null);
                }
            }

            List<List<Vector3>> loops = ChainCutSegments(cutSegments, capWeldCell);
            if (loops.Count == 0 ||
                (validateResult && HasUnsafeCutContours(loops, plane.normal))) return false;

            // A complete loop cap is already the only safe closure. The previous
            // second sealing pass used a different fixed weld grid and could mistake
            // an already closed edge for an opening, add an overlapping partial cap,
            // and make the final manifold validation reject an otherwise valid cut.
            if (!AddCapsFromLoops(loops, cutSegments, plane.normal, worldScale,
                    texelDensity, pos, neg, interiorSubmesh))
                return false;
            positive = pos.ToMesh(source.name + " Runtime A");
            negative = neg.ToMesh(source.name + " Runtime B");
            if (positive == null || negative == null)
            {
                if (positive != null) UnityEngine.Object.Destroy(positive);
                if (negative != null) UnityEngine.Object.Destroy(negative);
                positive = null;
                negative = null;
                return false;
            }

            if (validateResult)
            {
                positive = SanitizeClosedRuntimePiece(positive, source.name + " Runtime A");
                negative = SanitizeClosedRuntimePiece(negative, source.name + " Runtime B");
                if (positive == null || negative == null)
                {
                    if (positive != null) UnityEngine.Object.Destroy(positive);
                    if (negative != null) UnityEngine.Object.Destroy(negative);
                    positive = null;
                    negative = null;
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Runtime cuts are intentionally conservative: remove exact duplicate faces
        /// and reject an unsealed result. This prevents dynamic debris from filling an
        /// authored hole or rendering two coplanar faces on top of each other.
        /// </summary>
        static Mesh SanitizeClosedRuntimePiece(Mesh source, string name)
        {
            if (source == null) return null;
            if (source.vertexCount < 4)
            {
                UnityEngine.Object.Destroy(source);
                return null;
            }
            Vector3[] vertices = source.vertices;
            Vector3[] normals = source.normals;
            Vector2[] uvs = source.uv;
            Vector4[] tangents = source.tangents;
            int submeshCount = Mathf.Max(1, source.subMeshCount);
            Bounds bounds = source.bounds;
            float weldCell = Mathf.Max(bounds.size.magnitude * 2e-5f, 1e-6f);
            var welded = new Dictionary<Vector3Int, List<int>>(vertices.Length);
            var weldedPositions = new List<Vector3>(vertices.Length);
            var remap = new int[vertices.Length];
            float weldDistanceSqr = weldCell * weldCell;
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 point = vertices[i];
                var cell = new Vector3Int(
                    Mathf.FloorToInt(point.x / weldCell),
                    Mathf.FloorToInt(point.y / weldCell),
                    Mathf.FloorToInt(point.z / weldCell));
                int id = -1;
                for (int x = -1; x <= 1 && id < 0; x++)
                for (int y = -1; y <= 1 && id < 0; y++)
                for (int z = -1; z <= 1 && id < 0; z++)
                {
                    var neighbour = new Vector3Int(cell.x + x, cell.y + y,
                        cell.z + z);
                    if (!welded.TryGetValue(neighbour,
                            out List<int> candidates)) continue;
                    for (int candidate = 0; candidate < candidates.Count;
                         candidate++)
                    {
                        int candidateId = candidates[candidate];
                        if ((weldedPositions[candidateId] - point).sqrMagnitude >
                            weldDistanceSqr) continue;
                        id = candidateId;
                        break;
                    }
                }
                if (id < 0)
                {
                    id = weldedPositions.Count;
                    weldedPositions.Add(point);
                    if (!welded.TryGetValue(cell, out List<int> bucket))
                    {
                        bucket = new List<int>(1);
                        welded.Add(cell, bucket);
                    }
                    bucket.Add(id);
                }
                remap[i] = id;
            }

            var cleaned = new List<int>[submeshCount];
            var uniqueFaces = new HashSet<RuntimeTriangleKey>();
            var edgeUse = new Dictionary<RuntimeEdgeKey, RuntimeEdgeUse>();
            bool removedAny = false;
            for (int submesh = 0; submesh < submeshCount; submesh++)
            {
                cleaned[submesh] = new List<int>();
                int[] triangles = source.GetTriangles(submesh);
                for (int i = 0; i + 2 < triangles.Length; i += 3)
                {
                    int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                    if (a < 0 || b < 0 || c < 0 || a >= vertices.Length ||
                        b >= vertices.Length || c >= vertices.Length ||
                        remap[a] == remap[b] || remap[b] == remap[c] ||
                        remap[a] == remap[c] ||
                        Vector3.Cross(vertices[b] - vertices[a],
                            vertices[c] - vertices[a]).sqrMagnitude < weldCell * weldCell * weldCell * weldCell)
                    {
                        removedAny = true;
                        continue;
                    }

                    var face = new RuntimeTriangleKey(remap[a], remap[b], remap[c]);
                    if (!uniqueFaces.Add(face))
                    {
                        removedAny = true;
                        continue;
                    }

                    cleaned[submesh].Add(a);
                    cleaned[submesh].Add(b);
                    cleaned[submesh].Add(c);
                    CountRuntimeEdge(edgeUse, remap[a], remap[b]);
                    CountRuntimeEdge(edgeUse, remap[b], remap[c]);
                    CountRuntimeEdge(edgeUse, remap[c], remap[a]);
                }
            }

            if (edgeUse.Count == 0)
            {
                UnityEngine.Object.Destroy(source);
                return null;
            }
            if (!removedAny) return source;

            var result = new Mesh { name = name, hideFlags = HideFlags.DontSave };
            if (vertices.Length > 65535)
                result.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            result.vertices = vertices;
            if (normals != null && normals.Length == vertices.Length) result.normals = normals;
            if (uvs != null && uvs.Length == vertices.Length) result.uv = uvs;
            if (tangents != null && tangents.Length == vertices.Length) result.tangents = tangents;
            result.subMeshCount = submeshCount;
            for (int submesh = 0; submesh < submeshCount; submesh++)
                result.SetTriangles(cleaned[submesh], submesh, false);
            result.RecalculateBounds();
            UnityEngine.Object.Destroy(source);
            return result;
        }

        static void CountRuntimeEdge(Dictionary<RuntimeEdgeKey, RuntimeEdgeUse> edges,
            int a, int b)
        {
            var edge = new RuntimeEdgeKey(a, b);
            edges.TryGetValue(edge, out RuntimeEdgeUse use);
            use.Count++;
            use.Winding += a < b ? 1 : -1;
            edges[edge] = use;
        }

        readonly struct RuntimeEdgeKey : System.IEquatable<RuntimeEdgeKey>
        {
            readonly int m_low;
            readonly int m_high;

            public RuntimeEdgeKey(int a, int b)
            {
                m_low = Mathf.Min(a, b);
                m_high = Mathf.Max(a, b);
            }

            public bool Equals(RuntimeEdgeKey other) =>
                m_low == other.m_low && m_high == other.m_high;
            public override bool Equals(object obj) => obj is RuntimeEdgeKey other && Equals(other);
            public override int GetHashCode() => (m_low * 397) ^ m_high;
        }

        struct RuntimeEdgeUse
        {
            public int Count;
            // A closed orientable surface traverses its two uses of every shared edge
            // in opposite directions, so the signed winding must cancel to zero.
            public int Winding;
        }

        readonly struct RuntimeTriangleKey : System.IEquatable<RuntimeTriangleKey>
        {
            readonly int m_a;
            readonly int m_b;
            readonly int m_c;

            public RuntimeTriangleKey(int a, int b, int c)
            {
                if (a > b) { int swap = a; a = b; b = swap; }
                if (b > c) { int swap = b; b = c; c = swap; }
                if (a > b) { int swap = a; a = b; b = swap; }
                m_a = a;
                m_b = b;
                m_c = c;
            }

            public bool Equals(RuntimeTriangleKey other) =>
                m_a == other.m_a && m_b == other.m_b && m_c == other.m_c;
            public override bool Equals(object obj) => obj is RuntimeTriangleKey other && Equals(other);
            public override int GetHashCode() => ((m_a * 397) ^ m_b) * 397 ^ m_c;
        }

        static void AddClippedTriangle(Vertex a, Vertex b, Vertex c, Plane plane,
            bool keepPositive, Builder builder, int submesh,
            List<Vertex> input, List<Vertex> output, List<Vertex> intersections,
            List<CutSegment> cutSegments)
        {
            input.Clear();
            output.Clear();
            input.Add(a);
            input.Add(b);
            input.Add(c);
            if (intersections != null) intersections.Clear();
            for (int i = 0; i < input.Count; i++)
            {
                Vertex current = input[i];
                Vertex next = input[(i + 1) % input.Count];
                float dc = plane.GetDistanceToPoint(current.Position);
                float dn = plane.GetDistanceToPoint(next.Position);
                bool currentInside = keepPositive ? dc >= -Epsilon : dc <= Epsilon;
                bool nextInside = keepPositive ? dn >= -Epsilon : dn <= Epsilon;
                if (currentInside) output.Add(current);
                if (currentInside == nextInside) continue;
                float t = dc / (dc - dn);
                Vertex inter = Vertex.Lerp(current, next, t);
                output.Add(inter);
                if (intersections != null) intersections.Add(inter);
            }

            if (cutSegments != null && intersections != null && intersections.Count == 2)
            {
                cutSegments.Add(new CutSegment
                {
                    A = intersections[0].Position,
                    B = intersections[1].Position,
                    UVA = intersections[0].UV,
                    UVB = intersections[1].UV
                });
            }

            for (int i = 1; i + 1 < output.Count; i++)
                builder.AddTriangle(output[0], output[i], output[i + 1], submesh);
        }

        static List<List<Vector3>> ChainCutSegments(List<CutSegment> segments, float weldCell)
        {
            var loops = new List<List<Vector3>>();
            if (segments == null || segments.Count < 3) return loops;

            float weldTolerance = Mathf.Max(weldCell, 1e-7f);
            float weldToleranceSqr = weldTolerance * weldTolerance;
            var unused = new List<CutSegment>(segments.Count);
            for (int i = 0; i < segments.Count; i++)
                if ((segments[i].A - segments[i].B).sqrMagnitude >
                    weldToleranceSqr)
                    unused.Add(segments[i]);
            while (unused.Count > 0)
            {
                var loop = new List<Vector3>();
                CutSegment currentSeg = unused[unused.Count - 1];
                unused.RemoveAt(unused.Count - 1);
                loop.Add(currentSeg.A);
                loop.Add(currentSeg.B);

                Vector3 start = currentSeg.A;
                Vector3 currentEnd = currentSeg.B;
                int safety = segments.Count + 1;
                while ((currentEnd - start).sqrMagnitude > weldToleranceSqr &&
                       safety-- > 0)
                {
                    int bestIndex = -1;
                    bool reverse = false;
                    float bestDistance = weldToleranceSqr;
                    for (int i = 0; i < unused.Count; i++)
                    {
                        CutSegment cand = unused[i];
                        float distanceA = (cand.A - currentEnd).sqrMagnitude;
                        if (distanceA <= bestDistance)
                        {
                            bestDistance = distanceA;
                            bestIndex = i;
                            reverse = false;
                        }
                        float distanceB = (cand.B - currentEnd).sqrMagnitude;
                        if (distanceB <= bestDistance)
                        {
                            bestDistance = distanceB;
                            bestIndex = i;
                            reverse = true;
                        }
                    }

                    if (bestIndex < 0) return new List<List<Vector3>>();
                    CutSegment nextSegment = unused[bestIndex];
                    unused.RemoveAt(bestIndex);
                    Vector3 nextEnd = reverse ? nextSegment.A : nextSegment.B;
                    if ((nextEnd - start).sqrMagnitude <= weldToleranceSqr)
                    {
                        currentEnd = start;
                        break;
                    }
                    if ((nextEnd - currentEnd).sqrMagnitude <= weldToleranceSqr)
                        return new List<List<Vector3>>();
                    loop.Add(nextEnd);
                    currentEnd = nextEnd;
                }

                // An open chain cannot be safely capped. Returning no loops rejects this
                // runtime split rather than inventing a face across an authored opening.
                if (loop.Count < 3 ||
                    (currentEnd - start).sqrMagnitude > weldToleranceSqr)
                    return new List<List<Vector3>>();
                loops.Add(loop);
            }
            return loops;
        }

        /// <summary>
        /// Runtime cap triangulation supports independent simple contours, but not a
        /// contour nested inside another one. The latter is a cut through an authored
        /// tunnel/window: capping both contours separately would fill the hole. Reject
        /// self-crossing or nested contours instead of producing a closed-looking mesh
        /// with overlapping faces.
        /// </summary>
        static bool HasUnsafeCutContours(List<List<Vector3>> loops, Vector3 normal)
        {
            Vector3 axisU = Vector3.Cross(normal,
                Mathf.Abs(normal.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            Vector3 axisV = Vector3.Cross(normal, axisU).normalized;
            var polygons = new List<List<Vector2>>(loops.Count);
            for (int i = 0; i < loops.Count; i++)
            {
                List<Vector3> loop = loops[i];
                if (loop == null || loop.Count < 3) return true;
                var polygon = new List<Vector2>(loop.Count);
                for (int p = 0; p < loop.Count; p++)
                    polygon.Add(new Vector2(Vector3.Dot(loop[p], axisU),
                        Vector3.Dot(loop[p], axisV)));
                if (RuntimePolygonSelfIntersects(polygon)) return true;
                polygons.Add(polygon);
            }

            for (int a = 0; a < polygons.Count; a++)
            for (int b = a + 1; b < polygons.Count; b++)
            {
                List<Vector2> first = polygons[a];
                List<Vector2> second = polygons[b];
                if (RuntimePolygonsIntersect(first, second) ||
                    RuntimePointInOrOnPolygon(first, second[0]) ||
                    RuntimePointInOrOnPolygon(second, first[0]))
                    return true;
            }
            return false;
        }

        static bool RuntimePolygonSelfIntersects(List<Vector2> polygon)
        {
            int count = polygon.Count;
            for (int i = 0; i < count; i++)
            {
                Vector2 a0 = polygon[i];
                Vector2 a1 = polygon[(i + 1) % count];
                for (int j = i + 1; j < count; j++)
                {
                    if (j == i + 1 || (i == 0 && j == count - 1)) continue;
                    if (RuntimeSegmentsIntersectOrTouch(a0, a1, polygon[j],
                            polygon[(j + 1) % count]))
                        return true;
                }
            }
            return false;
        }

        static bool RuntimePolygonsIntersect(List<Vector2> first, List<Vector2> second)
        {
            for (int i = 0; i < first.Count; i++)
            for (int j = 0; j < second.Count; j++)
                if (RuntimeSegmentsIntersectOrTouch(first[i], first[(i + 1) % first.Count],
                        second[j], second[(j + 1) % second.Count]))
                    return true;
            return false;
        }

        static bool RuntimePointInOrOnPolygon(List<Vector2> polygon, Vector2 point)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                Vector2 a = polygon[j];
                Vector2 b = polygon[i];
                if (RuntimePointOnSegment(a, b, point)) return true;
                bool crosses = (a.y > point.y) != (b.y > point.y);
                if (crosses && point.x < (b.x - a.x) * (point.y - a.y) /
                    (b.y - a.y) + a.x)
                    inside = !inside;
            }
            return inside;
        }

        static bool RuntimeSegmentsIntersectOrTouch(Vector2 a, Vector2 b,
            Vector2 c, Vector2 d)
        {
            const float epsilon = 1e-6f;
            float abC = RuntimeCross2D(a, b, c);
            float abD = RuntimeCross2D(a, b, d);
            float cdA = RuntimeCross2D(c, d, a);
            float cdB = RuntimeCross2D(c, d, b);
            if ((Mathf.Abs(abC) <= epsilon && RuntimePointOnSegment(a, b, c)) ||
                (Mathf.Abs(abD) <= epsilon && RuntimePointOnSegment(a, b, d)) ||
                (Mathf.Abs(cdA) <= epsilon && RuntimePointOnSegment(c, d, a)) ||
                (Mathf.Abs(cdB) <= epsilon && RuntimePointOnSegment(c, d, b)))
                return true;
            return ((abC > epsilon && abD < -epsilon) ||
                    (abC < -epsilon && abD > epsilon)) &&
                   ((cdA > epsilon && cdB < -epsilon) ||
                    (cdA < -epsilon && cdB > epsilon));
        }

        static bool RuntimePointOnSegment(Vector2 a, Vector2 b, Vector2 point)
        {
            const float epsilon = 1e-6f;
            if (Mathf.Abs(RuntimeCross2D(a, b, point)) > epsilon) return false;
            return point.x >= Mathf.Min(a.x, b.x) - epsilon &&
                   point.x <= Mathf.Max(a.x, b.x) + epsilon &&
                   point.y >= Mathf.Min(a.y, b.y) - epsilon &&
                   point.y <= Mathf.Max(a.y, b.y) + epsilon;
        }

        static float RuntimeCross2D(Vector2 a, Vector2 b, Vector2 point)
        {
            return (b.x - a.x) * (point.y - a.y) -
                   (b.y - a.y) * (point.x - a.x);
        }

        static bool AddCapsFromLoops(List<List<Vector3>> loops,
            List<CutSegment> cutSegments, Vector3 normal, Vector3 worldScale,
            float texelDensity, Builder positive, Builder negative, int submesh)
        {
            BuildWorldTexelProjection(normal, worldScale, out Vector3 axisU,
                out Vector3 axisV, out Vector3 localTangent);

            CutPlaneUVBounds uvBounds = ComputeCutPlaneUVBounds(cutSegments,
                axisU, axisV, texelDensity);

            Vertex CreateVertex(Vector3 pos, Vector3 n) => new Vertex
            {
                Position = pos,
                Normal = n,
                UV = uvBounds.valid
                    ? SampleExtrudedUV(pos, axisU, axisV, uvBounds)
                    : new Vector2(Vector3.Dot(pos, axisU),
                        Vector3.Dot(pos, axisV)) * texelDensity,
                Tangent = new Vector4(localTangent.x, localTangent.y,
                    localTangent.z, 1f)
            };

            for (int l = 0; l < loops.Count; l++)
            {
                List<Vector3> loop = loops[l];
                if (loop.Count < 3) continue;

                // Project 3D loop into 2D polygon using plane axes
                var poly2D = new List<Vector2>(loop.Count);
                for (int i = 0; i < loop.Count; i++)
                    poly2D.Add(new Vector2(Vector3.Dot(loop[i], axisU), Vector3.Dot(loop[i], axisV)));

                List<int> tris2D = TriangulatePolygon2D(poly2D);
                if (tris2D == null ||
                    tris2D.Count != (loop.Count - 2) * 3)
                    return false;

                for (int t = 0; t + 2 < tris2D.Count; t += 3)
                {
                    Vector3 p0 = loop[tris2D[t]];
                    Vector3 p1 = loop[tris2D[t + 1]];
                    Vector3 p2 = loop[tris2D[t + 2]];

                    Vector3 cross = Vector3.Cross(p1 - p0, p2 - p0);
                    if (cross.sqrMagnitude >= 1e-16f && Vector3.Dot(cross, normal) > 0f)
                    {
                        negative.AddTriangle(CreateVertex(p0, normal), CreateVertex(p1, normal), CreateVertex(p2, normal), submesh);
                        positive.AddTriangle(CreateVertex(p0, -normal), CreateVertex(p2, -normal), CreateVertex(p1, -normal), submesh);
                    }
                    else
                    {
                        negative.AddTriangle(CreateVertex(p0, normal), CreateVertex(p2, normal), CreateVertex(p1, normal), submesh);
                        positive.AddTriangle(CreateVertex(p0, -normal), CreateVertex(p1, -normal), CreateVertex(p2, -normal), submesh);
                    }
                }
            }
            return true;
        }

        static List<int> TriangulatePolygon2D(List<Vector2> poly)
        {
            var indices = new List<int>();
            int n = poly.Count;
            if (n < 3) return indices;

            Vector2 min = poly[0];
            Vector2 max = poly[0];
            for (int i = 1; i < n; i++)
            {
                min = Vector2.Min(min, poly[i]);
                max = Vector2.Max(max, poly[i]);
            }
            float extent = Mathf.Max(max.x - min.x, max.y - min.y);
            float areaEpsilon = Mathf.Max(extent * extent * 1e-10f, 1e-14f);

            var V = new List<int>(n);
            if (IsClockwise2D(poly))
            {
                // Snip2D expects counter-clockwise ears.
                for (int v = 0; v < n; v++) V.Add((n - 1) - v);
            }
            else
            {
                for (int v = 0; v < n; v++) V.Add(v);
            }

            int count = n;
            int countSafety = 2 * count;

            for (int v = count - 1; count > 2;)
            {
                if (countSafety-- <= 0) break;

                int u = v;
                if (count <= u) u = 0;
                v = u + 1;
                if (count <= v) v = 0;
                int w = v + 1;
                if (count <= w) w = 0;

                if (Snip2D(poly, u, v, w, count, V, areaEpsilon))
                {
                    indices.Add(V[u]);
                    indices.Add(V[v]);
                    indices.Add(V[w]);
                    V.RemoveAt(v);
                    count--;
                    countSafety = 2 * count;
                }
            }
            if (indices.Count != (n - 2) * 3)
            {
                indices.Clear();
                if (IsClockwise2D(poly))
                {
                    for (int i = 1; i < n - 1; i++)
                    {
                        indices.Add(0);
                        indices.Add(i + 1);
                        indices.Add(i);
                    }
                }
                else
                {
                    for (int i = 1; i < n - 1; i++)
                    {
                        indices.Add(0);
                        indices.Add(i);
                        indices.Add(i + 1);
                    }
                }
            }
            return indices;
        }

        static bool IsClockwise2D(List<Vector2> poly)
        {
            double area = 0.0;
            for (int i = 0; i < poly.Count; i++)
            {
                Vector2 p1 = poly[i];
                Vector2 p2 = poly[(i + 1) % poly.Count];
                area += (double)p1.x * p2.y - (double)p2.x * p1.y;
            }
            return area < 0.0;
        }

        static bool Snip2D(List<Vector2> poly, int u, int v, int w, int n,
            List<int> V, float areaEpsilon)
        {
            Vector2 A = poly[V[u]];
            Vector2 B = poly[V[v]];
            Vector2 C = poly[V[w]];

            if ((B.x - A.x) * (C.y - A.y) -
                (B.y - A.y) * (C.x - A.x) <= areaEpsilon)
                return false;

            for (int p = 0; p < n; p++)
            {
                if (p == u || p == v || p == w) continue;
                Vector2 P = poly[V[p]];
                if (PointInTriangle2D(P, A, B, C)) return false;
            }
            return true;
        }

        static bool PointInTriangle2D(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float ax = c.x - b.x; float ay = c.y - b.y;
            float bx = a.x - c.x; float by = a.y - c.y;
            float cx = b.x - a.x; float cy = b.y - a.y;

            float apx = p.x - a.x; float apy = p.y - a.y;
            float bpx = p.x - b.x; float bpy = p.y - b.y;
            float cpx = p.x - c.x; float cpy = p.y - c.y;

            float aCROSSbp = ax * bpy - ay * bpx;
            float cCROSSap = cx * apy - cy * apx;
            float bCROSScp = bx * cpy - by * cpx;

            return (aCROSSbp >= 0f) && (bCROSScp >= 0f) && (cCROSSap >= 0f);
        }
    }
}
