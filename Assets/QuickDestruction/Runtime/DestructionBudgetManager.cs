using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

namespace RipVertices.QuickDestruction
{
    /// <summary>
    /// Global activation budgets. Destructibles sharing a budget group share a cap of active
    /// fragment rigidbodies. Degradation is deterministic: activation requests are granted in
    /// order and starved requests raise DelayedByBudget instead of randomly deleting fragments.
    /// </summary>
    public class DestructionBudgetManager : MonoBehaviour
    {
        [Header("Scene Debris Cleanup")]
        [Tooltip("Use one debris cleanup rule for Quick Destruction objects in this scene, except profiles with Keep Debris enabled. Overrides automatic removal in profiles; attached structure is protected. Disable to use the original profile settings.")]
        public bool SceneDebrisCleanup;
        [Tooltip("All Debris After Rest removes every eligible loose piece after it rests on a supporting surface. Keep All disables automatic removal; the filtered rules preserve pieces by size or body type.")]
        public SceneDebrisCleanupRule CleanupRule = SceneDebrisCleanupRule.AfterDelay;
        [Tooltip("Seconds of continuous rest on a supporting surface before removal is allowed, including under scene limits. Movement or loss of support restarts the wait.")]
        [Min(1f)] public float SceneDebrisLifetime = 30f;
        [Tooltip("Maximum world-space volume eligible for small debris cleanup. The Small and Runtime rule also includes runtime-generated pieces of any size. 0.125 cubic metres is the volume of a cube with 0.5 metre sides.")]
        [Min(0.0001f)] public float SmallDebrisMaxVolume = 0.125f;
        [Tooltip("Maximum retained loose fragments across this scene, including secondary and runtime-split pieces. Only excess fragments are removed, smallest first. 0 disables this count limit.")]
        [Range(0, 5000)] public int MaxSceneDebris = 300;
        [Tooltip("Maximum mesh triangles in retained loose debris across the scene. 0 disables this triangle limit. Smallest fragments are removed until both enabled limits are met.")]
        [Min(0)] public int MaxSceneDebrisTriangles = 500000;
        [Tooltip("Maximum fragments that begin removal in one frame. Spreads cleanup work across frames; a large burst can temporarily exceed the limits.")]
        [Range(1, 64)] public int SceneDebrisRemovalsPerFrame = 8;
        // Retained for compatibility with the earlier visual-only selector.
        [HideInInspector] public DebrisDespawnMode SceneDebrisDespawnMode = DebrisDespawnMode.SmoothShrink;

        static readonly HashSet<DestructibleRoot> s_sceneRoots = new HashSet<DestructibleRoot>();
        static readonly HashSet<DestructionBudgetManager> s_sceneManagers = new HashSet<DestructionBudgetManager>();
        readonly List<SceneDebrisCandidate> m_sceneDebris = new List<SceneDebrisCandidate>(512);
        readonly DebrisRestTracker m_sceneRest = new DebrisRestTracker();
        float m_nextSceneScan;
        int m_nextSceneRemoval;
        int m_retainedSceneDebris;
        long m_retainedSceneTriangles;
        SceneDebrisCleanupRule m_scannedRule;
        public int RetainedSceneDebris => m_retainedSceneDebris;

        void OnEnable() => s_sceneManagers.Add(this);
        void OnDisable()
        {
            s_sceneManagers.Remove(this);
            m_sceneRest.Clear();
            m_sceneDebris.Clear();
            m_nextSceneScan = 0f;
        }

        internal static bool OverridesProfileRemoval(DestructibleRoot root) =>
            root != null && ((root.Profile != null && root.Profile.KeepDebris) || FindSceneCleanup(root) != null);

        internal static DestructionBudgetManager FindSceneCleanup(DestructibleRoot root)
        {
            if (root == null || (root.Profile != null && root.Profile.KeepDebris)) return null;
            DestructionBudgetManager selected = null;
            foreach (DestructionBudgetManager manager in s_sceneManagers)
                if (manager != null && manager.isActiveAndEnabled && manager.SceneDebrisCleanup &&
                    manager.gameObject.scene == root.gameObject.scene &&
                    (selected == null || ManagerOrderId(manager) < ManagerOrderId(selected)))
                    selected = manager;
            return selected;
        }

        static ulong ManagerOrderId(DestructionBudgetManager manager)
        {
#if UNITY_6000_4_OR_NEWER
            return EntityId.ToULong(manager.GetEntityId());
#else
            return unchecked((uint)manager.GetInstanceID());
#endif
        }

        internal static void RegisterRoot(DestructibleRoot root) => s_sceneRoots.Add(root);
        internal static void UnregisterRoot(DestructibleRoot root) => s_sceneRoots.Remove(root);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetSceneRegistry()
        {
            s_sceneRoots.Clear();
            s_sceneManagers.Clear();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void SeedSceneRegistry()
        {
            // Also covers entering Play Mode with scene reload disabled.
#if UNITY_6000_4_OR_NEWER
            DestructibleRoot[] roots = Object.FindObjectsByType<DestructibleRoot>();
#elif UNITY_2022_2_OR_NEWER
            DestructibleRoot[] roots = Object.FindObjectsByType<DestructibleRoot>(FindObjectsSortMode.None);
#else
            DestructibleRoot[] roots = Object.FindObjectsOfType<DestructibleRoot>();
#endif
            foreach (DestructibleRoot root in roots) RegisterRoot(root);
#if UNITY_6000_4_OR_NEWER
            var managers = Object.FindObjectsByType<DestructionBudgetManager>();
#elif UNITY_2022_2_OR_NEWER
            var managers = Object.FindObjectsByType<DestructionBudgetManager>(FindObjectsSortMode.None);
#else
            var managers = Object.FindObjectsOfType<DestructionBudgetManager>();
#endif
            foreach (DestructionBudgetManager manager in managers)
                if (manager.isActiveAndEnabled) s_sceneManagers.Add(manager);
        }

        internal struct SceneDebrisCandidate
        {
            internal static float WorldVolume(float localVolume, Transform transform)
            {
                Vector3 scale = transform.lossyScale;
                return Mathf.Max(0f, localVolume) * Mathf.Abs(scale.x * scale.y * scale.z);
            }
            public DestructibleRoot Root;
            public FragmentRecord Fragment;
            public int PrimaryIndex;
            public SecondaryFragmentNode Secondary;
            public RuntimeSplitPiece RuntimePiece;
            public float Volume;
            public int Triangles;
            public Transform Transform => RuntimePiece != null ? RuntimePiece.transform : Fragment?.Transform;
            public float ActivatedTime => RuntimePiece != null ? RuntimePiece.ActivatedTime : Fragment.ActivatedTime;
            public Collider Collider => RuntimePiece != null ? RuntimePiece.CachedCollider : Fragment?.Collider;
            public Rigidbody Body => RuntimePiece != null ? RuntimePiece.CachedBody : Fragment?.Body;

            public bool CanRemove => Root != null && Root.isActiveAndEnabled &&
                (RuntimePiece != null
                    ? RuntimePiece.gameObject.activeInHierarchy && !RuntimePiece.IsDisappearing
                    : Fragment != null && Fragment.Activated && !Fragment.BudgetDespawned &&
                      !Fragment.DebrisDespawned && Fragment.Transform != null &&
                      Fragment.Transform.gameObject.activeInHierarchy &&
                      (Secondary != null ? Secondary.IsBroken : Root.IsSceneDebris(PrimaryIndex)));

            public void Remove(DebrisDespawnMode mode)
            {
                if (RuntimePiece != null) RuntimePiece.DespawnForSceneBudget(mode);
                else if (Secondary != null) Secondary.DespawnChildForSceneBudget(Fragment, mode);
                else Root.DespawnForSceneBudget(PrimaryIndex, mode);
            }
        }

        internal void ProcessSceneDebrisCleanup(bool forceScan = false)
        {
            if (!SceneDebrisCleanup || CleanupRule == SceneDebrisCleanupRule.KeepAll ||
                (CleanupRule == SceneDebrisCleanupRule.SceneLimit &&
                 MaxSceneDebris <= 0 && MaxSceneDebrisTriangles <= 0))
            {
                m_sceneDebris.Clear();
                m_sceneRest.Clear();
                m_retainedSceneDebris = 0;
                m_retainedSceneTriangles = 0;
                m_nextSceneScan = 0f;
                return;
            }
            if (forceScan || m_scannedRule != CleanupRule || Time.unscaledTime >= m_nextSceneScan)
            {
                if (m_scannedRule != CleanupRule) m_sceneRest.Clear();
                m_scannedRule = CleanupRule;
                m_nextSceneScan = Time.unscaledTime + 0.25f;
                m_sceneDebris.Clear();
                foreach (DestructibleRoot root in s_sceneRoots)
                    if (root != null && root.isActiveAndEnabled && root.gameObject.scene == gameObject.scene &&
                        FindSceneCleanup(root) == this)
                        root.CollectSceneDebris(m_sceneDebris);
                m_retainedSceneDebris = m_sceneDebris.Count;
                m_retainedSceneTriangles = 0;
                foreach (SceneDebrisCandidate piece in m_sceneDebris)
                    m_retainedSceneTriangles += piece.Triangles;
                m_nextSceneRemoval = 0;
                m_sceneRest.BeginScan();
                int eligible = 0;
                for (int i = 0; i < m_sceneDebris.Count; i++)
                {
                    SceneDebrisCandidate piece = m_sceneDebris[i];
                    if (MatchesCleanupRule(piece) && m_sceneRest.Observe(piece, Time.time, SceneDebrisLifetime))
                        m_sceneDebris[eligible++] = piece;
                }
                m_sceneRest.EndScan();
                m_sceneDebris.RemoveRange(eligible, m_sceneDebris.Count - eligible);
                m_sceneDebris.Sort(CleanupRule == SceneDebrisCleanupRule.SceneLimit
                    ? (System.Comparison<SceneDebrisCandidate>)CompareSceneDebris : CompareSceneDebrisAge);
            }

            int remaining = Mathf.Clamp(SceneDebrisRemovalsPerFrame, 1, 64);
            while (remaining > 0 && m_nextSceneRemoval < m_sceneDebris.Count &&
                   (CleanupRule != SceneDebrisCleanupRule.SceneLimit || SceneOverBudget()))
            {
                SceneDebrisCandidate piece = m_sceneDebris[m_nextSceneRemoval++];
                if (!piece.CanRemove || FindSceneCleanup(piece.Root) != this)
                {
                    // Reset, further splitting or a lifetime timer invalidated the snapshot.
                    // Rescan before removing anything else; stale counts must not over-clean.
                    m_nextSceneScan = 0f;
                    break;
                }
                if (!MatchesCleanupRule(piece) || !m_sceneRest.Observe(piece, Time.time, SceneDebrisLifetime))
                    continue;
                m_sceneRest.Forget(piece.Transform);
                piece.Remove(SceneDebrisDespawnMode);
                m_retainedSceneDebris--;
                m_retainedSceneTriangles -= piece.Triangles;
                remaining--;
            }
        }

        bool MatchesCleanupRule(SceneDebrisCandidate piece)
        {
            switch (CleanupRule)
            {
                case SceneDebrisCleanupRule.SceneLimit:
                case SceneDebrisCleanupRule.AfterDelay:
                    return true;
                case SceneDebrisCleanupRule.SmallAfterDelay:
                    return piece.Volume <= Mathf.Max(0.0001f, SmallDebrisMaxVolume);
                case SceneDebrisCleanupRule.SmallAndRuntimeAfterDelay:
                    return piece.RuntimePiece != null || piece.Volume <= Mathf.Max(0.0001f, SmallDebrisMaxVolume);
                case SceneDebrisCleanupRule.DynamicAfterDelay:
                    return piece.Body != null && !piece.Body.isKinematic;
                default:
                    return false;
            }
        }

        static int CompareSceneDebrisAge(SceneDebrisCandidate a, SceneDebrisCandidate b) =>
            a.ActivatedTime.CompareTo(b.ActivatedTime);

        bool SceneOverBudget() =>
            (MaxSceneDebris > 0 && m_retainedSceneDebris > MaxSceneDebris) ||
            (MaxSceneDebrisTriangles > 0 && m_retainedSceneTriangles > MaxSceneDebrisTriangles);

        static int CompareSceneDebris(SceneDebrisCandidate a, SceneDebrisCandidate b)
        {
            int volume = a.Volume.CompareTo(b.Volume);
            return volume != 0 ? volume : a.Transform.gameObject.GetHashCode().CompareTo(b.Transform.gameObject.GetHashCode());
        }
        sealed class InstanceState
        {
            public DestructionBudgetManager Instance;
        }

        static readonly InstanceState s_instanceState = new InstanceState();
        static readonly ProfilerMarker s_degradeMarker = new ProfilerMarker("QD.Budget.Degrade");

        public static DestructionBudgetManager Instance
        {
            get
            {
                if (s_instanceState.Instance == null)
                {
                    var go = new GameObject("[QuickDestruction Budgets]");
                    go.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave;
                    if (Application.isPlaying)
                        DontDestroyOnLoad(go);
                    s_instanceState.Instance = go.AddComponent<DestructionBudgetManager>();
                }
                return s_instanceState.Instance;
            }
        }

        class Group
        {
            public int Cap;
            public int Active;
            public int DelayedThisFrame;
        }

        readonly Dictionary<string, Group> m_groups = new Dictionary<string, Group>(8);

        Group GetGroup(string name, int cap)
        {
            if (string.IsNullOrEmpty(name)) name = "Default";
            if (!m_groups.TryGetValue(name, out var g))
            {
                g = new Group { Cap = cap };
                m_groups.Add(name, g);
            }
            // The largest cap declared by any profile in the group wins.
            if (cap > g.Cap) g.Cap = cap;
            return g;
        }

        /// <summary>Requests activation slots. Returns how many were granted (0..desired).</summary>
        public int RequestActivation(string groupName, int groupCap, int desired)
        {
            var g = GetGroup(groupName, groupCap);
            int available = Mathf.Max(0, g.Cap - g.Active);
            int granted = Mathf.Min(available, desired);
            g.Active += granted;
            if (granted < desired)
            {
                using (s_degradeMarker.Auto())
                {
                    g.DelayedThisFrame += desired - granted;
                }
            }
            return granted;
        }

        /// <summary>
        /// Atomically replaces already counted bodies with new bodies. If no replacement body can
        /// be granted, the existing active count remains unchanged and the caller keeps its parent.
        /// </summary>
        public int RequestReplacement(string groupName, int groupCap,
            int releasing, int desired)
        {
            var g = GetGroup(groupName, groupCap);
            int baseActive = Mathf.Max(0, g.Active - Mathf.Max(0, releasing));
            int available = Mathf.Max(0, g.Cap - baseActive);
            int granted = Mathf.Min(available, Mathf.Max(0, desired));
            if (granted <= 0)
            {
                if (desired > 0)
                {
                    using (s_degradeMarker.Auto())
                    {
                        g.DelayedThisFrame += desired;
                    }
                }
                return 0;
            }

            g.Active = baseActive + granted;
            if (granted < desired)
            {
                using (s_degradeMarker.Auto())
                {
                    g.DelayedThisFrame += desired - granted;
                }
            }
            return granted;
        }

        /// <summary>Returns activation slots to the group when fragments settle or recycle.</summary>
        public void ReleaseActivation(string groupName, int count)
        {
            if (string.IsNullOrEmpty(groupName)) groupName = "Default";
            if (m_groups.TryGetValue(groupName, out var g))
                g.Active = Mathf.Max(0, g.Active - count);
        }

        public int GetActiveCount(string groupName)
        {
            if (string.IsNullOrEmpty(groupName)) groupName = "Default";
            return m_groups.TryGetValue(groupName, out var g) ? g.Active : 0;
        }

        public int GetCap(string groupName)
        {
            if (string.IsNullOrEmpty(groupName)) groupName = "Default";
            return m_groups.TryGetValue(groupName, out var g) ? g.Cap : 0;
        }

        /// <summary>
        /// Activation requests denied for this group during the current frame.
        /// The counter resets every frame after LateUpdate.
        /// </summary>
        public int GetDelayedCount(string groupName)
        {
            if (string.IsNullOrEmpty(groupName)) groupName = "Default";
            return m_groups.TryGetValue(groupName, out var g) ? g.DelayedThisFrame : 0;
        }

        void LateUpdate()
        {
            ProcessSceneDebrisCleanup();
            foreach (var kv in m_groups)
                kv.Value.DelayedThisFrame = 0;
        }

        void OnDestroy()
        {
            s_sceneManagers.Remove(this);
            if (s_instanceState.Instance == this) s_instanceState.Instance = null;
        }
    }
}
