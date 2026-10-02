using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

namespace RipVertices.QuickDestruction
{
    /// <summary>
    /// Monitors every disconnected structural element independently, while all probes that
    /// originally touched the world form one lightweight foundation group. A local wall or
    /// carried floor can still fail on its own; crossing the shared foundation threshold
    /// releases the whole construction.
    /// </summary>
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    public class StructuralSupportNode : MonoBehaviour
    {
        static readonly ProfilerMarker s_structuralGroupsMarker =
            new ProfilerMarker("QD.Support.UpdateGroups");
        static readonly ProfilerMarker s_fragmentProbePrewarmMarker =
            new ProfilerMarker("QD.Support.PrewarmFragments");

        [Header("Baked References")]
        public DestructibleRoot Root;
        public Rigidbody IntactBody;
        [Tooltip("Legacy primary collider. New bakes also store every disconnected intact collider below.")]
        public Collider ProbeCollider;
        [Tooltip("Every disconnected intact collider is probed independently, so posts, columns and other mesh islands all contribute support.")]
        public List<Collider> ProbeColliders = new List<Collider>();

        [Header("Optional Scene Supports")]
        [Tooltip("Used by Manual List and Automatic + Manual modes. Assign scene-instance destructibles such as supporting columns.")]
        public List<DestructibleRoot> ManualSupports = new List<DestructibleRoot>();

        public int InitialSupportUnits => m_initialSupportUnits;
        public int CurrentSupportUnits => m_currentSupportUnits;
        public int RequiredSupportUnits => m_requiredSupportUnits;
        public float CurrentSupportRatio => m_initialSupportUnits > 0
            ? Mathf.Clamp01((float)m_currentSupportUnits / m_initialSupportUnits)
            : 0f;
        public bool HasBaseline => m_initialized && m_initialSupportUnits > 0;
        public bool Released => m_released;

        readonly RaycastHit[] m_hits = new RaycastHit[32];
        readonly HashSet<int> m_manualIds = new HashSet<int>();
        readonly List<Vector3> m_probeOriginsLocal = new List<Vector3>(100);
        readonly List<float> m_probeAttachmentRadii = new List<float>(100);
        readonly List<int> m_probeGroupIndices = new List<int>(100);
        readonly List<bool> m_probeCovered = new List<bool>(100);
        readonly List<bool> m_probeLiveSupported = new List<bool>(100);
        // Immutable after the fractured baseline is captured. Only probes that genuinely
        // touched scene/world support belong to the shared foundation group; carried floors,
        // roofs and beams keep their own structural-group thresholds.
        readonly List<bool> m_probeUsesWorldFootingBaseline = new List<bool>(100);
        // A support ray can hit either permanent scene geometry, a runtime support whose
        // state must still be polled, or nothing. The attached shell does not move between
        // support checks, so repeating identical raycasts against the floor several times
        // per second only adds periodic main-thread spikes.
        readonly List<byte> m_probeWorldSupportKinds = new List<byte>(100);
        readonly List<Collider> m_probeWorldSupports = new List<Collider>(100);
        // What is actually holding each probe up, remembered purely so the Scene gizmo can
        // point at it. "Why is this thing still standing" is the question the overlay
        // exists to answer, and a green dot on its own cannot answer it.
        readonly List<Object> m_probeSupportSources = new List<Object>(100);
        // Authored carrier relationships between source modules are immutable while the
        // building remains attached. Resolve those relationships once instead of doing a
        // ProbeCount x SourceColliderCount ClosestPoint sweep on every support poll.
        readonly List<List<int>> m_probeCarrierGroups = new List<List<int>>(100);
        // The same relationship across object boundaries. Root + Nested Meshes bakes one
        // independent destructible per source mesh, so the material a panel stands on
        // usually belongs to a DIFFERENT root and the scan above can never see it. Resolved
        // by proximity, once, while everything is still intact: modular panels normally
        // interpenetrate the one below them, and a downward ray started inside that
        // neighbour produces no hit at all.
        readonly List<List<DestructibleRoot>> m_probeNeighbourCarriers =
            new List<List<DestructibleRoot>>(100);
        // Exact fragment/triangle membership is immutable while the fractured shell is
        // attached. Build it once at first fracture; recurring support polls only need to
        // check whether any cached member has since been activated or detached.
        readonly List<List<FragmentRecord>> m_probeFragments =
            new List<List<FragmentRecord>>(100);
        readonly List<List<FragmentRecord>> m_probeCarrierFragments =
            new List<List<FragmentRecord>>(100);
        // Samples standing where two carrier modules meet, whose material the bake could
        // only file under one of them. Grows in lockstep with the fragment probe cache.
        readonly List<bool> m_probeJunctionAmbiguous = new List<bool>(100);
        // Probe samples are appended one collider at a time. Remember each collider's
        // contiguous range so a group update does not rescan every sample in the building.
        readonly List<int> m_groupProbeStarts = new List<int>();
        readonly List<int> m_groupProbeCounts = new List<int>();
        readonly List<int> m_groupInitialUnits = new List<int>();
        readonly List<int> m_groupCurrentUnits = new List<int>();
        readonly List<int> m_groupRequiredUnits = new List<int>();
        readonly List<float> m_groupBelowSince = new List<float>();
        readonly List<bool> m_groupReleased = new List<bool>();
        // True only when this element was authored as side-attached and had no measured
        // footing below it at baseline. A stacked wall/roof that originally rested on
        // another module must keep using its probe percentage after that carrier is lost;
        // one surviving edge contact is not a replacement for its entire footing.
        readonly List<bool> m_groupUsesSideAttachmentBaseline = new List<bool>();
        // Footing on the world, not on another element of this object. These groups feed
        // the shared foundation threshold; carried floors/roofs stay local.
        readonly List<bool> m_groupGrounded = new List<bool>();

        int m_initialSupportUnits;
        int m_currentSupportUnits;
        int m_requiredSupportUnits;
        float m_belowThresholdSince = -1f;
        int m_foundationInitialUnits;
        int m_foundationCurrentUnits;
        int m_foundationRequiredUnits;
        float m_foundationBelowSince = -1f;
        // One connectivity sweep settles a failed shared foundation; polling it again
        // every support tick would re-walk the whole graph for an answer that cannot change.
        bool m_foundationSweepRun;
        float m_nextCheckTime;
        bool m_initialized;
        bool m_released;
        bool m_bakedBaselineTaken;
        // True once any probe of this element has been held up by another destructible.
        // Losing a footing that was itself destructible is a collapse, not a drop: the
        // material under the element disintegrated while it watched.
        bool m_dependsOnDestructibleSupport;
        bool m_fragmentProbeCacheBuilt;
        float m_fragmentProbeCarrierRadius = -1f;
        float m_probeAttachmentRadius = 0.05f;
        float m_worldSupportCacheDistance = -1f;
        int m_worldSupportCacheLayers = int.MinValue;

        // Shared by the neighbour-carrier resolve, which runs once per element at baseline
        // and never nests.
        static readonly Collider[] s_neighbourOverlap = new Collider[64];

        const byte WorldSupportUnknown = 0;
        const byte WorldSupportNone = 1;
        const byte WorldSupportPermanent = 2;
        const byte WorldSupportDynamic = 3;
        const int FragmentProbePrewarmBatchSize = 4;

        void OnValidate()
        {
            if (Root == null) Root = GetComponent<DestructibleRoot>();
            if (IntactBody == null) IntactBody = GetComponent<Rigidbody>();
            if (ProbeCollider == null && Root != null && Root.IntactRoot != null)
                ProbeCollider = Root.IntactRoot.GetComponentInChildren<Collider>();
            EnsureProbeColliders();
        }

        void Start()
        {
            RebuildSupportBaseline();
            if (Root != null && ProbeColliders != null && ProbeColliders.Count > 1)
                Root.PrewarmStructuralContactCache();
        }

        void FixedUpdate()
        {
            var profile = Root != null ? Root.Profile : null;
            if (profile != null && !m_released)
                ProcessFragmentProbePrewarm(profile);
            if (profile == null ||
                profile.IntactBody != IntactBodyMode.ReleaseWhenUnsupported ||
                m_released)
                return;

            if (!m_initialized)
            {
                RebuildSupportBaseline();
                return;
            }

            if (Time.fixedTime < m_nextCheckTime) return;
            m_nextCheckTime = Time.fixedTime + Mathf.Max(0.02f, profile.SupportCheckInterval);

            // The intact baseline is taken through the collider hull, which for a hollow
            // building also covers its empty middle, so 100% counted footing that never
            // existed and no realistic amount of damage could push the share below the
            // threshold. The moment the shell actually breaks the fragments are real, so
            // the baseline is retaken with exactly the rule used from then on: 100% now
            // means the footing that genuinely stands under material.
            if (!m_bakedBaselineTaken && Root != null &&
                Root.State >= DestructibleState.Fractured)
            {
                m_bakedBaselineTaken = true;
                RebuildGroupBaselinesPreservingNonZero(profile);
            }

            if (UsesAutomaticDetection(profile) && m_groupInitialUnits.Count > 0)
            {
                using (s_structuralGroupsMarker.Auto())
                    UpdateStructuralGroups(profile);
                return;
            }

            m_currentSupportUnits = CountCurrentSupportUnits(profile);

            if (m_currentSupportUnits >= m_requiredSupportUnits)
            {
                m_belowThresholdSince = -1f;
                return;
            }

            // All support timing uses Time.fixedTime because these checks run in
            // FixedUpdate; mixing clocks would make the grace window frame-rate dependent.
            if (m_belowThresholdSince < 0f)
                m_belowThresholdSince = Time.fixedTime;

            if (Time.fixedTime - m_belowThresholdSince >= profile.SupportGraceTime)
                ReleaseNow();
        }

        /// <summary>Captures the currently detected support as the new 100% baseline.</summary>
        public void RebuildSupportBaseline()
        {
            var profile = Root != null ? Root.Profile : null;
            CacheAutomaticProbeOrigins(profile);
            m_currentSupportUnits = profile != null ? CountCurrentSupportUnits(profile) : 0;
            m_initialSupportUnits = m_currentSupportUnits;
            m_requiredSupportUnits = profile != null && m_initialSupportUnits > 0
                ? Mathf.Clamp(
                    Mathf.CeilToInt(m_initialSupportUnits * profile.RequiredSupportRatio),
                    1, m_initialSupportUnits)
                // Legacy/manual whole-object mode still needs one valid support sample.
                : profile != null ? 1 : 0;
            RebuildGroupBaselines(profile);
            m_belowThresholdSince = -1f;
            m_nextCheckTime = Time.fixedTime;
            m_initialized = true;
        }

        /// <summary>
        /// Caches the intact probe geometry before DestructibleRoot hides IntactRoot.
        /// A first hit can arrive before this component's first FixedUpdate (most commonly
        /// immediately after Reset). In that case rebuilding after the visibility swap sees
        /// disabled probe colliders and produces a zero-unit baseline, which makes the
        /// affected structural group permanently exempt from its support percentage.
        /// </summary>
        internal void PrepareFracturedBaselineBeforeVisibilityChange()
        {
            if (m_bakedBaselineTaken || m_initialized || Root == null) return;
            DestructionProfile profile = Root.Profile;
            if (!UsesAutomaticDetection(profile)) return;
            RebuildSupportBaseline();
        }

        /// <summary>
        /// Re-measures the intact baked fragment shell after it becomes visible but before
        /// the first local fragment is activated. This removes empty compound-collider
        /// space from the 100% baseline without allowing the first destroyed column to
        /// disappear from that baseline. FixedUpdate used to perform the same retake one
        /// physics step too late, after the struck fragments had already left the shell.
        /// </summary>
        internal void CaptureFracturedBaselineBeforeActivation()
        {
            if (m_bakedBaselineTaken || Root == null) return;
            DestructionProfile profile = Root.Profile;
            if (!UsesAutomaticDetection(profile)) return;

            // BreakNow may legitimately be called from another component's Start before
            // this node has received its own Start callback. Build the same baseline here
            // rather than falling back to the late post-damage FixedUpdate retake.
            if (!m_initialized)
            {
                RebuildSupportBaseline();
                m_bakedBaselineTaken = true;
                return;
            }

            RebuildGroupBaselinesPreservingNonZero(profile);
            m_bakedBaselineTaken = true;
        }

        /// <summary>Immediately releases the intact body or remaining fractured shell.</summary>
        public bool ReleaseNow()
        {
            if (m_released || Root == null) return false;
            bool released;
            if (Root.State >= DestructibleState.Fractured)
                released = Root.ReleaseFracturedStructure();
            else
                // An element whose footing was other destructible material comes apart:
                // that material was destroyed under it, so arriving on the ground as one
                // untouched slab is the one outcome that cannot have happened. An element
                // that merely lost the world it stood on is still whole and simply falls.
                released = (m_dependsOnDestructibleSupport &&
                            Root.CollapseUnsupportedStructure()) ||
                           Root.ReleaseStructure();
            if (released) m_released = true;
            return released;
        }

        internal void MarkReleased()
        {
            m_released = true;
            m_belowThresholdSince = -1f;
        }

        internal void ResetStructure()
        {
            m_released = false;
            m_bakedBaselineTaken = false;
            m_initialized = false;
            m_initialSupportUnits = 0;
            m_currentSupportUnits = 0;
            m_requiredSupportUnits = 0;
            m_belowThresholdSince = -1f;
            m_nextCheckTime = 0f;
            m_probeOriginsLocal.Clear();
            m_probeAttachmentRadii.Clear();
            m_probeGroupIndices.Clear();
            m_probeCovered.Clear();
            m_probeLiveSupported.Clear();
            m_probeUsesWorldFootingBaseline.Clear();
            m_probeWorldSupportKinds.Clear();
            m_probeWorldSupports.Clear();
            m_probeSupportSources.Clear();
            m_probeCarrierGroups.Clear();
            m_probeNeighbourCarriers.Clear();
            m_dependsOnDestructibleSupport = false;
            m_worldSupportCacheDistance = -1f;
            m_worldSupportCacheLayers = int.MinValue;
            InvalidateFragmentProbeCache();
            m_groupProbeStarts.Clear();
            m_groupProbeCounts.Clear();
            m_groupInitialUnits.Clear();
            m_groupCurrentUnits.Clear();
            m_groupRequiredUnits.Clear();
            m_groupBelowSince.Clear();
            m_groupReleased.Clear();
            m_groupUsesSideAttachmentBaseline.Clear();
            m_groupGrounded.Clear();
            m_foundationInitialUnits = 0;
            m_foundationCurrentUnits = 0;
            m_foundationRequiredUnits = 0;
            m_foundationBelowSince = -1f;
            m_foundationSweepRun = false;
        }

        int CountCurrentSupportUnits(DestructionProfile profile)
        {
            int count = 0;
            bool automatic = profile.SupportDetection == SupportDetectionMode.AutomaticBelow ||
                             profile.SupportDetection == SupportDetectionMode.AutomaticAndManual;
            bool manual = profile.SupportDetection == SupportDetectionMode.ManualList ||
                          profile.SupportDetection == SupportDetectionMode.AutomaticAndManual;

            if (automatic)
            {
                if (m_probeOriginsLocal.Count == 0)
                    CacheAutomaticProbeOrigins(profile);

                bool fractured = Root != null && Root.State >= DestructibleState.Fractured;
                // Built from the baked geometry, never from whatever happened to be
                // active in the frame the object first broke. See the note at the
                // other call site.
                if (fractured) EnsureFragmentProbeCache(profile, true);
                for (int i = 0; i < m_probeOriginsLocal.Count; i++)
                {
                    Vector3 origin = transform.TransformPoint(m_probeOriginsLocal[i]);

                    // Once Intact is hidden, an old probe must only remain valid where
                    // the kinematic fragment shell still reaches its original footing.
                    // Otherwise the floor below a removed column would incorrectly be
                    // treated as if that column still existed.
                    if (fractured && !HasCachedAttachedFragment(i))
                    {
                        SetProbeState(i, false, false);
                        continue;
                    }

                    int group = i < m_probeGroupIndices.Count
                        ? m_probeGroupIndices[i]
                        : -1;
                    bool carried = HasSupportBelow(origin, profile, i) ||
                                   HasCarrierSupportBelow(origin, profile, group, i) ||
                                   HasNeighbourCarrierSupport(origin, profile, i);
                    SetProbeState(i, true, carried);
                    if (carried)
                        count++;
                }
            }

            if (manual)
            {
                m_manualIds.Clear();
                for (int i = 0; i < ManualSupports.Count; i++)
                {
                    var support = ManualSupports[i];
                    if (support == null || support == Root || !support.isActiveAndEnabled ||
                        support.IsStructurallyReleased ||
                        support.State >= DestructibleState.Released)
                        continue;

                    int id = support.GetHashCode();
                    if (m_manualIds.Add(id))
                        count++;
                }
            }

            return count;
        }

        void CacheAutomaticProbeOrigins(DestructionProfile profile)
        {
            m_probeOriginsLocal.Clear();
            m_probeAttachmentRadii.Clear();
            m_probeGroupIndices.Clear();
            m_probeCovered.Clear();
            m_probeLiveSupported.Clear();
            m_probeUsesWorldFootingBaseline.Clear();
            m_probeWorldSupportKinds.Clear();
            m_probeWorldSupports.Clear();
            m_probeSupportSources.Clear();
            m_probeCarrierGroups.Clear();
            m_probeNeighbourCarriers.Clear();
            m_dependsOnDestructibleSupport = false;
            m_worldSupportCacheDistance = -1f;
            m_worldSupportCacheLayers = int.MinValue;
            InvalidateFragmentProbeCache();
            m_groupProbeStarts.Clear();
            m_groupProbeCounts.Clear();
            if (profile == null) return;

            EnsureProbeColliders();

            int grid = GetEffectiveProbeResolution(profile.SupportProbeGrid);
            float smallestRadius = float.MaxValue;
            for (int colliderIndex = 0; colliderIndex < ProbeColliders.Count; colliderIndex++)
            {
                int groupStart = m_probeOriginsLocal.Count;
                Collider probe = ProbeColliders[colliderIndex];
                if (probe != null && probe.enabled &&
                    probe.gameObject.activeInHierarchy)
                {
                    Bounds bounds = probe.bounds;
                    GetProbePlaneMetrics(probe, bounds, out float planeU,
                        out float planeV, out float verticalLength);
                    float cellU = planeU / Mathf.Max(1, grid - 1);
                    float cellV = planeV / Mathf.Max(1, grid - 1);
                    // The sample begins slightly inside the intact collider so a ray does
                    // not immediately re-hit its own lower face. Its fragment association,
                    // however, is measured against render triangles. On a tall thin wall
                    // the old radius (30% of the 10 cm thickness cell) was 3 cm while the
                    // vertical inset was 4 cm, so 23/25 samples permanently mapped to no
                    // fragment. Those empty samples continued seeing the floor after the
                    // whole base had gone and stayed green forever. Always cover the inset
                    // as well as the authored grid cell.
                    float verticalInset = GetProbeVerticalInset(verticalLength);
                    float attachmentRadius = Mathf.Max(0.025f,
                        Mathf.Max(Mathf.Min(cellU, cellV) * 0.3f,
                            verticalInset + 0.01f));
                    float probeInset = IsBroadHorizontalSupportSurface(probe)
                        ? 0f
                        : profile.SupportProbeInset;
                    smallestRadius = Mathf.Min(smallestRadius, attachmentRadius);

                    // Each disconnected collider receives a complete probe grid. A narrow
                    // post therefore remains an independently measured support instead of
                    // disappearing inside the broad bounds of the bridge/deck collider.
                    for (int z = 0; z < grid; z++)
                    {
                        for (int x = 0; x < grid; x++)
                        {
                            Vector3 world = GetProbeOrigin(probe, bounds, grid, x, z,
                                probeInset);
                            m_probeOriginsLocal.Add(transform.InverseTransformPoint(world));
                            m_probeAttachmentRadii.Add(attachmentRadius);
                            m_probeGroupIndices.Add(colliderIndex);
                            m_probeCovered.Add(false);
                            m_probeLiveSupported.Add(false);
                            m_probeUsesWorldFootingBaseline.Add(false);
                            m_probeWorldSupportKinds.Add(WorldSupportUnknown);
                            m_probeWorldSupports.Add(null);
                            m_probeSupportSources.Add(null);
                            m_probeCarrierGroups.Add(null);
                            m_probeNeighbourCarriers.Add(null);
                        }
                    }
                }

                // Keep one range entry per collider, including temporarily disabled ones,
                // so StructuralGroup continues to map directly to ProbeColliders.
                m_groupProbeStarts.Add(groupStart);
                m_groupProbeCounts.Add(m_probeOriginsLocal.Count - groupStart);
            }

            if (smallestRadius < float.MaxValue)
                m_probeAttachmentRadius = smallestRadius;

            BuildProbeCarrierGroupCache(profile);
            BuildProbeNeighbourCarrierCache(profile);
        }

        void BuildProbeCarrierGroupCache(DestructionProfile profile)
        {
            if (profile == null || ProbeColliders == null ||
                ProbeColliders.Count < 2)
                return;

            float distance = Mathf.Max(0.01f, profile.SupportProbeDistance);
            float distanceSqr = distance * distance;
            for (int probeIndex = 0; probeIndex < m_probeOriginsLocal.Count;
                 probeIndex++)
            {
                int group = probeIndex < m_probeGroupIndices.Count
                    ? m_probeGroupIndices[probeIndex]
                    : -1;
                if (group < 0) continue;

                Vector3 origin = transform.TransformPoint(
                    m_probeOriginsLocal[probeIndex]);
                List<int> carriers = null;
                for (int colliderIndex = 0;
                     colliderIndex < ProbeColliders.Count; colliderIndex++)
                {
                    if (colliderIndex == group) continue;
                    Collider other = ProbeColliders[colliderIndex];
                    if (other == null) continue;

                    bool exact = other is MeshCollider meshCollider &&
                                 !meshCollider.convex;
                    Vector3 closest = exact
                        ? other.bounds.ClosestPoint(origin)
                        : other.ClosestPoint(origin);
                    if ((closest - origin).sqrMagnitude > distanceSqr ||
                        !DestructibleRoot.CarriesFooting(origin, closest))
                        continue;

                    if (carriers == null) carriers = new List<int>(2);
                    carriers.Add(colliderIndex);
                }
                m_probeCarrierGroups[probeIndex] = carriers;
            }
        }

        /// <summary>
        /// Resolves which OTHER destructibles stand under each probe, once, while the whole
        /// scene is still intact.
        ///
        /// <see cref="HasCarrierSupportBelow"/> answers the same question inside one baked
        /// object and stops at its own probe colliders, which is everything a Whole Building
        /// bake needs. Root + Nested Meshes deliberately produces one independent
        /// destructible per source mesh instead, so a stacked panel's entire footing belongs
        /// to a neighbouring root. A downward ray finds that neighbour only where the two
        /// meshes meet cleanly; modular panels normally overlap, and a ray started inside
        /// the panel below returns nothing at all, which left the upper panel with a zero
        /// baseline - permanently exempt from support monitoring, and still hanging in the
        /// air once the panel under it was destroyed. Proximity answers both layouts.
        /// </summary>
        void BuildProbeNeighbourCarrierCache(DestructionProfile profile)
        {
            if (profile == null || Root == null || ProbeColliders == null) return;

            float distance = Mathf.Max(0.01f, profile.SupportProbeDistance);
            float distanceSqr = distance * distance;
            for (int probeIndex = 0; probeIndex < m_probeOriginsLocal.Count;
                 probeIndex++)
            {
                int group = probeIndex < m_probeGroupIndices.Count
                    ? m_probeGroupIndices[probeIndex]
                    : -1;
                Collider owner = group >= 0 && group < ProbeColliders.Count
                    ? ProbeColliders[group]
                    : null;
                if (owner == null) continue;

                float ownerBaseY = owner.bounds.min.y;
                Vector3 origin = transform.TransformPoint(
                    m_probeOriginsLocal[probeIndex]);
                int hits = Physics.OverlapSphereNonAlloc(origin, distance,
                    s_neighbourOverlap, profile.SupportLayers,
                    QueryTriggerInteraction.Ignore);
                List<DestructibleRoot> carriers = null;
                for (int i = 0; i < hits; i++)
                {
                    Collider candidate = s_neighbourOverlap[i];
                    if (candidate == null || !candidate.enabled ||
                        !candidate.gameObject.activeInHierarchy ||
                        candidate.transform == transform ||
                        candidate.transform.IsChildOf(transform))
                        continue;
                    // Loose debris and rolling props hold nothing up.
                    Rigidbody body = candidate.attachedRigidbody;
                    if (body != null && !body.isKinematic) continue;
                    // Static scene geometry is the raycast's business, not this one's.
                    var carrier = candidate.GetComponentInParent<DestructibleRoot>();
                    if (carrier == null || carrier == Root ||
                        carrier.IsStructurallyReleased ||
                        carrier.State >= DestructibleState.Released)
                        continue;
                    if (carriers != null && carriers.Contains(carrier)) continue;
                    // Reaching lower than this element is what makes the neighbour a
                    // carrier, and it is the whole cycle guard: min.y is a total order, so
                    // of any two objects at most one can reach lower than the other and
                    // they can never hold each other up. Two panels sharing a base height
                    // carry each other not at all, which is correct.
                    //
                    // This deliberately replaces the 45 degree cone CarriesFooting applies
                    // inside a single object. There the cone IS the cycle guard and has to
                    // stay. Here it would throw away the commonest modular joint there is:
                    // a slab let into the face of a wall is held from the side, its contact
                    // sits beside the probe rather than under it, and the cone rejected
                    // every sample of it — which is why a balcony went on hanging in the
                    // air after the wall carrying it had collapsed.
                    if (candidate.bounds.min.y >= ownerBaseY - 0.001f) continue;
                    // A non-convex MeshCollider returns the query point from ClosestPoint,
                    // which would make every probe look supported forever.
                    bool exact = candidate is MeshCollider mesh && !mesh.convex;
                    Vector3 closest = exact
                        ? candidate.bounds.ClosestPoint(origin)
                        : candidate.ClosestPoint(origin);
                    if ((closest - origin).sqrMagnitude > distanceSqr) continue;

                    if (carriers == null) carriers = new List<DestructibleRoot>(2);
                    carriers.Add(carrier);
                }

                if (carriers == null) continue;
                if (probeIndex < m_probeNeighbourCarriers.Count)
                    m_probeNeighbourCarriers[probeIndex] = carriers;
                m_dependsOnDestructibleSupport = true;
            }
        }

        /// <summary>
        /// Whether any probe of this element was resolved as carried by another
        /// destructible when the baseline was taken.
        /// </summary>
        bool GroupHadNeighbourCarrier(int group)
        {
            GetProbeRange(group, out int probeStart, out int probeEnd);
            for (int i = probeStart;
                 i < probeEnd && i < m_probeNeighbourCarriers.Count; i++)
            {
                List<DestructibleRoot> carriers = m_probeNeighbourCarriers[i];
                if (carriers != null && carriers.Count > 0) return true;
            }
            return false;
        }

        /// <summary>
        /// Whether the neighbouring destructibles resolved for this probe are still holding
        /// it up. An intact neighbour is whole; a fractured one carries only where its shell
        /// is still attached, which is exactly the rule this object applies to its own
        /// modules.
        /// </summary>
        bool HasNeighbourCarrierSupport(Vector3 origin, DestructionProfile profile,
            int probeIndex)
        {
            if (probeIndex < 0 || probeIndex >= m_probeNeighbourCarriers.Count)
                return false;
            List<DestructibleRoot> carriers = m_probeNeighbourCarriers[probeIndex];
            if (carriers == null) return false;

            float reach = Mathf.Max(0.01f, profile.SupportProbeDistance);
            for (int i = 0; i < carriers.Count; i++)
            {
                DestructibleRoot carrier = carriers[i];
                // Mind the enum: Released is 3 and Fractured is 4, so the `>= Released`
                // shorthand used elsewhere also swallows every broken-but-standing
                // neighbour. Here the two must be told apart — a wall with a hole in it is
                // still a wall.
                if (carrier == null || !carrier.isActiveAndEnabled ||
                    carrier.IsStructurallyReleased ||
                    carrier.State == DestructibleState.Released ||
                    carrier.State == DestructibleState.Recycled)
                    continue;
                if (carrier.State < DestructibleState.Released)
                {
                    RecordProbeSupportSource(probeIndex, carrier);
                    return true;
                }
                // Attached material near the joint, judged by the rule the resolve used:
                // proximity, not a downward cone. HasAttachedFragmentSupportNear applies
                // the cone and would drop a slab the moment one hole was shot in the wall
                // holding it.
                if (!carrier.HasAttachedFragmentNear(origin, reach)) continue;
                RecordProbeSupportSource(probeIndex, carrier);
                return true;
            }
            return false;
        }

        void InvalidateFragmentProbeCache()
        {
            m_probeFragments.Clear();
            m_probeCarrierFragments.Clear();
            m_probeJunctionAmbiguous.Clear();
            m_fragmentProbeCacheBuilt = false;
            m_fragmentProbeCarrierRadius = -1f;
        }

        void EnsureFragmentProbeCache(DestructionProfile profile,
            bool includeInactive = false)
        {
            float carrierRadius = profile != null
                ? Mathf.Max(0.01f, profile.SupportProbeDistance)
                : 0.01f;
            if (m_fragmentProbeCacheBuilt &&
                m_probeFragments.Count == m_probeOriginsLocal.Count &&
                m_probeCarrierFragments.Count == m_probeOriginsLocal.Count &&
                Mathf.Approximately(m_fragmentProbeCarrierRadius, carrierRadius))
                return;

            bool compatiblePartial = !m_fragmentProbeCacheBuilt &&
                m_probeFragments.Count == m_probeCarrierFragments.Count &&
                m_probeFragments.Count <= m_probeOriginsLocal.Count &&
                (m_probeFragments.Count == 0 ||
                 Mathf.Approximately(m_fragmentProbeCarrierRadius, carrierRadius));
            if (!compatiblePartial)
                InvalidateFragmentProbeCache();
            if (Root == null ||
                (!includeInactive && Root.State < DestructibleState.Fractured))
                return;

            if (m_probeFragments.Count == 0)
                m_fragmentProbeCarrierRadius = carrierRadius;
            while (m_probeFragments.Count < m_probeOriginsLocal.Count)
                AppendFragmentProbeCacheEntry(profile,
                    m_probeFragments.Count, includeInactive);

            m_fragmentProbeCarrierRadius = carrierRadius;
            m_fragmentProbeCacheBuilt = true;
        }

        void ProcessFragmentProbePrewarm(DestructionProfile profile)
        {
            if (Root == null || Root.State >= DestructibleState.Fractured ||
                Root.Fragments == null || Root.Fragments.Count < 160 ||
                profile.IntactBody != IntactBodyMode.ReleaseWhenUnsupported ||
                !UsesAutomaticDetection(profile) ||
                m_probeOriginsLocal.Count == 0 || m_fragmentProbeCacheBuilt)
                return;

            float carrierRadius = Mathf.Max(0.01f,
                profile.SupportProbeDistance);
            bool compatiblePartial =
                m_probeFragments.Count == m_probeCarrierFragments.Count &&
                m_probeFragments.Count <= m_probeOriginsLocal.Count &&
                (m_probeFragments.Count == 0 ||
                 Mathf.Approximately(m_fragmentProbeCarrierRadius, carrierRadius));
            if (!compatiblePartial)
                InvalidateFragmentProbeCache();
            if (m_probeFragments.Count == 0)
                m_fragmentProbeCarrierRadius = carrierRadius;

            using (s_fragmentProbePrewarmMarker.Auto())
            {
                int target = Mathf.Min(m_probeOriginsLocal.Count,
                    m_probeFragments.Count + FragmentProbePrewarmBatchSize);
                while (m_probeFragments.Count < target)
                    AppendFragmentProbeCacheEntry(profile,
                        m_probeFragments.Count, true);
            }

            if (m_probeFragments.Count >= m_probeOriginsLocal.Count)
            {
                m_fragmentProbeCarrierRadius = carrierRadius;
                m_fragmentProbeCacheBuilt = true;
            }
        }

        void AppendFragmentProbeCacheEntry(DestructionProfile profile,
            int index, bool includeInactive)
        {
            var covering = new List<FragmentRecord>(4);
            var carriers = new List<FragmentRecord>(4);
            int group = index < m_probeGroupIndices.Count
                ? m_probeGroupIndices[index]
                : -1;
            float coverageRadius = index < m_probeAttachmentRadii.Count
                ? m_probeAttachmentRadii[index]
                : m_probeAttachmentRadius;
            Vector3 origin = transform.TransformPoint(m_probeOriginsLocal[index]);
            float carrierReach = Mathf.Max(0.01f, profile.SupportProbeDistance);
            Root.CollectAttachedSupportFragments(origin, coverageRadius, carrierReach,
                group, covering, carriers, includeInactive);
            m_probeFragments.Add(covering);
            m_probeCarrierFragments.Add(carriers);
            m_probeJunctionAmbiguous.Add(ResolveJunctionAmbiguity(index, carriers));
        }

        /// <summary>
        /// True when the authored relationship names several modules as carrying this
        /// sample but the material actually found under it belongs to fewer of them.
        ///
        /// A sample on the corner where two walls meet is exactly that case: the corner
        /// column is one piece of material and the bake files it under a single module, so
        /// the sample silently becomes load-bearing for that one wall. Destroying it then
        /// removed a support the other wall was equally providing, which is why two
        /// identical floors answered differently to the same damage — the upper one owned
        /// such a corner sample, the lower one did not. A sample that cannot be attributed
        /// fairly must not decide the outcome, so it leaves the measurement entirely:
        /// <see cref="CountGroupSupportUnitsCore"/> drops it from the count, and because
        /// the baseline runs through the same method it leaves the denominator with it.
        /// </summary>
        bool ResolveJunctionAmbiguity(int index, List<FragmentRecord> carriers)
        {
            if (index < 0 || index >= m_probeCarrierGroups.Count) return false;
            List<int> authored = m_probeCarrierGroups[index];
            if (authored == null || authored.Count < 2) return false;

            for (int i = 0; i < authored.Count; i++)
            {
                bool represented = false;
                for (int c = 0; carriers != null && c < carriers.Count; c++)
                {
                    if (carriers[c] == null ||
                        carriers[c].StructuralGroup != authored[i])
                        continue;
                    represented = true;
                    break;
                }
                if (!represented) return true;
            }
            return false;
        }

        bool IsAmbiguousJunctionProbe(int index)
        {
            return index >= 0 && index < m_probeJunctionAmbiguous.Count &&
                   m_probeJunctionAmbiguous[index];
        }

        static bool IsCachedFragmentAttached(FragmentRecord fragment)
        {
            return fragment != null && !fragment.Activated && !fragment.Detached &&
                   fragment.Transform != null &&
                   fragment.Transform.gameObject.activeInHierarchy;
        }

        /// <summary>
        /// True when the probe-to-fragment map actually holds entries for this probe. An
        /// empty list means the map never resolved anything there, which is a gap in the
        /// data rather than an answer about support.
        /// </summary>
        bool HasAnyCachedFragment(int probeIndex)
        {
            return m_fragmentProbeCacheBuilt && probeIndex >= 0 &&
                   probeIndex < m_probeFragments.Count &&
                   m_probeFragments[probeIndex] != null &&
                   m_probeFragments[probeIndex].Count > 0;
        }

        bool HasCachedAttachedFragment(int probeIndex)
        {
            if (!m_fragmentProbeCacheBuilt || probeIndex < 0 ||
                probeIndex >= m_probeFragments.Count)
                return false;
            List<FragmentRecord> fragments = m_probeFragments[probeIndex];
            for (int i = 0; i < fragments.Count; i++)
            {
                if (IsCachedFragmentAttached(fragments[i])) return true;
            }
            return false;
        }

        bool HasCachedCarrierFragment(int probeIndex)
        {
            if (!m_fragmentProbeCacheBuilt || probeIndex < 0 ||
                probeIndex >= m_probeCarrierFragments.Count)
                return false;
            List<FragmentRecord> fragments = m_probeCarrierFragments[probeIndex];
            for (int i = 0; i < fragments.Count; i++)
            {
                if (IsCachedFragmentAttached(fragments[i])) return true;
            }
            return false;
        }

        void GetProbeRange(int group, out int start, out int end)
        {
            if (group >= 0 && group < m_groupProbeStarts.Count &&
                group < m_groupProbeCounts.Count)
            {
                start = Mathf.Clamp(m_groupProbeStarts[group], 0,
                    m_probeOriginsLocal.Count);
                end = Mathf.Clamp(start + m_groupProbeCounts[group], start,
                    m_probeOriginsLocal.Count);
                return;
            }

            start = 0;
            end = m_probeOriginsLocal.Count;
        }

        void SetProbeState(int index, bool covered, bool supported)
        {
            if (index >= 0 && index < m_probeCovered.Count)
                m_probeCovered[index] = covered;
            if (index >= 0 && index < m_probeLiveSupported.Count)
                m_probeLiveSupported[index] = supported;
            if (!supported && index >= 0 && index < m_probeSupportSources.Count)
                m_probeSupportSources[index] = null;
        }

        void RecordProbeSupportSource(int index, Object source)
        {
            if (index >= 0 && index < m_probeSupportSources.Count)
                m_probeSupportSources[index] = source;
        }

        static bool UsesAutomaticDetection(DestructionProfile profile)
        {
            return profile != null &&
                   (profile.SupportDetection == SupportDetectionMode.AutomaticBelow ||
                    profile.SupportDetection == SupportDetectionMode.AutomaticAndManual);
        }

        public static int GetEffectiveProbeResolution(int quality)
        {
            // The profile stores a compact 2..5 quality setting. Odd resolutions retain
            // a stable center sample while reducing the threshold quantization from
            // 25%/11% steps to 11%/4%/2%/1% steps across the available settings.
            return Mathf.Clamp(quality, 2, 5) * 2 - 1;
        }

        void RebuildGroupBaselines(DestructionProfile profile)
        {
            m_groupInitialUnits.Clear();
            m_groupCurrentUnits.Clear();
            m_groupRequiredUnits.Clear();
            m_groupBelowSince.Clear();
            m_groupReleased.Clear();
            m_groupUsesSideAttachmentBaseline.Clear();
            m_groupGrounded.Clear();
            m_foundationInitialUnits = 0;
            m_foundationCurrentUnits = 0;
            m_foundationRequiredUnits = 0;
            m_foundationBelowSince = -1f;
            m_foundationSweepRun = false;
            for (int i = 0; i < m_probeUsesWorldFootingBaseline.Count; i++)
                m_probeUsesWorldFootingBaseline[i] = false;
            if (!UsesAutomaticDetection(profile)) return;

            int groups = ProbeColliders != null ? ProbeColliders.Count : 0;
            for (int group = 0; group < groups; group++)
            {
                int current = CountGroupSupportUnitsCore(profile, group, true,
                    out bool usedSideAttachment);
                m_groupInitialUnits.Add(current);
                m_groupCurrentUnits.Add(current);
                m_groupRequiredUnits.Add(current > 0
                    ? Mathf.Clamp(Mathf.CeilToInt(
                        current * profile.RequiredSupportRatio), 1, current)
                    : 0);
                m_groupBelowSince.Add(-1f);
                m_groupReleased.Add(false);
                m_groupUsesSideAttachmentBaseline.Add(usedSideAttachment);
                int foundationUnits = CaptureFoundationProbeBaseline(profile, group);
                m_groupGrounded.Add(foundationUnits > 0);
                m_foundationInitialUnits += foundationUnits;
            }

            m_foundationCurrentUnits = m_foundationInitialUnits;
            m_foundationRequiredUnits = CalculateRequiredSupportUnits(
                m_foundationInitialUnits, profile.RequiredSupportRatio);
            SyncReportedSupportTotals();
        }

        void RebuildGroupBaselinesPreservingNonZero(DestructionProfile profile)
        {
            var previousInitial = new List<int>(m_groupInitialUnits);
            var previousFoundationProbes =
                new List<bool>(m_probeUsesWorldFootingBaseline);
            int previousFoundationInitial = m_foundationInitialUnits;
            int previousFoundationCurrent = m_foundationCurrentUnits;

            RebuildGroupBaselines(profile);

            // A retake that finds nothing must never replace a working baseline with zero:
            // zero exempts that group from monitoring. Preserve only the missing entries;
            // every successfully remeasured group keeps its more accurate fractured-shell
            // denominator.
            for (int group = 0; group < m_groupInitialUnits.Count &&
                                    group < previousInitial.Count; group++)
            {
                if (m_groupInitialUnits[group] > 0 || previousInitial[group] <= 0)
                    continue;
                m_groupInitialUnits[group] = previousInitial[group];
                m_groupRequiredUnits[group] = CalculateRequiredSupportUnits(
                    previousInitial[group], profile.RequiredSupportRatio);
            }

            if (m_foundationInitialUnits <= 0 && previousFoundationInitial > 0 &&
                previousFoundationProbes.Count == m_probeUsesWorldFootingBaseline.Count)
            {
                for (int i = 0; i < previousFoundationProbes.Count; i++)
                    m_probeUsesWorldFootingBaseline[i] = previousFoundationProbes[i];
                m_foundationInitialUnits = previousFoundationInitial;
                m_foundationCurrentUnits = Mathf.Clamp(previousFoundationCurrent,
                    0, previousFoundationInitial);
                m_foundationRequiredUnits = CalculateRequiredSupportUnits(
                    previousFoundationInitial, profile.RequiredSupportRatio);
            }

            SyncReportedSupportTotals();
        }

        int CaptureFoundationProbeBaseline(DestructionProfile profile, int group)
        {
            int count = 0;
            GetProbeRange(group, out int probeStart, out int probeEnd);
            for (int i = probeStart; i < probeEnd; i++)
            {
                bool covered = i < m_probeCovered.Count && m_probeCovered[i];
                bool worldFooting = covered && HasSupportBelow(
                    transform.TransformPoint(m_probeOriginsLocal[i]), profile, i);
                if (i < m_probeUsesWorldFootingBaseline.Count)
                    m_probeUsesWorldFootingBaseline[i] = worldFooting;
                if (worldFooting) count++;
            }
            return count;
        }

        int CountCurrentFoundationUnits(DestructionProfile profile)
        {
            if (m_foundationInitialUnits <= 0) return 0;
            bool fractured = Root != null && Root.State >= DestructibleState.Fractured;
            if (fractured) EnsureFragmentProbeCache(profile, true);

            int count = 0;
            for (int i = 0; i < m_probeUsesWorldFootingBaseline.Count; i++)
            {
                if (!m_probeUsesWorldFootingBaseline[i]) continue;
                if (fractured && !HasCachedAttachedFragment(i)) continue;
                Vector3 origin = transform.TransformPoint(m_probeOriginsLocal[i]);
                // The neighbour test belongs here as well as in the group count. A probe
                // joins this set because a ray found something under it, and when that
                // something is another destructible the ray stops answering the moment it
                // fractures: the Intact collider is swapped out and the shell that replaced
                // it overlaps the probe instead of sitting cleanly beneath it. The
                // foundation then failed on a neighbour that was merely damaged, and
                // because it is evaluated before the group rules it got the first word.
                if (HasSupportBelow(origin, profile, i) ||
                    HasNeighbourCarrierSupport(origin, profile, i))
                    count++;
            }
            return count;
        }

        static int CalculateRequiredSupportUnits(int initial, float ratio)
        {
            return initial > 0
                ? Mathf.Clamp(Mathf.CeilToInt(initial * Mathf.Clamp01(ratio)), 1, initial)
                : 0;
        }

        internal static bool FoundationSupportIsBelowThreshold(int initial, int current,
            float requiredRatio)
        {
            return initial > 0 && current < CalculateRequiredSupportUnits(
                initial, requiredRatio);
        }

        bool UpdateFoundationGroup(DestructionProfile profile)
        {
            if (m_foundationInitialUnits <= 0) return false;
            m_foundationCurrentUnits = CountCurrentFoundationUnits(profile);
            SyncReportedSupportTotals();

            if (!FoundationSupportIsBelowThreshold(m_foundationInitialUnits,
                    m_foundationCurrentUnits, profile.RequiredSupportRatio))
            {
                m_foundationBelowSince = -1f;
                return false;
            }

            if (m_foundationBelowSince < 0f)
                m_foundationBelowSince = Time.fixedTime;
            if (Time.fixedTime - m_foundationBelowSince < profile.SupportGraceTime)
                return false;

            // The shared foundation describes ONE construction resting on many footings, and
            // a blanket release is only honest when that is what the object is. A bridge or
            // a fence is a single root holding several independent legs: destroying three of
            // four posts drops the total below the threshold, yet nothing physically
            // connects the fourth to the ones that went, and it was still standing on its
            // own footing when the whole object was activated.
            //
            // The connectivity sweep answers exactly that question and is already the
            // fallback used when the last grounded element is released. Material that kept a
            // route to the ground stays; a genuine total collapse still takes everything,
            // because once every footing is gone there is nothing left to seed the search.
            if (Root != null && Root.HasBakedSupportConnectivity &&
                Root.State >= DestructibleState.Fractured)
            {
                // Foundation units only ever decrease, so one sweep settles it. New damage
                // strands new islands through the event-driven sweep, not through this poll.
                if (m_foundationSweepRun) return false;
                m_foundationSweepRun = true;
                Root.ReleaseUnsupportedIslandsNow();
                return false;
            }

            // No baked graph: a self-supporting leg cannot be told from a stranded one, so
            // the blanket release remains the only honest answer.
            return ReleaseNow();
        }

        void SyncReportedSupportTotals()
        {
            if (m_foundationInitialUnits > 0)
            {
                m_initialSupportUnits = m_foundationInitialUnits;
                m_currentSupportUnits = m_foundationCurrentUnits;
                m_requiredSupportUnits = m_foundationRequiredUnits;
                return;
            }

            int initial = 0;
            int current = 0;
            int required = 0;
            for (int group = 0; group < m_groupInitialUnits.Count; group++)
            {
                initial += m_groupInitialUnits[group];
                if (group < m_groupCurrentUnits.Count)
                    current += m_groupCurrentUnits[group];
                if (group < m_groupRequiredUnits.Count)
                    required += m_groupRequiredUnits[group];
            }
            m_initialSupportUnits = initial;
            m_currentSupportUnits = current;
            m_requiredSupportUnits = required;
        }

        void UpdateStructuralGroups(DestructionProfile profile)
        {
            int totalInitial = 0;
            int totalCurrent = 0;
            int totalRequired = 0;
            int groundedGroups = 0;
            int releasedGroundedGroups = 0;
            bool needsConnectivitySweep = false;

            // World footings from every wall/post share one aggregate threshold. This is
            // deliberately evaluated before local group failures: losing enough total
            // foundation is an explicit reason to collapse the whole building, while probes
            // carried by another module (floors/roofs) never enter this total.
            if (UpdateFoundationGroup(profile)) return;

            for (int group = 0; group < m_groupInitialUnits.Count; group++)
            {
                int initial = m_groupInitialUnits[group];
                int failedBand = -1;
                // A carried roof or deck is monitored like any other element, but it must
                // not count as a leg of the construction: releasing it would otherwise
                // read as "the last grounded element is gone" and drop everything else.
                bool grounded = group >= m_groupGrounded.Count || m_groupGrounded[group];
                // The height-section rule describes a standing element that can be cut
                // through at some height. A carried floor or roof has no vertical load path
                // of its own: its bands measure the few centimetres of its own thickness,
                // plus whatever oversized corner fragments a Whole Building fracture pushed
                // into it. Four destroyed pieces then read as a severed section — measured
                // 0.06 on a floor slab from a single hit — and ReleaseGroupAboveSection
                // takes everything above the cut, which for a flat slab is the whole floor.
                // A carried element fails the way it physically fails: by losing the
                // carriers under it, which its own probe threshold already measures.
                bool sectionFailed = grounded && Root != null &&
                    Root.GetGroupWeakestSectionRatio(group, out failedBand) <
                        profile.RequiredSupportRatio;
                if (initial <= 0)
                {
                    // Carried element, or one whose footing the probes never saw. There are
                    // two independent reasons for it to come down, and only the first was
                    // ever checked: its own thinnest section gave way, OR whatever was
                    // carrying it is gone. Nothing re-examined the carrier, so a piece of
                    // perfectly intact material kept hanging in the air after the column
                    // beneath it had been destroyed — its own section had nothing to report,
                    // because the fault was never in that piece to begin with.
                    int carriedCurrent = Root != null
                        ? CountGroupSupportUnits(profile, group) : 0;
                    // Inside one object a carried module cannot lose its carrier while the
                    // object is still whole: the carrier is made of the same fragments, so
                    // something must have broken first. That is no longer true once the
                    // carrier is a DIFFERENT destructible, which can be demolished while
                    // this one has not been touched at all. Requiring a fracture here left
                    // an untouched slab hanging over a room whose walls were gone.
                    bool lostCarrier = Root != null && carriedCurrent <= 0 &&
                        (Root.State >= DestructibleState.Fractured ||
                         GroupHadNeighbourCarrier(group));
                    // A carried element can also lose only part of what holds it up. That
                    // strands the pieces above whatever went, which is a connectivity
                    // question rather than a whole-group one.
                    if (group < m_groupCurrentUnits.Count)
                    {
                        if (carriedCurrent < m_groupCurrentUnits[group])
                            needsConnectivitySweep = true;
                        m_groupCurrentUnits[group] = carriedCurrent;
                    }
                    if (!m_groupReleased[group] && (sectionFailed || lostCarrier) &&
                        BelowLongEnough(group, profile))
                    {
                        Collider carried = group < ProbeColliders.Count
                            ? ProbeColliders[group]
                            : null;
                        if (Root.ReleaseStructuralGroup(group, carried != null
                                ? carried.bounds.center
                                : transform.position))
                            m_groupReleased[group] = true;
                        else if (EscalateSingleElementRelease())
                            m_groupReleased[group] = true;
                    }
                    continue;
                }

                if (grounded) groundedGroups++;
                totalInitial += initial;
                totalRequired += m_groupRequiredUnits[group];
                if (m_groupReleased[group])
                {
                    if (grounded) releasedGroundedGroups++;
                    continue;
                }

                int current = CountGroupSupportUnits(profile, group);
                // A decreasing probe count is not itself a collapse condition. Running the
                // graph sweep here bypassed RequiredSupportRatio: one local Whole Building
                // hit could detach every island whose only route happened to cross the hit,
                // even while 94% of this group's authored support remained. Connectivity is
                // evaluated after the configured threshold/section failure below, so the
                // percentage and grace period remain authoritative.
                m_groupCurrentUnits[group] = current;
                totalCurrent += current;
                // Footing and thinnest section are two independent ways to lose a
                // structure: the base can be shot out from under it, or it can be cut
                // through at any height. Either one brings it down.
                if (current >= m_groupRequiredUnits[group] && !sectionFailed)
                {
                    m_groupBelowSince[group] = -1f;
                    continue;
                }

                if (!BelowLongEnough(group, profile)) continue;

                Collider probe = group < ProbeColliders.Count
                    ? ProbeColliders[group]
                    : null;
                Vector3 point = probe != null ? probe.bounds.center : transform.position;

                // A severed section and a lost footing are different failures and deserve
                // different answers. If the footing still holds, only what stood above the
                // cut comes down; the base is still supported and stays where it is. That
                // keeps a blow to the top of a tower from levelling the whole tower, while
                // a base shot out from under it still drops everything as before.
                bool footingHolds = current >= m_groupRequiredUnits[group];
                if (footingHolds && sectionFailed && failedBand >= 0)
                {
                    // A connectivity graph answers only whether ANY path remains. It cannot
                    // decide whether a thin strip is strong enough to carry the full sheet
                    // above it. Once this group's authored section ratio has failed (and the
                    // grace time elapsed), that percentage is authoritative at every height:
                    // release the material crossing/resting above the failed slice, then use
                    // connectivity to find secondary islands. This is deliberately scoped to
                    // the same structural group, so another wall/module can never be woken by
                    // a coincident fragment index or a nearby cut.
                    if (Root.ReleaseGroupAboveSection(group, failedBand, point) > 0)
                        m_groupBelowSince[group] = -1f;
                    continue;
                }

                // The authored probe percentage is authoritative after the grace period.
                // A connectivity graph answers only whether ANY path survives; letting it
                // override the percentage allowed one corner shard to hold an entire floor
                // (1/8 probes in a reproduced Whole Building case) and a narrow base strip
                // to hold a wall (10/25 probes with 13 required). Maximum Unsupported Span
                // cannot be the correctness gate either because zero intentionally disables
                // that optional shaping rule. Release exactly this failed source element;
                // ReleaseStructuralGroup then performs the ordinary dependency sweep so a
                // genuinely carried neighbour can react without mixing group indices.
                if (!Root.ReleaseStructuralGroup(group, point))
                {
                    // The group index matched no material. On a one-element destructible
                    // "this group" and "this object" are the same thing, so ask for the
                    // whole element rather than leaving it standing on nothing. A building
                    // with several modules is left alone: one module whose indices do not
                    // line up is no reason to drop the other four.
                    if (EscalateSingleElementRelease())
                    {
                        m_groupReleased[group] = true;
                        if (grounded) releasedGroundedGroups++;
                    }
                }
                else
                {
                    m_groupReleased[group] = true;
                    // Carried beams, boards and roofs are structural groups too, but
                    // releasing one of them must not pretend that another footing was
                    // removed. Otherwise three failed fence boards can satisfy the
                    // "three grounded posts released" condition and drop the posts that
                    // are still standing on the floor.
                    if (grounded) releasedGroundedGroups++;
                }
            }

            if (m_foundationInitialUnits <= 0)
            {
                m_initialSupportUnits = totalInitial;
                m_currentSupportUnits = totalCurrent;
                m_requiredSupportUnits = totalRequired;
            }

            // Once the final grounded element is gone, no carried deck/roof can remain.
            if (groundedGroups > 0 && releasedGroundedGroups >= groundedGroups)
            {
                // ReleaseFracturedStructure activates every remaining fragment of the whole
                // object and sets the released flag, which makes ActivateAllOnFirstBreak
                // report true for the entire root. That is right for a single prop, whose
                // last footing really did carry all of it. In Whole Building one root holds
                // every module, and only the modules reaching the building's lowest point
                // are ever counted as grounded — often just one. Losing that one wall then
                // activated every fragment of every other module in the same frame: one hit,
                // whole building. With a baked neighbour graph the sweep can tell which
                // material actually lost its route to the ground, and a genuine total
                // collapse still releases everything because nothing is left to seed it.
                if (Root.HasBakedSupportConnectivity)
                {
                    Root.ReleaseUnsupportedIslandsNow();
                }
                else
                {
                    // No graph: a carried piece cannot be told from an unsupported one, so
                    // the blanket release remains the only honest answer.
                    Root.ReleaseFracturedStructure();
                    m_released = true;
                }
            }
            else if (needsConnectivitySweep)
            {
                Root.ReleaseUnsupportedIslandsNow();
            }
        }

        /// <summary>
        /// Last resort for a destructible made of a single structural element whose
        /// group-scoped release found nothing to release. The element has been measured as
        /// unsupported for the whole grace period, so it comes down as one element.
        /// </summary>
        bool EscalateSingleElementRelease()
        {
            return m_groupInitialUnits.Count == 1 && ReleaseNow();
        }

        /// <summary>
        /// Starts this element's collapse timer on the first call and reports whether the
        /// configured delay has elapsed. Keeps the one-frame contact gaps out.
        /// </summary>
        bool BelowLongEnough(int group, DestructionProfile profile)
        {
            if (m_groupBelowSince[group] < 0f)
                m_groupBelowSince[group] = Time.fixedTime;
            return Time.fixedTime - m_groupBelowSince[group] >=
                   profile.SupportGraceTime;
        }

        int CountGroupSupportUnits(DestructionProfile profile, int group)
        {
            // Before the first baseline every group is allowed to prove that it is an
            // intentionally side-attached element. Afterwards only groups that actually
            // needed that rule at baseline retain it. This keeps a nailed plank working,
            // while a stacked modular wall cannot switch from measured footing to an
            // unrelated one-edge contact after its lower wall is destroyed.
            bool allowSideAttachment = group < 0 ||
                group >= m_groupUsesSideAttachmentBaseline.Count ||
                m_groupUsesSideAttachmentBaseline[group];
            return CountGroupSupportUnitsCore(profile, group, allowSideAttachment,
                out _);
        }

        int CountGroupSupportUnitsCore(DestructionProfile profile, int group,
            bool allowSideAttachment, out bool usedSideAttachment)
        {
            usedSideAttachment = false;
            int count = 0;
            int measuredProbes = 0;
            bool fractured = Root != null && Root.State >= DestructibleState.Fractured;
            // The probe-to-fragment map is a STATIC association; whether a mapped
            // fragment is still holding anything up is decided per query by
            // IsCachedFragmentAttached. Building the map with includeInactive:false
            // conflated the two: it was built once, in the frame the root first
            // fractured, and every fragment whose GameObject was not active yet in
            // that frame was left out of it permanently. In Whole Building one root
            // holds every module, so a single hit anywhere flipped every module onto
            // this path at once and an untouched wall could come out of it with an
            // empty map — no probes, no footing, released whole. The prewarm path
            // already builds it with includeInactive:true for the same reason.
            if (fractured) EnsureFragmentProbeCache(profile, true);
            // A group that originally stood on the world must continue to be judged by
            // that footing. Otherwise a damaged post could claim support from the fence
            // board attached to its side, while the board simultaneously claims support
            // from the post. Side joints are support only for originally carried groups.
            bool worldGrounded = group < m_groupGrounded.Count
                ? m_groupGrounded[group]
                : HasWorldFooting(profile, group);
            // A junction sample that had to be attributed to one of several carriers is
            // held back rather than counted. If it turns out to be the only support this
            // element has left, the element is not silently zeroed: the held-back samples
            // are added again below.
            int ambiguousSupported = 0;
            GetProbeRange(group, out int probeStart, out int probeEnd);
            for (int i = probeStart; i < probeEnd; i++)
            {
                Vector3 origin = transform.TransformPoint(m_probeOriginsLocal[i]);
                // Once fragments are visible, material membership is authoritative. An
                // empty list is not permission to keep counting the world below forever:
                // that was the exact state which left a fully removed wall base green.
                // RebuildGroupBaselines runs this same rule before the first fragment is
                // activated, so an unresolved sample is excluded from both numerator and
                // denominator instead of creating either false support or a sudden drop.
                if (fractured && !HasCachedAttachedFragment(i))
                {
                    SetProbeState(i, false, false);
                    continue;
                }
                bool world = HasSupportBelow(origin, profile, i);
                bool carrier = !world &&
                               (HasCarrierSupportBelow(origin, profile, group, i) ||
                                HasNeighbourCarrierSupport(origin, profile, i));
                bool carried = world || carrier;
                // World footing is measured against the scene and is never ambiguous.
                // Only carrier support can be attributed to the wrong module.
                if (!world && IsAmbiguousJunctionProbe(i))
                {
                    if (carried) ambiguousSupported++;
                    SetProbeState(i, false, false);
                    continue;
                }
                measuredProbes++;
                SetProbeState(i, true, carried);
                if (carried) count++;
            }

            if (count == 0 && ambiguousSupported > 0)
            {
                count = ambiguousSupported;
                measuredProbes += ambiguousSupported;
            }

            // Side attachment answers "is this element held at all", never "how much of it
            // is still held", so it may only stand in where nothing measurable does. Applied
            // per probe it reported every sample of an arch as supported for as long as one
            // leg survived: the share never left 100%, Minimum Support could not be crossed,
            // and the arch went on spanning a column that was no longer there. A plank
            // nailed to the side of a post has no carrier under any of its probes, so it
            // still receives the whole-element answer this fallback was written for.
            // Evaluating it only here also keeps the path-to-ground search off every poll of
            // an element whose own probes already found their carrier.
            // A floor or roof distributes its load over a footprint. Treating one wall
            // contact as a nail would promote every sample to supported, so a 20 m slab
            // could keep floating from one triangular wall remnant. Its grid deliberately
            // reaches the perimeter and must retain the measured percentage instead.
            bool requiresDistributedFooting = group >= 0 &&
                group < ProbeColliders.Count && ProbeColliders[group] != null &&
                IsBroadHorizontalSupportSurface(ProbeColliders[group]);
            if (allowSideAttachment && !requiresDistributedFooting && count == 0 &&
                measuredProbes > 0 && !worldGrounded &&
                HasSideAttachment(profile, group, fractured))
            {
                count = measuredProbes;
                usedSideAttachment = true;
                for (int i = probeStart; i < probeEnd; i++)
                {
                    bool covered = i < m_probeCovered.Count && m_probeCovered[i];
                    if (covered && i < m_probeLiveSupported.Count)
                        m_probeLiveSupported[i] = true;
                }
            }

            if (profile.SupportDetection == SupportDetectionMode.AutomaticAndManual)
                count += CountManualSupportUnits();
            return count;
        }

        /// <summary>
        /// A plank nailed into the side of a post is carried through a face contact, not by
        /// something directly below its footprint. Keep the tolerance deliberately small:
        /// visually touching source elements form a structural joint, nearby independent
        /// props do not.
        /// </summary>
        bool HasSideAttachment(DestructionProfile profile, int group, bool fractured)
        {
            if (group < 0 || Root == null || ProbeColliders == null ||
                group >= ProbeColliders.Count)
                return false;

            float tolerance = Mathf.Clamp(profile.SupportProbeDistance * 0.1f,
                0.005f, 0.03f);
            if (fractured)
                // A mere side contact is not support after fracture. Two loose source
                // groups can still touch each other while both are hanging, which used
                // to create a cyclic "you support me, I support you" result. Require the
                // contact chain to end at a still-attached baked ground anchor instead.
                return Root.HasAttachedStructuralPathToGround(group, tolerance);

            Collider source = ProbeColliders[group];
            if (source == null) return false;
            Bounds sourceBounds = source.bounds;
            for (int i = 0; i < ProbeColliders.Count; i++)
            {
                if (i == group) continue;
                Collider other = ProbeColliders[i];
                if (other == null || !other.enabled ||
                    !other.gameObject.activeInHierarchy)
                    continue;
                if (DestructibleRoot.StructuralBoundsTouch(
                        sourceBounds, other.bounds, tolerance))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// True when at least one probe of this element rests on the world (ground, a static
        /// prop, another intact destructible) rather than on a different element of this
        /// same object. Evaluated once, on the intact baseline.
        /// </summary>
        bool HasWorldFooting(DestructionProfile profile, int group)
        {
            GetProbeRange(group, out int probeStart, out int probeEnd);
            for (int i = probeStart; i < probeEnd; i++)
            {
                if (HasSupportBelow(transform.TransformPoint(m_probeOriginsLocal[i]),
                        profile, i))
                    return true;
            }
            return false;
        }

        int CountManualSupportUnits()
        {
            int count = 0;
            m_manualIds.Clear();
            for (int i = 0; i < ManualSupports.Count; i++)
            {
                DestructibleRoot support = ManualSupports[i];
                if (support == null || support == Root || !support.isActiveAndEnabled ||
                    support.IsStructurallyReleased ||
                    support.State >= DestructibleState.Released)
                    continue;
                if (m_manualIds.Add(support.GetHashCode())) count++;
            }
            return count;
        }

        void EnsureProbeColliders()
        {
            if (ProbeColliders == null)
                ProbeColliders = new List<Collider>();

            for (int i = ProbeColliders.Count - 1; i >= 0; i--)
            {
                if (ProbeColliders[i] == null)
                    ProbeColliders.RemoveAt(i);
            }

            if (ProbeColliders.Count == 0 && Root != null && Root.IntactRoot != null)
            {
                Collider[] found = Root.IntactRoot.GetComponentsInChildren<Collider>(true);
                for (int i = 0; i < found.Length; i++)
                {
                    if (found[i] != null && !ProbeColliders.Contains(found[i]))
                        ProbeColliders.Add(found[i]);
                }
            }

            // Backward compatibility for prefabs baked before compound support probes.
            if (ProbeColliders.Count == 0 && ProbeCollider != null)
                ProbeColliders.Add(ProbeCollider);
            if (ProbeCollider == null && ProbeColliders.Count > 0)
                ProbeCollider = ProbeColliders[0];
        }

        void EnsureWorldSupportCacheConfiguration(DestructionProfile profile)
        {
            float distance = Mathf.Max(0.01f, profile.SupportProbeDistance);
            int layers = profile.SupportLayers.value;
            if (Mathf.Approximately(m_worldSupportCacheDistance, distance) &&
                m_worldSupportCacheLayers == layers)
                return;

            m_worldSupportCacheDistance = distance;
            m_worldSupportCacheLayers = layers;
            for (int i = 0; i < m_probeWorldSupportKinds.Count; i++)
            {
                m_probeWorldSupportKinds[i] = WorldSupportUnknown;
                if (i < m_probeWorldSupports.Count)
                    m_probeWorldSupports[i] = null;
            }
        }

        bool HasSupportBelow(Vector3 origin, DestructionProfile profile,
            int probeIndex = -1)
        {
            EnsureWorldSupportCacheConfiguration(profile);
            bool cacheable = probeIndex >= 0 &&
                             probeIndex < m_probeWorldSupportKinds.Count;
            bool keepPollingDynamicSupport = false;
            if (cacheable)
            {
                byte kind = m_probeWorldSupportKinds[probeIndex];
                if (kind == WorldSupportNone)
                    return false;
                keepPollingDynamicSupport = kind == WorldSupportDynamic;
                if (kind == WorldSupportPermanent)
                {
                    Collider cached = probeIndex < m_probeWorldSupports.Count
                        ? m_probeWorldSupports[probeIndex]
                        : null;
                    if (cached != null && cached.enabled &&
                        cached.gameObject.activeInHierarchy)
                        return true;
                    m_probeWorldSupportKinds[probeIndex] = WorldSupportUnknown;
                }
            }

            float distance = Mathf.Max(0.01f, profile.SupportProbeDistance);
            // Objects are rarely placed exactly on the floor. A base sunk a centimetre
            // into terrain starts the ray below the heightfield surface, where a downward
            // raycast can never hit it, and the whole element would read as unsupported.
            // The lift is added to the reach, so the tested distance below the footing
            // stays exactly SupportProbeDistance.
            float lift = Mathf.Min(0.05f, distance * 0.5f);
            int hitCount = Physics.RaycastNonAlloc(origin + Vector3.up * lift,
                Vector3.down, m_hits,
                distance + lift, profile.SupportLayers, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < hitCount; i++)
            {
                Collider hitCollider = m_hits[i].collider;
                if (hitCollider == null || !hitCollider.enabled ||
                    !hitCollider.gameObject.activeInHierarchy)
                    continue;

                Transform hitTransform = hitCollider.transform;
                if (hitTransform == transform || hitTransform.IsChildOf(transform))
                    continue;

                // Loose debris, physics bullets and rolling props are not structural
                // supports. Without this guard a single fragment beneath a roof could
                // keep its intact body kinematic indefinitely. Static colliders and
                // kinematic intact destructibles remain valid supports.
                Rigidbody hitBody = hitCollider.attachedRigidbody;
                if (hitBody != null && !hitBody.isKinematic)
                    continue;

                var supportRoot = hitCollider.GetComponentInParent<DestructibleRoot>();
                if (supportRoot != null &&
                    (supportRoot == Root || supportRoot.IsStructurallyReleased ||
                     supportRoot.State == DestructibleState.Released ||
                     supportRoot.State == DestructibleState.Settled ||
                     supportRoot.State == DestructibleState.Recycled))
                    continue;

                // Footing that is itself destructible changes what losing it means: the
                // material under this element was destroyed rather than merely removed.
                if (supportRoot != null) m_dependsOnDestructibleSupport = true;
                RecordProbeSupportSource(probeIndex, hitCollider);

                if (cacheable)
                {
                    m_probeWorldSupportKinds[probeIndex] =
                        supportRoot == null && hitBody == null
                            ? WorldSupportPermanent
                            : WorldSupportDynamic;
                    m_probeWorldSupports[probeIndex] = hitCollider;
                }
                return true;
            }

            if (cacheable)
            {
                // Once this probe has depended on a moving or destructible support it
                // must keep polling even while that support is temporarily absent.
                m_probeWorldSupportKinds[probeIndex] = keepPollingDynamicSupport
                    ? WorldSupportDynamic
                    : WorldSupportNone;
                m_probeWorldSupports[probeIndex] = null;
            }
            return false;
        }

        /// <summary>
        /// Support that comes from another structural element of the same destructible.
        /// A roof, deck or beam modelled as a separate mesh island rests on its own walls,
        /// never on the ground, and <see cref="HasSupportBelow"/> deliberately ignores every
        /// collider under this root. Without this test such a group reports zero footing at
        /// bake time, is filed as "carried", and keeps hanging in the air after the walls
        /// below it are gone. Proximity is used instead of a raycast because a probe that
        /// starts inside the carrier (roofs usually overlap the wall they sit on) produces
        /// no raycast hit at all.
        /// </summary>
        bool HasCarrierSupportBelow(Vector3 origin, DestructionProfile profile, int group,
            int probeIndex = -1)
        {
            // A single-element object has nothing that could carry it: skip the scan.
            if (group < 0 || Root == null || ProbeColliders == null ||
                ProbeColliders.Count < 2)
                return false;

            if (Root.State >= DestructibleState.Fractured && probeIndex >= 0 &&
                m_fragmentProbeCacheBuilt)
            {
                if (HasCachedCarrierFragment(probeIndex)) return true;
                // An EMPTY carrier list is missing data, not proof that nothing carries
                // this probe. The list is filled once, in the frame the shell first breaks,
                // by searching below the probe for fragments of another group; a carried
                // module whose search found nothing then kept an empty list forever and
                // every one of its probes read as unsupported. With no ground anchor of its
                // own that is the whole footing gone, so the module was released with
                // nothing destroyed, and its neighbours followed. A list that HAS entries,
                // none of them still attached, is a real loss and still reports false.
                if (probeIndex < m_probeCarrierFragments.Count &&
                    m_probeCarrierFragments[probeIndex] != null &&
                    m_probeCarrierFragments[probeIndex].Count > 0)
                    return false;
                // Fall through to the authored carrier relationship below, which answers
                // from the live state of the carrying group rather than from baked data.
            }

            if (probeIndex >= 0 && probeIndex < m_probeCarrierGroups.Count)
            {
                List<int> cachedGroups = m_probeCarrierGroups[probeIndex];
                for (int cachedIndex = 0;
                     cachedGroups != null && cachedIndex < cachedGroups.Count;
                     cachedIndex++)
                {
                    int carrierGroup = cachedGroups[cachedIndex];
                    if (carrierGroup < 0 ||
                        carrierGroup >= ProbeColliders.Count ||
                        (carrierGroup < m_groupReleased.Count &&
                         m_groupReleased[carrierGroup]))
                        continue;
                    Collider cachedCarrier = ProbeColliders[carrierGroup];
                    if (cachedCarrier != null && cachedCarrier.enabled &&
                        cachedCarrier.gameObject.activeInHierarchy)
                        return true;
                }
                return false;
            }

            float distance = Mathf.Max(0.01f, profile.SupportProbeDistance);
            float distanceSqr = distance * distance;

            for (int i = 0; i < ProbeColliders.Count; i++)
            {
                if (i == group) continue;
                if (i < m_groupReleased.Count && m_groupReleased[i]) continue;
                Collider other = ProbeColliders[i];
                if (other == null || !other.enabled ||
                    !other.gameObject.activeInHierarchy)
                    continue;

                // A non-convex MeshCollider returns the query point from ClosestPoint,
                // which would make every probe look supported forever.
                bool exact = other is MeshCollider mc && !mc.convex;
                Vector3 closest = exact
                    ? other.bounds.ClosestPoint(origin)
                    : other.ClosestPoint(origin);
                if ((closest - origin).sqrMagnitude <= distanceSqr &&
                    DestructibleRoot.CarriesFooting(origin, closest)) return true;
            }

            return Root.HasAttachedFragmentSupportNear(origin, distance, group);
        }

        static float GetProbeVerticalInset(float verticalLength)
        {
            return Mathf.Clamp(verticalLength * 0.002f, 0.002f, 0.04f);
        }

        static int GetWorldVerticalLocalAxis(Transform transform)
        {
            int vertical = 0;
            float bestAlignment = Mathf.Abs(Vector3.Dot(
                transform.TransformVector(Vector3.right).normalized, Vector3.up));
            float upAlignment = Mathf.Abs(Vector3.Dot(
                transform.TransformVector(Vector3.up).normalized, Vector3.up));
            if (upAlignment > bestAlignment)
            {
                bestAlignment = upAlignment;
                vertical = 1;
            }
            float forwardAlignment = Mathf.Abs(Vector3.Dot(
                transform.TransformVector(Vector3.forward).normalized, Vector3.up));
            if (forwardAlignment > bestAlignment) vertical = 2;
            return vertical;
        }

        static void GetProbePlaneMetrics(Collider collider, Bounds fallback,
            out float planeU, out float planeV, out float verticalLength)
        {
            if (collider is BoxCollider box)
            {
                Transform t = box.transform;
                // Pick the world-vertical local axis from orientation alone. Multiplying
                // by size first makes the authored thin axis a zero vector for Unity
                // Planes, so a horizontal floor is misclassified as a wall and all probes
                // are inset away from the supporting wall joint.
                Vector3 axisX = t.TransformVector(Vector3.right * box.size.x);
                Vector3 axisY = t.TransformVector(Vector3.up * box.size.y);
                Vector3 axisZ = t.TransformVector(Vector3.forward * box.size.z);
                switch (GetWorldVerticalLocalAxis(t))
                {
                    case 0:
                        planeU = axisY.magnitude;
                        planeV = axisZ.magnitude;
                        verticalLength = axisX.magnitude;
                        break;
                    case 2:
                        planeU = axisX.magnitude;
                        planeV = axisY.magnitude;
                        verticalLength = axisZ.magnitude;
                        break;
                    default:
                        planeU = axisX.magnitude;
                        planeV = axisZ.magnitude;
                        verticalLength = axisY.magnitude;
                        break;
                }
                return;
            }

            planeU = fallback.size.x;
            planeV = fallback.size.z;
            verticalLength = fallback.size.y;
        }

        static Vector3 GetProbeOrigin(Collider collider, Bounds fallback,
            int grid, int x, int z, float inset)
        {
            inset = Mathf.Clamp(inset, 0f, 0.45f);
            float tx = grid > 1 ? (float)x / (grid - 1) : 0.5f;
            float tz = grid > 1 ? (float)z / (grid - 1) : 0.5f;
            tx = Mathf.Lerp(inset, 1f - inset, tx);
            tz = Mathf.Lerp(inset, 1f - inset, tz);

            if (collider is BoxCollider box)
            {
                Transform t = box.transform;
                int vertical = GetWorldVerticalLocalAxis(t);
                Vector3 local = box.center;
                Vector3 verticalWorld;
                float verticalSize;
                switch (vertical)
                {
                    case 0:
                        local.y += Mathf.Lerp(-0.5f, 0.5f, tx) * box.size.y;
                        local.z += Mathf.Lerp(-0.5f, 0.5f, tz) * box.size.z;
                        verticalWorld = t.TransformVector(Vector3.right);
                        verticalSize = box.size.x;
                        break;
                    case 2:
                        local.x += Mathf.Lerp(-0.5f, 0.5f, tx) * box.size.x;
                        local.y += Mathf.Lerp(-0.5f, 0.5f, tz) * box.size.y;
                        verticalWorld = t.TransformVector(Vector3.forward);
                        verticalSize = box.size.z;
                        break;
                    default:
                        local.x += Mathf.Lerp(-0.5f, 0.5f, tx) * box.size.x;
                        local.z += Mathf.Lerp(-0.5f, 0.5f, tz) * box.size.z;
                        verticalWorld = t.TransformVector(Vector3.up);
                        verticalSize = box.size.y;
                        break;
                }

                float sign = Mathf.Sign(Vector3.Dot(verticalWorld, Vector3.up));
                if (Mathf.Approximately(sign, 0f)) sign = 1f;
                float verticalWorldLength = verticalWorld.magnitude * verticalSize;
                float insetWorld = GetProbeVerticalInset(verticalWorldLength);
                float insetLocal = verticalWorld.magnitude > 0.000001f
                    ? insetWorld / verticalWorld.magnitude
                    : 0f;
                float offset = sign * (verticalSize * 0.5f - insetLocal);
                if (vertical == 0) local.x -= offset;
                else if (vertical == 2) local.z -= offset;
                else local.y -= offset;
                return t.TransformPoint(local);
            }

            return new Vector3(
                Mathf.Lerp(fallback.min.x, fallback.max.x, tx),
                fallback.min.y + GetProbeVerticalInset(fallback.size.y),
                Mathf.Lerp(fallback.min.z, fallback.max.z, tz));
        }

        static bool IsBroadHorizontalSupportSurface(Collider collider)
        {
            Bounds bounds = collider != null ? collider.bounds : default;
            GetProbePlaneMetrics(collider, bounds, out float planeU,
                out float planeV, out float verticalLength);
            float narrowHorizontal = Mathf.Min(planeU, planeV);
            if (narrowHorizontal <= 0.001f) return false;
            return verticalLength <= Mathf.Max(0.05f, narrowHorizontal * 0.25f);
        }

        void OnDrawGizmosSelected()
        {
            var profile = Root != null ? Root.Profile : null;
            if (profile == null ||
                profile.SupportDetection == SupportDetectionMode.ManualList)
                return;

            EnsureProbeColliders();

            int grid = GetEffectiveProbeResolution(profile.SupportProbeGrid);
            float distance = Mathf.Max(0.01f, profile.SupportProbeDistance);
            Color idle = m_released
                ? new Color(1f, 0.25f, 0.12f, 0.9f)
                : new Color(1f, 0.62f, 0.12f, 0.9f);

            // In Play Mode every probe is drawn in the state the collapse rule reads it
            // in: green still carries the element, red has lost its footing. It answers
            // "why is this thing still standing" by pointing at the exact samples.
            bool live = Application.isPlaying && m_initialized;
            for (int i = 0; live && i < m_probeOriginsLocal.Count; i++)
            {
                Vector3 origin = transform.TransformPoint(m_probeOriginsLocal[i]);
                // Runtime support checks already cache this state at the configured poll
                // interval. Re-running raycasts and triangle searches here made a selected
                // building pay the entire structural query again on every Scene repaint.
                bool carried = i < m_probeLiveSupported.Count &&
                               m_probeLiveSupported[i];
                Gizmos.color = carried
                    ? new Color(0.25f, 0.95f, 0.35f, 0.95f)
                    : new Color(1f, 0.2f, 0.2f, 0.95f);
                Gizmos.DrawLine(origin, origin + Vector3.down * distance);
                Gizmos.DrawSphere(origin, carried ? 0.03f : 0.045f);
                if (!carried) continue;
                // A second line to whatever is answering for this sample. A green dot over
                // thin air is the hardest state to diagnose, and it is nearly always
                // something real but unexpected underneath rather than a phantom.
                Object source = i < m_probeSupportSources.Count
                    ? m_probeSupportSources[i]
                    : null;
                Vector3 anchor;
                if (source is Collider sourceCollider && sourceCollider != null)
                    anchor = sourceCollider.bounds.center;
                else if (source is Component sourceComponent && sourceComponent != null)
                    anchor = sourceComponent.transform.position;
                else continue;
                Gizmos.color = new Color(0.25f, 0.95f, 0.35f, 0.35f);
                Gizmos.DrawLine(origin, anchor);
            }
            if (live) return;

            Gizmos.color = idle;
            for (int colliderIndex = 0; colliderIndex < ProbeColliders.Count; colliderIndex++)
            {
                Collider probe = ProbeColliders[colliderIndex];
                if (probe == null || !probe.enabled) continue;
                float probeInset = IsBroadHorizontalSupportSurface(probe)
                    ? 0f
                    : profile.SupportProbeInset;

                for (int z = 0; z < grid; z++)
                {
                    for (int x = 0; x < grid; x++)
                    {
                        Vector3 origin = GetProbeOrigin(probe, probe.bounds, grid, x, z,
                            probeInset);
                        Gizmos.DrawLine(origin, origin + Vector3.down * distance);
                        Gizmos.DrawSphere(origin + Vector3.down * distance, 0.015f);
                    }
                }
            }
        }
    }
}
