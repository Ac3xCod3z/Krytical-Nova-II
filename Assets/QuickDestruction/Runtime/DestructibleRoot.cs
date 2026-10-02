using System;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;

namespace RipVertices.QuickDestruction
{
    /// <summary>
    /// Runtime heart of a baked destructible prop.
    ///
    /// Explicit state machine:
    /// Intact -> Damaged -> (Released) -> Fractured -> Settled -> Recycled.
    /// The fractured hierarchy is baked in the prefab, so normal operation never calls
    /// Instantiate/Destroy - activation and reset only toggle pre-existing objects.
    /// Reset is idempotent and safe from every state.
    /// </summary>
    [AddComponentMenu("Quick Destruction/Destructible Root")]
    [DisallowMultipleComponent]
    public partial class DestructibleRoot : MonoBehaviour, IDamageableDestructible
    {
        static readonly ProfilerMarker s_breakMarker = new ProfilerMarker("QD.Break.Activate");
        static readonly ProfilerMarker s_resetMarker = new ProfilerMarker("QD.Reset");
        static readonly ProfilerMarker s_tickMarker = new ProfilerMarker("QD.Lifecycle.Tick");
        static readonly ProfilerMarker s_fxMarker = new ProfilerMarker("QD.FX.Dispatch");
        static readonly ProfilerMarker s_activationBatchMarker =
            new ProfilerMarker("QD.Break.ActivationBatch");
        static readonly ProfilerMarker s_structuralPathMarker =
            new ProfilerMarker("QD.Support.StructuralPath");
        static readonly ProfilerMarker s_structuralJointCacheMarker =
            new ProfilerMarker("QD.Support.BuildJointCache");
        const int ActivationBatchThreshold = 60;
        const int LargeActivationBatchSize = 40;
        sealed class SharedPhysicsState
        {
            public List<Collider> CurrentImpactColliders;
            public List<Collider> ImpactColliderBuffer;
#if UNITY_6000_0_OR_NEWER
            public PhysicsMaterial DefaultDebrisContactMaterial;
#else
            public PhysicMaterial DefaultDebrisContactMaterial;
#endif
        }

        static readonly SharedPhysicsState s_sharedPhysics = new SharedPhysicsState();
        // Intact Health is authored per prop rather than multiplied by mesh volume. Using
        // the baked body's full mass for self-fall damage therefore made a larger copy of
        // the same prop vastly more fragile. Twenty kilograms calibrates default wood to
        // take partial damage near a five-metre fall and to fail near twenty metres, while
        // external Rigidbody hits continue to use their real masses.
        const float SelfFallReferenceMass = 20f;
        // A Dynamic prop may bounce or scrape the floor several times after a short drop.
        // Do not turn those contacts into accumulated fracture damage. Nine metres per
        // second is roughly a four-metre free fall; the default curve then starts showing
        // visible damage around five metres and still reaches a full break near twenty.
        const float MinimumSelfFallVelocity = 9f;

        [Header("Identity")]
        [Tooltip("Stable ID used for save/load and networking intents.")]
        public string StableId;

        [Header("Configuration")]
        public DestructionProfile Profile;
        [Tooltip("Per-instance override. Use Profile keeps the shared profile value; Local and All Pieces affect only this destructible.")]
        public FirstBreakActivationOverride FirstBreakActivation =
            FirstBreakActivationOverride.UseProfile;
        [HideInInspector, Tooltip("Scene source disabled when this generated instance was placed. Used by editor Clean commands to restore the authored scene object.")]
        public GameObject OriginalSourceObject;
        [HideInInspector, Tooltip("All scene roots disabled when a merged modular assembly was placed. The first entry is also kept in Original Source Object for backwards compatibility.")]
        public List<GameObject> OriginalSourceObjects = new List<GameObject>();
        [HideInInspector, Tooltip("Authored activeSelf state for every Original Source Objects entry, restored by editor Clean commands.")]
        public List<bool> OriginalSourceActiveStates = new List<bool>();
        [HideInInspector, Tooltip("Source hierarchy root hidden after every nested mesh in it was baked. Kept separate from mesh-source identity so rebaking one child cannot claim its siblings' outputs.")]
        public GameObject OriginalHierarchyRoot;
        [HideInInspector, Tooltip("Authored activeSelf state restored to Original Hierarchy Root by editor Clean commands.")]
        public bool OriginalHierarchyRootActiveState = true;
        [HideInInspector, Tooltip("True when a Whole Building bake produced this instance. The window restores that mode before a rebake, because rebaking a merged assembly in Selected Objects mode replaces its per-panel intact colliders with a single convex hull spanning the building.")]
        public bool BakedAsWholeBuilding;

        [Header("Baked References (filled by Bake pipeline)")]
        public GameObject IntactRoot;
        public GameObject FracturedRoot;
        public Rigidbody IntactBody;
        [Tooltip("Exact non-convex intact colliders used while the body is kinematic.")]
        public List<Collider> IntactExactColliders = new List<Collider>();
        [Tooltip("Convex companions used if an Exact Mesh intact body becomes dynamic.")]
        public List<Collider> IntactDynamicColliders = new List<Collider>();
        [Tooltip("Every intact collider that can identify one Whole Building structural module. Kept parallel with Intact Collider Structural Groups.")]
        public List<Collider> IntactColliders = new List<Collider>();
        [Tooltip("Baked structural-group identity for every Intact Colliders entry. Direct hits use it so a radius at a corner cannot wake a neighbouring wall.")]
        public List<int> IntactColliderStructuralGroups = new List<int>();
        public StructuralSupportNode StructuralNode;
        public List<FragmentRecord> Fragments = new List<FragmentRecord>();
        [Tooltip("Exterior-only shadow mesh used by still-attached fragments. It is rebuilt after a piece detaches, preventing fracture-cap faces and per-piece edges from casting visible crack shadows.")]
        public MeshFilter AttachedShadowProxyFilter;
        public Renderer AttachedShadowProxyRenderer;
        [HideInInspector, Tooltip("Large merged bakes add fragment Rigidbodies only when a piece actually detaches. This avoids simulating hundreds of inactive kinematic bodies after the first local hit.")]
        public bool DeferInactiveFragmentPhysics;

        [Header("Events (designer hooks)")]
        public UnityEvent OnStructureReleasedEvent;
        public UnityEvent OnSecondaryBreakEvent;
        public UnityEvent OnBrokenEvent;
        public UnityEvent OnResetEvent;

        // Allocation-free C# events for code integrations.
        public event Action<DamageContext> HitReceived;
        public event Action<DamageContext, DamageResult> Damaged;
        public event Action StructureReleased;
        public event Action<int, int> SecondaryBroken;        // parent index, activated children
        public event Action<BreakContext> Broken;
        public event Action<int> FragmentActivated;           // fragment index
        public event Action<Vector3, float, int, float> FragmentImpact; // point, energy, fragment index, fragment size
        public event Action<int> SettledAll;                  // settled fragment count
        public event Action<ResetMode> WasReset;

        public DestructibleState State => m_state;
        public float Health => m_health;
        public float HealthNormalized => Profile != null && Profile.Health > 0f ? m_health / Profile.Health : 0f;
        public int ActiveFragmentCount => m_activeCount;
        public int SettledFragmentCount => m_settledCount;
        public int ActiveSecondaryFragmentCount => m_secondaryActiveCount;
        public int SettledSecondaryFragmentCount => m_secondarySettledCount;
        public int BakedSecondaryFragmentCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < Fragments.Count; i++)
                {
                    SecondaryFragmentNode node = Fragments[i]?.SecondaryNode;
                    if (node != null) count += node.Children.Count;
                }
                return count;
            }
        }
        public string MaterialTag => Profile != null ? Profile.MaterialTag : string.Empty;
        public bool IsStructurallyReleased => m_bodyReleased;

        /// <summary>
        /// Returns true when this primary fragment (or the primary parent of a level-two
        /// fragment) has already left the kinematic shell and is now loose debris.
        /// Weapon raycasts can use this to continue toward attached geometry behind it.
        /// </summary>
        public bool IsFragmentLoose(int fragmentIndex)
        {
            if (fragmentIndex < 0 || fragmentIndex >= Fragments.Count)
                return false;
            FragmentRecord fragment = Fragments[fragmentIndex];
            return fragment != null &&
                   ((!IsDynamicClusterMember(fragmentIndex) && fragment.Activated) ||
                     fragment.Detached ||
                     m_pendingActivationIndices.Contains(fragmentIndex));
        }

        /// <summary>
        /// Resolves an intact hit collider to the exact structural module baked from the
        /// authored Whole Building source. Explosions deliberately do not call this and
        /// remain free to affect every group inside their radial footprint.
        /// </summary>
        public int ResolveStructuralGroup(Collider hitCollider)
        {
            if (hitCollider == null) return -1;

            // Once the fractured shell is visible, hitscan weapons raycast the baked
            // fragment collider rather than an intact panel. Preserve the same group
            // identity across that visual swap; otherwise only the first click is scoped
            // and later clicks at a corner can spill back into neighbouring modules.
            FragmentRuntime runtime =
                hitCollider.GetComponentInParent<FragmentRuntime>();
            if (runtime != null && runtime.Root == this)
            {
                int fragmentIndex = runtime.Level == 0
                    ? ResolvePrimaryFragmentIndex(runtime)
                    : runtime.FragmentIndex;
                if (fragmentIndex >= 0 && fragmentIndex < Fragments.Count &&
                    Fragments[fragmentIndex] != null)
                    return Fragments[fragmentIndex].StructuralGroup;
                return -1;
            }

            int mappedCount = Mathf.Min(
                IntactColliders != null ? IntactColliders.Count : 0,
                IntactColliderStructuralGroups != null
                    ? IntactColliderStructuralGroups.Count : 0);
            for (int i = 0; i < mappedCount; i++)
            {
                if (IntactColliders[i] == hitCollider)
                    return IntactColliderStructuralGroups[i];
            }

            // Compatibility for generated prefabs baked before the explicit parallel map
            // was added. StructuralSupportNode already stores these colliders in exactly
            // the same stable order used to assign FragmentRecord.StructuralGroup.
            if (StructuralNode != null && StructuralNode.ProbeColliders != null)
            {
                for (int i = 0; i < StructuralNode.ProbeColliders.Count; i++)
                    if (StructuralNode.ProbeColliders[i] == hitCollider)
                        return i;
            }
            return -1;
        }

        /// <summary>
        /// Deterministic compatibility fallback for direct-hit integrations that know the
        /// world contact point but cannot pass the hit Collider. It always returns at most
        /// one module; exact-collider routing remains preferred at shared seams.
        /// </summary>
        public int ResolveStructuralGroup(Vector3 worldPoint)
        {
            float bestDistance = float.PositiveInfinity;
            int bestGroup = -1;
            int mappedCount = Mathf.Min(
                IntactColliders != null ? IntactColliders.Count : 0,
                IntactColliderStructuralGroups != null
                    ? IntactColliderStructuralGroups.Count : 0);
            for (int i = 0; i < mappedCount; i++)
            {
                Collider collider = IntactColliders[i];
                if (collider == null) continue;
                float distance = (GetColliderClosestPoint(collider, worldPoint) -
                                  worldPoint).sqrMagnitude;
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                bestGroup = IntactColliderStructuralGroups[i];
            }
            if (bestGroup >= 0) return bestGroup;

            // Older prefab fallback: choose a single fragment group even when no explicit
            // intact-collider map has been baked yet. Ties remain stable by fragment order.
            for (int i = 0; i < Fragments.Count; i++)
            {
                FragmentRecord fragment = Fragments[i];
                if (fragment == null || fragment.Transform == null) continue;
                float distance = (GetFragmentClosestPoint(fragment, worldPoint) -
                                  worldPoint).sqrMagnitude;
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                bestGroup = fragment.StructuralGroup;
            }
            return bestGroup;
        }

        /// <summary>
        /// Validates the serialized component index against its exact fragment Transform.
        /// A stale index is repaired only by identity lookup; a foreign/unmapped component
        /// returns -1 so collision code cannot fall back to radius activation.
        /// </summary>
        internal int ResolvePrimaryFragmentIndex(FragmentRuntime runtime)
        {
            if (runtime == null || runtime.Root != this ||
                runtime.transform == null || Fragments == null)
                return -1;

            int bakedIndex = runtime.FragmentIndex;
            if (bakedIndex >= 0 && bakedIndex < Fragments.Count &&
                Fragments[bakedIndex] != null &&
                Fragments[bakedIndex].Transform == runtime.transform)
                return bakedIndex;

            for (int i = 0; i < Fragments.Count; i++)
                if (Fragments[i] != null &&
                    Fragments[i].Transform == runtime.transform)
                    return i;
            return -1;
        }

        /// <summary>
        /// Resolves a collision reported by the root compound Rigidbody back to one exact
        /// deferred fragment. Large Whole Building bakes keep attached FragmentRuntime
        /// components disabled and add a per-piece Rigidbody only when that piece detaches;
        /// their collider therefore reports incoming projectile contacts on this root.
        /// Enabled fragment runtimes are deliberately excluded because they receive their
        /// own OnCollisionEnter and routing them here as well would apply the hit twice.
        /// </summary>
        int ResolveDeferredFragmentCollisionIndex(Collider hitCollider)
        {
            if (!DeferInactiveFragmentPhysics ||
                m_state < DestructibleState.Fractured ||
                hitCollider == null)
                return -1;

            FragmentRuntime runtime =
                hitCollider.GetComponentInParent<FragmentRuntime>();
            if (runtime == null || runtime.Root != this || runtime.Level != 0 ||
                runtime.enabled)
                return -1;

            return ResolvePrimaryFragmentIndex(runtime);
        }

        DestructibleState m_state = DestructibleState.Intact;
        float m_health;
        float m_lastDamageTime = -999f;
        float m_lastImpactSpreadTime = -999f;
        int m_lastDamageSourceId = 0;
        int m_activeCount;
        int m_settledCount;
        int m_secondaryActiveCount;
        int m_secondarySettledCount;
        int m_outstandingBudgetCount;
        bool m_bodyReleased;
        bool m_allDebrisDespawned;
        RuntimeFragmentSplitter m_runtimeSplitter;
        // Section support is intentionally finer than the footing probe grid. Footing
        // probes answer whether the base is present; these slices answer the different
        // question "is there still enough material at this height to carry what is above?"
        // Sixteen bands keep a twenty-metre wall at roughly metre resolution without
        // turning the support poll into a per-triangle operation. When the attached set
        // changes, one cached 4x refinement catches a narrow cut hidden inside a coarse
        // band; unchanged groups continue to pay only for these sixteen bands.
        const int SectionBands = 16;
        const int RefinedSectionBands = SectionBands * 4;
        // A height band alone cannot say WHERE along a long element the material went. A
        // twenty-metre wall shot through near one end lost more than half of that band's
        // volume, the band read as severed, and everything above it came down across the
        // whole wall — with the floors it carried. Bands are therefore also split along the
        // element's long horizontal axis, so a local hole fails only the lanes it actually
        // crosses. Lanes are never narrower than the material they measure, so a column, a
        // barrel or any element only a fragment or two wide keeps exactly one lane and the
        // previous whole-section behaviour.
        const int MaxSectionLanes = 8;
        Dictionary<long, List<int>> m_footingGrid;
        readonly List<int> m_footingScratch = new List<int>(32);
        float m_footingCell = 1f;
        // A neighbouring structural node asks about the same fixed probe point on every
        // support poll. Resolve the expensive visual-triangle membership once; fragments
        // never move while attached, and their live flags still decide every later query.
        Dictionary<Vector4, List<int>> m_attachedFragmentNearQueries;
        sealed class SupportVisualGeometry
        {
            public Renderer Renderer;
            public MeshFilter Filter;
            public Mesh Mesh;
            public Vector3[] Vertices;
            public Vector3[] WorldVertices;
            public int[] Triangles;
            public Matrix4x4 LocalToWorld;
        }
#if UNITY_6000_4_OR_NEWER
        Dictionary<EntityId, SupportVisualGeometry> m_supportVisualGeometry;
#else
        Dictionary<int, SupportVisualGeometry> m_supportVisualGeometry;
#endif
        Dictionary<int, float[]> m_sectionTotals;
        Dictionary<int, float> m_sectionMinY;
        Dictionary<int, float> m_sectionHeight;
        Dictionary<int, List<int>> m_sectionGroupFragments;
        List<FragmentRecord> m_sectionFragmentList;
        int m_sectionFragmentListCount = -1;
        float[] m_sectionScratch;
        /// <summary>
        /// How one structural group's height bands are split horizontally. Count 1 means
        /// the element is measured as a single section, exactly as before lanes existed.
        /// </summary>
        sealed class SectionLaneLayout
        {
            public int Axis;
            public float Min;
            public float Width = 1f;
            public int Count = 1;

            public int IndexOf(float coordinate)
            {
                return Mathf.Clamp(
                    Mathf.FloorToInt((coordinate - Min) / Mathf.Max(1e-6f, Width)),
                    0, Count - 1);
            }

            public float LaneMin(int lane) => Min + lane * Width;
            public float LaneMax(int lane) => Min + (lane + 1) * Width;
        }
        Dictionary<int, SectionLaneLayout> m_sectionLaneLayouts;
        static readonly SectionLaneLayout s_singleSectionLane = new SectionLaneLayout();
        sealed class RefinedSectionCache
        {
            public float[] Totals;
            public ulong AttachedSignature;
            public bool HasSignature;
            public float Ratio = 1f;
            public int FailedBand = -1;
            public int FailedLane = -1;
            public float FailedSectionY;
            public bool HasSelectedFailure;
            public float SelectedFailureY;
            public int SelectedFailureAxis = -1;
            public float SelectedFailureLaneMin;
            public float SelectedFailureLaneMax;
        }
        Dictionary<int, RefinedSectionCache> m_refinedSectionCaches;
        float[] m_refinedSectionScratch;
        Vector3 m_initialLocalPosition;
        Quaternion m_initialLocalRotation;
        Vector3 m_initialLocalScale;
        // Awake is the only place the authoring pose is read, and Awake never runs for a
        // scene object in the editor. A reset invoked from editor tooling before play
        // therefore restored three default-initialised fields and collapsed the object to
        // the origin at zero scale. Remember whether the pose was ever actually captured.
        bool m_initialTransformCaptured;
        bool[] m_supportVisited;
        bool[] m_supportPruned;
        bool[] m_supportSeeds;
        float[] m_supportInitialContactAreas;
        float[] m_supportRemainingContactAreas;
        List<int>[] m_crossStructuralNeighbours;
        int m_crossStructuralNeighbourFragmentCount = -1;
        // Connected components of the BAKED neighbour graph, per structural group. The
        // bake links two fragments only when their welded cut vertices resolve a shared
        // face, so a slab or wall can arrive with material that physically abuts its
        // neighbour yet owns no link to it. Such an island is unreachable for the support
        // search and the first sweep drops it out of an otherwise untouched element. The
        // components are static bake topology, which is exactly what makes them safe to
        // bridge by contact: a real cut separates ATTACHED material inside one component
        // and is never mistaken for a missing bake link.
        int[] m_bakedSupportComponents;
        // A probe-threshold failure asks the connectivity sweep to evaluate one grounded
        // element with the authored cantilever limit even while most of its material is
        // still present. Unrelated healthy groups keep the normal late-collapse gate.
        int m_forcedSpanStructuralGroup = -1;
        Mesh m_bakedAttachedShadowMesh;
        Mesh m_runtimeAttachedShadowMesh;
        bool m_attachedShadowProxyDirty;
        readonly Queue<int> m_supportQueue = new Queue<int>();
        readonly Queue<int> m_supportPruneQueue = new Queue<int>();
        readonly List<int> m_unsupportedBodies = new List<int>();
        readonly HashSet<int> m_anchorGroups = new HashSet<int>();
        Dictionary<int, float> m_attachedGroupBaseY;
        readonly Queue<PendingFragmentActivation> m_pendingActivations =
            new Queue<PendingFragmentActivation>();
        readonly HashSet<int> m_pendingActivationIndices = new HashSet<int>();
        MeshRenderer m_effectFallbackRenderer;
        readonly RaycastHit[] m_dynamicSupportHits = new RaycastHit[16];
        // Shared by the overlap fallback of the external-footing test. Queries run inside a
        // single sweep and never nest, so one buffer for the whole scene is enough.
        static readonly Collider[] s_externalFootingOverlap = new Collider[16];
        // Which baked ground anchors are standing on something right now, refreshed once
        // per connectivity sweep, and whether any anchor of this object has EVER been seen
        // standing. Attached pieces do not move; what is underneath them does.
        bool[] m_anchorStanding;
        bool m_anchorFootingSeen;
        readonly RaycastHit[] m_fragmentGroundGuardHits = new RaycastHit[8];
        float m_nextDynamicSupportCheck;
        float m_dynamicUnsupportedSince = -1f;
        Vector3 m_dynamicResidualLinearVelocity;
        Vector3 m_dynamicResidualAngularVelocity;
        bool m_clusterDynamicResidualOnNextBreak;
        bool m_applyingCollisionDamage;
        readonly HashSet<int> m_skipNextLoosePushFragments =
            new HashSet<int>();
        readonly List<int> m_dynamicClusterHitMembers = new List<int>(32);
        bool[] m_dynamicClusterMembers;
        Transform[] m_dynamicClusterOriginalParents;
        int m_dynamicClusterCarrierIndex = -1;
        BoxCollider m_dynamicClusterProxyCollider;
        readonly List<Collider> m_dynamicClusterProxyColliders =
            new List<Collider>(32);

        struct PendingFragmentActivation
        {
            public int Index;
            public BreakContext Context;
            public Vector3 InheritedLinearVelocity;
            public Vector3 InheritedAngularVelocity;
            public Vector3 InheritedVelocityOrigin;
            public bool HasInheritedVelocityOrigin;
        }

        bool UsesActiveBodyBudget =>
            Profile != null && Profile.EnforceActiveBodyBudget;

        /// <summary>Records the authoring pose once, the first time it is needed.</summary>
        void CaptureInitialTransform()
        {
            if (m_initialTransformCaptured) return;
            m_initialLocalPosition = transform.localPosition;
            m_initialLocalRotation = transform.localRotation;
            m_initialLocalScale = transform.localScale;
            m_initialTransformCaptured = true;
        }

        void Awake()
        {
            m_health = Profile != null ? Profile.Health : DestructionProfile.DefaultHealth;
            if (string.IsNullOrEmpty(StableId))
                StableId = Guid.NewGuid().ToString("N");

            if (IntactBody == null)
                IntactBody = GetComponent<Rigidbody>();
            if (StructuralNode == null)
                StructuralNode = GetComponent<StructuralSupportNode>();

            // Bakes normally serialize both references. Keep runtime resilient to older
            // prefabs, missing-script cleanup and partially migrated Generated folders:
            // a loose visual must never be activated without a Rigidbody and a collider
            // that PhysX is allowed to use on a dynamic body.
            if (!DeferInactiveFragmentPhysics)
                EnsureBakedFragmentPhysics();

            // New bakes already contain this component. Add it lazily for older baked
            // prefabs whose assigned profile enables runtime re-fracture, and strip it
            // again when the profile turns it off, so a disabled feature costs neither a
            // component nor an update.
            m_runtimeSplitter = GetComponent<RuntimeFragmentSplitter>();
            if (Profile != null && Profile.RuntimeFragmentSplitting)
            {
                EnsureRuntimeSplitter();
            }
            else if (m_runtimeSplitter != null)
            {
                // No event subscription and no Update path in the default-off mode.
                m_runtimeSplitter.enabled = false;
            }

            CaptureInitialTransform();

            if (AttachedShadowProxyFilter != null)
                m_bakedAttachedShadowMesh = AttachedShadowProxyFilter.sharedMesh;

            // Make sure baked initial data matches the authoring-time pose.
            EnsureIntactState();
            ConfigureIntactBody();
        }

        void OnEnable() => DestructionBudgetManager.RegisterRoot(this);
        void OnDisable() => DestructionBudgetManager.UnregisterRoot(this);

        /// <summary>
        /// Cached lazy access to the optional runtime re-fracture component. Older bakes
        /// predate it and the profile switch can be flipped during Play Mode, so it is
        /// still created on demand — just not looked up again on every hit.
        /// </summary>
        RuntimeFragmentSplitter EnsureRuntimeSplitter()
        {
            if (m_runtimeSplitter == null)
            {
                m_runtimeSplitter = GetComponent<RuntimeFragmentSplitter>();
                if (m_runtimeSplitter == null)
                    m_runtimeSplitter = gameObject.AddComponent<RuntimeFragmentSplitter>();
            }
            if (!m_runtimeSplitter.enabled) m_runtimeSplitter.enabled = true;
            return m_runtimeSplitter;
        }

        void EnsureIntactState()
        {
            if (IntactRoot != null && !IntactRoot.activeSelf) IntactRoot.SetActive(true);
            if (FracturedRoot != null && FracturedRoot.activeSelf) FracturedRoot.SetActive(false);
            if (IntactBody != null) IntactBody.detectCollisions = true;
        }

        // ------------------------------------------------------------------ damage

        public DamageResult ApplyDamage(in DamageContext context)
        {
            return ApplyDamage(context, null);
        }

        /// <summary>Applies a direct hit while excluding a shooter hierarchy from
        /// the optional nearby impact wave. The direct hit keeps its authored radius.</summary>
        public DamageResult ApplyDamage(in DamageContext context, Transform ignoredImpactHierarchy)
        {
            DamageResult result = ApplyDamageWithoutSpread(context);
            TrySpreadImpact(context, ignoredImpactHierarchy);
            return result;
        }

        void TrySpreadImpact(in DamageContext context, Transform ignoredHierarchy = null)
        {
            if (Profile == null || !Profile.SpreadImpactToNearbyObjects ||
                !isActiveAndEnabled || m_state == DestructibleState.Recycled)
                return;

            float impulse = Mathf.Max(context.BreakEnergy >= 0f
                ? context.BreakEnergy : context.Amount, context.DirectionalForce);
            float radius = Profile.ResolveImpactSpreadRadius(impulse);
            if (radius <= 0f ||
                Time.time - m_lastImpactSpreadTime < Mathf.Max(0.05f, Profile.DamageCooldown))
                return;

            // Reserve before dispatch: one compound collision can report through several
            // fragments, and damage listeners can synchronously enter the system again.
            m_lastImpactSpreadTime = Time.time;
            DestructionExplosion.SpreadImpact(this, context, radius, impulse,
                ignoredHierarchy);
        }

        internal DamageResult ApplyDamageWithoutSpread(in DamageContext context)
        {
            if (Profile == null)
                return new DamageResult(DamageOutcome.Ignored, 0f, 0);

            // Reject weak hits before cooldowns, fragment health and runtime splitting.
            // Contact feedback and physical response still happen without accumulating wear.
            if (!Profile.AcceptsHitDamage(context.Amount))
            {
                HitReceived?.Invoke(context);
                if (!m_applyingCollisionDamage)
                {
                    if (m_state >= DestructibleState.Fractured)
                    {
                        m_skipNextLoosePushFragments.Clear();
                        if (m_dynamicClusterCarrierIndex >= 0)
                            m_skipNextLoosePushFragments.Add(m_dynamicClusterCarrierIndex);
                        PushLooseFragments(context,
                            radial: context.ImpactDirection.sqrMagnitude <= 0.000001f);
                        m_skipNextLoosePushFragments.Clear();
                        ApplyImpactToDynamicClusterCarrier(new BreakContext(context.Point,
                            context.Normal, context.Radius,
                            context.BreakEnergy >= 0f ? context.BreakEnergy : context.Amount,
                            context.SourceId, context.ImpactDirection, context.DirectionalForce));
                    }
                    else if (IntactBody != null && !IntactBody.isKinematic)
                        ApplyDirectImpactToBody(IntactBody, context.Point, context.Normal,
                            context.ImpactDirection,
                            context.BreakEnergy >= 0f ? context.BreakEnergy : context.Amount,
                            context.DirectionalForce);
                }
                return new DamageResult(DamageOutcome.Ignored, 0f, 0);
            }

            // Supports enabling the runtime-only profile option during Play Mode and
            // legacy bakes that predate the component, without a geometry rebake.
            RuntimeFragmentSplitter runtimeSplitter = Profile.RuntimeFragmentSplitting
                ? EnsureRuntimeSplitter()
                : null;

            // Direct dispatch keeps runtime splitting reliable even on legacy bakes and
            // components enabled during Play Mode. The splitter no longer depends on event
            // subscription order; the normal HitReceived event below remains presentation-only.
            runtimeSplitter?.ProcessHit(context);

            // Dispatched for every gameplay hit, including hits rejected by cooldown or
            // hits on an already-fractured shell. This gives presentation systems an
            // exact, allocation-free impact point without changing damage semantics.
            HitReceived?.Invoke(context);

            // Post-fracture localized activation: further hits wake the remaining
            // kinematic shell fragments inside the context radius through the
            // normal budget path. Radius <= 0 activates all remaining fragments.
            if (m_state >= DestructibleState.Fractured)
            {
                m_skipNextLoosePushFragments.Clear();
                float minCooldown = Mathf.Max(0.05f, Profile.DamageCooldown);
                if (Time.time - m_lastDamageTime < minCooldown || (Time.time == m_lastDamageTime && context.SourceId != 0 && context.SourceId == m_lastDamageSourceId))
                    return new DamageResult(DamageOutcome.Ignored, 0f, 0);
                m_lastDamageTime = Time.time;
                m_lastDamageSourceId = context.SourceId;

                int detachedCluster =
                    DetachDynamicClusterFragmentsInRadius(context);
                if (detachedCluster > 0)
                    runtimeSplitter?.ProcessHit(context);

                // Debris that is already loose is still a physical object: a shot into it
                // has to shove it. Activation alone only wakes pieces that are still part
                // of the kinematic shell, so rubble on the ground would absorb every
                // further hit without moving.
                // Same rule the BreakNow path uses: a caller that supplied no travel vector
                // is a blast, and every loose piece leaves along its own outward vector. One
                // shared direction resolved from the root centre threw a whole pile of rubble
                // the same way regardless of where each piece lay relative to the origin.
                PushLooseFragments(context,
                    radial: context.ImpactDirection.sqrMagnitude <= 0.000001f);
                m_skipNextLoosePushFragments.Clear();

                int reactivated = ActivateRemaining(new BreakContext(
                    context.Point, context.Normal, context.Radius,
                    context.BreakEnergy >= 0f ? context.BreakEnergy : context.Amount,
                    context.SourceId, context.ImpactDirection,
                    context.DirectionalForce, context.TargetStructuralGroup));
                if (reactivated < 0)
                {
                    var delayed = new DamageResult(DamageOutcome.DelayedByBudget, 0f, 0);
                    Damaged?.Invoke(context, delayed);
                    return delayed;
                }
                int changed = reactivated + detachedCluster;
                if (changed == 0)
                    return new DamageResult(DamageOutcome.Ignored, 0f, 0);
                var postBreak = new DamageResult(DamageOutcome.Broken, 0f, changed);
                Damaged?.Invoke(context, postBreak);
                return postBreak;
            }

            float minCooldownIntact = Mathf.Max(0.05f, Profile.DamageCooldown);
            if (Time.time - m_lastDamageTime < minCooldownIntact || (Time.time == m_lastDamageTime && context.SourceId != 0 && context.SourceId == m_lastDamageSourceId))
                return new DamageResult(DamageOutcome.Ignored, 0f, 0);
            m_lastDamageTime = Time.time;
            m_lastDamageSourceId = context.SourceId;

            float consumed = Mathf.Min(m_health, context.Amount);
            m_health -= consumed;

            if (m_health <= 0f)
            {
                float firstBreakRadius = ActivateAllOnFirstBreak ? 0f : context.Radius;
                var bc = new BreakContext(context.Point, context.Normal, firstBreakRadius,
                    context.BreakEnergy >= 0f ? context.BreakEnergy : context.Amount,
                    context.SourceId, context.ImpactDirection,
                    context.DirectionalForce,
                    ActivateAllOnFirstBreak ? -1 : context.TargetStructuralGroup);
                int activated = DoBreak(bc);

                if (activated < 0)
                {
                    m_health = 1f;
                }
                var result = new DamageResult(
                    activated < 0 ? DamageOutcome.DelayedByBudget : DamageOutcome.Broken,
                    consumed, Mathf.Max(activated, 0));
                Damaged?.Invoke(context, result);
                return result;
            }

            // API/hitscan calls do not pass through Unity's collision solver. Honour their
            // authored push on an intact Dynamic prop. Collision entry already moved the
            // body physically, so applying BreakEnergy again there would double the shot.
            if (IntactBody != null && !IntactBody.isKinematic &&
                (!m_applyingCollisionDamage || context.DirectionalForce >= 0f))
                ApplyDirectImpactToBody(IntactBody, context.Point,
                    context.Normal, context.ImpactDirection,
                    context.BreakEnergy >= 0f
                        ? context.BreakEnergy : context.Amount,
                    context.DirectionalForce);

            if (m_state == DestructibleState.Intact)
                m_state = DestructibleState.Damaged;

            int hitActivated = 0;
            if (context.Radius > 0f && m_state >= DestructibleState.Fractured)
            {
                hitActivated = ActivateRemaining(new BreakContext(context.Point,
                    context.Normal, context.Radius, context.Amount,
                    context.SourceId, context.ImpactDirection,
                    context.DirectionalForce));
            }

            var dmgResult = new DamageResult(
                DamageOutcome.Damaged,
                consumed, hitActivated);
            Damaged?.Invoke(context, dmgResult);
            return dmgResult;
        }

        public void BreakNow(in BreakContext context)
        {
            BreakNow(context, null);
        }

        /// <summary>Forces the direct break; nearby objects still use their own Health
        /// and hit threshold. Optional exclusion protects the shooter's hierarchy.</summary>
        public void BreakNow(in BreakContext context, Transform ignoredImpactHierarchy)
        {
            BreakWithoutSpread(context);
            TrySpreadImpact(new DamageContext(Mathf.Max(0f, context.Energy),
                context.Point, context.Normal, context.Radius, 0, context.SourceId,
                context.Energy, context.ImpactDirection, context.DirectionalForce,
                context.TargetStructuralGroup), ignoredImpactHierarchy);
        }

        internal void BreakWithoutSpread(in BreakContext context)
        {
            // A decisive hit is still a hit. Only ApplyDamage used to announce itself, so a
            // weapon wired to the convenient Break Immediately option fired silently into
            // settled rubble: no sound, no particle, whenever the piece under the muzzle
            // did not happen to split. Presentation systems need the event regardless of
            // what the hit did to the geometry. A duplicate is not a concern — Break
            // Effects already drops a Broken that repeats the same source and point in the
            // same frame.
            HitReceived?.Invoke(new DamageContext(
                Mathf.Max(0f, context.Energy), context.Point, context.Normal,
                context.Radius, 0, context.SourceId, context.Energy,
                context.ImpactDirection, context.DirectionalForce,
                context.TargetStructuralGroup));

            // Post-fracture: an immediate weapon/bullet hit must also re-fracture the
            // exact loose piece under its contact point. Previously BreakNow only
            // activated the remaining baked shell, so projectiles using the convenient
            // Break Immediately option could never split runtime pieces once they had
            // settled on the ground. This remains an explicit-hit path; ordinary ground
            // contacts are still filtered by RuntimeFragmentSplitter.ProcessCollision.
            if (m_state >= DestructibleState.Fractured)
            {
                var directContext = new DamageContext(
                    Profile != null ? Mathf.Max(Profile.MinimumHitDamage,
                        Mathf.Max(Profile.RuntimeSplitStrength, Mathf.Max(0f, context.Energy)))
                        : Mathf.Max(0f, context.Energy),
                    context.Point, context.Normal, context.Radius, 0,
                    context.SourceId, context.Energy,
                    context.ImpactDirection, context.DirectionalForce,
                    context.TargetStructuralGroup);
                DetachDynamicClusterFragmentsInRadius(directContext);
                if (Profile != null && Profile.RuntimeFragmentSplitting)
                {
                    RuntimeFragmentSplitter splitter =
                        GetComponent<RuntimeFragmentSplitter>();
                    if (splitter == null)
                        splitter = gameObject.AddComponent<RuntimeFragmentSplitter>();
                    if (!splitter.enabled) splitter.enabled = true;

                    // BreakNow means the gameplay source has already decided this is a
                    // decisive hit. Supply at least the configured split threshold so
                    // its behaviour matches ApplyDamage with sufficient damage.
                    splitter.ProcessHit(directContext);
                }

                // Debris that already came loose is still a physical object, and an
                // explosion is the one hit that most obviously has to move it. Only
                // ApplyDamage shoved loose rubble, so a blast on an already-fractured prop
                // woke the remaining shell and left the pile in front of it untouched.
                // Radial whenever the caller supplied no travel direction, which is what
                // Explode does: a blast throws every piece away from its origin, while a
                // projectile using Break Immediately keeps pushing along its own flight.
                if (Profile != null)
                {
                    PushLooseFragments(directContext,
                        radial: context.ImpactDirection.sqrMagnitude <= 0.000001f);
                    m_skipNextLoosePushFragments.Clear();
                }

                ActivateRemaining(context);
                return;
            }
            m_health = 0f;
            if (Profile != null && ActivateAllOnFirstBreak && context.Radius > 0f)
            {
                DoBreak(new BreakContext(context.Point, context.Normal, 0f,
                    context.Energy, context.SourceId,
                    context.ImpactDirection, context.DirectionalForce));
            }
            else
            {
                DoBreak(context);
            }
        }

        public bool ActivateAllOnFirstBreak
        {
            get
            {
                // A structurally released shell is already one falling body, so leaving
                // untouched fragments behind would freeze part of that collapse in space.
                // Dynamic props are different: their authored Local/All Pieces choice must
                // still be honoured. Local mode transfers the intact velocity only to the
                // struck pieces and keeps the remainder attached for later hits.
                if (m_bodyReleased)
                    return true;
                if (FirstBreakActivation == FirstBreakActivationOverride.AllPieces)
                    return true;
                if (FirstBreakActivation == FirstBreakActivationOverride.Local)
                    return false;
                return Profile != null && Profile.ActivateAllOnFirstBreak;
            }
        }

        /// <summary>Convenience explosion entry point using profile defaults.</summary>
        public void Explode(Vector3 origin)
        {
            float force = Profile != null ? Profile.DefaultExplosionForce : 30f;
            float radius = Profile != null ? Profile.DefaultExplosionRadius : 3f;
            BreakWithoutSpread(new BreakContext(origin, Vector3.up, radius, force));
        }

        // ------------------------------------------------------------------ break / activation

        /// <summary>Returns activated fragment count, or -1 when the budget delayed everything.</summary>
        int DoBreak(in BreakContext context, int structuralGroup = -1,
            bool bypassBudget = false)
        {
            using (s_breakMarker.Auto())
            {
                if (structuralGroup < 0 && !ActivateAllOnFirstBreak)
                {
                    if (context.TargetStructuralGroup >= 0)
                        structuralGroup = context.TargetStructuralGroup;
                    else if (BakedAsWholeBuilding &&
                             context.ImpactDirection.sqrMagnitude > 0.000001f)
                        structuralGroup = ResolveStructuralGroup(context.Point);
                }
                // Localized activation: fragments inside the radius wake up, the rest stay
                // kinematic until a later break activates them.
                int desired = CountActivatable(context, structuralGroup);

                int granted = desired;
                if (UsesActiveBodyBudget && !bypassBudget && !ActivateAllOnFirstBreak)
                {
                    desired = Mathf.Min(desired,
                        Mathf.Max(0, Profile.MaxActiveFragments - m_activeCount -
                                     m_pendingActivationIndices.Count));
                    granted = DestructionBudgetManager.Instance.RequestActivation(
                        Profile.BudgetGroup, Profile.GroupMaxActiveBodies, desired);
                }

                // Do not hide the intact object or enter Fractured when the budget denied
                // every requested body. A later damage/break request can retry cleanly.
                if (granted == 0 && desired > 0)
                    return -1;

                Vector3 inheritedLinearVelocity = Vector3.zero;
                Vector3 inheritedAngularVelocity = Vector3.zero;
                // A hit can arrive before StructuralSupportNode's first FixedUpdate, and
                // Reset deliberately invalidates its cached grid. Preserve the intact probe
                // geometry while IntactRoot is still active; after the visibility swap the
                // disabled source colliders no longer expose usable bounds. The exact baked
                // fragment denominator is still retaken below, before any fragment wakes.
                StructuralNode?.PrepareFracturedBaselineBeforeVisibilityChange();
                PrepareIntactBodyForFracture(
                    out inheritedLinearVelocity, out inheritedAngularVelocity);

                EnsureFracturedVisible();
                m_state = DestructibleState.Fractured;

                // The support denominator must describe the complete baked shell. Capture
                // it after Fractured becomes queryable but before this hit removes any
                // fragments; otherwise the first destroyed column is silently redefined as
                // never having supported the arch and the surviving side reads as 100%.
                StructuralNode?.CaptureFracturedBaselineBeforeActivation();

                int activated = ActivateEligible(context, granted,
                    inheritedLinearVelocity, inheritedAngularVelocity,
                    structuralGroup);
                // A Dynamic intact object must keep its untouched remainder as one
                // moving body after every local first break. A direct hit normally carries
                // structural group 0 even on a single-mesh prop, so group scoping cannot be
                // used to decide whether this carrier is needed. Leaving that grouped
                // remainder as the old kinematic "supported shell" can freeze an untouched
                // barrel ring in mid-air after the pieces below it are destroyed.
                if (Profile != null &&
                    Profile.IntactBody == IntactBodyMode.Dynamic &&
                    !ActivateAllOnFirstBreak)
                    activated += ActivateDynamicResidualCluster(context,
                        inheritedLinearVelocity, inheritedAngularVelocity);
                int detachedVisual = DetachHitNonPhysicalFragments(
                    context, structuralGroup);
                int unsupportedCulled = 0;
                activated += ReleaseUnsupportedAfterFragmentChange(
                    context, structuralGroup,
                    activated > 0 || detachedVisual > 0,
                    out unsupportedCulled);
                if (structuralGroup < 0)
                {
                    detachedVisual += DetachResidualNonPhysicalFragments();
                }

                Broken?.Invoke(context);
                using (s_fxMarker.Auto())
                {
                    OnBrokenEvent?.Invoke();
                }

                return activated;
            }
        }

        /// <summary>
        /// Post-fracture localized activation: wakes not-yet-activated fragments inside the
        /// context radius through the normal budget path. Radius &lt;= 0 activates all
        /// remaining fragments. Returns the activated count, or -1 when the budget delayed
        /// everything (the caller can retry later).
        /// </summary>
        int ActivateRemaining(in BreakContext context, bool bypassBudget = false,
            int structuralGroup = -1,
            Vector3 inheritedLinearVelocity = default(Vector3),
            Vector3 inheritedAngularVelocity = default(Vector3))
        {
            using (s_breakMarker.Auto())
            {
                if (structuralGroup < 0)
                {
                    if (context.TargetStructuralGroup >= 0)
                        structuralGroup = context.TargetStructuralGroup;
                    else if (BakedAsWholeBuilding &&
                             context.ImpactDirection.sqrMagnitude > 0.000001f)
                        structuralGroup = ResolveStructuralGroup(context.Point);
                }
                int desired = CountActivatable(context, structuralGroup);
                if (desired == 0) return 0;

                int granted = desired;
                if (UsesActiveBodyBudget && !bypassBudget && !ActivateAllOnFirstBreak)
                {
                    desired = Mathf.Min(desired,
                        Mathf.Max(0, Profile.MaxActiveFragments - m_activeCount -
                                     m_pendingActivationIndices.Count));
                    granted = DestructionBudgetManager.Instance.RequestActivation(
                        Profile.BudgetGroup, Profile.GroupMaxActiveBodies, desired);
                }
                if (granted == 0 && desired > 0)
                    return -1;

                // New activity returns the object to the fractured lifecycle so the
                // settle/cleanup tick keeps processing the freshly activated pieces.
                m_state = DestructibleState.Fractured;

                int activated = ActivateEligible(context, granted,
                    inheritedLinearVelocity, inheritedAngularVelocity,
                    structuralGroup);
                int detachedVisual = DetachHitNonPhysicalFragments(
                    context, structuralGroup);
                int unsupportedCulled = 0;
                activated += ReleaseUnsupportedAfterFragmentChange(
                    context, structuralGroup,
                    activated > 0 || detachedVisual > 0,
                    out unsupportedCulled);
                if (structuralGroup < 0)
                    detachedVisual += DetachResidualNonPhysicalFragments();
                if (activated > 0 || detachedVisual > 0 || unsupportedCulled > 0)
                {
                    Broken?.Invoke(context);
                    using (s_fxMarker.Auto())
                    {
                        OnBrokenEvent?.Invoke();
                    }
                }
                return activated;
            }
        }

        /// <summary>
        /// A collision reported by FragmentRuntime already identifies the exact baked
        /// fragment that was struck. Activate that record only; impact energy still drives
        /// its physical launch, but it must not enlarge the gameplay selection radius and
        /// silently wake neighbouring pieces. StructuralSupportNode evaluates any resulting
        /// support loss later, through the authored support threshold and grace period.
        /// A fully disconnected local island is different: it has no support to measure and
        /// is swept immediately after this exact fragment changes state.
        /// </summary>
        int ActivateExactFragmentFromImpact(int index, in BreakContext context)
        {
            if (Profile == null || m_state < DestructibleState.Fractured ||
                index < 0 || index >= Fragments.Count ||
                m_pendingActivationIndices.Contains(index))
                return 0;

            FragmentRecord fragment = Fragments[index];
            if (fragment == null || fragment.Activated || fragment.Detached ||
                fragment.Transform == null)
                return 0;

            int activated = 0;
            int detached = 0;
            if (HasActivatablePhysics(fragment))
            {
                if (UsesActiveBodyBudget && !ActivateAllOnFirstBreak)
                {
                    int granted = DestructionBudgetManager.Instance.RequestActivation(
                        Profile.BudgetGroup, Profile.GroupMaxActiveBodies, 1);
                    if (granted <= 0) return -1;
                }
                ActivateFragment(index, context, Vector3.zero, Vector3.zero);
                activated = 1;
            }
            else
            {
                DetachFragmentRecord(fragment);
                detached = 1;
            }

            int unsupportedCulled = 0;
            activated += ReleaseUnsupportedAfterFragmentChange(
                context, fragment.StructuralGroup,
                activated > 0 || detached > 0,
                out unsupportedCulled);
            detached += unsupportedCulled + DetachResidualNonPhysicalFragments();
            if (activated > 0 || detached > 0)
            {
                Broken?.Invoke(context);
                using (s_fxMarker.Auto())
                {
                    OnBrokenEvent?.Invoke();
                }
            }
            return activated;
        }

        int ActivateExactFragmentFromCollision(int index,
            in BreakContext context, Collision collision)
        {
            GameObject source = collision != null ? collision.gameObject : null;
            if (source == null)
                return ActivateExactFragmentFromImpact(index, context);

            if (s_sharedPhysics.ImpactColliderBuffer == null)
                s_sharedPhysics.ImpactColliderBuffer = new List<Collider>(8);
            source.GetComponentsInChildren(s_sharedPhysics.ImpactColliderBuffer);
            s_sharedPhysics.CurrentImpactColliders = s_sharedPhysics.ImpactColliderBuffer;
            try
            {
                // The reporting collision is still being solved while this fragment is
                // changed from kinematic shell geometry into a dynamic body. Ignoring the
                // projectile for that transition prevents it from immediately driving the
                // newly released piece back into the intact shell.
                return ActivateExactFragmentFromImpact(index, context);
            }
            finally
            {
                s_sharedPhysics.CurrentImpactColliders = null;
            }
        }

        bool HasActivatablePhysics(FragmentRecord fragment) =>
            fragment != null && fragment.Transform != null &&
            (fragment.Body != null || DeferInactiveFragmentPhysics);

        int CountActivatable(in BreakContext context, int structuralGroup = -1)
        {
            int desired = 0;
            for (int i = 0; i < Fragments.Count; i++)
            {
                var f = Fragments[i];
                if (f == null || f.Activated || f.Detached ||
                    m_pendingActivationIndices.Contains(i) ||
                    !HasActivatablePhysics(f)) continue;
                if (structuralGroup >= 0 && f.StructuralGroup != structuralGroup)
                    continue;
                if (!InsideActivationRadius(f, context)) continue;
                desired++;
            }
            return desired;
        }

        int ActivateEligible(in BreakContext context, int granted,
            Vector3 inheritedLinearVelocity, Vector3 inheritedAngularVelocity,
            int structuralGroup = -1)
        {
            int accepted = 0;
            // Valid Radial Glass bakes are capped at authoring time, so their complete
            // shatter remains synchronous. Large non-glass structures may still amortise
            // physics activation without changing when the fractured hierarchy appears.
            bool batchActivation = granted > ActivationBatchThreshold &&
                                   (Profile == null ||
                                    Profile.Pattern != FracturePattern.RadialGlass);
            int immediateLimit = batchActivation ? LargeActivationBatchSize : granted;
            for (int i = 0; i < Fragments.Count && accepted < granted; i++)
            {
                var f = Fragments[i];
                if (f == null || f.Activated || f.Detached ||
                    m_pendingActivationIndices.Contains(i) ||
                    !HasActivatablePhysics(f) ||
                    f.Transform == null)
                    continue;
                if (structuralGroup >= 0 && f.StructuralGroup != structuralGroup)
                    continue;
                if (!InsideActivationRadius(f, context)) continue;
                if (accepted < immediateLimit)
                {
                    ActivateFragment(i, context,
                        inheritedLinearVelocity, inheritedAngularVelocity);
                }
                else
                {
                    m_pendingActivationIndices.Add(i);
                    m_pendingActivations.Enqueue(new PendingFragmentActivation
                    {
                        Index = i,
                        Context = context,
                        InheritedLinearVelocity = inheritedLinearVelocity,
                        InheritedAngularVelocity = inheritedAngularVelocity
                    });
                }
                accepted++;
            }
            return accepted;
        }

        /// <summary>
        /// Converts the still-intact remainder of a catastrophically landed Dynamic prop
        /// into one physical carrier plus kinematic visual/collider followers. Activating
        /// every baked Rigidbody and joining them created a large one-frame solver spike;
        /// this representation moves the same connected remainder with one simulating body.
        /// </summary>
        int ActivateDynamicResidualCluster(in BreakContext impact,
            Vector3 inheritedLinearVelocity, Vector3 inheritedAngularVelocity)
        {
            ClearDynamicClusterLinks();
            int count = Fragments.Count;
            if (m_dynamicClusterMembers == null ||
                m_dynamicClusterMembers.Length != count)
                m_dynamicClusterMembers = new bool[count];
            else
                Array.Clear(m_dynamicClusterMembers, 0, count);
            if (m_dynamicClusterOriginalParents == null ||
                m_dynamicClusterOriginalParents.Length != count)
                m_dynamicClusterOriginalParents = new Transform[count];

            var candidates = new bool[count];
            int candidateCount = 0;
            int carrierIndex = -1;
            float carrierVolume = -1f;
            float totalMass = 0f;
            for (int i = 0; i < count; i++)
            {
                FragmentRecord fragment = Fragments[i];
                if (fragment == null || fragment.Activated || fragment.Detached ||
                    fragment.Body == null || fragment.Transform == null ||
                    !fragment.Transform.gameObject.activeInHierarchy)
                    continue;
                candidates[i] = true;
                candidateCount++;
                m_dynamicClusterOriginalParents[i] = fragment.Transform.parent;
                totalMass += Mathf.Max(0.001f, fragment.Mass);
                if (fragment.Volume > carrierVolume)
                {
                    carrierVolume = fragment.Volume;
                    carrierIndex = i;
                }
            }

            if (candidateCount == 0 || carrierIndex < 0)
                return 0;

            // The carrier is created with no launch of its own. Once the compound has
            // been assembled below, the original hit is applied exactly once to the whole
            // connected remainder.
            var clusterContext = new BreakContext(impact.Point, impact.Normal,
                0f, 0f, impact.SourceId, Vector3.zero, 0f);
            ActivateFragment(carrierIndex, clusterContext,
                inheritedLinearVelocity, inheritedAngularVelocity);
            FragmentRecord carrier = Fragments[carrierIndex];
            carrier.Body.mass = Mathf.Max(0.001f, totalMass);
            m_dynamicClusterCarrierIndex = carrierIndex;
            m_dynamicClusterMembers[carrierIndex] = true;

            for (int i = 0; i < count; i++)
            {
                if (!candidates[i] || i == carrierIndex) continue;
                FragmentRecord follower = Fragments[i];
                follower.Activated = true;
                follower.Settled = false;
                follower.ActivatedTime = Time.time;
                follower.SleepTimer = 0f;
                follower.Body.isKinematic = true;
                follower.Body.detectCollisions = true;
                UseExactFragmentCollider(follower);
                follower.Transform.SetParent(carrier.Transform, true);
                m_dynamicClusterMembers[i] = true;
            }
            int releasedIslands = ReleaseDisconnectedDynamicClusterFollowers(
                inheritedLinearVelocity, inheritedAngularVelocity,
                IntactBody != null
                    ? IntactBody.worldCenterOfMass : transform.position);
            RebuildDynamicClusterProxyCollider();
            // Positive values are explicit API/projectile overrides. A negative value is
            // the automatic collision path: the intact body's real collision impulse is
            // already inherited above and must not be added a second time.
            if (!m_applyingCollisionDamage || impact.DirectionalForce >= 0f)
                ApplyImpactToDynamicClusterCarrier(impact);
            return 1 + releasedIslands;
        }

        bool IsDynamicClusterMember(int index) =>
            m_dynamicClusterMembers != null && index >= 0 &&
            index < m_dynamicClusterMembers.Length &&
            m_dynamicClusterMembers[index];

        bool DetachDynamicClusterFragment(int index)
        {
            return ChangeDynamicClusterMembership(index, true);
        }

        bool RemoveReplacedDynamicClusterFragment(int index)
        {
            return ChangeDynamicClusterMembership(index, false);
        }

        bool ChangeDynamicClusterMembership(int index, bool activateDetached)
        {
            if (!IsDynamicClusterMember(index)) return false;
            RemoveDynamicClusterProxyCollider();
            FragmentRecord oldCarrier = m_dynamicClusterCarrierIndex >= 0 &&
                                        m_dynamicClusterCarrierIndex < Fragments.Count
                ? Fragments[m_dynamicClusterCarrierIndex] : null;
            Vector3 inheritedLinear = oldCarrier?.Body != null
#if UNITY_6000_0_OR_NEWER
                ? oldCarrier.Body.linearVelocity
#else
                ? oldCarrier.Body.velocity
#endif
                : Vector3.zero;
            Vector3 inheritedAngular = oldCarrier?.Body != null
                ? oldCarrier.Body.angularVelocity : Vector3.zero;
            Vector3 inheritedVelocityOrigin = oldCarrier?.Body != null
                ? oldCarrier.Body.worldCenterOfMass : transform.position;

            FragmentRecord detached = Fragments[index];
            Transform originalParent = m_dynamicClusterOriginalParents != null &&
                                       index < m_dynamicClusterOriginalParents.Length
                ? m_dynamicClusterOriginalParents[index] : FracturedRoot?.transform;
            detached.Transform.SetParent(originalParent, true);
            m_dynamicClusterMembers[index] = false;

            if (activateDetached)
            {
                detached.Body.mass = Mathf.Max(0.001f, detached.Mass);
                detached.Body.detectCollisions = true;
                UseDynamicFragmentCollider(detached);
            }
            else
            {
                // A level-two or runtime split already replaced this visual/physics
                // object. It must leave the residual compound without briefly becoming
                // live debris again, otherwise the hidden parent remains a collider and
                // can bridge two visibly separate islands under one carrier Rigidbody.
                detached.Body.isKinematic = true;
                detached.Body.detectCollisions = false;
                if (detached.Collider != null) detached.Collider.enabled = false;
                SetCollidersEnabled(detached.DynamicColliders, false);
                if (detached.DynamicCollider != null)
                    detached.DynamicCollider.enabled = false;
            }

            if (index != m_dynamicClusterCarrierIndex)
            {
                if (activateDetached)
                {
                    detached.Activated = false;
                    ActivateFragment(index, new BreakContext(
                        detached.Transform.position, Vector3.up, 0f, 0f),
                        inheritedLinear, inheritedAngular,
                        inheritedVelocityOrigin);
                }
                ReleaseDisconnectedDynamicClusterFollowers(inheritedLinear,
                    inheritedAngular, inheritedVelocityOrigin);
                RebuildDynamicClusterProxyCollider();
                return true;
            }

            int newCarrier = -1;
            int retainedCount = 0;
            float bestVolume = -1f;
            for (int i = 0; i < Fragments.Count; i++)
            {
                if (!IsDynamicClusterMember(i)) continue;
                retainedCount++;
                if (Fragments[i].Volume <= bestVolume) continue;
                bestVolume = Fragments[i].Volume;
                newCarrier = i;
            }

            if (retainedCount <= 1)
            {
                if (newCarrier >= 0)
                {
                    FragmentRecord last = Fragments[newCarrier];
                    last.Transform.SetParent(m_dynamicClusterOriginalParents[newCarrier],
                        true);
                    last.Activated = false;
                    ActivateFragment(newCarrier, new BreakContext(last.Transform.position,
                        Vector3.up, 0f, 0f), inheritedLinear, inheritedAngular,
                        inheritedVelocityOrigin);
                    m_dynamicClusterMembers[newCarrier] = false;
                }
                m_dynamicClusterCarrierIndex = -1;
                return true;
            }

            for (int i = 0; i < Fragments.Count; i++)
            {
                if (!IsDynamicClusterMember(i)) continue;
                Fragments[i].Transform.SetParent(
                    m_dynamicClusterOriginalParents[i], true);
            }
            FragmentRecord carrier = Fragments[newCarrier];
            carrier.Activated = false;
            ActivateFragment(newCarrier, new BreakContext(carrier.Transform.position,
                Vector3.up, 0f, 0f), inheritedLinear, inheritedAngular,
                inheritedVelocityOrigin);
            float retainedMass = 0f;
            for (int i = 0; i < Fragments.Count; i++)
            {
                if (!IsDynamicClusterMember(i)) continue;
                retainedMass += Mathf.Max(0.001f, Fragments[i].Mass);
                if (i == newCarrier) continue;
                Fragments[i].Body.isKinematic = true;
                UseExactFragmentCollider(Fragments[i]);
                Fragments[i].Transform.SetParent(carrier.Transform, true);
            }
            carrier.Body.mass = Mathf.Max(0.001f, retainedMass);
            m_dynamicClusterCarrierIndex = newCarrier;
            ReleaseDisconnectedDynamicClusterFollowers(inheritedLinear,
                inheritedAngular, inheritedVelocityOrigin);
            RebuildDynamicClusterProxyCollider();
            return true;
        }

        /// <summary>
        /// Drops islands which no longer have a physically touching route to the current
        /// carrier. This runs only when the cluster is created or loses a member; it is
        /// deliberately not a FixedUpdate contact scan. Baked neighbours and tight bounds
        /// cheaply identify candidates, but collider distance is the final authority. A
        /// stale baked edge must never leave a visual shard flying as a kinematic follower.
        /// </summary>
        int ReleaseDisconnectedDynamicClusterFollowers(
            Vector3 inheritedLinear, Vector3 inheritedAngular)
        {
            return ReleaseDisconnectedDynamicClusterFollowers(inheritedLinear,
                inheritedAngular, transform.position);
        }

        int ReleaseDisconnectedDynamicClusterFollowers(
            Vector3 inheritedLinear, Vector3 inheritedAngular,
            Vector3 inheritedVelocityOrigin)
        {
            int carrierIndex = m_dynamicClusterCarrierIndex;
            if (!IsDynamicClusterMember(carrierIndex)) return 0;
            int count = Fragments.Count;
            if (m_supportVisited == null || m_supportVisited.Length != count)
                m_supportVisited = new bool[count];
            else
                Array.Clear(m_supportVisited, 0, count);
            m_supportQueue.Clear();
            m_supportVisited[carrierIndex] = true;
            m_supportQueue.Enqueue(carrierIndex);
            float contactTolerance = DynamicClusterContactTolerance(Profile);

            // The carrier proxy disables every member collider. Restore each member's
            // dynamic-safe shape for this one connectivity pass; RebuildDynamicCluster-
            // ProxyCollider disables them again immediately afterwards. This avoids both
            // per-frame overlap scans and illegal non-convex contacts on a dynamic body.
            for (int i = 0; i < count; i++)
                if (IsDynamicClusterMember(i))
                    UseClusterConnectivityCollider(Fragments[i]);

            while (m_supportQueue.Count > 0)
            {
                int current = m_supportQueue.Dequeue();
                FragmentRecord source = Fragments[current];
                for (int candidate = 0; candidate < count; candidate++)
                {
                    if (m_supportVisited[candidate] ||
                        !IsDynamicClusterMember(candidate))
                        continue;
                    FragmentRecord other = Fragments[candidate];
                    bool bakedNeighbour = source.SupportNeighbours != null &&
                        source.SupportNeighbours.Contains(candidate);
                    if (!bakedNeighbour && other.SupportNeighbours != null)
                        bakedNeighbour = other.SupportNeighbours.Contains(current);
                    bool tightBounds = StructuralBoundsTouch(
                        GetStructuralBounds(source), GetStructuralBounds(other),
                        contactTolerance);
                    if (!bakedNeighbour && !tightBounds)
                        continue;
                    if (!DynamicClusterFragmentsTouch(source, other,
                            contactTolerance))
                        continue;
                    m_supportVisited[candidate] = true;
                    m_supportQueue.Enqueue(candidate);
                }
            }

            m_unsupportedBodies.Clear();
            for (int i = 0; i < count; i++)
                if (IsDynamicClusterMember(i) && !m_supportVisited[i])
                    m_unsupportedBodies.Add(i);
            if (m_unsupportedBodies.Count == 0) return 0;

            bool batch = m_unsupportedBodies.Count > ActivationBatchThreshold;
            int immediate = batch
                ? Mathf.Min(LargeActivationBatchSize, m_unsupportedBodies.Count)
                : m_unsupportedBodies.Count;
            for (int order = 0; order < m_unsupportedBodies.Count; order++)
            {
                int index = m_unsupportedBodies[order];
                FragmentRecord orphan = Fragments[index];
                orphan.Transform.SetParent(m_dynamicClusterOriginalParents[index],
                    true);
                orphan.Body.mass = Mathf.Max(0.001f, orphan.Mass);
                orphan.Body.detectCollisions = true;
                UseDynamicFragmentCollider(orphan);
                orphan.Activated = false;
                m_dynamicClusterMembers[index] = false;
                var release = new BreakContext(orphan.Transform.position,
                    Vector3.down, 0f, 0f);
                if (order < immediate)
                {
                    ActivateFragment(index, release, inheritedLinear,
                        inheritedAngular, inheritedVelocityOrigin);
                }
                else
                {
                    m_pendingActivationIndices.Add(index);
                    m_pendingActivations.Enqueue(new PendingFragmentActivation
                    {
                        Index = index,
                        Context = release,
                        InheritedLinearVelocity = inheritedLinear,
                        InheritedAngularVelocity = inheritedAngular,
                        InheritedVelocityOrigin = inheritedVelocityOrigin,
                        HasInheritedVelocityOrigin = true
                    });
                }
            }
            return m_unsupportedBodies.Count;
        }

        /// <summary>
        /// Allocation-free narrow phase used only while a residual cluster is created or
        /// loses a member. Bounds reject the common non-contact case; alternating closest-
        /// point queries distinguish genuinely touching convex compounds from long or
        /// rotated shapes whose AABBs merely overlap. Legacy bakes without a usable
        /// collider retain the old tight-bounds fallback instead of losing all support.
        /// </summary>
        internal static bool DynamicClusterFragmentsTouch(FragmentRecord first,
            FragmentRecord second, float tolerance)
        {
            if (first == null || second == null) return false;
            tolerance = Mathf.Max(0f, tolerance);
            if (!StructuralBoundsTouch(GetStructuralBounds(first),
                    GetStructuralBounds(second), tolerance))
                return false;

            bool firstHasCollider = HasEnabledClusterCollider(first);
            bool secondHasCollider = HasEnabledClusterCollider(second);
            if (!firstHasCollider || !secondHasCollider)
                return true;

            int firstSlots = ClusterColliderSlotCount(first);
            int secondSlots = ClusterColliderSlotCount(second);
            for (int a = 0; a < firstSlots; a++)
            {
                Collider firstCollider = GetClusterCollider(first, a);
                if (firstCollider == null || !firstCollider.enabled ||
                    !firstCollider.gameObject.activeInHierarchy)
                    continue;
                for (int b = 0; b < secondSlots; b++)
                {
                    Collider secondCollider = GetClusterCollider(second, b);
                    if (secondCollider == null || !secondCollider.enabled ||
                        !secondCollider.gameObject.activeInHierarchy)
                        continue;
                    if (ClusterCollidersTouch(firstCollider, secondCollider,
                            tolerance))
                        return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Residual fragments originate from the same bake and their shared cut faces are
        /// already coincident. A large support-ray distance is useful for finding ground,
        /// but must not become a glue radius between separate islands: 5 cm was enough to
        /// keep visibly disconnected barrel halves under one carrier Rigidbody.
        /// </summary>
        internal static float DynamicClusterContactTolerance(
            DestructionProfile profile) => Mathf.Clamp(
                profile != null ? profile.SupportProbeDistance * 0.04f : 0.01f,
                0.003f, 0.012f);

        static bool ClusterCollidersTouch(Collider first, Collider second,
            float tolerance)
        {
            if (!StructuralBoundsTouch(first.bounds, second.bounds, tolerance))
                return false;
            float toleranceSqr = tolerance * tolerance;

            Vector3 point = second.bounds.center;
            for (int pass = 0; pass < 3; pass++)
            {
                Vector3 onFirst = first.ClosestPoint(point);
                Vector3 onSecond = second.ClosestPoint(onFirst);
                if ((onFirst - onSecond).sqrMagnitude <= toleranceSqr)
                    return true;
                point = onSecond;
            }

            point = first.bounds.center;
            for (int pass = 0; pass < 3; pass++)
            {
                Vector3 onSecond = second.ClosestPoint(point);
                Vector3 onFirst = first.ClosestPoint(onSecond);
                if ((onFirst - onSecond).sqrMagnitude <= toleranceSqr)
                    return true;
                point = onFirst;
            }
            return false;
        }

        static bool HasEnabledClusterCollider(FragmentRecord fragment)
        {
            int slots = ClusterColliderSlotCount(fragment);
            for (int i = 0; i < slots; i++)
            {
                Collider collider = GetClusterCollider(fragment, i);
                if (collider != null && collider.enabled &&
                    collider.gameObject.activeInHierarchy)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Leaves a member holding one shape the connectivity narrow phase can measure.
        ///
        /// UseDynamicFragmentCollider does nothing at all for a bake that has no convex
        /// companion — which is every fragment under the default Convex Mesh strategy, where
        /// the baked collider is already dynamic-safe — and the carrier proxy has by then
        /// disabled that baked collider. Such a member used to enter the pass with no enabled
        /// shape whatsoever, and a member without a shape is reported as touching everything
        /// its bounding box overlaps. On a curved shell that means every other piece: the
        /// AABBs of barrel staves all overlap across the empty middle, so a shard holding on
        /// to nothing stayed welded to the far side of the shell and flew along with it.
        /// </summary>
        static void UseClusterConnectivityCollider(FragmentRecord fragment)
        {
            UseDynamicFragmentCollider(fragment);
            if (!HasEnabledClusterCollider(fragment))
                UseExactFragmentCollider(fragment);
        }

        static int ClusterColliderSlotCount(FragmentRecord fragment) =>
            fragment == null ? 0 : 2 +
            (fragment.DynamicColliders != null
                ? fragment.DynamicColliders.Count : 0);

        static Collider GetClusterCollider(FragmentRecord fragment, int slot)
        {
            if (fragment == null || slot < 0) return null;
            if (slot == 0) return fragment.Collider;
            if (slot == 1) return fragment.DynamicCollider;
            int compoundIndex = slot - 2;
            return fragment.DynamicColliders != null && compoundIndex >= 0 &&
                   compoundIndex < fragment.DynamicColliders.Count
                ? fragment.DynamicColliders[compoundIndex]
                : null;
        }

        /// <summary>
        /// Gives the connected visual remainder a hole-preserving compound collider under
        /// its single carrier Rigidbody. Each proxy reuses the baked convex collider shape
        /// of its fragment. The complete compound may therefore be concave (for example a
        /// barrel shell), while every individual PhysX shape remains dynamic-safe convex.
        /// Legacy bakes without a convex companion fall back to one local bounds box only
        /// for the affected member rather than filling the complete object's cavity.
        /// </summary>
        void RebuildDynamicClusterProxyCollider()
        {
            RemoveDynamicClusterProxyCollider();
            if (m_dynamicClusterCarrierIndex < 0 ||
                m_dynamicClusterCarrierIndex >= Fragments.Count)
                return;
            FragmentRecord carrier = Fragments[m_dynamicClusterCarrierIndex];
            if (carrier?.Body == null || carrier.Transform == null) return;

            float connectedMass = 0f;
            for (int i = 0; i < Fragments.Count; i++)
            {
                if (!IsDynamicClusterMember(i)) continue;
                FragmentRecord member = Fragments[i];
                if (member?.Transform == null) continue;
                connectedMass += Mathf.Max(0.001f, member.Mass);
                int before = m_dynamicClusterProxyColliders.Count;
                if (member.DynamicColliders != null)
                {
                    for (int slot = 0; slot < member.DynamicColliders.Count; slot++)
                        AddDynamicClusterProxy(member.DynamicColliders[slot],
                            carrier.Transform, i, slot);
                }
                if (m_dynamicClusterProxyColliders.Count == before)
                    AddDynamicClusterProxy(member.DynamicCollider,
                        carrier.Transform, i, 0);
                if (m_dynamicClusterProxyColliders.Count == before)
                    AddDynamicClusterProxy(member.Collider,
                        carrier.Transform, i, 0);
                if (m_dynamicClusterProxyColliders.Count == before)
                    AddDynamicClusterBoundsFallback(member, carrier.Transform, i);
            }
            if (m_dynamicClusterProxyColliders.Count == 0) return;

            // Only this compound belongs to the simulating carrier. The original
            // fragment colliders remain available for exact local selection math, but
            // do not create independent contacts or solver work while connected.
            for (int i = 0; i < Fragments.Count; i++)
            {
                if (!IsDynamicClusterMember(i)) continue;
                FragmentRecord member = Fragments[i];
                if (member?.Body != null)
                    member.Body.detectCollisions = i == m_dynamicClusterCarrierIndex;
                if (member?.Collider != null) member.Collider.enabled = false;
                SetCollidersEnabled(member?.DynamicColliders, false);
                if (member?.DynamicCollider != null)
                    member.DynamicCollider.enabled = false;
            }

            carrier.Body.mass = Mathf.Max(0.001f, connectedMass);
            carrier.Body.detectCollisions = true;
            carrier.Body.WakeUp();
        }

        void AddDynamicClusterProxy(Collider source, Transform carrier,
            int memberIndex, int slot)
        {
            if (source == null || carrier == null) return;
            // A non-convex MeshCollider cannot participate in a dynamic compound.
            // Exact source meshes therefore use their baked convex companion above.
            if (source is MeshCollider sourceMesh &&
                (!sourceMesh.convex || sourceMesh.sharedMesh == null))
                return;

            var proxyObject = new GameObject($"__QD_ClusterProxy_{memberIndex}_{slot}");
            proxyObject.layer = source.gameObject.layer;
            proxyObject.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave;
            Transform proxyTransform = proxyObject.transform;
            proxyTransform.SetParent(carrier, false);
            MatchProxyTransform(proxyTransform, source.transform, carrier);

            Collider proxy = null;
            if (source is BoxCollider sourceBox)
            {
                var box = proxyObject.AddComponent<BoxCollider>();
                box.center = sourceBox.center;
                box.size = sourceBox.size;
                proxy = box;
                if (m_dynamicClusterProxyCollider == null)
                    m_dynamicClusterProxyCollider = box;
            }
            else if (source is SphereCollider sourceSphere)
            {
                var sphere = proxyObject.AddComponent<SphereCollider>();
                sphere.center = sourceSphere.center;
                sphere.radius = sourceSphere.radius;
                proxy = sphere;
            }
            else if (source is CapsuleCollider sourceCapsule)
            {
                var capsule = proxyObject.AddComponent<CapsuleCollider>();
                capsule.center = sourceCapsule.center;
                capsule.radius = sourceCapsule.radius;
                capsule.height = sourceCapsule.height;
                capsule.direction = sourceCapsule.direction;
                proxy = capsule;
            }
            else if (source is MeshCollider mesh)
            {
                var meshProxy = proxyObject.AddComponent<MeshCollider>();
                meshProxy.cookingOptions = mesh.cookingOptions;
                meshProxy.convex = true;
                meshProxy.sharedMesh = mesh.sharedMesh;
                proxy = meshProxy;
            }

            if (proxy == null)
            {
                if (Application.isPlaying) Destroy(proxyObject);
                else DestroyImmediate(proxyObject);
                return;
            }
            proxy.sharedMaterial = source.sharedMaterial != null
                ? source.sharedMaterial : DefaultDebrisContactMaterial;
            proxy.isTrigger = source.isTrigger;
            proxy.contactOffset = source.contactOffset;
            proxy.enabled = true;
            m_dynamicClusterProxyColliders.Add(proxy);
        }

        static void MatchProxyTransform(Transform proxy, Transform source,
            Transform carrier)
        {
            if (proxy == null || source == null || carrier == null) return;
            if (source == carrier)
            {
                proxy.localPosition = Vector3.zero;
                proxy.localRotation = Quaternion.identity;
                proxy.localScale = Vector3.one;
                return;
            }

            proxy.position = source.position;
            proxy.rotation = source.rotation;
            Vector3 sourceScale = source.lossyScale;
            Vector3 carrierScale = carrier.lossyScale;
            proxy.localScale = new Vector3(
                SafeScaleRatio(sourceScale.x, carrierScale.x),
                SafeScaleRatio(sourceScale.y, carrierScale.y),
                SafeScaleRatio(sourceScale.z, carrierScale.z));
        }

        static float SafeScaleRatio(float source, float carrier) =>
            Mathf.Abs(carrier) > 0.000001f ? source / carrier : source;

        void AddDynamicClusterBoundsFallback(FragmentRecord member,
            Transform carrier, int memberIndex)
        {
            Bounds world = member.Renderer != null
                ? member.Renderer.bounds
                : GetStructuralBounds(member);
            if (world.size.sqrMagnitude <= 0.000001f) return;
            bool initialized = false;
            Bounds localBounds = default(Bounds);
            Vector3 min = world.min;
            Vector3 max = world.max;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 worldCorner = new Vector3(
                    (corner & 1) == 0 ? min.x : max.x,
                    (corner & 2) == 0 ? min.y : max.y,
                    (corner & 4) == 0 ? min.z : max.z);
                Vector3 local = carrier.InverseTransformPoint(worldCorner);
                if (!initialized)
                {
                    localBounds = new Bounds(local, Vector3.zero);
                    initialized = true;
                }
                else
                    localBounds.Encapsulate(local);
            }
            if (!initialized) return;

            var proxyObject = new GameObject(
                $"__QD_ClusterProxyFallback_{memberIndex}");
            proxyObject.layer = member.Transform.gameObject.layer;
            proxyObject.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave;
            proxyObject.transform.SetParent(carrier, false);
            var proxy = proxyObject.AddComponent<BoxCollider>();
            proxy.center = localBounds.center;
            proxy.size = new Vector3(
                Mathf.Max(0.01f, localBounds.size.x),
                Mathf.Max(0.01f, localBounds.size.y),
                Mathf.Max(0.01f, localBounds.size.z));
            ApplyDefaultDebrisContactMaterial(proxy);
            m_dynamicClusterProxyColliders.Add(proxy);
            if (m_dynamicClusterProxyCollider == null)
                m_dynamicClusterProxyCollider = proxy;
        }

        void RemoveDynamicClusterProxyCollider()
        {
            for (int i = 0; i < m_dynamicClusterProxyColliders.Count; i++)
            {
                Collider proxy = m_dynamicClusterProxyColliders[i];
                if (proxy == null) continue;
                proxy.enabled = false;
                if (Application.isPlaying)
                    Destroy(proxy.gameObject);
                else
                    DestroyImmediate(proxy.gameObject);
            }
            m_dynamicClusterProxyColliders.Clear();
            m_dynamicClusterProxyCollider = null;
        }

        int DetachDynamicClusterFragmentsInRadius(in DamageContext context)
        {
            m_skipNextLoosePushFragments.Clear();
            m_dynamicClusterHitMembers.Clear();
            bool allMembers = context.Radius <= 0f;
            float radiusSqr = context.Radius * context.Radius;
            for (int i = 0; i < Fragments.Count; i++)
            {
                if (!IsDynamicClusterMember(i)) continue;
                FragmentRecord fragment = Fragments[i];
                if (fragment == null || fragment.Transform == null ||
                    !fragment.Transform.gameObject.activeInHierarchy)
                    continue;
                if (allMembers ||
                    (GetFragmentClosestPoint(fragment, context.Point) -
                     context.Point).sqrMagnitude <= radiusSqr)
                    m_dynamicClusterHitMembers.Add(i);
            }
            if (m_dynamicClusterHitMembers.Count == 0)
                return 0;

            RemoveDynamicClusterProxyCollider();
            int oldCarrierIndex = m_dynamicClusterCarrierIndex;
            FragmentRecord oldCarrier = oldCarrierIndex >= 0 &&
                                        oldCarrierIndex < Fragments.Count
                ? Fragments[oldCarrierIndex] : null;
            Vector3 inheritedLinear = oldCarrier?.Body != null
#if UNITY_6000_0_OR_NEWER
                ? oldCarrier.Body.linearVelocity
#else
                ? oldCarrier.Body.velocity
#endif
                : Vector3.zero;
            Vector3 inheritedAngular = oldCarrier?.Body != null
                ? oldCarrier.Body.angularVelocity : Vector3.zero;
            Vector3 inheritedVelocityOrigin = oldCarrier?.Body != null
                ? oldCarrier.Body.worldCenterOfMass : transform.position;
            bool carrierWasHit = m_dynamicClusterHitMembers.Contains(
                oldCarrierIndex);

            // If the old carrier itself is leaving, first restore every retained
            // member's normal hierarchy. This prevents a selected carrier from dragging
            // visually separate survivors along as children for even one physics step.
            if (carrierWasHit)
            {
                for (int i = 0; i < Fragments.Count; i++)
                {
                    if (!IsDynamicClusterMember(i) ||
                        m_dynamicClusterHitMembers.Contains(i))
                        continue;
                    Fragments[i].Transform.SetParent(
                        m_dynamicClusterOriginalParents[i], true);
                }
            }

            // Release every fragment whose actual baked surface intersects the local
            // hit sphere. Doing this as one batch avoids rebuilding the compound proxy
            // and rerunning connectivity once per pellet/fragment.
            for (int hit = 0; hit < m_dynamicClusterHitMembers.Count; hit++)
            {
                int index = m_dynamicClusterHitMembers[hit];
                if (!IsDynamicClusterMember(index)) continue;
                FragmentRecord detached = Fragments[index];
                detached.Transform.SetParent(
                    m_dynamicClusterOriginalParents[index], true);
                detached.Body.mass = Mathf.Max(0.001f, detached.Mass);
                detached.Body.detectCollisions = true;
                UseDynamicFragmentCollider(detached);
                m_dynamicClusterMembers[index] = false;
                detached.Activated = false;
                ActivateFragment(index, new BreakContext(
                    detached.Transform.position, Vector3.up, 0f, 0f),
                    inheritedLinear, inheritedAngular,
                    inheritedVelocityOrigin);
                m_skipNextLoosePushFragments.Add(index);
            }

            int retainedCount = 0;
            int newCarrierIndex = -1;
            float bestVolume = -1f;
            for (int i = 0; i < Fragments.Count; i++)
            {
                if (!IsDynamicClusterMember(i)) continue;
                retainedCount++;
                if (Fragments[i].Volume <= bestVolume) continue;
                bestVolume = Fragments[i].Volume;
                newCarrierIndex = i;
            }

            if (retainedCount <= 1)
            {
                if (newCarrierIndex >= 0)
                {
                    FragmentRecord last = Fragments[newCarrierIndex];
                    last.Transform.SetParent(
                        m_dynamicClusterOriginalParents[newCarrierIndex], true);
                    last.Activated = false;
                    ActivateFragment(newCarrierIndex, new BreakContext(
                        last.Transform.position, Vector3.up, 0f, 0f),
                        inheritedLinear, inheritedAngular,
                        inheritedVelocityOrigin);
                    m_dynamicClusterMembers[newCarrierIndex] = false;
                }
                m_dynamicClusterCarrierIndex = -1;
            }
            else
            {
                if (!carrierWasHit && IsDynamicClusterMember(oldCarrierIndex))
                    newCarrierIndex = oldCarrierIndex;
                else
                {
                    FragmentRecord replacementCarrier =
                        Fragments[newCarrierIndex];
                    replacementCarrier.Activated = false;
                    ActivateFragment(newCarrierIndex, new BreakContext(
                        replacementCarrier.Transform.position,
                        Vector3.up, 0f, 0f),
                        inheritedLinear, inheritedAngular,
                        inheritedVelocityOrigin);
                }

                FragmentRecord carrier = Fragments[newCarrierIndex];
                float retainedMass = 0f;
                for (int i = 0; i < Fragments.Count; i++)
                {
                    if (!IsDynamicClusterMember(i)) continue;
                    retainedMass += Mathf.Max(0.001f, Fragments[i].Mass);
                    if (i == newCarrierIndex) continue;
                    Fragments[i].Body.isKinematic = true;
                    UseExactFragmentCollider(Fragments[i]);
                    Fragments[i].Transform.SetParent(carrier.Transform, true);
                }
                carrier.Body.mass = Mathf.Max(0.001f, retainedMass);
                m_dynamicClusterCarrierIndex = newCarrierIndex;
                ReleaseDisconnectedDynamicClusterFollowers(inheritedLinear,
                    inheritedAngular, inheritedVelocityOrigin);
                RebuildDynamicClusterProxyCollider();
            }

            // Apply the authored local hit once to every released member. The ordinary
            // loose-debris pass skips this set, preventing a double impulse.
            float energy = context.BreakEnergy >= 0f
                ? context.BreakEnergy
                : context.Amount;
            bool stableDynamicDirectHit = !m_applyingCollisionDamage &&
                context.ImpactDirection.sqrMagnitude > 0.000001f;
            for (int hit = 0; hit < m_dynamicClusterHitMembers.Count; hit++)
            {
                FragmentRecord detached =
                    Fragments[m_dynamicClusterHitMembers[hit]];
                if (detached?.Body == null || detached.Body.isKinematic) continue;
                detached.Body.WakeUp();
                float push = Mathf.Max(0f, energy) *
                             Mathf.Max(0f, Profile != null
                                 ? Profile.FragmentPushMultiplier : 0f);
                Vector3 direction = ResolveImpactPushDirection(
                    context.ImpactDirection, context.Point,
                    detached.Body.worldCenterOfMass, context.Normal);
                direction = ResolveGroundSafeEjectDirection(direction,
                    IsFragmentNearStaticGround(detached));
                if (stableDynamicDirectHit)
                {
                    float boundedPush = ClampLaunchImpulse(push, detached.Body,
                        ForceMode.Impulse);
                    float pushSpeed = ForceToSpeed(boundedPush, detached.Body,
                        ForceMode.Impulse);
                    float directionalSpeed = ForceToSpeed(ResolveDirectionalImpactImpulse(
                        Profile, energy, context.DirectionalForce), detached.Body, ForceMode.Impulse);
                    ApplyCappedDirectVelocity(detached.Body, direction,
                        Mathf.Max(pushSpeed, directionalSpeed),
                        Profile.MaxFragmentLaunchSpeed);
#if UNITY_6000_0_OR_NEWER
                    float descentSpeed = Mathf.Max(0f,
                        -detached.Body.linearVelocity.y);
#else
                    float descentSpeed = Mathf.Max(0f,
                        -detached.Body.velocity.y);
#endif
                    LimitLaunchTowardStaticGround(detached, detached.Body,
                        descentSpeed);
                }
                else if (push > 0f)
                {
                    detached.Body.AddForceAtPosition(direction *
                        ClampLaunchImpulse(push, detached.Body,
                            ForceMode.Impulse), context.Point,
                        ForceMode.Impulse);
                }
                if (!stableDynamicDirectHit)
                    ApplyDirectionalImpactForce(detached.Body, context.Point,
                        context.ImpactDirection, energy,
                        context.DirectionalForce);
            }
            if (!m_applyingCollisionDamage || context.DirectionalForce >= 0f)
                ApplyImpactToDynamicClusterCarrier(new BreakContext(context.Point,
                    context.Normal, context.Radius,
                    context.BreakEnergy >= 0f ? context.BreakEnergy : context.Amount,
                    context.SourceId, context.ImpactDirection,
                    context.DirectionalForce));
            return m_dynamicClusterHitMembers.Count;
        }

        int FindClosestDynamicClusterMember(Vector3 point,
            out float closestSqr)
        {
            closestSqr = float.MaxValue;
            int closestIndex = -1;
            if (m_dynamicClusterMembers == null) return closestIndex;
            for (int i = 0; i < Fragments.Count; i++)
            {
                if (!IsDynamicClusterMember(i)) continue;
                FragmentRecord fragment = Fragments[i];
                if (fragment == null || fragment.Transform == null ||
                    !fragment.Transform.gameObject.activeInHierarchy)
                    continue;
                float distanceSqr = (GetFragmentClosestPoint(fragment, point) -
                                     point).sqrMagnitude;
                if (distanceSqr >= closestSqr)
                    continue;
                closestSqr = distanceSqr;
                closestIndex = i;
            }
            return closestIndex;
        }

        void ClearDynamicClusterLinks()
        {
            RemoveDynamicClusterProxyCollider();
            if (m_dynamicClusterMembers != null)
            {
                for (int i = 0; i < m_dynamicClusterMembers.Length &&
                                i < Fragments.Count; i++)
                {
                    if (!m_dynamicClusterMembers[i] || Fragments[i] == null)
                        continue;
                    FragmentRecord fragment = Fragments[i];
                    if (fragment.Transform != null &&
                        m_dynamicClusterOriginalParents != null &&
                        i < m_dynamicClusterOriginalParents.Length)
                        fragment.Transform.SetParent(
                            m_dynamicClusterOriginalParents[i], true);
                    if (fragment.Body != null)
                    {
                        fragment.Body.mass = Mathf.Max(0.001f, fragment.Mass);
                        fragment.Body.detectCollisions = true;
                    }
                }
                Array.Clear(m_dynamicClusterMembers, 0,
                    m_dynamicClusterMembers.Length);
            }
            m_dynamicClusterCarrierIndex = -1;
        }

        int DetachHitNonPhysicalFragments(in BreakContext context,
            int structuralGroup = -1)
        {
            if (Profile == null)
                return 0;

            int detached = 0;
            for (int i = 0; i < Fragments.Count; i++)
            {
                FragmentRecord fragment = Fragments[i];
                if (fragment == null || fragment.Detached || fragment.Activated ||
                    HasActivatablePhysics(fragment) ||
                    !InsideActivationRadius(fragment, context))
                    continue;
                if (structuralGroup >= 0 &&
                    fragment.StructuralGroup != structuralGroup)
                    continue;
                DetachFragmentRecord(fragment);
                detached++;
            }
            return detached;
        }

        int DetachResidualNonPhysicalFragments()
        {
            if (Profile == null)
                return 0;

            // Once no activatable physical body remains, a visual-only or invalid-collider
            // record has no collider through which a later projectile could reach the root.
            // Remove those records now instead of leaving one permanent wood/glass sliver.
            for (int i = 0; i < Fragments.Count; i++)
            {
                FragmentRecord fragment = Fragments[i];
                if (fragment == null || fragment.Activated || fragment.Detached)
                    continue;
                if (HasActivatablePhysics(fragment))
                    return 0;
            }

            int detached = 0;
            for (int i = 0; i < Fragments.Count; i++)
            {
                FragmentRecord fragment = Fragments[i];
                if (fragment == null || fragment.Activated || fragment.Detached ||
                    HasActivatablePhysics(fragment))
                    continue;
                DetachFragmentRecord(fragment);
                detached++;
            }
            return detached;
        }

        /// <summary>
        /// Releases attached fragment islands that lost every baked connection to their
        /// anchor. Radial glass keeps its boundary-anchored pane behaviour; every other
        /// pattern uses per-structural-group ground anchors when the intact body is set
        /// to Release When Unsupported.
        /// </summary>
        int ReleaseUnsupportedIslands(in BreakContext context, out int culledCount,
            int releaseOnlyStructuralGroup = -1)
        {
            culledCount = 0;
            if (Profile == null || Fragments.Count == 0)
                return 0;

            var dropContext = new BreakContext(context.Point, Vector3.down, 0f, 0f, context.SourceId);

            if (Profile.Pattern == FracturePattern.RadialGlass)
                return ReleaseUnsupportedGlassIslands(dropContext, out culledCount);

            int released = ReleaseUnsupportedStructuralIslands(dropContext, out culledCount,
                releaseOnlyStructuralGroup);
            if (releaseOnlyStructuralGroup >= 0)
            {
                // Removing this module's pieces can also strand an already severed part
                // of an arch, beam or floor. Its group-wide probes may still see the other
                // carrier, so they cannot detect that isolated half. Recheck carried
                // islands after the local releases using connectivity alone: another
                // module's weak-contact and span thresholds still belong to its own poll.
                released += ReleaseUnsupportedStructuralIslands(dropContext,
                    out int carriedCulled, releaseOnlyCarriedGroups: true);
                culledCount += carriedCulled;
            }
            return released;
        }

        /// <summary>
        /// Lightweight event-driven safeguard for local destruction. Foundation probes and
        /// height-section thresholds still own partial support and their grace time, but a
        /// fragment island that has lost every graph route to a real anchor must not wait for
        /// a coarse probe band to fail. The hit scopes partial-support rules to its own
        /// group; carried islands that lose every carrier are then released as well.
        /// This runs only after geometry actually changes, never from Update or FixedUpdate.
        /// </summary>
        int ReleaseUnsupportedAfterFragmentChange(in BreakContext context,
            int structuralGroup, bool fragmentChanged, out int culledCount)
        {
            culledCount = 0;
            if (!fragmentChanged || Profile == null)
                return 0;
            if (Profile.Pattern == FracturePattern.RadialGlass)
                return ReleaseUnsupportedIslands(context, out culledCount);
            if (Profile.IntactBody != IntactBodyMode.ReleaseWhenUnsupported)
                return 0;
            return ReleaseUnsupportedIslands(context, out culledCount,
                structuralGroup);
        }

        /// <summary>
        /// Non-glass structural patterns: breadth-first search per structural group, seeded
        /// by everything that is genuinely standing on something — a still-attached baked
        /// ground anchor, a piece resting on the world, or a piece resting on a still-attached
        /// element of this same object. Attached fragments the search cannot reach are
        /// released with zero break energy so a cut-off section falls instead of hanging in
        /// the air.
        /// </summary>
        int ReleaseUnsupportedStructuralIslands(in BreakContext context,
            out int culledCount, int releaseOnlyStructuralGroup = -1,
            bool releaseOnlyCarriedGroups = false)
        {
            culledCount = 0;

            // Which elements the bake filed as reaching the ground. This is static bake data,
            // so it is collected from every fragment whether or not that fragment is still
            // attached: the question is "was this element ever standing on the floor", not
            // "is its base still there".
            //
            // It used to double as the live anchor test, with a blanket 1.5 m height tolerance
            // on top, and that broke both readings at once. A group whose base had already
            // been shot away still read as anchored, which disabled the carried-element
            // seeding below — the very rule written to keep arches and decks reachable. An
            // arch the bake gave no ground anchor of its own then had no seed of any kind, the
            // sweep found nothing to release, and the whole span stayed hanging in mid-air.
            m_anchorGroups.Clear();
            for (int i = 0; i < Fragments.Count; i++)
            {
                FragmentRecord fragment = Fragments[i];
                if (fragment != null && fragment.GroundAnchor)
                    m_anchorGroups.Add(fragment.StructuralGroup);
            }
            // Prefabs baked before connectivity data existed have every GroundAnchor false and
            // no neighbour graph, so the sweep cannot tell a supported piece from an
            // unsupported one and must not guess.
            bool hasBakedAnchors = m_anchorGroups.Count > 0;

            if (m_supportVisited == null ||
                m_supportVisited.Length != Fragments.Count)
                m_supportVisited = new bool[Fragments.Count];
            else
                Array.Clear(m_supportVisited, 0,
                    m_supportVisited.Length);
            // Connectivity says a fragment can be traced back to the ground; it does not say
            // the trace ever passed underneath it. A neighbour to the side counts exactly as
            // much as a neighbour below, so one anchored corner brick could hold an entire
            // wall whose base had been shot away. Span is the missing quantity: how far
            // sideways the support had to reach to get here.
            float spanLimit = Profile != null
                ? Mathf.Max(0f, Profile.MaxUnsupportedSpan)
                : 0f;
            bool restrictSpan = spanLimit > 0f && !releaseOnlyCarriedGroups;
            EnsureWeakSupportScratch();
            m_supportQueue.Clear();
            m_unsupportedBodies.Clear();

            float carrierReach = Mathf.Max(0.01f, Profile.SupportProbeDistance);
            CollectAttachedGroupBaseHeights();
            MeasureAnchorFooting(carrierReach);
            for (int i = 0; i < Fragments.Count; i++)
            {
                FragmentRecord fragment = Fragments[i];
                if (!IsAttachedSupportCandidate(i)) continue;

                // A seed is a real route into the static world, never merely another part
                // of this same destructible. Cross-module contacts are followed later by
                // EnqueueTouchingStructuralNeighbours, after the search has started at a
                // genuine anchor. Treating "rests on another attached fragment" as a seed
                // let a wall and a floor validate one another after every ground anchor was
                // gone: a closed support cycle with no path to the world, which is exactly
                // how an otherwise destroyed building could remain floating.
                //
                // 1. It is a baked base piece of its element and has not come loose, AND
                //    something outside this destructible is still under it. GroundAnchor is
                //    bake data: it records that a piece sat at its element's own lowest
                //    point, never that the floor is still there. Inside one root that is the
                //    same thing, because the root's own bottom is what stands on the world.
                //    Root + Nested Meshes bakes one independent destructible per source
                //    mesh, so the panel above rests on a DIFFERENT root — and its baked
                //    anchors went on seeding the search after that root was destroyed, which
                //    left the whole panel reachable, attached, and hanging in mid-air.
                Bounds bounds = GetStructuralBounds(fragment);
                bool seed = fragment.GroundAnchor && AnchorStillSeedsSearch(i);
                if (!seed && !fragment.GroundAnchor)
                {
                    // 2. Its element was never given a ground anchor at all — a carried arch,
                    //    deck or beam — so where it meets the world has to be measured. The
                    //    probe is deliberately NOT offered to elements that do own anchors:
                    //    Support Probe Distance is a placement-error tolerance of up to a
                    //    third of a metre, and letting a column's second course claim the
                    //    floor from that far up is exactly why destroying the piece at the
                    //    bottom left the rest of the column standing in mid-air.
                    if (!m_anchorGroups.Contains(fragment.StructuralGroup) &&
                        IsInGroupBaseBand(fragment, bounds, carrierReach))
                        seed = TryGetStaticGroundBelow(bounds, carrierReach, out _);

                }
                if (seed) m_supportSeeds[i] = true;
            }

            // Connectivity alone is not enough for a fractured solid: a large shard that
            // retains only a sliver of its original bonded faces is technically reachable
            // from an anchor but should not hang there forever. New bakes serialize shared-
            // face areas, allowing this event-driven pass to peel such weak remnants before
            // the normal reachability search. Legacy bakes have no aligned area list and
            // retain their previous binary behaviour.
            if (!releaseOnlyCarriedGroups) PruneWeakSupportContacts();
            for (int i = 0; i < Fragments.Count; i++)
            {
                if (!m_supportSeeds[i] || m_supportPruned[i] ||
                    !IsAttachedSupportCandidate(i))
                    continue;
                m_supportVisited[i] = true;
                m_supportQueue.Enqueue(i);
            }

            // No seed anywhere means nothing that is still attached is standing on anything.
            // Localized progressive destruction is not a reason to leave it in the air: the
            // author asked for pieces to wake locally, not for a shell to survive having its
            // entire base removed. Only a bake with no connectivity data is exempt, because
            // there the empty seed set proves nothing.
            if (m_supportQueue.Count == 0)
            {
                if (!hasBakedAnchors && !ActivateAllOnFirstBreak)
                    return 0;

                for (int i = 0; i < Fragments.Count; i++)
                {
                    FragmentRecord fragment = Fragments[i];
                    if (!IsAttachedSupportCandidate(i))
                        continue;
                    if (releaseOnlyStructuralGroup >= 0 &&
                        fragment.StructuralGroup != releaseOnlyStructuralGroup)
                        continue;
                    if (releaseOnlyCarriedGroups &&
                        m_anchorGroups.Contains(fragment.StructuralGroup))
                        continue;
                    if (!HasActivatablePhysics(fragment))
                    {
                        DetachFragmentRecord(fragment);
                        culledCount++;
                    }
                    else
                    {
                        m_unsupportedBodies.Add(i);
                    }
                }
                int releasedAll = ActivateUnsupportedBodies(context, out int culledAll);
                culledCount += culledAll;
                return releasedAll;
            }

            while (m_supportQueue.Count > 0)
            {
                int current = m_supportQueue.Dequeue();
                FragmentRecord currentFragment = Fragments[current];
                List<int> links = currentFragment.SupportNeighbours;
                for (int n = 0; links != null && n < links.Count; n++)
                {
                    int neighbour = links[n];
                    if (neighbour < 0 || neighbour >= Fragments.Count ||
                        m_supportVisited[neighbour] || m_supportPruned[neighbour])
                        continue;
                    FragmentRecord next = Fragments[neighbour];
                    // Baked links propagate only inside one source element. Cross-source
                    // joints are resolved separately from tight visual contact below.
                    if (next == null ||
                        next.StructuralGroup != currentFragment.StructuralGroup ||
                        !IsAttachedSupportCandidate(neighbour))
                        continue;
                    m_supportVisited[neighbour] = true;
                    m_supportQueue.Enqueue(neighbour);
                }

                // Baked neighbours intentionally describe cuts inside one source mesh.
                // Real structures also contain joints between separate source meshes: a
                // fence board nailed into the side of a post, for example. Follow only
                // tight visual contacts here, so the support search can cross such a joint
                // without treating merely nearby objects as welded together.
                EnqueueTouchingStructuralNeighbours(current, carrierReach);
            }

            if (restrictSpan) ApplyBoundedSupportReach(spanLimit);

            for (int i = 0; i < Fragments.Count; i++)
            {
                FragmentRecord fragment = Fragments[i];
                if (!IsAttachedSupportCandidate(i) ||
                    m_supportVisited[i])
                    continue;
                if (releaseOnlyStructuralGroup >= 0 &&
                    fragment.StructuralGroup != releaseOnlyStructuralGroup)
                    continue;
                if (releaseOnlyCarriedGroups &&
                    m_anchorGroups.Contains(fragment.StructuralGroup))
                    continue;
                // Carried groups are no longer skipped here: the pass above seeds them from
                // the pieces still resting on a surviving element, so a roof spanning four
                // walls keeps every fragment reachable when one wall goes, while a section
                // that is BOTH unsupported and cut off from the rest finally falls.
                if (!HasActivatablePhysics(fragment))
                {
                    DetachFragmentRecord(fragment);
                    culledCount++;
                }
                else
                {
                    m_unsupportedBodies.Add(i);
                }
            }

            int activated = ActivateUnsupportedBodies(context, out int culled);
            culledCount += culled;
            return activated;
        }

        void EnsureWeakSupportScratch()
        {
            int count = Fragments.Count;
            if (m_supportPruned == null || m_supportPruned.Length != count)
            {
                m_supportPruned = new bool[count];
                m_supportSeeds = new bool[count];
                m_supportInitialContactAreas = new float[count];
                m_supportRemainingContactAreas = new float[count];
                return;
            }
            Array.Clear(m_supportPruned, 0, count);
            Array.Clear(m_supportSeeds, 0, count);
            Array.Clear(m_supportInitialContactAreas, 0, count);
            Array.Clear(m_supportRemainingContactAreas, 0, count);
        }

        void PruneWeakSupportContacts()
        {
            m_supportPruneQueue.Clear();
            float requiredRatio = Profile != null
                ? Mathf.Clamp01(Profile.RequiredSupportRatio)
                : 0f;
            if (requiredRatio <= 0f) return;

            for (int i = 0; i < Fragments.Count; i++)
            {
                if (!IsAttachedSupportCandidate(i)) continue;
                FragmentRecord fragment = Fragments[i];
                List<int> links = fragment.SupportNeighbours;
                List<float> areas = fragment.SupportNeighbourContactAreas;
                if (links == null || areas == null || links.Count == 0 ||
                    areas.Count != links.Count)
                    continue;

                float initial = 0f;
                float remaining = 0f;
                for (int n = 0; n < links.Count; n++)
                {
                    int neighbour = links[n];
                    if (neighbour < 0 || neighbour >= Fragments.Count) continue;
                    FragmentRecord other = Fragments[neighbour];
                    if (other == null) continue;
                    // This ratio asks one question: has this shard lost the faces that bond
                    // it INSIDE its own element? A Whole Building fracture crosses module
                    // seams, so a wall shard also carries baked links into the floor beside
                    // it. Counting those made destroying the floor look like the wall shard
                    // coming loose: it was pruned, and because a pruned fragment conducts
                    // nothing, the rest of the floor lost every route through it and the
                    // whole slab came down from one hit. Leaving the seam bond out of both
                    // sums keeps the baseline neutral — which is all the earlier reading
                    // needed — without letting one module's damage condemn the next.
                    if (other.StructuralGroup != fragment.StructuralGroup) continue;
                    float area = Mathf.Max(0f, areas[n]);
                    initial += area;
                    if (IsAttachedSupportCandidate(neighbour))
                        remaining += area;
                }
                m_supportInitialContactAreas[i] = initial;
                m_supportRemainingContactAreas[i] = remaining;
            }

            for (int i = 0; i < Fragments.Count; i++)
                QueueWeakSupportFragmentIfNeeded(i, requiredRatio);

            while (m_supportPruneQueue.Count > 0)
            {
                int removed = m_supportPruneQueue.Dequeue();
                FragmentRecord fragment = Fragments[removed];
                List<int> links = fragment.SupportNeighbours;
                List<float> areas = fragment.SupportNeighbourContactAreas;
                for (int n = 0; links != null && areas != null &&
                                n < links.Count && n < areas.Count; n++)
                {
                    int neighbour = links[n];
                    if (neighbour < 0 || neighbour >= Fragments.Count ||
                        m_supportPruned[neighbour] ||
                        !IsAttachedSupportCandidate(neighbour))
                        continue;
                    FragmentRecord other = Fragments[neighbour];
                    // The cascade subtracts the same seam-free area the sums above used.
                    if (other == null ||
                        other.StructuralGroup != fragment.StructuralGroup)
                        continue;
                    m_supportRemainingContactAreas[neighbour] = Mathf.Max(0f,
                        m_supportRemainingContactAreas[neighbour] -
                        Mathf.Max(0f, areas[n]));
                    QueueWeakSupportFragmentIfNeeded(neighbour, requiredRatio);
                }
            }
        }

        void QueueWeakSupportFragmentIfNeeded(int index, float requiredRatio)
        {
            if (index < 0 || index >= Fragments.Count ||
                m_supportPruned[index] || m_supportSeeds[index] ||
                !IsAttachedSupportCandidate(index))
                return;
            float initial = m_supportInitialContactAreas[index];
            if (initial <= 1e-8f ||
                m_supportRemainingContactAreas[index] + 1e-8f >=
                initial * requiredRatio)
                return;
            m_supportPruned[index] = true;
            m_supportPruneQueue.Enqueue(index);
        }

        int ReleaseUnsupportedGlassIslands(in BreakContext context,
            out int culledCount)
        {
            culledCount = 0;
            if (Profile == null || Profile.Pattern != FracturePattern.RadialGlass ||
                !Profile.ReleaseUnsupportedGlass || Fragments.Count == 0)
                return 0;

            bool hasBakedAnchor = false;
            for (int i = 0; i < Fragments.Count; i++)
            {
                if (Fragments[i] != null && Fragments[i].BoundaryAnchor)
                {
                    hasBakedAnchor = true;
                    break;
                }
            }
            // Legacy prefabs have no connectivity data. They keep the previous behavior
            // until rebaked instead of dropping every fragment as an unsupported island.
            if (!hasBakedAnchor) return 0;

            if (m_supportVisited == null ||
                m_supportVisited.Length != Fragments.Count)
                m_supportVisited = new bool[Fragments.Count];
            else
                Array.Clear(m_supportVisited, 0,
                    m_supportVisited.Length);
            m_supportQueue.Clear();
            m_unsupportedBodies.Clear();

            for (int i = 0; i < Fragments.Count; i++)
            {
                FragmentRecord fragment = Fragments[i];
                if (!IsAttachedSupportCandidate(i) || !fragment.BoundaryAnchor)
                    continue;
                m_supportVisited[i] = true;
                m_supportQueue.Enqueue(i);
            }

            while (m_supportQueue.Count > 0)
            {
                int current = m_supportQueue.Dequeue();
                List<int> links = Fragments[current].SupportNeighbours;
                if (links == null) continue;
                for (int n = 0; n < links.Count; n++)
                {
                    int neighbour = links[n];
                    if (neighbour < 0 || neighbour >= Fragments.Count ||
                        m_supportVisited[neighbour] ||
                        !IsAttachedSupportCandidate(neighbour))
                        continue;
                    m_supportVisited[neighbour] = true;
                    m_supportQueue.Enqueue(neighbour);
                }
            }

            for (int i = 0; i < Fragments.Count; i++)
            {
                FragmentRecord fragment = Fragments[i];
                if (!IsAttachedSupportCandidate(i) ||
                    m_supportVisited[i])
                    continue;
                if (!HasActivatablePhysics(fragment))
                {
                    DetachFragmentRecord(fragment);
                    culledCount++;
                }
                else
                {
                    m_unsupportedBodies.Add(i);
                }
            }

            int activated = ActivateUnsupportedBodies(context, out int culled);
            culledCount += culled;
            return activated;
        }

        /// <summary>
        /// Activates the collected unsupported bodies with zero break energy so they
        /// simply drop. Unsupported pieces must never hang because a performance budget
        /// is full: low-priority pieces beyond the budget are hidden and restored by
        /// Reset.
        /// </summary>
        int ActivateUnsupportedBodies(in BreakContext context, out int culledCount)
        {
            culledCount = 0;
            if (m_unsupportedBodies.Count == 0) return 0;
            int granted = m_unsupportedBodies.Count;
            if (UsesActiveBodyBudget)
            {
                int cap = Mathf.Max(0, Profile.MaxActiveFragments - m_activeCount);
                int desired = Mathf.Min(cap, m_unsupportedBodies.Count);
                granted = DestructionBudgetManager.Instance.RequestActivation(
                    Profile.BudgetGroup, Profile.GroupMaxActiveBodies, desired);
            }

            var dropContext = new BreakContext(
                context.Point, context.Normal, 0f, 0f, context.SourceId);
            bool batchActivation = granted > ActivationBatchThreshold &&
                                   (Profile == null ||
                                    Profile.Pattern != FracturePattern.RadialGlass);
            int immediateLimit = batchActivation ? LargeActivationBatchSize : granted;
            for (int i = 0; i < granted; i++)
            {
                int index = m_unsupportedBodies[i];
                if (i < immediateLimit)
                {
                    ActivateFragment(index, dropContext,
                        Vector3.zero, Vector3.zero);
                }
                else if (m_pendingActivationIndices.Add(index))
                {
                    m_pendingActivations.Enqueue(new PendingFragmentActivation
                    {
                        Index = index,
                        Context = dropContext,
                        InheritedLinearVelocity = Vector3.zero,
                        InheritedAngularVelocity = Vector3.zero
                    });
                }
            }

            for (int i = granted; i < m_unsupportedBodies.Count; i++)
            {
                DetachFragmentRecord(Fragments[m_unsupportedBodies[i]]);
                culledCount++;
            }
            return granted;
        }

        /// <summary>
        /// Lowest still-attached height per structural group, refreshed once per sweep. The
        /// ground probe below is a raycast, so it is worth spending only on the pieces that
        /// could actually be standing on something.
        /// </summary>
        void CollectAttachedGroupBaseHeights()
        {
            if (m_attachedGroupBaseY == null)
                m_attachedGroupBaseY = new Dictionary<int, float>();
            m_attachedGroupBaseY.Clear();
            for (int i = 0; i < Fragments.Count; i++)
            {
                FragmentRecord fragment = Fragments[i];
                if (fragment == null || !IsAttachedSupportCandidate(i)) continue;
                float minY = GetStructuralBounds(fragment).min.y;
                if (!m_attachedGroupBaseY.TryGetValue(fragment.StructuralGroup,
                        out float current) || minY < current)
                    m_attachedGroupBaseY[fragment.StructuralGroup] = minY;
            }
        }

        /// <summary>
        /// Finds static world below a piece — the floor, a static prop, another intact
        /// destructible — as opposed to part of this same object, and reports the vertical gap
        /// to it. Used for two questions: whether a still-attached piece is standing on the
        /// ground (with Support Probe Distance as the reach, so this and
        /// StructuralSupportNode's own footing probes agree), and how much room a launched
        /// piece has before it reaches a floor. The shorter
        /// <see cref="IsFragmentNearStaticGround"/> clearance is a third, coarser question:
        /// whether a piece is resting beside a floor right now.
        /// </summary>
        bool TryGetStaticGroundBelow(Bounds bounds, float reach, out float gap)
        {
            gap = 0f;
            if (bounds.size.sqrMagnitude <= 0.000001f) return false;
            // Objects are rarely placed exactly on the floor, and a base sunk slightly into
            // terrain starts the ray below the surface where nothing can be hit.
            float lift = Mathf.Min(0.05f, reach * 0.5f);
            var origin = new Vector3(bounds.center.x, bounds.min.y + lift,
                bounds.center.z);
            int hits = Physics.RaycastNonAlloc(origin, Vector3.down,
                m_dynamicSupportHits, reach + lift,
                Profile != null ? Profile.SupportLayers : Physics.AllLayers,
                QueryTriggerInteraction.Ignore);
            bool found = false;
            for (int i = 0; i < hits; i++)
            {
                RaycastHit candidate = m_dynamicSupportHits[i];
                Collider hit = candidate.collider;
                if (hit == null || !hit.enabled ||
                    !hit.gameObject.activeInHierarchy ||
                    hit.transform == transform ||
                    hit.transform.IsChildOf(transform))
                    continue;
                // Loose debris and rolling props hold nothing up.
                Rigidbody supportBody = hit.attachedRigidbody;
                if (supportBody != null && !supportBody.isKinematic) continue;
                var supportRoot = hit.GetComponentInParent<DestructibleRoot>();
                if (supportRoot != null &&
                    (supportRoot == this || supportRoot.IsStructurallyReleased ||
                     supportRoot.State >= DestructibleState.Released))
                    continue;
                float candidateGap = Mathf.Max(0f, candidate.distance - lift);
                if (!found || candidateGap < gap) gap = candidateGap;
                found = true;
            }
            return found;
        }

        /// <summary>
        /// Measures, once per sweep, which baked ground anchors are still standing on
        /// something outside this object.
        ///
        /// GroundAnchor is geometry, not contact: the bake marks every piece sitting at its
        /// element's lowest point. For a wall that is its base course and the two readings
        /// agree. For a floor slab it is the ENTIRE slab, every piece of which then seeded
        /// the connectivity search on its own behalf and could never be found unsupported —
        /// destroy the edge that rested on the wall and the rest went on hanging over an
        /// empty room.
        /// </summary>
        void MeasureAnchorFooting(float reach)
        {
            if (m_anchorStanding == null ||
                m_anchorStanding.Length != Fragments.Count)
                m_anchorStanding = new bool[Fragments.Count];
            else
                Array.Clear(m_anchorStanding, 0, m_anchorStanding.Length);

            for (int i = 0; i < Fragments.Count; i++)
            {
                FragmentRecord fragment = Fragments[i];
                if (fragment == null || !fragment.GroundAnchor ||
                    !IsAttachedSupportCandidate(i) ||
                    !HasExternalFootingUnder(GetStructuralBounds(fragment), reach))
                    continue;
                m_anchorStanding[i] = true;
                m_anchorFootingSeen = true;
            }
        }

        /// <summary>
        /// Whether this baked ground anchor may still seed the search.
        ///
        /// Judging anchors by what is under them is only allowed once SOME anchor of this
        /// object has been seen standing on something. An object placed above its floor, one
        /// whose footing sits on a layer the profile excludes, or a fixture with nothing
        /// under it by design was always held up by its anchors alone; taking that away
        /// would drop it the first time it was scratched. An object that demonstrably does
        /// stand on the world has no such excuse, and its anchors answer individually.
        /// </summary>
        bool AnchorStillSeedsSearch(int fragmentIndex)
        {
            if (m_anchorStanding != null && fragmentIndex < m_anchorStanding.Length &&
                m_anchorStanding[fragmentIndex])
                return true;
            return !m_anchorFootingSeen;
        }

        /// <summary>
        /// True when something that is not part of this destructible is still holding this
        /// piece up: the floor, a static prop, or another destructible whose material is
        /// still standing there. The downward ray answers the ordinary case; the overlap
        /// below answers the modular one, where the panel underneath interpenetrates this
        /// one and a ray started inside it produces no hit at all.
        /// </summary>
        bool HasExternalFootingUnder(Bounds bounds, float reach)
        {
            return TryGetStaticGroundBelow(bounds, reach, out _) ||
                   HasOverlappingExternalFooting(bounds, reach);
        }

        /// <summary>
        /// Foreign material occupying the slab of space directly under this piece. Only
        /// reached when the ray found nothing, so the cost stays with the modular layouts
        /// that need it.
        /// </summary>
        bool HasOverlappingExternalFooting(Bounds bounds, float reach)
        {
            if (bounds.size.sqrMagnitude <= 0.000001f) return false;
            reach = Mathf.Max(0.01f, reach);
            var center = new Vector3(bounds.center.x, bounds.min.y - reach * 0.5f,
                bounds.center.z);
            var halfExtents = new Vector3(
                Mathf.Max(0.005f, bounds.extents.x),
                reach * 0.5f,
                Mathf.Max(0.005f, bounds.extents.z));
            int hits = Physics.OverlapBoxNonAlloc(center, halfExtents,
                s_externalFootingOverlap, Quaternion.identity,
                Profile != null ? Profile.SupportLayers.value : Physics.AllLayers,
                QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hits; i++)
            {
                if (IsExternalStructuralSupport(s_externalFootingOverlap[i]))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// One shared answer to "does this collider hold anything up, and does it belong to
        /// someone else". Loose debris, released destructibles and this object's own
        /// colliders are not support.
        /// </summary>
        bool IsExternalStructuralSupport(Collider collider)
        {
            if (collider == null || !collider.enabled ||
                !collider.gameObject.activeInHierarchy ||
                collider.transform == transform ||
                collider.transform.IsChildOf(transform))
                return false;
            // Loose debris holds nothing up, and an activated fragment is exactly that:
            // activation is what gives it a non-kinematic body. A detached one has had its
            // GameObject switched off and is already gone above. So the collider itself
            // answers "is this still standing", and the root only has to say whether the
            // whole object has been let go.
            Rigidbody supportBody = collider.attachedRigidbody;
            if (supportBody != null && !supportBody.isKinematic) return false;
            var supportRoot = collider.GetComponentInParent<DestructibleRoot>();
            return supportRoot == null ||
                   (supportRoot != this && !supportRoot.IsStructurallyReleased &&
                    supportRoot.State != DestructibleState.Released &&
                    supportRoot.State != DestructibleState.Recycled);
        }

        bool IsInGroupBaseBand(FragmentRecord fragment, Bounds bounds, float reach)
        {
            if (m_attachedGroupBaseY == null ||
                !m_attachedGroupBaseY.TryGetValue(fragment.StructuralGroup,
                    out float baseY))
                return true;
            return bounds.min.y <= baseY + Mathf.Max(0.01f, reach);
        }

        bool IsAttachedSupportCandidate(int index)
        {
            // Runtime splitting only deactivates the fragment GameObject without
            // setting Activated/Detached, so a hidden piece must not seed or carry
            // support through the connectivity sweep. A piece already queued for dynamic
            // activation is loose as well and must not be released a second time by the
            // structural pass.
            if (index < 0 || index >= Fragments.Count ||
                m_pendingActivationIndices.Contains(index))
                return false;
            FragmentRecord fragment = Fragments[index];
            return fragment != null && !fragment.Activated && !fragment.Detached &&
                   fragment.Transform != null &&
                   fragment.Transform.gameObject.activeInHierarchy;
        }

        static void DetachFragmentRecord(FragmentRecord fragment)
        {
            fragment.Detached = true;
            if (fragment.Body != null) fragment.Body.isKinematic = true;
            if (fragment.Collider != null) fragment.Collider.enabled = false;
            SetCollidersEnabled(fragment.DynamicColliders, false);
            if (fragment.DynamicCollider != null) fragment.DynamicCollider.enabled = false;
            if (fragment.Transform != null)
                fragment.Transform.gameObject.SetActive(false);
        }

        /// <summary>
        /// The configured radius is measured from the hit point to the fragment surface.
        /// Using the fragment pivot here makes thick or large pieces impossible to hit with a
        /// small local radius even when the ray or projectile visibly touches their collider.
        /// Radius &lt;= 0 means all fragments.
        /// </summary>
        bool InsideActivationRadius(FragmentRecord f, in BreakContext context)
        {
            if (context.Radius <= 0f || f.Transform == null) return true;
            Vector3 closest = GetFragmentClosestPoint(f, context.Point);
            float d = (closest - context.Point).sqrMagnitude;
            return d <= context.Radius * context.Radius;
        }

        static Vector3 GetFragmentClosestPoint(FragmentRecord fragment, Vector3 point)
        {
            Collider collider = fragment.Collider;
            if (collider != null)
                return GetColliderClosestPoint(collider, point);

            Renderer renderer = fragment.Renderer;
            if (renderer != null)
            {
                Bounds worldBounds = renderer.bounds;
                if (worldBounds.size.sqrMagnitude > 0.000001f)
                    return worldBounds.ClosestPoint(point);
            }

            return fragment.Transform.position;
        }

        static Vector3 GetColliderClosestPoint(Collider collider, Vector3 point)
        {
            // Once its hierarchy is visible Unity can query the cooked collider exactly.
            if (collider.enabled && collider.gameObject.activeInHierarchy)
                return collider.ClosestPoint(point);

            // Inactive baked roots expose empty Collider.bounds. Query the serialized
            // local shape instead so first hits and compatibility group resolution remain
            // surface-based before the visual hierarchy is swapped.
            Bounds localBounds;
            if (collider is MeshCollider meshCollider &&
                meshCollider.sharedMesh != null)
            {
                localBounds = meshCollider.sharedMesh.bounds;
                Vector3 localPoint = collider.transform.InverseTransformPoint(point);
                return collider.transform.TransformPoint(
                    localBounds.ClosestPoint(localPoint));
            }
            if (collider is BoxCollider boxCollider)
            {
                localBounds = new Bounds(boxCollider.center, boxCollider.size);
                Vector3 localPoint = collider.transform.InverseTransformPoint(point);
                return collider.transform.TransformPoint(
                    localBounds.ClosestPoint(localPoint));
            }
            return collider.transform.position;
        }

        void EnsureFracturedVisible()
        {
            if (IntactRoot != null && IntactRoot.activeSelf) IntactRoot.SetActive(false);
            if (FracturedRoot != null && !FracturedRoot.activeSelf) FracturedRoot.SetActive(true);
        }

        void PrepareIntactBodyForFracture(
            out Vector3 inheritedLinearVelocity, out Vector3 inheritedAngularVelocity)
        {
            inheritedLinearVelocity = Vector3.zero;
            inheritedAngularVelocity = Vector3.zero;
            if (IntactBody == null) return;

            if (!IntactBody.isKinematic)
            {
#if UNITY_6000_0_OR_NEWER
                inheritedLinearVelocity = IntactBody.linearVelocity;
#else
                inheritedLinearVelocity = IntactBody.velocity;
#endif
                inheritedAngularVelocity = IntactBody.angularVelocity;
            }

            if (Profile != null && Profile.IntactBody == IntactBodyMode.Dynamic)
            {
                m_dynamicResidualLinearVelocity = inheritedLinearVelocity;
                m_dynamicResidualAngularVelocity = inheritedAngularVelocity;
            }

            // Unity rejects velocity writes on a kinematic Rigidbody. Stop a dynamic
            // intact body before freezing it; a body that is already kinematic has no
            // simulated velocity to clear.
            if (!IntactBody.isKinematic)
            {
#if UNITY_6000_0_OR_NEWER
                IntactBody.linearVelocity = Vector3.zero;
#else
                IntactBody.velocity = Vector3.zero;
#endif
                IntactBody.angularVelocity = Vector3.zero;
            }
            IntactBody.isKinematic = true;
            // Deferred Whole Building fragments intentionally have no individual body
            // while attached. Keep the one root body as their kinematic compound carrier
            // after IntactRoot is hidden, so physical projectiles still collide with the
            // remaining shell. A fragment becomes its own body when it detaches.
            IntactBody.detectCollisions = DeferInactiveFragmentPhysics;
        }

        void ActivateFragment(int index, in BreakContext context,
            Vector3 inheritedLinearVelocity, Vector3 inheritedAngularVelocity)
        {
            Vector3 inheritedVelocityOrigin = IntactBody != null
                ? IntactBody.worldCenterOfMass : transform.position;
            ActivateFragment(index, context, inheritedLinearVelocity,
                inheritedAngularVelocity, inheritedVelocityOrigin);
        }

        void ActivateFragment(int index, in BreakContext context,
            Vector3 inheritedLinearVelocity, Vector3 inheritedAngularVelocity,
            Vector3 inheritedVelocityOrigin)
        {
            var f = Fragments[index];
            FragmentDespawnTransition.Cancel(f.Transform);
            SetFragmentProbeAnchor(f, null);
            SetFragmentShadowMode(f, true);
            m_attachedShadowProxyDirty = true;
            f.Activated = true;
            f.Settled = false;
            f.ActivatedTime = Time.time;
            f.SleepTimer = 0f;
            m_activeCount++;

            var body = EnsureFragmentPhysics(f, Profile);
            if (body == null) return;
            UseDynamicFragmentCollider(f);
            EnsureEnabledDynamicCollider(f);
            ApplyDefaultDebrisContactMaterial(f);
            body.detectCollisions = true;
            body.isKinematic = false;
            if (DeferInactiveFragmentPhysics && Fragments.Count >= 160)
                body.collisionDetectionMode = CollisionDetectionMode.Discrete;
            body.maxDepenetrationVelocity = 2.5f;
            ApplyFragmentSpinLimit(f, body);
            if (Profile != null && Profile.Pattern == FracturePattern.RadialGlass)
                body.solverIterations = 4;
            ApplyFragmentTunnelGuard(body);
            f.NeedsMeshGroundGuard = FragmentRuntime.RequiresMeshGroundGuard(body);
            f.Runtime = body.GetComponent<FragmentRuntime>();
            if (f.Runtime != null && !f.Runtime.enabled)
                f.Runtime.enabled = true;
            f.GroundGuardResolved = true;
            // Preserve the point velocity of the body that carried this fragment. Dynamic
            // residual clusters move independently from DestructibleRoot, so using the root
            // transform as the angular origin creates an artificial lever arm after the prop
            // has translated or rotated. That intermittent error can launch a detached shard
            // at the safety cap even when the carrier itself is moving slowly.
            Vector3 inheritedMotion = inheritedLinearVelocity +
                Vector3.Cross(inheritedAngularVelocity,
                    body.worldCenterOfMass - inheritedVelocityOrigin);
            if (Profile != null)
                inheritedMotion = Profile.ClampInheritedVelocity(inheritedMotion);
#if UNITY_6000_0_OR_NEWER
            body.linearVelocity = inheritedMotion;
#else
            body.velocity = inheritedMotion;
#endif
            // Spin needs no clamp of its own here: ApplyFragmentSpinLimit above set this
            // body's maxAngularVelocity, and PhysX enforces that on every step.
            body.angularVelocity = inheritedAngularVelocity;
            body.WakeUp();

            if (s_sharedPhysics.CurrentImpactColliders != null)
            {
                for (int c = 0; c < s_sharedPhysics.CurrentImpactColliders.Count; c++)
                {
                    Collider sc = s_sharedPhysics.CurrentImpactColliders[c];
                    if (sc == null) continue;
                    if (f.DynamicCollider != null) Physics.IgnoreCollision(f.DynamicCollider, sc);
                    if (f.DynamicColliders != null)
                    {
                        for (int i = 0; i < f.DynamicColliders.Count; i++)
                            if (f.DynamicColliders[i] != null) Physics.IgnoreCollision(f.DynamicColliders[i], sc);
                    }
                }
            }

            float pushMultiplier = Profile != null
                ? Profile.FragmentPushMultiplier : 1f;
            float pushEnergy = context.Energy * Mathf.Max(0f, pushMultiplier);
            ForceMode forceMode = ForceMode.Impulse;
            bool stableDynamicDirectHit = Profile != null &&
                Profile.IntactBody == IntactBodyMode.Dynamic &&
                !m_applyingCollisionDamage &&
                context.ImpactDirection.sqrMagnitude > 0.000001f;
            // Descent this activation is about to hand the piece. It has to be predicted
            // rather than read back: an impulse queued through AddForce is not visible in
            // Rigidbody.velocity until the next physics step, so anything that inspects the
            // velocity here sees only what the piece inherited.
            float plannedDescentSpeed = Mathf.Max(0f, -inheritedLinearVelocity.y);
            float directionalEnergy = 0f;
            if (stableDynamicDirectHit)
            {
                bool nearStaticGround = IsFragmentNearStaticGround(f);
                Vector3 launchDirection = ResolveGroundSafeEjectDirection(
                    context.ImpactDirection, nearStaticGround);
                float boundedPush = ClampLaunchImpulse(pushEnergy, body, forceMode);
                float pushSpeed = ForceToSpeed(boundedPush, body, forceMode);
                float ejectSpeed = 0f;
                if (context.Radius > 0f)
                {
                    float configuredEjectSpeed = Profile.LocalImpactEjectSpeed;
                    float energyScale = Mathf.Clamp(boundedPush / 35f, 0f, 2f);
                    ejectSpeed = ForceToSpeed(Mathf.Max(0f, configuredEjectSpeed) *
                        energyScale, body, ForceMode.Impulse);
                }
                float authoredDirectionalSpeed = ForceToSpeed(ResolveDirectionalImpactImpulse(
                    Profile, context.Energy, context.DirectionalForce), body, ForceMode.Impulse);
                float requestedSpeed = Mathf.Max(pushSpeed,
                    Mathf.Max(ejectSpeed, authoredDirectionalSpeed));
                directionalEnergy = ApplyCappedDirectVelocity(body, launchDirection,
                    requestedSpeed, Profile.MaxFragmentLaunchSpeed);
#if UNITY_6000_0_OR_NEWER
                plannedDescentSpeed = Mathf.Max(plannedDescentSpeed,
                    -body.linearVelocity.y);
#else
                plannedDescentSpeed = Mathf.Max(plannedDescentSpeed,
                    -body.velocity.y);
#endif
            }
            else if (pushEnergy > 0f)
            {
                pushEnergy = ClampLaunchImpulse(pushEnergy, body, forceMode);
                // Exact configured radius; 0 lets the force apply everywhere
                // without distance falloff, matching "all fragments".
                float explosionRadius = context.LinearRadialFalloff
                    ? Mathf.Max(context.Radius, 0f) : 0f;
                float upward = DestructionExplosion.ResolveUpwardOffset(
                    Profile != null
                        ? Profile.ExplosionUpwardModifier
                        : DestructionExplosion.DefaultUpwardShare,
                    explosionRadius);
                body.AddExplosionForce(pushEnergy, context.Point,
                    explosionRadius, upward, forceMode);
                plannedDescentSpeed += PredictExplosionDescentSpeed(body, pushEnergy,
                    context.Point, explosionRadius, upward, forceMode);

                if (context.Radius > 0f && !context.IsImpactSpread)
                {
                    // A local surface hit may select a fragment through Collider.ClosestPoint
                    // while its centre of mass still lies outside the configured radius.
                    // AddExplosionForce then contributes no useful motion (or pushes the
                    // piece deeper into the kinematic shell). Guarantee a small outward
                    // separation speed so the activated piece visibly leaves the hole.
                    Vector3 separationDirection = context.ImpactDirection;
                    // Direct hits travel into the surface. RaycastHit.normal points back
                    // toward the weapon, so it is only a legacy fallback when the caller
                    // did not supply a travel direction.
                    //
                    // No travel vector means a blast, and each piece then leaves along its
                    // own outward vector from the origin. Every fallback below this one is
                    // measured against the root pivot, which is a single shared point: they
                    // hand the whole object one direction, which is exactly what made an
                    // explosion throw every fragment the same way.
                    if (separationDirection.sqrMagnitude < 0.000001f)
                        separationDirection = body.worldCenterOfMass - context.Point;
                    if (separationDirection.sqrMagnitude < 0.000001f)
                        separationDirection = context.Point - transform.position;
                    if (separationDirection.sqrMagnitude < 0.000001f)
                        separationDirection = body.worldCenterOfMass - transform.position;
                    if (separationDirection.sqrMagnitude < 0.000001f)
                        separationDirection = -context.Normal;
                    if (separationDirection.sqrMagnitude < 0.000001f)
                        separationDirection = Vector3.up;
                    bool nearStaticGround = IsFragmentNearStaticGround(f);
                    separationDirection = ResolveGroundSafeEjectDirection(
                        separationDirection, nearStaticGround);

                    float configuredEjectSpeed = Profile != null
                        ? Profile.LocalImpactEjectSpeed : 2.5f;
                    float energyScale = Mathf.Clamp(pushEnergy / 35f, 0f, 2f);
                    float targetSeparationSpeed =
                        ForceToSpeed(Mathf.Max(0f, configuredEjectSpeed) * energyScale,
                            body, ForceMode.Impulse);
                    float maxLaunchSpeed = Profile != null
                        ? Mathf.Max(0f, Profile.MaxFragmentLaunchSpeed) : 7f;
                    targetSeparationSpeed = Mathf.Min(
                        targetSeparationSpeed, maxLaunchSpeed);
#if UNITY_6000_0_OR_NEWER
                    Vector3 currentVelocity = body.linearVelocity;
#else
                    Vector3 currentVelocity = body.velocity;
#endif
                    // AddExplosionForce points from the exact hit point toward the
                    // fragment centre. A hit above a low fragment can therefore add a
                    // downward component even though the surface normal is horizontal.
                    // Cancel only that component when the piece is already beside a
                    // static floor; airborne debris keeps the authored launch arc.
                    if (nearStaticGround && currentVelocity.y < 0f)
                    {
                        float liftSpeed = Mathf.Min(0.35f,
                            targetSeparationSpeed * 0.15f);
                        body.AddForce(Vector3.up *
                            (liftSpeed - currentVelocity.y),
                            ForceMode.VelocityChange);
                        currentVelocity.y = liftSpeed;
                    }
                    float currentOutwardSpeed =
                        Vector3.Dot(currentVelocity, separationDirection);
                    if (currentOutwardSpeed < targetSeparationSpeed)
                    {
                        body.AddForce(separationDirection *
                            (targetSeparationSpeed - currentOutwardSpeed),
                            ForceMode.VelocityChange);
                    }
                }
            }

            if (!stableDynamicDirectHit)
                directionalEnergy = ApplyDirectionalImpactForce(body,
                    context.Point, context.ImpactDirection, context.Energy,
                    context.DirectionalForce);
            if (directionalEnergy > 0f &&
                context.ImpactDirection.sqrMagnitude > 0.000001f)
            {
                plannedDescentSpeed += directionalEnergy *
                    Mathf.Max(0f, -context.ImpactDirection.normalized.y);
            }

            float launchEnergy = Mathf.Max(pushEnergy, directionalEnergy);
            if (launchEnergy > 0f)
            {
                // The direct Dynamic-cluster path already spent its one complete launch
                // budget. A second mass-independent topple velocity here would put a tall
                // stave above the cap again before its first CCD sweep.
                if (!stableDynamicDirectHit)
                    ApplyTallFragmentTopple(index, f, body, launchEnergy);
                LimitLaunchTowardStaticGround(f, body, plannedDescentSpeed);
            }

            FragmentActivated?.Invoke(index);
        }

        /// <summary>
        /// Keeps every renderer in the attached shell on one light-probe sample, then
        /// returns loose debris to position-based sampling when it starts moving. The
        /// renderer list is baked so a 250-piece collapse does not allocate or scan each
        /// fragment hierarchy on the activation frame.
        /// </summary>
        internal static void SetFragmentProbeAnchor(FragmentRecord fragment,
            Transform anchor)
        {
            if (fragment == null) return;
            bool assignedPrimary = false;
            List<Renderer> renderers = fragment.ProbeRenderers;
            for (int i = 0; renderers != null && i < renderers.Count; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null) continue;
                renderer.probeAnchor = anchor;
                if (renderer == fragment.Renderer) assignedPrimary = true;
            }
            // Compatibility for bakes made before ProbeRenderers was serialized.
            if (!assignedPrimary && fragment.Renderer != null)
                fragment.Renderer.probeAnchor = anchor;
        }

        /// <summary>
        /// Attached pieces render normally but leave shadow casting to one exterior-only
        /// proxy. This removes both the fracture cap at every shared edge and the tiny
        /// per-renderer shadow-map discontinuities. A loose piece restores the exact modes
        /// copied from the source renderer at bake time.
        /// </summary>
        internal static void SetFragmentShadowMode(FragmentRecord fragment,
            bool dynamic)
        {
            if (fragment == null) return;
            List<Renderer> renderers = fragment.ProbeRenderers;
            List<ShadowCastingMode> modes = fragment.DynamicShadowCastingModes;
            bool assignedPrimary = false;
            for (int i = 0; renderers != null && i < renderers.Count; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null) continue;
                renderer.shadowCastingMode = dynamic && modes != null && i < modes.Count
                    ? modes[i]
                    : ShadowCastingMode.Off;
                if (renderer == fragment.Renderer) assignedPrimary = true;
            }
            if (!assignedPrimary && fragment.Renderer != null)
            {
                fragment.Renderer.shadowCastingMode = dynamic && modes != null &&
                    modes.Count > 0 ? modes[0] : ShadowCastingMode.Off;
            }
        }

        void RebuildAttachedShadowProxy()
        {
            m_attachedShadowProxyDirty = false;
            if (AttachedShadowProxyFilter == null ||
                AttachedShadowProxyRenderer == null || FracturedRoot == null)
                return;

            var combine = new List<CombineInstance>(Fragments.Count * 2);
            Matrix4x4 toProxy = AttachedShadowProxyFilter.transform.worldToLocalMatrix;
            for (int i = 0; i < Fragments.Count; i++)
            {
                FragmentRecord fragment = Fragments[i];
                if (fragment == null || fragment.Transform == null ||
                    fragment.Renderer == null || fragment.Activated ||
                    fragment.Detached || fragment.BudgetDespawned ||
                    fragment.DebrisDespawned ||
                    !fragment.Transform.gameObject.activeInHierarchy)
                    continue;

                MeshFilter filter = fragment.Renderer.GetComponent<MeshFilter>();
                Mesh mesh = filter != null ? filter.sharedMesh : null;
                if (mesh == null) continue;
                int exteriorCount = Mathf.Clamp(fragment.ExteriorSubMeshCount,
                    0, mesh.subMeshCount);
                for (int subMesh = 0; subMesh < exteriorCount; subMesh++)
                {
                    combine.Add(new CombineInstance
                    {
                        mesh = mesh,
                        subMeshIndex = subMesh,
                        transform = toProxy * filter.transform.localToWorldMatrix
                    });
                }
            }

            if (m_runtimeAttachedShadowMesh == null)
            {
                m_runtimeAttachedShadowMesh = new Mesh
                {
                    name = name + " Attached Shadow Proxy (Runtime)",
                    indexFormat = UnityEngine.Rendering.IndexFormat.UInt32
                };
            }
            else
            {
                m_runtimeAttachedShadowMesh.Clear();
            }

            if (combine.Count == 0)
            {
                AttachedShadowProxyFilter.sharedMesh = m_runtimeAttachedShadowMesh;
                AttachedShadowProxyRenderer.enabled = false;
                return;
            }

            m_runtimeAttachedShadowMesh.CombineMeshes(combine.ToArray(), true, true,
                false);
            m_runtimeAttachedShadowMesh.RecalculateBounds();
            AttachedShadowProxyFilter.sharedMesh = m_runtimeAttachedShadowMesh;
            AttachedShadowProxyRenderer.enabled = true;
        }

        void OnDestroy()
        {
            if (m_runtimeAttachedShadowMesh != null)
                Destroy(m_runtimeAttachedShadowMesh);
        }

        /// <summary>
        /// A break may not fire a piece downward faster than simply falling would deliver it.
        ///
        /// The existing ground-safe eject only looks at pieces that are already touching a
        /// floor, which is not where the trouble is: a piece halfway up a barrel is nowhere
        /// near the ground when it is launched, it merely arrives there at full launch speed.
        /// Free-fall over the gap it actually has is the natural ceiling — it needs no setting,
        /// it scales with height on its own, and it leaves debris launched high in the air
        /// completely untouched, so the break still looks the way it was authored.
        /// </summary>
        void LimitLaunchTowardStaticGround(FragmentRecord fragment, Rigidbody body,
            float descentSpeed)
        {
            if (body == null || descentSpeed <= 0.01f) return;

            // Look only as far ahead as this launch would actually carry the piece.
            float reach = descentSpeed * Mathf.Max(0.02f, Time.fixedDeltaTime) * 4f;
            Bounds bounds = GetStructuralBounds(fragment);
            if (!TryGetStaticGroundBelow(bounds, reach, out float gap)) return;

            float gravity = Mathf.Abs(Physics.gravity.y);
            float freeFallSpeed = gravity > 0.0001f
                ? Mathf.Sqrt(2f * gravity * gap)
                : 0f;
            if (descentSpeed <= freeFallSpeed) return;

            body.AddForce(Vector3.up * (descentSpeed - freeFallSpeed),
                ForceMode.VelocityChange);
        }

        /// <summary>
        /// Downward speed Unity's AddExplosionForce is about to add. It applies the force along
        /// the normalized vector from the explosion position — lowered by the upward modifier —
        /// to the body's centre of mass, scaled by linear distance falloff when a radius was
        /// given. Reproducing that here is the only way to know the launch before the next
        /// physics step integrates it.
        /// </summary>
        static float PredictExplosionDescentSpeed(Rigidbody body, float energy,
            Vector3 point, float radius, float upwardModifier, ForceMode mode)
        {
            if (body == null || energy <= 0f) return 0f;
            Vector3 delta = body.worldCenterOfMass -
                            (point - Vector3.up * upwardModifier);
            float distance = delta.magnitude;
            if (distance < 0.000001f) return 0f;

            float falloff = radius > 0f
                ? Mathf.Max(0f, 1f - distance / radius)
                : 1f;
            if (falloff <= 0f) return 0f;

            float speed = mode == ForceMode.VelocityChange
                ? energy * falloff
                : energy * falloff / Mathf.Max(0.0001f, body.mass);
            return Mathf.Max(0f, speed * (-delta.y / distance));
        }

        /// <summary>
        /// Continuous Speculative is the cheap approximation: it widens contact generation but
        /// still lets a thin, fast piece pass through a surface, and Unity documents it as
        /// such. Debris is exactly that case, and a floor is often modelled as a zero-thickness
        /// plane, which has no volume for the solver to push a piece back out of — once the
        /// piece is below it, there is nothing left to collide with and it falls forever.
        ///
        /// Continuous — not ContinuousDynamic — is the right mode for that. It sweeps against
        /// static geometry, which is the floor and the only thing debris tunnels through in
        /// practice, and leaves piece-against-piece discrete. ContinuousDynamic adds sweeping
        /// between the fragments themselves, and eighty siblings grinding in one pile then
        /// generate a stream of extra collision events: every one of those reaches
        /// FragmentRuntime.OnCollisionEnter and is reported as an impact, which a profile with
        /// Play On Every Hit turns into a continuous rattle of hit sounds.
        ///
        /// Baked kinematic shell pieces keep the authored mode. Once a piece is released it
        /// always receives the static sweep guard, even when an old profile saved Discrete:
        /// a zero-thickness Plane offers no recovery after one missed step. The cost is paid
        /// only by bodies that are actually flying, and Continuous does not sweep debris
        /// against the other dynamic debris pieces.
        /// </summary>
        static void ApplyFragmentTunnelGuard(Rigidbody body)
        {
            if (body == null) return;
            body.collisionDetectionMode = CollisionDetectionMode.Continuous;
        }

        /// <summary>
        /// Bounds how fast one released piece may spin. Without this every fragment inherits
        /// the project-wide Default Max Angular Speed, which is unrelated to the profile: on
        /// a project that raised it, an off-centre hit spun small debris like a top and drove
        /// it through the floor between two physics steps. The limit is derived from the
        /// authored launch speed and the piece's own size, so it needs no separate setting.
        /// </summary>
        void ApplyFragmentSpinLimit(FragmentRecord fragment, Rigidbody body)
        {
            if (Profile == null || body == null) return;
            body.maxAngularVelocity = Profile.ResolveMaxAngularSpeed(
                FragmentBoundingRadius(fragment));
        }

        static float FragmentBoundingRadius(FragmentRecord fragment)
        {
            if (fragment == null) return 0f;
            if (fragment.Renderer != null)
                return fragment.Renderer.bounds.extents.magnitude;
            if (fragment.Collider != null)
                return fragment.Collider.bounds.extents.magnitude;
            return 0f;
        }

        void ApplyTallFragmentTopple(int index, FragmentRecord fragment,
            Rigidbody body, float pushEnergy)
        {
            if (Profile == null || body == null || fragment == null ||
                fragment.Renderer == null || Profile.TallFragmentToppleSpeed <= 0f)
                return;

            Vector3 size = fragment.Renderer.bounds.size;
            float horizontalSize = Mathf.Max(size.x, size.z);
            float uprightRatio = size.y / Mathf.Max(horizontalSize, 0.001f);
            if (uprightRatio < 1.35f) return;

            float slenderness = Mathf.Clamp01((uprightRatio - 1.35f) / 2.65f);
            float energyScale = Mathf.Clamp(pushEnergy / 30f, 0f, 2f);
            float speed = Profile.TallFragmentToppleSpeed *
                          Mathf.Lerp(0.35f, 1f, slenderness) * energyScale;

            // Golden-angle distribution avoids every fragment leaning in the same
            // direction while remaining deterministic across preview and play mode.
            float angle = Mathf.Repeat((index + 1) * 2.39996323f +
                (StableId != null ? StableId.GetHashCode() * 0.0001f : 0f),
                Mathf.PI * 2f);
            Vector3 sideways = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            Vector3 torqueAxis = Vector3.Cross(Vector3.up, sideways).normalized;
            body.AddForce(sideways * (speed * 0.3f), ForceMode.Impulse);
            body.AddTorque(torqueAxis * speed, ForceMode.Impulse);
        }

        internal void ReportFragmentImpact(int index, Vector3 point, float energy, float size = 1.0f)
        {
            FragmentImpact?.Invoke(point, energy, index, size);
        }

        /// <summary>
        /// Replaces one eligible primary fragment with its already pre-baked level-two
        /// children after FragmentRuntime has confirmed a real ground landing.  This
        /// deliberately shares the dust gate (height, downward speed and static upward
        /// ground) instead of treating every physics contact as damage.  It never
        /// creates or slices geometry at runtime and is disabled for Dynamic Runtime
        /// Re-Fracture profiles.
        /// </summary>
        internal bool TryBreakSecondaryOnGroundLanding(
            int parentIndex, Vector3 point, Vector3 groundNormal)
        {
            if (Profile == null || Profile.RuntimeFragmentSplitting ||
                !CanSecondaryBreak(parentIndex))
                return false;

            FragmentRecord parent = Fragments[parentIndex];
            if (parent == null) return false;

            if (Profile.FragmentHealthFraction > 0f)
            {
                if (!parent.RuntimeHealthReady)
                {
                    parent.RuntimeHealth = Mathf.Max(10f, Profile.Health * Profile.FragmentHealthFraction);
                    parent.RuntimeHealthReady = true;
                }

                parent.RuntimeHealth -= Profile.SecondaryStrength * 0.5f;
                if (parent.RuntimeHealth > 0f)
                    return false;
            }

            SecondaryFragmentNode secondary = parent.SecondaryNode;
            if (secondary == null || secondary.IsBroken)
                return false;

            Vector3 normal = groundNormal.sqrMagnitude > 0.0001f
                ? groundNormal.normalized : Vector3.up;
            return secondary.BreakSecondary(point, normal);
        }

        internal void ReportRuntimeFragmentHit(DamageContext context)
        {
            TrySpreadImpact(context);
            HitReceived?.Invoke(context);
        }

        /// <summary>
        /// Weakest LOAD-BEARING horizontal section of one structural element. The element is
        /// sliced into height bands and each band reports how much of its original material
        /// is still attached, but only bands that still carry material above them count.
        ///
        /// A section with nothing left on top of it transmits no weight, so losing it cannot
        /// bring anything down: shearing the top off a tower must leave the tower standing.
        /// Taking the thinnest band anywhere — the old rule — meant a wrecking ball landing
        /// on the roof emptied the topmost band, drove the ratio to zero and collapsed the
        /// whole structure from a hit that removed a few percent of its material. Cutting
        /// through the middle still collapses it, because the intact top above the gap is
        /// then a real load resting on nothing.
        /// </summary>
        internal float GetGroupWeakestSectionRatio(int structuralGroup) =>
            GetGroupWeakestSectionRatio(structuralGroup, out _);

        /// <summary>
        /// Reports whether the still-attached material of one baked source element has
        /// separated into more than one connectivity island. This remains useful for
        /// diagnostics; it must not override an authored section threshold. An unsupported
        /// chip appearing as a second island is not evidence that the remaining thin load
        /// path can carry the wall above it.
        /// </summary>
        /// <summary>
        /// True when the bake stored the data the connectivity sweep needs: at least one
        /// ground anchor and at least one neighbour link. Prefabs generated before that
        /// data existed have neither, and the sweep must not guess for them — callers fall
        /// back to the coarser section rule instead.
        /// </summary>
        internal bool HasBakedSupportConnectivity
        {
            get
            {
                if (Fragments == null) return false;
                bool anchor = false;
                bool link = false;
                for (int i = 0; i < Fragments.Count; i++)
                {
                    FragmentRecord fragment = Fragments[i];
                    if (fragment == null) continue;
                    if (fragment.GroundAnchor) anchor = true;
                    if (fragment.SupportNeighbours != null &&
                        fragment.SupportNeighbours.Count > 0)
                        link = true;
                    if (anchor && link) return true;
                }
                return false;
            }
        }

        /// <summary>
        /// True when this element owns at least one ground anchor. A group without one is
        /// carried rather than grounded — the bake gives no anchor to a deck, a beam or any
        /// group whose base sits above the object's lowest point — and the connectivity
        /// sweep can only ever find such a group unsupported. Callers must keep those on the
        /// structural-group rule instead of handing them to the sweep.
        /// </summary>
        internal bool HasGroupGroundAnchor(int structuralGroup)
        {
            if (Fragments == null) return false;
            for (int i = 0; i < Fragments.Count; i++)
            {
                FragmentRecord fragment = Fragments[i];
                if (fragment != null && fragment.GroundAnchor &&
                    fragment.StructuralGroup == structuralGroup)
                    return true;
            }
            return false;
        }

        internal bool HasMultipleAttachedIslands(int structuralGroup)
        {
            if (Fragments == null || Fragments.Count < 2) return false;

            if (m_supportVisited == null ||
                m_supportVisited.Length != Fragments.Count)
                m_supportVisited = new bool[Fragments.Count];
            else
                Array.Clear(m_supportVisited, 0, m_supportVisited.Length);
            m_supportQueue.Clear();

            int candidateCount = 0;
            bool hasBakedConnectivity = false;
            for (int i = 0; i < Fragments.Count; i++)
            {
                FragmentRecord fragment = Fragments[i];
                if (fragment == null || fragment.StructuralGroup != structuralGroup ||
                    !IsAttachedSupportCandidate(i))
                    continue;

                candidateCount++;
                List<int> links = fragment.SupportNeighbours;
                if (links == null) continue;
                for (int n = 0; n < links.Count; n++)
                {
                    int neighbour = links[n];
                    if (neighbour < 0 || neighbour >= Fragments.Count) continue;
                    FragmentRecord linked = Fragments[neighbour];
                    if (linked != null && linked.StructuralGroup == structuralGroup)
                    {
                        hasBakedConnectivity = true;
                        break;
                    }
                }
            }

            // Legacy bakes have no neighbour graph. Preserve their previous behaviour
            // instead of treating every individual fragment as an island.
            if (candidateCount < 2 || !hasBakedConnectivity) return false;

            int islandCount = 0;
            for (int i = 0; i < Fragments.Count; i++)
            {
                FragmentRecord start = Fragments[i];
                if (start == null || start.StructuralGroup != structuralGroup ||
                    m_supportVisited[i] || !IsAttachedSupportCandidate(i))
                    continue;

                islandCount++;
                if (islandCount > 1) return true;

                m_supportVisited[i] = true;
                m_supportQueue.Enqueue(i);
                while (m_supportQueue.Count > 0)
                {
                    int current = m_supportQueue.Dequeue();
                    List<int> links = Fragments[current].SupportNeighbours;
                    if (links == null) continue;
                    for (int n = 0; n < links.Count; n++)
                    {
                        int neighbour = links[n];
                        if (neighbour < 0 || neighbour >= Fragments.Count ||
                            m_supportVisited[neighbour])
                            continue;
                        FragmentRecord next = Fragments[neighbour];
                        if (next == null || next.StructuralGroup != structuralGroup ||
                            !IsAttachedSupportCandidate(neighbour))
                            continue;
                        m_supportVisited[neighbour] = true;
                        m_supportQueue.Enqueue(neighbour);
                    }
                }
            }

            return false;
        }

        internal float GetGroupWeakestSectionRatio(int structuralGroup, out int failedBand)
        {
            failedBand = -1;
            if (m_state < DestructibleState.Fractured) return 1f;

            if (!TryGetSectionBaseline(structuralGroup, out float[] totals,
                    out float minY, out float height, out List<int> members,
                    out SectionLaneLayout lanes))
                return 1f;

            int laneCount = Mathf.Max(1, lanes.Count);
            int cellCount = SectionBands * laneCount;
            // Scratch buffer reused across groups. Validate it on every structural poll:
            // the support node can survive a Unity hot reload with an obsolete array.
            // several times a second per element, and a fresh array each time was pure
            // garbage for a value that lives only to the end of the method.
            // Unity hot reload can preserve a non-serialized array created by an older
            // script revision. Validate the size as well as null so a changed band count
            // cannot leave a stale buffer that fails on the following support poll.
            if (m_sectionScratch == null || m_sectionScratch.Length != cellCount)
                m_sectionScratch = new float[cellCount];
            float[] attached = m_sectionScratch;
            for (int cell = 0; cell < cellCount; cell++) attached[cell] = 0f;

            // Only this element's fragments, resolved once at baseline capture instead of
            // rescanning the whole fragment list on every poll. The signature is folded
            // into this already-required walk. It is group-local and includes the exact
            // fragment index plus its attached state, so two walls cannot share or confuse
            // damage state even when their fragments overlap in world space.
            float attachedTotal = 0f;
            bool missingAny = false;
            ulong attachedSignature = 14695981039346656037UL;
            for (int m = 0; m < members.Count; m++)
            {
                int index = members[m];
                // The baseline helper repairs stale membership before this loop. Keep the
                // boundary guarded as well because generated fragment lists are public and
                // a gameplay script can still replace one between support checks.
                bool isAttached = index >= 0 && Fragments != null &&
                    index < Fragments.Count &&
                    Fragments[index] != null &&
                    Fragments[index].StructuralGroup == structuralGroup &&
                    IsAttachedSupportCandidate(index);
                unchecked
                {
                    attachedSignature ^= (uint)index;
                    attachedSignature *= 1099511628211UL;
                    attachedSignature ^= isAttached ? 1UL : 0UL;
                    attachedSignature *= 1099511628211UL;
                }
                if (!isAttached)
                {
                    missingAny = true;
                    continue;
                }
                FragmentRecord fragment = Fragments[index];
                float volume = Mathf.Max(0f, fragment.Volume);
                AccumulateSectionCellWeights(attached, fragment, minY, height,
                    lanes, SectionBands);
                attachedTotal += volume;
            }
            unchecked
            {
                attachedSignature ^= (uint)members.Count;
                attachedSignature *= 1099511628211UL;
            }

            float weakest = EvaluateWeakestSectionRatio(totals, attached,
                attachedTotal, laneCount, out failedBand, out int failedLane);

            EnsureSectionCacheContainers();
            if (!m_refinedSectionCaches.TryGetValue(structuralGroup,
                    out RefinedSectionCache refined))
            {
                refined = new RefinedSectionCache();
                m_refinedSectionCaches.Add(structuralGroup, refined);
            }

            // A burst can activate many fragments in one physics step. The signature
            // coalesces all of them into one refinement on the next structural poll, then
            // the scalar result is reused until this particular group's attached set changes.
            if (!refined.HasSignature ||
                refined.AttachedSignature != attachedSignature)
            {
                refined.HasSignature = true;
                refined.AttachedSignature = attachedSignature;
                refined.Ratio = 1f;
                refined.FailedBand = -1;
                refined.FailedLane = -1;

                // The intact state is exactly one at every occupied slice, so do not even
                // allocate the fine baseline until there is material missing.
                if (missingAny)
                {
                    int refinedCellCount = RefinedSectionBands * laneCount;
                    if (refined.Totals == null ||
                        refined.Totals.Length != refinedCellCount)
                    {
                        refined.Totals = CaptureRefinedSectionBaseline(
                            structuralGroup, minY, height, members, lanes);
                    }
                    if (m_refinedSectionScratch == null ||
                        m_refinedSectionScratch.Length != refinedCellCount)
                    {
                        m_refinedSectionScratch =
                            new float[refinedCellCount];
                    }
                    for (int cell = 0; cell < refinedCellCount; cell++)
                        m_refinedSectionScratch[cell] = 0f;

                    for (int m = 0; m < members.Count; m++)
                    {
                        int index = members[m];
                        if (index < 0 || Fragments == null ||
                            index >= Fragments.Count ||
                            Fragments[index] == null ||
                            Fragments[index].StructuralGroup != structuralGroup ||
                            !IsAttachedSupportCandidate(index))
                            continue;
                        AccumulateSectionCellWeights(m_refinedSectionScratch,
                            Fragments[index], minY, height, lanes,
                            RefinedSectionBands);
                    }

                    refined.Ratio = EvaluateWeakestSectionRatio(
                        refined.Totals, m_refinedSectionScratch, attachedTotal,
                        laneCount, out refined.FailedBand, out refined.FailedLane);
                    if (refined.FailedBand >= 0)
                    {
                        refined.FailedSectionY = minY + height *
                            (refined.FailedBand / (float)RefinedSectionBands);
                    }
                }
            }

            refined.HasSelectedFailure = false;
            refined.SelectedFailureAxis = -1;
            int selectedLane = -1;
            if (refined.FailedBand >= 0 && refined.Ratio <= weakest)
            {
                weakest = refined.Ratio;
                failedBand = Mathf.Clamp(
                    refined.FailedBand * SectionBands / RefinedSectionBands,
                    0, SectionBands - 1);
                selectedLane = refined.FailedLane;
                refined.SelectedFailureY = refined.FailedSectionY;
                refined.HasSelectedFailure = true;
            }
            else if (failedBand >= 0)
            {
                selectedLane = failedLane;
                refined.SelectedFailureY = minY +
                    height * (failedBand / (float)SectionBands);
                refined.HasSelectedFailure = true;
            }

            // Only the lane that failed is a failed load path. Recording its world range
            // here keeps ReleaseGroupAboveSection on the same signature-cached result and
            // stops a hole near one end of a long wall from dropping the far end with it.
            if (refined.HasSelectedFailure && selectedLane >= 0 && laneCount > 1)
            {
                refined.SelectedFailureAxis = lanes.Axis;
                refined.SelectedFailureLaneMin = lanes.LaneMin(selectedLane);
                refined.SelectedFailureLaneMax = lanes.LaneMax(selectedLane);
            }
            return weakest;
        }

        static float EvaluateWeakestSectionRatio(float[] totals, float[] attached,
            float attachedTotal, int laneCount, out int failedBand, out int failedLane)
        {
            failedBand = -1;
            failedLane = -1;
            if (totals == null || attached == null || totals.Length == 0 ||
                totals.Length != attached.Length)
                return 1f;

            laneCount = Mathf.Max(1, laneCount);
            int bandCount = totals.Length / laneCount;
            if (bandCount <= 0) return 1f;

            // A stray chip still clinging above a band is not a load worth collapsing for.
            // The floor stays a share of the whole element on purpose: a lane only fails
            // once it is genuinely carrying something, never for the few litres of debris
            // left leaning above it.
            float loadFloor = attachedTotal * 0.02f;
            float weakest = 1f;
            float carriedAbove = 0f;
            // Failure is decided on the whole slice, never on a single lane. A lane cell is
            // roughly one fragment of material on a wall of ordinary fragment count, so
            // judging it alone made one shot read as a severed section and drop the column
            // above it — a chain reaction exactly where the player aimed. The slice keeps
            // the coarse, meaningful denominator; lanes only answer WHERE inside that
            // failed slice the material went, so the release stays local instead of
            // levelling the whole element.
            for (int band = bandCount - 1; band >= 0; band--)
            {
                float bandTotal = 0f;
                float bandAttached = 0f;
                for (int lane = 0; lane < laneCount; lane++)
                {
                    int cell = band * laneCount + lane;
                    bandTotal += totals[cell];
                    bandAttached += attached[cell];
                }

                if (bandTotal > 1e-9f && carriedAbove > loadFloor)
                {
                    float ratio = bandAttached / bandTotal;
                    if (ratio < weakest)
                    {
                        weakest = ratio;
                        failedBand = band;
                        failedLane = WeakestLaneInBand(totals, attached, band, laneCount);
                    }
                }
                carriedAbove += bandAttached;
            }
            return weakest;
        }

        /// <summary>
        /// Which lane of an already-failed slice lost the most material. This only scopes
        /// the release; it can never start one on its own.
        /// </summary>
        static int WeakestLaneInBand(float[] totals, float[] attached, int band,
            int laneCount)
        {
            int weakestLane = -1;
            float weakestRatio = float.MaxValue;
            for (int lane = 0; lane < laneCount; lane++)
            {
                int cell = band * laneCount + lane;
                if (totals[cell] <= 1e-9f) continue;
                float ratio = attached[cell] / totals[cell];
                if (ratio >= weakestRatio) continue;
                weakestRatio = ratio;
                weakestLane = lane;
            }
            return weakestLane;
        }

        /// <summary>
        /// Drops only the material resting ABOVE a section that can no longer carry it.
        /// A tower cut through at mid height loses its top; the base below the cut is still
        /// standing on its own intact footing and has no physical reason to fall with it.
        /// Releasing the whole element instead — the previous behaviour — is why a single
        /// blow anywhere on a one-piece structure levelled the entire thing. Returns how
        /// many fragments were released.
        /// </summary>
        internal int ReleaseGroupAboveSection(
            int structuralGroup, int failedBand, Vector3 point)
        {
            if (Profile == null || m_state < DestructibleState.Fractured ||
                failedBand < 0 ||
                !TryGetSectionBaseline(structuralGroup, out _, out float minY,
                    out float height, out List<int> members))
                return 0;

            var context = new BreakContext(point, Vector3.down, 0f, 0f);
            int released = 0;
            for (int m = 0; m < members.Count; m++)
            {
                int index = members[m];
                if (index < 0 || Fragments == null || index >= Fragments.Count) continue;
                FragmentRecord fragment = Fragments[index];
                if (fragment == null || fragment.StructuralGroup != structuralGroup ||
                    fragment.Activated || fragment.Detached ||
                    !HasActivatablePhysics(fragment) || fragment.Transform == null)
                    continue;
                // A fragment that crosses the failed slice is part of the failed load
                // path even when its pivot lies below it. Centroid-only classification
                // left tall and diagonal shards kinematic across a visible gap.
                float failedSectionY = minY +
                    height * (failedBand / (float)SectionBands);
                int failedAxis = -1;
                float failedLaneMin = 0f;
                float failedLaneMax = 0f;
                if (m_refinedSectionCaches != null &&
                    m_refinedSectionCaches.TryGetValue(structuralGroup,
                        out RefinedSectionCache refined) &&
                    refined.HasSelectedFailure)
                {
                    failedSectionY = refined.SelectedFailureY;
                    failedAxis = refined.SelectedFailureAxis;
                    failedLaneMin = refined.SelectedFailureLaneMin;
                    failedLaneMax = refined.SelectedFailureLaneMax;
                }
                Bounds fragmentBounds = GetStructuralBounds(fragment);
                if (fragmentBounds.max.y <= failedSectionY + 0.0001f)
                    continue;
                // Material standing over an intact part of the same slice keeps a real
                // load path to the footing and must not be released with the cut lane.
                // Anything the cut genuinely stranded is still collected by the
                // connectivity sweep below.
                if (failedAxis >= 0)
                {
                    float boundsMin = failedAxis == 0
                        ? fragmentBounds.min.x : fragmentBounds.min.z;
                    float boundsMax = failedAxis == 0
                        ? fragmentBounds.max.x : fragmentBounds.max.z;
                    if (boundsMax < failedLaneMin - 0.0001f ||
                        boundsMin > failedLaneMax + 0.0001f)
                        continue;
                }
                ActivateFragment(index, context, Vector3.zero, Vector3.zero);
                released++;
            }

            if (released > 0)
            {
                m_state = DestructibleState.Fractured;
                // Losing the top can strand pieces further down that were only held by it.
                // The section threshold belongs to one baked source element. Sweep that
                // element's remaining islands now, but do not let contact pruning wake a
                // corner shard in another wall. Other groups have their own support probes
                // and grace timers and will release only if their carrier really disappears.
                ReleaseUnsupportedIslands(context, out _, structuralGroup);
                Broken?.Invoke(context);
            }
            return released;
        }

        /// <summary>
        /// Applies the cantilever limit after the ordinary connectivity search. The support
        /// graph answers whether material is connected; it is not a metric path mesh and can
        /// zigzag or leave two nearby shards connected only through another source element.
        /// Measuring distance while walking that graph made a small lower hit drop remote
        /// upper pieces. Once a grounded element has lost the Required Support Ratio of its
        /// material, a remaining fragment is therefore supported when it is connected to the
        /// structure and lies within straight horizontal reach of any surviving footing in
        /// its structural group. Healthy elements and carried decks/roofs retain the ordinary
        /// connectivity verdict supplied by their real joints.
        /// </summary>
        void ApplyBoundedSupportReach(float spanLimit)
        {
            float spanSquared = spanLimit * spanLimit;
            float requiredRemainingRatio = Profile != null
                ? Mathf.Clamp01(Profile.RequiredSupportRatio)
                : 0f;
            var totalMaterial = new Dictionary<int, float>();
            var attachedMaterial = new Dictionary<int, float>();
            for (int i = 0; i < Fragments.Count; i++)
            {
                FragmentRecord candidate = Fragments[i];
                if (candidate == null ||
                    !m_anchorGroups.Contains(candidate.StructuralGroup))
                    continue;
                float weight = Mathf.Max(0.0001f, candidate.Volume);
                totalMaterial.TryGetValue(candidate.StructuralGroup,
                    out float total);
                totalMaterial[candidate.StructuralGroup] = total + weight;
                if (!IsAttachedSupportCandidate(i)) continue;
                attachedMaterial.TryGetValue(candidate.StructuralGroup,
                    out float attached);
                attachedMaterial[candidate.StructuralGroup] = attached + weight;
            }

            for (int i = 0; i < Fragments.Count; i++)
            {
                if (!m_supportVisited[i] || m_supportPruned[i] ||
                    !IsAttachedSupportCandidate(i))
                    continue;

                FragmentRecord fragment = Fragments[i];
                int structuralGroup = fragment.StructuralGroup;
                if (!m_anchorGroups.Contains(structuralGroup)) continue;

                // A span limit is a late-collapse rule, not an extra way for the first local
                // hit to invalidate a healthy wall. Until the grounded element has lost the
                // configured material fraction, ordinary connectivity remains authoritative.
                // Once most of the wall is gone, the limit releases the large sheet that is
                // connected only through small surviving side/base remnants.
                totalMaterial.TryGetValue(structuralGroup, out float total);
                attachedMaterial.TryGetValue(structuralGroup, out float attached);
                bool forcedByProbeFailure =
                    structuralGroup == m_forcedSpanStructuralGroup;
                if (!forcedByProbeFailure && total > 0f &&
                    attached / total >= requiredRemainingRatio)
                    continue;

                Vector3 center = GetStructuralBounds(fragment).center;
                bool inReach = false;
                for (int seedIndex = 0; seedIndex < Fragments.Count; seedIndex++)
                {
                    if (!m_supportSeeds[seedIndex] || m_supportPruned[seedIndex] ||
                        !IsAttachedSupportCandidate(seedIndex))
                        continue;
                    FragmentRecord seed = Fragments[seedIndex];
                    if (seed.StructuralGroup != structuralGroup) continue;

                    Vector3 seedCenter = GetStructuralBounds(seed).center;
                    float dx = center.x - seedCenter.x;
                    float dz = center.z - seedCenter.z;
                    if (dx * dx + dz * dz <= spanSquared)
                    {
                        inReach = true;
                        break;
                    }
                }

                if (!inReach) m_supportVisited[i] = false;
            }
        }

        /// <summary>
        /// Resolves one structural group's immutable section baseline and repairs only cache
        /// state that cannot have been produced by the current fragment list. This matters in
        /// the Editor where hot reload can preserve private arrays/dictionaries from the
        /// previous script revision while the newly compiled code uses a different band
        /// count. It also keeps runtime-generated or user-edited fragment lists from indexing
        /// an obsolete membership table.
        /// </summary>
        bool TryGetSectionBaseline(int structuralGroup, out float[] totals,
            out float minY, out float height, out List<int> members)
        {
            return TryGetSectionBaseline(structuralGroup, out totals, out minY,
                out height, out members, out _);
        }

        bool TryGetSectionBaseline(int structuralGroup, out float[] totals,
            out float minY, out float height, out List<int> members,
            out SectionLaneLayout lanes)
        {
            EnsureSectionCacheContainers();

            int fragmentCount = Fragments != null ? Fragments.Count : 0;
            if (!ReferenceEquals(m_sectionFragmentList, Fragments) ||
                m_sectionFragmentListCount != fragmentCount)
            {
                // Fragments are normally immutable after Bake. If external/runtime code did
                // replace the public list, invalidate every group together so a baseline
                // captured earlier in this same support pass cannot validate another group.
                m_sectionTotals.Clear();
                m_sectionMinY.Clear();
                m_sectionHeight.Clear();
                m_sectionGroupFragments.Clear();
                m_sectionLaneLayouts.Clear();
                m_refinedSectionCaches.Clear();
                m_sectionFragmentList = Fragments;
                m_sectionFragmentListCount = fragmentCount;
            }

            bool valid = m_sectionTotals.TryGetValue(structuralGroup, out totals) &&
                         m_sectionLaneLayouts.TryGetValue(structuralGroup, out lanes) &&
                         lanes != null && totals != null &&
                         totals.Length == SectionBands * lanes.Count &&
                         m_sectionMinY.TryGetValue(structuralGroup, out minY) &&
                         m_sectionHeight.TryGetValue(structuralGroup, out height) &&
                         m_sectionGroupFragments.TryGetValue(structuralGroup, out members) &&
                         members != null;
            if (!valid)
            {
                totals = CaptureSectionBaseline(structuralGroup);
                if (totals != null) m_sectionTotals[structuralGroup] = totals;
            }

            if (totals == null ||
                !m_sectionLaneLayouts.TryGetValue(structuralGroup, out lanes) ||
                lanes == null || totals.Length != SectionBands * lanes.Count ||
                !m_sectionMinY.TryGetValue(structuralGroup, out minY) ||
                !m_sectionHeight.TryGetValue(structuralGroup, out height) ||
                !m_sectionGroupFragments.TryGetValue(structuralGroup, out members) ||
                members == null)
            {
                minY = 0f;
                height = 1f;
                members = null;
                lanes = s_singleSectionLane;
                return false;
            }
            return true;
        }

        void EnsureSectionCacheContainers()
        {
            if (m_sectionTotals == null)
                m_sectionTotals = new Dictionary<int, float[]>();
            if (m_sectionMinY == null)
                m_sectionMinY = new Dictionary<int, float>();
            if (m_sectionHeight == null)
                m_sectionHeight = new Dictionary<int, float>();
            if (m_sectionGroupFragments == null)
                m_sectionGroupFragments = new Dictionary<int, List<int>>();
            if (m_sectionLaneLayouts == null)
                m_sectionLaneLayouts = new Dictionary<int, SectionLaneLayout>();
            if (m_refinedSectionCaches == null)
                m_refinedSectionCaches =
                    new Dictionary<int, RefinedSectionCache>();
        }

        float[] CaptureSectionBaseline(int structuralGroup)
        {
            EnsureSectionCacheContainers();
            // A rebuilt coarse baseline invalidates the fine baseline and signature for
            // this group as one atomic cache unit.
            m_refinedSectionCaches.Remove(structuralGroup);
            Bounds groupBounds = default;
            bool hasBounds = false;
            float volumeTotal = 0f;
            int memberCount = 0;
            for (int i = 0; i < Fragments.Count; i++)
            {
                FragmentRecord fragment = Fragments[i];
                if (fragment == null || fragment.StructuralGroup != structuralGroup ||
                    fragment.Transform == null)
                    continue;
                Bounds bounds = GetStructuralBounds(fragment);
                if (!hasBounds) { groupBounds = bounds; hasBounds = true; }
                else groupBounds.Encapsulate(bounds);
                volumeTotal += Mathf.Max(0f, fragment.Volume);
                memberCount++;
            }
            if (!hasBounds) return null;

            float minY = groupBounds.min.y;
            float height = Mathf.Max(0.001f, groupBounds.size.y);
            m_sectionMinY[structuralGroup] = minY;
            m_sectionHeight[structuralGroup] = height;

            SectionLaneLayout lanes = BuildSectionLaneLayout(groupBounds, height,
                volumeTotal, memberCount);
            m_sectionLaneLayouts[structuralGroup] = lanes;

            // Membership never changes after the bake, so resolve it once here and let the
            // per-poll ratio walk only this element's fragments.
            if (m_sectionGroupFragments == null)
                m_sectionGroupFragments = new Dictionary<int, List<int>>();
            var members = new List<int>();
            var totals = new float[SectionBands * lanes.Count];
            for (int i = 0; i < Fragments.Count; i++)
            {
                FragmentRecord fragment = Fragments[i];
                if (fragment == null || fragment.StructuralGroup != structuralGroup ||
                    fragment.Transform == null)
                    continue;
                members.Add(i);
                AccumulateSectionCellWeights(totals, fragment, minY, height,
                    lanes, SectionBands);
            }
            m_sectionGroupFragments[structuralGroup] = members;
            return totals;
        }

        /// <summary>
        /// Splits one element's height bands along its longer horizontal axis. A lane must
        /// stay wide enough that its ratio still describes material rather than a single
        /// shard, so the width never drops below a couple of fragments, the element's own
        /// thickness, or one band height. Anything that is not clearly longer than it is
        /// thick keeps a single lane and the previous whole-section behaviour.
        /// </summary>
        static SectionLaneLayout BuildSectionLaneLayout(Bounds groupBounds,
            float height, float volumeTotal, int memberCount)
        {
            int axis = groupBounds.size.x >= groupBounds.size.z ? 0 : 2;
            float extent = axis == 0 ? groupBounds.size.x : groupBounds.size.z;
            float thickness = axis == 0 ? groupBounds.size.z : groupBounds.size.x;
            float fragmentSize = memberCount > 0 && volumeTotal > 1e-9f
                ? Mathf.Pow(volumeTotal / memberCount, 1f / 3f)
                : 0f;
            float minimumWidth = Mathf.Max(
                Mathf.Max(fragmentSize * 2f, thickness),
                height / SectionBands);
            int count = minimumWidth > 1e-6f
                ? Mathf.FloorToInt(extent / minimumWidth)
                : 1;
            count = Mathf.Clamp(count, 1, MaxSectionLanes);
            return new SectionLaneLayout
            {
                Axis = axis,
                Min = axis == 0 ? groupBounds.min.x : groupBounds.min.z,
                Width = Mathf.Max(1e-6f, extent / count),
                Count = count
            };
        }

        float[] CaptureRefinedSectionBaseline(int structuralGroup, float minY,
            float height, List<int> members, SectionLaneLayout lanes)
        {
            var totals = new float[RefinedSectionBands * lanes.Count];
            for (int m = 0; m < members.Count; m++)
            {
                int index = members[m];
                if (index < 0 || Fragments == null ||
                    index >= Fragments.Count)
                    continue;
                FragmentRecord fragment = Fragments[index];
                if (fragment == null ||
                    fragment.StructuralGroup != structuralGroup ||
                    fragment.Transform == null)
                    continue;
                AccumulateSectionCellWeights(totals, fragment, minY, height,
                    lanes, RefinedSectionBands);
            }
            return totals;
        }

        /// <summary>
        /// Distributes a fragment's material through every height slice its real bounds
        /// occupy. The old implementation filed the fragment's entire volume under its
        /// pivot band. A tall or diagonal shard could therefore cross a destroyed section
        /// while contributing no support there, and several shards cut at one height could
        /// be diluted across unrelated centroid bands. Volume is distributed by vertical
        /// overlap, so the sum remains exactly the fragment volume and each band measures
        /// the average material present inside that physical slice.
        /// </summary>
        static void AccumulateSectionWeights(float[] bands, FragmentRecord fragment,
            float minY, float height)
        {
            if (bands == null || bands.Length == 0) return;
            AccumulateSectionCellWeights(bands, fragment, minY, height,
                s_singleSectionLane, bands.Length);
        }

        /// <summary>
        /// Cell layout is band-major: index = band * lanes.Count + lane. A fragment's
        /// volume is split by its vertical overlap with each band and then by its
        /// horizontal overlap with each lane, so the accumulated total still equals the
        /// fragment volume exactly and every cell measures the material really standing
        /// inside that slice of the element.
        /// </summary>
        static void AccumulateSectionCellWeights(float[] cells, FragmentRecord fragment,
            float minY, float height, SectionLaneLayout lanes, int bandCount)
        {
            if (cells == null || cells.Length == 0 || fragment == null ||
                lanes == null || bandCount <= 0)
                return;

            int laneCount = Mathf.Max(1, lanes.Count);
            if (cells.Length < bandCount * laneCount) return;
            float volume = Mathf.Max(0f, fragment.Volume);
            if (volume <= 0f) return;

            Bounds bounds = GetStructuralBounds(fragment);
            float laneMin = lanes.Axis == 0 ? bounds.min.x : bounds.min.z;
            float laneMax = lanes.Axis == 0 ? bounds.max.x : bounds.max.z;
            int firstLane = laneCount > 1 ? lanes.IndexOf(laneMin) : 0;
            int lastLane = laneCount > 1 ? lanes.IndexOf(laneMax) : 0;
            // Lane shares must sum to one even when the fragment reaches outside the
            // captured extent, otherwise material would quietly vanish from the baseline.
            float laneSpan = Mathf.Max(0f, laneMax - laneMin);
            float laneShareTotal = 0f;
            for (int lane = firstLane; lane <= lastLane; lane++)
            {
                laneShareTotal += laneSpan <= 0.0001f
                    ? 1f
                    : Mathf.Max(0f, Mathf.Min(laneMax, lanes.LaneMax(lane)) -
                                    Mathf.Max(laneMin, lanes.LaneMin(lane)));
            }
            if (laneShareTotal <= 1e-9f)
            {
                firstLane = lastLane = laneCount > 1
                    ? lanes.IndexOf((laneMin + laneMax) * 0.5f)
                    : 0;
                laneShareTotal = 1f;
            }

            float verticalSpan = bounds.size.y;
            if (verticalSpan <= 0.0001f || height <= 0.0001f)
            {
                float t = (bounds.center.y - minY) / Mathf.Max(0.0001f, height);
                int band = Mathf.Clamp(Mathf.FloorToInt(t * bandCount),
                    0, bandCount - 1);
                AddLaneShares(cells, band, firstLane, lastLane, laneCount, lanes,
                    laneMin, laneMax, laneSpan, laneShareTotal, volume);
                return;
            }

            float bandHeight = height / bandCount;
            int first = Mathf.Clamp(Mathf.FloorToInt(
                (bounds.min.y - minY) / bandHeight), 0, bandCount - 1);
            // Treat an exact upper boundary as belonging to the band below it. Without
            // the epsilon, a box ending on a boundary contributed a zero-width entry to
            // the following band and made its baseline depend on floating-point noise.
            int last = Mathf.Clamp(Mathf.FloorToInt(
                (bounds.max.y - minY - 0.000001f) / bandHeight),
                0, bandCount - 1);
            float densityPerHeight = volume / verticalSpan;
            for (int band = first; band <= last; band++)
            {
                float bandMin = minY + band * bandHeight;
                float bandMax = bandMin + bandHeight;
                float overlap = Mathf.Max(0f,
                    Mathf.Min(bounds.max.y, bandMax) -
                    Mathf.Max(bounds.min.y, bandMin));
                AddLaneShares(cells, band, firstLane, lastLane, laneCount, lanes,
                    laneMin, laneMax, laneSpan, laneShareTotal,
                    densityPerHeight * overlap);
            }
        }

        static void AddLaneShares(float[] cells, int band, int firstLane, int lastLane,
            int laneCount, SectionLaneLayout lanes, float laneMin, float laneMax,
            float laneSpan, float laneShareTotal, float bandVolume)
        {
            if (bandVolume <= 0f) return;
            // One lane, or material that never crosses a lane boundary: file the whole
            // share directly. This also keeps volume exact for a fragment whose bounds
            // resolve to a single lane by fallback rather than by overlap.
            if (laneCount <= 1 || firstLane >= lastLane)
            {
                cells[band * laneCount +
                      Mathf.Clamp(firstLane, 0, laneCount - 1)] += bandVolume;
                return;
            }
            for (int lane = firstLane; lane <= lastLane; lane++)
            {
                float share = laneSpan <= 0.0001f
                    ? 1f
                    : Mathf.Max(0f, Mathf.Min(laneMax, lanes.LaneMax(lane)) -
                                    Mathf.Max(laneMin, lanes.LaneMin(lane)));
                if (share <= 0f) continue;
                cells[band * laneCount + lane] +=
                    bandVolume * (share / laneShareTotal);
            }
        }

        /// <summary>
        /// Trims a launch impulse so it cannot exceed the profile's maximum launch speed.
        /// An impulse divided by mass IS a speed, which is why the same break used to send a
        /// wooden plank away roughly ten times faster than a concrete block of equal size —
        /// wood is about a tenth as dense. Beyond looking wrong, that turned light debris
        /// into a projectile that could damage other destructibles on landing. Clamping the
        /// impulse rather than the resulting velocity keeps this a single multiply here,
        /// instead of a correction pass over every body after each physics step.
        /// </summary>
        float ClampLaunchImpulse(float impulse, Rigidbody body, ForceMode mode)
        {
            if (Profile == null || impulse <= 0f) return impulse;
            return ClampImpulseToSpeed(impulse, body, mode,
                Profile.MaxFragmentLaunchSpeed);
        }

        float ClampWholeBodyLaunchImpulse(float impulse, Rigidbody body,
            ForceMode mode)
        {
            if (Profile == null || impulse <= 0f) return impulse;
            return ClampImpulseToSpeed(impulse, body, mode,
                Profile.MaxWholeBodyLaunchSpeed);
        }

        static float ClampImpulseToSpeed(float impulse, Rigidbody body,
            ForceMode mode, float authoredMaxSpeed)
        {
            float maxSpeed = Mathf.Max(0.01f, authoredMaxSpeed);
            // VelocityChange already expresses a speed; Impulse must be divided by the mass.
            float impliedSpeed = mode == ForceMode.VelocityChange
                ? impulse
                : impulse / Mathf.Max(0.0001f, body != null ? body.mass : 1f);
            if (impliedSpeed <= maxSpeed) return impulse;
            return impulse * (maxSpeed / impliedSpeed);
        }

        static float ForceToSpeed(float force, Rigidbody body, ForceMode mode)
        {
            if (force <= 0f) return 0f;
            return mode == ForceMode.VelocityChange
                ? force
                : force / Mathf.Max(0.0001f, body != null ? body.mass : 1f);
        }

        /// <summary>
        /// Gives a direct hit one final velocity budget instead of queuing several separately
        /// capped forces. This is used for hitscan damage on Dynamic residual clusters: a
        /// detached member already inherited the carrier velocity, and adding break,
        /// separation and directional impulses independently could exceed the authored cap
        /// before PhysX integrated any of them. Writing the bounded target immediately also
        /// lets continuous collision detection sweep the correct velocity on its first step.
        /// </summary>
        static float ApplyCappedDirectVelocity(Rigidbody body, Vector3 direction,
            float requestedSpeed, float authoredMaxSpeed)
        {
            if (body == null || direction.sqrMagnitude <= 0.000001f ||
                requestedSpeed <= 0f)
                return 0f;

            direction.Normalize();
            float maxSpeed = Mathf.Max(0.01f, authoredMaxSpeed);
#if UNITY_6000_0_OR_NEWER
            Vector3 current = body.linearVelocity;
#else
            Vector3 current = body.velocity;
#endif
            float currentForward = Mathf.Max(0f, Vector3.Dot(current, direction));
            float targetForward = Mathf.Min(maxSpeed,
                Mathf.Max(currentForward, requestedSpeed));
            Vector3 perpendicular = current - direction *
                Vector3.Dot(current, direction);
            Vector3 target = perpendicular + direction * targetForward;
            if (target.sqrMagnitude > maxSpeed * maxSpeed)
                target = target.normalized * maxSpeed;
#if UNITY_6000_0_OR_NEWER
            body.linearVelocity = target;
#else
            body.velocity = target;
#endif
            return Mathf.Max(0f, Vector3.Dot(target, direction) - currentForward);
        }

        /// <summary>
        /// Adds a weapon-style launch in the actual travel direction. Directional launch is
        /// an impulse scaled by the material: the receiving mass determines how much speed
        /// a shot adds. It is applied last and uses only the remaining
        /// forward speed budget, so light debris cannot exceed the launch-speed cap.
        /// </summary>
        float ApplyDirectionalImpactForce(Rigidbody body, Vector3 point,
            Vector3 impactDirection, float energy, float directionalForce = -1f)
        {
            return ApplyDirectionalImpactForceCore(body, point,
                impactDirection, energy, directionalForce, false);
        }

        float ApplyDirectionalImpactForceCore(Rigidbody body, Vector3 point,
            Vector3 impactDirection, float energy, float directionalForce,
            bool throughCenterOfMass)
        {
            if (body == null || Profile == null ||
                impactDirection.sqrMagnitude <= 0.000001f)
                return 0f;

            Vector3 direction = impactDirection.normalized;
            float speedChange = ForceToSpeed(ResolveDirectionalImpactImpulse(Profile,
                energy, directionalForce), body, ForceMode.Impulse);
            if (speedChange <= 0f) return 0f;
#if UNITY_6000_0_OR_NEWER
            Vector3 velocity = body.linearVelocity;
#else
            Vector3 velocity = body.velocity;
#endif
            float maxSpeed = Mathf.Max(0.01f, throughCenterOfMass
                ? Profile.MaxWholeBodyLaunchSpeed
                : Profile.MaxFragmentLaunchSpeed);
            float remainingForwardSpeed = Mathf.Max(0f,
                maxSpeed - Mathf.Max(0f, Vector3.Dot(velocity, direction)));
            speedChange = Mathf.Min(speedChange, remainingForwardSpeed);
            if (speedChange <= 0f) return 0f;

            if (throughCenterOfMass)
                body.AddForce(direction * (speedChange * body.mass),
                    ForceMode.Impulse);
            else
                body.AddForceAtPosition(direction * (speedChange * body.mass), point,
                    ForceMode.Impulse);
            return speedChange;
        }

        static float ResolveDirectionalImpactImpulse(DestructionProfile profile,
            float energy, float directionalForce)
        {
            if (directionalForce >= 0f) return directionalForce;
            return profile != null
                ? Mathf.Max(0f, energy) *
                  Mathf.Max(0f, profile.DirectionalImpactMultiplier)
                : 0f;
        }

        /// <summary>
        /// Applies the authored hit to the one Rigidbody carrying a connected Dynamic
        /// remainder. Radius decides which pieces detach; it must not prevent the surviving
        /// prop itself from receiving the weapon impulse. The cluster impulse is applied at
        /// its centre of mass: applying the same click at a remote surface point generated
        /// an unbounded torque difference, so one hit barely moved a barrel while another
        /// spun the complete remainder out of view.
        /// </summary>
        void ApplyImpactToDynamicClusterCarrier(in BreakContext context)
        {
            if (m_dynamicClusterCarrierIndex < 0 ||
                m_dynamicClusterCarrierIndex >= Fragments.Count)
                return;
            FragmentRecord carrier = Fragments[m_dynamicClusterCarrierIndex];
            if (carrier?.Body == null || carrier.Body.isKinematic) return;

            carrier.Body.WakeUp();
            ApplyDirectImpactToBody(carrier.Body, context.Point,
                context.Normal, context.ImpactDirection, context.Energy,
                context.DirectionalForce);
        }

        void ApplyDirectImpactToBody(Rigidbody body, Vector3 point,
            Vector3 surfaceNormal, Vector3 impactDirection, float energy,
            float directionalForce)
        {
            if (body == null || Profile == null) return;
            float push = Mathf.Max(0f, energy) *
                         Mathf.Max(0f, Profile.FragmentPushMultiplier);
            if (push > 0f)
            {
                Vector3 direction = ResolveImpactPushDirection(impactDirection,
                    point, body.worldCenterOfMass, surfaceNormal);
                body.AddForce(direction *
                    ClampWholeBodyLaunchImpulse(push, body, ForceMode.Impulse),
                    ForceMode.Impulse);
            }
            ApplyDirectionalImpactForceCore(body, point, impactDirection, energy,
                directionalForce, true);
        }

        /// <summary>
        /// Direct hits use the projectile travel vector for their push. The surface normal
        /// points out toward the shooter and is intentionally only a legacy fallback; using
        /// it first made click hits recoil back toward the weapon.
        /// </summary>
        internal static Vector3 ResolveImpactPushDirection(
            Vector3 impactDirection, Vector3 point, Vector3 bodyCenter,
            Vector3 surfaceNormal)
        {
            if (impactDirection.sqrMagnitude > 0.000001f)
                return impactDirection.normalized;
            Vector3 radial = bodyCenter - point;
            if (radial.sqrMagnitude > 0.000001f)
                return radial.normalized;
            if (surfaceNormal.sqrMagnitude > 0.000001f)
                return -surfaceNormal.normalized;
            return Vector3.up;
        }

        /// <summary>
        /// Shoves already-loose debris inside the hit radius. Runtime split pieces are
        /// included through the splitter, so a shot moves what it visibly hits whether or
        /// not that piece can still be cut.
        ///
        /// <paramref name="radial"/> selects the explosion form: every piece is thrown away
        /// from the origin along its own outward vector, instead of all of them following one
        /// weapon travel direction. A hitscan or projectile hit is directional; a blast is not.
        /// </summary>
        void PushLooseFragments(in DamageContext context, bool radial = false)
        {
            float energy = context.BreakEnergy >= 0f
                ? context.BreakEnergy
                : context.Amount;
            float push = energy * Mathf.Max(0f, Profile.FragmentPushMultiplier);
            float directionalImpulse = ResolveDirectionalImpactImpulse(Profile,
                energy, context.DirectionalForce);
            bool hasDirection = radial ||
                context.ImpactDirection.sqrMagnitude > 0.000001f;
            if (push <= 0f && (!hasDirection || directionalImpulse <= 0f)) return;

            float radius = Mathf.Max(0.05f, context.Radius);
            float radiusSqr = radius * radius;
            Vector3 direction = ResolveImpactPushDirection(
                context.ImpactDirection, context.Point, transform.position,
                context.Normal);

            for (int i = 0; i < Fragments.Count; i++)
            {
                if (m_skipNextLoosePushFragments.Contains(i)) continue;
                FragmentRecord fragment = Fragments[i];
                if (fragment == null || !fragment.Activated || fragment.Detached ||
                    fragment.Body == null || fragment.Body.isKinematic ||
                    fragment.Transform == null ||
                    !fragment.Transform.gameObject.activeInHierarchy)
                    continue;
                Vector3 closest = GetFragmentClosestPoint(fragment, context.Point);
                if ((closest - context.Point).sqrMagnitude > radiusSqr) continue;

                // A freshly activated shell piece beside a floor already has its downward
                // launch component removed, but debris that came loose earlier did not: the
                // very pieces that spend the most time lying on the ground were the ones a
                // hit could still drive straight through it.
                Vector3 pieceDirection = ResolveGroundSafeEjectDirection(
                    radial
                        ? ResolveOutwardBlastDirection(fragment, context.Point)
                        : direction,
                    IsFragmentNearStaticGround(fragment));
                fragment.Body.WakeUp();
                fragment.Body.AddForceAtPosition(
                    pieceDirection *
                    ClampLaunchImpulse(push, fragment.Body, ForceMode.Impulse),
                    closest, ForceMode.Impulse);
                // A blast supplies its own travel vector; a hit that carried none still gets
                // no directional launch, exactly as before.
                Vector3 travelReference = radial ||
                    context.ImpactDirection.sqrMagnitude > 0.000001f
                        ? pieceDirection
                        : Vector3.zero;
                ApplyDirectionalImpactForce(fragment.Body, closest, travelReference,
                    energy, context.DirectionalForce);
            }

            m_runtimeSplitter?.PushLoosePieces(context.Point, radius, direction, push,
                context.ImpactDirection, directionalImpulse, radial);
        }

        /// <summary>
        /// Raw outward direction from a blast origin to one loose piece, before the
        /// ground-safe correction the caller applies.
        /// </summary>
        static Vector3 ResolveOutwardBlastDirection(FragmentRecord fragment,
            Vector3 origin)
        {
            Vector3 direction = fragment.Body.worldCenterOfMass - origin;
            if (direction.sqrMagnitude < 0.000001f)
                direction = fragment.Transform.position - origin;
            if (direction.sqrMagnitude < 0.000001f)
                direction = Vector3.up;
            return direction;
        }

        internal bool TrySplitRuntimeFragmentOnGroundLanding(int fragmentIndex,
            Vector3 point, Vector3 normal, float energy)
        {
            if (Profile == null || !Profile.RuntimeFragmentSplitting ||
                fragmentIndex < 0 || fragmentIndex >= Fragments.Count ||
                Fragments[fragmentIndex]?.Transform == null ||
                !IsFragmentLoose(fragmentIndex))
                return false;

            FragmentRecord frag = Fragments[fragmentIndex];
            if (Profile.FragmentHealthFraction > 0f)
            {
                if (!frag.RuntimeHealthReady)
                {
                    frag.RuntimeHealth = Mathf.Max(10f, Profile.Health * Profile.FragmentHealthFraction);
                    frag.RuntimeHealthReady = true;
                }

                float fragMass = frag.Body != null ? Mathf.Max(frag.Body.mass, 0.1f) : 1f;
                float landingEnergyKJ = 0.0005f * fragMass * energy * energy;
                float landingDamage = landingEnergyKJ * 10f;
                if (Profile.ImpulseToDamage != null && Profile.ImpulseToDamage.length > 0)
                {
                    landingDamage = Profile.ImpulseToDamage.Evaluate(landingEnergyKJ);
                }
                landingDamage *= Mathf.Lerp(0.35f, 2.5f, Profile.Brittleness);

                frag.RuntimeHealth -= landingDamage;
                if (frag.RuntimeHealth > 0f)
                    return false;
            }

            RuntimeFragmentSplitter splitter = GetComponent<RuntimeFragmentSplitter>();
            if (splitter == null)
                splitter = gameObject.AddComponent<RuntimeFragmentSplitter>();
            if (!splitter.enabled) splitter.enabled = true;
            return splitter.ProcessGroundLanding(
                frag.Transform.gameObject,
                point, normal, energy);
        }

        internal bool TryGetFragmentEffectColor(Vector3 point, out Color color)
        {
            color = Color.white;
            float bestDistance = float.PositiveInfinity;
            bool found = false;

            RuntimeFragmentSplitter splitter = GetComponent<RuntimeFragmentSplitter>();
            if (splitter != null && splitter.TryGetEffectColor(point,
                out Color runtimeColor, out float runtimeDistance))
            {
                color = runtimeColor;
                bestDistance = runtimeDistance;
                found = true;
            }

            // Radial Glass bakes intentionally omit FragmentRuntime from every shard.
            // Scanning a malformed legacy pane with hundreds of shards only to discover
            // that none has an override caused a large avoidable spike on the break frame.
            bool hasPerFragmentEffectColors = Profile == null ||
                                              Profile.Pattern != FracturePattern.RadialGlass;
            if (hasPerFragmentEffectColors && Fragments != null)
            {
                for (int i = 0; i < Fragments.Count; i++)
                {
                    FragmentRecord record = Fragments[i];
                    if (record?.Transform == null || record.Renderer == null) continue;
                    FragmentRuntime runtime = record.Transform.GetComponent<FragmentRuntime>();
                    if (runtime == null || !runtime.HasEffectColor) continue;
                    float distance = record.Renderer.bounds.SqrDistance(point);
                    if (!record.Transform.gameObject.activeInHierarchy)
                    {
                        MeshFilter filter = record.Transform.GetComponent<MeshFilter>();
                        if (filter != null && filter.sharedMesh != null)
                        {
                            Vector3 center = record.Transform.TransformPoint(
                                filter.sharedMesh.bounds.center);
                            distance = (center - point).sqrMagnitude;
                        }
                    }
                    if (distance >= bestDistance) continue;
                    bestDistance = distance;
                    color = runtime.EffectColor;
                    found = true;
                }
            }

            // Fallback for barrels, intact objects or missing EffectColor:
            // Extract the true visual albedo color of the hit object's material/texture.
            if (!found)
            {
                MeshRenderer mr = ResolveEffectFallbackRenderer();
                if (mr != null && mr.sharedMaterial != null)
                {
                    color = ResolveMaterialColor(mr.sharedMaterial);
                    found = true;
                }
            }

            color.a = 1f;
            return found;
        }

        MeshRenderer ResolveEffectFallbackRenderer()
        {
            if (m_effectFallbackRenderer != null)
                return m_effectFallbackRenderer;

            if (IntactRoot != null)
                m_effectFallbackRenderer =
                    IntactRoot.GetComponentInChildren<MeshRenderer>(true);
            if (m_effectFallbackRenderer == null)
                m_effectFallbackRenderer = GetComponentInChildren<MeshRenderer>(true);
            return m_effectFallbackRenderer;
        }

        static readonly Dictionary<int, Color> s_materialColorCache = new Dictionary<int, Color>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetMaterialColorCache()
        {
            s_materialColorCache.Clear();
        }

        static Color ResolveMaterialColor(Material mat)
        {
            if (mat == null) return Color.white;
            int id = mat.GetHashCode();
            if (s_materialColorCache.TryGetValue(id, out Color cached)) return cached;

            Color tint = mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor")
                       : mat.HasProperty("_Color") ? mat.GetColor("_Color")
                       : mat.HasProperty("_UnlitColor") ? mat.GetColor("_UnlitColor")
                       : Color.white;

            // If authored material tint is non-white, use it directly!
            if (tint.r < 0.92f || tint.g < 0.92f || tint.b < 0.92f)
            {
                tint.a = 1f;
                s_materialColorCache[id] = tint;
                return tint;
            }

            // Material property is white; inspect texture or material name!
            Texture mainTex = mat.HasProperty("_BaseColorMap") ? mat.GetTexture("_BaseColorMap")
                            : mat.HasProperty("_BaseMap") ? mat.GetTexture("_BaseMap")
                            : mat.mainTexture;

            if (mainTex is Texture2D tex2D)
            {
                try
                {
                    if (tex2D.isReadable)
                    {
                        Color[] pixels = tex2D.GetPixels(0, 0, Mathf.Min(tex2D.width, 32), Mathf.Min(tex2D.height, 32));
                        List<Color> validPixels = new List<Color>();
                        for (int i = 0; i < pixels.Length; i++)
                        {
                            Color c = pixels[i];
                            float lum = 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
                            if (lum > 0.15f && c.a > 0.1f) // Filter out dark cracks/shadow gaps & transparent pixels
                            {
                                validPixels.Add(c);
                            }
                        }

                        if (validPixels.Count > 0)
                        {
                            // Sort by luminance to find the true MEDIAN surface color (ignoring dark gaps/shadows)
                            validPixels.Sort((a, b) =>
                            {
                                float lumA = 0.2126f * a.r + 0.7152f * a.g + 0.0722f * a.b;
                                float lumB = 0.2126f * b.r + 0.7152f * b.g + 0.0722f * b.b;
                                return lumA.CompareTo(lumB);
                            });

                            Color medianColor = validPixels[validPixels.Count / 2];
                            medianColor.a = 1f;
                            s_materialColorCache[id] = medianColor;
                            return medianColor;
                        }
                    }
                }
                catch { }
            }

            // Fallback for compressed unreadable textures: inspect material / texture name keywords
            string name = (mat.name + (mainTex != null ? mainTex.name : "")).ToLowerInvariant();
            Color nameColor = new Color(0.65f, 0.65f, 0.65f);
            if (name.Contains("wood") || name.Contains("barrel") || name.Contains("plank") || name.Contains("timber") || name.Contains("crate"))
                nameColor = new Color(0.54f, 0.45f, 0.36f); // Natural neutral timber wood tone
            else if (name.Contains("roof") || name.Contains("tile") || name.Contains("brick") || name.Contains("red"))
                nameColor = new Color(0.68f, 0.30f, 0.20f); // Warm roof tile red
            else if (name.Contains("stone") || name.Contains("rock") || name.Contains("wall") || name.Contains("tower") || name.Contains("concrete"))
                nameColor = new Color(0.58f, 0.58f, 0.58f); // Bright stone grey
            else if (name.Contains("grass") || name.Contains("leaf") || name.Contains("moss") || name.Contains("tree"))
                nameColor = new Color(0.35f, 0.65f, 0.35f);
            else if (name.Contains("gold") || name.Contains("yellow"))
                nameColor = new Color(0.9f, 0.75f, 0.25f);

            s_materialColorCache[id] = nameColor;
            return nameColor;
        }

        internal void ReportPrimaryFragmentCollision(int index, Collision collision)
        {
            if (Profile == null || m_state < DestructibleState.Fractured ||
                collision == null)
                return;
            bool isLoose = IsFragmentLoose(index);
            bool isClustered = IsDynamicClusterMember(index);
            // The intact Dynamic body has already paid for this landing. Its connected
            // residual cluster must settle as one object; treating every member/ground
            // contact as a fresh destructive hit causes an O(n) cascade of detachments,
            // runtime splits and effects in the same landing frame.
            if (ShouldIgnoreDynamicClusterContact(isClustered,
                collision.rigidbody))
                return;
            if (isClustered && collision.contactCount > 0)
            {
                int contactedIndex = FindClosestDynamicClusterMember(
                    collision.GetContact(0).point, out _);
                if (contactedIndex >= 0)
                    index = contactedIndex;
            }
            isLoose = IsFragmentLoose(index);
            isClustered = IsDynamicClusterMember(index);
            if (index >= 0 && index < Fragments.Count && Fragments[index]?.Transform != null)
            {
                FragmentRecord frag = Fragments[index];
                float maxFragHealth = Profile.Health * Profile.FragmentHealthFraction;
                if (maxFragHealth > 0f)
                {
                    if (!frag.RuntimeHealthReady)
                    {
                        frag.RuntimeHealth = maxFragHealth;
                        frag.RuntimeHealthReady = true;
                    }

                    float rawDamage = CollisionImpactDamage(collision, out _, frag.Body);
                    bool ownDebris = IsOwnDebrisBody(collision.rigidbody,
                        this, out bool debris);

                    // A piece shed by this destructible is already represented by the
                    // structural release that made it loose. Letting that same piece damage
                    // a still-kinematic sibling turns one authored local hit into a collision
                    // chain: A wakes B, B wakes C, and dense Concrete/Brick bakes can activate
                    // the complete building in one frame. Foreign debris still reaches the
                    // normal significance threshold below and can damage this object.
                    if (ownDebris) return;

                    float minDebrisDamage = Profile.Health * Mathf.Max(0.05f, Profile.DebrisImpactSignificance);
                    if (!debris || rawDamage >= minDebrisDamage)
                    {
                        // Visual feedback belongs to the physical hit, even when this
                        // fragment still has enough runtime health to absorb it. It carries
                        // its own debounce: the damage cooldown below cannot serve, because it
                        // only advances when damage actually lands, so a surviving fragment
                        // would leave it untouched and report every single contact. A piece
                        // wedged against a floor jitters at well above Min Collision Velocity
                        // for as long as it takes to settle, and that became dozens of
                        // particles and sounds stacked in one spot.
                        float effectCooldown = Mathf.Max(0.05f, Profile.DamageCooldown);
                        if (Profile.CollisionDamage &&
                            ((1 << collision.gameObject.layer) &
                             Profile.CollisionLayers.value) != 0 &&
                            collision.relativeVelocity.magnitude >=
                            Profile.MinCollisionVelocity &&
                            rawDamage > 0f &&
                            Time.time - frag.LastImpactEffectTime >= effectCooldown)
                        {
                            frag.LastImpactEffectTime = Time.time;
                            ReportPrimaryFragmentHit(collision, rawDamage,
                                frag.Transform);
                        }

                        int sourceId = collision.gameObject.GetHashCode();
                        DestructionImpactSource fragmentImpactSource =
                            FindImpactSource(collision.gameObject);
                        float fragmentDirectionalForce = fragmentImpactSource != null
                            ? fragmentImpactSource.DirectionalForce : -1f;
                        float cooldown = Mathf.Max(0.05f, Profile.DamageCooldown); // Fragments always have at least a tiny debounce
                        if (Time.time - frag.LastDamageTime < cooldown || (Time.time == frag.LastDamageTime && sourceId == frag.LastDamageSourceId))
                            return;

                        frag.LastDamageTime = Time.time;
                        frag.LastDamageSourceId = sourceId;
                        frag.RuntimeHealth -= rawDamage;
                        if (frag.RuntimeHealth > 0f)
                            return; // Fragment ABSORBS impact and SURVIVES INTACT (whether kinematic or loose)!

                        if (isClustered && DetachDynamicClusterFragment(index))
                        {
                            isClustered = false;
                            isLoose = true;
                            Vector3 ejectPoint = frag.Transform.position;
                            Vector3 ejectNormal = Vector3.up;
                            if (collision.contactCount > 0)
                            {
                                ContactPoint contact = collision.GetContact(0);
                                ejectPoint = contact.point;
                                ejectNormal = ResolveCollisionEjectDirection(
                                    contact.normal, GetIncomingBodyVelocity(collision),
                                    ejectPoint - transform.position);
                            }
                            if (frag.Body != null)
                            {
                                frag.Body.WakeUp();
                                float push = rawDamage * Mathf.Max(0f,
                                    Profile.FragmentPushMultiplier);
                                if (push > 0f)
                                    frag.Body.AddForceAtPosition(ejectNormal *
                                        ClampLaunchImpulse(push, frag.Body,
                                            ForceMode.Impulse), ejectPoint,
                                        ForceMode.Impulse);
                                ApplyDirectionalImpactForce(frag.Body, ejectPoint,
                                    GetIncomingImpactDirection(collision), rawDamage,
                                    fragmentDirectionalForce);
                            }
                        }

                        if (!isLoose)
                        {
                            Vector3 exactPoint = frag.Transform.position;
                            Vector3 exactNormal = Vector3.up;
                            if (collision.contactCount > 0)
                            {
                                ContactPoint exactContact = collision.GetContact(0);
                                exactPoint = exactContact.point;
                                exactNormal = exactContact.normal;
                            }
                            exactNormal = ResolveCollisionEjectDirection(
                                exactNormal, GetIncomingBodyVelocity(collision),
                                exactPoint - transform.position);
                            ActivateExactFragmentFromCollision(index, new BreakContext(
                                exactPoint, exactNormal,
                                ResolveDirectImpactRadius(Profile,
                                    rawDamage, fragmentImpactSource),
                                collision.impulse.magnitude,
                                collision.gameObject.GetHashCode(),
                                GetIncomingImpactDirection(collision),
                                fragmentDirectionalForce), collision);
                            return;
                        }
                    }
                    else
                    {
                        return;
                    }
                }

                if (isLoose)
                {
                    float looseDamage = CollisionImpactDamage(collision, out _, frag.Body);
                    if (Profile.CollisionDamage &&
                        ((1 << collision.gameObject.layer) &
                         Profile.CollisionLayers.value) != 0 &&
                        collision.relativeVelocity.magnitude >=
                        Profile.MinCollisionVelocity &&
                        looseDamage > 0f)
                    {
                        ReportPrimaryFragmentHit(collision, looseDamage,
                            frag.Transform);
                    }

                    if (Profile.RuntimeFragmentSplitting)
                    {
                        EnsureRuntimeSplitter().ProcessCollision(
                            Fragments[index].Transform.gameObject, collision);
                    }
                    else if (Profile.SecondaryBreakup != SecondaryBreakupLevel.Off && frag.SecondaryNode != null)
                    {
                        Vector3 hitPoint = collision.contactCount > 0 ? collision.GetContact(0).point : frag.Transform.position;
                        Vector3 hitNormal = collision.contactCount > 0 ? collision.GetContact(0).normal : Vector3.up;
                        frag.SecondaryNode.ApplyImpactDamage(CollisionImpactDamage(collision, out _), hitPoint, hitNormal);
                    }
                    return;
                }
            }
            if (!Profile.CollisionDamage) return;
            if (((1 << collision.gameObject.layer) & Profile.CollisionLayers.value) == 0)
                return;

            float velocity = collision.relativeVelocity.magnitude;
            if (velocity < Profile.MinCollisionVelocity) return;

            // This shares the intact receiver's physics deliberately. It used to carry its
            // own copy of the old impulse-based formula, which quietly kept feeding raw
            // impulse into a curve whose axis is now kilojoules — and since the wood preset
            // dropped its last key from 50 to 5 for that calibration, this path became about
            // ten times more violent instead of gentler. One plank clipping another read as
            // an enormous blow. There is one damage model now, and one place to change it.
            float damage = CollisionImpactDamage(collision, out _);
            if (damage <= 0f) return;

            // Loose debris knocking into the shell is held to the same significance floor as
            // debris arriving from another destructible; without it, tumbling rubble unpicks
            // the structure it came from one contact at a time.
            //
            // Debris means "a piece shed by a destructible" — NOT merely "a dynamic
            // Rigidbody". A fired projectile is dynamic too, and judging by that alone
            // silenced every shot the moment an object fractured: before the break the
            // bullet hit the intact receiver and worked, afterwards it landed on a fragment
            // and was dismissed as rubble, because a bullet is never worth a third of a
            // wall's strength. These two components are what actually mark a piece as
            // debris, and reading them off the colliding body costs one lookup.
            if (IsOwnDebrisBody(collision.rigidbody, this,
                    out bool debrisSource))
                return;

            if (debrisSource &&
                damage < Profile.Health * Mathf.Max(0f, Profile.DebrisImpactSignificance))
                return;

            Vector3 point = index >= 0 && index < Fragments.Count &&
                            Fragments[index]?.Transform != null
                ? Fragments[index].Transform.position
                : transform.position;
            Vector3 normal = Vector3.up;
            if (collision.contactCount > 0)
            {
                ContactPoint contact = collision.GetContact(0);
                point = contact.point;
                normal = ResolveCollisionEjectDirection(
                    contact.normal, GetIncomingBodyVelocity(collision),
                    point - transform.position);
            }

            ReportPrimaryFragmentHit(collision, damage,
                index >= 0 && index < Fragments.Count
                    ? Fragments[index]?.Transform
                    : transform);

            DestructionImpactSource impactSource =
                FindImpactSource(collision.gameObject);
            float sourceDirectionalForce = impactSource != null
                ? impactSource.DirectionalForce : -1f;
            float directRadius = ResolveDirectImpactRadius(Profile,
                damage, impactSource);

            if (index >= 0 && index < Fragments.Count && !isLoose)
            {
                // Fragment health can be disabled (0), in which case the first valid
                // collision still removes only the collider that actually reported it.
                ActivateExactFragmentFromCollision(index, new BreakContext(
                    point, normal,
                    directRadius,
                    collision.impulse.magnitude,
                    collision.gameObject.GetHashCode(),
                    GetIncomingImpactDirection(collision),
                    sourceDirectionalForce), collision);
                return;
            }

            // The intact Health cooldown must not gate progressive destruction.
            // Every distinct external collision that reaches a baked fragment may
            // wake the next local part of the shell until no pieces remain.
            ActivateRemaining(new BreakContext(
                point, normal,
                directRadius,
                collision.impulse.magnitude, collision.gameObject.GetHashCode(),
                GetIncomingImpactDirection(collision), sourceDirectionalForce));
        }

        void ReportPrimaryFragmentHit(Collision collision, float damage,
            Transform fallback)
        {
            Vector3 point = fallback != null ? fallback.position : transform.position;
            Vector3 normal = Vector3.up;
            if (collision.contactCount > 0)
            {
                ContactPoint contact = collision.GetContact(0);
                point = contact.point;
                normal = ResolveCollisionEjectDirection(
                    contact.normal, GetIncomingBodyVelocity(collision),
                    point - transform.position);
            }

            DestructionImpactSource impactSource =
                FindImpactSource(collision.gameObject);
            var context = new DamageContext(
                Mathf.Max(0f, damage), point, normal,
                ResolveDirectImpactRadius(Profile, damage, impactSource),
                0, collision.gameObject.GetHashCode(),
                collision.impulse.magnitude,
                GetIncomingImpactDirection(collision),
                impactSource != null ? impactSource.DirectionalForce : -1f);
            TrySpreadImpact(context);
            HitReceived?.Invoke(context);
        }

        /// <summary>
        /// The one collision damage model, shared by the intact receiver and by hits that
        /// land straight on a baked fragment. Damage follows the kinetic energy of the
        /// impact, because energy is what breaks material: impulse alone cannot tell a heavy
        /// slow mass from a light fast one. The reduced mass is the part of the system that
        /// can convert its motion into damage — against a fixed wall that is simply the
        /// projectile's mass.
        /// </summary>
        internal float CollisionImpactDamage(Collision collision, out float energyKilojoules, Rigidbody ownBodyOverride = null)
        {
            energyKilojoules = 0f;
            if (Profile == null) return 0f;

            float velocity = collision != null ? collision.relativeVelocity.magnitude : 0f;
            Rigidbody ownBody = ownBodyOverride != null ? ownBodyOverride : IntactBody;
            float ownMass = ownBody != null ? Mathf.Max(ownBody.mass, 10f) : 100f;
            Rigidbody hitBody = collision != null ? collision.rigidbody : null;
            float otherMass = hitBody != null ? Mathf.Max(hitBody.mass, 0.1f) : 0f;
            return CalculateImpactDamage(Profile, ownMass, otherMass, velocity,
                hitBody == null, out energyKilojoules);
        }

        /// <summary>
        /// Converts collision speed and mass into material damage. A fixed floor or wall
        /// can absorb the falling body's full kinetic energy; two dynamic bodies use their
        /// reduced mass because only their relative motion is available to deform them.
        /// Kept deterministic so landing thresholds can be regression-tested without
        /// manufacturing a native Unity Collision object.
        /// </summary>
        internal static float CalculateImpactDamage(DestructionProfile profile,
            float ownMass, float otherMass, float velocity, bool otherIsStatic,
            out float energyKilojoules)
        {
            energyKilojoules = 0f;
            if (profile == null) return 0f;

            ownMass = Mathf.Max(ownMass, 0.1f);
            otherMass = Mathf.Max(otherMass, 0.1f);
            velocity = Mathf.Max(0f, velocity);
            float effectiveMass = otherIsStatic
                ? ownMass
                : (ownMass * otherMass) / (ownMass + otherMass);
            energyKilojoules = 0.0005f * effectiveMass * velocity * velocity;

            float damage = energyKilojoules * 10f;
            if (profile.ImpulseToDamage != null && profile.ImpulseToDamage.length > 0)
            {
                Keyframe[] keys = profile.ImpulseToDamage.keys;
                float maxTime = keys[keys.Length - 1].time;
                float maxVal = keys[keys.Length - 1].value;
                damage = energyKilojoules > maxTime && maxTime > 0.01f
                    ? maxVal * Mathf.Sqrt(energyKilojoules / maxTime)
                    : profile.ImpulseToDamage.Evaluate(energyKilojoules);
            }
            damage *= Mathf.Lerp(0.35f, 2.5f, profile.Brittleness);
            return profile.AcceptsHitDamage(damage) ? damage : 0f;
        }

        /// <summary>
        /// Calculates damage for an impact forwarded by the moving body's collision
        /// callback. In that callback Unity's collision.rigidbody is the struck target,
        /// not the source that carried the energy. Passing the source mass explicitly
        /// prevents a light chip from inheriting a wall's or barrel's mass and dealing
        /// heavy-object damage.
        /// </summary>
        internal static float CalculateIncomingBodyImpactDamage(
            DestructionProfile targetProfile, float sourceMass, float targetMass,
            float velocity, bool targetIsStatic, out float energyKilojoules)
        {
            return CalculateImpactDamage(targetProfile, sourceMass, targetMass,
                velocity, targetIsStatic, out energyKilojoules);
        }

        /// <summary>
        /// Damage caused by the intact body's own landing on static world geometry.
        /// Self-fall severity follows impact speed (and therefore equivalent fall height)
        /// instead of baked mesh mass, because profile Health is also size-independent.
        /// Projectile, vehicle and debris impacts still use CalculateImpactDamage with
        /// their physical masses.
        /// </summary>
        internal static float CalculateSelfFallDamage(DestructionProfile profile,
            float velocity, out float energyKilojoules)
        {
            energyKilojoules = 0f;
            if (profile == null || velocity < Mathf.Max(
                    MinimumSelfFallVelocity,
                    Mathf.Max(0f, profile.MinCollisionVelocity)))
                return 0f;

            return CalculateImpactDamage(profile, SelfFallReferenceMass, 0f,
                velocity, true, out energyKilojoules);
        }

        /// <summary>
        /// Radius multiplier for a blow that carried more than the material could absorb.
        /// Destroyed volume grows with the energy spent past the breaking point and a radius
        /// with the cube root of a volume, so the exponent is physical rather than chosen.
        /// A blow that did not overshoot returns 1 and leaves the authored footprint exactly
        /// as it was. Nothing caps the top end: fragment selection saturates on its own once
        /// the radius covers the object, which is what "it came apart completely" looks like.
        /// </summary>
        internal static float OverkillRadiusScale(float damage, float health)
        {
            float overkill = Mathf.Max(0f, damage) / Mathf.Max(0.0001f, health);
            return overkill <= 1f ? 1f : Mathf.Pow(overkill, 1f / 3f);
        }

        /// <summary>
        /// Gameplay footprint for a direct Rigidbody contact: the authored radius widened by
        /// how far the blow overshot this object's own strength.
        ///
        /// Absolute kinetic energy was tried here and rejected, because it is not comparable
        /// between materials: it let a heavy ball wake most of a structure while a click
        /// removed a single piece. Damage measured against Health is dimensionless and says
        /// what actually matters — a blow that only just breaks through still opens exactly
        /// the authored hole, while one worth many times the whole object takes most of it
        /// along. That is what makes both drop height and the authored Health visible in how
        /// much of a falling prop survives, instead of every landing removing one patch.
        /// </summary>
        static float DirectImpactSelectionRadius(DestructionProfile profile, float damage)
        {
            float authored = Mathf.Max(0.05f,
                profile != null ? profile.PostFractureImpactRadius : 0.05f);
            return authored * OverkillRadiusScale(damage,
                profile != null ? profile.Health : 0f);
        }

        static bool ShouldClusterDynamicResidual(DestructionProfile profile,
            bool ownBodyHitStaticWorld, float damage, float currentHealth) =>
            profile != null && profile.IntactBody == IntactBodyMode.Dynamic &&
            ownBodyHitStaticWorld && currentHealth > 0f &&
            damage >= currentHealth;

        public bool CanSecondaryBreak(int parentIndex)
        {
            if (Profile == null ||
                Profile.SecondaryBreakup == SecondaryBreakupLevel.Off ||
                m_state != DestructibleState.Fractured ||
                parentIndex < 0 || parentIndex >= Fragments.Count)
                return false;

            FragmentRecord parent = Fragments[parentIndex];
            return parent != null && parent.Activated && !parent.Settled &&
                   parent.Body != null;
        }

        internal int GetSecondaryActivationAllowance(int desired)
        {
            if (Profile == null) return 0;
            if (!UsesActiveBodyBudget) return Mathf.Max(0, desired);
            return Mathf.Clamp(
                Profile.MaxActiveSecondaryBodies - m_secondaryActiveCount,
                0, Mathf.Max(0, desired));
        }

        internal void NotifyPrimaryReplaced(
            int parentIndex, bool budgetAlreadyReplaced)
        {
            if (parentIndex < 0 || parentIndex >= Fragments.Count) return;
            FragmentRecord parent = Fragments[parentIndex];
            if (parent == null || !parent.Activated || parent.Settled) return;

            bool wasClusterMember = IsDynamicClusterMember(parentIndex);
            bool occupiedActivationSlot = !wasClusterMember ||
                                          parentIndex == m_dynamicClusterCarrierIndex;
            if (wasClusterMember)
                RemoveReplacedDynamicClusterFragment(parentIndex);

            parent.Activated = false;
            parent.Settled = true;
            if (occupiedActivationSlot)
                m_activeCount = Mathf.Max(0, m_activeCount - 1);
            if (occupiedActivationSlot && UsesActiveBodyBudget &&
                !budgetAlreadyReplaced)
            {
                DestructionBudgetManager.Instance.ReleaseActivation(
                    Profile.BudgetGroup, 1);
            }
        }

        internal void NotifySecondaryBreak(int parentIndex, int activatedChildren)
        {
            // Zero-children notifications mean the parent was replaced by visual-only
            // children (or nothing at all); that is not a settled fragment, so the
            // settled counter must not move.
            if (activatedChildren > 0)
                m_secondaryActiveCount += activatedChildren;
            SecondaryBroken?.Invoke(parentIndex, activatedChildren);
            OnSecondaryBreakEvent?.Invoke();
        }

        internal void NotifySecondarySettled(int count, CleanupAction? actualAction = null)
        {
            count = Mathf.Max(0, count);
            m_secondaryActiveCount = Mathf.Max(0, m_secondaryActiveCount - count);
            m_secondarySettledCount += count;
            if (UsesActiveBodyBudget && (actualAction ?? Profile.Cleanup) == CleanupAction.None)
            {
                // Same rule as SettleFragment: simulating bodies keep their budget slots.
                m_outstandingBudgetCount += count;
                return;
            }
            if (UsesActiveBodyBudget)
                DestructionBudgetManager.Instance.ReleaseActivation(
                    Profile.BudgetGroup, count);
        }

        // ------------------------------------------------------------------ collision damage source

        // When the intact colliders live on a child of this Rigidbody, Unity treats them
        // as a compound collider and dispatches OnCollisionEnter to the Rigidbody object.
        // Keeping this receiver on DestructibleRoot also repairs prefabs baked before the
        // relay host was corrected. Static roots still use DestructibleCollisionRelay on
        // their collider child because they have no Rigidbody here.
        void OnCollisionEnter(Collision collision)
        {
            bool routedToDeferredFragment = false;
            if (collision != null && collision.contactCount > 0)
            {
                int deferredFragment = ResolveDeferredFragmentCollisionIndex(
                    collision.GetContact(0).thisCollider);
                if (deferredFragment >= 0)
                {
                    ReportPrimaryFragmentCollision(deferredFragment, collision);
                    routedToDeferredFragment = true;
                }
            }

            if (!routedToDeferredFragment && IntactBody != null)
                ReportIntactCollision(collision);

            // A destructible that is itself in motion — a structurally released tower
            // toppling over, a Dynamic body thrown by gameplay — is a heavy moving object
            // like any other, and striking a DIFFERENT destructible has to damage it. Only
            // fragments and runtime pieces forwarded their impacts, so the one case that
            // carries the most energy of all reported nothing: the receiver above tells
            // this object it was hit, never the thing it hit.
            if (IntactBody == null || IntactBody.isKinematic ||
                collision.collider == null ||
                collision.relativeVelocity.sqrMagnitude <= 1f)
                return;

            var struckRoot = collision.collider.GetComponentInParent<DestructibleRoot>();
            if (struckRoot != null && struckRoot != this)
                struckRoot.ReportExternalDebrisImpact(collision, gameObject, IntactBody);
        }

        int ResolveCollisionStructuralGroup(Collision collision)
        {
            if (collision == null || collision.contactCount <= 0) return -1;
            return ResolveStructuralGroup(collision.GetContact(0).thisCollider);
        }

        internal void ReportIntactCollision(Collision collision) =>
            ReportCollisionDamage(collision, collision.gameObject, fromDebris: false,
                targetStructuralGroup: ResolveCollisionStructuralGroup(collision));

        internal void ReportIntactCollision(Collision collision, GameObject damageSource) =>
            ReportCollisionDamage(collision, damageSource, fromDebris: false,
                targetStructuralGroup: ResolveCollisionStructuralGroup(collision));


        /// <summary>
        /// Impact forwarded from a loose fragment of ANOTHER destructible. Unlike the
        /// intact receiver, it also acts after this object has fractured: a fractured glass
        /// pane stands as a kinematic shell that looks identical to an intact one, and
        /// debris crashing into it must wake that shell exactly like a projectile would.
        /// ApplyDamage already routes post-fracture requests into local shell activation
        /// and debris pushes, so the same damage math serves both states.
        /// </summary>
        internal void ReportExternalDebrisImpact(
            Collision collision, GameObject damageSource, Rigidbody sourceBody)
        {
            DestructionProfile sourceProfile = null;
            if (damageSource != null &&
                damageSource.TryGetComponent(out FragmentRuntime fragment))
                sourceProfile = fragment.Root != null ? fragment.Root.Profile : null;
            else if (damageSource != null &&
                     damageSource.TryGetComponent(out RuntimeSplitPiece splitPiece))
                sourceProfile = splitPiece.Root != null ? splitPiece.Root.Profile : null;

            if (!CanDebrisDamageTarget(sourceProfile, Profile)) return;
            ReportCollisionDamage(collision, damageSource, fromDebris: true,
                forwardedSourceBody: sourceBody,
                targetStructuralGroup: ResolveCollisionStructuralGroup(collision));
        }

        static bool CanDebrisDamageTarget(DestructionProfile source,
            DestructionProfile target)
        {
            if (source == null || target == null) return true;
            // A loose wooden chip keeps its physical collision response but is not a
            // demolition projectile. It may shatter fragile glass; it must not chain-break
            // wood, masonry, brick, slabs or pillars even at a comical launch speed.
            return source.Pattern != FracturePattern.DirectionalWood ||
                   target.Pattern == FracturePattern.RadialGlass;
        }

        static DestructionImpactSource FindImpactSource(GameObject source) =>
            source != null
                ? source.GetComponentInParent<DestructionImpactSource>()
                : null;

        /// <summary>
        /// Classifies a dynamic collision body once for both fragment-damage paths. Returns
        /// true only when the body is debris owned by <paramref name="owner"/>; the out value
        /// also identifies debris from another destructible so its significance threshold is
        /// still applied normally.
        /// </summary>
        internal static bool IsOwnDebrisBody(Rigidbody body,
            DestructibleRoot owner, out bool debris)
        {
            debris = false;
            if (body == null || body.isKinematic) return false;

            if (body.TryGetComponent(out FragmentRuntime fragment))
            {
                debris = true;
                return fragment.Root == owner;
            }
            if (body.TryGetComponent(out RuntimeSplitPiece split))
            {
                debris = true;
                return split.Root == owner;
            }
            return false;
        }

        static float ResolveDirectImpactRadius(DestructionProfile profile,
            float damage, DestructionImpactSource source)
        {
            if (source != null && source.OverrideImpactRadius)
                return Mathf.Max(0.01f, source.ImpactRadius);
            return DirectImpactSelectionRadius(profile, damage);
        }

        static bool ShouldIgnoreDynamicClusterContact(bool isClustered,
            Rigidbody otherBody) =>
            isClustered && (otherBody == null || otherBody.isKinematic);

        /// <summary>
        /// Rigidbody impact damage. The damage source is passed separately because a
        /// forwarded report (a loose fragment of another destructible striking this one)
        /// arrives with collision.gameObject pointing at THIS object, and the layer filter
        /// must always judge the thing that hit, not the thing that was hit.
        /// </summary>
        void ReportCollisionDamage(Collision collision, GameObject damageSource,
            bool fromDebris, Rigidbody forwardedSourceBody = null,
            int targetStructuralGroup = -1)
        {
            if (Profile == null || !Profile.CollisionDamage) return;
            // Only debris keeps acting after this object has fractured: its kinematic shell
            // still looks and behaves like an intact wall and must react to being hit.
            if (!fromDebris && m_state >= DestructibleState.Fractured) return;
            if (damageSource == null) damageSource = collision.gameObject;
            if (((1 << damageSource.layer) & Profile.CollisionLayers.value) == 0) return;

            // A released/dynamic intact body reports a fixed floor or wall with no
            // other Rigidbody. Keep projectile and moving-body impacts enabled when
            // Fall Damage is off; this controls only the body's own static-world hit.
            bool ownBodyHitStaticWorld = !fromDebris && IntactBody != null &&
                                         !IntactBody.isKinematic &&
                                         collision.rigidbody == null;
            if (ownBodyHitStaticWorld && !Profile.FallDamage) return;

            float vel = collision.relativeVelocity.magnitude;
            if (vel < Profile.MinCollisionVelocity) return;

            Rigidbody hitBody = fromDebris && forwardedSourceBody != null
                ? forwardedSourceBody
                : collision.rigidbody;
            float damage = ownBodyHitStaticWorld
                ? CalculateSelfFallDamage(Profile, vel, out _)
                : fromDebris && forwardedSourceBody != null
                    ? CalculateIncomingBodyImpactDamage(Profile,
                        forwardedSourceBody.mass,
                        IntactBody != null ? IntactBody.mass : 100f,
                        vel,
                        IntactBody == null || IntactBody.isKinematic,
                        out _)
                : CollisionImpactDamage(collision, out _);
            if (damage <= 0f) return;

            // A tumbling plank is a bump, not an attack. Physics still shoves whatever it
            // lands on through the ordinary collision response; what it must not do is
            // count as damage and wake the structure around it. The floor is a share of the
            // target's own strength, so it scales with the material: wood arriving at wood
            // needs a genuinely violent speed to matter, while a stone landing on glass is
            // worth many times the pane's whole strength and still shatters it instantly.
            if (fromDebris &&
                damage < Profile.Health * Mathf.Max(0f, Profile.DebrisImpactSignificance))
                return;

            // Punch-through. A blow carrying more than the material could absorb keeps
            // going instead of bouncing, slowed by the energy it actually spent breaking
            // through. Kinetic energy is quadratic in speed, so the surviving speed is
            // sqrt(1 - spent share). The previous form derived this from Health treated as
            // a mass in kilograms, which is not a physical quantity and made heavy hits
            // behave inconsistently with light ones.
            //
            // Only a simulating body can be slowed. Forwarded impacts carry their actual
            // source Rigidbody explicitly because collision.rigidbody points at the struck
            // target when the callback originates on the flying fragment.
            if (hitBody != null && !hitBody.isKinematic && m_health > 0f && damage >= m_health)
            {
                Vector3 travel = collision.relativeVelocity;
                if (travel.sqrMagnitude > 0.1f)
                {
                    Vector3 contactPoint = collision.contactCount > 0
                        ? collision.GetContact(0).point : transform.position;
                    // relativeVelocity carries the impact speed but its sign depends on
                    // which collider reports it; orient it along the approach direction.
                    if (Vector3.Dot(travel, contactPoint - hitBody.position) < 0f)
                        travel = -travel;

                    float spentShare = Mathf.Clamp01(m_health / damage);
                    float retention = Mathf.Sqrt(Mathf.Max(0f, 1f - spentShare));
#if UNITY_6000_0_OR_NEWER
                    hitBody.linearVelocity = travel * retention;
#else
                    hitBody.velocity = travel * retention;
#endif
                    hitBody.angularVelocity *= retention;
                }
            }

            Vector3 point = transform.position;
            Vector3 normal = Vector3.up;
            if (collision.contactCount > 0)
            {
                var contact = collision.GetContact(0);
                point = contact.point;
                normal = ResolveCollisionEjectDirection(
                    contact.normal, GetIncomingBodyVelocity(collision),
                    point - transform.position);
            }

            // A Rigidbody impact is local. Its energy controls damage and physical impulse,
            // while the authored local radius controls fragment selection. Explosion radius
            // belongs to explicit radial damage; scaling this radius by projectile energy
            // made a heavy ball wake most of a structure while LMB removed one fragment.
            DestructionImpactSource impactSource =
                FindImpactSource(damageSource);
            float activationRadius = ResolveDirectImpactRadius(
                Profile, damage, impactSource);
            float directionalForce = impactSource != null
                ? impactSource.DirectionalForce : -1f;
            var context = new DamageContext(
                damage, point, normal, activationRadius, 0,
                damageSource.GetHashCode(), collision.impulse.magnitude,
                GetIncomingImpactDirection(collision), directionalForce,
                targetStructuralGroup);

            // Reused buffer: GetComponentsInChildren allocates a fresh array on every
            // reported collision, and a busy scene reports a great many of them.
            if (s_sharedPhysics.ImpactColliderBuffer == null)
                s_sharedPhysics.ImpactColliderBuffer = new List<Collider>(8);
            damageSource.GetComponentsInChildren(s_sharedPhysics.ImpactColliderBuffer);
            s_sharedPhysics.CurrentImpactColliders = s_sharedPhysics.ImpactColliderBuffer;
            m_clusterDynamicResidualOnNextBreak =
                ShouldClusterDynamicResidual(Profile, ownBodyHitStaticWorld,
                    damage, m_health);
            m_applyingCollisionDamage = true;
            try
            {
                ApplyDamage(context);
            }
            finally
            {
                m_clusterDynamicResidualOnNextBreak = false;
                m_applyingCollisionDamage = false;
                s_sharedPhysics.CurrentImpactColliders = null;
            }
        }

        static Vector3 GetIncomingBodyVelocity(Collision collision)
        {
            Rigidbody incoming = collision != null ? collision.rigidbody : null;
            if (incoming == null || incoming.isKinematic) return Vector3.zero;
#if UNITY_6000_0_OR_NEWER
            return incoming.linearVelocity;
#else
            return incoming.velocity;
#endif
        }

        static Vector3 GetIncomingImpactDirection(Collision collision)
        {
            Vector3 velocity = GetIncomingBodyVelocity(collision);
            return velocity.sqrMagnitude > 0.000001f
                ? velocity.normalized : Vector3.zero;
        }

        /// <summary>
        /// Converts a physics contact normal to Quick Destruction's outward-surface
        /// convention. Unity can report the same contact from either collider's side;
        /// the outward direction must oppose the incoming projectile velocity so a newly
        /// activated baked piece is ejected from the hole rather than into the shell.
        /// </summary>
        internal static Vector3 ResolveCollisionEjectDirection(
            Vector3 contactNormal, Vector3 incomingVelocity,
            Vector3 radialFallback)
        {
            Vector3 direction = contactNormal;
            if (direction.sqrMagnitude < 0.000001f &&
                incomingVelocity.sqrMagnitude > 0.000001f)
                direction = -incomingVelocity;
            if (direction.sqrMagnitude < 0.000001f)
                direction = radialFallback;
            if (direction.sqrMagnitude < 0.000001f)
                return Vector3.up;

            direction.Normalize();
            if (incomingVelocity.sqrMagnitude > 0.000001f &&
                Vector3.Dot(direction, incomingVelocity) > 0f)
                direction = -direction;
            return direction;
        }

        /// <summary>
        /// Keeps local debris from being deliberately launched through a floor while
        /// preserving the original direction everywhere else. This is deterministic and
        /// separated from the physics probe so its behavior can be regression-tested.
        /// </summary>
        internal static Vector3 ResolveGroundSafeEjectDirection(
            Vector3 desiredDirection, bool nearStaticGround)
        {
            Vector3 direction = desiredDirection;
            if (direction.sqrMagnitude < 0.000001f)
                direction = Vector3.up;
            else
                direction.Normalize();
            if (!nearStaticGround || direction.y >= 0.15f)
                return direction;
            direction.y = 0.15f;
            return direction.normalized;
        }

        bool IsFragmentNearStaticGround(FragmentRecord fragment)
        {
            if (fragment == null || fragment.Transform == null) return false;
            Bounds bounds = GetStructuralBounds(fragment);
            if (bounds.size.sqrMagnitude <= 0.000001f) return false;

            float lift = 0.02f;
            float clearance = Mathf.Clamp(bounds.extents.y * 0.15f,
                0.025f, 0.1f);
            Vector3 origin = bounds.center + Vector3.up * lift;
            float distance = bounds.extents.y + lift + clearance;
            int hits = Physics.RaycastNonAlloc(origin, Vector3.down,
                m_dynamicSupportHits, distance,
                Profile != null ? Profile.SupportLayers : Physics.AllLayers,
                QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hits; i++)
            {
                Collider hit = m_dynamicSupportHits[i].collider;
                if (hit == null || !hit.enabled ||
                    !hit.gameObject.activeInHierarchy ||
                    hit.transform == transform || hit.transform.IsChildOf(transform))
                    continue;
                Rigidbody supportBody = hit.attachedRigidbody;
                if (supportBody != null && !supportBody.isKinematic)
                    continue;
                return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ structural body

        /// <summary>
        /// Collapses only one disconnected source element. The intact visual is exchanged
        /// for the baked shell on the first call, but fragments outside this structural
        /// group remain kinematic and visually preserve the rest of the construction.
        /// </summary>
        internal bool ReleaseStructuralGroup(int structuralGroup, Vector3 point)
        {
            if (Profile == null || structuralGroup < 0 ||
                Profile.IntactBody != IntactBodyMode.ReleaseWhenUnsupported)
                return false;

            var context = new BreakContext(point, Vector3.down, 0f, 0f);
            // Nothing to release under this group index. Saying so is the whole point:
            // DoBreak would still hide the intact visual, show the baked shell and enter
            // Fractured, leaving a kinematic collider-shaped ghost hanging in the air with
            // not one piece activated — and the caller, told the release succeeded, would
            // strike the group off its list and never look at those probes again.
            if (CountActivatable(context, structuralGroup) == 0) return false;

            int released = m_state >= DestructibleState.Fractured
                ? ActivateRemaining(context, bypassBudget: true,
                    structuralGroup: structuralGroup)
                : DoBreak(context, structuralGroup, bypassBudget: true);
            if (released > 0 && m_state >= DestructibleState.Fractured)
            {
                // Releasing a grounded group can strand islands in the surviving
                // groups. The group-filtered activation above skips the sweep, so
                // run it here; carried groups without ground anchors are unaffected.
                ReleaseUnsupportedIslands(context, out _);
            }
            return released > 0;
        }

        /// <summary>
        /// Runs the connectivity sweep after an authored support failure. When a structural
        /// group is supplied, both the forced span rule and the release result are scoped to
        /// that group. Neighbouring modules still get evaluated by their own probes on the
        /// same/following support polls; they cannot be woken merely because pruning one
        /// failed module happened to inspect their shared corner contacts.
        /// </summary>
        internal void ReleaseUnsupportedIslandsNow(
            int spanLimitedStructuralGroup = -1)
        {
            if (Profile == null || m_state < DestructibleState.Fractured) return;
            int previous = m_forcedSpanStructuralGroup;
            m_forcedSpanStructuralGroup = spanLimitedStructuralGroup;
            try
            {
                ReleaseUnsupportedIslands(new BreakContext(
                    transform.position, Vector3.down, 0f, 0f), out _,
                    spanLimitedStructuralGroup);
            }
            finally
            {
                m_forcedSpanStructuralGroup = previous;
            }
        }

        /// <summary>
        /// Releases a baked intact Rigidbody. StructuralSupportNode calls this automatically
        /// when the remaining support falls below the profile threshold.
        /// </summary>
        public bool ReleaseStructure()
        {
            if (Profile == null || IntactBody == null ||
                Profile.IntactBody != IntactBodyMode.ReleaseWhenUnsupported ||
                m_state >= DestructibleState.Fractured ||
                m_bodyReleased)
                return false;

            ConfigureIntactColliders(true);
            IntactBody.detectCollisions = true;
            IntactBody.useGravity = Profile.UseGravity;
            IntactBody.isKinematic = false;
            IntactBody.WakeUp();
            m_bodyReleased = true;
            m_state = DestructibleState.Released;
            StructuralNode?.MarkReleased();
            StructureReleased?.Invoke();
            OnStructureReleasedEvent?.Invoke();
            return true;
        }

        /// <summary>
        /// Brings down an intact element whose carrier was destroyed, as a collapse rather
        /// than as a drop: the baked shell replaces the intact body and every piece comes
        /// away, exactly as it does when a carried module of a Whole Building bake loses the
        /// wall under it. <see cref="ReleaseStructure"/> stays the answer when the object
        /// simply lost the ground it stood on — nothing broke it, it just fell — but a slab
        /// resting on another destructible has watched its support disintegrate, and sailing
        /// down in one rigid piece is the one thing that cannot have happened.
        /// </summary>
        internal bool CollapseUnsupportedStructure()
        {
            if (Profile == null ||
                Profile.IntactBody != IntactBodyMode.ReleaseWhenUnsupported ||
                m_state >= DestructibleState.Fractured ||
                m_bodyReleased ||
                Fragments == null || Fragments.Count == 0)
                return false;

            // Set before the break so ActivateAllOnFirstBreak reports the whole element: a
            // collapse takes every piece with it, and a budget-denied remainder would hang
            // in the air exactly where the released rigid body used to.
            m_bodyReleased = true;
            m_health = 0f;
            Vector3 point = IntactRoot != null
                ? IntactRoot.transform.position
                : transform.position;
            if (DoBreak(new BreakContext(point, Vector3.down, 0f, 0f),
                    bypassBudget: true) < 0)
            {
                // Nothing was activated, so the element is still whole. Undo the flag and
                // let the caller fall back to the plain rigid release.
                m_bodyReleased = false;
                return false;
            }

            StructuralNode?.MarkReleased();
            StructureReleased?.Invoke();
            OnStructureReleasedEvent?.Invoke();
            return true;
        }

        /// <summary>
        /// Releases every still-attached primary fragment after a local fracture. Structural
        /// support monitoring uses this when the remaining shell loses its required footing.
        /// </summary>
        internal bool ReleaseFracturedStructure()
        {
            if (Profile == null ||
                Profile.IntactBody != IntactBodyMode.ReleaseWhenUnsupported ||
                m_state < DestructibleState.Fractured ||
                m_bodyReleased)
                return false;

            // A support collapse is authoritative: leaving a budget-denied subset
            // kinematic would recreate the visibly floating shell this path fixes.
            ActivateRemaining(new BreakContext(
                transform.position, Vector3.up, 0f, 0f), bypassBudget: true);
            m_bodyReleased = true;
            StructuralNode?.MarkReleased();
            StructureReleased?.Invoke();
            OnStructureReleasedEvent?.Invoke();
            return true;
        }

        /// <summary>
        /// Fragment indices whose footprint can reach this point, from a uniform XZ grid
        /// built the first time it is needed. Returns null when the grid cannot be built,
        /// which makes the caller fall back to a full scan.
        /// </summary>
        List<int> GetFootingCandidates(Vector3 worldPoint)
        {
            if (m_footingGrid == null)
            {
                m_footingGrid = new Dictionary<long, List<int>>();
                Bounds span = default;
                bool hasSpan = false;
                for (int i = 0; i < Fragments.Count; i++)
                {
                    Renderer renderer = Fragments[i]?.Renderer;
                    if (renderer == null) continue;
                    if (!hasSpan) { span = renderer.bounds; hasSpan = true; }
                    else span.Encapsulate(renderer.bounds);
                }
                // Nothing to index. Drop the half-built grid rather than caching it: it
                // would keep a zero cell size, and every later call would take the grid
                // path, divide by it and match nothing at all. The contract above is a
                // fall back to the full scan, and it has to hold on every call.
                if (!hasSpan)
                {
                    m_footingGrid = null;
                    return null;
                }

                // One cell per roughly ten fragments across, floored so a huge object
                // cannot produce a cell so large that the grid stops filtering.
                m_footingCell = Mathf.Max(0.25f,
                    Mathf.Max(span.size.x, span.size.z) /
                    Mathf.Max(4f, Mathf.Sqrt(Fragments.Count)));

                for (int i = 0; i < Fragments.Count; i++)
                {
                    Renderer renderer = Fragments[i]?.Renderer;
                    if (renderer == null) continue;
                    Bounds b = renderer.bounds;
                    int minX = Mathf.FloorToInt(b.min.x / m_footingCell);
                    int maxX = Mathf.FloorToInt(b.max.x / m_footingCell);
                    int minZ = Mathf.FloorToInt(b.min.z / m_footingCell);
                    int maxZ = Mathf.FloorToInt(b.max.z / m_footingCell);
                    for (int x = minX; x <= maxX; x++)
                    {
                        for (int z = minZ; z <= maxZ; z++)
                        {
                            long key = FootingKey(x, z);
                            if (!m_footingGrid.TryGetValue(key, out List<int> cell))
                            {
                                cell = new List<int>(8);
                                m_footingGrid.Add(key, cell);
                            }
                            cell.Add(i);
                        }
                    }
                }
            }

            m_footingScratch.Clear();
            int px = Mathf.FloorToInt(worldPoint.x / m_footingCell);
            int pz = Mathf.FloorToInt(worldPoint.z / m_footingCell);
            for (int x = px - 1; x <= px + 1; x++)
            {
                for (int z = pz - 1; z <= pz + 1; z++)
                {
                    if (!m_footingGrid.TryGetValue(FootingKey(x, z), out List<int> cell))
                        continue;
                    for (int i = 0; i < cell.Count; i++)
                    {
                        int index = cell[i];
                        if (!m_footingScratch.Contains(index))
                            m_footingScratch.Add(index);
                    }
                }
            }
            return m_footingScratch;
        }

        static long FootingKey(int x, int z) => ((long)x << 32) | (uint)z;

        /// <summary>True when a non-released primary fragment still covers this footing.</summary>
        internal bool HasAttachedFragmentNear(Vector3 worldPoint, float radius)
        {
            radius = Mathf.Max(0.001f, radius);
            Vector4 query = new Vector4(worldPoint.x, worldPoint.y,
                worldPoint.z, radius);
            if (m_attachedFragmentNearQueries != null &&
                m_attachedFragmentNearQueries.TryGetValue(query,
                    out List<int> cached))
            {
                for (int i = 0; i < cached.Count; i++)
                    if (IsAttachedSupportCandidate(cached[i])) return true;
                return false;
            }

            float radiusSqr = radius * radius;
            // Support probing asks this for every sample of every element several times a
            // second. Scanning the whole fragment list each time is quadratic in practice,
            // so the candidates come from a horizontal grid built once: attached fragments
            // never move, and a fragment only ever leaves the set.
            List<int> candidates = GetFootingCandidates(worldPoint);
            int count = candidates != null ? candidates.Count : Fragments.Count;
            var matching = new List<int>(2);
            for (int c = 0; c < count; c++)
            {
                int i = candidates != null ? candidates[c] : c;
                FragmentRecord fragment = Fragments[i];
                if (!IsAttachedSupportCandidate(i) ||
                    (fragment.Renderer == null && fragment.Collider == null))
                    continue;

                // Renderer bounds are a conservative broad phase for the exact visual
                // triangle query below. Most grid candidates are nowhere near this probe;
                // rejecting those AABBs avoids walking every triangle of every candidate.
                Renderer supportRenderer = fragment.Renderer;
                if (supportRenderer != null &&
                    supportRenderer.bounds.SqrDistance(worldPoint) > radiusSqr)
                    continue;

                Vector3 closest;
                // Structural attachment is a visual/geometric question, not a physics
                // proxy question. Dense bakes intentionally use stable Box Colliders, and
                // a Renderer AABB covers the empty half of a diagonal shard. Either proxy
                // can therefore report an entire missing footing as material. Measure the
                // generated render triangles; their arrays are cached after the first poll.
                if (!TryGetClosestSupportVisualPoint(fragment, worldPoint, out closest))
                    closest = fragment.Collider.ClosestPoint(worldPoint);
                if ((closest - worldPoint).sqrMagnitude <= radiusSqr)
                    matching.Add(i);
            }

            if (m_attachedFragmentNearQueries == null)
                m_attachedFragmentNearQueries =
                    new Dictionary<Vector4, List<int>>();
            m_attachedFragmentNearQueries[query] = matching;
            return matching.Count > 0;
        }

        /// <summary>
        /// True when a still-attached fragment from another structural element covers this
        /// footing from below. A carried roof or deck is its own mesh island, so its
        /// support is never the ground: it is the walls of the same destructible. Once
        /// those fragments are gone this returns false and the carried group collapses.
        /// </summary>
        internal bool HasAttachedFragmentSupportNear(Vector3 worldPoint, float radius,
            int excludeStructuralGroup)
        {
            float radiusSqr = Mathf.Max(0.001f, radius) * Mathf.Max(0.001f, radius);
            List<int> candidates = GetFootingCandidates(worldPoint);
            int count = candidates != null ? candidates.Count : Fragments.Count;
            for (int c = 0; c < count; c++)
            {
                int i = candidates != null ? candidates[c] : c;
                FragmentRecord fragment = Fragments[i];
                if (!IsAttachedSupportCandidate(i) ||
                    fragment.StructuralGroup == excludeStructuralGroup ||
                    (fragment.Renderer == null && fragment.Collider == null))
                    continue;

                Renderer supportRenderer = fragment.Renderer;
                if (supportRenderer != null &&
                    supportRenderer.bounds.SqrDistance(worldPoint) > radiusSqr)
                    continue;

                Vector3 closest;
                if (!TryGetClosestSupportVisualPoint(fragment, worldPoint, out closest))
                    closest = fragment.Collider.ClosestPoint(worldPoint);
                if ((closest - worldPoint).sqrMagnitude <= radiusSqr &&
                    CarriesFooting(worldPoint, closest))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Resolves the immutable fragment membership for one structural-support probe.
        /// Attached fragments never move; they only leave the attached shell. The support
        /// node can therefore run this exact triangle query once when fracture begins and
        /// reduce every later support poll to a few boolean/component-state checks.
        /// </summary>
        internal void CollectAttachedSupportFragments(Vector3 worldPoint,
            float coverageRadius, float carrierRadius, int structuralGroup,
            List<FragmentRecord> covering, List<FragmentRecord> carriers,
            bool includeInactive = false)
        {
            if (covering == null || carriers == null) return;
            covering.Clear();
            carriers.Clear();

            coverageRadius = Mathf.Max(0.001f, coverageRadius);
            carrierRadius = Mathf.Max(0.001f, carrierRadius);
            float coverageRadiusSqr = coverageRadius * coverageRadius;
            float carrierRadiusSqr = carrierRadius * carrierRadius;
            float broadRadiusSqr = Mathf.Max(coverageRadiusSqr, carrierRadiusSqr);
            List<int> candidates = GetFootingCandidates(worldPoint);
            int count = candidates != null ? candidates.Count : Fragments.Count;
            for (int c = 0; c < count; c++)
            {
                int i = candidates != null ? candidates[c] : c;
                FragmentRecord fragment = Fragments[i];
                if (fragment == null || fragment.Activated || fragment.Detached ||
                    (fragment.Renderer == null && fragment.Collider == null) ||
                    (!includeInactive &&
                     (fragment.Transform == null ||
                      !fragment.Transform.gameObject.activeInHierarchy)))
                    continue;

                Renderer supportRenderer = fragment.Renderer;
                if (supportRenderer != null &&
                    supportRenderer.bounds.SqrDistance(worldPoint) > broadRadiusSqr)
                    continue;

                Vector3 closest;
                if (!TryGetClosestSupportVisualPoint(fragment, worldPoint, out closest))
                    closest = fragment.Collider.ClosestPoint(worldPoint);
                float distanceSqr = (closest - worldPoint).sqrMagnitude;
                // Coverage is the material belonging to THIS probe's source element.
                // The old unscoped add also cached an overlapping wall below a floor (or a
                // neighbouring wall at a corner) as if it were the probed module itself.
                // Destroying every floor fragment could therefore leave all floor probes
                // green because a different structural group still occupied the same point.
                if (fragment.StructuralGroup == structuralGroup &&
                    distanceSqr <= coverageRadiusSqr)
                    covering.Add(fragment);
                if (fragment.StructuralGroup != structuralGroup &&
                    distanceSqr <= carrierRadiusSqr &&
                    CarriesFooting(worldPoint, closest))
                    carriers.Add(fragment);
            }
        }


        bool TryGetClosestSupportVisualPoint(FragmentRecord fragment,
            Vector3 worldPoint, out Vector3 closest)
        {
            closest = worldPoint;
            Renderer renderer = fragment != null ? fragment.Renderer : null;
            if (renderer == null) return false;

            if (m_supportVisualGeometry == null)
#if UNITY_6000_4_OR_NEWER
                m_supportVisualGeometry =
                    new Dictionary<EntityId, SupportVisualGeometry>();
            EntityId key = renderer.GetEntityId();
#else
                m_supportVisualGeometry =
                    new Dictionary<int, SupportVisualGeometry>();
            int key = renderer.GetInstanceID();
#endif
            if (!m_supportVisualGeometry.TryGetValue(key,
                    out SupportVisualGeometry geometry) ||
                geometry == null || geometry.Renderer != renderer ||
                geometry.Filter == null || geometry.Filter.sharedMesh != geometry.Mesh)
            {
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                Mesh mesh = filter != null ? filter.sharedMesh : null;
                if (mesh == null || mesh.vertexCount < 3) return false;
                try
                {
                    geometry = new SupportVisualGeometry
                    {
                        Renderer = renderer,
                        Filter = filter,
                        Mesh = mesh,
                        Vertices = mesh.vertices,
                        Triangles = mesh.triangles
                    };
                }
                catch (UnityException)
                {
                    // Imported user meshes can be marked non-readable. Generated fracture
                    // meshes remain readable, but preserve collider fallback compatibility
                    // for custom/legacy prefabs instead of breaking their support checks.
                    return false;
                }
                m_supportVisualGeometry[key] = geometry;
            }

            Vector3[] vertices = geometry.Vertices;
            int[] triangles = geometry.Triangles;
            if (vertices == null || triangles == null || triangles.Length < 3)
                return false;

            Matrix4x4 localToWorld = geometry.Filter.transform.localToWorldMatrix;
            if (geometry.WorldVertices == null ||
                geometry.WorldVertices.Length != vertices.Length ||
                geometry.LocalToWorld != localToWorld)
            {
                if (geometry.WorldVertices == null ||
                    geometry.WorldVertices.Length != vertices.Length)
                    geometry.WorldVertices = new Vector3[vertices.Length];
                for (int i = 0; i < vertices.Length; i++)
                    geometry.WorldVertices[i] =
                        localToWorld.MultiplyPoint3x4(vertices[i]);
                geometry.LocalToWorld = localToWorld;
            }
            Vector3[] worldVertices = geometry.WorldVertices;
            float bestDistance = float.PositiveInfinity;
            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                int ia = triangles[i];
                int ib = triangles[i + 1];
                int ic = triangles[i + 2];
                if (ia < 0 || ib < 0 || ic < 0 ||
                    ia >= vertices.Length || ib >= vertices.Length ||
                    ic >= vertices.Length)
                    continue;
                Vector3 a = worldVertices[ia];
                Vector3 b = worldVertices[ib];
                Vector3 c = worldVertices[ic];
                Vector3 candidate = ClosestPointOnTriangle(worldPoint, a, b, c);
                float distance = (candidate - worldPoint).sqrMagnitude;
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                closest = candidate;
                if (distance <= 1e-12f) break;
            }
            return bestDistance < float.PositiveInfinity;
        }

        static Vector3 ClosestPointOnTriangle(Vector3 point,
            Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a;
            Vector3 ac = c - a;
            if (Vector3.Cross(ab, ac).sqrMagnitude <= 1e-16f)
            {
                Vector3 onAB = ClosestPointOnSegment(point, a, b);
                Vector3 onBC = ClosestPointOnSegment(point, b, c);
                Vector3 onCA = ClosestPointOnSegment(point, c, a);
                float abDistance = (onAB - point).sqrMagnitude;
                float bcDistance = (onBC - point).sqrMagnitude;
                float caDistance = (onCA - point).sqrMagnitude;
                return abDistance <= bcDistance && abDistance <= caDistance
                    ? onAB : bcDistance <= caDistance ? onBC : onCA;
            }

            Vector3 ap = point - a;
            float d1 = Vector3.Dot(ab, ap);
            float d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f) return a;

            Vector3 bp = point - b;
            float d3 = Vector3.Dot(ab, bp);
            float d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3) return b;

            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f)
                return a + ab * (d1 / (d1 - d3));

            Vector3 cp = point - c;
            float d5 = Vector3.Dot(ab, cp);
            float d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6) return c;

            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f)
                return a + ac * (d2 / (d2 - d6));

            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f)
                return b + (c - b) * ((d4 - d3) /
                    ((d4 - d3) + (d5 - d6)));

            float denominator = 1f / (va + vb + vc);
            float v = vb * denominator;
            float w = vc * denominator;
            return a + ab * v + ac * w;
        }

        static Vector3 ClosestPointOnSegment(Vector3 point,
            Vector3 a, Vector3 b)
        {
            Vector3 edge = b - a;
            float lengthSqr = edge.sqrMagnitude;
            if (lengthSqr <= 1e-16f) return a;
            return a + edge * Mathf.Clamp01(Vector3.Dot(point - a, edge) / lengthSqr);
        }

        /// <summary>
        /// True when any still-attached fragment in one source group physically touches a
        /// still-attached fragment from another group. This represents nailed, bolted or
        /// otherwise joined source meshes; it is not a broad proximity support test.
        /// </summary>
        internal bool HasAttachedStructuralContact(int structuralGroup, float tolerance)
        {
            tolerance = Mathf.Clamp(tolerance, 0.001f, 0.05f);
            EnsureCrossStructuralNeighbourCache();
            for (int i = 0; i < Fragments.Count; i++)
            {
                FragmentRecord source = Fragments[i];
                if (source == null || source.StructuralGroup != structuralGroup ||
                    !IsAttachedSupportCandidate(i))
                    continue;
                Bounds sourceBounds = GetStructuralBounds(source);

                List<int> neighbours = m_crossStructuralNeighbours != null &&
                    i < m_crossStructuralNeighbours.Length
                    ? m_crossStructuralNeighbours[i]
                    : null;
                for (int n = 0; neighbours != null && n < neighbours.Count; n++)
                {
                    int j = neighbours[n];
                    FragmentRecord other = Fragments[j];
                    if (other == null ||
                        !IsAttachedSupportCandidate(j))
                        continue;
                    if (StructuralBoundsTouch(sourceBounds,
                            GetStructuralBounds(other), tolerance))
                        return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Builds the immutable cross-module contact broad phase during normal startup.
        /// Whole-building fracture then reuses it instead of paying the cost on the exact
        /// frame of the first shot.
        /// </summary>
        internal void PrewarmStructuralContactCache()
        {
            if (Fragments == null || Fragments.Count == 0) return;
            EnsureCrossStructuralNeighbourCache();
        }

        /// <summary>
        /// True only when the attached remainder of a source group has a continuous
        /// structural path to a still-attached baked ground anchor. This deliberately
        /// rejects cycles made only from floating groups touching one another.
        /// </summary>
        internal bool HasAttachedStructuralPathToGround(int structuralGroup,
            float tolerance)
        {
            using (s_structuralPathMarker.Auto())
                return HasAttachedStructuralPathToGroundCore(structuralGroup,
                    tolerance);
        }

        bool HasAttachedStructuralPathToGroundCore(int structuralGroup,
            float tolerance)
        {
            tolerance = Mathf.Clamp(tolerance, 0.001f, 0.05f);
            if (m_supportVisited == null ||
                m_supportVisited.Length != Fragments.Count)
                m_supportVisited = new bool[Fragments.Count];
            else
                Array.Clear(m_supportVisited, 0, m_supportVisited.Length);
            m_supportQueue.Clear();

            for (int i = 0; i < Fragments.Count; i++)
            {
                FragmentRecord fragment = Fragments[i];
                if (fragment == null ||
                    fragment.StructuralGroup != structuralGroup ||
                    !IsAttachedSupportCandidate(i))
                    continue;
                m_supportVisited[i] = true;
                m_supportQueue.Enqueue(i);
            }

            while (m_supportQueue.Count > 0)
            {
                int current = m_supportQueue.Dequeue();
                FragmentRecord fragment = Fragments[current];
                if (fragment.GroundAnchor)
                    return true;

                List<int> links = fragment.SupportNeighbours;
                for (int n = 0; links != null && n < links.Count; n++)
                {
                    int neighbour = links[n];
                    if (neighbour < 0 || neighbour >= Fragments.Count ||
                        m_supportVisited[neighbour] ||
                        !IsAttachedSupportCandidate(neighbour))
                        continue;
                    FragmentRecord next = Fragments[neighbour];
                    if (next == null ||
                        next.StructuralGroup != fragment.StructuralGroup)
                        continue;
                    m_supportVisited[neighbour] = true;
                    m_supportQueue.Enqueue(neighbour);
                }

                EnqueueTouchingStructuralNeighboursWithTolerance(current,
                    tolerance, false);
            }

            return false;
        }

        void EnqueueTouchingStructuralNeighbours(int currentIndex, float supportReach)
        {
            float tolerance = Mathf.Clamp(supportReach * 0.1f, 0.005f, 0.03f);
            EnqueueTouchingStructuralNeighboursWithTolerance(currentIndex,
                tolerance, true, true);
        }

        void EnqueueTouchingStructuralNeighboursWithTolerance(int currentIndex,
            float tolerance, bool followSupportUpward,
            bool skipWeakSupport = false)
        {
            if (currentIndex < 0 || currentIndex >= Fragments.Count) return;
            FragmentRecord current = Fragments[currentIndex];
            if (current == null) return;

            tolerance = Mathf.Clamp(tolerance, 0.001f, 0.05f);
            Bounds currentBounds = GetStructuralBounds(current);
            EnsureCrossStructuralNeighbourCache();
            List<int> neighbours = m_crossStructuralNeighbours != null &&
                currentIndex < m_crossStructuralNeighbours.Length
                ? m_crossStructuralNeighbours[currentIndex]
                : null;
            for (int n = 0; neighbours != null && n < neighbours.Count; n++)
            {
                int i = neighbours[n];
                if (i == currentIndex || m_supportVisited[i] ||
                    !IsAttachedSupportCandidate(i) ||
                    (skipWeakSupport && m_supportPruned != null &&
                     i < m_supportPruned.Length && m_supportPruned[i]))
                    continue;
                FragmentRecord candidate = Fragments[i];
                if (candidate == null) continue;
                Bounds candidateBounds = GetStructuralBounds(candidate);
                if (!StructuralBoundsTouch(currentBounds,
                        candidateBounds, tolerance))
                    continue;
                bool validDirection = followSupportUpward
                    ? CanCarryAcrossStructuralGroups(currentBounds,
                        candidateBounds, tolerance)
                    : CanCarryAcrossStructuralGroups(candidateBounds,
                        currentBounds, tolerance);
                if (!validDirection)
                    continue;
                m_supportVisited[i] = true;
                m_supportQueue.Enqueue(i);
            }
        }

        /// <summary>
        /// Authored module seams never move while fragments remain attached. Cache the
        /// cross-module contact graph once instead of scanning every fragment for every
        /// breadth-first support step on every structural polling interval.
        /// </summary>
        void EnsureCrossStructuralNeighbourCache()
        {
            int count = Fragments != null ? Fragments.Count : 0;
            if (m_crossStructuralNeighbours != null &&
                m_crossStructuralNeighbourFragmentCount == count)
                return;

            using (s_structuralJointCacheMarker.Auto())
            {
                m_crossStructuralNeighbours = new List<int>[count];
                m_crossStructuralNeighbourFragmentCount = count;
                BuildBakedSupportComponents(count);

                // Authored module contacts are local. The old all-pairs pass still tested
                // every fragment against every other fragment, so the first local hit on
                // a 250-piece building paid tens of thousands of bounds checks in one
                // frame. Sweep the precomputed bounds along X and stop as soon as the next
                // candidate is outside contact tolerance. The final 3D contact predicate
                // is unchanged; this only removes pairs that cannot possibly touch.
                var structuralBounds = new Bounds[count];
                var sorted = new List<int>(count);
                for (int i = 0; i < count; i++)
                {
                    FragmentRecord fragment = Fragments[i];
                    if (fragment == null) continue;
                    structuralBounds[i] = GetStructuralBounds(fragment);
                    sorted.Add(i);
                }
                sorted.Sort((left, right) => structuralBounds[left].min.x
                    .CompareTo(structuralBounds[right].min.x));

                // A fixed 5 cm is a different statement on every object. Measured across
                // the demo content it is 3.6% of a tower fragment but 26% of a window
                // shard, so the same constant welded glass that was a quarter of a piece
                // apart while barely reaching across a wall's fracture gap. Scale it by the
                // characteristic fragment length instead — the cube root of mean volume,
                // which is the natural length of a solid and far steadier on irregular
                // shards than a surface-area estimate. The clamp keeps it from collapsing
                // to nothing on dust-sized debris and never loosens it past the old value.
                float contactTolerance = FragmentContactTolerance();
                for (int sortedA = 0; sortedA < sorted.Count; sortedA++)
                {
                    int i = sorted[sortedA];
                    FragmentRecord a = Fragments[i];
                    Bounds aBounds = structuralBounds[i];
                    float maximumCandidateX = aBounds.max.x + contactTolerance;
                    for (int sortedB = sortedA + 1;
                         sortedB < sorted.Count; sortedB++)
                    {
                        int j = sorted[sortedB];
                        Bounds bBounds = structuralBounds[j];
                        if (bBounds.min.x > maximumCandidateX) break;
                        FragmentRecord b = Fragments[j];
                        // Inside one element the baked fracture graph is the authority:
                        // bridging fragments it already relates would let the support
                        // search walk straight across a real cut. The exception is a pair
                        // the bake never related at all — two separate components of the
                        // same element that physically abut. That is a hole in the baked
                        // data, not a cut, and closing it here keeps an untouched slab
                        // from shedding an island the search could never reach.
                        if (b.StructuralGroup == a.StructuralGroup &&
                            !AreSeparateBakedSupportComponents(i, j))
                            continue;
                        if (!StructuralBoundsTouch(aBounds,
                                bBounds, contactTolerance))
                            continue;
                        if (!FragmentSurfacesTouch(a, b, aBounds, bBounds,
                                contactTolerance))
                            continue;

                        if (m_crossStructuralNeighbours[i] == null)
                            m_crossStructuralNeighbours[i] = new List<int>(4);
                        if (m_crossStructuralNeighbours[j] == null)
                            m_crossStructuralNeighbours[j] = new List<int>(4);
                        m_crossStructuralNeighbours[i].Add(j);
                        m_crossStructuralNeighbours[j].Add(i);
                    }
                }
            }
        }

        /// <summary>
        /// Contact distance expressed in the object's own scale: a few percent of the
        /// characteristic fragment length, so a wall and a pane of glass are judged by the
        /// same proportion rather than the same absolute millimetres.
        /// </summary>
        float FragmentContactTolerance()
        {
            double volume = 0d;
            int counted = 0;
            for (int i = 0; i < Fragments.Count; i++)
            {
                FragmentRecord fragment = Fragments[i];
                if (fragment == null || fragment.Volume <= 0f) continue;
                volume += fragment.Volume;
                counted++;
            }
            if (counted == 0) return 0.05f;
            float length = Mathf.Pow((float)(volume / counted), 1f / 3f);
            return Mathf.Clamp(length * 0.04f, 0.002f, 0.05f);
        }

        /// <summary>
        /// Whether two fragments really meet, as opposed to merely owning overlapping
        /// bounding boxes.
        ///
        /// A whole-building fracture returns long diagonal shards, and two of them can have
        /// boxes that interpenetrate by metres while the solids are nowhere near each other.
        /// StructuralBoundsTouch reports a contact for every such pair, so a piece left in
        /// mid-air kept a route to the ground through a wall it does not touch and never
        /// fell. The box test stays as the cheap broad phase; this settles the pairs it
        /// cannot. Ping-ponging the closest-point query converges on the real gap in a few
        /// steps, and it runs once per pair while the cache is built — attached fragments do
        /// not move, so the answer cannot change afterwards.
        ///
        /// A fragment whose mesh cannot be read keeps the old box answer: without geometry
        /// there is nothing better to say, and refusing the contact would drop material the
        /// previous behaviour held up.
        /// </summary>
        bool FragmentSurfacesTouch(FragmentRecord a, FragmentRecord b,
            Bounds aBounds, Bounds bBounds, float tolerance)
        {
            // Boxes that only graze each other were already decided by the broad phase.
            // Re-testing them would spend mesh queries on the easy case.
            if (!BoundsInterpenetrate(aBounds, bBounds, tolerance)) return true;

            float toleranceSqr = tolerance * tolerance;
            Vector3 probe = bBounds.center;
            for (int step = 0; step < 3; step++)
            {
                if (!TryGetClosestSupportVisualPoint(a, probe, out Vector3 onA))
                    return true;
                if (!TryGetClosestSupportVisualPoint(b, onA, out Vector3 onB))
                    return true;
                if ((onA - onB).sqrMagnitude <= toleranceSqr) return true;
                if ((probe - onB).sqrMagnitude <= 1e-8f) break;
                probe = onB;
            }
            return false;
        }

        /// <summary>
        /// True when the two boxes overlap on every axis by more than the contact tolerance,
        /// which is the case a face contact and a metre-deep interpenetration share.
        /// </summary>
        static bool BoundsInterpenetrate(Bounds a, Bounds b, float tolerance)
        {
            return Mathf.Min(a.max.x, b.max.x) - Mathf.Max(a.min.x, b.min.x) > tolerance &&
                   Mathf.Min(a.max.y, b.max.y) - Mathf.Max(a.min.y, b.min.y) > tolerance &&
                   Mathf.Min(a.max.z, b.max.z) - Mathf.Max(a.min.z, b.min.z) > tolerance;
        }

        /// <summary>
        /// Labels every fragment with the connected component of the baked neighbour graph
        /// it belongs to, following links inside one structural group only. Attachment is
        /// deliberately ignored: this describes what the bake welded together, so a piece
        /// destroyed later cannot make its neighbours look like a separate component.
        /// </summary>
        void BuildBakedSupportComponents(int count)
        {
            if (m_bakedSupportComponents == null ||
                m_bakedSupportComponents.Length != count)
                m_bakedSupportComponents = new int[count];
            for (int i = 0; i < count; i++) m_bakedSupportComponents[i] = -1;

            int component = 0;
            var queue = new Queue<int>();
            for (int start = 0; start < count; start++)
            {
                if (m_bakedSupportComponents[start] >= 0 ||
                    Fragments[start] == null)
                    continue;

                int group = Fragments[start].StructuralGroup;
                m_bakedSupportComponents[start] = component;
                queue.Clear();
                queue.Enqueue(start);
                while (queue.Count > 0)
                {
                    int current = queue.Dequeue();
                    List<int> links = Fragments[current].SupportNeighbours;
                    for (int n = 0; links != null && n < links.Count; n++)
                    {
                        int neighbour = links[n];
                        if (neighbour < 0 || neighbour >= count ||
                            m_bakedSupportComponents[neighbour] >= 0 ||
                            Fragments[neighbour] == null ||
                            Fragments[neighbour].StructuralGroup != group)
                            continue;
                        m_bakedSupportComponents[neighbour] = component;
                        queue.Enqueue(neighbour);
                    }
                }
                component++;
            }
        }

        bool AreSeparateBakedSupportComponents(int a, int b)
        {
            if (m_bakedSupportComponents == null ||
                a < 0 || b < 0 ||
                a >= m_bakedSupportComponents.Length ||
                b >= m_bakedSupportComponents.Length)
                return false;
            return m_bakedSupportComponents[a] != m_bakedSupportComponents[b];
        }

        /// <summary>
        /// Cross-source joints transfer support from a lower element into material whose
        /// base is at the same height or higher. They must not transfer it back down from a
        /// carried arch/beam into the severed top of a column. Without this direction the
        /// breadth-first search could travel up the surviving column, across the arch and
        /// back down the broken column, leaving that upper island suspended in mid-air.
        /// Same-source fracture neighbours remain bidirectional; only authored joints
        /// between separate source elements need this gravity direction.
        /// </summary>
        internal static bool CanCarryAcrossStructuralGroups(Bounds support,
            Bounds carried, float tolerance)
        {
            return carried.min.y + Mathf.Max(0f, tolerance) >= support.min.y;
        }

        static Bounds GetStructuralBounds(FragmentRecord fragment)
        {
            if (fragment.Renderer != null) return fragment.Renderer.bounds;
            if (fragment.Collider != null) return fragment.Collider.bounds;
            return new Bounds(fragment.Transform != null
                ? fragment.Transform.position : Vector3.zero, Vector3.zero);
        }

        internal static bool StructuralBoundsTouch(Bounds a, Bounds b, float tolerance)
        {
            tolerance = Mathf.Max(0f, tolerance);
            float dx = Mathf.Max(0f, Mathf.Max(a.min.x - b.max.x,
                b.min.x - a.max.x));
            float dy = Mathf.Max(0f, Mathf.Max(a.min.y - b.max.y,
                b.min.y - a.max.y));
            float dz = Mathf.Max(0f, Mathf.Max(a.min.z - b.max.z,
                b.min.z - a.max.z));
            return dx * dx + dy * dy + dz * dz <= tolerance * tolerance;
        }

        /// <summary>
        /// True when <paramref name="contact"/> sits under <paramref name="footing"/> inside a
        /// 45° downward cone, or contains it. Two elements standing side by side are always
        /// within reach of each other horizontally; without the cone they would hold each
        /// other up forever and neither would ever collapse.
        /// </summary>
        internal static bool CarriesFooting(Vector3 footing, Vector3 contact)
        {
            float drop = footing.y - contact.y;
            if (drop < -0.001f) return false;
            float horizontal = new Vector2(footing.x - contact.x,
                footing.z - contact.z).magnitude;
            return drop >= horizontal - 0.001f;
        }

        void ConfigureIntactBody()
        {
            if (IntactBody == null || Profile == null) return;

            IntactBody.detectCollisions = true;
            IntactBody.useGravity = Profile.UseGravity;
            bool dynamicFromStart = Profile.IntactBody == IntactBodyMode.Dynamic;
            ConfigureIntactColliders(dynamicFromStart);
            IntactBody.isKinematic = !dynamicFromStart;
            if (dynamicFromStart)
                IntactBody.WakeUp();
        }

        // ------------------------------------------------------------------ lifecycle tick

        void FixedUpdate()
        {
            ProcessPendingActivationBatch();
            if (m_attachedShadowProxyDirty)
                RebuildAttachedShadowProxy();
            if (Profile == null) return;

            // A locally fractured Dynamic prop no longer has its intact Rigidbody/collider.
            // Its not-yet-broken pieces are a kinematic visual shell. Keep that shell only
            // while it genuinely rests on static world geometry; once airborne, release the
            // remaining pieces with gravity and the original body's velocity instead of
            // leaving them frozen in space.
            if (m_state >= DestructibleState.Fractured &&
                m_state < DestructibleState.Recycled &&
                Profile.IntactBody == IntactBodyMode.Dynamic)
                ProcessDynamicResidualShellSupport();

            GuardPrimaryFragmentsAgainstStaticGround();

            // Debris auto-despawn must keep ticking after the lifecycle has moved past
            // Fractured: settle-cleanup can freeze every piece and flip the state to
            // Settled long before the despawn timer matures, and those frozen proxies
            // are exactly the rubble the timer exists to reclaim.
            bool fractured = m_state == DestructibleState.Fractured;
            bool sceneCleanup = DestructionBudgetManager.OverridesProfileRemoval(this);
            bool sweepRetiredDebris = !sceneCleanup && Profile.DespawnDebris && !m_allDebrisDespawned &&
                                      m_state > DestructibleState.Fractured;
            if (!fractured && !sweepRetiredDebris) return;

            bool doCleanup = Profile.LimitActiveTime || Profile.CleanupSettledFragments ||
                             (!sceneCleanup && Profile.DespawnDebris);
            if (!doCleanup && !Profile.DegradeSmallestOverBudget) return;

            using (s_tickMarker.Auto())
            {
                if (doCleanup) ProcessTimedCleanup();
                if (fractured && Profile.DegradeSmallestOverBudget)
                    ProcessBudgetDegradation();
            }
        }

        void GuardPrimaryFragmentsAgainstStaticGround()
        {
            if (m_activeCount <= 0 || Fragments == null ||
                Time.fixedDeltaTime <= 0f ||
                !FragmentRuntime.UsesMeshGroundGuard(Profile))
                return;
            int layers = Profile != null ? Profile.CollisionLayers : Physics.AllLayers;
            for (int i = 0; i < Fragments.Count; i++)
            {
                FragmentRecord fragment = Fragments[i];
                if (fragment == null || !fragment.Activated || fragment.Settled ||
                    fragment.DebrisDespawned || fragment.Body == null ||
                    fragment.Renderer == null || IsDynamicClusterMember(i) ||
                    (fragment.GroundGuardResolved && !fragment.NeedsMeshGroundGuard))
                    continue;
                if (!fragment.GroundGuardResolved)
                {
                    fragment.NeedsMeshGroundGuard =
                        FragmentRuntime.RequiresMeshGroundGuard(fragment.Body);
                    fragment.Runtime = fragment.Body.GetComponent<FragmentRuntime>();
                    fragment.GroundGuardResolved = true;
                    if (!fragment.NeedsMeshGroundGuard) continue;
                }
                FragmentRuntime.GuardStaticGroundCrossing(fragment.Body,
                    fragment.Renderer.bounds, transform, m_fragmentGroundGuardHits,
                    layers, Time.fixedDeltaTime, true,
                    fragment.Runtime != null &&
                    fragment.Runtime.HasSustainedGroundContact);
            }

            // A Dynamic remainder is one carrier Rigidbody with several convex proxy
            // shapes. Its member renderers move as children and therefore cannot be guarded
            // independently; query their union once for the carrier. This preserves the
            // cheap clustered simulation while keeping the visible remainder above Plane.
            if (m_dynamicClusterCarrierIndex >= 0 &&
                m_dynamicClusterCarrierIndex < Fragments.Count &&
                m_dynamicClusterProxyColliders.Count > 0)
            {
                FragmentRecord carrier = Fragments[m_dynamicClusterCarrierIndex];
                if (carrier?.Body != null && !carrier.Body.isKinematic)
                {
                    bool initialized = false;
                    Bounds clusterBounds = default(Bounds);
                    bool needsGuard = false;
                    for (int proxyIndex = 0;
                         proxyIndex < m_dynamicClusterProxyColliders.Count;
                         proxyIndex++)
                    {
                        Collider proxy = m_dynamicClusterProxyColliders[proxyIndex];
                        if (proxy == null || !proxy.enabled || proxy.isTrigger) continue;
                        needsGuard |= proxy is MeshCollider mesh && mesh.convex;
                        if (!initialized)
                        {
                            clusterBounds = proxy.bounds;
                            initialized = true;
                        }
                        else
                            clusterBounds.Encapsulate(proxy.bounds);
                    }
                    if (initialized && needsGuard)
                        FragmentRuntime.GuardStaticGroundCrossing(carrier.Body,
                            clusterBounds, transform, m_fragmentGroundGuardHits,
                            layers, Time.fixedDeltaTime, true,
                            carrier.Runtime != null &&
                            carrier.Runtime.HasSustainedGroundContact);
                }
            }
        }

        void ProcessDynamicResidualShellSupport()
        {
            if (Time.fixedTime < m_nextDynamicSupportCheck) return;
            m_nextDynamicSupportCheck = Time.fixedTime +
                Mathf.Max(0.02f, Profile.SupportCheckInterval);

            bool hasAttached = false;
            bool supported = false;
            float distance = Mathf.Max(0.01f, Profile.SupportProbeDistance);
            float lift = Mathf.Min(0.05f, distance * 0.5f);

            for (int i = 0; i < Fragments.Count; i++)
            {
                if (!IsAttachedSupportCandidate(i)) continue;
                FragmentRecord fragment = Fragments[i];
                Bounds bounds = GetStructuralBounds(fragment);
                if (bounds.size.sqrMagnitude <= 0.000001f) continue;
                hasAttached = true;

                Vector3 origin = new Vector3(bounds.center.x,
                    bounds.min.y + Mathf.Max(0.002f, bounds.size.y * 0.002f),
                    bounds.center.z);
                int hits = Physics.RaycastNonAlloc(origin + Vector3.up * lift,
                    Vector3.down, m_dynamicSupportHits, distance + lift,
                    Profile.SupportLayers, QueryTriggerInteraction.Ignore);
                for (int h = 0; h < hits; h++)
                {
                    Collider hit = m_dynamicSupportHits[h].collider;
                    if (hit == null || !hit.enabled ||
                        !hit.gameObject.activeInHierarchy ||
                        hit.transform == transform || hit.transform.IsChildOf(transform))
                        continue;
                    Rigidbody body = hit.attachedRigidbody;
                    if (body != null && !body.isKinematic) continue;
                    DestructibleRoot supportRoot =
                        hit.GetComponentInParent<DestructibleRoot>();
                    if (supportRoot != null &&
                        (supportRoot == this || supportRoot.IsStructurallyReleased ||
                         supportRoot.State >= DestructibleState.Released))
                        continue;
                    supported = true;
                    break;
                }
                if (supported) break;
            }

            if (!hasAttached || supported)
            {
                m_dynamicUnsupportedSince = -1f;
                return;
            }

            if (m_dynamicUnsupportedSince < 0f)
                m_dynamicUnsupportedSince = Time.fixedTime;
            if (Time.fixedTime - m_dynamicUnsupportedSince <
                Mathf.Max(0f, Profile.SupportGraceTime))
                return;

            var drop = new BreakContext(transform.position, Vector3.down, 0f, 0f);
            ActivateRemaining(drop, bypassBudget: true, structuralGroup: -1,
                inheritedLinearVelocity: m_dynamicResidualLinearVelocity,
                inheritedAngularVelocity: m_dynamicResidualAngularVelocity);
            m_dynamicResidualLinearVelocity = Vector3.zero;
            m_dynamicResidualAngularVelocity = Vector3.zero;
            m_dynamicUnsupportedSince = -1f;
        }

        void ProcessTimedCleanup()
        {
            bool sceneCleanup = DestructionBudgetManager.OverridesProfileRemoval(this);
            {
                float dt = Time.fixedDeltaTime;
                float sleepVelSqr = Profile.SleepVelocity * Profile.SleepVelocity;
                int stillActive = 0;
                bool debrisRemaining = false;

                for (int i = 0; i < Fragments.Count; i++)
                {
                    var f = Fragments[i];
                    if (f == null || f.Body == null) continue;
                    if (IsDynamicClusterMember(i))
                    {
                        // The carrier and its kinematic followers are the still-connected
                        // remainder, not debris. Settling/despawning followers used to
                        // detach and freeze the barrel one piece at a time.
                        continue;
                    }

                    // Debris auto-despawn runs for loose AND already-settled pieces: a
                    // frozen proxy still pays its renderer and collider, and the whole
                    // point of the timer is that rubble eventually costs nothing.
                    if (!sceneCleanup && Profile.DespawnDebris && !f.DebrisDespawned &&
                        (f.Activated || f.Settled))
                    {
                        if (Time.time - f.ActivatedTime > Profile.DespawnDebrisAfter)
                        {
                            DespawnDebrisFragment(i);
                            continue;
                        }
                        debrisRemaining = true;
                    }

                    if (!f.Activated || f.Settled) continue;

#if UNITY_6000_0_OR_NEWER
                    float speedSqr = f.Body.linearVelocity.sqrMagnitude;
#else
                    float speedSqr = f.Body.velocity.sqrMagnitude;
#endif
                    if (f.Body.IsSleeping() || speedSqr < sleepVelSqr)
                        f.SleepTimer += dt;
                    else
                        f.SleepTimer = 0f;

                    bool lifetimeExpired = Profile.LimitActiveTime &&
                                           Profile.CleanupLifetime > 0f &&
                                           Time.time - f.ActivatedTime > Profile.CleanupLifetime;
                    bool settledByRest = Profile.CleanupSettledFragments &&
                                         f.SleepTimer >= Profile.SleepDuration;

                    if (settledByRest || lifetimeExpired)
                        SettleFragment(i);
                    else
                        stillActive++;
                }

                if (!sceneCleanup && Profile.DespawnDebris)
                    m_allDebrisDespawned = !debrisRemaining;

                if (m_state == DestructibleState.Fractured &&
                    stillActive == 0 && m_activeCount == 0 &&
                    m_secondaryActiveCount == 0 &&
                    (m_settledCount > 0 || m_secondarySettledCount > 0))
                {
                    m_state = AutomaticCleanupAction == CleanupAction.Recycle
                        ? DestructibleState.Recycled
                        : DestructibleState.Settled;
                    SettledAll?.Invoke(m_settledCount);
                }
            }
        }

        void ProcessPendingActivationBatch()
        {
            if (m_pendingActivations.Count == 0) return;
            using (s_activationBatchMarker.Auto())
            {
                int count = Mathf.Min(LargeActivationBatchSize,
                    m_pendingActivations.Count);
                for (int i = 0; i < count; i++)
                {
                    PendingFragmentActivation pending =
                        m_pendingActivations.Dequeue();
                    m_pendingActivationIndices.Remove(pending.Index);
                    if (pending.Index < 0 || pending.Index >= Fragments.Count)
                        continue;
                    FragmentRecord fragment = Fragments[pending.Index];
                    if (fragment == null || fragment.Activated || fragment.Detached ||
                        !HasActivatablePhysics(fragment))
                    {
                        if (UsesActiveBodyBudget)
                            DestructionBudgetManager.Instance.ReleaseActivation(
                                Profile.BudgetGroup, 1);
                        continue;
                    }
                    if (pending.HasInheritedVelocityOrigin)
                    {
                        ActivateFragment(pending.Index, pending.Context,
                            pending.InheritedLinearVelocity,
                            pending.InheritedAngularVelocity,
                            pending.InheritedVelocityOrigin);
                    }
                    else
                    {
                        ActivateFragment(pending.Index, pending.Context,
                            pending.InheritedLinearVelocity,
                            pending.InheritedAngularVelocity);
                    }
                }
            }
        }

        /// <summary>
        /// Immediately retires every loose piece of debris this destructible has shed —
        /// primary fragments, broken-off level-two children and runtime split pieces —
        /// using the same fade the automatic Despawn Debris timer applies. Call it when
        /// gameplay wants the wreck gone ahead of the timer (end of a wave, a cutscene,
        /// streaming the area out). Returns how many pieces were despawned.
        /// </summary>
        public int DespawnDebrisNow()
        {
            int despawned = 0;
            for (int i = 0; i < Fragments.Count; i++)
            {
                var f = Fragments[i];
                if (f == null) continue;
                if (IsDynamicClusterMember(i)) continue;
                if (f.SecondaryNode != null && f.SecondaryNode.IsBroken)
                    despawned += f.SecondaryNode.DespawnAllChildDebris();
                if (f.Body == null || f.DebrisDespawned) continue;
                if (!f.Activated && !f.Settled) continue;
                DespawnDebrisFragment(i);
                despawned++;
            }
            if (m_runtimeSplitter != null)
                despawned += m_runtimeSplitter.DespawnAllPieces();
            if (despawned > 0) m_allDebrisDespawned = true;
            return despawned;
        }

        /// <summary>
        /// Retires one piece of debris for good: whatever the configured Cleanup action
        /// was, the piece ends frozen, collider-free and faded out. Pieces the cleanup
        /// already settled (frozen proxies, still-simulating None pieces) are retired
        /// without disturbing the active/settled bookkeeping a second time.
        /// </summary>
        void DespawnDebrisFragment(int index, DebrisDespawnMode? mode = null)
        {
            var f = Fragments[index];
            if (f == null || f.DebrisDespawned ||
                IsDynamicClusterMember(index)) return;
            f.DebrisDespawned = true;
            DebrisDespawnMode despawnMode = mode ?? (Profile.SmoothDespawn
                ? DebrisDespawnMode.SmoothShrink : DebrisDespawnMode.Instant);

            if (!f.Settled)
            {
                SettleFragment(index, CleanupAction.Recycle,
                    despawnMode != DebrisDespawnMode.Instant, despawnMode);
                return;
            }

            // A None cleanup left the body simulating and still counted against the
            // group budget; the slot returns now that the piece freezes for good.
            if (UsesActiveBodyBudget && Profile.Cleanup == CleanupAction.None &&
                m_outstandingBudgetCount > 0)
            {
                m_outstandingBudgetCount--;
                DestructionBudgetManager.Instance.ReleaseActivation(
                    Profile.BudgetGroup, 1);
            }
            if (f.Body != null) f.Body.isKinematic = true;
            if (f.Collider != null) f.Collider.enabled = false;
            SetCollidersEnabled(f.DynamicColliders, false);
            if (f.DynamicCollider != null) f.DynamicCollider.enabled = false;
            if (f.Transform != null && f.Transform.gameObject.activeSelf)
            {
                FragmentDespawnTransition.Begin(f.Transform, despawnMode);
            }
        }

        internal CleanupAction AutomaticCleanupAction =>
            Profile.Cleanup == CleanupAction.Recycle && DestructionBudgetManager.OverridesProfileRemoval(this)
                ? CleanupAction.FreezeAsProxy : Profile.Cleanup;

        void SettleFragment(int index) =>
            SettleFragment(index, AutomaticCleanupAction, Profile.SmoothDespawn);

        void SettleFragment(int index, CleanupAction action, bool smoothDespawn,
            DebrisDespawnMode despawnMode = DebrisDespawnMode.SmoothShrink)
        {
            var f = Fragments[index];
            DetachDynamicClusterFragment(index);
            f.Settled = true;
            m_activeCount = Mathf.Max(0, m_activeCount - 1);
            m_settledCount++;

            if (UsesActiveBodyBudget && action == CleanupAction.None)
            {
                // The Rigidbody keeps simulating, so it stays counted against the group
                // budget. The slot is returned when the destructible resets and the
                // body is frozen (or the fragment object is destroyed by gameplay).
                m_outstandingBudgetCount++;
            }
            else if (UsesActiveBodyBudget)
            {
                DestructionBudgetManager.Instance.ReleaseActivation(
                    Profile.BudgetGroup, 1);
            }

            switch (action)
            {
                case CleanupAction.None:
                    break;
                case CleanupAction.FreezeAsProxy:
                    if (f.Body != null) f.Body.isKinematic = true;
                    UseExactFragmentCollider(f);
                    break;
                case CleanupAction.Recycle:
                    if (f.Body != null) f.Body.isKinematic = true;
                    if (f.Collider != null) f.Collider.enabled = false;
                    SetCollidersEnabled(f.DynamicColliders, false);
                    if (f.DynamicCollider != null) f.DynamicCollider.enabled = false;
                    if (f.Transform != null)
                    {
                        if (smoothDespawn)
                            FragmentDespawnTransition.Begin(f.Transform, despawnMode);
                        else
                            f.Transform.gameObject.SetActive(false);
                    }
                    break;
            }
        }

        // -------------------------------------------------- smallest-first degradation

        readonly List<int> m_budgetScratch = new List<int>(64);

        internal bool IsSceneDebris(int index)
        {
            if (index < 0 || index >= Fragments.Count) return false;
            FragmentRecord piece = Fragments[index];
            return piece != null && piece.Activated && !piece.BudgetDespawned &&
                !piece.DebrisDespawned && !IsDynamicClusterMember(index) &&
                (piece.SecondaryNode == null || !piece.SecondaryNode.IsBroken) &&
                piece.Transform != null && piece.Transform.gameObject.activeInHierarchy;
        }

        internal void CollectSceneDebris(List<DestructionBudgetManager.SceneDebrisCandidate> output)
        {
            if (Profile == null) return;
            for (int i = 0; i < Fragments.Count; i++)
            {
                FragmentRecord piece = Fragments[i];
                if (IsSceneDebris(i))
                    output.Add(new DestructionBudgetManager.SceneDebrisCandidate
                    {
                        Root = this, Fragment = piece, PrimaryIndex = i,
                        Volume = DestructionBudgetManager.SceneDebrisCandidate.WorldVolume(piece.Volume, piece.Transform),
                        Triangles = FragmentTriangleCount(piece)
                    });
                SecondaryFragmentNode secondary = piece?.SecondaryNode;
                if (secondary == null || !secondary.IsBroken) continue;
                foreach (FragmentRecord child in secondary.Children)
                    if (child != null && child.Activated && !child.DebrisDespawned &&
                        child.Transform != null && child.Transform.gameObject.activeInHierarchy)
                        output.Add(new DestructionBudgetManager.SceneDebrisCandidate
                        {
                            Root = this, Fragment = child, Secondary = secondary,
                            Volume = DestructionBudgetManager.SceneDebrisCandidate.WorldVolume(child.Volume, child.Transform),
                            Triangles = FragmentTriangleCount(child)
                        });
            }
            if (m_runtimeSplitter != null) m_runtimeSplitter.CollectSceneDebris(output);
        }

        internal void DespawnForSceneBudget(int index, DebrisDespawnMode mode)
        {
            if (IsSceneDebris(index)) DespawnDebrisFragment(index, mode);
        }

        internal void ReleaseSettledBodyBudget(int count)
        {
            if (!UsesActiveBodyBudget || Profile.Cleanup != CleanupAction.None) return;
            int released = Mathf.Min(count, m_outstandingBudgetCount);
            m_outstandingBudgetCount -= released;
            DestructionBudgetManager.Instance.ReleaseActivation(Profile.BudgetGroup, released);
        }

        /// <summary>
        /// Optional reactive budget: keeps heavy destruction bounded by degrading the
        /// smallest fragments first. Stage 1 freezes the smallest simulating pieces to
        /// non-interactive visual props once they exceed Max Active Fragments. Stage 2
        /// removes the smallest visible pieces (gradual shrink) once the visible count or
        /// triangle budget is exceeded. Larger, more visually important chunks are kept.
        /// </summary>
        void ProcessBudgetDegradation()
        {
            int softCap = Profile.MaxActiveFragments;
            if (softCap > 0 && m_activeCount > softCap)
            {
                m_budgetScratch.Clear();
                for (int i = 0; i < Fragments.Count; i++)
                {
                    var f = Fragments[i];
                    if (f != null && f.Activated && !f.Settled &&
                        f.Body != null && !f.BudgetDespawned &&
                        !IsDynamicClusterMember(i))
                        m_budgetScratch.Add(i);
                }
                m_budgetScratch.Sort(CompareByVolumeAscending);
                int toFreeze = m_budgetScratch.Count - softCap;
                for (int k = 0; k < m_budgetScratch.Count && toFreeze > 0; k++)
                {
                    int idx = m_budgetScratch[k];
                    SettleFragment(idx, CleanupAction.FreezeAsProxy, false);
                    Fragments[idx].BudgetFrozen = true;
                    toFreeze--;
                }
            }

            // Scene rules own disappearance; the local physics freeze cap can still run.
            if (DestructionBudgetManager.OverridesProfileRemoval(this)) return;
            int maxVisible = Profile.MaxVisibleFragments;
            int maxTris = Profile.MaxVisibleTriangles;
            if (maxVisible <= 0 && maxTris <= 0) return;

            m_budgetScratch.Clear();
            long totalTris = 0;
            for (int i = 0; i < Fragments.Count; i++)
            {
                var f = Fragments[i];
                if (f == null || f.BudgetDespawned || !f.Activated) continue;
                if (IsDynamicClusterMember(i)) continue;
                if (f.Transform == null || !f.Transform.gameObject.activeSelf) continue;
                m_budgetScratch.Add(i);
                totalTris += FragmentTriangleCount(f);
            }

            bool overCount = maxVisible > 0 && m_budgetScratch.Count > maxVisible;
            bool overTris = maxTris > 0 && totalTris > maxTris;
            if (!overCount && !overTris) return;

            m_budgetScratch.Sort(CompareByVolumeAscending);
            int visibleCount = m_budgetScratch.Count;
            for (int k = 0; k < m_budgetScratch.Count; k++)
            {
                bool stillOverCount = maxVisible > 0 && visibleCount > maxVisible;
                bool stillOverTris = maxTris > 0 && totalTris > maxTris;
                if (!stillOverCount && !stillOverTris) break;
                int idx = m_budgetScratch[k];
                totalTris -= FragmentTriangleCount(Fragments[idx]);
                visibleCount--;
                BudgetDespawn(idx);
            }
        }

        int CompareByVolumeAscending(int a, int b) =>
            Fragments[a].Volume.CompareTo(Fragments[b].Volume);

        void BudgetDespawn(int index)
        {
            var f = Fragments[index];
            if (f == null || f.BudgetDespawned) return;
            if (!f.Settled && f.Activated)
            {
                // Still simulating: settle as a gradual recycle so it fades out and the
                // active count and any budget slot are released correctly.
                SettleFragment(index, CleanupAction.Recycle, true);
            }
            else
            {
                // Already a frozen proxy: hide it now with the same gradual shrink.
                if (f.Body != null) f.Body.isKinematic = true;
                if (f.Collider != null) f.Collider.enabled = false;
                SetCollidersEnabled(f.DynamicColliders, false);
                if (f.DynamicCollider != null) f.DynamicCollider.enabled = false;
                if (f.Transform != null) FragmentDespawnTransition.Begin(f.Transform);
            }
            f.BudgetFrozen = false;
            f.BudgetDespawned = true;
        }

        int FragmentTriangleCount(FragmentRecord f)
        {
            if (f.CachedTriangleCount >= 0) return f.CachedTriangleCount;
            int count = 0;
            MeshFilter filter = f.Transform != null
                ? f.Transform.GetComponent<MeshFilter>() : null;
            Mesh mesh = filter != null ? filter.sharedMesh : null;
            if (mesh != null)
                for (int s = 0; s < mesh.subMeshCount; s++)
                    count += (int)(mesh.GetIndexCount(s) / 3);
            f.CachedTriangleCount = count;
            return count;
        }

        // ------------------------------------------------------------------ reset

        /// <summary>Idempotent full reset: transforms, velocities, renderer, collider, health
        /// and event state return to the baked values. No Instantiate/Destroy involved.</summary>
        public void ResetDestructible(ResetMode mode)
        {
            using (s_resetMarker.Auto())
            {
                ClearDynamicClusterLinks();
                m_clusterDynamicResidualOnNextBreak = false;
                // Return remaining budget slots, including settled CleanupAction.None
                // bodies that kept their slots while simulating.
                int activeBodies = m_activeCount + m_secondaryActiveCount +
                                   m_outstandingBudgetCount +
                                   m_pendingActivationIndices.Count;
                if (UsesActiveBodyBudget && activeBodies > 0)
                {
                    DestructionBudgetManager.Instance.ReleaseActivation(
                        Profile.BudgetGroup, activeBodies);
                }

                if (IntactBody != null)
                {
                    if (!IntactBody.isKinematic)
                    {
#if UNITY_6000_0_OR_NEWER
                        IntactBody.linearVelocity = Vector3.zero;
#else
                        IntactBody.velocity = Vector3.zero;
#endif
                        IntactBody.angularVelocity = Vector3.zero;
                    }
                    IntactBody.isKinematic = true;
                    IntactBody.detectCollisions = true;
                }

                // Never restore a pose that was never recorded: an editor-side reset runs
                // without Awake, and writing the uninitialised fields moved the object to
                // the origin and scaled it to nothing. Take the current pose as the
                // authoring pose instead, which is exactly what Awake would have read.
                CaptureInitialTransform();
                transform.localPosition = m_initialLocalPosition;
                transform.localRotation = m_initialLocalRotation;
                transform.localScale = m_initialLocalScale;
                m_allDebrisDespawned = false;
                FragmentDespawnTransition.Cancel(transform);
                if (!gameObject.activeSelf) gameObject.SetActive(true);

                for (int i = 0; i < Fragments.Count; i++)
                {
                    var f = Fragments[i];
                    if (f == null || f.Transform == null) continue;

                    if (f.Body != null)
                    {
                        if (!f.Body.isKinematic)
                        {
#if UNITY_6000_0_OR_NEWER
                            f.Body.linearVelocity = Vector3.zero;
#else
                            f.Body.velocity = Vector3.zero;
#endif
                            f.Body.angularVelocity = Vector3.zero;
                        }
                        f.Body.isKinematic = true;
                    }

                    f.Transform.localPosition = f.InitialLocalPosition;
                    f.Transform.localRotation = f.InitialLocalRotation;
                    f.Transform.localScale = f.InitialLocalScale;
                    if (!f.Transform.gameObject.activeSelf)
                        f.Transform.gameObject.SetActive(true);
                    FragmentDespawnTransition.Cancel(f.Transform);
                    SetFragmentProbeAnchor(f, f.AttachedProbeAnchor);
                    SetFragmentShadowMode(f, false);
                    UseExactFragmentCollider(f);

                    f.Activated = false;
                    f.DebrisDespawned = false;
                    f.Settled = false;
                    f.Detached = false;
                    f.SleepTimer = 0f;
                    f.BudgetFrozen = false;
                    f.BudgetDespawned = false;
                    // Debounces describe the run that just ended, not the restored shell.
                    f.LastDamageTime = -999f;
                    f.LastDamageSourceId = 0;
                    f.LastImpactEffectTime = -999f;
                    f.GroundGuardResolved = false;
                    f.NeedsMeshGroundGuard = false;
                    if (DeferInactiveFragmentPhysics)
                    {
                        if (f.Runtime == null)
                            f.Runtime = f.Transform.GetComponent<FragmentRuntime>();
                        if (f.Runtime != null) f.Runtime.enabled = false;
                    }
                    f.SecondaryNode?.ResetFromRoot();
                }

                if (FracturedRoot != null) FracturedRoot.SetActive(false);
                if (IntactRoot != null) IntactRoot.SetActive(true);
                m_attachedShadowProxyDirty = false;
                if (AttachedShadowProxyFilter != null)
                    AttachedShadowProxyFilter.sharedMesh = m_bakedAttachedShadowMesh;
                if (AttachedShadowProxyRenderer != null)
                    AttachedShadowProxyRenderer.enabled = true;

                m_bodyReleased = false;
                m_nextDynamicSupportCheck = 0f;
                m_dynamicUnsupportedSince = -1f;
                m_dynamicResidualLinearVelocity = Vector3.zero;
                m_dynamicResidualAngularVelocity = Vector3.zero;
                // Section baselines and the footing grid describe the shell as it stood;
                // a reset puts every fragment back, so both are rebuilt on demand.
                m_sectionTotals = null;
                m_sectionMinY = null;
                m_sectionHeight = null;
                m_sectionGroupFragments = null;
                m_sectionLaneLayouts = null;
                m_sectionFragmentList = null;
                m_sectionFragmentListCount = -1;
                m_sectionScratch = null;
                m_refinedSectionCaches = null;
                m_refinedSectionScratch = null;
                m_footingGrid = null;
                m_attachedFragmentNearQueries = null;
                m_anchorStanding = null;
                m_anchorFootingSeen = false;
                m_crossStructuralNeighbours = null;
                m_crossStructuralNeighbourFragmentCount = -1;
                m_bakedSupportComponents = null;
                StructuralNode?.ResetStructure();

                m_health = Profile != null ? Profile.Health : DestructionProfile.DefaultHealth;
                m_activeCount = 0;
                m_settledCount = 0;
                m_secondaryActiveCount = 0;
                m_secondarySettledCount = 0;
                m_outstandingBudgetCount = 0;
                m_pendingActivations.Clear();
                m_pendingActivationIndices.Clear();
                m_lastDamageTime = -999f;
                m_lastImpactSpreadTime = -999f;
                m_impactSpreadFragmentHealth.Clear();
                m_lastDamageSourceId = 0;
                m_state = DestructibleState.Intact;
                ConfigureIntactBody();

                if (mode != ResetMode.Silent)
                {
                    WasReset?.Invoke(mode);
                    OnResetEvent?.Invoke();
                }
            }
        }

        void EnsureBakedFragmentPhysics()
        {
            if (Fragments == null) return;
            for (int i = 0; i < Fragments.Count; i++)
            {
                FragmentRecord fragment = Fragments[i];
                EnsureFragmentPhysics(fragment, Profile);
                if (fragment?.SecondaryNode?.Children == null) continue;
                for (int child = 0;
                     child < fragment.SecondaryNode.Children.Count; child++)
                    EnsureFragmentPhysics(fragment.SecondaryNode.Children[child], Profile);
            }
        }

        /// <summary>
        /// Repairs legacy/partially migrated baked records without touching their mesh.
        /// Generated fragments are expected to carry these components, but a missing
        /// serialized reference must not turn visible debris into a transform-only object
        /// that can cross the floor. A local bounds box is the last-resort dynamic-safe
        /// shape; current bakes continue using their authored convex compound.
        /// </summary>
        internal static Rigidbody EnsureFragmentPhysics(FragmentRecord fragment,
            DestructionProfile profile)
        {
            if (fragment?.Transform == null) return null;
            GameObject go = fragment.Transform.gameObject;
            bool addedBody = false;
            if (fragment.Body == null)
            {
                fragment.Body = go.GetComponent<Rigidbody>();
                if (fragment.Body == null)
                {
                    fragment.Body = go.AddComponent<Rigidbody>();
                    addedBody = true;
                }
            }

            Rigidbody body = fragment.Body;
            if (addedBody)
            {
                body.mass = Mathf.Max(0.001f, fragment.Mass);
#if UNITY_6000_0_OR_NEWER
                body.linearDamping = profile != null ? profile.Drag : 0.1f;
                body.angularDamping = profile != null ? profile.AngularDrag : 0.6f;
#else
                body.drag = profile != null ? profile.Drag : 0.1f;
                body.angularDrag = profile != null ? profile.AngularDrag : 0.6f;
#endif
                body.useGravity = profile == null || profile.UseGravity;
                body.isKinematic = true;
                body.detectCollisions = true;
                body.collisionDetectionMode = profile != null
                    ? profile.FragmentCollisionDetection
                    : CollisionDetectionMode.ContinuousSpeculative;
                body.interpolation = profile != null
                    ? profile.FragmentInterpolation
                    : RigidbodyInterpolation.Interpolate;
            }

            Collider[] localColliders = go.GetComponents<Collider>();
            if (fragment.Collider == null && localColliders.Length > 0)
                fragment.Collider = localColliders[0];

            bool hasRecordedDynamic = IsDynamicSafeCollider(fragment.DynamicCollider);
            if (!hasRecordedDynamic && fragment.DynamicColliders != null)
            {
                for (int i = 0; i < fragment.DynamicColliders.Count; i++)
                    if (IsDynamicSafeCollider(fragment.DynamicColliders[i]))
                    {
                        hasRecordedDynamic = true;
                        break;
                    }
            }
            if (!hasRecordedDynamic && localColliders != null)
            {
                for (int i = 0; i < localColliders.Length; i++)
                {
                    Collider candidate = localColliders[i];
                    if (candidate == fragment.Collider ||
                        !IsDynamicSafeCollider(candidate)) continue;
                    fragment.DynamicCollider = candidate;
                    break;
                }
            }

            if (!HasDynamicSafeCollider(fragment, localColliders))
            {
                var fallback = go.AddComponent<BoxCollider>();
                MeshFilter filter = go.GetComponent<MeshFilter>();
                if (filter != null && filter.sharedMesh != null)
                {
                    fallback.center = filter.sharedMesh.bounds.center;
                    fallback.size = Vector3.Max(filter.sharedMesh.bounds.size,
                        Vector3.one * 0.001f);
                }
                else if (fragment.Renderer != null)
                {
                    Bounds world = fragment.Renderer.bounds;
                    fallback.center = fragment.Transform.InverseTransformPoint(world.center);
                    Vector3 scale = fragment.Transform.lossyScale;
                    fallback.size = new Vector3(
                        world.size.x / Mathf.Max(0.0001f, Mathf.Abs(scale.x)),
                        world.size.y / Mathf.Max(0.0001f, Mathf.Abs(scale.y)),
                        world.size.z / Mathf.Max(0.0001f, Mathf.Abs(scale.z)));
                }
                fallback.enabled = fragment.Activated;
                fragment.DynamicCollider = fallback;
            }
            return body;
        }

        static bool HasDynamicSafeCollider(FragmentRecord fragment,
            Collider[] localColliders)
        {
            if (IsDynamicSafeCollider(fragment?.DynamicCollider) ||
                IsDynamicSafeCollider(fragment?.Collider))
                return true;
            if (fragment?.DynamicColliders != null)
            {
                for (int i = 0; i < fragment.DynamicColliders.Count; i++)
                    if (IsDynamicSafeCollider(fragment.DynamicColliders[i])) return true;
            }
            if (localColliders == null) return false;
            for (int i = 0; i < localColliders.Length; i++)
                if (IsDynamicSafeCollider(localColliders[i])) return true;
            return false;
        }

        static bool IsDynamicSafeCollider(Collider collider)
        {
            if (collider == null || collider.isTrigger) return false;
            return !(collider is MeshCollider mesh) ||
                   (mesh.convex && mesh.sharedMesh != null);
        }

        static void EnsureEnabledDynamicCollider(FragmentRecord fragment)
        {
            if (fragment == null) return;
            if (IsDynamicSafeCollider(fragment.DynamicCollider) &&
                fragment.DynamicCollider.enabled) return;
            if (fragment.DynamicColliders != null)
            {
                for (int i = 0; i < fragment.DynamicColliders.Count; i++)
                {
                    Collider collider = fragment.DynamicColliders[i];
                    if (IsDynamicSafeCollider(collider) && collider.enabled) return;
                }
            }
            if (IsDynamicSafeCollider(fragment.Collider))
                fragment.Collider.enabled = true;
        }

        static void UseDynamicFragmentCollider(FragmentRecord fragment)
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

        /// <summary>
        /// Applies a conservative contact fallback without replacing a Physics Material
        /// explicitly authored by the user. Irregular convex fracture hulls otherwise land
        /// on changing edges and can keep bouncing or sliding on default project surfaces.
        /// One shared runtime material serves every generated piece and render pipeline.
        /// </summary>
        internal static void ApplyDefaultDebrisContactMaterial(
            FragmentRecord fragment)
        {
            if (fragment == null) return;
            ApplyDefaultDebrisContactMaterial(fragment.Collider);
            ApplyDefaultDebrisContactMaterial(fragment.DynamicCollider);
            if (fragment.DynamicColliders == null) return;
            for (int i = 0; i < fragment.DynamicColliders.Count; i++)
                ApplyDefaultDebrisContactMaterial(fragment.DynamicColliders[i]);
        }

        internal static void ApplyDefaultDebrisContactMaterial(Collider collider)
        {
            if (collider == null || collider.sharedMaterial != null) return;
            collider.sharedMaterial = DefaultDebrisContactMaterial;
        }

#if UNITY_6000_0_OR_NEWER
        static PhysicsMaterial DefaultDebrisContactMaterial
#else
        static PhysicMaterial DefaultDebrisContactMaterial
#endif
        {
            get
            {
                if (s_sharedPhysics.DefaultDebrisContactMaterial != null)
                    return s_sharedPhysics.DefaultDebrisContactMaterial;
#if UNITY_6000_0_OR_NEWER
                s_sharedPhysics.DefaultDebrisContactMaterial =
                    new PhysicsMaterial("Quick Destruction Debris Contact");
#else
                s_sharedPhysics.DefaultDebrisContactMaterial =
                    new PhysicMaterial("Quick Destruction Debris Contact");
#endif
                s_sharedPhysics.DefaultDebrisContactMaterial.hideFlags =
                    HideFlags.HideAndDontSave;
                s_sharedPhysics.DefaultDebrisContactMaterial.dynamicFriction = 0.65f;
                s_sharedPhysics.DefaultDebrisContactMaterial.staticFriction = 0.75f;
                s_sharedPhysics.DefaultDebrisContactMaterial.bounciness = 0f;
                s_sharedPhysics.DefaultDebrisContactMaterial.frictionCombine =
#if UNITY_6000_0_OR_NEWER
                    PhysicsMaterialCombine.Maximum;
#else
                    PhysicMaterialCombine.Maximum;
#endif
                s_sharedPhysics.DefaultDebrisContactMaterial.bounceCombine =
#if UNITY_6000_0_OR_NEWER
                    PhysicsMaterialCombine.Minimum;
#else
                    PhysicMaterialCombine.Minimum;
#endif
                return s_sharedPhysics.DefaultDebrisContactMaterial;
            }
        }

        static void UseExactFragmentCollider(FragmentRecord fragment)
        {
            if (fragment == null) return;
            SetCollidersEnabled(fragment.DynamicColliders, false);
            if (fragment.DynamicCollider != null) fragment.DynamicCollider.enabled = false;
            if (fragment.Collider != null) fragment.Collider.enabled = true;
        }

        static void SetCollidersEnabled(List<Collider> colliders, bool enabled)
        {
            if (colliders == null) return;
            for (int i = 0; i < colliders.Count; i++)
                if (colliders[i] != null) colliders[i].enabled = enabled;
        }

        void ConfigureIntactColliders(bool dynamic)
        {
            if (IntactDynamicColliders == null || IntactDynamicColliders.Count == 0)
                return;

            for (int i = 0; i < IntactExactColliders.Count; i++)
            {
                if (IntactExactColliders[i] != null)
                    IntactExactColliders[i].enabled = !dynamic;
            }
            for (int i = 0; i < IntactDynamicColliders.Count; i++)
            {
                if (IntactDynamicColliders[i] != null)
                    IntactDynamicColliders[i].enabled = dynamic;
            }
        }
    }

}
