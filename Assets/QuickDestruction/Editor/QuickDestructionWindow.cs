using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using RipVertices.QuickDestruction.Runtime;

namespace RipVertices.QuickDestruction.Editor
{
    /// <summary>
    /// One window drives the whole golden workflow:
    /// SELECT -> ANALYZE -> PRESET -> BAKE -> TEST, without overwriting the source.
    /// Quick Setup keeps the common workflow focused; Advanced Setup reveals seeds,
    /// collider cooking, budgets and detailed cleanup policies.
    /// </summary>
    public partial class QuickDestructionWindow : EditorWindow
    {
        internal enum Workspace { Analyze, Fracture, Physics, Structure, Runtime, Bake }
        internal enum SourceSelectionMode
        {
            SelectedObjects,
            RootHierarchy,
            WholeBuilding
        }

        const float SidebarWidth = 192f;
        const float ActionFooterHeight = 58f;

        [SerializeField] GameObject m_source;
        [SerializeField] DestructionProfile m_profile;
        [SerializeField] DestructibleRoot m_contextRoot;
        // Setup Mode is gone. The "quick" variant only hid a handful of fields — probe
        // grid, mass clamps, force mode, a layer mask — which is nowhere near enough
        // difference to justify making the author pick a mode before they can start, and
        // it meant a control could be missing for a reason that had nothing to do with the
        // object. Everything is shown; the fold-out cards already do the real hiding.
        readonly bool m_advancedSetup = true;
        [SerializeField] bool m_autoAnalyze = true;
        [SerializeField] bool m_placeInScene = true;
        [SerializeField] FracturePattern m_quickPreset = FracturePattern.UniformConcrete;
        [SerializeField] DamageTriggerMode m_quickDamageTrigger = DamageTriggerMode.Mixed;
        [SerializeField] bool m_quickActivateAllOnFirstBreak = false;
        [SerializeField] ColliderStrategy m_quickColliderStrategy =
            ColliderStrategy.ConvexMesh;
        [SerializeField] bool m_quickSolidify;
        [SerializeField] float m_quickSolidifyThickness = 0.08f;
        // Source the Hollow Shell default was already offered for. A default that
        // re-applied every repaint would not be a default: choosing FULL SOLID for a
        // modular building has to survive the next frame.
        [SerializeField] GameObject m_hollowShellDefaultedSource;
        [SerializeField] bool m_quickRuntimeFragmentSplitting = true;
        [SerializeField] int m_quickRuntimeFinalIteration = 2;
        [SerializeField] bool m_quickDespawnDebris = true;
        [SerializeField] float m_quickDespawnDebrisAfter = 60f;
        [SerializeField] bool m_quickUseDefaultHitParticles = true;
        [SerializeField] bool m_quickUseDefaultHitSound = true;
        [SerializeField] bool m_quickPlayHitEffectsOnEveryHit = true;
        [SerializeField] ParticleSystem m_quickHitParticlePrefab;
        [SerializeField] AudioClip m_quickHitSound;
        [SerializeField] Vector3 m_quickBrickCellSize =
            new Vector3(0.5f, 0.25f, 0.25f);
        [SerializeField] SourceSelectionMode m_sourceSelectionMode =
            SourceSelectionMode.SelectedObjects;
        [SerializeField] bool m_wholeBuildingShowShellWhileIntact;
        // These are the roots the user selected, not necessarily the objects Bake will
        // receive. Root Hierarchy mode expands them into one source per nested MeshFilter.
        [SerializeField] List<GameObject> m_selectedSources = new List<GameObject>();
        [SerializeField] float m_previewExplode = 0.08f;
        [SerializeField] bool m_autoFracturePreview = true;
        [SerializeField] bool m_previewWireframe = true;
        [SerializeField] FracturePreviewMaterialMode m_previewMaterialMode =
            FracturePreviewMaterialMode.Colorized;
        [SerializeField] bool m_geometryDirty;

        Workspace m_workspace = Workspace.Analyze;
        Vector2 m_scroll;
        Vector2 m_sidebarScroll;
        Vector2 m_searchResultsScroll;
        AnalysisReport m_report;
        BakeOutcome m_lastBake;
        [SerializeField] string m_searchQuery = string.Empty;
        string m_pendingSettingLabel;
        string m_pendingSettingSection;
        int m_pendingSettingRepaints;
        bool m_settingJumpScrolled;
        string m_layoutSearchQuery = string.Empty;
        Workspace m_highlightWorkspace;
        FracturePreviewSession m_fracturePreview;
        CombinedMeshSource m_combinedSource;
        double m_statusUntil;
        string m_statusMessage;
        string m_previewError;
        bool m_showPreviewFailureDialog;
        int m_prefabAssetWarningId;
        double m_ignoreSelectionUntil;
        bool m_isBakingSelection;
        bool m_previewRefreshPending;
        bool m_previewRefreshReady;
        double m_nextSolveRepaintAt;
        // The background solve can finish between an IMGUI Layout and Repaint pass.
        // Draw code must use this per-frame snapshot, never the live session state,
        // so both passes always see the same control tree.
        bool m_layoutSolving;
        float m_layoutSolveProgress;
        bool m_previewGeometryControlWasHot;
        bool m_selectionRefreshPending;
        [System.NonSerialized] bool m_profileInitializationQueued;
        AnalysisReport m_layoutReport;
        double m_previewRefreshAt;
        List<DestructionProfile> m_profileLibrary;
        double m_profileLibraryScanTime;
        List<DestructibleRoot> m_layoutProfileUsers;
        DestructionProfile m_layoutUsersProfile;

        // GUI style caches are kept in one readonly holder so Fast Enter Play Mode has
        // no mutable static fields to retain between sessions.
        sealed class WindowGuiState
        {
            public GUIStyle SidebarStyle;
            public GUIStyle ActionFooterStyle;
            public GUIStyle FooterBlockerStyle;
        }

        static readonly WindowGuiState s_gui = new WindowGuiState();

        static readonly string[] PatternNames =
        {
            "Uniform (Concrete)", "Radial (Glass)", "Directional (Wood)", "Brick (Cells)",
            "Layered (Slabs)", "Columnar (Pillars)", "Impact (Shatter)"
        };
        static readonly string[] ColliderNames =
            { "Convex Mesh", "Primitive Box", "Exact Mesh" };
        static readonly ColliderStrategy[] ColliderValues =
            { ColliderStrategy.ConvexMesh, ColliderStrategy.PrimitiveBox,
              ColliderStrategy.ExactMesh };
        static readonly string[] CutUVModeNames =
            { "Projected Tiling", "Submesh Island Fit (No Stretch)", "Edge Stretch (Extruded UV)" };
        static readonly string[] IntactBodyNames =
            { "Static (Fixed)", "Dynamic Rigidbody", "Release When Unsupported" };
        static readonly string[] SourceSelectionModeNames =
            { "Selected Objects", "Root + Nested Meshes", "Whole Building (Modular Workflow)" };

        internal static string GetSourceSelectionModeDescription(SourceSelectionMode mode)
        {
            switch (mode)
            {
                case SourceSelectionMode.RootHierarchy:
                    return "USE FOR: one or more hierarchy roots whose child meshes must all " +
                           "become destructible. Every MeshFilter below each selected root is " +
                           "collected recursively, including inactive and deeply nested objects; " +
                           "overlapping selections are deduplicated. RESULT: every resolved mesh " +
                           "is analyzed and baked as its own independent destructible - geometry " +
                           "and structural support are not merged between children.";
                case SourceSelectionMode.WholeBuilding:
                    return "USE FOR: a modular building assembled from adjoining wall, floor and " +
                           "roof modules. Every MeshFilter below all selected roots is combined " +
                           "only in a temporary source. RESULT: Preview and Bake treat the complete " +
                           "assembly as one destructible and one structural system, allowing cuts " +
                           "to cross joined module seams; visible gaps stay separate. With HOLLOW " +
                           "SHELL, open Plane-style panels receive Wall Thickness while closed solid " +
                           "modules keep their authored geometry and depth. Source objects are never " +
                           "rewritten.";
                default:
                    return "USE FOR: unrelated props or copies that should be processed together " +
                           "but remain separate. Every selected Scene object is one batch source. " +
                           "RESULT: each source is analyzed and baked as its own independent " +
                           "destructible; meshes are not combined and one object's support cannot " +
                           "hold another object.";
            }
        }

        bool IsWholeBuildingMode =>
            m_sourceSelectionMode == SourceSelectionMode.WholeBuilding;

        int PrimaryFragmentAuthoringLimit(DestructionProfile profile)
        {
            int modeLimit = IsWholeBuildingMode
                ? MeshAnalyzer.MaxMergedAssemblyFragmentCount
                : MeshAnalyzer.MaxSafePrimaryFragmentCount;
            // Merely leaving Whole Building mode must not silently truncate a shared
            // profile that was authored for a building. Standard Bake still enforces its
            // 250-piece gate until this mode is selected again.
            return Mathf.Max(modeLimit, profile != null ? profile.FragmentCount : 2);
        }

        [MenuItem("Tools/Quick Destruction %#d", false, 10)]
        public static void Open()
        {
            var window = GetWindow<QuickDestructionWindow>();
            window.titleContent = new GUIContent("Quick Destruction");
            window.minSize = new Vector2(620f, 480f);
            window.m_workspace = Workspace.Analyze;
            window.m_scroll = Vector2.zero;
            window.AdoptSelectionIfSourceMissing();
            window.Repaint();
        }

        public static void OpenWith(GameObject source)
        {
            Open();
            var window = GetWindow<QuickDestructionWindow>();
            window.m_sourceSelectionMode = SourceSelectionMode.SelectedObjects;
            window.m_source = source;
            window.m_selectedSources.Clear();
            if (source != null) window.m_selectedSources.Add(source);
            window.RunAnalysis();
        }

        public static void OpenFor(DestructibleRoot root)
        {
            Open();
            var window = GetWindow<QuickDestructionWindow>();
            window.EndFracturePreviews();
            window.m_sourceSelectionMode = SourceSelectionMode.SelectedObjects;
            window.m_contextRoot = root;
            window.m_profile = root != null ? root.Profile : null;

            GameObject source = root != null && root.gameObject.scene.IsValid() &&
                                HasUsableMesh(root.gameObject)
                ? root.gameObject
                : null;
            window.m_source = source;
            window.m_selectedSources.Clear();
            if (source != null) window.m_selectedSources.Add(source);
            window.m_report = source != null ? MeshAnalyzer.Analyze(source) : null;

            // Opening from a component is an explicit edit-context request. Show the
            // full profile, start on geometry and create the assembled wireframe preview.
            window.m_workspace = Workspace.Fracture;
            window.m_scroll = Vector2.zero;
            window.m_sidebarScroll = Vector2.zero;
            window.m_previewExplode = 0.08f;
            window.m_previewWireframe = true;
            window.m_autoFracturePreview = true;
            window.m_ignoreSelectionUntil = EditorApplication.timeSinceStartup + 1d;
            window.m_selectionRefreshPending = false;
            EnsureInteriorMaterialAssigned(window.m_profile, false);
            window.ScheduleFracturePreview();
            window.ShowStatus(root != null
                ? $"Editing {root.name} with profile " +
                  $"{(root.Profile != null ? root.Profile.name : "None")}."
                : "No Destructible Root was supplied.");
            window.Focus();
            window.Repaint();
        }

        // Scene selection is the source of truth for Quick Setup. A cached list survives
        // selecting a profile/material in the Project window without losing a batch.
        bool HasSourceOrSelectionCandidate()
        {
            return GetSourceCandidates().Count > 0;
        }

        void AdoptSelectionIfSourceMissing()
        {
            if (m_isBakingSelection) return;
            if (EditorApplication.timeSinceStartup < m_ignoreSelectionUntil) return;
            List<GameObject> sceneSelection = GetSceneSelection();
            if (sceneSelection.Count == 0)
            {
                // Selecting a profile, material or another Project asset must not discard
                // the cached scene source and its active profile. Creating a named profile
                // imports that asset and can make it the active Project selection while the
                // modal naming window returns focus to us. Treat only a genuinely empty
                // selection as a request to clear the setup context.
                if (Selection.activeObject != null)
                    return;
                if (m_source != null || m_profile != null)
                {
                    ReleaseCombinedSource();
                    m_source = null;
                    m_selectedSources.Clear();
                    m_contextRoot = null;
                    m_profile = null;
                    m_report = null;
                    Repaint();
                }
                return;
            }

            bool selectionChanged = m_selectedSources == null ||
                                    m_selectedSources.Count != sceneSelection.Count;
            if (!selectionChanged)
            {
                for (int i = 0; i < sceneSelection.Count; i++)
                {
                    if (m_selectedSources[i] == sceneSelection[i]) continue;
                    selectionChanged = true;
                    break;
                }
            }
            if (selectionChanged) ReleaseCombinedSource();
            m_selectedSources.Clear();
            m_selectedSources.AddRange(sceneSelection);
            RefreshSourceFromCachedSelection();
            // Before anything reads the mode, take it from what the object records rather
            // than from whatever this session happens to be left holding.
            RestoreWholeBuildingModeIfGenerated();
            if (selectionChanged)
            {
                m_report = m_autoAnalyze && m_source != null
                    ? AnalyzeCurrentSource(false)
                    : null;
            }
        }

        void RefreshSourceFromCachedSelection()
        {
            m_sourceCandidateCache = null;
            List<GameObject> candidates = GetSourceCandidates();
            GameObject nextSource = candidates.Count > 0 ? candidates[0] : null;
            bool changed = m_source != nextSource;
            if (changed)
            {
                ReleaseCombinedSource();
                m_workspace = Workspace.Analyze;
                m_contextRoot = null;
                // The profile deliberately survives the switch. Clearing it here meant an
                // in-memory profile was destroyed and rebuilt from the window's quick
                // defaults, so everything tuned in the detail tabs — part counts, secondary
                // breakup, supports — had to be re-entered for every copy of the same
                // prefab. A profile is the reusable setup; SyncProfileFromSelection still
                // replaces it when the new source is a baked object that owns one.
            }
            m_source = nextSource;
            SyncProfileFromSelection(nextSource);
            if (changed)
            {
                UpdateInMemoryProfileName();
            }
            if (changed)
                m_report = m_autoAnalyze && m_source != null
                    ? AnalyzeCurrentSource(false)
                    : null;
        }

        /// <summary>
        /// The window mirrors the Inspector: selecting an already baked destructible
        /// loads that object's assigned profile, so both views always show the same
        /// asset and edits land on the profile the object actually uses.
        /// </summary>
        void SyncProfileFromSelection(GameObject source)
        {
            DestructibleRoot root = source != null
                ? source.GetComponentInParent<DestructibleRoot>(true)
                : null;
            if (root == null || root.Profile == null) return;
            m_contextRoot = root;
            if (m_profile == root.Profile) return;
            m_profile = root.Profile;
            m_quickPreset = root.Profile.Pattern;
            if (root.Profile.Pattern == FracturePattern.BrickCells)
                m_quickBrickCellSize = ClampBrickCellSize(root.Profile.BrickCellSize);
            EnsureInteriorMaterialAssigned(root.Profile, false);
            ShowStatus(
                $"Selected destructible '{root.name}': profile " +
                $"'{root.Profile.name}' loaded into the window.");
        }

        [System.NonSerialized] List<GameObject> m_sourceCandidateCache;

        /// <summary>
        /// Resolving the Hierarchy is the most expensive thing this window does per
        /// frame, and several cards ask for the same answer inside one event. The cache
        /// is dropped at the start of every Layout, so it never outlives an IMGUI frame.
        /// </summary>
        List<GameObject> GetSourceCandidates()
        {
            if (m_sourceCandidateCache != null) return m_sourceCandidateCache;
            List<GameObject> result = CollectSourceCandidates(
                m_selectedSources, m_sourceSelectionMode);
            if (result.Count == 0 && m_source != null)
                result = CollectSourceCandidates(
                    new[] { m_source }, m_sourceSelectionMode);
            m_sourceCandidateCache = result;
            return result;
        }

        static List<GameObject> GetSceneSelection()
        {
            var result = new List<GameObject>();
            GameObject active = Selection.activeGameObject;
            if (active != null && active.scene.IsValid()) result.Add(active);
            GameObject[] selected = Selection.gameObjects;
            for (int i = 0; i < selected.Length; i++)
            {
                GameObject item = selected[i];
                if (item != null && item.scene.IsValid() && !result.Contains(item))
                    result.Add(item);
            }
            return result;
        }

        List<GameObject> GetWholeBuildingRoots()
        {
            var roots = new List<GameObject>();
            if (m_selectedSources != null)
            {
                for (int i = 0; i < m_selectedSources.Count; i++)
                {
                    GameObject item = m_selectedSources[i];
                    if (item != null && item.scene.IsValid() && !roots.Contains(item))
                        roots.Add(item);
                }
            }
            if (roots.Count == 0 && m_source != null && m_source.scene.IsValid())
                roots.Add(m_source);
            return CombinedMeshSource.NormalizeRoots(roots);
        }

        internal static List<GameObject> CollectWholeBuildingMeshObjects(
            IReadOnlyList<GameObject> selectedRoots)
        {
            var result = new List<GameObject>();
            var seen = new HashSet<MeshFilter>();
            List<GameObject> roots = CombinedMeshSource.ResolveUnavailableGeneratedSources(
                CombinedMeshSource.NormalizeRoots(selectedRoots),
                preferOriginalSources: true);
            for (int rootIndex = 0; rootIndex < roots.Count; rootIndex++)
            {
                GameObject root = roots[rootIndex];
                DestructibleRoot bakedRoot = root.GetComponent<DestructibleRoot>();
                GameObject filterRoot = bakedRoot != null && bakedRoot.IntactRoot != null
                    ? bakedRoot.IntactRoot
                    : root;
                MeshFilter[] filters = filterRoot.GetComponentsInChildren<MeshFilter>(true);
                for (int i = 0; i < filters.Length; i++)
                {
                    MeshFilter filter = filters[i];
                    if (filter != null && filter.sharedMesh != null && seen.Add(filter))
                        result.Add(filter.gameObject);
                }
            }
            return result;
        }

        /// <summary>
        /// True for an instance that a Whole Building bake produced. Outputs baked before
        /// the explicit flag existed fall back to the number of linked authored roots,
        /// since a plain bake only ever links the single object it came from.
        /// </summary>
        internal static bool IsGeneratedWholeBuildingAssembly(GameObject candidate)
        {
            DestructibleRoot root = candidate != null
                ? candidate.GetComponent<DestructibleRoot>()
                : null;
            if (root == null) return false;
            if (root.BakedAsWholeBuilding) return true;
            if (root.OriginalSourceObjects != null && root.OriginalSourceObjects.Count > 1)
                return true;
            // Legacy prefabs did not persist the Whole Building flag and often have just
            // one authored container. Their distinct module groups are the baked evidence.
            int firstGroup = -1;
            foreach (int group in root.IntactColliderStructuralGroups)
            {
                if (group < 0) continue;
                if (firstGroup >= 0 && group != firstGroup) return true;
                firstGroup = group;
            }
            foreach (FragmentRecord fragment in root.Fragments)
            {
                if (fragment == null || fragment.StructuralGroup < 0) continue;
                if (firstGroup >= 0 && fragment.StructuralGroup != firstGroup) return true;
                firstGroup = fragment.StructuralGroup;
            }
            return root.OriginalSourceObject != null &&
                   root.OriginalSourceObject.GetComponentsInChildren<MeshFilter>(true).Length > 1;
        }

        /// <summary>
        /// Puts the window back into Whole Building mode when the object in hand was baked
        /// that way. The mode itself does not survive a domain reload - a script recompile,
        /// leaving Play Mode or an Editor restart all reset it - and until now nothing
        /// noticed. The rebake then ran the ordinary path, which never receives the module
        /// collision sources, so the same geometry came back with one convex intact hull
        /// instead of the authored per-panel colliders, silently.
        /// </summary>
        bool RestoreWholeBuildingModeIfGenerated()
        {
            if (IsWholeBuildingMode) return false;
            if (!IsGeneratedWholeBuildingAssembly(m_source)) return false;
            m_sourceSelectionMode = SourceSelectionMode.WholeBuilding;
            ReleaseCombinedSource();
            ShowStatus(
                "Whole Building mode restored: this object was baked as a merged " +
                "assembly. Rebaking it as Selected Objects would have replaced its " +
                "per-panel intact colliders with one convex hull.", 8d);
            Repaint();
            return true;
        }

        void ReleaseCombinedSource(bool endPreview = true)
        {
            if (endPreview) EndFracturePreviews();
            m_combinedSource?.Dispose();
            m_combinedSource = null;
        }

        bool TryPrepareWholeBuildingSource(bool rebuild, out GameObject fractureSource,
            out string error)
        {
            fractureSource = m_source;
            error = null;
            if (!IsWholeBuildingMode) return fractureSource != null;

            // A generated assembly normally links the authored modular roots that placement
            // disabled. Prefer those roots even when Intact is usable: rebaking the already
            // merged Intact mesh loses per-panel collider boundaries and can recreate one
            // large convex triangle across a room. Only legacy/unlinked outputs fall back to
            // the usable Intact mesh; the inactive fragment pool is never collected.
            if (m_source != null)
            {
                DestructibleRoot generated =
                    m_source.GetComponent<DestructibleRoot>();
                if (generated != null &&
                    CombinedMeshSource.HasUsableIntactMesh(generated))
                {
                    List<GameObject> recovered =
                        CombinedMeshSource.ResolveUnavailableGeneratedSources(
                            new List<GameObject> { m_source },
                            preferOriginalSources: true);
                    bool hasAuthoredSource = false;
                    for (int i = 0; i < recovered.Count; i++)
                    {
                        if (recovered[i] != null && recovered[i] != m_source)
                        {
                            hasAuthoredSource = true;
                            break;
                        }
                    }
                    if (!hasAuthoredSource) return true;
                }
            }

            if (!rebuild && m_combinedSource != null &&
                m_combinedSource.ProxyObject != null)
            {
                fractureSource = m_combinedSource.ProxyObject;
                return true;
            }

            ReleaseCombinedSource();
            List<GameObject> roots = GetWholeBuildingRoots();
            try
            {
                bool ContinuePreparation(string phase, float progress)
                {
                    return !EditorUtility.DisplayCancelableProgressBar(
                        "Quick Destruction - Whole Building",
                        phase, Mathf.Clamp01(progress));
                }

                if (!CombinedMeshSource.TryCreate(roots, out m_combinedSource,
                        out error, ContinuePreparation))
                    return false;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            fractureSource = m_combinedSource.ProxyObject;
            m_source = m_combinedSource.Anchor;
            return fractureSource != null;
        }

        GameObject GetCurrentFractureSource()
        {
            return IsWholeBuildingMode && m_combinedSource != null &&
                   m_combinedSource.ProxyObject != null
                ? m_combinedSource.ProxyObject
                : m_source;
        }

        /// <summary>
        /// Resolves the user's Scene selection into the exact objects handled by the
        /// existing batch Bake. Selected Objects keeps one source per selected object.
        /// Root Hierarchy finds every nested MeshFilter, including inactive branches, and
        /// treats its owning GameObject as an independent source. Normalizing generated
        /// children back to their DestructibleRoot keeps rebakes deduplicated.
        /// </summary>
        internal static List<GameObject> CollectSourceCandidates(
            IEnumerable<GameObject> selectedObjects, SourceSelectionMode mode)
        {
            var result = new List<GameObject>();
            if (selectedObjects == null) return result;

            var selectedList = new List<GameObject>();
            foreach (GameObject selected in selectedObjects)
                if (selected != null && selected.scene.IsValid() &&
                    !selectedList.Contains(selected))
                    selectedList.Add(selected);

            // Whole Building is one solver source regardless of how many roots/modules
            // were selected. The first usable selected root is the placement anchor; the
            // complete cached selection is passed to CombinedMeshSource separately.
            if (mode == SourceSelectionMode.WholeBuilding)
            {
                for (int i = 0; i < selectedList.Count; i++)
                {
                    GameObject selected = NormalizeSourceCandidate(selectedList[i]);
                    if (HasUsableMesh(selected, true))
                    {
                        result.Add(selected);
                        break;
                    }
                }
                return result;
            }

            Dictionary<GameObject, GameObject> generatedByOriginal =
                mode == SourceSelectionMode.RootHierarchy
                    ? BuildGeneratedSourceLookup()
                    : null;
            for (int selectedIndex = 0; selectedIndex < selectedList.Count; selectedIndex++)
            {
                GameObject selected = selectedList[selectedIndex];
                if (mode == SourceSelectionMode.RootHierarchy)
                {
                    DestructibleRoot selectedGenerated =
                        selected.GetComponent<DestructibleRoot>();
                    if (selectedGenerated != null)
                        AddSourceCandidate(result, selectedGenerated.gameObject, true);
                    else
                    {
                        int candidateCountBeforeRoot = result.Count;
                        CollectNestedMeshSources(selected.transform, result,
                            generatedByOriginal);
                        // A completed Root + Nested bake groups its generated siblings
                        // below a meshless *_Destructibles container. The authored-source
                        // traversal deliberately stops at DestructibleRoot to avoid treating
                        // fragments as new input, which made this container resolve to zero
                        // items and GetSourceCandidates fall back to the first remembered
                        // source. Only when no authored source was found, treat each nested
                        // DestructibleRoot itself as the independent preview/rebake source.
                        if (result.Count == candidateCountBeforeRoot)
                            CollectNestedGeneratedSources(selected.transform, result);
                    }
                }
                else
                {
                    // includeInactive: a completed bake disables the original it replaced.
                    // Refusing an inactive object here is what made "create the missing
                    // ones by hand" impossible - the object the author had just clicked in
                    // the Hierarchy resolved to no usable source at all.
                    AddSourceCandidate(result,
                        NormalizeSourceCandidate(selected), true);
                }
            }
            return result;
        }

        /// <summary>
        /// Collects nested mesh owners, but never descends into a generated destructible:
        /// its fragments are output, not sources, and one baked object can hold hundreds
        /// of them. Sweeping them per MeshFilter - each one resolved back up to its root
        /// and re-checked for a usable mesh - is what made a repaint crawl once a batch
        /// had placed its results inside the selected hierarchy.
        /// </summary>
        static void CollectNestedMeshSources(Transform current,
            List<GameObject> result,
            IReadOnlyDictionary<GameObject, GameObject> generatedByOriginal)
        {
            if (current == null) return;
            if (current.GetComponent<DestructibleRoot>() != null)
            {
                // A generated subtree found inside an authored hierarchy is output, not
                // another source. Directly selecting that generated root is handled by
                // the caller above and still produces one rebake candidate.
                return;
            }
            if (generatedByOriginal != null &&
                generatedByOriginal.TryGetValue(current.gameObject,
                    out GameObject generated))
            {
                AddSourceCandidate(result, generated, true);
                return;
            }
            MeshFilter filter = current.GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh != null)
                AddSourceCandidate(result,
                    NormalizeSourceCandidate(current.gameObject), true);
            for (int i = 0; i < current.childCount; i++)
                CollectNestedMeshSources(current.GetChild(i), result,
                    generatedByOriginal);
        }

        static void CollectNestedGeneratedSources(Transform current,
            List<GameObject> result)
        {
            if (current == null) return;
            DestructibleRoot generated = current.GetComponent<DestructibleRoot>();
            if (generated != null)
            {
                AddSourceCandidate(result, generated.gameObject, true);
                // A generated destructible owns many fragment MeshFilters. Once its root
                // is accepted, its output hierarchy must never be scanned as more sources.
                return;
            }
            for (int i = 0; i < current.childCount; i++)
                CollectNestedGeneratedSources(current.GetChild(i), result);
        }

        static Dictionary<GameObject, GameObject> BuildGeneratedSourceLookup()
        {
            var result = new Dictionary<GameObject, GameObject>();
            DestructibleRoot[] roots =
                Resources.FindObjectsOfTypeAll<DestructibleRoot>();
            for (int i = 0; i < roots.Length; i++)
            {
                DestructibleRoot root = roots[i];
                if (root == null || !root.gameObject.scene.IsValid() ||
                    EditorUtility.IsPersistent(root) ||
                    !HasUsableMesh(root.gameObject, true))
                    continue;

                AddGeneratedSourceLink(result, root.OriginalSourceObject,
                    root.gameObject);
                if (root.OriginalSourceObjects == null) continue;
                for (int sourceIndex = 0;
                     sourceIndex < root.OriginalSourceObjects.Count;
                     sourceIndex++)
                    AddGeneratedSourceLink(result,
                        root.OriginalSourceObjects[sourceIndex], root.gameObject);
            }
            return result;
        }

        static void AddGeneratedSourceLink(
            IDictionary<GameObject, GameObject> lookup, GameObject original,
            GameObject generated)
        {
            if (original == null || generated == null ||
                !original.scene.IsValid() || original.scene != generated.scene ||
                lookup.ContainsKey(original))
                return;
            lookup.Add(original, generated);
        }

        static void AddSourceCandidate(List<GameObject> result, GameObject candidate,
            bool includeInactive)
        {
            if (HasUsableMesh(candidate, includeInactive) && !result.Contains(candidate))
                result.Add(candidate);
        }

        static GameObject NormalizeSourceCandidate(GameObject selected)
        {
            if (selected == null) return null;
            DestructibleRoot bakedRoot = selected.GetComponentInParent<DestructibleRoot>(true);
            return bakedRoot != null && bakedRoot.gameObject.scene.IsValid()
                ? bakedRoot.gameObject
                : selected;
        }

        static bool HasUsableMesh(GameObject source, bool includeInactive = false)
        {
            if (source == null || !source.scene.IsValid()) return false;
            var filter = source.GetComponentInChildren<MeshFilter>(includeInactive);
            return filter != null && filter.sharedMesh != null;
        }
        void OnEnable()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorApplication.update += ProcessScheduledPreview;
            // The Scene handle writes to the profile outside this window's change check,
            // so it has to say so explicitly or the preview keeps the old hit position.
            PatternHintOverlay.AddImpactPointMovedListener(OnImpactPointMoved);
            m_selectedSources ??= new List<GameObject>();
            m_fracturePreview ??= new FracturePreviewSession();
            AdoptSelectionIfSourceMissing();
            // OnEnable also runs while a script/domain reload is still importing assets.
            // Creating the legacy scratch profile here can write its .asset successfully,
            // then re-enter OnEnable before AssetDatabase can load that new file and try to
            // create the same path a second time. Defer every profile write/load until the
            // database reports that compilation and refresh have both finished.
            QueueSelectedProfileInitialization();
        }

        void QueueSelectedProfileInitialization()
        {
            if (m_profileInitializationQueued) return;
            m_profileInitializationQueued = true;
            EditorApplication.delayCall += InitializeSelectedProfileWhenReady;
        }

        void InitializeSelectedProfileWhenReady()
        {
            EditorApplication.delayCall -= InitializeSelectedProfileWhenReady;
            m_profileInitializationQueued = false;
            if (this == null) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                QueueSelectedProfileInitialization();
                return;
            }

            EnsureSelectedProfile();
            EnsureInteriorMaterialAssigned(m_profile, false);
            Repaint();
        }
        void OnFocus()
        {
            m_selectionRefreshPending = true;
            RefreshProfileLibrary(true);
            // A report is otherwise rebuilt only when the source object reference changes,
            // so fixing the cause — splitting a multi-mesh model, reimporting, enabling
            // Read/Write — left Bake disabled until an unrelated object was selected and
            // the original picked again. Re-checking only while blocked keeps the healthy
            // path free of analysis cost on every focus.
            if (m_source != null && m_report != null &&
                m_report.Status == AnalysisSeverity.Blocked)
                m_report = AnalyzeCurrentSource(false);
            Repaint();
        }

        void OnSelectionChange()
        {
            // Selection callbacks may arrive between this window's Layout and Repaint
            // events. Adopt the new source at the start of the next Layout so the two
            // passes always see the same IMGUI control tree.
            m_selectionRefreshPending = true;
            Repaint();
        }

        void OnDisable()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.update -= ProcessScheduledPreview;
            PatternHintOverlay.RemoveImpactPointMovedListener(OnImpactPointMoved);
            EditorApplication.delayCall -= InitializeSelectedProfileWhenReady;
            m_profileInitializationQueued = false;
            ReleaseCombinedSource();
            PatternHintOverlay.Hide();
        }

        void OnImpactPointMoved()
        {
            ScheduleFracturePreview();
            Repaint();
        }

        void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingEditMode)
                EndFracturePreviews();
            Repaint();
        }

        // ------------------------------------------------------------------ GUI root

        void OnGUI()
        {
            if (Event.current == null || Event.current.type == EventType.Layout)
                m_sourceCandidateCache = null;
            ProcessDeferredLayoutChanges();
            QDUI.EnsureStyles();
            QDUI.FillBackground(new Rect(0f, 0f, position.width, position.height));

            QDUI.DrawHeader(position.width,
                "Quick", "Destruction",
                "ANALYZE  |  HIERARCHICAL FRACTURE  |  STRUCTURAL COLLAPSE",
                "PRE-FRACTURE  •  LEVEL-TWO BREAKUP  •  SUPPORTS  •  RESET",
                "v1.9.0");

            EditorGUILayout.BeginHorizontal();
            DrawSidebar();
            DrawContent();
            EditorGUILayout.EndHorizontal();

            DrawActionFooter();
            DrawStatusBar();
        }

        void DrawActionFooter()
        {
            s_gui.ActionFooterStyle ??= new GUIStyle
            {
                normal = { background = QDUI.Tex(QDUI.FromHex("#0C0C0C")) },
                padding = new RectOffset(12, 12, 8, 8),
                fixedHeight = ActionFooterHeight
            };

            s_gui.FooterBlockerStyle ??= new GUIStyle(EditorStyles.label)
            {
                fontSize = 10,
                wordWrap = false,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = QDUI.Red },
                hover = { textColor = QDUI.Red }
            };

            bool hasSource = HasSourceOrSelectionCandidate();
            // The footer draws a control that only exists while blocked, so it has to read
            // the report frozen for this Layout/Repaint pair. m_report itself can change in
            // a selection callback between the two passes.
            AnalysisReport footerReport = m_layoutReport;
            bool blocked = footerReport != null &&
                           footerReport.Status == AnalysisSeverity.Blocked;
            // Enabling a control is not a layout decision, so the gate stays on the live
            // report: the frozen one is null until the first Layout event of a fresh window
            // and must never be the reason a blocked source gets a clickable Bake button.
            bool blockedNow = blocked ||
                              (m_report != null &&
                               m_report.Status == AnalysisSeverity.Blocked);
            bool canPreview = !EditorApplication.isPlaying && m_source != null &&
                              m_profile != null && !blockedNow;
            bool canBake = m_source != null && m_profile != null && !blockedNow;

            string state;
            Color stateColor;
            if (!hasSource)
            {
                state = "SELECT SOURCE";
                stateColor = QDUI.TextDim;
            }
            else if (blockedNow)
            {
                state = "BLOCKED";
                stateColor = QDUI.Red;
            }
            else if (m_geometryDirty && m_contextRoot != null)
            {
                state = "REBAKE REQUIRED";
                stateColor = QDUI.Amber;
            }
            else
            {
                state = m_contextRoot != null ? "BAKED / READY" : "READY TO BAKE";
                stateColor = QDUI.Green;
            }

            EditorGUILayout.BeginHorizontal(s_gui.ActionFooterStyle);
            EditorGUILayout.BeginVertical(GUILayout.Width(142f));
            GUILayout.Label("CURRENT STATE", QDUI.StatLabel);
            QDUI.StatusPill(state, stateColor, 132f,
                blockedNow ? DescribeBlockers(footerReport ?? m_report) : null);
            EditorGUILayout.EndVertical();
            GUILayout.Space(8f);

            bool previewActive = IsFracturePreviewActive();
            bool previewSolving = m_layoutSolving;
            using (new EditorGUI.DisabledScope(
                       !previewSolving && !previewActive && !canPreview))
            {
                string previewLabel = previewSolving
                    ? "CANCEL  " + Mathf.RoundToInt(
                          m_layoutSolveProgress * 100f) + "%"
                    : previewActive
                        ? "BACK TO NORMAL VIEW"
                        : "PREVIEW";
                if (GUILayout.Button(previewLabel,
                        QDUI.GhostBtn, GUILayout.Width(
                            previewActive ? 170f : 120f), GUILayout.Height(32f)))
                {
                    if (previewSolving)
                        CancelFracturePreviewSolve();
                    else if (previewActive)
                        EndFracturePreviews();
                    else
                        GenerateFracturePreview(true);
                }
            }
            // A dead Bake button has to name its own reason here. The tooltip carries the
            // full report without requiring a click; clicking remains a shortcut to the
            // Start page where the same report is visible in context.
            if (blocked)
            {
                GUILayout.Space(10f);
                var blockerContent = new GUIContent(
                    "Bake blocked — " + DescribeFirstBlocker(footerReport) +
                    "   (hover for the full report)",
                    DescribeBlockers(footerReport));
                if (GUILayout.Button(blockerContent, s_gui.FooterBlockerStyle,
                        GUILayout.MaxWidth(Mathf.Max(120f, position.width - 620f)),
                        GUILayout.Height(32f)))
                {
                    m_workspace = Workspace.Analyze;
                    GUIUtility.ExitGUI();
                }
            }
            GUILayout.FlexibleSpace();
            if (position.width >= 850f)
            {
                EditorGUILayout.BeginVertical(GUILayout.Width(104f));
                GUILayout.Label("SETTING SCOPE", QDUI.StatLabel);
                GUILayout.Label("BAKE  /  PROFILE", QDUI.HelpText);
                EditorGUILayout.EndVertical();
            }
            GUILayout.Space(6f);
            using (new EditorGUI.DisabledScope(!canBake))
            {
                if (GUILayout.Button("BAKE  &  SETUP",
                        QDUI.AccentBtn, GUILayout.Width(190f), GUILayout.Height(34f)))
                {
                    RunBake();
                    GUIUtility.ExitGUI();
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        // ------------------------------------------------------------------ sidebar

        void DrawSidebar()
        {
            s_gui.SidebarStyle ??= new GUIStyle
            {
                normal = { background = QDUI.Tex(QDUI.BgSidebar) },
                padding = new RectOffset(0, 0, 6, 6)
            };
            EditorGUILayout.BeginVertical(s_gui.SidebarStyle,
                GUILayout.Width(SidebarWidth), GUILayout.ExpandHeight(true));
            m_sidebarScroll = EditorGUILayout.BeginScrollView(m_sidebarScroll);

            DrawSettingsSearchField();

            QDUI.SidebarLabel("MAIN PAGE");
            DrawWorkspaceButton(Workspace.Analyze, "Start — Quick Setup");
            QDUI.SidebarLabel("DETAIL TABS");
            DrawWorkspaceSubButton(Workspace.Fracture, "Fracture",
                "Assets/QuickDestruction/Editor/Icons/Fracture.png",
                "Fragment shape, count, direction and cut surfaces", "#");
            DrawWorkspaceSubButton(Workspace.Physics, "Physics",
                "Assets/QuickDestruction/Editor/Icons/Physics.png",
                "Mass, colliders, impact force and simulation", "O");
            DrawWorkspaceSubButton(Workspace.Structure, "Structure",
                "Assets/QuickDestruction/Editor/Icons/Structure.png",
                "Supports, unsupported release and structural collapse", "H");
            DrawWorkspaceSubButton(Workspace.Runtime, "Runtime",
                "Assets/QuickDestruction/Editor/Icons/Runtime.png",
                "Damage, runtime re-fracture, effects and cleanup", ">");
            DrawWorkspaceSubButton(Workspace.Bake, "Bake & Output",
                "Assets/QuickDestruction/Editor/Icons/Bake.png",
                "Bake settings, generated assets and cleanup", "B");

            QDUI.SidebarSeparator();
            QDUI.SidebarLabel("PLAY PREVIEW");
            DestructibleRoot previewTarget = FindPreviewTarget();
            bool canPreview = EditorApplication.isPlaying && previewTarget != null;
            using (new EditorGUI.DisabledScope(!canPreview))
            {
                if (QDUI.DrawSidebarButton("Impact (local)", false)) PreviewImpact();
                if (QDUI.DrawSidebarButton("Explosion (radial)", false)) PreviewExplosion();
                if (QDUI.DrawSidebarButton("Break All (full)", false)) PreviewBreakAll();
                using (new EditorGUI.DisabledScope(
                           previewTarget == null ||
                           previewTarget.BakedSecondaryFragmentCount == 0))
                {
                    if (QDUI.DrawSidebarButton("Level-Two Hit", false))
                        PreviewSecondaryHit();
                }
                if (QDUI.DrawSidebarButton("Stress x10", false)) PreviewStress();
                if (QDUI.DrawSidebarButton("Reset All", false)) PreviewReset();
            }
            DrawSidebarHint(
                "Impact breaks a small camera-facing area. Explosion uses the profile radius, so " +
                "large objects may keep an unbroken shell. Break All activates every baked piece.");
            GUILayout.Space(2f);
            GUILayout.Label(
                EditorApplication.isPlaying
                    ? canPreview ? "  Play preview ready" : "  Select a destructible to test"
                    : "  Enter Play Mode to test",
                QDUI.SidebarSection);

            QDUI.SidebarSeparator();
            QDUI.SidebarLabel("TOGGLES");
            if (QDUI.DrawSidebarButton((m_autoAnalyze ? "[x]" : "[ ]") + "   Auto Analyze Source", m_autoAnalyze))
                m_autoAnalyze = !m_autoAnalyze;
            DrawSidebarHint("Checks the mesh again whenever the selected Source changes.");
            if (QDUI.DrawSidebarButton((m_placeInScene ? "[x]" : "[ ]") + "   Place Result In Scene", m_placeInScene))
                m_placeInScene = !m_placeInScene;
            DrawSidebarHint("After Bake, inserts the prefab at the Source transform and disables the original.");

            GUILayout.FlexibleSpace();

            QDUI.SidebarSeparator();
            QDUI.SidebarLabel("STATS");
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(10f);
            EditorGUILayout.BeginVertical();
            int fragments = m_profile != null ? m_profile.FragmentCount : 0;
            string activeMax = m_profile == null
                ? "-"
                : (m_profile.EnforceActiveBodyBudget
                    ? m_profile.MaxActiveFragments.ToString()
                    : "Unlimited");
            bool secondaryEnabled = m_profile != null &&
                                    m_profile.SecondaryBreakup != SecondaryBreakupLevel.Off;
            int secondaryEstimate = secondaryEnabled
                ? m_profile.SecondaryEligibleFragments * m_profile.SecondaryPiecesPerFragment
                : 0;
            bool hasReport = m_report != null && m_report.Mesh != null;
            string secondaryActive = !secondaryEnabled
                ? "Off"
                : (m_profile.EnforceActiveBodyBudget
                    ? m_profile.MaxActiveSecondaryBodies.ToString()
                    : "Unlimited");
            string statsText =
                $"Max Parts: {fragments}\n" +
                $"Active Max: {activeMax}\n" +
                $"Level-Two (est): {(secondaryEnabled ? secondaryEstimate.ToString() : "Off")}\n" +
                $"Level-Two Active: {secondaryActive}\n" +
                (hasReport ? $"Base Tris: {m_report.TriangleCount:n0}\n" : "Base Tris: -\n") +
                (hasReport
                    ? $"Cache Est: {m_report.EstimatedCacheMB(fragments):0.0} MB"
                    : "Cache Est: -");
            // A single multiline control keeps the sidebar layout identical even when
            // profile/report values are replaced by selection, Analyze or Bake.
            GUILayout.Label(statsText, QDUI.BodyDim,
                GUILayout.Width(SidebarWidth - 20f));
            GUILayout.Space(4f);
            QDUI.StatusPill(
                hasReport ? m_report.RiskLabel + " RISK" : "NOT ANALYZED",
                hasReport ? RiskColor(m_report.RiskLabel) : QDUI.TextDim, 108f);
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(6f);

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();

            // Sidebar divider line.
            var r = GUILayoutUtility.GetLastRect();
            EditorGUI.DrawRect(new Rect(r.xMax - 1f, r.y, 1f, r.height), QDUI.Line);
        }

        /// <summary>
        /// Finds a control by name across all six tabs. Six pages of settings are more than
        /// anyone memorises, and the old answer to "where is Collapse Delay" was to open
        /// each tab in turn.
        /// </summary>
        void DrawSettingsSearchField()
        {
            QDUI.SidebarLabel("SEARCH SETTINGS");
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(8f);
            GUI.SetNextControlName(SearchFieldControlName);
            EditorGUI.BeginChangeCheck();
            m_searchQuery = EditorGUILayout.TextField(m_searchQuery);
            if (EditorGUI.EndChangeCheck()) Repaint();
            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(m_searchQuery)))
            {
                if (GUILayout.Button("x", QDUI.GhostBtn, GUILayout.Width(20f)))
                {
                    ClearSettingsSearch();
                    GUIUtility.ExitGUI();
                }
            }
            GUILayout.Space(8f);
            EditorGUILayout.EndHorizontal();
            DrawSidebarHint("Type part of a setting name, for example \"support\" or " +
                            "\"despawn\", and jump straight to it.");
        }

        const string SearchFieldControlName = "QuickDestructionSettingsSearch";

        void ClearSettingsSearch()
        {
            m_searchQuery = string.Empty;
            // Leaving the cleared field focused swallows the next keystrokes aimed at the
            // Scene view, and the caret would sit in a box the user just emptied.
            if (GUI.GetNameOfFocusedControl() == SearchFieldControlName)
                GUI.FocusControl(null);
        }

        /// <summary>
        /// Result list for the current query, occupying the right content pane. Clicking a row
        /// switches to the owning tab, opens the card holding the control and marks its
        /// label, so the jump ends on the setting rather than merely near it.
        /// </summary>
        void DrawSettingsSearchResults()
        {
            const int MaxShown = 12;
            List<QuickDestructionSettingsIndex.Entry> matches =
                QuickDestructionSettingsIndex.Search(m_layoutSearchQuery, MaxShown);
            int total = QuickDestructionSettingsIndex.CountMatches(m_layoutSearchQuery);

            QDUI.BeginCard();
            // The typed text is not echoed into a header: these styles enable rich text,
            // and a stray angle bracket would be parsed as markup rather than shown.
            QDUI.Header("Settings Search");
            if (matches.Count == 0)
            {
                QDUI.InfoBox(
                    "No setting matches that text. The search covers every control name " +
                    "and the card it sits on, across all six tabs.");
                QDUI.EndCard();
                GUILayout.Space(6f);
                return;
            }

            for (int i = 0; i < matches.Count; i++)
            {
                QuickDestructionSettingsIndex.Entry entry = matches[i];
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button(QDUI.FieldContent(entry.Label), QDUI.GhostBtn, GUILayout.Width(280f)))
                {
                    GoToSetting(entry);
                    GUIUtility.ExitGUI();
                }
                QDUI.CaptureField(entry.Label);
                GUILayout.Space(10f);
                if (GUILayout.Button(new GUIContent(entry.Path,
                    QDUI.FieldContent(entry.Label).tooltip), QDUI.BodyDim, GUILayout.ExpandWidth(true)))
                {
                    GoToSetting(entry);
                    GUIUtility.ExitGUI();
                }
                EditorGUILayout.EndHorizontal();
                GUILayout.Space(2f);
            }

            GUILayout.Label(total > matches.Count
                    ? $"Showing {matches.Count} of {total} matches — add another word to narrow it down."
                    : $"{total} match{(total == 1 ? string.Empty : "es")}.",
                QDUI.HelpText);
            QDUI.EndCard();
            GUILayout.Space(6f);
        }

        void GoToSetting(QuickDestructionSettingsIndex.Entry entry)
        {
            m_workspace = entry.Workspace;
            QDUI.SetFoldOpen(entry.Section, true);
            if (entry.Workspace == Workspace.Fracture)
                QDUI.SetFoldOpen("Fracture Settings — Full Control", true);
            // Start the one-second highlight only once the destination was measured.
            QDUI.BeginSettingsHighlight(entry.Label, entry.Section);
            m_pendingSettingLabel = entry.Label;
            m_pendingSettingSection = entry.Section;
            m_pendingSettingRepaints = 0;
            m_settingJumpScrolled = false;
            m_highlightWorkspace = entry.Workspace;
            m_scroll = Vector2.zero;
            ClearSettingsSearch();
            Repaint();
            ShowStatus($"{entry.Path}  ›  {entry.Label}");
        }

        void CompleteSettingJump()
        {
            if (Event.current.type != EventType.Repaint || m_pendingSettingLabel == null) return;
            bool found = QDUI.TryGetSettingRect(m_pendingSettingLabel, out Rect target);
            if (!found && ++m_pendingSettingRepaints < 2) { Repaint(); return; }
            if (!m_settingJumpScrolled && (found || QDUI.TryGetSectionRect(m_pendingSettingSection, out target)))
            {
                m_scroll.y = Mathf.Max(0f, target.y - 30f);
                m_settingJumpScrolled = true;
                // The current Repaint still shows the old scroll position. Wait for
                // the destination frame before starting its visible one-second cue.
                if (found) { Repaint(); return; }
            }
            if (found)
                QDUI.ShowSettingsHighlight();
            if (!found)
                ShowStatus($"{m_pendingSettingLabel}: unavailable with the current pattern or options. " +
                    "Open its section and check the prerequisite settings.");
            m_pendingSettingLabel = null;
            Repaint();
        }

        void OnInspectorUpdate()
        {
            if (m_pendingSettingLabel != null || (QDUI.HasSettingHighlight &&
                EditorApplication.timeSinceStartup < QDUI.SettingHighlightUntil + 0.2d)) Repaint();
        }

        static void DrawSidebarHint(string text)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(12f);
            GUILayout.Label(text, QDUI.HelpText, GUILayout.Width(SidebarWidth - 24f));
            GUILayout.Space(12f);
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(2f);
        }

        void DrawWorkspaceButton(Workspace ws, string label)
        {
            if (QDUI.DrawSidebarMainButton(label, m_workspace == ws,
                    "d_Favorite", "Select a source, choose a preset and bake the complete setup",
                    "Q") && (m_workspace != ws || !string.IsNullOrWhiteSpace(m_searchQuery)))
            {
                m_workspace = ws;
                ClearSettingsSearch();
                m_pendingSettingLabel = null;
                QDUI.ClearSettingsHighlight();
                // The destination workspace has a different control tree. Start it on
                // a fresh Layout event instead of continuing this mouse event.
                GUIUtility.ExitGUI();
            }
        }

        void DrawWorkspaceSubButton(Workspace ws, string label,
            string iconName, string tooltip, string fallbackGlyph)
        {
            if (QDUI.DrawSidebarSubButton(label, m_workspace == ws,
                    iconName, tooltip, fallbackGlyph) &&
                (m_workspace != ws || !string.IsNullOrWhiteSpace(m_searchQuery)))
            {
                m_workspace = ws;
                ClearSettingsSearch();
                m_pendingSettingLabel = null;
                QDUI.ClearSettingsHighlight();
                GUIUtility.ExitGUI();
            }
        }

        internal static string WorkspaceTitle(Workspace ws)
        {
            switch (ws)
            {
                case Workspace.Fracture: return "Fracture";
                case Workspace.Physics: return "Physics";
                case Workspace.Structure: return "Structure";
                case Workspace.Runtime: return "Runtime";
                case Workspace.Bake: return "Bake & Output";
                default: return "Start";
            }
        }

        static Color RiskColor(string label)
        {
            switch (label)
            {
                case "LOW": return QDUI.Green;
                case "MEDIUM": return QDUI.Amber;
                case "BLOCKED": return QDUI.Red;
                default: return QDUI.Red;
            }
        }

        static string GetColliderDescription(ColliderStrategy strategy)
        {
            switch (strategy)
            {
                case ColliderStrategy.PrimitiveBox:
                    return "Primitive Box creates one inexpensive BoxCollider per simulated fragment. " +
                           "Use it for mobile, distant debris and box-like props where exact contact shape is unnecessary.";
                case ColliderStrategy.ExactMesh:
                    return "Exact Mesh uses the final fragment geometry after Hollow Shell / Solidify, including its " +
                           "holes, wall thickness and concave surfaces, while static or kinematic. For a moving Rigidbody " +
                           "it switches to a pre-baked compound made from several smaller fitted parts, because Unity does " +
                           "not support one dynamic non-convex MeshCollider. The compound preserves cavities instead of " +
                           "wrapping the whole fragment in one large hull.";
                default:
                    return "Convex Mesh creates a fitted convex MeshCollider for every fragment and a Rigidbody " +
                           "for every simulated fragment. It gives the best physical shape but has the highest bake and runtime cost. " +
                           "Every released fragment is validated again at runtime: it must have a Rigidbody and at least " +
                           "one enabled dynamic-safe Collider before it may enter the physics simulation.";
            }
        }

        static ColliderStrategy ColliderStrategyRow(string label,
            ColliderStrategy strategy)
        {
            int selected = 0;
            for (int i = 0; i < ColliderValues.Length; i++)
                if (ColliderValues[i] == strategy) { selected = i; break; }
            selected = QDUI.PopupRow(label, selected, ColliderNames);
            return ColliderValues[Mathf.Clamp(selected, 0, ColliderValues.Length - 1)];
        }

        static string ColliderStrategyName(ColliderStrategy strategy)
        {
            for (int i = 0; i < ColliderValues.Length; i++)
                if (ColliderValues[i] == strategy) return ColliderNames[i];
            return ColliderNames[0];
        }

        /*
        static string GetTierDescription(QualityTier tier)
        {
            switch (tier)
            {
                case QualityTier.Mobile:
                    return "Mobile targets 8–24 larger fragments and BoxColliders, then enables timed/rest-based " +
                           "recycling. After Bake, the whole source mesh is repartitioned into fewer pieces; " +
                           "nothing is merged at runtime and the object still breaks completely.";
                case QualityTier.High:
                    return "High targets 80–250 smaller baked fragments for close-up hero destruction. " +
                           "Rebake to regenerate the whole mesh at this density; active-body limits remain " +
                           "off unless an expert enables the optional Physics Budget.";
                default:
                    return "Balanced targets 24–80 baked fragments with convex collision and unlimited " +
                           "activity by default. The source is repartitioned on Bake, never partially merged " +
                           "at runtime; optional cleanup and level-two settings remain available.";
            }
        }
        */

        static string GetCleanupDescription(CleanupAction action)
        {
            switch (action)
            {
                case CleanupAction.None:
                    return "None leaves settled fragments fully owned by gameplay. Use it when pieces " +
                           "must remain interactive or be managed by another system.";
                case CleanupAction.Recycle:
                    return "Despawn removes settled debris from view and returns it to the baked pool. " +
                           "It is not Destroy: Reset can bring the same baked pieces back immediately.";
                default:
                    return "Freeze As Proxy keeps settled debris visible but makes its Rigidbody " +
                           "kinematic, preserving the scene result without continued simulation cost.";
            }
        }

        static string GetIntactBodyDescription(IntactBodyMode mode)
        {
            switch (mode)
            {
                case IntactBodyMode.Dynamic:
                    return "Dynamic Rigidbody starts simulating immediately. Use it for loose props " +
                           "that should already fall, roll and receive collision damage before breaking.";
                case IntactBodyMode.ReleaseWhenUnsupported:
                    return "Release When Unsupported monitors every disconnected structural element " +
                           "independently. A post that loses its footing collapses without releasing the " +
                           "other posts immediately. Ground-contact probes from all walls also form one " +
                           "foundation group: crossing its total threshold releases the construction. " +
                           "Carried floors and roofs remain evaluated as their own local groups. " +
                           "Neighbouring destructibles count as footing too, so a Root + Nested Meshes " +
                           "building holds itself up: an element standing on a panel that is destroyed " +
                           "comes apart into its baked pieces, while one that simply loses the ground " +
                           "under it falls whole and breaks where it lands.";
                default:
                    return "Static / Fixed is the cheapest default. The intact object never moves, " +
                           "but scripted or collision damage can still replace it with baked fragments.";
            }
        }

        static string GetSupportDetectionDescription()
        {
            return "Automatic Below casts a small grid of rays under every disconnected " +
                   "structural element. Ordinary colliders count as support; released or " +
                   "fractured destructibles stop counting. Another destructible standing " +
                   "under this one counts for as long as its material is still there, " +
                   "including a modular panel this one overlaps, which no downward ray " +
                   "can reach. Support Probe Distance is the reach for both.";
        }

        // ------------------------------------------------------------------ content

        void DrawContent()
        {
            EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            bool searching = !string.IsNullOrWhiteSpace(m_layoutSearchQuery);
            Vector2 scroll = EditorGUILayout.BeginScrollView(searching ? m_searchResultsScroll : m_scroll);
            if (searching) m_searchResultsScroll = scroll;
            else m_scroll = scroll;
            QDUI.BeginSettingsCapture();
            try
            {
                GUILayout.Space(10f);
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(12f);
                EditorGUILayout.BeginVertical();

                // Search has its own view and scroll position. A deeply scrolled tab
                // must not hide the results, or shift the destination's measured offset.
                if (searching)
                    DrawSettingsSearchResults();
                else
                {
                    if (m_workspace != Workspace.Analyze)
                    {
                        // Detail tabs are sub-pages of Start: a breadcrumb row keeps the way
                        // back to the main page always one click away.
                        EditorGUILayout.BeginHorizontal();
                        if (GUILayout.Button("<  Start", QDUI.GhostBtn, GUILayout.Width(68f)))
                        {
                            m_workspace = Workspace.Analyze;
                            GUIUtility.ExitGUI();
                        }
                        GUILayout.Space(8f);
                        GUILayout.Label($"Detail tab:  {WorkspaceTitle(m_workspace)}", QDUI.BodyDim);
                        GUILayout.FlexibleSpace();
                        EditorGUILayout.EndHorizontal();
                        GUILayout.Space(6f);
                    }

                    // Start renders the source list inside its Step 1 card; Fracture shows a
                    // compact source row inside its essentials card.
                    if (m_workspace != Workspace.Analyze && m_workspace != Workspace.Fracture)
                        DrawSourceRow();

                    switch (m_workspace)
                    {
                        case Workspace.Analyze: DrawAnalyzeTab(); break;
                        case Workspace.Fracture: DrawFractureTab(); break;
                        case Workspace.Physics: DrawPhysicsTab(); break;
                        case Workspace.Structure: DrawStructureTab(); break;
                        case Workspace.Runtime: DrawRuntimeTab(); break;
                        case Workspace.Bake: DrawBakeTab(); break;
                    }

                }

                GUILayout.Space(16f);
                EditorGUILayout.EndVertical();
                GUILayout.Space(12f);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndScrollView();
                Rect viewport = GUILayoutUtility.GetLastRect();
                EditorGUILayout.EndVertical();
                if (!searching)
                {
                    CompleteSettingJump();
                    QDUI.DrawSettingsHighlight(viewport);
                }
            }
            finally { QDUI.EndSettingsCapture(); }
        }

        List<GameObject> GetDisplayedSources()
        {
            // A copy: the fallbacks below append, and the candidate list is shared.
            var displayed = new List<GameObject>(IsWholeBuildingMode
                ? CollectWholeBuildingMeshObjects(GetWholeBuildingRoots())
                : GetSourceCandidates());
            // Keep an invalid selection visible so its NO MESH status explains why the
            // action is unavailable. In hierarchy mode, a valid root instead resolves to
            // the actual nested mesh objects that the batch will process.
            if (displayed.Count == 0 && m_selectedSources != null)
            {
                for (int i = 0; i < m_selectedSources.Count; i++)
                {
                    GameObject item = m_selectedSources[i];
                    if (item != null && item.scene.IsValid() && !displayed.Contains(item))
                        displayed.Add(item);
                }
            }
            if (displayed.Count == 0 && m_source != null)
                displayed.Add(m_source);
            return displayed;
        }

        void DrawContextRootInfo()
        {
            if (m_contextRoot == null) return;
            QDUI.InfoBox(
                $"EDITING DESTRUCTIBLE: {m_contextRoot.name}\n" +
                $"Profile: {(m_profile != null ? m_profile.name : "None")}. " +
                "Every workspace below now edits this object's assigned profile. " +
                "Runtime-only values apply immediately; fracture geometry requires Bake & Setup. " +
                "If this profile is shared, its other destructibles receive the same settings.");
            GUILayout.Space(4f);
        }

        void DrawSourceRow()
        {
            var displayed = GetDisplayedSources();
            QDUI.BeginCard();
            QDUI.Header(displayed.Count > 1
                ? IsWholeBuildingMode
                    ? $"Whole Building Inputs ({displayed.Count} meshes)"
                    : $"Selected Sources ({displayed.Count})"
                : "Selected Source");
            DrawContextRootInfo();
            DrawSourceListBody(displayed);
            QDUI.EndCard();
        }

        void DrawStepSourceCard()
        {
            var displayed = GetDisplayedSources();
            int readyCount = IsWholeBuildingMode
                ? (displayed.Count > 0 ? 1 : 0)
                : GetSourceCandidates().Count;
            QDUI.BeginWorkflowCard(1);
            QDUI.StepHeader("1", "SELECT OBJECT OR ADD SELECTED OBJECTS",
                readyCount > 1 ? $"{readyCount} OBJECTS SELECTED"
                : readyCount == 1 ? "OBJECT SELECTED" : "PICK IN SCENE",
                readyCount > 0 ? QDUI.Green : QDUI.Amber);
            GUILayout.Label(
                "Choose independent objects, collect every nested mesh as a batch, or combine " +
                "a modular building into one non-destructive fracture source.",
                QDUI.HelpText);
            GUILayout.Space(4f);

            EditorGUI.BeginChangeCheck();
            SourceSelectionMode nextMode = (SourceSelectionMode)QDUI.PopupRow(
                "Mode", (int)m_sourceSelectionMode, SourceSelectionModeNames);
            if (EditorGUI.EndChangeCheck())
            {
                SetSourceSelectionMode(nextMode);
                GUIUtility.ExitGUI();
            }
            GUILayout.Label(GetSourceSelectionModeDescription(m_sourceSelectionMode),
                QDUI.HelpText);
            if (IsWholeBuildingMode)
            {
                ApplyQuickHollowShellDefault();
                // A Hollow Shell exists to give an open panel the depth it was never
                // modelled with. A kit of closed solid modules already has that depth, so
                // the thickness controls describe nothing there and only invite a value
                // that changes no output. Hide them for such a source; the moment the
                // author turns the shell on deliberately they come back, and an open-panel
                // source keeps them visible because that is the case they steer.
                bool sourceIsClosedSolid = m_layoutReport != null &&
                                           m_layoutReport.OpenEdges == 0;
                bool hollowShellEnabled = m_profile != null
                    ? m_profile.Solidify
                    : m_quickSolidify;
                if (sourceIsClosedSolid && !hollowShellEnabled)
                {
                    GUILayout.Label(
                        "Closed solid modules detected — these already carry their own " +
                        "modelled depth, so Hollow Shell thickness does not apply. Enable " +
                        "Hollow Shell in the Fracture tab if you want to override that.",
                        QDUI.HelpText);
                }
                else
                {
                DrawModularHollowShellRecommendation();
                EditorGUI.BeginChangeCheck();
                bool showShellWhileIntact = QDUI.ToggleRow(
                    "Show Hollow Shell Thickness While Intact",
                    m_wholeBuildingShowShellWhileIntact);
                if (EditorGUI.EndChangeCheck())
                {
                    m_wholeBuildingShowShellWhileIntact = showShellWhileIntact;
                    MarkBakeAppearanceDirty();
                    ShowStatus(showShellWhileIntact
                        ? "The next Whole Building Bake will show its Hollow Shell thickness " +
                          "before the first hit. Original modules remain unchanged."
                        : "The next Whole Building Bake will keep the authored thin intact " +
                          "visual. Hollow Shell thickness appears after the first break.");
                }
                DrawWholeBuildingWallThickness();
                GUILayout.Label(
                    "Off (default): the Intact renderer keeps the authored module surfaces; " +
                    "its colliders still receive the selected wall thickness. On: Bake builds " +
                    "the Intact visual from the same prepared Hollow Shell as the fragments, " +
                    "preventing a visible thickness pop on first damage. This affects only " +
                    "HOLLOW SHELL output, never rewrites the original modules, and requires " +
                    "Bake & Setup after a change. Set Wall Thickness here; Bake confirms the " +
                    "same value in cm or m when open modular panels are detected.",
                    QDUI.HelpText);
                }
                GUILayout.Space(4f);
                QDUI.WarnBox(
                    $"Whole Building raises Max Parts to {MeshAnalyzer.MaxMergedAssemblyFragmentCount}. " +
                    "That is a bake allowance, not a promise that all pieces are cheap at runtime: " +
                    "enable active/visible fragment budgets for large collapses.");
            }
            GUILayout.Space(4f);
            DrawContextRootInfo();
            DrawSourceListBody(displayed);
            DrawBatchPanelSettings(displayed);
            if (displayed.Count > 0)
            {
                GUILayout.Space(4f);
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("REFRESH SCENE SELECTION", QDUI.GhostBtn,
                        GUILayout.Width(180f)))
                {
                    m_ignoreSelectionUntil = 0d;
                    AdoptSelectionIfSourceMissing();
                    if (m_sourceSelectionMode == SourceSelectionMode.RootHierarchy)
                        QueueBatchPanelPrompt();
                    int count = IsWholeBuildingMode
                        ? CollectWholeBuildingMeshObjects(GetWholeBuildingRoots()).Count
                        : GetSourceCandidates().Count;
                    ShowStatus(count > 0
                        ? IsWholeBuildingMode
                            ? $"Using {count} modular mesh(es) as one Whole Building source."
                            : $"Using {count} resolved mesh object(s) as Source."
                        : "The current selection has no usable mesh.");
                    GUIUtility.ExitGUI();
                }
                GUILayout.Label("Re-reads the current multi-selection from the Scene.", QDUI.HelpText);
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
            }
            QDUI.EndCard();
        }

        /// <summary>
        /// Open boundary edges under Whole Building mean the modules are panels with no
        /// authored depth. FULL SOLID fills each one, so a wall bakes as a block instead
        /// of a wall — the answer is known before the bake, and the window should say so
        /// rather than showing a FULL SOLID estimate and asking at the end.
        /// </summary>
        bool ModularSourceNeedsHollowShell =>
            IsWholeBuildingMode && m_layoutReport != null && m_layoutReport.OpenEdges > 0;

        /// <summary>
        /// Starts an open modular source on Hollow Shell. Quick mode owns its volume flag
        /// outright, so the default costs nothing and is applied silently, once per source.
        /// A source carrying a profile is left to
        /// <see cref="DrawModularHollowShellRecommendation"/>: writing its shell on can
        /// mean detaching a shared preset into a new asset, and selecting an object is not
        /// an instruction to create files.
        /// </summary>
        void ApplyQuickHollowShellDefault()
        {
            if (!ModularSourceNeedsHollowShell || m_profile != null) return;
            if (m_source == null || m_source == m_hollowShellDefaultedSource) return;
            m_hollowShellDefaultedSource = m_source;
            if (m_quickSolidify) return;

            m_quickSolidify = true;
            ShowStatus(
                "Open modular panels detected — Whole Building starts on HOLLOW SHELL at " +
                $"{m_quickSolidifyThickness * 100f:0.##} cm. Switch to FULL SOLID under " +
                "Volume & Colliders if the modules already have their own depth.");
        }

        void DrawModularHollowShellRecommendation()
        {
            if (!ModularSourceNeedsHollowShell || m_profile == null ||
                m_profile.Solidify) return;

            QDUI.WarnBox(
                "Open modular panels detected, and this profile is set to FULL SOLID: the " +
                "bake would fill every panel, so a wall becomes a block. HOLLOW SHELL gives " +
                "each panel the Wall Thickness below.");
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("USE HOLLOW SHELL", QDUI.AccentBtn, GUILayout.Width(180f)))
            {
                // Volume construction belongs to one source mesh, so a shared preset is
                // detached first — the same rule the manual FULL/HOLLOW switch follows.
                DestructionProfile local = EnsureVolumeModeIsObjectLocal(m_profile);
                if (local != null)
                {
                    Undo.RecordObject(local, "Use Hollow Shell For Modular Source");
                    local.Solidify = true;
                    local.SolidifyThickness = Mathf.Clamp(
                        local.SolidifyThickness, 0.001f, 2f);
                    m_profile = local;
                    m_quickSolidify = true;
                    m_quickSolidifyThickness = local.SolidifyThickness;
                    EditorUtility.SetDirty(local);
                    ScheduleFracturePreview();
                    ShowStatus("HOLLOW SHELL enabled at " +
                        $"{local.SolidifyThickness * 100f:0.##} cm for this modular building.");
                }
                GUIUtility.ExitGUI();
            }
            GUILayout.Label(
                "Sets this object's Volume Mode. A shared preset is copied first so other " +
                "objects using it are unaffected.", QDUI.HelpText);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(4f);
        }

        void DrawWholeBuildingWallThickness()
        {
            float currentMeters = m_profile != null
                ? m_profile.SolidifyThickness
                : m_quickSolidifyThickness;
            currentMeters = Mathf.Clamp(currentMeters,
                HollowShellThicknessPrompt.MinThicknessMeters,
                HollowShellThicknessPrompt.MaxThicknessMeters);
            HollowShellThicknessPrompt.ThicknessUnit unit =
                HollowShellThicknessPrompt.GetPreferredUnit();
            float displayValue = HollowShellThicknessPrompt.FromMeters(
                currentMeters, unit);

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.BeginHorizontal();
            QDUI.DrawFieldLabel("Wall Thickness", GUILayout.Width(QDUI.LabelWidth));
            float editedDisplay = EditorGUILayout.FloatField(displayValue);
            HollowShellThicknessPrompt.ThicknessUnit editedUnit =
                (HollowShellThicknessPrompt.ThicknessUnit)EditorGUILayout.EnumPopup(
                    unit, GUILayout.Width(112f));
            EditorGUILayout.EndHorizontal();
            bool changed = EditorGUI.EndChangeCheck();

            float editedMeters = HollowShellThicknessPrompt.ToMeters(
                editedDisplay, unit);
            if (editedUnit != unit)
            {
                HollowShellThicknessPrompt.SetPreferredUnit(editedUnit);
                // Preserve the physical value when only the display unit changes. The next
                // repaint converts it to the newly selected unit, exactly like the Bake modal.
                editedDisplay = HollowShellThicknessPrompt.FromMeters(
                    editedMeters, editedUnit);
                Repaint();
            }

            bool valid = HollowShellThicknessPrompt.IsValidThickness(editedMeters);
            if (valid)
            {
                GUILayout.Label(
                    editedUnit == HollowShellThicknessPrompt.ThicknessUnit.Centimeters
                        ? $"Stored as {editedMeters:0.####} m (1 Unity unit = 1 m)."
                        : $"Equivalent to {editedMeters * 100f:0.##} cm.",
                    QDUI.HelpText);
            }
            else
            {
                QDUI.WarnBox("Enter 0.1-200 cm (0.001-2 m).");
            }

            if (!changed || !valid ||
                Mathf.Approximately(editedMeters, currentMeters)) return;

            m_quickSolidifyThickness = editedMeters;
            if (m_profile != null)
            {
                DestructionProfile local = EnsureVolumeModeIsObjectLocal(m_profile);
                if (local == null) return;
                Undo.RecordObject(local, "Change Whole Building Wall Thickness");
                local.SolidifyThickness = editedMeters;
                m_profile = local;
                EditorUtility.SetDirty(local);
                if (local.Solidify)
                    ScheduleFracturePreview();
                else
                    MarkBakeAppearanceDirty();
            }
            ShowStatus($"Whole Building Wall Thickness set to " +
                $"{editedMeters * 100f:0.##} cm ({editedMeters:0.####} m).");
        }

        void DrawSourceListBody(List<GameObject> displayed)
        {
            if (displayed.Count == 0)
            {
                QDUI.InfoBox(
                    "Select a scene object. A mesh object becomes the Quick Setup source " +
                    "automatically; no drag-and-drop is required.");
                return;
            }

            if (displayed.Count == 1)
            {
                GameObject source = displayed[0];
                EditorGUILayout.BeginHorizontal();
                DrawSelectedSourceName(source);
                DrawSourceStatus(source, m_layoutReport);
                EditorGUILayout.EndHorizontal();
                GUILayout.Label(
                    "The selected scene object is used automatically. Select another mesh in the " +
                    "Scene or Hierarchy to switch Source.", QDUI.HelpText);
            }
            else
            {
                EditorGUILayout.BeginHorizontal();
                QDUI.DrawFieldLabel("#", GUILayout.Width(22f));
                QDUI.SubHeaderLabel("SCENE OBJECT");
                GUILayout.FlexibleSpace();
                GUILayout.Label("MESH", QDUI.SubHeader, GUILayout.Width(120f));
                GUILayout.Label("STATUS", QDUI.SubHeader, GUILayout.Width(82f));
                EditorGUILayout.EndHorizontal();

                for (int i = 0; i < displayed.Count; i++)
                {
                    GameObject source = displayed[i];
                    var filter = source.GetComponentInChildren<MeshFilter>();
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Label((i + 1).ToString(), QDUI.BodyDim, GUILayout.Width(22f));
                    DrawSelectedSourceName(source);
                    GUILayout.Label(filter != null && filter.sharedMesh != null
                            ? filter.sharedMesh.name
                            : "-",
                        QDUI.BodyDim, GUILayout.Width(120f));
                    DrawSourceStatus(source, m_layoutReport);
                    EditorGUILayout.EndHorizontal();
                }

                int validCount = IsWholeBuildingMode
                    ? displayed.Count
                    : GetSourceCandidates().Count;
                GUILayout.Label(
                    IsWholeBuildingMode
                        ? $"Whole Building ready: {validCount} modular mesh(es) will be Previewed " +
                          "and Baked as one destructible object."
                        : validCount == displayed.Count
                        ? $"Batch ready: Analyze + Bake will process all {validCount} selected mesh objects."
                        : $"Batch will process {validCount} mesh object(s); entries without a mesh are skipped.",
                    QDUI.HelpText);
            }

            AnalysisReport reportForLayout = m_layoutReport;
            if (reportForLayout != null)
            {
                GUILayout.Space(4f);
                EditorGUILayout.BeginHorizontal();
                StatusFromReport(reportForLayout);
                GUILayout.Space(10f);
                // Blockers that return before a single MeshFilter is resolved — skinned
                // source, no mesh, several mesh children — carry no geometry to summarize.
                // Gating the whole strip on Mesh != null hid their status pill and finding
                // as well, which is how a bake could be blocked with no visible reason.
                GUILayout.Label(
                    reportForLayout.Mesh != null
                        ? $"{reportForLayout.TriangleCount:n0} tris  •  {reportForLayout.SubmeshCount} submesh  •  " +
                          $"{reportForLayout.BoundsSize.x:0.##} x {reportForLayout.BoundsSize.y:0.##} x {reportForLayout.BoundsSize.z:0.##} m"
                        : "No single fracture mesh could be resolved from this source.",
                    QDUI.BodyDim);
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();

                if (reportForLayout.Status == AnalysisSeverity.Warning ||
                    reportForLayout.Status == AnalysisSeverity.Blocked)
                {
                    AnalysisSeverity detailSeverity = reportForLayout.Status;
                    AnalysisItem firstFinding = null;
                    int findingCount = 0;
                    for (int i = 0; i < reportForLayout.Items.Count; i++)
                    {
                        AnalysisItem item = reportForLayout.Items[i];
                        if (item == null || item.Severity != detailSeverity) continue;
                        if (firstFinding == null) firstFinding = item;
                        findingCount++;
                    }
                    if (firstFinding != null)
                    {
                        string label = detailSeverity == AnalysisSeverity.Blocked
                            ? "blocker"
                            : "warning";
                        string more = findingCount > 1
                            ? $"  (+{findingCount - 1} more {label}{(findingCount == 2 ? string.Empty : "s")})"
                            : string.Empty;
                        string details =
                            $"{firstFinding.Code}: {firstFinding.Title}{more}\n" +
                            $"Why: {firstFinding.Why}\nFix: {firstFinding.Fix}";
                        if (detailSeverity == AnalysisSeverity.Blocked)
                            QDUI.ErrorBox(details);
                        else
                            QDUI.WarnBox(details);
                    }
                }
            }
        }

        static void DrawSelectedSourceName(GameObject source)
        {
            Rect rect = GUILayoutUtility.GetRect(0f, 18f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, QDUI.FromHex("#20150D"));
            const float border = 1f;
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, border), QDUI.Accent);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - border, rect.width, border), QDUI.Accent);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, border, rect.height), QDUI.Accent);
            EditorGUI.DrawRect(new Rect(rect.xMax - border, rect.y, border, rect.height), QDUI.Accent);
            Texture icon = AssetPreview.GetMiniThumbnail(source);
            if (icon != null)
                GUI.DrawTexture(new Rect(rect.x + 3f, rect.y + 2f, 14f, 14f), icon,
                    ScaleMode.ScaleToFit, true);
            Color previous = GUI.color;
            GUI.color = QDUI.Accent;
            GUI.Label(new Rect(rect.x + 21f, rect.y + 1f, rect.width - 25f, rect.height - 2f),
                source.name, QDUI.Body);
            GUI.color = previous;
        }

        static void DrawSourceStatus(GameObject source, AnalysisReport report = null)
        {
            // The analyzer, not this shallow component scan, decides whether Bake may run.
            // GetComponentInChildren stops at the first MeshFilter, so a model root with
            // several mesh children reported SOURCE OK next to a disabled Bake button and
            // a red BLOCKED footer, with the actual reason nowhere in sight.
            if (report != null && report.Source == source &&
                report.Status == AnalysisSeverity.Blocked)
            {
                QDUI.StatusPill("BLOCKED", QDUI.Red, 82f, DescribeBlockers(report));
                return;
            }

            var filter = source != null ? source.GetComponentInChildren<MeshFilter>() : null;
            if (filter == null || filter.sharedMesh == null)
                QDUI.StatusPill("NO MESH", QDUI.Amber, 82f);
            else if (filter.GetComponent<MeshRenderer>() == null)
                QDUI.StatusPill("NO RENDERER", QDUI.Amber, 82f);
            else
                QDUI.StatusPill("SOURCE OK", QDUI.Green, 82f);
        }

        /// <summary>
        /// Every blocking finding as "code: title" plus its fix. The footer pill, the
        /// Source rows and the Bake button share this text so a disabled Bake states its
        /// reason wherever the user happens to be looking, not only on the Start page.
        /// </summary>
        static string DescribeBlockers(AnalysisReport report)
        {
            if (report == null) return string.Empty;
            var text = new System.Text.StringBuilder();
            for (int i = 0; i < report.Items.Count; i++)
            {
                AnalysisItem item = report.Items[i];
                if (item == null || item.Severity != AnalysisSeverity.Blocked) continue;
                if (text.Length > 0) text.Append("\n\n");
                text.Append(item.Code).Append(": ").Append(item.Title)
                    .Append("\nFix: ").Append(item.Fix);
            }
            return text.ToString();
        }

        /// <summary>Shortest honest headline for the footer: "QD-GEO-011: ... (+1 more)".</summary>
        static string DescribeFirstBlocker(AnalysisReport report)
        {
            if (report == null) return string.Empty;
            AnalysisItem first = null;
            int count = 0;
            for (int i = 0; i < report.Items.Count; i++)
            {
                AnalysisItem item = report.Items[i];
                if (item == null || item.Severity != AnalysisSeverity.Blocked) continue;
                first ??= item;
                count++;
            }
            if (first == null) return string.Empty;
            return count > 1
                ? $"{first.Code}: {first.Title}  (+{count - 1} more)"
                : $"{first.Code}: {first.Title}";
        }

        static void StatusFromReport(AnalysisReport report)
        {
            switch (report.Status)
            {
                case AnalysisSeverity.Info: QDUI.StatusPill("BAKE CHECK OK", QDUI.Green); break;
                case AnalysisSeverity.Warning: QDUI.StatusPill("WARNING", QDUI.Amber); break;
                case AnalysisSeverity.Blocked: QDUI.StatusPill("BLOCKED", QDUI.Red); break;
            }
        }

        // ------------------------------------------------------------------ analyze

        void DrawAnalyzeTab()
        {
            // A blocked source makes every step below it unusable, so the report leads the
            // page instead of sitting under three workflow cards the user cannot complete.
            // Warnings keep their old position at the bottom.
            bool blockedFirst = m_layoutReport != null &&
                                m_layoutReport.Status == AnalysisSeverity.Blocked;
            if (blockedFirst) DrawAnalyzeFoldCard();
            DrawStepSourceCard();
            DrawStepPresetCard();
            DrawStepBakeCard();
            DrawDetailTabsCard();
            if (!blockedFirst) DrawAnalyzeFoldCard();
        }

        void DrawDetailTabsCard()
        {
            QDUI.BeginCard();
            QDUI.Header("Detail Tabs");
            GUILayout.Label(
                "The three steps above are everything a standard prop needs. Full control " +
                "lives in the detail tabs — each edits the same selected profile.",
                QDUI.HelpText);
            GUILayout.Space(2f);
            EditorGUILayout.BeginHorizontal();
            if (QDUI.Chip("FRACTURE", false))
            {
                m_workspace = Workspace.Fracture;
                GUIUtility.ExitGUI();
            }
            if (QDUI.Chip("PHYSICS", false))
            {
                m_workspace = Workspace.Physics;
                GUIUtility.ExitGUI();
            }
            if (QDUI.Chip("STRUCTURE", false))
            {
                m_workspace = Workspace.Structure;
                GUIUtility.ExitGUI();
            }
            if (QDUI.Chip("RUNTIME", false))
            {
                m_workspace = Workspace.Runtime;
                GUIUtility.ExitGUI();
            }
            if (QDUI.Chip("BAKE & OUTPUT", false))
            {
                m_workspace = Workspace.Bake;
                GUIUtility.ExitGUI();
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            QDUI.EndCard();
        }

        void DrawAnalyzeFoldCard()
        {
            // Step 1 already summarizes a healthy automatic analysis. Keep the long
            // report out of the normal workflow and surface it only when actionable.
            // Read the frozen report: the card's control count depends on it, and a
            // selection callback can replace m_report between Layout and Repaint.
            AnalysisReport report = m_layoutReport;
            if (report == null || report.Status == AnalysisSeverity.Info)
                return;

            string status = report.Status == AnalysisSeverity.Blocked ? "BLOCKED"
                : report.Status == AnalysisSeverity.Warning ? "WARNING"
                : "CHECK PASSED";
            Color statusColor = report.Status == AnalysisSeverity.Blocked ? QDUI.Red
                : report.Status == AnalysisSeverity.Warning ? QDUI.Amber
                : QDUI.Green;
            if (!QDUI.BeginFoldCard("Source Health — Action Required", true,
                    status, statusColor))
            {
                QDUI.EndFoldCard();
                return;
            }

            GUILayout.Label(
                "The analyzer gates the bake: it prevents long bakes on bad data and explains " +
                "risk without requiring topology knowledge. Every finding has a <b>Why</b> and a <b>Fix</b>. " +
                "One-Click runs it automatically; run it here to inspect the full report.",
                QDUI.HelpText);
            GUILayout.Space(6f);

            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(m_source == null))
            {
                if (GUILayout.Button("ANALYZE", QDUI.AccentBtn, GUILayout.Width(140f)))
                {
                    RunAnalysis();
                    GUIUtility.ExitGUI();
                }
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(6f);
            QDUI.SubHeaderLabel("REPORT");
            foreach (var item in report.Items)
            {
                EditorGUILayout.BeginHorizontal();
                Color c = item.Severity == AnalysisSeverity.Blocked ? QDUI.Red
                        : item.Severity == AnalysisSeverity.Warning ? QDUI.Amber : QDUI.Green;
                QDUI.StatusPill(item.Severity.ToString().ToUpperInvariant(), c, 76f);
                GUILayout.Space(8f);
                EditorGUILayout.BeginVertical();
                GUILayout.Label($"<b>{item.Title}</b>  <color=#5C5C5C>{item.Code}</color>", QDUI.Body);
                GUILayout.Label($"Why: {item.Why}", QDUI.HelpText);
                GUILayout.Label($"Fix: {item.Fix}", QDUI.HelpText);
                EditorGUILayout.EndVertical();
                EditorGUILayout.EndHorizontal();
                GUILayout.Space(6f);
            }

            if (report.Mesh != null)
            {
                GUILayout.Space(4f);
                QDUI.SubHeaderLabel("GEOMETRY");
                EditorGUILayout.BeginHorizontal();
                QDUI.StatBlock($"{report.VertexCount:n0}", "VERTICES");
                QDUI.StatBlock($"{report.TriangleCount:n0}", "TRIANGLES");
                QDUI.StatBlock(report.IsClosedVolume ? "YES" : "NO", "CLOSED VOLUME",
                    report.IsClosedVolume ? QDUI.Green : QDUI.Amber);
                QDUI.StatBlock($"{report.OpenEdges:n0}", "OPEN EDGES");
                QDUI.StatBlock($"{report.DegenerateTriangles:n0}", "DEGENERATE");
                EditorGUILayout.EndHorizontal();
            }
            QDUI.EndFoldCard();
        }

        void DrawStepPresetCard()
        {
            FracturePattern shownPattern = m_profile != null
                ? m_profile.Pattern
                : m_quickPreset;
            QDUI.BeginWorkflowCard(2);
            QDUI.StepHeader("2", "CHOOSE A PRESET OR PROFILE",
                PresetLabel(shownPattern).ToUpperInvariant(), QDUI.Green);
            GUILayout.Label(
                "Pick what the object is made from. One click configures fracture geometry, " +
                "physics, lifecycle and a visible cut-surface material together — and flashes " +
                "the planned cut layout on the model for a few seconds.", QDUI.HelpText);
            GUILayout.Space(6f);
            QDUI.SubHeaderLabel("FRACTURE PATTERN");
            DrawStartFracturePatternSelector();

            // A reusable profile is the second half of the primary preset choice,
            // so expose it before any controls that read or modify its values.
            GUILayout.Space(7f);
            QDUI.SubHeaderLabel("PROFILE — QUICK OR REUSABLE");
            DrawProfileLibrary();

            QDUI.HelpFoldout("Start.Pattern", "How does this pattern work?",
                GetPatternDescription(shownPattern));

            // Shape controls follow the pattern/profile choice so every edit below
            // clearly targets it.
            DrawLandingQuickControls();
            DrawStartRefinementFolds();

            DrawResultEstimateCard();

            if (shownPattern == FracturePattern.BrickCells)
            {
                GUILayout.Space(5f);
                QDUI.SubHeaderLabel("BRICK SIZE");
                Vector3 brickSize = m_profile != null
                    ? m_profile.BrickCellSize
                    : m_quickBrickCellSize;
                EditorGUI.BeginChangeCheck();
                if (m_profile != null)
                    Undo.RecordObject(m_profile, "Edit Quick Brick Size");
                EditorGUILayout.BeginHorizontal();
                QDUI.DrawFieldLabel("Width X / Height Y / Depth Z",
                    GUILayout.Width(QDUI.LabelWidth));
                brickSize = EditorGUILayout.Vector3Field(GUIContent.none, brickSize);
                EditorGUILayout.EndHorizontal();
                brickSize = ClampBrickCellSize(brickSize);
                if (EditorGUI.EndChangeCheck())
                {
                    m_quickBrickCellSize = brickSize;
                    if (m_profile != null)
                    {
                        m_profile.BrickUseCellSize = true;
                        m_profile.BrickCellSize = brickSize;
                        EditorUtility.SetDirty(m_profile);
                        ScheduleFracturePreview();
                    }
                    else
                    {
                        PatternHintOverlay.Show(GetCurrentFractureSource(), null, m_quickPreset);
                    }
                }
                GUILayout.Label(
                    "Target cell dimensions in source-mesh local units. Quick Setup uses these values " +
                    "for One-Click Bake. Fracture > Brick / Cells also shows the estimated grid and " +
                    "offers Max Parts as a fallback.", QDUI.HelpText);
            }
            QDUI.EndCard();
        }

        /// <summary>
        /// The refinement drawer under Step 2. A preset already answers these four
        /// questions, so a quick setup never opens them — but the object that needs a
        /// hollow shell or a custom cut material needs them exactly once. Folding keeps
        /// the page short without hiding the answers: every badge reports the live value,
        /// so a collapsed card still says what the bake will do.
        ///
        /// Height was the reason these used to sit above the shape controls; a folded card
        /// is one row tall whatever the pattern, so the order can now follow the workflow.
        /// </summary>
        void DrawStartRefinementFolds()
        {
            GUILayout.Space(7f);

            bool shell = m_profile != null ? m_profile.Solidify : m_quickSolidify;
            ColliderStrategy colliders = m_profile != null
                ? m_profile.Colliders
                : m_quickColliderStrategy;
            bool needsShell = !shell && ModularSourceNeedsHollowShell;
            if (QDUI.BeginFoldCard("Volume & Colliders", false,
                    needsShell
                        ? "FULL SOLID  •  NEEDS SHELL"
                        : (shell ? "HOLLOW SHELL" : "FULL SOLID") + "  •  " +
                          ColliderStrategyName(colliders).ToUpperInvariant(),
                    needsShell ? QDUI.Amber : QDUI.Blue, RefinementBadgeWidth))
            {
                DrawStartSolidify();
                DrawStartColliderToggle();
            }
            QDUI.EndFoldCard();

            string interiorStatus;
            Color interiorColor;
            if (m_profile == null)
            {
                interiorStatus = "SOURCE MATERIAL";
                interiorColor = QDUI.TextDim;
            }
            else if (m_profile.InteriorUseSourceMaterial)
            {
                interiorStatus = "SOURCE MATERIAL";
                interiorColor = QDUI.Green;
            }
            else if (m_profile.InteriorMaterial != null)
            {
                interiorStatus = m_profile.InteriorMaterial.name.ToUpperInvariant();
                interiorColor = QDUI.Green;
            }
            else
            {
                // The bake needs a material here, so the warning has to survive folding.
                interiorStatus = "NOT ASSIGNED";
                interiorColor = QDUI.Amber;
            }
            if (QDUI.BeginFoldCard("Cut Faces & Interior Material", false,
                    interiorStatus, interiorColor, RefinementBadgeWidth))
                DrawStartInteriorSurface();
            QDUI.EndFoldCard();

            bool refracture = m_profile != null
                ? m_profile.RuntimeFragmentSplitting
                : m_quickRuntimeFragmentSplitting;
            bool despawn = m_profile != null
                ? m_profile.DespawnDebris
                : m_quickDespawnDebris;
            float despawnAfter = m_profile != null
                ? m_profile.DespawnDebrisAfter
                : m_quickDespawnDebrisAfter;
            if (QDUI.BeginFoldCard("Debris & Runtime Re-Fracture", false,
                    (refracture ? "RE-FRACTURE  •  " : string.Empty) +
                    (despawn ? $"DESPAWN {despawnAfter:0} s" : "RUBBLE STAYS"),
                    refracture || despawn ? QDUI.Green : QDUI.TextDim, RefinementBadgeWidth))
                DrawStartRuntimeFragmentSplitting();
            QDUI.EndFoldCard();

            bool effectsOn = m_profile != null
                ? m_profile.UseDefaultHitParticles || m_profile.UseDefaultHitSound ||
                  m_profile.HitParticlePrefab != null || m_profile.HitSound != null ||
                  CountAssigned(m_profile.HitSoundVariants) > 0
                : m_quickUseDefaultHitParticles || m_quickUseDefaultHitSound ||
                  m_quickHitParticlePrefab != null || m_quickHitSound != null;
            if (QDUI.BeginFoldCard("Effects — Particles & Sound", false,
                    effectsOn ? "PARTICLES & SOUND ON" : "OFF",
                    effectsOn ? QDUI.Green : QDUI.TextDim, RefinementBadgeWidth))
                DrawStartProfileEffects();
            QDUI.EndFoldCard();
        }

        /// <summary>Badge width for the Step 2 drawer: its pills carry two values.</summary>
        const float RefinementBadgeWidth = 168f;

        void DrawStartColliderToggle()
        {
            GUILayout.Space(7f);
            QDUI.SubHeaderLabel("COLLIDERS");

            ColliderStrategy strategy = m_profile != null
                ? m_profile.Colliders
                : m_quickColliderStrategy;
            ColliderStrategy nextStrategy = ColliderStrategyRow(
                "Collider Mode", strategy);
            if (nextStrategy != strategy)
            {
                m_quickColliderStrategy = nextStrategy;
                if (m_profile != null)
                {
                    Undo.RecordObject(m_profile, "Change Quick Collider Mode");
                    m_profile.Colliders = nextStrategy;
                    EditorUtility.SetDirty(m_profile);
                }

                ShowStatus(ColliderStrategyName(nextStrategy) +
                    " selected. Run Bake & Setup to rebuild the destructible.");
            }

            QDUI.HelpFoldout("Start.Colliders", "Which collider mode should I use?",
                GetColliderDescription(nextStrategy) + " Changing this option affects baked " +
                "fragment colliders and requires Bake & Setup again. Current bakes serialize a Collider for every " +
                "physical fragment. Large Whole Building bakes add inactive-piece Rigidbodies only when those pieces detach; " +
                "other bakes serialize the complete body pool. Legacy or partially migrated bakes are repaired at runtime " +
                "with a conservative bounds collider if their saved dynamic shape is missing or invalid.");
        }

        void DrawStartRuntimeFragmentSplitting()
        {
            GUILayout.Space(7f);
            QDUI.SubHeaderLabel("RUNTIME RE-FRACTURE");

            bool enabled = m_profile != null
                ? m_profile.RuntimeFragmentSplitting
                : m_quickRuntimeFragmentSplitting;
            bool glass = (m_profile != null ? m_profile.Pattern : m_quickPreset) ==
                         FracturePattern.RadialGlass;
            if (glass)
            {
                enabled = false;
                m_quickRuntimeFragmentSplitting = false;
                if (m_profile != null && m_profile.RuntimeFragmentSplitting)
                {
                    m_profile.RuntimeFragmentSplitting = false;
                    EditorUtility.SetDirty(m_profile);
                }
            }
            EditorGUI.BeginChangeCheck();
            bool next;
            using (new EditorGUI.DisabledScope(glass))
                next = QDUI.ToggleRow("Split Hit Fragments At Runtime", enabled);
            if (EditorGUI.EndChangeCheck())
            {
                m_quickRuntimeFragmentSplitting = next;
                if (m_profile != null)
                {
                    Undo.RecordObject(m_profile, "Toggle Runtime Re-Fracture");
                    m_profile.RuntimeFragmentSplitting = next;
                    if (next)
                        m_profile.SecondaryBreakup = SecondaryBreakupLevel.Off;
                    EditorUtility.SetDirty(m_profile);
                }
            }

            if (glass)
                QDUI.InfoBox(
                    "GLASS: runtime re-fracture and interior scatter are disabled. " +
                    "Radial Glass uses only its baked spokes and rings.");

            if (next)
            {
                int finalIteration = m_profile != null
                    ? m_profile.RuntimeSplitMaxDepth
                    : m_quickRuntimeFinalIteration;
                EditorGUI.BeginChangeCheck();
                if (m_profile != null)
                    Undo.RecordObject(m_profile, "Set Final Destruction Iteration");
                finalIteration = QDUI.IntSliderRow(
                    "Final Destruction Iteration", finalIteration, 1, 4);
                if (EditorGUI.EndChangeCheck())
                {
                    m_quickRuntimeFinalIteration = finalIteration;
                    if (m_profile != null)
                    {
                        m_profile.RuntimeSplitMaxDepth = finalIteration;
                        EditorUtility.SetDirty(m_profile);
                    }
                }
                GUILayout.Label(
                    "Iteration 1 is the first runtime split. Pieces created by the selected " +
                    "final iteration are terminal and no longer divide on later hits.",
                    QDUI.HelpText);
            }

            QDUI.HelpFoldout("Start.RuntimeRefracture",
                "What does runtime re-fracture do?",
                "Enabled by default and completely idle while disabled. An explicit " +
                "ApplyDamage hit or an external simulated Rigidbody impact splits the struck " +
                "loose fragment into real pieces clipped from its current geometry. The normal baked " +
                "release always happens first; generated children require a later hit before they can " +
                "divide again. This replaces pre-baked Secondary Breakup and is useful for close-up " +
                "hero objects. If a true cut cannot be produced, the fragment stays unchanged.");

            if (next)
                QDUI.InfoBox(
                    "LMB / ApplyDamage and external Rigidbody impacts can split the exact " +
                    "loose fragment. Baked fragments and generated runtime children may also split " +
                    "on their first valid ground landing. " +
                    "Automatic landing splits are queued one per frame and limited to eight per " +
                    "destructible collapse cycle.");

            GUILayout.Space(7f);
            QDUI.SubHeaderLabel("DEBRIS AUTO-DESPAWN");
            DrawSceneCleanupOverrideNotice();
            bool despawn = m_profile != null
                ? m_profile.DespawnDebris
                : m_quickDespawnDebris;
            float despawnAfter = m_profile != null
                ? m_profile.DespawnDebrisAfter
                : m_quickDespawnDebrisAfter;
            EditorGUI.BeginChangeCheck();
            despawn = QDUI.ToggleRow("Despawn Debris", despawn);
            if (despawn)
                despawnAfter = QDUI.SliderRow(
                    "Despawn After (s)", despawnAfter, 5f, 300f, "0");
            if (EditorGUI.EndChangeCheck())
            {
                m_quickDespawnDebris = despawn;
                m_quickDespawnDebrisAfter = despawnAfter;
                if (m_profile != null)
                {
                    Undo.RecordObject(m_profile, "Edit Debris Auto-Despawn");
                    m_profile.DespawnDebris = despawn;
                    m_profile.DespawnDebrisAfter = Mathf.Max(1f, despawnAfter);
                    EditorUtility.SetDirty(m_profile);
                }
            }
            GUILayout.Label(
                "Every broken-off piece fades out this long after it came loose — including " +
                "level-two children and runtime split pieces — releasing its physics and " +
                "rendering cost. Used when Scene Debris Cleanup is disabled. Other profile cleanup rules may still remove debris.",
                QDUI.HelpText);
        }

        /// <summary>
        /// A variant pool is a plain list of asset references, so it uses Unity's own list
        /// drawer instead of hand-built rows: reordering, multi-asset drops and the add and
        /// remove buttons all come for free, and none of them mutate the IMGUI layout in the
        /// middle of a frame the way a custom row with a delete button would.
        /// </summary>
        /// <summary>Assigned (non-empty) entries of a variant pool.</summary>
        static int CountAssigned<T>(List<T> list) where T : UnityEngine.Object
        {
            if (list == null) return 0;
            int count = 0;
            for (int i = 0; i < list.Count; i++)
                if (list[i] != null) count++;
            return count;
        }

        void DrawProfileVariantList(string propertyName, string label)
        {
            if (m_profile == null) return;
            var profileObject = new SerializedObject(m_profile);
            profileObject.Update();
            SerializedProperty list = profileObject.FindProperty(propertyName);
            if (list != null)
            {
                EditorGUILayout.PropertyField(list, QDUI.FieldContent(label), true);
                QDUI.CaptureField(label);
            }
            profileObject.ApplyModifiedProperties();
            GUILayout.Space(3f);
        }

        void DrawStartProfileEffects()
        {
            bool everyHit = m_profile != null
                ? m_profile.PlayHitEffectsOnEveryHit
                : m_quickPlayHitEffectsOnEveryHit;
            bool particles = m_profile != null
                ? m_profile.UseDefaultHitParticles
                : m_quickUseDefaultHitParticles;
            bool sound = m_profile != null
                ? m_profile.UseDefaultHitSound
                : m_quickUseDefaultHitSound;
            if (m_profile != null)
            {
                if (m_profile.HitSound == null)
                {
                    m_profile.HitSound = Resources.Load<AudioClip>($"Sounds/QD_Sound_{m_profile.Pattern}");
                }
                if (m_profile.GroundDustSound == null &&
                    m_profile.Pattern != FracturePattern.DirectionalWood &&
                    m_profile.Pattern != FracturePattern.RadialGlass)
                {
                    m_profile.GroundDustSound =
                        Resources.Load<AudioClip>("Sounds/QD_stone falls");
                }
            }

            ParticleSystem particleOverride = m_profile != null
                ? m_profile.HitParticlePrefab
                : null;
            ParticleSystem groundDustOverride = m_profile != null
                ? m_profile.GroundDustParticlePrefab
                : null;
            AudioClip groundDustSoundOverride = m_profile != null
                ? m_profile.GroundDustSound
                : null;
            AudioClip soundOverride = m_profile != null
                ? m_profile.HitSound
                : null;
            string presetName = PresetLabel(m_profile != null
                ? m_profile.Pattern : m_quickPreset);

            bool groundDust = m_profile != null
                ? m_profile.FragmentImpactEffects
                : true;

            EditorGUI.BeginChangeCheck();
            if (m_profile != null)
                Undo.RecordObject(m_profile, "Edit Default Hit Effects");
            everyHit = QDUI.ToggleRow("Play On Every Hit", everyHit);
            groundDust = QDUI.ToggleRow("One-Time Ground Landing Dust", groundDust);
            particles = QDUI.ToggleRow("Preset Particles", particles);
            EditorGUILayout.BeginHorizontal();
            QDUI.DrawFieldLabel("Particle Source",
                GUILayout.Width(QDUI.LabelWidth));
            GUILayout.Label(particleOverride != null
                ? particleOverride.name + "  (profile override)"
                : particles ? presetName + " built-in particles  (connected)" : "Off",
                QDUI.HelpText);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            QDUI.DrawFieldLabel("Particle Override",
                GUILayout.Width(QDUI.LabelWidth));
            particleOverride = (ParticleSystem)EditorGUILayout.ObjectField(
                particleOverride, typeof(ParticleSystem), false);
            // An empty field reads as "nothing will play", which is the opposite of the
            // truth while the preset is on. The preset burst is generated at runtime and
            // has no asset to show here — the generic pipeline template is a renderer
            // stand-in, and honouring it as an override is what renders a burst magenta in
            // another pipeline — so say in words what the empty field means.
            if (particleOverride == null)
                GUILayout.Label(particles
                    ? "empty — uses the preset burst above"
                    : "empty — no hit particles", QDUI.HelpText);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            QDUI.DrawFieldLabel("Ground Landing Dust",
                GUILayout.Width(QDUI.LabelWidth));
            groundDustOverride = (ParticleSystem)EditorGUILayout.ObjectField(
                groundDustOverride, typeof(ParticleSystem), false);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            QDUI.DrawFieldLabel("Ground Landing Sound",
                GUILayout.Width(QDUI.LabelWidth));
            groundDustSoundOverride = (AudioClip)EditorGUILayout.ObjectField(
                groundDustSoundOverride, typeof(AudioClip), false);
            EditorGUILayout.EndHorizontal();
            DrawProfileVariantList("GroundDustSoundVariants", "Landing Sound Variants");
            if (m_profile != null && groundDust)
            {
                m_profile.FragmentImpactMinDropHeight = QDUI.SliderRow(
                    "Minimum Drop Height (m)", m_profile.FragmentImpactMinDropHeight,
                    0f, 10f, "0.00");
                m_profile.FragmentImpactMinSpeed = QDUI.SliderRow(
                    "Minimum Landing Speed", m_profile.FragmentImpactMinSpeed,
                    0f, 20f, "0.0");
                m_profile.FragmentImpactStrength = QDUI.SliderRow(
                    "Landing Dust Strength", m_profile.FragmentImpactStrength,
                    0f, 1f);
                GUILayout.Label(
                    "Plays once per physical fragment only after it falls onto external static ground. " +
                    "It keeps the assigned particle prefab's own colour and plays the separate " +
                    "landing clip.", QDUI.HelpText);
            }

            sound = QDUI.ToggleRow("Preset Sound", sound);
            EditorGUILayout.BeginHorizontal();
            QDUI.DrawFieldLabel("Sound Source",
                GUILayout.Width(QDUI.LabelWidth));
            GUILayout.Label(soundOverride != null
                ? soundOverride.name + "  (profile override)"
                : sound ? presetName + " built-in sound  (connected)" : "Off",
                QDUI.HelpText);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            QDUI.DrawFieldLabel("Sound Override",
                GUILayout.Width(QDUI.LabelWidth));
            soundOverride = (AudioClip)EditorGUILayout.ObjectField(
                soundOverride, typeof(AudioClip), false);
            if (soundOverride == null)
                GUILayout.Label(sound
                    ? "empty — uses the preset sound above"
                    : "empty — no hit sound", QDUI.HelpText);
            EditorGUILayout.EndHorizontal();
            DrawProfileVariantList("HitSoundVariants", "Sound Variants");
            if (EditorGUI.EndChangeCheck())
            {
                m_quickPlayHitEffectsOnEveryHit = everyHit;
                m_quickUseDefaultHitParticles = particles;
                m_quickUseDefaultHitSound = sound;
                if (m_profile != null)
                {
                    m_profile.PlayHitEffectsOnEveryHit = everyHit;
                    m_profile.FragmentImpactEffects = groundDust;
                    m_profile.UseDefaultHitParticles = particles;
                    m_profile.UseDefaultHitSound = sound;
                    m_profile.HitParticlePrefab = particleOverride;
                    m_profile.GroundDustParticlePrefab = groundDustOverride;
                    m_profile.GroundDustSound = groundDustSoundOverride;
                    m_profile.HitSound = soundOverride;
                    EditorUtility.SetDirty(m_profile);
                }
            }

            // Only sounds carry variants. The burst is procedural and already takes its
            // colour from the surface it came off, so a second particle prefab would have
            // varied nothing that the preset does not vary by itself.
            int soundPool = (soundOverride != null ? 1 : 0) +
                (m_profile != null ? CountAssigned(m_profile.HitSoundVariants) : 0);
            string randomNote = soundPool > 1
                ? $" Each break draws one of {soundPool} sounds at random, never the same " +
                  "one twice in a row."
                : string.Empty;
            QDUI.InfoBox(
                $"CONNECTED TO PROFILE: {(particleOverride != null ? "custom particle" : particles ? presetName + " procedural particles" : "particles off")} + " +
                $"{(soundPool > 1 ? "custom sound set" : soundOverride != null ? "custom sound" : sound ? presetName + " synthesized sound" : "sound off")}." +
                randomNote +
                " Ground landing dust is a separate, one-time effect and never uses hit-particle tinting.");
        }

        void DrawStartSolidify()
        {
            GUILayout.Space(7f);
            EditorGUILayout.BeginHorizontal();
            QDUI.SubHeaderLabel("VOLUME / SHELL");
            GUILayout.FlexibleSpace();
            QDUI.StatusPill("BAKE / OBJECT", QDUI.Blue, 112f);
            EditorGUILayout.EndHorizontal();

            bool enabled = m_profile != null ? m_profile.Solidify : m_quickSolidify;
            bool previousEnabled = enabled;
            float thickness = m_profile != null
                ? m_profile.SolidifyThickness
                : m_quickSolidifyThickness;
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("FULL SOLID", !enabled ? QDUI.ChipBtnActive : QDUI.ChipBtn,
                    GUILayout.MinWidth(120f)))
                enabled = false;
            if (GUILayout.Button("HOLLOW SHELL", enabled ? QDUI.ChipBtnActive : QDUI.ChipBtn,
                    GUILayout.MinWidth(120f)))
                enabled = true;
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            if (enabled != previousEnabled)
            {
                EditorGUI.EndChangeCheck();
                m_quickSolidify = enabled;
                if (m_profile != null)
                {
                    DestructionProfile local = EnsureVolumeModeIsObjectLocal(m_profile);
                    Undo.RecordObject(local, "Change Object Volume Mode");
                    local.Solidify = enabled;
                    EditorUtility.SetDirty(local);
                    ScheduleFracturePreview();
                }
                ShowStatus(enabled
                    ? "Hollow Shell enabled. Set Wall Thickness, then run Bake & Setup."
                    : "Full Solid enabled. Wall Thickness is no longer used; run Bake & Setup.");
                GUIUtility.ExitGUI();
            }

            if (enabled)
                thickness = QDUI.SliderRow(
                    "Wall Thickness (m)", thickness, 0.005f, 2f, "0.000");
            if (EditorGUI.EndChangeCheck())
            {
                thickness = Mathf.Max(0.001f, thickness);
                m_quickSolidify = enabled;
                m_quickSolidifyThickness = thickness;
                if (m_profile != null)
                {
                    DestructionProfile local = EnsureVolumeModeIsObjectLocal(m_profile);
                    Undo.RecordObject(local, "Change Object Shell Thickness");
                    local.Solidify = enabled;
                    local.SolidifyThickness = thickness;
                    EditorUtility.SetDirty(local);
                    ScheduleFracturePreview();
                }
            }
            QDUI.HelpFoldout("Start.VolumeMode",
                "What should use Full Solid or Hollow Shell?",
                "FULL SOLID creates filled chunks. Use it for rocks, concrete blocks, pillars and " +
                "other genuinely solid objects. Also keep Full Solid for a barrel, pipe or building " +
                "that already has its own modeled inner wall - Hollow Shell would add a second one.\n\n" +
                "HOLLOW SHELL generates an inner wall using Wall Thickness. Use it for a barrel, " +
                "tank, pipe, roof or metal panel modeled only from the outside. It is also useful " +
                "for RTS buildings that are only an exterior hull with no interior: walls break as " +
                "a shell instead of becoming completely filled blocks. Wall Thickness is measured " +
                "in scene units; FBX/Transform scale is handled automatically.\n\n" +
                "Rule of thumb: only an outer surface = Hollow Shell; an authored interior or a " +
                "truly solid volume = Full Solid. This is stored for the selected object. If its " +
                "profile is shared, Quick Destruction creates a local copy automatically. Changing " +
                "the mode requires Bake & Setup.");
        }

        void DrawStartFracturePatternSelector()
        {
            EditorGUILayout.BeginHorizontal();
            DrawQuickPresetChip(FracturePattern.UniformConcrete, "CONCRETE");
            DrawQuickPresetChip(FracturePattern.RadialGlass, "GLASS");
            DrawQuickPresetChip(FracturePattern.DirectionalWood, "WOOD");
            DrawQuickPresetChip(FracturePattern.BrickCells, "BRICK");
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            DrawQuickPresetChip(FracturePattern.LayeredSlabs, "SLABS");
            DrawQuickPresetChip(FracturePattern.ColumnarPillars, "PILLARS");
            DrawQuickPresetChip(FracturePattern.ImpactShatter, "IMPACT");
            int column = 3;
            DrawNamedProfileChips(ref column);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// Closes the pattern grid with the "save this as a profile" tile, and — when a
        /// named profile is in use — one tile showing it.
        ///
        /// The grid deliberately does NOT list every DestructionProfile in the project. The
        /// seven presets are the catalogue of what an object can be made from; enumerating
        /// every asset beside them buried that choice under sample and legacy profiles that
        /// have nothing to do with the object being set up. Switching to an older profile
        /// stays available through the Profile Asset field directly below.
        /// </summary>
        void DrawNamedProfileChips(ref int column)
        {
            // The profile currently in use earns a tile so the grid still answers "what is
            // this object set to?" at a glance, without becoming a project-wide listing.
            // It is drawn BEFORE the "+" so a freshly created profile takes the slot the
            // author just clicked and the "+" steps aside — the new tile appears exactly
            // where the eye already is, instead of pushing into view somewhere else.
            if (m_profile != null && IsNamedProfileAsset(m_profile))
            {
                column = NextGridCell(column);
                QDUI.NamedProfileChip(m_profile.name, true);
            }

            column = NextGridCell(column);
            if (!QDUI.CreateProfileChip()) return;

            DestructionProfile previousProfile = m_profile;
            CreateProfileAsset();
            if (m_profile != null && m_profile != previousProfile)
            {
                m_quickPreset = m_profile.Pattern;
                RefreshProfileLibrary(true);
                ShowStatus(
                    $"Profile '{m_profile.name}' created from the selected preset. " +
                    "Bake & Setup now uses it.");
                GUIUtility.ExitGUI();
            }
        }

        /// <summary>
        /// A profile the author made and named, as opposed to one the plugin generated for
        /// a single object. Membership is decided by folder: named profiles live in their
        /// own directory, the automatic ones in the protected QuickProfiles directory.
        /// </summary>
        static bool IsNamedProfileAsset(DestructionProfile profile)
        {
            if (profile == null) return false;
            string path = AssetDatabase.GetAssetPath(profile).Replace('\\', '/');
            if (string.IsNullOrEmpty(path)) return false;
            return !IsAutoQuickProfile(profile);
        }

        /// <summary>
        /// Selects a profile and hands it to the object being set up. The handover
        /// matters: selection sync re-reads the profile from the DestructibleRoot, so
        /// without it the choice would be reverted a few frames later.
        /// </summary>
        void ApplyProfileToSource(DestructionProfile profile)
        {
            if (profile == null) return;
            EndFracturePreviews();
            m_profile = profile;
            m_quickPreset = profile.Pattern;
            SyncQuickFieldsFromProfile();

            DestructibleRoot target = m_contextRoot;
            if (target == null && m_source != null)
                target = m_source.GetComponentInParent<DestructibleRoot>(true);
            if (target != null && target.Profile != profile)
            {
                Undo.RecordObject(target, "Assign Destruction Profile");
                target.Profile = profile;
                if (PrefabUtility.IsPartOfPrefabInstance(target))
                    PrefabUtility.RecordPrefabInstancePropertyModifications(target);
                EditorUtility.SetDirty(target);
                m_contextRoot = target;
            }
            m_layoutProfileUsers = null;
            m_layoutUsersProfile = null;

            PatternHintOverlay.Show(GetCurrentFractureSource(), profile, profile.Pattern);
            ShowStatus(target != null
                ? $"Profile '{profile.name}' assigned to {target.name}."
                : $"Profile '{profile.name}' selected. Bake & Setup uses its settings.");
        }

        /// <summary>Advances the pattern grid by one cell, wrapping to a fresh row after
        /// four so the tiles keep the shape the preset rows established.</summary>
        static int NextGridCell(int column)
        {
            const int columnsPerRow = 4;
            if (column < columnsPerRow) return column + 1;
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            return 1;
        }

        void DrawStartInteriorSurface()
        {
            if (m_profile == null)
            {
                QDUI.InfoBox(
                    "One-Click creates new profiles with Use Source Material enabled: each fragment's " +
                    "cut faces match its dominant source submesh (for example roof or wall). Select or " +
                    "create a profile here to assign custom core art.");
                return;
            }

            DestructionProfile p = m_profile;
            EditorGUI.BeginChangeCheck();
            Undo.RecordObject(p, "Edit Start Interior Surface");
            p.InteriorUseSourceMaterial = QDUI.ToggleRow(
                "Use Source Material", p.InteriorUseSourceMaterial);
            QDUI.HelpFoldout("Start.InteriorMaterial",
                "How are cut-face materials selected?",
                "Recommended default: each fragment reuses its dominant exterior material, so roof " +
                "pieces match the roof and wall pieces match the wall. Choose below how UVs are " +
                "generated on newly exposed surfaces. Turn it off " +
                "only when the inside should look different, such as raw concrete or splintered wood. " +
                "The preview below shows the dominant material of the exact mesh selected for fracture; " +
                "individual multi-material fragments can still match their own dominant submesh.");
            DrawCutSurfaceUVModeRow(p);
            if (!p.InteriorUseSourceMaterial)
            {
                p.InteriorMaterial = (Material)QDUI.ObjectRow(
                    "Interior Material", p.InteriorMaterial, typeof(Material));
            }
            bool changed = EditorGUI.EndChangeCheck();
            if (changed)
            {
                EditorUtility.SetDirty(p);
                MarkBakeAppearanceDirty();
            }

            if (p.InteriorUseSourceMaterial)
            {
                DrawStartMaterialPreview(GetStartSourceMaterial(), "SOURCE MATERIAL PREVIEW");
            }
            else if (p.InteriorMaterial == null)
            {
                QDUI.WarnBox("Assign an Interior Material before Bake so new cut faces have visible core art.");
                DrawStartMaterialPreview(null, "CUSTOM INTERIOR PREVIEW");
            }
            else
            {
                DrawStartMaterialPreview(p.InteriorMaterial, "CUSTOM INTERIOR PREVIEW");
                GUILayout.Label("Projected Tiling scale is set beside Cut Face UV Layout.", QDUI.HelpText);
            }
        }

        Material GetStartSourceMaterial()
        {
            GameObject source = m_source;
            if (source == null)
            {
                var candidates = GetSourceCandidates();
                if (candidates.Count > 0) source = candidates[0];
            }
            if (source == null) return null;

            return GetStartSourceMaterial(source);
        }

        /// <summary>
        /// Resolves the material from the same MeshFilter/MeshRenderer pair that Preview
        /// and Bake fracture. Looking up the first arbitrary Renderer could select a decal,
        /// cap or sibling child and make the UI claim that a red barrel used Matte black.
        /// </summary>
        internal static Material GetStartSourceMaterial(GameObject source)
        {
            if (source == null) return null;

            MeshFilter filter = source.GetComponentInChildren<MeshFilter>(true);
            Renderer renderer = filter != null
                ? filter.GetComponent<Renderer>()
                : null;
            if (renderer == null)
                renderer = source.GetComponent<Renderer>();
            if (renderer == null)
                renderer = source.GetComponentInChildren<Renderer>(true);
            if (renderer == null) return null;

            Material[] materials = renderer.sharedMaterials;
            if (filter != null && filter.sharedMesh != null &&
                materials != null && materials.Length > 0)
            {
                Material dominant = FragmentMaterialUtility.GetDominantSourceMaterial(
                    filter.sharedMesh, filter.sharedMesh.subMeshCount, materials);
                if (dominant != null) return dominant;
            }

            return renderer.sharedMaterial;
        }

        static void DrawStartMaterialPreview(Material material, string title)
        {
            GUILayout.Space(3f);
            Rect rect = GUILayoutUtility.GetRect(1f, 46f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, QDUI.PanelLight);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 3f, rect.height), QDUI.Blue);
            Rect thumbnail = new Rect(rect.x + 8f, rect.y + 6f, 34f, 34f);
            // Asset previews are rendered asynchronously by the editor and cached per
            // artifact. Asking for one for an in-memory material — the quick profile makes
            // exactly that — leaves the cache holding an entry whose artifact never
            // resolves, which surfaces as the previewArtifactID assertion during GUI
            // events. Only persisted assets are ever asked for a rendered preview; the
            // rest use the type thumbnail, which needs no artifact at all.
            bool persisted = material != null &&
                             AssetDatabase.Contains(material) &&
                             !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(material));
            // AssetPreview keys on the asset's real id, so this cannot use GetHashCode.
            // Unity 6000.3 moved that id to EntityId. Both branches keep the original
            // short-circuit: a non-persisted material is never queried at all.
#if UNITY_6000_3_OR_NEWER
            bool previewLoading = persisted &&
                                  AssetPreview.IsLoadingAssetPreview(material.GetEntityId());
#else
            bool previewLoading = persisted &&
                                  AssetPreview.IsLoadingAssetPreview(material.GetInstanceID());
#endif
            Texture preview = persisted && !previewLoading
                ? AssetPreview.GetAssetPreview(material)
                : null;
            if (preview == null && material != null)
                preview = AssetPreview.GetMiniThumbnail(material);
            if (preview != null)
                GUI.DrawTexture(thumbnail, preview, ScaleMode.ScaleToFit);
            else
                EditorGUI.DrawRect(thumbnail, material != null ? material.color : QDUI.TextFaint);

            GUI.Label(new Rect(rect.x + 50f, rect.y + 5f, rect.width - 56f, 14f),
                title, QDUI.StatLabel);
            GUI.Label(new Rect(rect.x + 50f, rect.y + 19f, rect.width - 56f, 20f),
                material != null ? material.name : "No material detected", QDUI.Body);
        }

        void DrawLandingQuickControls()
        {
            GUILayout.Space(7f);
            // Orange-tinted inner card marks these as the live shape controls for the
            // currently selected pattern, distinct from material/profile asset setup.
            QDUI.BeginOutlinedCard(QDUI.Accent);
            EditorGUILayout.BeginHorizontal();
            QDUI.SubHeaderLabel("QUICK SHAPE CONTROLS");
            GUILayout.FlexibleSpace();
            QDUI.StatusPill("BAKE / PROFILE", QDUI.Accent, 112f);
            EditorGUILayout.EndHorizontal();
            if (m_profile == null)
            {
                QDUI.InfoBox(
                    "Select a reusable profile above or click Create Named Profile to unlock Max Parts, " +
                    "Break Push and pattern-specific controls here. Auto remains available when " +
                    "you only want to choose a material preset and bake once.");
                QDUI.EndOutlinedCard();
                return;
            }

            DestructionProfile p = m_profile;
            QDUI.HelpFoldout("Start.ProfileScope", "What is being edited?",
                $"Editing profile: <b>{p.name}</b>. These are the same important controls shown " +
                "on Destructible Root; geometry changes need Bake & Setup, while Break Push is runtime-only.");

            EditorGUI.BeginChangeCheck();
            Undo.RecordObject(p, "Edit Landing Quick Controls");
            string shapeHelp = null;
            switch (p.Pattern)
            {
                case FracturePattern.UniformConcrete:
                    p.FragmentCount = QDUI.IntSliderRow("Max Parts", p.FragmentCount, 2,
                        PrimaryFragmentAuthoringLimit(p));
                    p.FragmentShapeIrregularity = QDUI.SliderRow(
                        "Fragment Shape Irregularity",
                        p.FragmentShapeIrregularity, 0f, 1f);
                    p.ConcreteIrregularity = QDUI.SliderRow(
                        "Interior Stone Scatter",
                        p.ConcreteIrregularity, 0f, 1f);
                    shapeHelp = "Fragment Shape Irregularity changes the real cut positions and " +
                        "fragment silhouettes: 0 is more even and block-like, 1 is more varied " +
                        "and angular. Interior Stone Scatter changes only short render-only chips " +
                        "on newly exposed faces. Other patterns expose their equivalent as Angular, " +
                        "Cell, Layer or Column Irregularity, Splinter End Sharpness, or Impact Focus.";
                    break;
                case FracturePattern.RadialGlass:
                    p.RadialSpokes = QDUI.IntSliderRow("Glass Spokes", p.RadialSpokes, 3, 24);
                    p.RadialRings = QDUI.IntSliderRow("Glass Rings", p.RadialRings, 1, 8);
                    p.RadialJitter = QDUI.SliderRow(
                        "Irregularity / Jitter", p.RadialJitter, 0f, 1f);
                    p.GlassNormalAxis = AxisModeRow("Pane Normal Axis", p.GlassNormalAxis);
                    shapeHelp = "Auto uses the thinnest local axis of each disconnected pane. " +
                                "Choose X, Y or Z to force one shared normal for the complete object.";
                    break;
                case FracturePattern.DirectionalWood:
                    p.FragmentCount = QDUI.IntSliderRow("Max Parts", p.FragmentCount, 2,
                        PrimaryFragmentAuthoringLimit(p));
                    DrawDirectionalBiasRows(p, "Grain / Length Axis");
                    p.WoodEndSharpness = QDUI.SliderRow(
                        "Splinter End Sharpness", p.WoodEndSharpness, 0f, 1f);
                    p.WoodSplitIrregularity = QDUI.SliderRow(
                        "Interior Splinter Scatter", p.WoodSplitIrregularity, 0f, 1f);
                    shapeHelp = DirectionalBiasHelp(
                        "Wood defaults to full directional influence. End Sharpness creates " +
                        "wedge-like tips. Interior Splinter Scatter adds a few longer render-only " +
                        "splinters on new cut faces without changing the seed or cut layout.");
                    break;
                case FracturePattern.BrickCells when p.BrickUseCellSize:
                    EditorGUILayout.BeginHorizontal();
                    QDUI.DrawFieldLabel("Brick Cell Size",
                        GUILayout.Width(QDUI.LabelWidth));
                    p.BrickCellSize = EditorGUILayout.Vector3Field(
                        GUIContent.none, p.BrickCellSize);
                    EditorGUILayout.EndHorizontal();
                    p.BrickCellSize = ClampBrickCellSize(p.BrickCellSize);
                    p.BrickJitter = QDUI.SliderRow(
                        "Irregularity / Jitter", p.BrickJitter, 0f, 1f);
                    shapeHelp = "Cell Size controls the target brick width, height and depth in " +
                                "source-mesh local units.";
                    break;
                case FracturePattern.ColumnarPillars:
                    p.FragmentCount = QDUI.IntSliderRow("Max Parts", p.FragmentCount, 2,
                        PrimaryFragmentAuthoringLimit(p));
                    p.ColumnJitter = QDUI.SliderRow(
                        "Irregularity / Jitter", p.ColumnJitter, 0f, 1f);
                    p.PillarAxis = AxisModeRow("Pillar Length Axis", p.PillarAxis);
                    shapeHelp = "Auto follows every disconnected pillar, post or beam along its own " +
                                "longest local dimension. Choose X, Y or Z to force one shared axis.";
                    break;
                case FracturePattern.LayeredSlabs:
                    p.SlabNormalAxis = AxisModeRow("Layer Normal Axis", p.SlabNormalAxis);
                    p.SlabLayers = QDUI.IntSliderRow("Slab Layers", p.SlabLayers, 2, 16);
                    p.FragmentCount = QDUI.IntSliderRow("Max Parts", p.FragmentCount, 2,
                        PrimaryFragmentAuthoringLimit(p));
                    p.SlabJitter = QDUI.SliderRow(
                        "Irregularity / Jitter", p.SlabJitter, 0f, 1f);
                    DrawDirectionalBiasRows(p, "Plate Length Axis");
                    shapeHelp = DirectionalBiasHelp(
                        "For Slabs this affects only the cross-cuts inside each layer; Layer Normal " +
                        "Axis still controls how the stacked sheets are formed.");
                    break;
                case FracturePattern.ImpactShatter:
                    p.FragmentCount = QDUI.IntSliderRow("Max Parts", p.FragmentCount, 2,
                        PrimaryFragmentAuthoringLimit(p));
                    p.ImpactFocus = QDUI.SliderRow("Impact Focus", p.ImpactFocus, 0f, 1f);
                    p.ConcreteIrregularity = QDUI.SliderRow(
                        "Interior Stone Scatter", p.ConcreteIrregularity, 0f, 1f);
                    p.ImpactPointOverride = QDUI.ToggleRow(
                        "Place Impact Point", p.ImpactPointOverride);
                    if (p.ImpactPointOverride)
                    {
                        EditorGUILayout.BeginHorizontal();
                        QDUI.DrawFieldLabel("Impact Position",
                            GUILayout.Width(QDUI.LabelWidth));
                        Vector3 hit = EditorGUILayout.Vector3Field(
                            GUIContent.none, p.ImpactPointLocal);
                        // Fractions of the bounds: anything outside 0..1 is off the model
                        // and silently clamps, which reads as a field that ignores input.
                        p.ImpactPointLocal = new Vector3(
                            Mathf.Clamp01(hit.x),
                            Mathf.Clamp01(hit.y),
                            Mathf.Clamp01(hit.z));
                        EditorGUILayout.EndHorizontal();
                        GUILayout.Label(
                            "0 to 1 on each axis: the hit as a fraction of the source " +
                            "bounds. 0.5 is the centre.", QDUI.HelpText);
                    }
                    shapeHelp = "Impact Focus controls how strongly pieces cluster around the hit. " +
                                "Place Impact Point puts a drag handle on the model in the Scene view; " +
                                "with it off, Seed chooses the hit. Interior Stone Scatter adds " +
                                "render-only chips on new faces.";
                    break;
                default:
                    p.FragmentCount = QDUI.IntSliderRow("Max Parts", p.FragmentCount, 2,
                        PrimaryFragmentAuthoringLimit(p));
                    break;
            }
            // Every pattern whose cut faces receive render-only detail gets the same two
            // controls, so scatter is never a setting that exists only for some presets.
            if (PatternUsesScatter(p.Pattern))
            {
                EditorGUILayout.BeginHorizontal();
                QDUI.DrawFieldLabel("Scatter Style",
                    GUILayout.Width(QDUI.LabelWidth));
                p.InteriorScatterStyle = (InteriorScatterStyle)EditorGUILayout.EnumPopup(
                    GUIContent.none, p.InteriorScatterStyle);
                EditorGUILayout.EndHorizontal();
                if (p.InteriorScatterStyle == InteriorScatterStyle.Splinters &&
                    p.Pattern != FracturePattern.DirectionalWood)
                    p.WoodSplitIrregularity = QDUI.SliderRow(
                        "Interior Splinter Scatter", p.WoodSplitIrregularity, 0f, 1f);
                p.InteriorScatterLength = QDUI.SliderRow(
                    "Scatter Length", p.InteriorScatterLength, 0.2f, 3f);
            }
            if (!string.IsNullOrEmpty(shapeHelp))
                QDUI.HelpFoldout("Start.Shape." + p.Pattern,
                    "What do these shape controls change?", shapeHelp);

            PatternHintOverlay.Track(m_source, p);
            bool geometryChanged = EditorGUI.EndChangeCheck();
            if (geometryChanged)
            {
                EditorUtility.SetDirty(p);
                // PatternHintOverlay updates immediately, while the expensive solver is
                // debounced and waits until the user has released the active slider.
                ScheduleFracturePreview();
            }

            EditorGUI.BeginChangeCheck();
            float nextExplode = QDUI.SliderRow("Exploded View", m_previewExplode, 0f, 0.5f);
            if (EditorGUI.EndChangeCheck())
            {
                m_previewExplode = nextExplode;
                if (IsFracturePreviewActive())
                    SetFracturePreviewExplode(m_previewExplode);
            }

            EditorGUI.BeginChangeCheck();
            if (QDUI.BeginFoldCard("Launch & Advanced Shape Controls", false,
                    "OPTIONAL", QDUI.TextDim))
            {
                p.FragmentPushMultiplier = QDUI.SliderRow(
                    "Break Push (Launch Force)", p.FragmentPushMultiplier, 0f, 5f, "0.00x");
                p.DirectionalImpactMultiplier = QDUI.SliderRow(
                    "Directional Impact Force", p.DirectionalImpactMultiplier,
                    0f, 5f, "0.00x");
                p.LocalImpactEjectSpeed = QDUI.SliderRow(
                    "Local Eject Impulse", p.LocalImpactEjectSpeed, 0f, 10f, "0.00 N s");
                p.MaxFragmentLaunchSpeed = QDUI.SliderRow(
                    "Max Fragment Launch Speed", p.MaxFragmentLaunchSpeed,
                    1f, 50f, "0.0 m/s");
                p.MaxWholeBodyLaunchSpeed = QDUI.SliderRow(
                    "Max Whole Body Push Speed", p.MaxWholeBodyLaunchSpeed,
                    1f, 50f, "0.0 m/s");
                p.TallFragmentToppleSpeed = QDUI.SliderRow(
                    "Tall Piece Topple", p.TallFragmentToppleSpeed, 0f, 5f, "0.00 N m s");
                GUILayout.Label(
                    "Break Push controls radial launch. Directional Impact Force optionally adds a " +
                    "capped speed along the incoming ray or Rigidbody travel direction, " +
                    "so shotgun debris continues away from the attacker even when stone pieces " +
                    "are heavy. It defaults to 0; high values create a strong push. " +
                    "Max Fragment Launch Speed caps loose debris, and with it how fast a piece " +
                    "may spin: a fragment's surface is never allowed to travel faster than the " +
                    "whole fragment may, so lowering it calms tumbling and stops thin debris " +
                    "from spinning through a floor. Max Whole Body Push Speed " +
                    "separately caps the intact Dynamic Rigidbody and its connected remainder, " +
                    "so a heavy prop may react strongly without turning small debris dangerous. " +
                    "Local Eject Impulse is a short, " +
                    "separation impulse whose effect decreases with mass, helping pieces clear " +
                    "inside the kinematic shell. Tall Piece Topple adds angular and sideways motion " +
                    "to upright splinters. These settings do not change baked geometry.",
                    QDUI.HelpText);
            }
            QDUI.EndFoldCard();
            if (EditorGUI.EndChangeCheck())
            {
                EditorUtility.SetDirty(p);
                // Launch/eject/topple are runtime-only. Rebuilding fracture topology here
                // made these harmless sliders appear to freeze the editor.
                Repaint();
            }

            QDUI.HelpFoldout("Start.PreviewRefresh", "How does automatic preview work?",
                "Click a preset or change a geometry value to replace the 3D fracture preview on " +
                "the model after a short pause. Material, cut-face UV layout and Tiling changes " +
                "only mark the object for rebake; they keep the intact model visible because the " +
                "colour-coded fragment preview does not represent final textured art. The preview " +
                "uses the current Max Parts value exactly and runs in the background. Open " +
                "Fracture only for its extra preview controls.");
            QDUI.EndOutlinedCard();
        }

        static AxisMode AxisModeRow(string label, AxisMode axis)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(label, QDUI.FieldLabel, GUILayout.Width(QDUI.LabelWidth));
            axis = (AxisMode)EditorGUILayout.EnumPopup(axis);
            EditorGUILayout.EndHorizontal();
            return axis;
        }

        static void DrawDirectionalBiasRows(DestructionProfile profile,
            string axisLabel = "Preferred Length Axis")
        {
            profile.DirectionalCutStrength = QDUI.SliderRow(
                "Directional Influence", profile.DirectionalCutStrength, 0f, 1f);
            using (new EditorGUI.DisabledScope(profile.DirectionalCutStrength <= 0.0001f))
            {
                profile.PreferredCutAxis = AxisModeRow(axisLabel, profile.PreferredCutAxis);
                if (profile.Pattern == FracturePattern.DirectionalWood ||
                    profile.Pattern == FracturePattern.LayeredSlabs)
                {
                    profile.LengthwiseSplitBias = QDUI.SliderRow(
                        "Lengthwise Split Bias", profile.LengthwiseSplitBias, 0f, 1f);
                }
            }
        }

        /// <summary>Patterns whose cut faces receive render-only interior detail.</summary>
        static bool PatternUsesScatter(FracturePattern pattern) =>
            pattern != FracturePattern.RadialGlass;

        static string DirectionalBiasHelp(string patternSpecific)
        {
            return "Directional Influence: 0 keeps the preset's original free cuts; 1 fully follows " +
                   "the selected local frame. Auto resolves length, width and thickness separately " +
                   "for every disconnected element. Lengthwise Split Bias: 0 cuts mainly across " +
                   "local length, 1 makes fracture lines run along it. " + patternSpecific;
        }

        static void DrawCutSurfaceUVModeRow(DestructionProfile profile)
        {
            EditorGUILayout.BeginHorizontal();
            QDUI.DrawFieldLabel("Cut Face UV Layout", GUILayout.Width(QDUI.LabelWidth));
            profile.CutSurfaceUVMode = (CutSurfaceUVMode)EditorGUILayout.Popup(
                Mathf.Clamp((int)profile.CutSurfaceUVMode, 0, CutUVModeNames.Length - 1),
                CutUVModeNames, GUILayout.MinWidth(105f));
            if (profile.CutSurfaceUVMode == CutSurfaceUVMode.PlanarProjection)
            {
                GUILayout.Space(6f);
                GUILayout.Label(new GUIContent("Tiling", "1 is neutral. 0.1 makes texture features 10x larger; 10 repeats them 10x more densely."),
                    QDUI.FieldLabel, GUILayout.Width(36f));
                profile.InteriorUVScale = Mathf.Clamp(
                    EditorGUILayout.FloatField(profile.InteriorUVScale, GUILayout.Width(54f)),
                    0.05f, 10f);
            }
            EditorGUILayout.EndHorizontal();
            QDUI.HelpFoldout("Start.CutUV", "How is the cut texture mapped?",
                profile.CutSurfaceUVMode == CutSurfaceUVMode.PlanarProjection
                    ? "Projected Tiling projects the material in each generated cut plane at a consistent world-space scale. Tiling is direct: 1 is neutral, 0.1 makes texture features 10x larger, and 10 repeats them 10x more densely."
                    : profile.CutSurfaceUVMode == CutSurfaceUVMode.FitToCut
                        ? "Submesh Island Fit (No Stretch) maps UVs smoothly across 2D cut space while bounding them strictly inside the submesh's UV atlas island. This eliminates line stretching streaks without bleeding into adjacent atlas textures."
                        : "Edge Stretch chooses the 1D UV edge run on the cut boundary and extrudes it through the face like a stretched wall.");
        }

        void DrawProfileLibrary()
        {
            RefreshProfileLibrary();
            // Unity can keep a deleted asset as a fake-null PPtr and render it as
            // "Missing". Normalize it to real Auto/None without creating any asset.
            if (m_profile == null && !System.Object.ReferenceEquals(m_profile, null))
            {
                m_profile = null;
                EditorUtility.SetDirty(this);
            }
            bool usingBuiltInProfile = IsAutoQuickProfile(m_profile);
            QDUI.HelpFoldout("Start.ProfileType", "Which profile will Bake use?",
                "Each material tile selects one shared built-in profile. Objects using CONCRETE, " +
                "for example, all use QD_Profile_UniformConcrete. Use CREATE NEW FROM SELECTED " +
                "when this object needs its own values; RESET TO DEFAULT restores the active " +
                "profile to the shipped defaults for its pattern.");
            GUILayout.Space(3f);

            EditorGUILayout.BeginHorizontal();
            // "AUTO" named an implementation detail — whether an asset exists yet — and asked
            // the author to choose between two things that behave identically until Bake.
            // Restoring the preset's defaults is the action actually missing here, and the
            // Profile Asset field below already says which profile is in use.
            using (new EditorGUI.DisabledScope(m_profile == null))
            {
                if (GUILayout.Button(new GUIContent("RESET TO DEFAULT",
                        "Restores every setting of this profile to the selected pattern's " +
                        "shipped defaults. Fracture shape, physics, strength and lifecycle " +
                        "are all reset; the pattern itself is kept."),
                        QDUI.GhostBtn, GUILayout.Width(160f)) &&
                    EditorUtility.DisplayDialog("Reset profile to defaults",
                        $"Restore '{m_profile.name}' to the {PresetLabel(m_profile.Pattern)} " +
                        "defaults?\n\nEvery customised value in this profile is replaced. " +
                        "If the profile is shared, every object using it receives the reset values.",
                        "Reset", "Cancel"))
                {
                    EndFracturePreviews();
                    Undo.RecordObject(m_profile, "Reset Destruction Profile");
                    ResetProfileToPresetDefaults(m_profile, m_profile.Pattern);
                    EditorUtility.SetDirty(m_profile);
                    SyncQuickFieldsFromProfile();
                    ScheduleFracturePreview();
                    ShowStatus(
                        $"'{m_profile.name}' restored to the " +
                        $"{PresetLabel(m_profile.Pattern)} defaults.");
                    GUIUtility.ExitGUI();
                }
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(2f);

            QDUI.HelpFoldout("Start.ProfileStorage", "Where are these settings saved?",
                usingBuiltInProfile
                ? "BUILT-IN SHARED PROFILE: this material tile uses one reusable asset from " +
                  "QuickDestruction/QuickProfiles. Every object on the same tile can share these " +
                  "values. Use CREATE NEW FROM SELECTED before customising only one object."
                : "CUSTOM PROFILE ASSET: a profile created by CREATE NEW FROM SELECTED or assigned " +
                  "manually. Assign the same asset to several props when they should behave " +
                  "identically; editing it then changes all of them. Clear the field to return to " +
                  "the currently selected built-in material profile.");

            EditorGUI.BeginChangeCheck();
            m_profile = (DestructionProfile)QDUI.ObjectRow(
                "Profile Asset", m_profile, typeof(DestructionProfile));
            if (EditorGUI.EndChangeCheck())
            {
                if (m_profile == null)
                {
                    SelectSharedPresetProfile(m_quickPreset, true);
                    ShowStatus(
                        $"Using the shared {PresetLabel(m_quickPreset)} profile. " +
                        "Use Create New From Selected for object-specific values.");
                    GUIUtility.ExitGUI();
                }
                else
                {
                    m_quickPreset = m_profile.Pattern;
                    if (m_profile.Pattern == FracturePattern.BrickCells)
                        m_quickBrickCellSize = ClampBrickCellSize(m_profile.BrickCellSize);
                    EnsureInteriorMaterialAssigned(m_profile, true);
                    ApplyProfileToSource(m_profile);
                    GUIUtility.ExitGUI();
                }
            }
            QDUI.HelpFoldout("Start.ProfileBake", "Which profile will Bake use?",
                usingBuiltInProfile
                    ? "Bake uses the shared profile selected by the active material tile. It does not create another profile for this object."
                    : "This custom asset will be baked. Pattern buttons edit it in place unless you explicitly choose a shared built-in profile.");

            if (m_profile != null && !IsInMemoryProfile(m_profile))
                DrawSharedProfileBanner(m_profile);
        }

        List<DestructibleRoot> GetSceneProfileUsers(DestructionProfile profile)
        {
            var result = new List<DestructibleRoot>();
            if (profile == null) return result;
            DestructibleRoot[] roots = Resources.FindObjectsOfTypeAll<DestructibleRoot>();
            for (int i = 0; i < roots.Length; i++)
            {
                DestructibleRoot root = roots[i];
                if (root != null && root.gameObject.scene.IsValid() && root.Profile == profile)
                    result.Add(root);
            }
            return result;
        }

        void DrawSharedProfileBanner(DestructionProfile profile)
        {
            if (profile == null || IsInMemoryProfile(profile)) return;
            if (Event.current.type == EventType.Layout ||
                m_layoutProfileUsers == null || m_layoutUsersProfile != profile)
            {
                m_layoutUsersProfile = profile;
                m_layoutProfileUsers = GetSceneProfileUsers(profile);
            }
            List<DestructibleRoot> users = m_layoutProfileUsers;
            string profileKind = IsAutoQuickProfile(profile)
                ? "BUILT-IN PROFILE"
                : "CUSTOM PROFILE";
            if (users.Count <= 1)
            {
                QDUI.InfoBox(users.Count == 1
                    ? $"{profileKind} - currently used by this one scene destructible."
                    : $"{profileKind} - it becomes shared when assigned to more than one destructible.");
                return;
            }

            QDUI.WarnBox(
                $"SHARED {profileKind} - used by {users.Count} scene destructibles. " +
                "Profile edits affect all of them; geometry settings require rebaking every affected object.");
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("MAKE PRIVATE COPY",
                    "Duplicates this profile and assigns the copy to this object only, so " +
                    "edits here stop affecting the other destructibles sharing it."),
                    QDUI.GhostBtn, GUILayout.Width(160f)))
            {
                MakeLocalProfileCopy(profile);
                GUIUtility.ExitGUI();
            }
            if (GUILayout.Button(new GUIContent("SELECT USERS",
                    "Selects every scene object currently assigned this profile, so you can " +
                    "see what an edit here would affect."),
                    QDUI.GhostBtn, GUILayout.Width(112f)))
            {
                var selected = new UnityEngine.Object[users.Count];
                for (int i = 0; i < users.Count; i++) selected[i] = users[i].gameObject;
                Selection.objects = selected;
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            GUILayout.Label(
                "MAKE PRIVATE COPY duplicates this profile, assigns the duplicate to this " +
                "object alone and leaves the other " + (users.Count - 1) + " on the original — " +
                "use it when this one prop needs different settings. SELECT USERS highlights " +
                "every object sharing the profile, so you can see what an edit would change " +
                "before you make it. Leaving both alone keeps the profile shared, which is " +
                "what you want when the objects are meant to behave identically.",
                QDUI.HelpText);
        }

        void MakeLocalProfileCopy(DestructionProfile sourceProfile)
        {
            if (sourceProfile == null) return;
            string sourcePath = AssetDatabase.GetAssetPath(sourceProfile);
            string folder = !string.IsNullOrEmpty(sourcePath) && sourcePath.Contains("/")
                ? sourcePath.Substring(0, sourcePath.LastIndexOf('/'))
                : BakePipeline.QuickProfileFolder;
            EnsureAssetFolder(folder);
            string ownerName = m_contextRoot != null ? m_contextRoot.name :
                m_source != null ? m_source.name : sourceProfile.name;
            string path = AssetDatabase.GenerateUniqueAssetPath(
                $"{folder}/{BakePipeline.MakeSafeFileName(ownerName)}_LocalProfile.asset");
            DestructionProfile copy = UnityEngine.Object.Instantiate(sourceProfile);
            copy.name = System.IO.Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(copy, path);
            AssetDatabase.SaveAssets();

            DestructibleRoot target = m_contextRoot;
            if (target == null && m_source != null)
                target = m_source.GetComponentInParent<DestructibleRoot>(true);
            if (target != null)
            {
                Undo.RecordObject(target, "Make Destruction Profile Local");
                target.Profile = copy;
                PrefabUtility.RecordPrefabInstancePropertyModifications(target);
                EditorUtility.SetDirty(target);
                m_contextRoot = target;
            }
            m_profile = copy;
            m_quickPreset = copy.Pattern;
            SyncQuickFieldsFromProfile();
            m_geometryDirty = target != null;
            m_layoutProfileUsers = null;
            m_layoutUsersProfile = null;
            ShowStatus($"Created local profile '{copy.name}' for this object only.");
        }

        DestructionProfile EnsureVolumeModeIsObjectLocal(
            DestructionProfile sourceProfile)
        {
            if (sourceProfile == null) return null;
            if (IsInMemoryProfile(sourceProfile)) return sourceProfile;
            DestructibleRoot target = m_contextRoot;
            if (target == null && m_source != null)
                target = m_source.GetComponentInParent<DestructibleRoot>(true);

            List<DestructibleRoot> users = GetSceneProfileUsers(sourceProfile);
            if (users.Count == 1 && target != null && users[0] == target)
                return sourceProfile;

            string path = AssetDatabase.GetAssetPath(sourceProfile);
            if (users.Count == 0 && target == null && m_source != null &&
                !string.IsNullOrEmpty(path))
            {
                string expectedQuickName = BakePipeline.MakeSafeFileName(m_source.name) +
                                           "_QuickProfile.asset";
                if (path.EndsWith("/QuickProfiles/" + expectedQuickName))
                    return sourceProfile;
            }

            // A shared/named profile is a reusable behaviour preset. Volume construction
            // belongs to one source mesh, so detach it before changing Full/Hollow.
            MakeLocalProfileCopy(sourceProfile);
            return m_profile;
        }

        void DrawResultEstimateCard()
        {
            int primary = m_profile != null ? Mathf.Max(1, m_profile.FragmentCount) : 55;
            int secondary = m_profile != null &&
                            m_profile.SecondaryBreakup != SecondaryBreakupLevel.Off
                ? m_profile.SecondaryEligibleFragments * m_profile.SecondaryPiecesPerFragment
                : 0;
            int bodies = Mathf.Max(0, primary + secondary);
            ColliderStrategy colliders = m_profile != null
                ? m_profile.Colliders
                : m_quickColliderStrategy;
            bool shell = m_profile != null ? m_profile.Solidify : m_quickSolidify;

            GUILayout.Space(7f);
            QDUI.BeginOutlinedCard(QDUI.Blue);
            EditorGUILayout.BeginHorizontal();
            QDUI.SubHeaderLabel("ESTIMATED RESULT");
            GUILayout.FlexibleSpace();
            QDUI.StatusPill(
                m_report != null ? m_report.RiskLabel + " RISK" : "ESTIMATE",
                m_report != null ? RiskColor(m_report.RiskLabel) : QDUI.TextDim, 92f);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            QDUI.StatBlock(primary.ToString(), "PRIMARY PARTS");
            QDUI.StatBlock(secondary > 0 ? secondary.ToString() : "OFF", "LEVEL-TWO");
            QDUI.StatBlock(bodies.ToString(), "RIGIDBODIES");
            QDUI.StatBlock(m_report != null
                    ? $"{m_report.EstimatedCacheMB(primary):0.0} MB"
                    : "ANALYZE",
                "CACHE");
            EditorGUILayout.EndHorizontal();
            GUILayout.Label(
                $"{(shell ? "Hollow Shell" : "Full Solid")}  |  {colliders} colliders  |  " +
                $"{(m_profile != null && m_profile.InteriorUseSourceMaterial ? "source-matched cut materials" : "custom interior material")}",
                QDUI.HelpText);
            QDUI.EndOutlinedCard();
        }

        void SelectLibraryProfile(DestructionProfile profile)
        {
            EndFracturePreviews();
            m_profile = profile;
            m_quickPreset = profile.Pattern;
            if (profile.Pattern == FracturePattern.BrickCells)
                m_quickBrickCellSize = ClampBrickCellSize(profile.BrickCellSize);
            EnsureInteriorMaterialAssigned(profile, true);
            PatternHintOverlay.Show(GetCurrentFractureSource(), profile, profile.Pattern);
            ShowStatus(
                $"Profile '{profile.name}' selected ({PresetLabel(profile.Pattern)} pattern).");
            GUIUtility.ExitGUI();
        }

        void RefreshProfileLibrary(bool force = false)
        {
            if (!force && m_profileLibrary != null &&
                EditorApplication.timeSinceStartup - m_profileLibraryScanTime < 10d)
                return;
            m_profileLibraryScanTime = EditorApplication.timeSinceStartup;
            m_profileLibrary ??= new List<DestructionProfile>();
            m_profileLibrary.Clear();
            foreach (string guid in AssetDatabase.FindAssets("t:DestructionProfile"))
            {
                var profile = AssetDatabase.LoadAssetAtPath<DestructionProfile>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (profile != null) m_profileLibrary.Add(profile);
            }
            m_profileLibrary.Sort((a, b) =>
                string.Compare(a.name, b.name, System.StringComparison.OrdinalIgnoreCase));
        }

        void DrawStepBakeCard()
        {
            int sourceCount = GetSourceCandidates().Count;
            int wholeBuildingMeshCount = IsWholeBuildingMode
                ? CollectWholeBuildingMeshObjects(GetWholeBuildingRoots()).Count
                : 0;
            FracturePattern shownPattern = m_profile != null
                ? m_profile.Pattern
                : m_quickPreset;
            QDUI.BeginWorkflowCard(3);
            QDUI.StepHeader("3", "BAKE & SETUP",
                sourceCount > 0 ? "READY TO BAKE" : "NEEDS STEP 1",
                sourceCount > 0 ? QDUI.Green : QDUI.Amber);

            DrawStartDamageTrigger();
            GUILayout.Space(7f);

            string bakeOptionStatus = (m_autoAnalyze ? "AUTO ANALYZE" : "MANUAL") +
                (m_placeInScene ? " • PLACE IN SCENE" : " • PREFAB ONLY");
            if (QDUI.BeginFoldCard("Output & Bake Options", false, bakeOptionStatus, QDUI.TextDim))
            {
                m_autoAnalyze = QDUI.ToggleRow("Auto Analyze Source", m_autoAnalyze);
                GUILayout.Label(
                    "When Source changes, mesh validity, topology and bake risk are checked again automatically.",
                    QDUI.HelpText);
                m_placeInScene = QDUI.ToggleRow("Place Result In Scene", m_placeInScene);
                GUILayout.Label(
                    "On: inserts the generated prefab at the Source transform and disables the original. " +
                    "Off: creates only the prefab and leaves the scene unchanged.", QDUI.HelpText);

                Material interior = m_profile != null ? m_profile.InteriorMaterial : null;
                bool useSourceInterior = m_profile != null && m_profile.InteriorUseSourceMaterial;
                GUILayout.Label(
                    useSourceInterior
                        ? "Cut surfaces: the object's own material (generated cut UV mode applies; nothing extra to set up)."
                        : interior != null
                            ? $"Cut surfaces: {interior.name} (assigned to the profile)."
                            : $"Cut surfaces: QD_{PresetLabel(shownPattern)}_Interior will be created and assigned.",
                    QDUI.HelpText);
            }
            QDUI.EndFoldCard();
            GUILayout.Space(8f);
            using (new EditorGUI.DisabledScope(!HasSourceOrSelectionCandidate()))
            {
                string actionLabel = IsWholeBuildingMode
                    ? $"ONE-CLICK: MERGE {wholeBuildingMeshCount} MESHES + BAKE AS ONE"
                    : sourceCount > 1
                    ? $"ONE-CLICK:  ANALYZE + BAKE {sourceCount} OBJECTS"
                    : "ONE-CLICK:  ANALYZE + BAKE & SETUP";
                if (QDUI.BigButton(actionLabel))
                {
                    RunOneClickBake();
                    GUIUtility.ExitGUI();
                }
            }
            if (!HasSourceOrSelectionCandidate())
                QDUI.WarnBox("Select a scene object with a MeshFilter to enable One-Click Setup.");

            QDUI.EndCard();
        }

        void DrawStartDamageTrigger()
        {
            QDUI.SubHeaderLabel("HOW THIS OBJECT TAKES DAMAGE");
            DamageTriggerMode current = m_profile != null
                ? m_profile.DamageTrigger
                : m_quickDamageTrigger;

            EditorGUILayout.BeginHorizontal();
            if (QDUI.Chip("RIGIDBODY IMPACT", current == DamageTriggerMode.RigidbodyImpact))
                SetQuickDamageTrigger(DamageTriggerMode.RigidbodyImpact);
            if (QDUI.Chip("CODE / METHOD", current == DamageTriggerMode.ScriptedOnly))
                SetQuickDamageTrigger(DamageTriggerMode.ScriptedOnly);
            if (QDUI.Chip("MIXED (RECOMMENDED)", current == DamageTriggerMode.Mixed))
                SetQuickDamageTrigger(DamageTriggerMode.Mixed);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            // Directional source overrides and scene-wide explosions are useful in every
            // damage mode, including a project that otherwise relies on Rigidbody impacts.
            bool showIntegrationGuide = true;
            switch (current)
            {
                case DamageTriggerMode.RigidbodyImpact:
                    QDUI.InfoBox(
                        "Automatic Rigidbody impacts are enabled. Put a Collider and Rigidbody on a bullet; " +
                        "its mass and collision speed determine the damage. Direct API calls remain available " +
                        "for exceptional gameplay events.");
                    break;
                case DamageTriggerMode.ScriptedOnly:
                    QDUI.InfoBox(
                        "Automatic collision damage is off. Your weapon, bullet, raycast or Damage Dealer " +
                        "must explicitly send the hit to the target.");
                    break;
                default:
                    QDUI.InfoBox(
                        "Mixed is the default: ordinary Rigidbody impacts work, and weapons, explosions or " +
                        "hitscan can also call the damage method.");
                    break;
            }

            GUILayout.Space(5f);
            bool dynamicIntactBody = m_profile != null &&
                                     m_profile.IntactBody == IntactBodyMode.Dynamic;
            bool activateAll = m_contextRoot != null
                ? m_contextRoot.ActivateAllOnFirstBreak
                : m_profile != null
                    ? m_profile.ActivateAllOnFirstBreak
                    : m_quickActivateAllOnFirstBreak;
            bool nextActivateAll = QDUI.ToggleRow(
                "Activate All Pieces On First Break", activateAll);
            if (nextActivateAll != activateAll)
            {
                m_quickActivateAllOnFirstBreak = nextActivateAll;
                if (m_contextRoot != null)
                {
                    Undo.RecordObject(m_contextRoot,
                        "Change First Break Activation For Object");
                    m_contextRoot.FirstBreakActivation = nextActivateAll
                        ? FirstBreakActivationOverride.AllPieces
                        : FirstBreakActivationOverride.Local;
                    EditorUtility.SetDirty(m_contextRoot);
                }
                else if (m_profile != null)
                {
                    Undo.RecordObject(m_profile, "Change First Break Activation");
                    m_profile.ActivateAllOnFirstBreak = nextActivateAll;
                    EditorUtility.SetDirty(m_profile);
                }
            }
            GUILayout.Label(nextActivateAll
                    ? "Full collapse: when Health reaches zero, every baked primary piece becomes physical immediately."
                    : dynamicIntactBody
                        ? "Dynamic local mode: the first hit ejects only pieces inside its radius and transfers the prop's velocity to them; the unbroken remainder stays attached for later hits."
                        : "Recommended local mode: the first hit ejects pieces inside its radius; later hits widen the hole.",
                QDUI.HelpText);
            if (m_contextRoot != null)
                GUILayout.Label(
                    "INSTANCE OVERRIDE: this choice affects only the selected destructible, even when its Profile is shared.",
                    QDUI.HelpText);

            if (showIntegrationGuide && QDUI.BeginFoldCard(
                    "Weapon, Directional Hit & Explosion API", false, "OPTIONAL", QDUI.TextDim))
                DrawScriptedDamageGuide(current == DamageTriggerMode.Mixed);
            if (showIntegrationGuide) QDUI.EndFoldCard();

            GUILayout.Label(
                "Demo controls: LMB performs a direct raycast ApplyDamage call. RMB or F fires a real " +
                "Rigidbody projectile, so it demonstrates physical impact damage. Released debris " +
                "does not consume later demo shots; they continue toward the attached shell behind it.",
                QDUI.HelpText);
        }

        void DrawOutputBakeOptions(FracturePattern shownPattern)
        {
            string status = (m_autoAnalyze ? "AUTO ANALYZE" : "MANUAL") +
                (m_placeInScene ? " | PLACE IN SCENE" : " | PREFAB ONLY");
            if (QDUI.BeginFoldCard("Output & Bake Options", false, status, QDUI.TextDim))
            {
                m_autoAnalyze = QDUI.ToggleRow("Auto Analyze Source", m_autoAnalyze);
                GUILayout.Label(
                    "When Source changes, mesh validity, topology and bake risk are checked again automatically.",
                    QDUI.HelpText);
                m_placeInScene = QDUI.ToggleRow("Place Result In Scene", m_placeInScene);
                GUILayout.Label(
                    "On: inserts the generated prefab at the Source transform and disables the original. " +
                    "Off: creates only the prefab and leaves the scene unchanged.", QDUI.HelpText);

                Material interior = m_profile != null ? m_profile.InteriorMaterial : null;
                bool useSourceInterior = m_profile != null && m_profile.InteriorUseSourceMaterial;
                GUILayout.Label(
                    useSourceInterior
                        ? "Cut surfaces: the object's own material (generated cut UV mode applies; nothing extra to set up)."
                        : interior != null
                            ? $"Cut surfaces: {interior.name} (assigned to the profile)."
                            : $"Cut surfaces: QD_{PresetLabel(shownPattern)}_Interior will be created and assigned.",
                    QDUI.HelpText);
            }
            QDUI.EndFoldCard();
        }

        void SetQuickDamageTrigger(DamageTriggerMode mode)
        {
            m_quickDamageTrigger = mode;
            if (m_profile != null)
            {
                Undo.RecordObject(m_profile, "Set Damage Trigger Mode");
                m_profile.DamageTrigger = mode;
                m_profile.CollisionDamage = mode != DamageTriggerMode.ScriptedOnly;
                EditorUtility.SetDirty(m_profile);
            }
            Repaint();
        }

        static void DrawScriptedDamageGuide(bool alsoSupportsImpact)
        {
            QDUI.InfoBox(
                "DestructibleRoot is the component automatically placed on the baked target object. " +
                "It owns Health and the baked fragments. Do not add it to your bullet - find it on the " +
                "object that the bullet hit.");
            if (QDUI.BeginHelpFoldout("RuntimeApi.DirectionalHit",
                    "Directional hit / shotgun impulse", false))
            {
                GUILayout.Label(
                    "PHYSICAL PROJECTILE: add Collider + Rigidbody, then optionally add Quick Destruction > " +
                    "Destruction Impact Source. It overrides Directional Force and local Impact Radius without " +
                    "dealing damage a second time. A code-driven bullet may use Destruction Damage Dealer.",
                    QDUI.HelpText);
                GUILayout.Label(
                    alsoSupportsImpact
                        ? "Mixed mode accepts this explicit hit alongside ordinary Rigidbody collisions."
                        : "For custom collision code, pass the projectile velocity as the incoming direction:",
                    QDUI.HelpText);
                EditorGUILayout.SelectableLabel(
                    "void OnCollisionEnter(Collision collision)\n" +
                    "{\n" +
                    "    var root = collision.collider.GetComponentInParent<DestructibleRoot>();\n" +
                    "    if (root == null) return;\n" +
                    "    var hit = collision.GetContact(0);\n" +
                    "    var projectileBody = GetComponent<Rigidbody>();\n" +
                    "#if UNITY_6000_0_OR_NEWER\n" +
                    "    Vector3 travel = projectileBody.linearVelocity.normalized;\n" +
                    "#else\n" +
                    "    Vector3 travel = projectileBody.velocity.normalized;\n" +
                    "#endif\n" +
                    "#if UNITY_6000_4_OR_NEWER\n" +
                    "    int sourceId = GetEntityId().GetHashCode();\n" +
                    "#else\n" +
                    "    int sourceId = GetInstanceID();\n" +
                    "#endif\n" +
                    "    root.ApplyDamage(new DamageContext(35f, hit.point, hit.normal,\n" +
                    "        0.75f, 0, sourceId, 15f, travel, 7f));\n" +
                    "}",
                    EditorStyles.textArea, GUILayout.MinHeight(224f));
                GUILayout.Label("HITSCAN / RAYCAST:", QDUI.HelpText);
                EditorGUILayout.SelectableLabel(
                    "if (Physics.Raycast(ray, out RaycastHit hit, 100f))\n" +
                    "{\n" +
                    "    var root = hit.collider.GetComponentInParent<DestructibleRoot>();\n" +
                    "    root?.ApplyDamage(new DamageContext(35f, hit.point, hit.normal,\n" +
                    "        0.75f, 0, sourceId, 15f, ray.direction, 7f));\n" +
                    "}",
                    EditorStyles.textArea, GUILayout.MinHeight(98f));
                QDUI.InfoBox(
                    "hit.normal is the surface normal, not the shot direction. ImpactDirection is " +
                    "ray.direction or projectile velocity. The final value is directional impulse in N s: " +
                    "0 disables it, -1 uses the target profile, and a positive value overrides it for this hit.");
            }
            QDUI.EndHelpFoldout();
            GUILayout.Space(3f);

            if (QDUI.BeginHelpFoldout("RuntimeApi.AreaExplosion",
                    "AOE explosion / Rigidbody impulse", false))
            {
                GUILayout.Label(
                    "ONE-METHOD API: finds every target in Radius, deduplicates multiple colliders, applies " +
                    "distance falloff and one radial hit per DestructibleRoot. Ordinary Rigidbodies receive " +
                    "AddExplosionForce with ForceMode.Impulse. Quick Destruction objects use their profile's " +
                    "explosion force mode and never receive the same impulse twice.", QDUI.HelpText);
                EditorGUILayout.SelectableLabel(
                    "int affected = DestructionExplosion.Detonate(\n" +
                    "    transform.position, radius: 6f, damage: 120f, force: 25f);",
                    EditorStyles.textArea, GUILayout.MinHeight(48f));
                GUILayout.Label(
                    "COMPONENT / UNITYEVENT: add Quick Destruction > Destruction Bomb, configure Radius, " +
                    "Damage, Force, Falloff, Affected Layers and Affect Other Rigidbodies, then call:",
                    QDUI.HelpText);
                EditorGUILayout.SelectableLabel(
                    "GetComponent<DestructionBomb>().Detonate();",
                    EditorStyles.textArea, GUILayout.MinHeight(24f));
                QDUI.InfoBox(
                    "Detonate() is public and can be connected directly to a UnityEvent or animation event. " +
                    "The resulting shockwave points from the bomb toward each target; no manual OverlapSphere " +
                    "or bomb-to-barrel vector calculation is required. The supplied demo E key uses this same " +
                    "AOE and exposes Range, Damage, Force and Damage Falloff. No-Hit Aim Distance only places " +
                    "the blast centre after the aim ray misses; it never changes damage or force.");
            }
            QDUI.EndHelpFoldout();
            GUILayout.Space(2f);
            if (QDUI.BeginHelpFoldout("RuntimeApi.FragmentPhysicsSafety",
                    "Why can a fragment pass through a floor?", false))
            {
                GUILayout.Label(
                    "A Unity Plane has no thickness. A thin fragment using Discrete or speculative-only " +
                    "contacts may cross it between two FixedUpdate steps. Unity's Continuous sweep protects " +
                    "primitive colliders, but not the convex MeshColliders used by normal fracture pieces. " +
                    "Quick Destruction therefore validates every released primary, level-two and runtime-split " +
                    "piece and keeps primitive CCD enabled. On top of that, a profile whose Structure > Intact " +
                    "Body is Dynamic adds an allocation-free static-ground guard for its convex debris and " +
                    "Dynamic residual clusters. Static and Release When Unsupported deliberately skip that " +
                    "per-step query and leave their debris to PhysX, so give a zero-thickness floor real " +
                    "thickness there. Fragment-to-fragment contacts remain discrete for performance.",
                    QDUI.HelpText);
                QDUI.InfoBox(
                    "After updating an older asset, runtime repair works immediately. Run Bake & Setup again if " +
                    "you also want the repaired Rigidbody/collider references stored permanently in the generated prefab. " +
                    "Separately from that guard, and on every profile, a piece landing on static ground has only " +
                    "its implausible upward rebound capped while its sideways motion is preserved. Also check the " +
                    "Physics layer matrix and keep the floor collider enabled and non-trigger.");
            }
            QDUI.EndHelpFoldout();
            GUILayout.Space(2f);
            GUILayout.Label(
                "DEBRIS LIFETIME: with Despawn Debris enabled (the default, 60 s) every " +
                "broken-off piece fades out on its own, so sustained fire across many " +
                "destructibles never accumulates rubble cost. To clear a wreck earlier from " +
                "gameplay code (end of a wave, cutscene, streaming the area out) call:",
                QDUI.HelpText);
            EditorGUILayout.SelectableLabel(
                "root.DespawnDebrisNow();",
                EditorStyles.textArea, GUILayout.MinHeight(24f));
            GUILayout.Label(
                "Use OnTriggerEnter(Collider other) for trigger bullets. Use BreakNow(new BreakContext(...)) " +
                "when a hit should break immediately instead of reducing Health first; while Runtime Re-Fracture " +
                "is enabled, that same direct hit also splits a loose grounded piece. Beyond the per-piece " +
                "Despawn Debris timer above, the Runtime tab's cleanup rule decides what becomes of a fragment " +
                "that settles or outlives its lifetime: Freeze In Place keeps it visible but inert, Despawn " +
                "returns it to the baked pool.", QDUI.HelpText);
        }

        /// <summary>
        /// Asks what a material tile should do to an assigned named profile. Returns false
        /// to leave everything as it was — including the selected preset, so Cancel really
        /// is a no-op. <paramref name="useSharedPresetProfile"/> requests switching this
        /// object from its custom asset to the shared built-in profile represented by the tile.
        /// </summary>
        bool ConfirmPresetOnNamedProfile(FracturePattern pattern,
            out bool useSharedPresetProfile)
        {
            useSharedPresetProfile = false;
            if (m_profile == null) return true;

            CountProfileUsers(m_profile, out int sceneUsers, out int prefabUsers);
            int users = Mathf.Max(sceneUsers, prefabUsers);
            string shared = users > 1
                ? $"\n\n{users} baked objects use this profile, so applying the preset " +
                  "changes all of them."
                : string.Empty;

            string presetName = PresetLabel(pattern).ToUpperInvariant();
            int choice = EditorUtility.DisplayDialogComplex(
                "Quick Destruction",
                $"\"{m_profile.name}\" is assigned to this object.\n\n" +
                $"Apply the {presetName} preset to that profile, or leave the profile alone " +
                $"and switch this object to the shared {presetName} built-in profile instead?" +
                shared,
                $"Apply {presetName} To Profile", "Cancel", $"Use Shared {presetName} Profile");

            if (choice == 1) return false;
            useSharedPresetProfile = choice == 2;
            return true;
        }

        void DrawQuickPresetChip(FracturePattern pattern, string label)
        {
            // While a named profile asset is assigned, IT is the active tile in the grid.
            bool isNamed = IsNamedProfileAsset(m_profile);
            bool selected = !isNamed && (m_profile != null
                ? m_profile.Pattern == pattern
                : m_quickPreset == pattern);
            if (!QDUI.PresetChip(pattern, label, selected)) return;

            if (isNamed)
            {
                if (!ConfirmPresetOnNamedProfile(pattern, out bool useSharedPresetProfile))
                    return;
                m_quickPreset = pattern;
                if (!useSharedPresetProfile)
                {
                    Undo.RecordObject(m_profile, "Apply Quick Destruction Preset");
                    m_profile.ApplyPreset(pattern);
                    if (!m_profile.InteriorUseSourceMaterial)
                        m_profile.InteriorMaterial =
                            PresetMaterialLibrary.GetInteriorMaterial(pattern);
                    if (pattern == FracturePattern.BrickCells)
                        m_quickBrickCellSize = ClampBrickCellSize(m_profile.BrickCellSize);
                    EditorUtility.SetDirty(m_profile);
                    PatternHintOverlay.Show(GetCurrentFractureSource(), m_profile, pattern);
                    ShowStatus(
                        $"{label} defaults applied to custom profile '{m_profile.name}'.");
                    GUIUtility.ExitGUI();
                    return;
                }
            }

            SelectSharedPresetProfile(pattern, true);
            PatternHintOverlay.Show(GetCurrentFractureSource(), m_profile, pattern);
            ShowStatus(
                $"Shared {label} profile selected. Use Create New From Selected before " +
                "making values unique to this object.");
            // Brick adds its dimension controls to Quick Setup; other presets remove them.
            GUIUtility.ExitGUI();
        }

        static string PresetLabel(FracturePattern pattern)
        {
            switch (pattern)
            {
                case FracturePattern.RadialGlass: return "Glass";
                case FracturePattern.DirectionalWood: return "Wood";
                case FracturePattern.BrickCells: return "Brick";
                case FracturePattern.LayeredSlabs: return "Slab";
                case FracturePattern.ColumnarPillars: return "Basalt";
                case FracturePattern.ImpactShatter: return "Stone";
                default: return "Concrete";
            }
        }

        AnalysisReport AnalyzeCurrentSource(bool rebuildWholeBuilding)
        {
            if (m_source == null) return null;
            if (!TryPrepareWholeBuildingSource(rebuildWholeBuilding,
                    out GameObject fractureSource, out string preparationError))
            {
                var blocked = new AnalysisReport { Source = m_source };
                blocked.Items.Add(new AnalysisItem(
                    AnalysisSeverity.Blocked, "QD-ASSEMBLY-001",
                    "Whole Building source could not be prepared",
                    string.IsNullOrEmpty(preparationError)
                        ? "The selected modular meshes could not be combined."
                        : preparationError,
                    "Check that every selected module is in one scene and has a " +
                    "MeshFilter + MeshRenderer on the same object."));
                return blocked;
            }

            AnalysisReport report = MeshAnalyzer.Analyze(fractureSource);
            if (IsWholeBuildingMode && m_combinedSource != null &&
                report.Status != AnalysisSeverity.Blocked)
            {
                report.Items.Insert(0, new AnalysisItem(
                    AnalysisSeverity.Info, "QD-ASSEMBLY-OK",
                    $"Whole Building source ({m_combinedSource.MeshCount} modules)",
                    $"{m_combinedSource.VertexCount:n0} vertices and " +
                    $"{m_combinedSource.TriangleCount:n0} triangles are combined in memory " +
                    "and fractured as one source. Matching contact walls are merged by the solver.",
                    "Use Preview to inspect the complete building partition before Bake."));
            }
            return report;
        }

        void RunAnalysis()
        {
            m_report = AnalyzeCurrentSource(true);
            Repaint();
        }

        void RunWholeBuildingBake()
        {
            RestoreAssignedRebakeProfile();
            GameObject generatedRebakeObject = m_source != null &&
                m_source.GetComponent<DestructibleRoot>() != null
                    ? m_source
                    : null;
            DestructibleRoot generatedRebakeRoot = generatedRebakeObject != null
                ? generatedRebakeObject.GetComponent<DestructibleRoot>()
                : null;
            if (generatedRebakeRoot != null)
            {
                // Preparing the modular proxy changes m_source to the authored anchor.
                // Keep the placed destructible as the edit/profile owner so the Hollow
                // Shell prompt does not detach a second local profile from that anchor.
                m_contextRoot = generatedRebakeRoot;
            }
            if (!TryPrepareWholeBuildingSource(true,
                    out GameObject fractureSource, out string preparationError))
            {
                string message = string.IsNullOrEmpty(preparationError)
                    ? "Whole Building source could not be prepared."
                    : preparationError;
                ShowStatus(message, 12d);
                EditorUtility.DisplayDialog(
                    "Quick Destruction - Whole Building blocked", message, "OK");
                return;
            }

            m_report = MeshAnalyzer.Analyze(fractureSource);
            if (m_report == null || m_report.Status == AnalysisSeverity.Blocked)
            {
                string blocker = DescribeFirstBlocker(m_report);
                ShowStatus("Whole Building Bake blocked - " + blocker, 12d);
                return;
            }
            bool confirmed;
            bool allowUnsafeFractureInput;
            m_isBakingSelection = true;
            try
            {
                // A modal window pumps editor focus/selection notifications. Do not let
                // those notifications replace the generated rebake context mid-click.
                confirmed = TryConfirmUnsafeFractureInput(
                    m_report, m_profile, "BAKE", out allowUnsafeFractureInput);
            }
            finally
            {
                m_isBakingSelection = false;
                m_selectionRefreshPending = false;
                m_ignoreSelectionUntil = EditorApplication.timeSinceStartup + 0.5d;
            }
            if (!confirmed)
            {
                ShowStatus("Whole Building Bake cancelled. Sources were not changed.");
                return;
            }

            if (IsInMemoryProfile(m_profile))
            {
                if (!SaveInMemoryProfileToAsset())
                {
                    ShowStatus("Bake cancelled. No profile or generated asset was changed.");
                    return;
                }
            }
            else if (m_profile == null)
            {
                CreateOrLoadQuickProfile();
            }
            if (m_profile == null)
            {
                ShowStatus("Whole Building Bake needs a reusable profile.");
                return;
            }

            // Configuring Hollow Shell can create/save an object-local profile, and ending
            // Preview can release temporary render state. Complete both before creating the
            // final HideAndDontSave proxy. Nothing that can refresh assets or clean preview
            // objects is allowed between this reconstruction and BakePipeline.Bake.
            EndFracturePreviews();
            if (!TryPrepareWholeBuildingSource(true,
                    out fractureSource, out preparationError))
            {
                string message = string.IsNullOrEmpty(preparationError)
                    ? "Whole Building source could not be restored after Hollow Shell setup."
                    : preparationError;
                ShowStatus(message, 12d);
                EditorUtility.DisplayDialog(
                    "Quick Destruction - Whole Building blocked", message, "OK");
                return;
            }

            bool usesProxy = m_combinedSource != null &&
                             fractureSource == m_combinedSource.ProxyObject;
            GameObject anchor = usesProxy ? m_combinedSource.Anchor : m_source;
            IReadOnlyList<GameObject> originalRoots = usesProxy
                ? m_combinedSource.OriginalRoots
                : null;
            m_isBakingSelection = true;
            try
            {
                // A proxy is never placed or linked directly: it is transient. Once its
                // prefab is complete, placement uses the authored anchor and records every
                // modular source root so Clean can restore the complete building.
                m_lastBake = BakePipeline.Bake(fractureSource, m_profile,
                    usesProxy ? false : m_placeInScene,
                    allowUnsafeFractureInput, null, true,
                    m_wholeBuildingShowShellWhileIntact,
                    usesProxy ? m_combinedSource.ModuleCollisionSources : null);
                if (m_lastBake.Success && usesProxy && m_placeInScene &&
                    generatedRebakeObject == null)
                {
                    GameObject placed = BakePipeline.PlaceGeneratedInstance(
                        m_lastBake.PrefabPath, anchor, m_profile, originalRoots);
                    if (placed == null)
                        m_lastBake.Warnings.Add(
                            "The prefab was baked, but its scene instance could not be placed. " +
                            "The original modular sources were left active.");
                    else
                        Selection.activeGameObject = placed;
                }
                else if (m_lastBake.Success && generatedRebakeObject != null)
                {
                    // The prefab asset backing this scene instance was replaced in place.
                    // Do not instantiate a duplicate building on a rebake.
                    Selection.activeGameObject = generatedRebakeObject;
                }
            }
            finally
            {
                m_isBakingSelection = false;
                m_ignoreSelectionUntil = EditorApplication.timeSinceStartup + 0.5d;
                m_source = generatedRebakeObject != null
                    ? generatedRebakeObject
                    : anchor;
                ReleaseCombinedSource(false);
            }

            if (m_lastBake.Success)
            {
                m_geometryDirty = false;
                m_workspace = Workspace.Bake;
                string summary =
                    $"Whole Building baked as one destructible: " +
                    $"{m_lastBake.FragmentCount} primary + " +
                    $"{m_lastBake.SecondaryFragmentCount} level-two pieces in " +
                    $"{m_lastBake.Seconds:0.0}s.";
                ShowStatus(summary, 12d);
                SceneView.lastActiveSceneView?.ShowNotification(new GUIContent(summary));
            }
            else
            {
                ShowStatus(m_lastBake.Error, 12d);
                if (!m_lastBake.Cancelled)
                    EditorUtility.DisplayDialog(
                        "Quick Destruction - Whole Building bake failed",
                        string.IsNullOrEmpty(m_lastBake.Error)
                            ? "The bake produced no usable output. See the Console for details."
                            : m_lastBake.Error,
                        "OK");
            }
        }

        void RunOneClickBake()
        {
            m_sourceCandidateCache = null;
            AdoptSelectionIfSourceMissing();
            List<GameObject> sources = GetSourceCandidates();
            if (sources.Count == 0)
            {
                ShowStatus("Select one or more scene objects with a MeshFilter before running One-Click Setup.");
                return;
            }

            m_source = sources[0];
            if (!PrepareBatchProfile("One-Click")) return;

            if (IsWholeBuildingMode)
            {
                RunWholeBuildingBake();
                return;
            }

            RunSourceBatchBake(sources, "One-Click");
        }

        /// <summary>
        /// Saves/creates the reusable profile a batch has to hand to every source. Returns
        /// false when the author cancelled, in which case nothing was written.
        /// </summary>
        /// <summary>
        /// One container per selected root, so a Root + Nested Meshes batch leaves a
        /// single object in the Hierarchy instead of one loose destructible per nested
        /// mesh at the scene root. This is the grouping a Whole Building bake ends up
        /// with, without its merge: every piece stays an independent destructible with
        /// its own profile, fragments and structural support.
        /// </summary>
        Transform ResolveBatchOutputGroup(GameObject source,
            Dictionary<GameObject, Transform> groups)
        {
            if (!m_placeInScene || groups == null || source == null ||
                !source.scene.IsValid() ||
                m_sourceSelectionMode != SourceSelectionMode.RootHierarchy)
                return null;

            GameObject owner = FindSelectedRootFor(source);
            if (owner == null) return null;
            if (groups.TryGetValue(owner, out Transform cached) && cached != null)
                return cached;

            // Rebaking an existing batch: the author selected the container this batch
            // produced last time. Appending the suffix again would nest a
            // "..._Destructibles_Destructibles" beside it and leave two generations of the
            // same building in the Hierarchy — and the caller would then hide the first
            // one, which is every object the author was actually looking at.
            if (BakePipeline.IsOutputGroupContainer(owner))
            {
                groups[owner] = owner.transform;
                return owner.transform;
            }

            // Beside the baked object, never inside it: the output is not part of the
            // authored model, and nesting it there would hide or move every destructible
            // along with the source root the author still owns.
            string groupName = owner.name + BakePipeline.OutputGroupSuffix;
            Transform parent = owner.transform.parent;
            Transform group = parent != null
                ? parent.Find(groupName)
                : FindSceneRootTransform(owner.scene, groupName);
            if (group == null)
            {
                var go = new GameObject(groupName);
                Undo.RegisterCreatedObjectUndo(go, "Quick Destruction Group Output");
                if (parent != null) go.transform.SetParent(parent, false);
                else if (go.scene != owner.scene)
                    UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(
                        go, owner.scene);
                group = go.transform;
            }
            // An unrotated, unscaled container keeps every placed instance on the exact
            // world transform its own source defines.
            group.localPosition = Vector3.zero;
            group.localRotation = Quaternion.identity;
            group.localScale = Vector3.one;
            groups[owner] = group;
            return group;
        }

        /// <summary>A top-level object of the scene, by name.</summary>
        static Transform FindSceneRootTransform(
            UnityEngine.SceneManagement.Scene scene, string name)
        {
            if (!scene.IsValid() || !scene.isLoaded) return null;
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
                if (roots[i] != null && string.Equals(roots[i].name, name,
                        System.StringComparison.Ordinal))
                    return roots[i].transform;
            return null;
        }

        /// <summary>The selected root a resolved batch source came from.</summary>
        GameObject FindSelectedRootFor(GameObject source)
        {
            if (m_selectedSources == null || source == null) return null;
            for (int i = 0; i < m_selectedSources.Count; i++)
            {
                GameObject root = m_selectedSources[i];
                if (root == null || !root.scene.IsValid()) continue;
                if (source == root || source.transform.IsChildOf(root.transform))
                    return root;
            }
            return null;
        }

        static bool ResolveAuthoredActiveState(GameObject original)
        {
            if (original == null) return false;
            DestructibleRoot[] generated =
                Resources.FindObjectsOfTypeAll<DestructibleRoot>();
            for (int i = 0; i < generated.Length; i++)
            {
                DestructibleRoot result = generated[i];
                if (result == null || !result.gameObject.scene.IsValid() ||
                    EditorUtility.IsPersistent(result) ||
                    result.OriginalSourceObjects == null ||
                    result.OriginalSourceActiveStates == null) continue;
                int index = result.OriginalSourceObjects.IndexOf(original);
                if (index >= 0 && index < result.OriginalSourceActiveStates.Count)
                    return result.OriginalSourceActiveStates[index];
                if (result.OriginalHierarchyRoot == original)
                    return result.OriginalHierarchyRootActiveState;
            }
            return original.activeSelf;
        }

        readonly struct BatchPlacedResult
        {
            public readonly GameObject Instance;
            public readonly GameObject Source;
            public readonly GameObject Owner;

            public BatchPlacedResult(GameObject instance, GameObject source,
                GameObject owner)
            {
                Instance = instance;
                Source = source;
                Owner = owner;
            }
        }

        static void LinkBatchOriginal(DestructibleRoot result,
            GameObject original, bool authoredActive)
        {
            if (result == null || original == null) return;
            Undo.RecordObject(result, "Quick Destruction Link Batch Source");
            result.OriginalSourceObjects ??= new List<GameObject>();
            result.OriginalSourceActiveStates ??= new List<bool>();
            int index = result.OriginalSourceObjects.IndexOf(original);
            if (index < 0)
            {
                result.OriginalSourceObjects.Add(original);
                result.OriginalSourceActiveStates.Add(authoredActive);
            }
            else
            {
                while (result.OriginalSourceActiveStates.Count <= index)
                    result.OriginalSourceActiveStates.Add(true);
                result.OriginalSourceActiveStates[index] = authoredActive;
            }
            EditorUtility.SetDirty(result);
        }

        static void LinkBatchHierarchyRoot(DestructibleRoot result,
            GameObject hierarchyRoot, bool authoredActive)
        {
            if (result == null || hierarchyRoot == null) return;
            Undo.RecordObject(result, "Quick Destruction Link Batch Hierarchy");
            result.OriginalHierarchyRoot = hierarchyRoot;
            result.OriginalHierarchyRootActiveState = authoredActive;
            EditorUtility.SetDirty(result);
        }

        void RestoreAssignedRebakeProfile()
        {
            DestructibleRoot owner = m_source != null
                ? m_source.GetComponentInParent<DestructibleRoot>(true) : null;
            if (owner == null && m_contextRoot != null && m_source != null &&
                (m_contextRoot.OriginalSourceObject == m_source ||
                 m_contextRoot.OriginalHierarchyRoot == m_source ||
                 m_contextRoot.OriginalSourceObjects.Contains(m_source)))
                owner = m_contextRoot;
            if (owner == null || owner.Profile == null) return;
            m_contextRoot = owner;
            m_profile = owner.Profile;
            m_quickPreset = m_profile.Pattern;
            SyncQuickFieldsFromProfile();
        }

        bool PrepareBatchProfile(string actionName)
        {
            RestoreAssignedRebakeProfile();
            if (IsInMemoryProfile(m_profile))
            {
                if (!SaveInMemoryProfileToAsset())
                {
                    ShowStatus($"{actionName} cancelled. No profile or generated asset was changed.");
                    return false;
                }
            }
            else if (m_profile == null)
            {
                CreateOrLoadQuickProfile();
            }
            EnsureInteriorMaterialAssigned(m_profile, false);

            if (m_profile == null)
            {
                ShowStatus($"{actionName} could not create its reusable profile asset.");
                return false;
            }
            return true;
        }

        /// <summary>
        /// Analyzes and bakes every resolved source in one pass, one independent
        /// destructible per mesh. Both batch entry points share it: Root + Nested Meshes
        /// and a multi-object selection promise the whole list is processed, so neither
        /// Bake &amp; Setup nor One-Click may stop after the first source.
        /// </summary>
        void RunSourceBatchBake(List<GameObject> sources, string actionName)
        {
            if (sources == null || sources.Count == 0) return;
            EndFracturePreviews();
            int baked = 0;
            int blocked = 0;
            int failed = 0;
            AnalysisReport firstReport = null;
            BakeOutcome lastSuccess = null;
            bool? batchUnsafeInputDecision = null;
            var outputGroups = new Dictionary<GameObject, Transform>();
            bool shellAsked = HasBatchPanelChoice(sources);
            bool shellChosen = shellAsked &&
                m_batchPanelChoice == HollowShellBakeChoice.UseHollowShell;
            bool shellFullSolidAnyway = shellAsked &&
                m_batchPanelChoice == HollowShellBakeChoice.BakeFullSolidAnyway;
            bool shellWhileIntact = shellAsked
                ? m_batchPanelShowIntact : m_wholeBuildingShowShellWhileIntact;
            float shellThickness = shellAsked ? m_batchPanelThickness : m_profile != null
                ? Mathf.Max(0.001f, m_profile.SolidifyThickness)
                : 0.08f;
            int shellSources = 0;
            bool authoredSolidify = m_profile != null && m_profile.Solidify;
            float authoredThickness = m_profile != null
                ? m_profile.SolidifyThickness
                : 0.08f;
            int authoredFragmentCount = m_profile != null
                ? Mathf.Max(2, m_profile.FragmentCount)
                : 2;
            int batchPartsPerMesh = GetBatchPartsPerMesh(sources);
            bool batchCancelled = false;
            bool batchStopped = false;
            var sourceAuthoredStates = new Dictionary<GameObject, bool>();
            var ownerAuthoredStates = new Dictionary<GameObject, bool>();
            var ownerExpectedSources = new Dictionary<GameObject, int>();
            var ownerBakedSources = new Dictionary<GameObject, int>();
            var placedResults = new List<BatchPlacedResult>();
            for (int sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
            {
                GameObject batchSource = sources[sourceIndex];
                if (batchSource == null) continue;
                sourceAuthoredStates[batchSource] =
                    ResolveAuthoredActiveState(batchSource);
                GameObject owner = FindSelectedRootFor(batchSource);
                if (owner == null) continue;
                if (!ownerAuthoredStates.ContainsKey(owner))
                    ownerAuthoredStates[owner] = ResolveAuthoredActiveState(owner);
                ownerExpectedSources.TryGetValue(owner, out int expected);
                ownerExpectedSources[owner] = expected + 1;
            }
            string profilePath = AssetDatabase.GetAssetPath(m_profile);
            m_isBakingSelection = true;
            try
            {
                for (int i = 0; i < sources.Count; i++)
                {
                    GameObject source = sources[i];
                    if (source == null) continue;
                    var progress = new BakeProgressContext(i, sources.Count);
                    if (EditorUtility.DisplayCancelableProgressBar(
                            progress.Title,
                            progress.FormatMessage(
                                source.name, "Analyzing source...", 0d),
                            progress.OverallProgress(0f)))
                    {
                        batchCancelled = true;
                        break;
                    }
                    AnalysisReport report = MeshAnalyzer.Analyze(source);
                    if (firstReport == null) firstReport = report;
                    if (report == null || report.Status == AnalysisSeverity.Blocked)
                    {
                        blocked++;
                        AnalysisItem blocker = report?.Items.Find(
                            item => item != null && item.Severity == AnalysisSeverity.Blocked);
                        string blockerDetails = blocker != null
                            ? $"{blocker.Code}: {blocker.Title}\n" +
                              $"Why: {blocker.Why}\nFix: {blocker.Fix}"
                            : "Analyze did not return blocker details.";
                        Debug.LogWarning(
                            $"[Quick Destruction] Batch skipped '{source.name}'.\n{blockerDetails}",
                            source);
                        continue;
                    }

                    m_source = source;
                    m_report = report;

                    // The previous object may have baked with this batch's Hollow Shell
                    // override. Every decision below reads the authored Volume Mode, so
                    // put it back before the flat test runs again.
                    if (m_profile != null)
                    {
                        m_profile.Solidify = authoredSolidify;
                        m_profile.SolidifyThickness = authoredThickness;
                        m_profile.FragmentCount = authoredFragmentCount;
                    }

                    // Wall Thickness is only ever added to the flat, zero-thickness
                    // meshes in the list. Anything with modelled depth keeps its authored
                    // geometry, which is what the modular dialog below promises.
                    bool sourceIsFlat = m_profile != null && report.IsFlatSource &&
                        (m_profile.Solidify || (!MeshAnalyzer.ValidateClosedFractureInput(
                            report, m_profile, out string flatWarning) &&
                        MeshAnalyzer.IsRecoverableUnsafeInput(flatWarning) &&
                        MeshAnalyzer.IsFlatSourceWarning(flatWarning)));

                    // Asked once for the whole batch, with the same window a modular
                    // building shows, and the answer is reused for every flat mesh after
                    // it. A native progress window must not stand in front of it.
                    if (sourceIsFlat && !shellAsked && !authoredSolidify)
                    {
                        shellAsked = true;
                        if (Application.isBatchMode)
                        {
                            shellChosen = true;
                        }
                        else
                        {
                            EditorUtility.ClearProgressBar();
                            GameObject owner = FindSelectedRootFor(source);
                            HollowShellBakeChoice shellChoice =
                                HollowShellThicknessPrompt.Show(
                                    owner != null ? owner.name : source.name,
                                    shellThickness, shellWhileIntact,
                                    !m_profile.Solidify,
                                    out float promptThickness,
                                    out bool promptWhileIntact);
                            if (shellChoice == HollowShellBakeChoice.Cancel)
                            {
                                batchCancelled = true;
                                break;
                            }
                            if (shellChoice == HollowShellBakeChoice.BakeFullSolidAnyway)
                            {
                                shellFullSolidAnyway = true;
                            }
                            else
                            {
                                shellChosen = true;
                                shellThickness = Mathf.Clamp(promptThickness,
                                    HollowShellThicknessPrompt.MinThicknessMeters,
                                    HollowShellThicknessPrompt.MaxThicknessMeters);
                                shellWhileIntact = promptWhileIntact;
                                m_wholeBuildingShowShellWhileIntact = promptWhileIntact;
                            }
                        }
                    }

                    bool useHollowShellHere = sourceIsFlat && shellChosen;
                    bool allowUnsafeFractureInput =
                        sourceIsFlat && shellFullSolidAnyway;
                    if (!sourceIsFlat &&
                        !MeshAnalyzer.ValidateClosedFractureInput(
                            report, m_profile, out string fractureWarning) &&
                        MeshAnalyzer.IsRecoverableUnsafeInput(fractureWarning))
                    {
                        if (!batchUnsafeInputDecision.HasValue)
                        {
                            // A native progress window must not remain in front of the
                            // topology decision dialog. The next object/stage restores the
                            // same overall batch position immediately.
                            EditorUtility.ClearProgressBar();
                            batchUnsafeInputDecision = !Application.isBatchMode &&
                                EditorUtility.DisplayDialog(
                                    $"Quick Destruction - {actionName} warning",
                                    fractureWarning +
                                    "\n\nContinue anyway for unsafe topology sources in " +
                                    "this batch? Every generated fragment is still validated.",
                                    "BAKE ANYWAY", "SKIP UNSAFE SOURCES");
                        }

                        if (!batchUnsafeInputDecision.Value)
                        {
                            blocked++;
                            continue;
                        }
                        allowUnsafeFractureInput = true;
                    }
                    // Each bake saves assets and may unload the profile wrapper. Reload an
                    // asset-backed profile before the next object just like the demo builder.
                    DestructionProfile bakeProfile = string.IsNullOrEmpty(profilePath)
                        ? m_profile
                        : AssetDatabase.LoadAssetAtPath<DestructionProfile>(profilePath);
                    if (bakeProfile == null)
                    {
                        failed++;
                        batchStopped = true;
                        Debug.LogError(
                            $"[Quick Destruction] Batch stopped: profile could not be reloaded from " +
                            $"'{profilePath}'.");
                        break;
                    }
                    m_profile = bakeProfile;
                    // Per object, never on the asset the author owns: only the flat
                    // meshes bake with Wall Thickness, and the profile is put back the
                    // way it was found when the batch ends.
                    bakeProfile.Solidify = useHollowShellHere ||
                        (authoredSolidify && !(sourceIsFlat && shellFullSolidAnyway));
                    bakeProfile.SolidifyThickness = useHollowShellHere
                        ? shellThickness
                        : authoredThickness;
                    // Root + Nested is intentionally a lightweight batch. Its transient
                    // per-mesh budget prevents a 200-part profile from becoming 2400
                    // primary parts across 12 children; the reusable profile is restored
                    // before anything is saved.
                    bakeProfile.FragmentCount = batchPartsPerMesh;
                    if (useHollowShellHere) shellSources++;
                    BakeOutcome outcome = BakePipeline.Bake(source, bakeProfile,
                        m_placeInScene, allowUnsafeFractureInput, progress,
                        showShellThicknessWhileIntact:
                            useHollowShellHere && shellWhileIntact,
                        placementParent: ResolveBatchOutputGroup(source, outputGroups),
                        precomputedReport: report);
                    if (outcome.Success)
                    {
                        baked++;
                        lastSuccess = outcome;
                        if (m_placeInScene && outcome.SceneInstance != null)
                        {
                            DestructibleRoot generated =
                                outcome.SceneInstance.GetComponentInChildren<DestructibleRoot>(true);
                            GameObject owner = FindSelectedRootFor(source);
                            if (generated != null)
                                placedResults.Add(new BatchPlacedResult(
                                    outcome.SceneInstance, source, owner));
                            if (generated != null && owner != null &&
                                ownerAuthoredStates.TryGetValue(owner,
                                    out _))
                            {
                                ownerBakedSources.TryGetValue(owner, out int completed);
                                ownerBakedSources[owner] = completed + 1;
                            }
                        }
                    }
                    else if (outcome.Cancelled)
                    {
                        batchCancelled = true;
                        break;
                    }
                    else
                    {
                        failed++;
                        Debug.LogError(
                            $"[Quick Destruction] Batch bake failed for '{source.name}': {outcome.Error}",
                            source);
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                if (m_profile != null)
                {
                    m_profile.Solidify = authoredSolidify;
                    m_profile.SolidifyThickness = authoredThickness;
                    m_profile.FragmentCount = authoredFragmentCount;
                }
                // BakePipeline skips its per-object SaveAssets while a batch is running.
                AssetDatabase.SaveAssets();
                m_sourceCandidateCache = null;
                m_isBakingSelection = false;
                m_ignoreSelectionUntil = EditorApplication.timeSinceStartup + 0.5d;
                m_source = sources[0];
                m_report = firstReport;
                if (!string.IsNullOrEmpty(profilePath))
                {
                    var reloadedProfile =
                        AssetDatabase.LoadAssetAtPath<DestructionProfile>(profilePath);
                    if (reloadedProfile != null) m_profile = reloadedProfile;
                }
            }

            if (lastSuccess != null)
                m_lastBake = lastSuccess;
            // Saving a later prefab can refresh earlier prefab instances. Apply the
            // scene-only source links after the complete asset batch has finished so no
            // subsequent import can discard those prefab overrides.
            foreach (BatchPlacedResult placed in placedResults)
            {
                if (placed.Instance == null) continue;
                DestructibleRoot generated =
                    placed.Instance.GetComponentInChildren<DestructibleRoot>(true);
                if (generated == null) continue;
                if (placed.Source != null &&
                    sourceAuthoredStates.TryGetValue(placed.Source,
                        out bool sourceWasActive))
                    LinkBatchOriginal(generated, placed.Source, sourceWasActive);
                if (placed.Owner != null &&
                    ownerAuthoredStates.TryGetValue(placed.Owner,
                        out bool ownerWasActive))
                    LinkBatchHierarchyRoot(generated, placed.Owner, ownerWasActive);
            }
            int hiddenOwners = 0;
            if (m_placeInScene)
            {
                foreach (KeyValuePair<GameObject, int> pair in ownerExpectedSources)
                {
                    GameObject owner = pair.Key;
                    ownerBakedSources.TryGetValue(owner, out int completed);
                    if (owner == null) continue;
                    if (completed != pair.Value)
                    {
                        bool authoredActive = ownerAuthoredStates.TryGetValue(owner,
                            out bool storedActive) && storedActive;
                        if (owner.activeSelf != authoredActive)
                        {
                            Undo.RecordObject(owner,
                                "Quick Destruction Restore Partial Hierarchy");
                            owner.SetActive(authoredActive);
                            EditorUtility.SetDirty(owner);
                        }
                        continue;
                    }
                    // Hiding the authored model once its destructible replacement stands
                    // in the scene is the point. Hiding the batch's own output container is
                    // the opposite: the results were rebaked into that very object, so
                    // switching it off empties the scene.
                    if (BakePipeline.IsOutputGroupContainer(owner)) continue;
                    if (owner.activeSelf)
                    {
                        Undo.RecordObject(owner,
                            "Quick Destruction Hide Baked Hierarchy");
                        owner.SetActive(false);
                        EditorUtility.SetDirty(owner);
                    }
                    hiddenOwners++;
                }
            }
            if (baked > 0) m_geometryDirty = false;
            m_workspace = baked > 0 ? Workspace.Bake : Workspace.Analyze;
            int notProcessed = Mathf.Max(0,
                sources.Count - baked - blocked - failed);
            string outcomeLabel = batchCancelled
                ? "Batch cancelled"
                : batchStopped || notProcessed > 0
                    ? "Batch stopped"
                    : "Batch complete";
            string summary =
                $"{outcomeLabel}: {baked} baked, {blocked} blocked, {failed} failed" +
                (notProcessed > 0 ? $", {notProcessed} not processed" : string.Empty) +
                ". " +
                (m_placeInScene
                    ? "Results were placed at their source transforms."
                    : "Prefabs were created without changing the scene.") +
                $" Used {batchPartsPerMesh} parts per mesh; the profile remains at " +
                $"{authoredFragmentCount}." +
                (hiddenOwners > 0
                    ? $" Hid {hiddenOwners} fully baked source hierarchy root(s)."
                    : string.Empty) +
                (shellSources > 0
                    ? $" HOLLOW SHELL at {shellThickness * 100f:0.##} cm was applied to " +
                      $"{shellSources} flat mesh(es); everything with modelled depth kept " +
                      "its authored geometry, and the profile keeps its Volume Mode."
                    : string.Empty);
            ShowStatus(summary, 12d);
            SceneView.lastActiveSceneView?.ShowNotification(new GUIContent(summary));
        }

        /// <summary>
        /// Mirrors the active profile into the Start page's own fields. Those fields drive
        /// the in-memory profile used before anything is baked, so they have to follow the
        /// asset whenever it is loaded, reset or replaced — otherwise the page would keep
        /// showing values the profile no longer holds.
        /// </summary>
        void SyncQuickFieldsFromProfile()
        {
            if (m_profile == null) return;
            m_quickSolidify = m_profile.Solidify;
            m_quickSolidifyThickness = m_profile.SolidifyThickness;
            m_quickActivateAllOnFirstBreak = m_profile.ActivateAllOnFirstBreak;
            m_quickColliderStrategy = m_profile.Colliders;
            m_quickRuntimeFragmentSplitting = m_profile.RuntimeFragmentSplitting;
            m_quickRuntimeFinalIteration = m_profile.RuntimeSplitMaxDepth;
            m_quickDespawnDebris = m_profile.DespawnDebris;
            m_quickDespawnDebrisAfter = m_profile.DespawnDebrisAfter;
            m_quickUseDefaultHitParticles = m_profile.UseDefaultHitParticles;
            m_quickUseDefaultHitSound = m_profile.UseDefaultHitSound;
            m_quickPlayHitEffectsOnEveryHit = m_profile.PlayHitEffectsOnEveryHit;
            m_quickHitParticlePrefab = m_profile.HitParticlePrefab;
            m_quickHitSound = m_profile.HitSound;
            m_quickDamageTrigger = m_profile.DamageTrigger;
        }

        static bool IsAutoQuickProfile(DestructionProfile profile)
        {
            if (profile == null) return false;
            string path = AssetDatabase.GetAssetPath(profile).Replace('\\', '/');
            return path.StartsWith(BakePipeline.QuickProfileFolder + "/",
                       System.StringComparison.OrdinalIgnoreCase) ||
                   path.StartsWith(BakePipeline.GeneratedRootFolder + "/QuickProfiles/",
                       System.StringComparison.OrdinalIgnoreCase);
        }

        internal static string BuiltInProfilePath(FracturePattern pattern)
        {
            return $"{BakePipeline.QuickProfileFolder}/QD_Profile_{pattern}.asset";
        }

        static bool IsInMemoryProfile(DestructionProfile profile)
        {
            if (profile == null) return false;
            return string.IsNullOrEmpty(AssetDatabase.GetAssetPath(profile));
        }

        void EnsureSelectedProfile()
        {
            // An object that already owns a profile asset is the authority on which profile
            // the window edits. Otherwise the selected material tile resolves immediately to
            // its one shared built-in asset; Bake must never invent a profile per object.
            // Preserve a scratch profile serialized by an already-open pre-fix window as a
            // real custom asset once, so a domain reload neither loses its edits nor shows the
            // obsolete overwrite-shared-profile dialog on the next Bake.
            if (IsInMemoryProfile(m_profile) && m_source != null)
            {
                SaveInMemoryProfileToAsset();
                return;
            }
            if (m_profile == null && m_source != null)
            {
                DestructibleRoot owner = m_source.GetComponentInParent<DestructibleRoot>(true);
                if (owner != null && owner.Profile != null)
                    m_profile = owner.Profile;
            }
            if (m_profile == null)
            {
                CreateOrLoadQuickProfile();
            }
        }

        void UpdateInMemoryProfileName()
        {
            if (m_profile != null && IsInMemoryProfile(m_profile))
            {
                string presetName = PresetLabel(m_quickPreset);
                string sourceName = m_source != null ? m_source.name : "Destructible";
                m_profile.name = $"{sourceName}_{presetName} (In-Memory)";
            }
        }

        /// <summary>
        /// Promotes a scratch profile left open by an older window version. New windows use
        /// shared built-in assets immediately and do not enter this path.
        /// </summary>
        internal bool SaveInMemoryProfileToAsset()
        {
            if (!IsInMemoryProfile(m_profile)) return true;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                QueueSelectedProfileInitialization();
                return false;
            }

            BakePipeline.EnsureQuickProfileRoot();
            EnsureAssetFolder(BakePipeline.QuickProfileFolder);
            string path = BuiltInProfilePath(
                m_profile != null ? m_profile.Pattern : m_quickPreset);
            string assetName = System.IO.Path.GetFileNameWithoutExtension(path);

            var existing = AssetDatabase.LoadAssetAtPath<DestructionProfile>(path);
            // An old in-memory profile may contain object-local geometry choices made before
            // built-in tiles resolved directly to shared assets. Preserve that work as a real
            // custom profile instead of asking to overwrite the shared material default.
            if (existing != null && existing != m_profile)
            {
                EnsureAssetFolder(ProfileFolder);
                string sourceName = m_source != null ? m_source.name : "Destructible";
                string intendedPath =
                    $"{ProfileFolder}/{BakePipeline.MakeSafeFileName(sourceName)}_" +
                    $"{PresetLabel(m_profile.Pattern)}_Profile.asset";
                // A successful CreateAsset can trigger a domain reload before its object is
                // loadable. If the exact migration asset is already imported, it is the
                // persistent version of this legacy scratch profile and must be reused. If
                // only the file is visible, leave it to the current import and retry later;
                // calling CreateAsset over that on-disk path produces UnityException.
                DestructionProfile migrated =
                    AssetDatabase.LoadAssetAtPath<DestructionProfile>(intendedPath);
                if (migrated == null && AssetFileExists(intendedPath))
                {
                    QueueSelectedProfileInitialization();
                    return false;
                }
                path = migrated != null
                    ? intendedPath
                    : AssetDatabase.GenerateUniqueAssetPath(intendedPath);
                assetName = System.IO.Path.GetFileNameWithoutExtension(path);
                existing = migrated;
            }

            if (existing != null)
            {
                EditorUtility.CopySerialized(m_profile, existing);
                existing.name = assetName;
                m_profile = existing;
            }
            else
            {
                m_profile.name = assetName;
                AssetDatabase.CreateAsset(m_profile, path);
            }
            EditorUtility.SetDirty(m_profile);
            AssetDatabase.SaveAssets();

            // Switching an existing prefab instance from a named profile to a built-in
            // preset temporarily writes Profile=None on that instance. Overwriting the
            // generated prefab does not remove the scene override, so the baked object
            // would ignore ApplyDamage/collisions and never dispatch hit effects. As soon
            // as the transient profile becomes a real asset, hand that persistent asset
            // back to the scene root explicitly.
            DestructibleRoot target = m_contextRoot;
            if (target == null && m_source != null)
                target = m_source.GetComponentInParent<DestructibleRoot>(true);
            if (target != null && target.Profile != m_profile)
            {
                Undo.RecordObject(target, "Assign Quick Destruction Profile");
                target.Profile = m_profile;
                if (PrefabUtility.IsPartOfPrefabInstance(target))
                    PrefabUtility.RecordPrefabInstancePropertyModifications(target);
                EditorUtility.SetDirty(target);
                m_contextRoot = target;
            }
            return true;
        }

        static bool AssetFileExists(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return false;
            string projectRoot = System.IO.Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrEmpty(projectRoot)) return false;
            string relative = assetPath.Replace('/', System.IO.Path.DirectorySeparatorChar);
            return System.IO.File.Exists(System.IO.Path.Combine(projectRoot, relative));
        }

        /// <summary>
        /// Destructibles that would change behaviour if this profile were rewritten: the
        /// open scenes plus the generated prefabs, which are where a bake stores the
        /// reference. Used only to decide whether an overwrite needs confirming.
        /// </summary>
        static int CountProfileUsers(DestructionProfile profile,
            out int sceneObjects, out int generatedPrefabs)
        {
            sceneObjects = 0;
            generatedPrefabs = 0;
            if (profile == null) return 0;

            DestructibleRoot[] sceneRoots =
                Resources.FindObjectsOfTypeAll<DestructibleRoot>();
            for (int i = 0; i < sceneRoots.Length; i++)
            {
                DestructibleRoot root = sceneRoots[i];
                if (root == null || EditorUtility.IsPersistent(root)) continue;
                if (!root.gameObject.scene.IsValid()) continue;
                if (root.Profile == profile) sceneObjects++;
            }

            if (AssetDatabase.IsValidFolder(BakePipeline.GeneratedRootFolder))
            {
                foreach (string guid in AssetDatabase.FindAssets(
                             "t:GameObject", new[] { BakePipeline.GeneratedRootFolder }))
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                        AssetDatabase.GUIDToAssetPath(guid));
                    if (prefab == null) continue;
                    var root = prefab.GetComponent<DestructibleRoot>();
                    if (root != null && root.Profile == profile) generatedPrefabs++;
                }
            }
            // Reported separately, never summed: a placed instance and the prefab it came
            // from are one destructible to the user, and adding them would overstate how
            // much an overwrite reaches.
            return Mathf.Max(sceneObjects, generatedPrefabs);
        }

        void SelectSharedPresetProfile(FracturePattern pattern, bool assignToSource)
        {
            m_quickPreset = pattern;
            CreateOrLoadQuickProfile(false);
            if (assignToSource)
                ApplyProfileToSource(m_profile);
        }

        void CreateOrLoadQuickProfile(bool preferAssignedProfile = true)
        {
            // A rebaked destructible already owns the profile it was baked with.
            // Falling through would create "<X>_Destructible_QuickProfile" with bare
            // defaults and silently rebind the object to that duplicate asset.
            if (preferAssignedProfile && m_profile == null)
            {
                DestructibleRoot bakedRoot = m_source != null
                    ? m_source.GetComponentInParent<DestructibleRoot>(true)
                    : null;
                if (bakedRoot == null) bakedRoot = m_contextRoot;
                if (bakedRoot != null && bakedRoot.Profile != null)
                {
                    m_contextRoot = bakedRoot;
                    m_profile = bakedRoot.Profile;
                    m_quickPreset = m_profile.Pattern;
                    if (m_profile.Pattern == FracturePattern.BrickCells)
                        m_quickBrickCellSize = ClampBrickCellSize(m_profile.BrickCellSize);
                    return;
                }
            }

            BakePipeline.EnsureQuickProfileRoot();
            const string folder = BakePipeline.QuickProfileFolder;
            EnsureAssetFolder(folder);
            string path = BuiltInProfilePath(m_quickPreset);
            string expectedName = System.IO.Path.GetFileNameWithoutExtension(path);
            m_profile = AssetDatabase.LoadAssetAtPath<DestructionProfile>(path);
            bool created = m_profile == null;
            if (m_profile == null)
            {
                m_profile = CreateInstance<DestructionProfile>();
                m_profile.name = expectedName;
                ResetProfileToPresetDefaults(m_profile, m_quickPreset);
                AssetDatabase.CreateAsset(m_profile, path);
            }
            else if (m_profile.name != expectedName)
            {
                m_profile.name = expectedName;
                EditorUtility.SetDirty(m_profile);
            }

            if (!created && m_profile.Pattern != m_quickPreset)
            {
                // Older builds changed the Pattern field on whichever Quick Profile happened
                // to be active instead of switching assets. The path is the built-in profile's
                // identity, so repair that stale mismatch once when the tile is selected.
                Undo.RecordObject(m_profile, "Repair Built-In Destruction Profile");
                ResetProfileToPresetDefaults(m_profile, m_quickPreset);
                EditorUtility.SetDirty(m_profile);
                AssetDatabase.SaveAssets();
            }

            SyncQuickFieldsFromProfile();
        }

        internal static void ResetProfileToPresetDefaults(
            DestructionProfile profile, FracturePattern pattern)
        {
            if (profile == null) return;
            string profileName = profile.name;
            var defaults = CreateInstance<DestructionProfile>();
            defaults.ApplyPreset(pattern);
            defaults.InteriorUseSourceMaterial = true;
            defaults.InteriorMaterial = null;
            EditorUtility.CopySerialized(defaults, profile);
            profile.name = profileName;
            DestroyImmediate(defaults);
        }

        static void EnsureInteriorMaterialAssigned(DestructionProfile profile, bool recordUndo)
        {
            // Profiles that reuse the source material on cut faces need no interior asset.
            if (profile == null || profile.InteriorUseSourceMaterial ||
                profile.InteriorMaterial != null) return;
            if (recordUndo)
                Undo.RecordObject(profile, "Assign Default Interior Material");
            profile.InteriorMaterial =
                PresetMaterialLibrary.GetInteriorMaterial(profile.Pattern);
            EditorUtility.SetDirty(profile);
        }

        static Vector3 ClampBrickCellSize(Vector3 value)
        {
            return new Vector3(
                Mathf.Max(0.01f, value.x),
                Mathf.Max(0.01f, value.y),
                Mathf.Max(0.01f, value.z));
        }

        static void EnsureAssetFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            string parent = path.Substring(0, slash);
            string leaf = path.Substring(slash + 1);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureAssetFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        // ------------------------------------------------------------------ fracture

        bool EnsureProfileUI()
        {
            QDUI.BeginCard();
            QDUI.Header("Destruction Profile");
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            m_profile = (DestructionProfile)EditorGUILayout.ObjectField(
                m_profile, typeof(DestructionProfile), false);
            bool profileChanged = EditorGUI.EndChangeCheck();
            bool createdProfile = false;
            if (GUILayout.Button("New Profile", QDUI.GhostBtn, GUILayout.Width(96f)))
            {
                DestructionProfile previous = m_profile;
                CreateProfileAsset();
                createdProfile = m_profile != previous;
            }
            EditorGUILayout.EndHorizontal();

            if (profileChanged)
            {
                if (m_profile == null)
                {
                    SelectSharedPresetProfile(m_quickPreset, true);
                }
                else
                {
                    m_quickPreset = m_profile.Pattern;
                    EnsureInteriorMaterialAssigned(m_profile, true);
                    ApplyProfileToSource(m_profile);
                }
            }
            QDUI.EndCard();
            if (profileChanged || createdProfile)
                GUIUtility.ExitGUI();
            return m_profile != null;
        }

        /// <summary>
        /// Where a profile the author names goes. Deliberately NOT the Generated folder:
        /// named profiles are deliberately kept separate from both generated bake output
        /// and automatic Quick Profiles, so they remain visible as reusable tiles.
        /// </summary>
        internal const string ProfileFolder = "Assets/QuickDestruction/Profiles";

        void CreateProfileAsset()
        {
            string defaultName = m_source != null
                ? $"{m_source.name}_Profile" : "DestructionProfile";
            // A save panel made the author pick a folder every time, when there is only one
            // sensible answer: the folder the other profiles already live in. Naming is the
            // only real decision, so that is all this asks for.
            ProfileNamePrompt.Show(defaultName, ProfileFolder, CreateProfileAssetNamed);
        }

        void CreateProfileAssetNamed(string profileName)
        {
            if (string.IsNullOrWhiteSpace(profileName)) return;
            EnsureAssetFolder(ProfileFolder);
            string path = AssetDatabase.GenerateUniqueAssetPath(
                $"{ProfileFolder}/{BakePipeline.MakeSafeFileName(profileName.Trim())}.asset");
            var profile = CreateInstance<DestructionProfile>();
            if (m_profile != null)
            {
                // "From Selected" means the complete workspace, including the author's
                // shape, physics, effects and lifecycle edits, rather than a fresh preset.
                EditorUtility.CopySerialized(m_profile, profile);
            }
            else
            {
                profile.ApplyPreset(m_quickPreset);
                profile.UseDefaultHitParticles = true;
                profile.UseDefaultHitSound = true;
                profile.PlayHitEffectsOnEveryHit = true;
                profile.InteriorUseSourceMaterial = true;
                profile.InteriorMaterial = null;
            }
            profile.name = System.IO.Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(profile, path);
            AssetDatabase.SaveAssets();
            m_profile = profile;
            m_quickPreset = profile.Pattern;
            SyncQuickFieldsFromProfile();

            // Closing the modal focuses this window and schedules a selection refresh.
            // Consume that stale request now; otherwise it can replace the newly created
            // asset with a new in-memory profile on the next Layout pass.
            m_selectionRefreshPending = false;
            m_ignoreSelectionUntil = EditorApplication.timeSinceStartup + 0.5d;

            // Assign it to the object this was created for. Selection sync re-reads the
            // profile from the DestructibleRoot on every refresh, so without this the new
            // profile was replaced by the baked one a few frames later — the tile appeared
            // and then vanished. The same handover MakeLocalProfileCopy already performs.
            DestructibleRoot target = m_contextRoot;
            if (target == null && m_source != null)
                target = m_source.GetComponentInParent<DestructibleRoot>(true);
            if (target != null)
            {
                Undo.RecordObject(target, "Assign Destruction Profile");
                target.Profile = profile;
                if (PrefabUtility.IsPartOfPrefabInstance(target))
                    PrefabUtility.RecordPrefabInstancePropertyModifications(target);
                EditorUtility.SetDirty(target);
                m_contextRoot = target;
            }

            RefreshProfileLibrary(true);
            PatternHintOverlay.Show(GetCurrentFractureSource(), profile, profile.Pattern);
            EditorUtility.SetDirty(this);
            ShowStatus(target != null
                ? $"Profile '{profile.name}' created and assigned to {target.name}."
                : $"Profile '{profile.name}' created in {ProfileFolder}. Bake & Setup now uses it.");
            Repaint();
        }

        void DrawFractureTab()
        {
            m_fracturePreview ??= new FracturePreviewSession();
            if (!DrawFractureEssentialsCard()) return;
            var p = m_profile;

            if (QDUI.BeginFoldCard("Fracture Settings — Full Control", false,
                    PresetLabel(p.Pattern).ToUpperInvariant(), QDUI.Blue))
                DrawFractureSettingsBody(p);
            QDUI.EndFoldCard();

            if (QDUI.BeginFoldCard("Secondary Breakup", false,
                    p.SecondaryBreakup == SecondaryBreakupLevel.Off
                        ? "OFF"
                        : p.SecondaryBreakup.ToString().ToUpperInvariant(),
                    p.SecondaryBreakup == SecondaryBreakupLevel.Off
                        ? QDUI.TextDim
                        : QDUI.Amber))
                DrawSecondaryBreakupBody(p);
            QDUI.EndFoldCard();

            if (QDUI.BeginFoldCard("Interior Surface", false,
                    p.InteriorUseSourceMaterial ? "SOURCE MAT"
                    : p.InteriorMaterial != null ? "ASSIGNED" : "NONE",
                    p.InteriorUseSourceMaterial || p.InteriorMaterial != null
                        ? QDUI.Green
                        : QDUI.Amber))
                DrawInteriorSurfaceBody(p);
            QDUI.EndFoldCard();

            if (QDUI.BeginFoldCard("Edit-Mode Preview — Options", false,
                    IsFracturePreviewActive() ? "ACTIVE" : "OFF",
                    IsFracturePreviewActive() ? QDUI.Blue : QDUI.TextDim))
                DrawFracturePreviewBody();
            QDUI.EndFoldCard();
        }

        /// <summary>The simple-tool surface of the Fracture workspace: source, profile,
        /// preset chips and the key size parameter directly next to Preview and Bake.
        /// Everything else lives in the fold-out cards below.</summary>
        bool DrawFractureEssentialsCard()
        {
            QDUI.BeginCard();
            QDUI.Header("Fracture");

            EditorGUILayout.BeginHorizontal();
            QDUI.DrawFieldLabel("Source", GUILayout.Width(QDUI.LabelWidth));
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.ObjectField(m_source, typeof(GameObject), true);
            if (m_source != null) DrawSourceStatus(m_source);
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(3f);

            EditorGUILayout.BeginHorizontal();
            QDUI.DrawFieldLabel("Profile", GUILayout.Width(QDUI.LabelWidth));
            EditorGUI.BeginChangeCheck();
            m_profile = (DestructionProfile)EditorGUILayout.ObjectField(
                m_profile, typeof(DestructionProfile), false);
            bool profileChanged = EditorGUI.EndChangeCheck();
            bool createdProfile = false;
            if (GUILayout.Button("New", QDUI.GhostBtn, GUILayout.Width(48f)))
            {
                DestructionProfile previous = m_profile;
                CreateProfileAsset();
                createdProfile = m_profile != previous;
            }
            EditorGUILayout.EndHorizontal();
            if (profileChanged)
            {
                if (m_profile == null)
                {
                    SelectSharedPresetProfile(m_quickPreset, true);
                }
                else
                {
                    m_quickPreset = m_profile.Pattern;
                    EnsureInteriorMaterialAssigned(m_profile, true);
                    ApplyProfileToSource(m_profile);
                }
            }

            var p = m_profile;
            DrawSharedProfileBanner(p);
            GUILayout.Space(5f);
            QDUI.SubHeaderLabel("PATTERN");
            GUILayout.Label(
                "Changes the shape of the cuts only. Physics, strength and lifecycle keep " +
                "this profile's settings — use a preset on the Start page to configure all " +
                "of them together.", QDUI.HelpText);
            EditorGUILayout.BeginHorizontal();
            DrawPresetChip(p, FracturePattern.UniformConcrete, "CONCRETE");
            DrawPresetChip(p, FracturePattern.RadialGlass, "GLASS");
            DrawPresetChip(p, FracturePattern.DirectionalWood, "WOOD");
            DrawPresetChip(p, FracturePattern.BrickCells, "BRICK");
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            DrawPresetChip(p, FracturePattern.LayeredSlabs, "SLABS");
            DrawPresetChip(p, FracturePattern.ColumnarPillars, "PILLARS");
            DrawPresetChip(p, FracturePattern.ImpactShatter, "IMPACT");
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            GUILayout.Label(GetPatternDescription(p.Pattern), QDUI.HelpText);
            GUILayout.Space(4f);

            // Only the one or two parameters that define the result the most; the full
            // set stays in Fracture Settings - Full Control below.
            EditorGUI.BeginChangeCheck();
            Undo.RecordObject(p, "Edit Destruction Profile");
            switch (p.Pattern)
            {
                case FracturePattern.UniformConcrete:
                    p.FragmentCount = QDUI.IntSliderRow(
                        "Max Parts", p.FragmentCount, 2,
                        PrimaryFragmentAuthoringLimit(p));
                    p.FragmentShapeIrregularity = QDUI.SliderRow(
                        "Fragment Shape Irregularity",
                        p.FragmentShapeIrregularity, 0f, 1f);
                    break;
                case FracturePattern.RadialGlass:
                    p.RadialSpokes = QDUI.IntSliderRow("Spokes", p.RadialSpokes, 3, 24);
                    p.RadialRings = QDUI.IntSliderRow("Rings", p.RadialRings, 1, 8);
                    p.GlassNormalAxis = AxisModeRow("Pane Normal Axis", p.GlassNormalAxis);
                    break;
                case FracturePattern.DirectionalWood:
                    p.FragmentCount = QDUI.IntSliderRow(
                        "Max Parts", p.FragmentCount, 2,
                        PrimaryFragmentAuthoringLimit(p));
                    DrawDirectionalBiasRows(p, "Grain / Length Axis");
                    break;
                case FracturePattern.BrickCells when p.BrickUseCellSize:
                    EditorGUILayout.BeginHorizontal();
                    QDUI.DrawFieldLabel("Cell Size  X / Y / Z",
                        GUILayout.Width(QDUI.LabelWidth));
                    p.BrickCellSize = EditorGUILayout.Vector3Field(
                        GUIContent.none, p.BrickCellSize);
                    EditorGUILayout.EndHorizontal();
                    p.BrickCellSize = ClampBrickCellSize(p.BrickCellSize);
                    GUILayout.Space(3f);
                    break;
                case FracturePattern.LayeredSlabs:
                    p.SlabNormalAxis = AxisModeRow("Layer Normal Axis", p.SlabNormalAxis);
                    p.SlabLayers = QDUI.IntSliderRow("Layer Count", p.SlabLayers, 2, 16);
                    p.FragmentCount = QDUI.IntSliderRow(
                        "Max Parts", p.FragmentCount, 2,
                        PrimaryFragmentAuthoringLimit(p));
                    DrawDirectionalBiasRows(p, "Plate Length Axis");
                    break;
                case FracturePattern.ColumnarPillars:
                    p.FragmentCount = QDUI.IntSliderRow(
                        "Max Parts", p.FragmentCount, 2,
                        PrimaryFragmentAuthoringLimit(p));
                    p.PillarAxis = AxisModeRow("Pillar Length Axis", p.PillarAxis);
                    GUILayout.Label(
                        "Auto uses each disconnected element's own longest local axis. Use Y, X or Z " +
                        "only when every pillar or beam should be forced into one shared direction.",
                        QDUI.HelpText);
                    break;
                case FracturePattern.ImpactShatter:
                    p.FragmentCount = QDUI.IntSliderRow(
                        "Max Parts", p.FragmentCount, 2,
                        PrimaryFragmentAuthoringLimit(p));
                    p.ImpactFocus = QDUI.SliderRow("Impact Focus", p.ImpactFocus, 0f, 1f);
                    break;
                default:
                    p.FragmentCount = QDUI.IntSliderRow(
                        "Max Parts", p.FragmentCount, 2,
                        PrimaryFragmentAuthoringLimit(p));
                    break;
            }
            if (EditorGUI.EndChangeCheck())
            {
                EditorUtility.SetDirty(p);
                ScheduleFracturePreview();
            }

            GUILayout.Space(6f);
            bool canPreview = !EditorApplication.isPlaying && m_source != null &&
                              (m_report == null ||
                               m_report.Status != AnalysisSeverity.Blocked);
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(!m_layoutSolving && !canPreview))
            {
                string quickPreviewLabel = m_layoutSolving
                    ? "CANCEL  " + Mathf.RoundToInt(
                          m_layoutSolveProgress * 100f) + "%"
                    : IsFracturePreviewActive() ? "REFRESH PREVIEW" : "PREVIEW";
                if (GUILayout.Button(quickPreviewLabel,
                        QDUI.GhostBtn, GUILayout.Width(140f)))
                {
                    if (m_layoutSolving)
                        CancelFracturePreviewSolve();
                    else
                        GenerateFracturePreview(true);
                }
            }
            using (new EditorGUI.DisabledScope(!CanBake()))
            {
                if (GUILayout.Button("BAKE  &  SETUP", QDUI.AccentBtn))
                {
                    RunBake();
                    GUIUtility.ExitGUI();
                }
            }
            EditorGUILayout.EndHorizontal();
            GUILayout.Label(
                "Preview runs the real solver in memory only; Bake & Setup writes the prefab. " +
                "Fine-tuning lives in the fold-out sections below.",
                QDUI.HelpText);
            QDUI.EndCard();
            if (profileChanged || createdProfile) GUIUtility.ExitGUI();
            return true;
        }

        void DrawFractureSettingsBody(DestructionProfile p)
        {
            EditorGUI.BeginChangeCheck();
            Undo.RecordObject(p, "Edit Destruction Profile");

            FracturePattern previousPattern = p.Pattern;
            FracturePattern selectedPattern = (FracturePattern)QDUI.PopupRow(
                "Pattern", (int)p.Pattern, PatternNames);
            if (selectedPattern != previousPattern)
            {
                if (IsAutoQuickProfile(p))
                    SelectSharedPresetProfile(selectedPattern, true);
                else
                {
                    p.Pattern = selectedPattern;
                    EditorUtility.SetDirty(p);
                }
                ScheduleFracturePreview();
                // Each fracture family exposes a different set of controls.
                EditorGUI.EndChangeCheck();
                GUIUtility.ExitGUI();
            }
            GUILayout.Label(
                "Pattern changes the geometry of the cut, not only the physics preset. " +
                "The main size controls stay on the Fracture card above.",
                QDUI.HelpText);

            EditorGUILayout.BeginHorizontal();
            QDUI.DrawFieldLabel("Seed", GUILayout.Width(QDUI.LabelWidth));
            p.Seed = EditorGUILayout.IntField(p.Seed);
            if (GUILayout.Button("Randomize", QDUI.GhostBtn, GUILayout.Width(80f)))
                p.Seed = UnityEngine.Random.Range(1, int.MaxValue);
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(3f);
            GUILayout.Label(
                "The same source, profile settings and Seed generate the same partition. " +
                "Randomize to explore another layout without changing the pattern family.",
                QDUI.HelpText);

            EditorGUILayout.BeginHorizontal();
            QDUI.DrawFieldLabel("Volume Mode", GUILayout.Width(QDUI.LabelWidth));
            bool editedSolidify = p.Solidify;
            float editedThickness = p.SolidifyThickness;
            if (GUILayout.Button("FULL SOLID", !editedSolidify ? QDUI.ChipBtnActive : QDUI.ChipBtn))
                editedSolidify = false;
            if (GUILayout.Button("HOLLOW SHELL", editedSolidify ? QDUI.ChipBtnActive : QDUI.ChipBtn))
                editedSolidify = true;
            EditorGUILayout.EndHorizontal();

            // IMGUI must create the same control tree during Layout and Repaint. A click can
            // change editedSolidify between those events; conditionally omitting this row
            // then shifts every following control and produces "Getting control N's position"
            // plus an Invalid GUILayout state. Keep the row present and only disable it.
            using (new EditorGUI.DisabledScope(!editedSolidify))
                editedThickness = QDUI.SliderRow(
                    "Shell Thickness", editedThickness, 0.005f, 2f, "0.000");
            if (editedSolidify != p.Solidify ||
                !Mathf.Approximately(editedThickness, p.SolidifyThickness))
            {
                DestructionProfile local = EnsureVolumeModeIsObjectLocal(p);
                Undo.RecordObject(local, "Edit Object Volume Mode");
                local.Solidify = editedSolidify;
                local.SolidifyThickness = Mathf.Max(0.001f, editedThickness);
                EditorUtility.SetDirty(local);
                ScheduleFracturePreview();
                EditorGUI.EndChangeCheck();
                GUIUtility.ExitGUI();
            }
            QDUI.HelpFoldout("Fracture.VolumeMode", "How does the volume mode work?",
                "Generates an inward-offset inner wall and closes open boundary rims before fracture. " +
                "Use Hollow Shell for outer-surface-only meshes. Leave it on Full Solid for true " +
                "solids and meshes with an authored inner shell. This is an object-local bake " +
                "setting; shared profiles are copied automatically before it changes. Shell " +
                "Thickness is measured in scene units and automatically accounts for Transform scale.");

            switch (p.Pattern)
            {
                case FracturePattern.UniformConcrete:
                    GUILayout.Space(4f);
                    QDUI.SubHeaderLabel("UNIFORM / CONCRETE");
                    p.FragmentShapeIrregularity = QDUI.SliderRow(
                        "Fragment Shape Irregularity",
                        p.FragmentShapeIrregularity, 0f, 1f);
                    p.ConcreteIrregularity = QDUI.SliderRow(
                        "Interior Stone Scatter", p.ConcreteIrregularity, 0f, 1f);
                    GUILayout.Label(
                        "Fragment Shape Irregularity changes the actual cut-point wander: 0 produces " +
                        "more even, block-like pieces and 1 produces more varied angular silhouettes. " +
                        "It changes preview and baked geometry. Interior Stone Scatter is separate: " +
                        "it adds render-only stones on new concrete surfaces without changing the " +
                        "seed, primary cuts, closed cap topology or collider.",
                        QDUI.HelpText);
                    break;
                case FracturePattern.RadialGlass:
                    GUILayout.Space(4f);
                    QDUI.SubHeaderLabel("RADIAL / GLASS");
                    p.GlassNormalAxis = AxisModeRow("Pane Normal Axis", p.GlassNormalAxis);
                    p.RadialJitter = QDUI.SliderRow("Angular Jitter", p.RadialJitter, 0f, 1f);
                    p.ReleaseUnsupportedGlass = QDUI.ToggleRow(
                        "Release Unsupported Islands", p.ReleaseUnsupportedGlass);
                    GUILayout.Label(
                        "Spokes and Rings sit on the Fracture card above. Angular Jitter removes the " +
                        "perfectly regular wheel appearance. Unsupported Islands uses baked edge " +
                        "connectivity: pieces stay only while a path to the outer pane boundary " +
                        "exists. Rebake legacy glass prefabs once to create this graph.",
                        QDUI.HelpText);
                    break;
                case FracturePattern.DirectionalWood:
                    GUILayout.Space(4f);
                    QDUI.SubHeaderLabel("DIRECTIONAL / WOOD");
                    DrawDirectionalBiasRows(p, "Grain / Length Axis");
                    p.WoodSplitIrregularity = QDUI.SliderRow(
                        "Interior Splinter Scatter", p.WoodSplitIrregularity, 0f, 1f);
                    p.WoodEndSharpness = QDUI.SliderRow(
                        "Splinter End Sharpness", p.WoodEndSharpness, 0f, 1f);
                    GUILayout.Label(
                        "0 cuts mostly across each board's local width; 1 splits mostly along its local " +
                        "length. Interior Splinter Scatter adds sparse, long render-only splinters on " +
                        "new cut faces without changing the seed or primary cut layout. Splinter End " +
                        "Sharpness tilts cross-cuts into more wedge-like ends. Preview and Bake & Setup " +
                        "update this baked visual detail; it never changes the collider geometry.",
                        QDUI.HelpText);
                    break;
                case FracturePattern.BrickCells:
                    GUILayout.Space(4f);
                    QDUI.SubHeaderLabel("BRICK / CELLS");
                    p.BrickUseCellSize = QDUI.ToggleRow(
                        "Use Cell Dimensions", p.BrickUseCellSize);
                    GUILayout.Label(
                        "On uses the explicit brick width, height and depth from the Fracture card " +
                        "above. Off derives a proportional grid from Max Parts.", QDUI.HelpText);
                    var brickFilter = m_source != null
                        ? m_source.GetComponentInChildren<MeshFilter>()
                        : null;
                    if (brickFilter != null && brickFilter.sharedMesh != null)
                    {
                        Vector3Int grid = FractureEngine.GetBrickGrid(
                            brickFilter.sharedMesh.bounds, p);
                        GUILayout.Label(
                            $"Estimated grid: {grid.x} × {grid.y} × {grid.z} = " +
                            $"{grid.x * grid.y * grid.z} cells. Dimensions use source-mesh local units; " +
                            "very small targets are proportionally capped at 250 cells.",
                            QDUI.HelpText);
                    }
                    else
                    {
                        GUILayout.Label(
                            "Select a mesh Source to see the estimated X × Y × Z grid.",
                            QDUI.HelpText);
                    }
                    p.BrickJitter = QDUI.SliderRow(
                        "Cell Irregularity", p.BrickJitter, 0f, 1f);
                    GUILayout.Label(
                        "0 produces a regular grid. Higher values offset cell boundaries for less " +
                        "mechanical masonry and rubble.",
                        QDUI.HelpText);
                    break;
                case FracturePattern.LayeredSlabs:
                    GUILayout.Space(4f);
                    QDUI.SubHeaderLabel("LAYERED / SLABS");
                    p.SlabNormalAxis = AxisModeRow("Layer Normal Axis", p.SlabNormalAxis);
                    p.SlabJitter = QDUI.SliderRow("Layer Irregularity", p.SlabJitter, 0f, 1f);
                    DrawDirectionalBiasRows(p, "Plate Length Axis");
                    GUILayout.Label(
                        "Layer Count and Max Parts sit on the Fracture card above. Sheets are " +
                        "stacked along the thinnest mesh axis, then cross cuts break each sheet into " +
                        "plates. Layer Jitter offsets sheet thickness for a natural, uneven look.",
                        QDUI.HelpText);
                    break;
                case FracturePattern.ColumnarPillars:
                    GUILayout.Space(4f);
                    QDUI.SubHeaderLabel("COLUMNAR / PILLARS");
                    p.PillarAxis = AxisModeRow("Pillar Length Axis", p.PillarAxis);
                    p.ColumnJitter = QDUI.SliderRow("Column Irregularity", p.ColumnJitter, 0f, 1f);
                    GUILayout.Label(
                        "Pillars stay continuous along the selected local axis. Auto chooses the longest " +
                        "axis; choose Y for upright columns. Max Parts controls the cross-section " +
                        "grid density, while Column Irregularity makes it less mechanical.",
                        QDUI.HelpText);
                    break;
                case FracturePattern.ImpactShatter:
                    GUILayout.Space(4f);
                    QDUI.SubHeaderLabel("IMPACT / SHATTER");
                    GUILayout.Label(
                        "Max Parts and Impact Focus sit on the Fracture card above. Randomize " +
                        "the Seed to move the impact point. Impact stays radial and does not use a " +
                        "preferred length axis.",
                        QDUI.HelpText);
                    break;
            }

            if (EditorGUI.EndChangeCheck())
            {
                EditorUtility.SetDirty(p);
                ScheduleFracturePreview();
            }
        }

        void DrawInteriorSurfaceBody(DestructionProfile p)
        {
            EditorGUI.BeginChangeCheck();
            Undo.RecordObject(p, "Edit Destruction Profile");
            bool previousUseSource = p.InteriorUseSourceMaterial;
            p.InteriorUseSourceMaterial = QDUI.ToggleRow(
                "Use Source Material", p.InteriorUseSourceMaterial);
            GUILayout.Label(
                "On: every fragment assigns its cut faces to the source submesh material covering most " +
                "of that piece (for example roof or wall). Off: the Interior Material below is applied.",
                QDUI.HelpText);
            DrawCutSurfaceUVModeRow(p);
            if (!p.InteriorUseSourceMaterial)
            {
                p.InteriorMaterial = (Material)QDUI.ObjectRow(
                    "Interior Material", p.InteriorMaterial, typeof(Material));
            }
            bool interiorChanged = EditorGUI.EndChangeCheck();
            if (interiorChanged)
            {
                EditorUtility.SetDirty(p);
                MarkBakeAppearanceDirty();
            }
            if (p.InteriorUseSourceMaterial)
            {
                QDUI.InfoBox(
                    "Cut faces reuse each fragment's dominant source material, so roof, wall and trim " +
                    "pieces keep sensible matching cuts. Turn this off and assign a dedicated Interior Material for " +
                    "distinct core art (raw concrete, splintered wood, glass edges).");
            }
            else if (p.InteriorMaterial == null)
            {
                QDUI.WarnBox("No cut-surface material is assigned to this legacy profile.");
                if (GUILayout.Button("ASSIGN PRESET INTERIOR MATERIAL", QDUI.GhostBtn,
                        GUILayout.Width(230f)))
                {
                    EnsureInteriorMaterialAssigned(p, true);
                    MarkBakeAppearanceDirty();
                }
            }
            else
            {
                GUILayout.Label(
                    "A preset cut-surface material is assigned and will be visible on newly exposed faces. " +
                    "Replace it here with final concrete, wood grain, brick or glass-edge art at any time.",
                    QDUI.HelpText);
            }
            GUILayout.Label(
                "UV Layout affects only newly exposed cut faces; exterior UVs are never changed. " +
                "Tiling is shown beside the layout and is used only by Projected Tiling.", QDUI.HelpText);
            if (interiorChanged && previousUseSource != p.InteriorUseSourceMaterial)
                GUIUtility.ExitGUI();
        }

        void DrawSecondaryBreakupBody(DestructionProfile p)
        {
            EditorGUILayout.BeginHorizontal();
            bool isRuntime = p.RuntimeFragmentSplitting;
            if (QDUI.Chip("PRE-BAKED (EDITOR)", !isRuntime))
            {
                if (isRuntime)
                {
                    Undo.RecordObject(p, "Switch to Pre-Baked Secondary");
                    p.RuntimeFragmentSplitting = false;
                    m_quickRuntimeFragmentSplitting = false;
                    if (p.SecondaryBreakup == SecondaryBreakupLevel.Off)
                        p.ApplySecondaryPreset(SecondaryBreakupLevel.Low);
                    EditorUtility.SetDirty(p);
                }
            }
            bool selectDynamic = false;
            using (new EditorGUI.DisabledScope(
                       p.Pattern == FracturePattern.RadialGlass))
                selectDynamic = QDUI.Chip("DYNAMIC (PLAY MODE)", isRuntime);
            if (selectDynamic)
            {
                if (!isRuntime)
                {
                    Undo.RecordObject(p, "Switch to Dynamic Runtime Slicing");
                    p.RuntimeFragmentSplitting = true;
                    m_quickRuntimeFragmentSplitting = true;
                    p.SecondaryBreakup = SecondaryBreakupLevel.Off;
                    p.RuntimeSplitPieces = 2;
                    p.RuntimeSplitMaxDepth = 2;
                    p.RuntimeSplitMaxActivePieces = 32;
                    p.MaxSplitsPerFrame = 1;
                    p.RuntimeSplitMaxSourceTriangles = 5000;
                    p.RuntimeSplitMaxQueuedRequests = 16;
                    p.RuntimeSplitCollisionCooldown = 0.2f;
                    p.RuntimeSplitConvexColliderDepth = 1;
                    p.RuntimeSplitPiecesDisappear = true;
                    p.RuntimeSplitDisappearAfter = 6f;
                    EditorUtility.SetDirty(p);
                }
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(6f);

            if (p.RuntimeFragmentSplitting)
            {
                QDUI.InfoBox(
                    "Dynamic Runtime Re-Fracture is ENABLED. Normal baked release has priority. A later " +
                    "hit cuts only a loose fragment and returns only real geometry clipped from that " +
                    "fragment. Each child uses a lightweight inset tetrahedron collider. No substitute " +
                    "box or compressed-copy geometry is created when a true cut fails.");
                GUILayout.Space(4f);
                EditorGUI.BeginChangeCheck();
                Undo.RecordObject(p, "Edit Dynamic Runtime Re-Fracture");
                p.RuntimeSplitPieces = QDUI.IntSliderRow(
                    "Pieces Per Split", p.RuntimeSplitPieces, 2, 4);
                p.RuntimeSplitMaxDepth = QDUI.IntSliderRow(
                    "Max Split Iterations", p.RuntimeSplitMaxDepth, 1, 4);
                p.RuntimeSplitMinVolume = QDUI.SliderRow(
                    "Minimum Fragment Volume", p.RuntimeSplitMinVolume, 0.0001f, 0.1f, "0.0000");
                p.RuntimeSplitSeparationForce = QDUI.SliderRow(
                    "Break Apart Force", p.RuntimeSplitSeparationForce, 0f, 20f, "0.0");
                if (EditorGUI.EndChangeCheck())
                    EditorUtility.SetDirty(p);
                return;
            }

            GUILayout.Label(
                "Optionally pre-bakes smaller child pieces inside selected large primary fragments. " +
                "At runtime a strong impact, or a qualifying first landing that also raises dust, " +
                "swaps only that parent fragment to its prepared children.",
                QDUI.HelpText);
            GUILayout.Space(4f);

            EditorGUILayout.BeginHorizontal();
            DrawSecondaryPresetChip(p, SecondaryBreakupLevel.Off, "OFF");
            DrawSecondaryPresetChip(p, SecondaryBreakupLevel.Low, "LOW");
            DrawSecondaryPresetChip(p, SecondaryBreakupLevel.Medium, "MEDIUM");
            DrawSecondaryPresetChip(p, SecondaryBreakupLevel.High, "HIGH");
            if (p.SecondaryBreakup == SecondaryBreakupLevel.Custom)
                QDUI.Chip("CUSTOM", true);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            if (p.SecondaryBreakup == SecondaryBreakupLevel.Off)
            {
                QDUI.InfoBox(
                    "Off keeps one fracture generation and has no additional prefab or bake cost.");
                return;
            }

            string infoText = p.SecondaryBreakup switch
            {
                SecondaryBreakupLevel.Low => "LOW preset: splits ~30% of the largest primary fragments into 3 child pieces each.",
                SecondaryBreakupLevel.Medium => "MEDIUM preset: splits ~50% of the largest primary fragments into 4 child pieces each.",
                SecondaryBreakupLevel.High => "HIGH preset: splits ~75% of the largest primary fragments into 5 child pieces each.",
                _ => $"Up to {p.SecondaryEligibleFragments} large primary fragments × {p.SecondaryPiecesPerFragment} children = approximately {p.SecondaryEligibleFragments * p.SecondaryPiecesPerFragment} pre-baked level-two pieces."
            };
            QDUI.InfoBox($"{infoText} Runtime active cap: {p.MaxActiveSecondaryBodies}.");

            EditorGUI.BeginChangeCheck();
            Undo.RecordObject(p, "Edit Secondary Breakup");
            p.SecondaryStrength = QDUI.SliderRow(
                "Secondary Strength", p.SecondaryStrength, 1f, 250f, "0");
            GUILayout.Label(
                "Impact damage accumulated by an eligible primary fragment before it splits again.",
                QDUI.HelpText);
            p.SecondaryMinImpactVelocity = QDUI.SliderRow(
                "Minimum Impact (m/s)", p.SecondaryMinImpactVelocity, 0f, 20f);
            GUILayout.Label(
                "Slower contacts are ignored so rolling and settling chunks do not crumble by accident.",
                QDUI.HelpText);
            p.SecondaryImpactGrace = QDUI.SliderRow(
                "Settling Grace (s)", p.SecondaryImpactGrace, 0f, 3f);
            GUILayout.Label(
                "Quiet period after a fragment comes loose. Pieces separate touching each other, " +
                "so without it the collapse itself breaks every pre-baked fragment at once.",
                QDUI.HelpText);

            if (m_advancedSetup)
            {
                p.SecondaryEligibleFragments = QDUI.IntSliderRow(
                    "Eligible Parent Limit", p.SecondaryEligibleFragments, 1, 250);
                p.SecondaryPiecesPerFragment = QDUI.IntSliderRow(
                    "Children Per Parent", p.SecondaryPiecesPerFragment, 2, 12);
                p.SecondaryMinParentVolumeRatio = QDUI.SliderRow(
                    "Minimum Parent Size", p.SecondaryMinParentVolumeRatio, 0f, 3f);
                GUILayout.Label(
                    "Only the largest candidates are used. Minimum Parent Size is measured against " +
                    "average primary-fragment volume; 1 means average size or larger.",
                    QDUI.HelpText);
                p.SecondaryIrregularity = QDUI.SliderRow(
                    "Secondary Irregularity", p.SecondaryIrregularity, 0f, 1f);
                EditorGUILayout.BeginHorizontal();
                QDUI.DrawFieldLabel("Impact Speed → Damage",
                    GUILayout.Width(QDUI.LabelWidth));
                p.SecondaryImpulseToDamage =
                    EditorGUILayout.CurveField(p.SecondaryImpulseToDamage);
                EditorGUILayout.EndHorizontal();
                GUILayout.Space(3f);
                p.SecondaryDamageCooldown = QDUI.SliderRow(
                    "Damage Cooldown (s)", p.SecondaryDamageCooldown, 0f, 2f);
                p.SecondarySeparationForce = QDUI.SliderRow(
                    "Separation Force", p.SecondarySeparationForce, 0f, 50f);
                if (p.EnforceActiveBodyBudget)
                {
                    p.MaxActiveSecondaryBodies = QDUI.IntSliderRow(
                        "Max Active Level-Two", p.MaxActiveSecondaryBodies, 1, 128);
                }
                p.SecondaryInheritVelocity = QDUI.ToggleRow(
                    "Inherit Parent Velocity", p.SecondaryInheritVelocity);
                GUILayout.Label(
                    p.EnforceActiveBodyBudget
                        ? "Visual-only children remain visible without physics. The optional active cap is " +
                          "separate per destructible and still obeys the shared Budget Group cap."
                        : "No active-body cap is enabled: every physical level-two child may activate. " +
                          "Lifecycle cleanup remains available to control long-lived debris.",
                    QDUI.HelpText);
            }

            if (EditorGUI.EndChangeCheck())
            {
                p.SecondaryBreakup = SecondaryBreakupLevel.Custom;
                EditorUtility.SetDirty(p);
                ScheduleFracturePreview();
            }

            GUILayout.Label(
                "The second level is generated during Bake. Existing generated prefabs must be rebaked.",
                QDUI.HelpText);
        }

        void DrawSecondaryPresetChip(DestructionProfile profile,
            SecondaryBreakupLevel level, string label)
        {
            if (!QDUI.Chip(label, profile.SecondaryBreakup == level)) return;
            Undo.RecordObject(profile, "Apply Secondary Breakup Preset");
            profile.ApplySecondaryPreset(level);
            EditorUtility.SetDirty(profile);
            ScheduleFracturePreview();
            ShowStatus(
                level == SecondaryBreakupLevel.Off
                    ? "Secondary breakup disabled."
                    : $"{label} secondary breakup preset applied.");
            GUIUtility.ExitGUI();
        }

        /// <summary>
        /// In the Fracture tab these tiles change the cut pattern and nothing else on a
        /// custom profile. Built-in profiles switch to the matching shared asset instead. The
        /// Start page is where a preset configures fracture, physics, strength and lifecycle
        /// together; doing the same here silently discarded tuning the author had already
        /// done on this profile — switching Wood to Concrete to compare the cut also reset
        /// Health, damage response and cleanup behind their back.
        /// </summary>
        void DrawPresetChip(DestructionProfile p, FracturePattern pattern, string label)
        {
            if (QDUI.PresetChip(pattern, label, p.Pattern == pattern))
            {
                if (IsAutoQuickProfile(p))
                {
                    SelectSharedPresetProfile(pattern, true);
                    PatternHintOverlay.Show(GetCurrentFractureSource(), m_profile, pattern);
                    ScheduleFracturePreview();
                    ShowStatus(
                        $"Shared {label} profile selected. Create a custom profile to keep " +
                        "the current physics and lifecycle values while changing only its pattern.");
                    GUIUtility.ExitGUI();
                    return;
                }
                Undo.RecordObject(p, "Change Fracture Pattern");
                p.Pattern = pattern;
                EditorUtility.SetDirty(p);
                PatternHintOverlay.Show(GetCurrentFractureSource(), p, pattern);
                ScheduleFracturePreview();
                ShowStatus(
                    $"Pattern set to {label}. Only the cut geometry changed; physics, " +
                    "strength and lifecycle keep this profile's own settings.");
                GUIUtility.ExitGUI();
            }
        }

        static string GetPatternDescription(FracturePattern pattern)
        {
            switch (pattern)
            {
                case FracturePattern.RadialGlass:
                    return "Glass creates radial spokes and concentric rings around the pane center. " +
                           "Best for windows, screens, thin stone plates and brittle panels.";
                case FracturePattern.DirectionalWood:
                    return "Wood biases cuts along the mesh's longest axis to form splinters. " +
                           "Best for planks, beams, doors and wooden props with a clear grain direction.";
                case FracturePattern.BrickCells:
                    return "Brick divides the bounds with a jittered 3D grid. Best for masonry, " +
                           "block walls, tiles and stylized cellular rubble.";
                case FracturePattern.LayeredSlabs:
                    return "Slabs cuts parallel sheets along the thinnest axis, then breaks each " +
                           "sheet into plates. Best for slate, plaster, sedimentary rock and floors.";
                case FracturePattern.ColumnarPillars:
                    return "Pillars splits the object into full-length prisms along Auto (the longest " +
                           "local axis) or a chosen X, Y or Z axis. Best for basalt columns, pillars, " +
                           "fence posts and tree trunks.";
                case FracturePattern.ImpactShatter:
                    return "Impact clusters cuts around one hit point: fine debris near the impact, " +
                           "large chunks farther away. Best for shot walls, meteor hits and hero moments.";
                default:
                    return "Concrete recursively splits the largest remaining chunk with irregular " +
                           "planes. Best for rocks, columns, statues, concrete props and general rubble.";
            }
        }

        void DrawFracturePreviewBody()
        {
            m_fracturePreview ??= new FracturePreviewSession();
            bool canPreview = !EditorApplication.isPlaying &&
                              m_source != null && m_profile != null &&
                              (m_report == null ||
                               m_report.Status != AnalysisSeverity.Blocked);
            bool active = IsFracturePreviewActive();
            bool stale = active && IsFracturePreviewStale(
                GetCurrentFractureSource());

            GUILayout.Label(
                "Runs the real fracture solver in memory and displays temporary fragments in Scene " +
                "View. No assets, Colliders or Rigidbodies are created until Bake & Setup.",
                QDUI.HelpText);
            GUILayout.Space(4f);

            EditorGUI.BeginChangeCheck();
            m_autoFracturePreview = QDUI.ToggleRow(
                "Auto Refresh After Changes", m_autoFracturePreview);
            if (EditorGUI.EndChangeCheck() && m_autoFracturePreview)
                ScheduleFracturePreview();
            EditorGUI.BeginChangeCheck();
            m_previewWireframe = QDUI.ToggleRow(
                "Wireframe Overlay", m_previewWireframe);
            if (EditorGUI.EndChangeCheck() && active)
                SetFracturePreviewWireframe(m_previewWireframe);
            GUILayout.Label(
                "Auto Refresh waits briefly after a slider/edit, then rebuilds the in-memory preview. " +
                "Wireframe draws fragment topology over the selected preview surface.",
                QDUI.HelpText);
            GUILayout.Space(4f);

            EditorGUILayout.BeginHorizontal();
            QDUI.StatusPill(active ? "PREVIEW ACTIVE" : "NO PREVIEW",
                active ? QDUI.Blue : QDUI.TextDim, 108f);
            if (active)
            {
                GUILayout.Space(6f);
                QDUI.StatusPill(stale ? "OUT OF DATE" : "CURRENT",
                    stale ? QDUI.Amber : QDUI.Green, 92f);
            }
            if (m_layoutSolving)
            {
                GUILayout.Space(6f);
                QDUI.StatusPill("GENERATING", QDUI.Amber, 92f);
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            if (m_layoutSolving)
            {
                GUILayout.Space(4f);
                Rect solveRect = GUILayoutUtility.GetRect(
                    10f, 16f, GUILayout.ExpandWidth(true));
                EditorGUI.ProgressBar(solveRect,
                    m_layoutSolveProgress,
                    "Generating preview in the background... " +
                    Mathf.RoundToInt(m_layoutSolveProgress * 100f) + "%");
            }

            if (stale)
                QDUI.WarnBox(
                    "The profile or fracture parameters changed. Refresh Preview to see the new result.");

            GUILayout.Space(4f);
            QDUI.SubHeaderLabel("PREVIEW SURFACE");
            EditorGUILayout.BeginHorizontal();
            if (QDUI.Chip("COLORIZED", m_previewMaterialMode ==
                    FracturePreviewMaterialMode.Colorized))
            {
                m_previewMaterialMode = FracturePreviewMaterialMode.Colorized;
                if (active) SetFracturePreviewMaterialMode(m_previewMaterialMode);
            }
            if (QDUI.Chip("SOURCE + CUT", m_previewMaterialMode ==
                    FracturePreviewMaterialMode.SourceAndInterior))
            {
                m_previewMaterialMode = FracturePreviewMaterialMode.SourceAndInterior;
                if (active) SetFracturePreviewMaterialMode(m_previewMaterialMode);
            }
            if (QDUI.Chip("CUT HIGHLIGHT", m_previewMaterialMode ==
                    FracturePreviewMaterialMode.InteriorCuts))
            {
                m_previewMaterialMode = FracturePreviewMaterialMode.InteriorCuts;
                if (active) SetFracturePreviewMaterialMode(m_previewMaterialMode);
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            GUILayout.Label(
                "Colorized separates every fragment. Source + Cut previews the assigned exterior " +
                "and interior materials. Cut Highlight paints only newly generated cut surfaces orange.",
                QDUI.HelpText);

            EditorGUI.BeginChangeCheck();
            m_previewExplode = QDUI.SliderRow(
                "Exploded View", m_previewExplode, 0f, 0.5f);
            if (EditorGUI.EndChangeCheck() && active)
                SetFracturePreviewExplode(m_previewExplode);
            GUILayout.Label(
                "0 keeps the object assembled. Increase the value to inspect fragment boundaries " +
                "and interior surfaces without running physics.",
                QDUI.HelpText);

            GUILayout.Space(5f);
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(!m_layoutSolving && !canPreview))
            {
                string label = m_layoutSolving
                    ? "CANCEL GENERATION"
                    : active ? "REFRESH PREVIEW" : "GENERATE PREVIEW";
                if (GUILayout.Button(label, QDUI.AccentBtn, GUILayout.MinWidth(140f)))
                {
                    if (m_layoutSolving)
                        CancelFracturePreviewSolve();
                    else
                        GenerateFracturePreview(true);
                }
            }
            using (new EditorGUI.DisabledScope(!active))
            {
                if (GUILayout.Button("Clear", QDUI.GhostBtn, GUILayout.Width(58f)))
                    EndFracturePreviews();
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            if (!canPreview)
            {
                QDUI.WarnBox(EditorApplication.isPlaying
                    ? "Exit Play Mode to generate an Edit-Mode fracture preview."
                    : "Assign a valid source and resolve Analyze blockers before previewing.");
            }
            else if (!string.IsNullOrEmpty(m_previewError))
            {
                QDUI.ErrorBox(m_previewError);
            }
            else if (active)
            {
                GUILayout.Space(6f);
                EditorGUILayout.BeginHorizontal();
                int fragmentCount = HasBatchFracturePreview
                    ? BatchPreviewFragmentCount() : m_fracturePreview.FragmentCount;
                int triangleCount = HasBatchFracturePreview
                    ? BatchPreviewTriangleCount() : m_fracturePreview.TotalTriangles;
                int bodyCount = HasBatchFracturePreview
                    ? BatchPreviewBodyCount() : m_fracturePreview.EstimatedBodyCount;
                float mass = HasBatchFracturePreview
                    ? BatchPreviewMass() : m_fracturePreview.EstimatedMass;
                float buildSeconds = HasBatchFracturePreview
                    ? BatchPreviewBuildSeconds() : m_fracturePreview.BuildSeconds;
                int secondaryFragments = HasBatchFracturePreview
                    ? BatchPreviewSecondaryFragments()
                    : m_fracturePreview.EstimatedSecondaryFragmentCount;
                QDUI.StatBlock(fragmentCount.ToString(), "FRAGMENTS");
                QDUI.StatBlock(triangleCount.ToString("n0"), "TRIANGLES");
                QDUI.StatBlock(bodyCount.ToString(), "RIGIDBODIES",
                    QDUI.Blue);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.BeginHorizontal();
                QDUI.StatBlock($"{mass:0.#} kg", "TOTAL MASS");
                QDUI.StatBlock($"{buildSeconds:0.0}s", "PREVIEW TIME");
                EditorGUILayout.EndHorizontal();
                if (secondaryFragments > 0)
                {
                    EditorGUILayout.BeginHorizontal();
                    QDUI.StatBlock(
                        (HasBatchFracturePreview
                            ? BatchPreviewSecondaryParents()
                            : m_fracturePreview.SecondaryEligiblePrimaryCount).ToString(),
                        "LEVEL-TWO PARENTS", QDUI.Amber);
                    QDUI.StatBlock(
                        secondaryFragments.ToString(),
                        "LEVEL-TWO PIECES", QDUI.Amber);
                    QDUI.StatBlock(
                        (HasBatchFracturePreview
                            ? BatchPreviewSecondaryBodies()
                            : m_fracturePreview.EstimatedSecondaryBodyCount).ToString(),
                        "LEVEL-TWO BODIES", QDUI.Blue);
                    EditorGUILayout.EndHorizontal();
                    GUILayout.Label(
                        "Level-two pieces are cut by the same solver and replace their selected " +
                        "parents directly in this preview. Bake uses the same deterministic seeds.",
                        QDUI.HelpText);
                }
                DrawReportMessages(HasBatchFracturePreview
                        ? BatchPreviewWarnings() : m_fracturePreview.Warnings,
                    "Preview.MeshPreparation");
            }
        }

        void GenerateFracturePreview(bool showFailureDialog = false)
        {
            // The solver runs on a background thread so the Editor never blocks inside
            // this window's repaint. ProcessScheduledPreview polls for the result and
            // FinishFracturePreview assembles the scene objects at the next Layout.
            m_previewError = null;
            m_showPreviewFailureDialog = showFailureDialog;
            if (TryStartRootBatchPreview(showFailureDialog)) return;
            if (!TryPrepareWholeBuildingSource(showFailureDialog,
                    out GameObject fractureSource, out string preparationError))
            {
                ReportPreviewFailure(preparationError, showFailureDialog);
                m_showPreviewFailureDialog = false;
                return;
            }
            bool allowUnsafeFractureInput = false;
            if (showFailureDialog)
            {
                m_report = fractureSource != null
                    ? MeshAnalyzer.Analyze(fractureSource)
                    : null;
                if (!TryConfirmUnsafeFractureInput(
                        m_report, m_profile, "PREVIEW",
                        out allowUnsafeFractureInput))
                {
                    m_showPreviewFailureDialog = false;
                    ShowStatus("Preview cancelled. The source and profile were not changed.");
                    return;
                }
            }

            IReadOnlyList<GameObject> visibilitySources = IsWholeBuildingMode &&
                m_combinedSource != null
                ? m_combinedSource.OriginalRoots
                : null;
            // Re-tuning an already baked destructible: its intact renderer stands exactly
            // where the preview fragments appear, so it draws over them. Hiding the
            // fracture source is not enough to express "the preview replaces this object"
            // in Whole Building mode, where the source is a temporary proxy and the
            // visible geometry belongs to the baked result instead.
            if (m_contextRoot != null)
            {
                var hidden = new List<GameObject>();
                if (visibilitySources != null) hidden.AddRange(visibilitySources);
                else if (fractureSource != null) hidden.Add(fractureSource);
                hidden.Add(m_contextRoot.gameObject);
                visibilitySources = hidden;
            }
            if (m_fracturePreview.StartBuild(fractureSource, m_profile,
                    out string error, allowUnsafeFractureInput,
                    visibilitySources, IsWholeBuildingMode))
            {
                m_nextSolveRepaintAt = 0d;
                ShowStatus(
                    "Generating fracture preview in the background - " +
                    "the Editor stays responsive.");
            }
            else
            {
                ReportPreviewFailure(error, showFailureDialog);
                m_showPreviewFailureDialog = false;
            }
        }

        void FinishFracturePreview()
        {
            // Per-preview profiling log; enable only while measuring preview builds.
            const bool verbosePreviewProfiling = false;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            if (m_fracturePreview.FinishBuild(out string error))
            {
                m_previewError = null;
                m_showPreviewFailureDialog = false;
                long finishBuildMs = sw.ElapsedMilliseconds;
                sw.Restart();
                if (m_previewExplode < 0.01f)
                    m_previewExplode = m_profile != null &&
                        m_profile.SecondaryBreakup != SecondaryBreakupLevel.Off
                        ? 0.2f : 0.08f;
                m_fracturePreview.SetPresentation(
                    m_previewMaterialMode, m_previewExplode, m_previewWireframe);
                long setPresentationMs = sw.ElapsedMilliseconds;
#pragma warning disable 0162
                if (verbosePreviewProfiling)
                    UnityEngine.Debug.Log($"[QD Preview Profile] FinishBuild took {finishBuildMs}ms, SetPresentation took {setPresentationMs}ms");
#pragma warning restore 0162

                ShowStatus(
                    $"Preview generated: {m_fracturePreview.FragmentCount} fragments, " +
                    $"{m_fracturePreview.EstimatedBodyCount} estimated rigidbodies " +
                    $"({m_fracturePreview.BuildSeconds:0.0}s).");
            }
            else if (!string.IsNullOrEmpty(error))
            {
                // A null error means the solve was cancelled or superseded by a newer
                // request; the previous preview is intentionally left on screen.
                bool showFailureDialog = m_showPreviewFailureDialog;
                m_showPreviewFailureDialog = false;
                ReportPreviewFailure(error, showFailureDialog);
            }
            else
            {
                m_showPreviewFailureDialog = false;
            }
        }

        void ReportPreviewFailure(string error, bool showDialog)
        {
            m_previewError = string.IsNullOrEmpty(error)
                ? "Preview could not be generated."
                : error;
            ShowStatus(m_previewError);
            if (!showDialog || Application.isBatchMode) return;

            string message = m_previewError;
            EditorApplication.delayCall += () =>
            {
                if (this != null)
                    EditorUtility.DisplayDialog(
                        "Quick Destruction — Preview blocked", message, "OK");
            };
        }

        bool TryConfirmUnsafeFractureInput(AnalysisReport report,
            DestructionProfile profile, string operation,
            out bool allowUnsafeFractureInput)
        {
            allowUnsafeFractureInput = false;
            bool modularBakeWithOpenPanels = IsWholeBuildingMode &&
                string.Equals(operation, "BAKE", System.StringComparison.OrdinalIgnoreCase) &&
                report != null && report.OpenEdges > 0 && profile != null &&
                !Application.isBatchMode;

            // The modal exists to ask a question. Once Hollow Shell is already on with a
            // valid Wall Thickness, it has none left: the author chose the mode and the
            // number in the main window, and allowFullSolidAnyway below is false in exactly
            // that case, so the dialog could only echo the setting back. Bake straight
            // through and report the thickness in the status line instead.
            bool hollowShellAlreadyChosen = profile != null && profile.Solidify &&
                HollowShellThicknessPrompt.IsValidThickness(profile.SolidifyThickness);
            if (modularBakeWithOpenPanels && hollowShellAlreadyChosen)
            {
                m_quickSolidify = true;
                m_quickSolidifyThickness = profile.SolidifyThickness;
                ShowStatus(
                    $"Whole Building uses HOLLOW SHELL at " +
                    $"{profile.SolidifyThickness * 100f:0.##} cm for fragments and " +
                    "module-shaped intact colliders.");
                modularBakeWithOpenPanels = false;
            }

            if (modularBakeWithOpenPanels)
            {
                HollowShellBakeChoice choice = HollowShellThicknessPrompt.Show(
                    m_source != null ? m_source.name : "Whole Building",
                    Mathf.Max(0.001f, profile.SolidifyThickness),
                    m_wholeBuildingShowShellWhileIntact, !profile.Solidify,
                    out float thicknessMeters, out bool showShellWhileIntact);
                if (choice == HollowShellBakeChoice.Cancel)
                    return false;
                if (choice == HollowShellBakeChoice.BakeFullSolidAnyway)
                {
                    allowUnsafeFractureInput = true;
                    return true;
                }

                DestructionProfile local = EnsureVolumeModeIsObjectLocal(profile);
                if (local == null) return false;
                if (m_wholeBuildingShowShellWhileIntact != showShellWhileIntact)
                {
                    m_wholeBuildingShowShellWhileIntact = showShellWhileIntact;
                    MarkBakeAppearanceDirty();
                }
                Undo.RecordObject(local, "Configure Modular Hollow Shell");
                local.Solidify = true;
                local.SolidifyThickness = Mathf.Clamp(thicknessMeters, 0.001f, 2f);
                m_profile = local;
                m_quickSolidify = true;
                m_quickSolidifyThickness = local.SolidifyThickness;
                EditorUtility.SetDirty(local);
                ShowStatus(
                    $"Whole Building will use HOLLOW SHELL at " +
                    $"{local.SolidifyThickness * 100f:0.##} cm for fragments and " +
                    "module-shaped intact colliders.");
                profile = local;
            }
            if (MeshAnalyzer.ValidateClosedFractureInput(
                    report, profile, out string warning))
                return true;

            // Missing inputs and invalid generated output remain hard failures. This
            // prompt is deliberately limited to source topology the solver can attempt
            // without ever modifying the original mesh.
            if (!MeshAnalyzer.IsRecoverableUnsafeInput(warning) ||
                Application.isBatchMode)
                return true;

            // For a flat source the correct setting is known, so offer to apply it instead
            // of only offering to proceed into the outcome the warning describes. "Continue
            // anyway" on a zero-thickness plane is a choice between a bad result and no
            // result, which is not a choice worth presenting on its own.
            bool wholeBuildingOpenPanels = IsWholeBuildingMode &&
                                           MeshAnalyzer.IsOpenSourceWarning(warning);
            bool flatSource = MeshAnalyzer.IsFlatSourceWarning(warning);
            if ((flatSource || wholeBuildingOpenPanels) &&
                profile != null)
            {
                int choice = EditorUtility.DisplayDialogComplex(
                    $"Quick Destruction - {operation} warning",
                    warning +
                    (wholeBuildingOpenPanels
                        ? "\n\nWhole Building detected open modular surfaces. HOLLOW SHELL " +
                          "adds real wall thickness to every disconnected panel before the " +
                          "shared building fracture is generated."
                        : string.Empty) +
                    "\n\nSwitch this profile to HOLLOW SHELL and continue, or run " +
                    $"{operation} on FULL SOLID anyway? Generated output is still validated, " +
                    "and invalid fragments will not be saved.",
                    "USE HOLLOW SHELL", "CANCEL", $"{operation} ANYWAY");
                if (choice == 1) return false;
                if (choice == 0)
                {
                    Undo.RecordObject(profile, "Use Hollow Shell");
                    profile.Solidify = true;
                    m_quickSolidify = true;
                    EditorUtility.SetDirty(profile);
                    ShowStatus("Volume Mode switched to HOLLOW SHELL for this flat source.");
                    EditorUtility.DisplayDialog(
                        "Quick Destruction - Hollow Shell enabled",
                        BuildAutomaticHollowShellNotice(
                            profile, operation, flatSource, wholeBuildingOpenPanels),
                        $"CONTINUE {operation}");
                    return true;
                }
                allowUnsafeFractureInput = true;
                return true;
            }

            bool continueAnyway = EditorUtility.DisplayDialog(
                $"Quick Destruction - {operation} warning",
                warning +
                $"\n\nContinue with {operation} anyway? Generated output is still " +
                "validated, and invalid fragments will not be saved.",
                $"{operation} ANYWAY", "CANCEL");
            if (!continueAnyway) return false;

            allowUnsafeFractureInput = true;
            return true;
        }

        internal static string BuildAutomaticHollowShellNotice(
            DestructionProfile profile, string operation,
            bool flatSource, bool wholeBuildingOpenPanels)
        {
            string normalizedOperation = string.IsNullOrWhiteSpace(operation)
                ? "BAKE"
                : operation.Trim().ToUpperInvariant();
            string reason = wholeBuildingOpenPanels
                ? "open modular surfaces were detected in Whole Building mode"
                : flatSource
                    ? "a flat, zero-thickness mesh was detected"
                    : "an open surface source was detected";
            string profileName = profile != null &&
                                 !string.IsNullOrWhiteSpace(profile.name)
                ? profile.name
                : "Current profile";
            float thickness = profile != null
                ? Mathf.Max(0.001f, profile.SolidifyThickness)
                : 0.08f;

            return $"Quick Destruction changed Volume Mode from FULL SOLID to " +
                   $"HOLLOW SHELL before {normalizedOperation}, because {reason}.\n\n" +
                   $"Wall Thickness: {thickness:0.###} m " +
                   $"({thickness * 100f:0.##} cm)\n" +
                   $"Profile: {profileName}\n\n" +
                   "The original source mesh was not modified. The generated fracture " +
                   $"will use this Hollow Shell setting and {normalizedOperation} will " +
                   "now continue.";
        }

        void ScheduleFracturePreview()
        {
            m_geometryDirty = true;
            // Instant feedback: flash the planned cut layout on the model whenever
            // fracture parameters change, even while the full preview is still rebuilding
            // or auto refresh is disabled.
            if (m_profile != null)
                PatternHintOverlay.Show(GetCurrentFractureSource(),
                    m_profile, m_profile.Pattern);
            if (!m_autoFracturePreview) return;
            m_previewRefreshPending = true;
            m_previewRefreshReady = false;
            m_previewRefreshAt = EditorApplication.timeSinceStartup + 0.45d;
        }

        void MarkBakeAppearanceDirty()
        {
            // Materials and cut-surface UV settings affect the baked meshes, but not the
            // fracture topology. Keep the intact model (or the user's current preview)
            // visible instead of replacing it with the colour-coded fragment preview.
            m_geometryDirty = true;
            m_previewRefreshPending = false;
            m_previewRefreshReady = false;
            PatternHintOverlay.Hide();
            Repaint();
        }

        void ProcessScheduledPreview()
        {
            if (HasBatchFracturePreview)
            {
                if (ProcessBatchFracturePreview()) Repaint();
                else if (IsFracturePreviewSolving() &&
                         EditorApplication.timeSinceStartup >= m_nextSolveRepaintAt)
                {
                    m_nextSolveRepaintAt = EditorApplication.timeSinceStartup + 0.15d;
                    Repaint();
                }
            }
            else if (m_fracturePreview != null)
            {
                if (m_fracturePreview.HasSolvedResult)
                {
                    FinishFracturePreview();
                    Repaint();
                }
                else if (m_fracturePreview.IsSolving &&
                         EditorApplication.timeSinceStartup >= m_nextSolveRepaintAt)
                {
                    // Keep the progress label moving without repainting every tick.
                    m_nextSolveRepaintAt = EditorApplication.timeSinceStartup + 0.15d;
                    Repaint();
                }
            }

            if (!m_previewRefreshPending ||
                EditorApplication.timeSinceStartup < m_previewRefreshAt)
                return;
            m_previewRefreshPending = false;
            if (!m_autoFracturePreview ||
                EditorApplication.isPlaying || m_source == null || m_profile == null ||
                (m_report != null && m_report.Status == AnalysisSeverity.Blocked))
                return;
            // EditorApplication.update may run between an IMGUI Layout and Repaint.
            // Only request a new pass here; mutate preview/UI state at the beginning
            // of the next Layout event.
            m_previewRefreshReady = true;
            Repaint();
        }

        void ProcessDeferredLayoutChanges()
        {
            if (Event.current == null || Event.current.type != EventType.Layout) return;

            if (m_selectionRefreshPending)
            {
                m_selectionRefreshPending = false;
                AdoptSelectionIfSourceMissing();
            }
            EnsureSelectedProfile();

            // Freeze the report used by the matching Repaint pass. Selection callbacks and
            // analysis can otherwise change the conditional control count between passes.
            m_layoutReport = m_report;

            // The results panel exists only while the query is non-empty, so the query the
            // Repaint sees has to be the one the Layout measured.
            if (m_layoutSearchQuery != m_searchQuery)
            {
                m_searchResultsScroll = Vector2.zero;
                if (!string.IsNullOrWhiteSpace(m_searchQuery))
                {
                    m_pendingSettingLabel = null;
                    QDUI.ClearSettingsHighlight();
                }
            }
            m_layoutSearchQuery = m_searchQuery;

            // The highlight belongs to the tab a search result opened. Once the user moves
            // to another tab it is stale, and an orange label there would only mislead.
            if (QDUI.HasSettingHighlight && m_workspace != m_highlightWorkspace)
                QDUI.ClearSettingsHighlight();

            m_layoutSolving = IsFracturePreviewSolving();
            m_layoutSolveProgress = FracturePreviewSolveProgress();

            if (!m_previewRefreshReady) return;

            // Never launch the synchronous fracture solver while an IMGUI slider is being
            // dragged. The lightweight pattern overlay still follows every value change;
            // the full selected Parts count is rebuilt once, shortly after release.
            if (GUIUtility.hotControl != 0)
            {
                m_previewGeometryControlWasHot = true;
                m_previewRefreshReady = false;
                m_previewRefreshPending = true;
                m_previewRefreshAt = EditorApplication.timeSinceStartup + 0.2d;
                return;
            }

            if (m_previewGeometryControlWasHot)
            {
                m_previewGeometryControlWasHot = false;
                m_previewRefreshReady = false;
                m_previewRefreshPending = true;
                m_previewRefreshAt = EditorApplication.timeSinceStartup + 0.25d;
                return;
            }

            m_previewRefreshReady = false;
            if (!m_autoFracturePreview ||
                EditorApplication.isPlaying || m_source == null || m_profile == null ||
                (m_report != null && m_report.Status == AnalysisSeverity.Blocked))
                return;
            GenerateFracturePreview();
        }

        // ------------------------------------------------------------------ physics

        void DrawPhysicsTab()
        {
            if (!EnsureProfileUI()) return;
            var p = m_profile;

            QDUI.BeginCard();
            QDUI.Header("Collider Baker");
            GUILayout.Label("The most realistic collider is not always the best one. Strategy follows fragment " +
                            "size, quality tier and budget; cooking happens at bake, never on first break.",
                QDUI.HelpText);
            GUILayout.Space(4f);
            EditorGUI.BeginChangeCheck();
            Undo.RecordObject(p, "Edit Destruction Profile");
            p.Colliders = ColliderStrategyRow("Strategy", p.Colliders);
            if (EditorGUI.EndChangeCheck()) EditorUtility.SetDirty(p);
            QDUI.InfoBox(GetColliderDescription(p.Colliders));
            QDUI.EndCard();

            QDUI.BeginCard();
            QDUI.Header("Mass & Damping");
            EditorGUI.BeginChangeCheck();
            Undo.RecordObject(p, "Edit Destruction Profile");
            p.Density = QDUI.SliderRow("Density (kg/m3)", p.Density, 20f, 4000f, "0");
            GUILayout.Label(
                "Each Rigidbody mass is estimated from fragment volume × density. Glass should be " +
                "light, wood medium and concrete/brick heavy.",
                QDUI.HelpText);
            if (m_advancedSetup)
            {
                p.MinFragmentMass = QDUI.SliderRow("Min Fragment Mass", p.MinFragmentMass, 0.01f, 5f);
                p.MaxFragmentMass = QDUI.SliderRow("Max Fragment Mass", p.MaxFragmentMass, 1f, 2000f, "0");
                GUILayout.Label(
                    "Mass clamps prevent tiny fragments from becoming unstable and very large " +
                    "fragments from becoming practically immovable.",
                    QDUI.HelpText);
            }
            p.Drag = QDUI.SliderRow("Drag", p.Drag, 0f, 5f);
            p.AngularDrag = QDUI.SliderRow("Angular Drag", p.AngularDrag, 0f, 5f);
            GUILayout.Label(
                "Drag slows linear movement; Angular Drag slows spinning. Higher values make debris " +
                "settle sooner and feel less explosive.",
                QDUI.HelpText);
            p.UseGravity = QDUI.ToggleRow("Use Gravity", p.UseGravity);
            GUILayout.Label(
                "Applies to generated fragments and any Dynamic or structurally released intact " +
                "body. Disable it only for zero-gravity, underwater or stylized floating destruction.",
                QDUI.HelpText);
            if (m_advancedSetup)
            {
                EditorGUILayout.BeginHorizontal();
                QDUI.DrawFieldLabel("Collision Detection",
                    GUILayout.Width(QDUI.LabelWidth));
                p.FragmentCollisionDetection = (CollisionDetectionMode)EditorGUILayout.EnumPopup(
                    p.FragmentCollisionDetection);
                EditorGUILayout.EndHorizontal();
                GUILayout.Space(3f);
                EditorGUILayout.BeginHorizontal();
                QDUI.DrawFieldLabel("Interpolation",
                    GUILayout.Width(QDUI.LabelWidth));
                p.FragmentInterpolation = (RigidbodyInterpolation)EditorGUILayout.EnumPopup(
                    p.FragmentInterpolation);
                EditorGUILayout.EndHorizontal();
                GUILayout.Space(3f);
                GUILayout.Label(
                    "This is the authored mode while baked pieces are still kinematic. Once a fragment is " +
                    "released, Quick Destruction keeps Continuous static sweep for primitive colliders and uses " +
                    "a lightweight predictive static-ground guard for convex MeshCollider debris, including the " +
                    "connected remainder of a Dynamic prop. This does not enable the more expensive " +
                    "Continuous Dynamic fragment-to-fragment sweep. Interpolation can smooth close-up debris " +
                    "but adds runtime cost.",
                    QDUI.HelpText);
            }
            if (EditorGUI.EndChangeCheck()) EditorUtility.SetDirty(p);
            GUILayout.Label("Mass = fragment volume x density, clamped. Fragment mass sum is reported after bake.",
                QDUI.HelpText);
            QDUI.EndCard();

            QDUI.BeginCard();
            QDUI.Header("Break Force Response");
            EditorGUI.BeginChangeCheck();
            Undo.RecordObject(p, "Edit Destruction Profile");
            p.DefaultExplosionForce = QDUI.SliderRow(
                "Explosion Force", p.DefaultExplosionForce, 0f, 250f);
            p.FragmentPushMultiplier = QDUI.SliderRow(
                "Break Push (Launch Force)", p.FragmentPushMultiplier, 0f, 5f, "0.00x");
            p.DirectionalImpactMultiplier = QDUI.SliderRow(
                "Directional Impact Force", p.DirectionalImpactMultiplier,
                0f, 5f, "0.00x");
            p.LocalImpactEjectSpeed = QDUI.SliderRow(
                "Local Eject Impulse", p.LocalImpactEjectSpeed, 0f, 10f, "0.00 N s");
            p.MaxFragmentLaunchSpeed = QDUI.SliderRow(
                "Max Fragment Launch Speed", p.MaxFragmentLaunchSpeed,
                1f, 50f, "0.0 m/s");
            p.MaxWholeBodyLaunchSpeed = QDUI.SliderRow(
                "Max Whole Body Push Speed", p.MaxWholeBodyLaunchSpeed,
                1f, 50f, "0.0 m/s");
            p.TallFragmentToppleSpeed = QDUI.SliderRow(
                "Tall Piece Topple", p.TallFragmentToppleSpeed, 0f, 5f, "0.00 N m s");
            p.DefaultExplosionRadius = QDUI.SliderRow(
                "Activation Radius", p.DefaultExplosionRadius, 0.1f, 20f);
            p.ExplosionUpwardModifier = QDUI.SliderRow(
                "Upward Lift", p.ExplosionUpwardModifier, -2f, 5f);
            if (EditorGUI.EndChangeCheck()) EditorUtility.SetDirty(p);
            QDUI.InfoBox("All launch forces are impulses: heavier bodies gain less speed " +
                "from the same hit. No physics mode switch is needed.");
            GUILayout.Label(
                "Radius decides which baked pieces wake near the break point. Explosion Force is " +
                "the profile default for the single-target root.Explode convenience call. A scene-owned " +
                "DestructionExplosion, DestructionBomb or the demo E key supplies its own Force instead. " +
                "Break Push multiplies every launch response, " +
                "including Rigidbody impacts. Directional Impact Force optionally adds an impulse " +
                "in the incoming projectile or ray direction when one was supplied. It defaults " +
                "to 0; high values create a strong push. " +
                "Max Fragment Launch Speed caps loose debris; Max Whole Body Push Speed " +
                "separately caps the intact Dynamic Rigidbody and connected remainder. " +
                "That cap is also where Explosion Force and Break Push stop mattering: an " +
                "impulse divided by mass IS a speed, so once Explosion Force x Break Push " +
                "exceeds a piece's mass times Max Fragment Launch Speed, every higher value " +
                "produces the same launch. Light materials reach that point almost at once — " +
                "if a blast looks weak despite a large force, raise Max Fragment Launch Speed, " +
                "not the force. Select a baked object to read its exact saturation point. " +
                "Local Eject Impulse separates locally activated " +
                "pieces with an impulse that also respects mass. Upward Lift is a share of Activation Radius, " +
                "not a distance: it lowers the blast origin by that share before each piece " +
                "is pushed away from it, so 0 throws debris level with the blast straight " +
                "sideways and 0.25 throws it up and out at any explosion size.",
                QDUI.HelpText);
            QDUI.EndCard();

            QDUI.BeginCard();
            QDUI.Header("Collision Damage Source");
            EditorGUI.BeginChangeCheck();
            Undo.RecordObject(p, "Edit Destruction Profile");
            EditorGUILayout.BeginHorizontal();
            QDUI.DrawFieldLabel("Damage Trigger", GUILayout.Width(QDUI.LabelWidth));
            p.DamageTrigger = (DamageTriggerMode)EditorGUILayout.EnumPopup(p.DamageTrigger);
            EditorGUILayout.EndHorizontal();
            p.CollisionDamage = p.DamageTrigger != DamageTriggerMode.ScriptedOnly;
            GUILayout.Label(
                p.DamageTrigger == DamageTriggerMode.ScriptedOnly
                    ? "Code / Method disables automatic collision damage. Call ApplyDamage for hitscan, " +
                      "weapons and custom bullets; use BreakNow for an immediate break."
                    : p.DamageTrigger == DamageTriggerMode.Mixed
                        ? "Mixed accepts both physical impacts and gameplay method calls. Rigidbody collision " +
                          "damage uses Unity's measured mass and velocity response."
                        : "Rigidbody Impact enables automatic collision damage. A bullet needs a Collider and " +
                          "Rigidbody; its mass and relative velocity determine the impact.",
                QDUI.HelpText);
            using (new EditorGUI.DisabledScope(!p.CollisionDamage))
            {
                p.MinCollisionVelocity = QDUI.SliderRow("Min Velocity (m/s)", p.MinCollisionVelocity, 0f, 20f);
                GUILayout.Label(
                    "Collisions slower than this are ignored, preventing resting contacts and small " +
                    "bumps from damaging the prop.",
                    QDUI.HelpText);
                p.DamageCooldown = QDUI.SliderRow("Damage Cooldown (s)", p.DamageCooldown, 0f, 2f);
                GUILayout.Label(
                    "Minimum time between accepted impacts on the intact object or its remaining " +
                    "post-fracture shell.",
                    QDUI.HelpText);
                p.PostFractureImpactRadius = QDUI.SliderRow(
                    "Post-Break Wake Radius", p.PostFractureImpactRadius, 0.05f, 5f);
                GUILayout.Label(
                    "After the first break, another external Rigidbody hit wakes not-yet-active " +
                    "baked fragments inside this radius. It is the footprint of a blow that only " +
                    "just breaks through; one worth several times Health widens it by the cube " +
                    "root of that overshoot, so a longer fall or a lower Health takes visibly " +
                    "more of the object with it. Hits from sibling pieces of the same " +
                    "destructible are ignored to prevent artificial chain reactions.",
                    QDUI.HelpText);
                EditorGUILayout.BeginHorizontal();
                QDUI.DrawFieldLabel("Impact Energy (kJ) -> Damage", GUILayout.Width(QDUI.LabelWidth));
                p.ImpulseToDamage = EditorGUILayout.CurveField(p.ImpulseToDamage);
                EditorGUILayout.EndHorizontal();
                GUILayout.Space(3f);
                GUILayout.Label(
                    "For scale: a thrown crate is well under 1 kJ, a 100 kg ball dropped two " +
                    "metres is about 2 kJ, a car at city speed is over 100 kJ. Past the last " +
                    "key the curve keeps rising as a square root, so heavier blows stay " +
                    "ordered without running away. The last key is where brittleness really " +
                    "lives: each preset sets its own — glass gives way at a fraction of a " +
                    "kilojoule, wood at a few, masonry at tens.",
                    QDUI.HelpText);
                p.DebrisImpactSignificance = QDUI.SliderRow(
                    "Debris Must Be Worth", p.DebrisImpactSignificance, 0f, 1f, "0.00");
                GUILayout.Label(
                    "Share of this object's Health a piece of loose debris must be worth before " +
                    "its impact counts as damage instead of a bump. Physics still pushes what " +
                    "debris lands on at any speed. Raise it if falling rubble breaks too much; " +
                    "lower it for fragile props.",
                    QDUI.HelpText);
                GUILayout.Space(3f);
                GUILayout.Label(
                    "Maps the kinetic energy of a collision to base Health damage. The result is then " +
                    "multiplied by Brittleness from Runtime > Material Strength & Damage. Scripted " +
                    "ApplyDamage and BreakNow remain available for weapons or custom gameplay.",
                    QDUI.HelpText);
                if (m_advancedSetup)
                {
                    EditorGUILayout.BeginHorizontal();
                    QDUI.DrawFieldLabel("Collision Layers", GUILayout.Width(QDUI.LabelWidth));
                    p.CollisionLayers = LayerMaskField(p.CollisionLayers);
                    EditorGUILayout.EndHorizontal();
                }
            }
            if (EditorGUI.EndChangeCheck()) EditorUtility.SetDirty(p);
            QDUI.EndCard();
        }

        static LayerMask LayerMaskField(LayerMask mask)
        {
            var layers = UnityEditorInternal.InternalEditorUtility.layers;
            int concatenated = UnityEditorInternal.InternalEditorUtility.LayerMaskToConcatenatedLayersMask(mask);
            concatenated = EditorGUILayout.MaskField(concatenated, layers);
            return UnityEditorInternal.InternalEditorUtility.ConcatenatedLayersMaskToLayerMask(concatenated);
        }

        // ------------------------------------------------------------------ structure

        void DrawStructureTab()
        {
            if (!EnsureProfileUI()) return;
            var p = m_profile;

            QDUI.BeginCard();
            QDUI.Header("Intact Structural Body");
            GUILayout.Label(
                "This controls the complete, not-yet-fractured object. Fragment Rigidbodies still " +
                "use the settings from Physics after the object breaks.",
                QDUI.HelpText);
            GUILayout.Space(4f);
            EditorGUI.BeginChangeCheck();
            Undo.RecordObject(p, "Edit Structural Body");
            IntactBodyMode previousBodyMode = p.IntactBody;
            p.IntactBody = (IntactBodyMode)QDUI.PopupRow(
                "Body Mode", (int)p.IntactBody, IntactBodyNames);
            if (p.IntactBody != previousBodyMode)
            {
                EditorGUI.EndChangeCheck();
                EditorUtility.SetDirty(p);
                GUIUtility.ExitGUI();
            }
            if (p.IntactBody != IntactBodyMode.Static)
            {
                p.IntactMassMultiplier = QDUI.SliderRow(
                    "Intact Mass Multiplier", p.IntactMassMultiplier, 0.05f, 10f);
                GUILayout.Label(
                    "Bake calculates intact mass from object volume × Density, then applies this " +
                    "multiplier. Use it to make a slab feel reinforced or unusually light.",
                    QDUI.HelpText);
                using (new EditorGUI.DisabledScope(!p.CollisionDamage))
                    p.FallDamage = QDUI.ToggleRow("Fall Damage", p.FallDamage);
                GUILayout.Label(
                    p.CollisionDamage
                        ? "When enabled, landing speed (equivalent fall height) damages Health. " +
                          "Default wood takes partial damage near a 5 m fall and normally needs " +
                          "about 20 m to break from one landing. Baked mass does not make self-falls " +
                          "more fragile; projectile impacts still use their physical mass."
                        : "Enable Rigidbody Impact or Mixed damage in Physics to use Fall Damage.",
                    QDUI.HelpText);
            }
            if (EditorGUI.EndChangeCheck()) EditorUtility.SetDirty(p);
            QDUI.InfoBox(GetIntactBodyDescription(p.IntactBody));
            if (p.IntactBody != IntactBodyMode.Static)
            {
                GUILayout.Label(
                    "Changing Body Mode or Intact Mass requires Bake & Setup so the prefab receives " +
                    "the correct Rigidbody and structural component.",
                    QDUI.HelpText);
            }
            QDUI.EndCard();

            if (p.IntactBody == IntactBodyMode.ReleaseWhenUnsupported)
            {
                QDUI.BeginCard();
                QDUI.Header("Support Detection");
                if (p.SupportDetection != SupportDetectionMode.AutomaticBelow)
                {
                    Undo.RecordObject(p, "Use Automatic Support Detection");
                    p.SupportDetection = SupportDetectionMode.AutomaticBelow;
                    EditorUtility.SetDirty(p);
                }
                EditorGUI.BeginChangeCheck();
                Undo.RecordObject(p, "Edit Structural Supports");
                QDUI.InfoBox(GetSupportDetectionDescription());

                float requiredSupportPercent = QDUI.SliderRow(
                    "Minimum Support Remaining (%)", p.RequiredSupportRatio * 100f, 5f, 100f, "0");
                p.RequiredSupportRatio = requiredSupportPercent / 100f;
                int supportResolution =
                    StructuralSupportNode.GetEffectiveProbeResolution(p.SupportProbeGrid);
                int supportSamplesPerElement = supportResolution * supportResolution;
                int estimatedRequiredSamples = Mathf.Clamp(
                    Mathf.CeilToInt(supportSamplesPerElement * p.RequiredSupportRatio),
                    1, supportSamplesPerElement);
                float estimatedEffectivePercent =
                    estimatedRequiredSamples * 100f / supportSamplesPerElement;
                float estimatedPercentStep = 100f / supportSamplesPerElement;
                GUILayout.Label(
                    $"Currently {requiredSupportPercent:0}%: the body stays held while at least this share " +
                    "of the shared world foundation still stands. Ground-contact probes from walls at " +
                    "different heights join this one total; probes where floors/roofs rest on other " +
                    "modules remain local to those elements. 100% is captured the moment the shell " +
                    "actually breaks, so it means the footing that carries real material, not the whole " +
                    "collider footprint. Fragments also fall when less than this share of their original " +
                    "bonded face contact remains, preventing large shards from hanging by a tiny joint. " +
                    "Below the footing share, Collapse Delay starts.",
                    QDUI.HelpText);
                GUILayout.Label(
                    $"Approximate sampling: {supportResolution}x{supportResolution} = " +
                    $"{supportSamplesPerElement} points per structural element. One point is about " +
                    $"{estimatedPercentStep:0.#}%; the selected {requiredSupportPercent:0}% threshold " +
                    $"currently resolves to about {estimatedEffectivePercent:0.#}% " +
                    $"({estimatedRequiredSamples}/{supportSamplesPerElement} points). Play Mode shows " +
                    "the exact baseline and live percentage for this object.",
                    QDUI.HelpText);
                p.SupportGraceTime = QDUI.SliderRow(
                    "Collapse Delay (s)", p.SupportGraceTime, 0f, 3f);
                GUILayout.Label(
                    "The delay filters one-frame contact gaps and can create a short dramatic pause " +
                    "before the upper structure starts falling.",
                    QDUI.HelpText);

                p.MaxUnsupportedSpan = QDUI.SliderRow(
                    "Maximum Unsupported Span (m)", p.MaxUnsupportedSpan, 0f, 20f);
                GUILayout.Label(
                    p.MaxUnsupportedSpan > 0f
                        ? $"Connected fragments may bridge at most {p.MaxUnsupportedSpan:0.##} m " +
                          "sideways from surviving support after this element falls below Required " +
                          "Support Ratio. This keeps early local damage local, then releases large " +
                          "remnants held only by a thin chain or green footing probe."
                        : "Zero disables the span limit: any surviving neighbour path back to a " +
                          "green footing probe can hold the entire connected wall. Use a non-zero " +
                          "value to prevent large remnants hanging from one corner or side joint.",
                    QDUI.HelpText);

                p.SupportProbeDistance = QDUI.SliderRow(
                    "Probe Distance", p.SupportProbeDistance, 0.01f, 10f);
                GUILayout.Label(
                    "Only colliders inside this distance below the intact collider count. Keep it " +
                    "shorter than the distance to the ground so destroyed columns cannot be replaced " +
                    "by the floor as support. Only static or kinematic colliders count; debris, " +
                    "bullets and rolling props are ignored. If no valid support is found, the object " +
                    "releases after Collapse Delay instead of remaining suspended.",
                    QDUI.HelpText);
                if (m_advancedSetup)
                {
                    p.SupportProbeGrid = QDUI.IntSliderRow(
                        "Probe Grid", p.SupportProbeGrid, 2, 5);
                    p.SupportProbeInset = QDUI.SliderRow(
                        "Edge Inset", p.SupportProbeInset, 0f, 0.45f);
                    p.SupportCheckInterval = QDUI.SliderRow(
                        "Check Interval (s)", p.SupportCheckInterval, 0.02f, 1f);
                    EditorGUILayout.BeginHorizontal();
                    QDUI.DrawFieldLabel("Support Layers",
                        GUILayout.Width(QDUI.LabelWidth));
                    p.SupportLayers = LayerMaskField(p.SupportLayers);
                    EditorGUILayout.EndHorizontal();
                    GUILayout.Label(
                        "Probe Grid is a sampling-quality control: 2, 3, 4 and 5 evaluate effective " +
                        "3x3, 5x5, 7x7 and 9x9 footing grids. Higher density makes percentage thresholds " +
                        "smoother. Edge Inset moves samples away from thin borders; broad horizontal " +
                        "floors and roofs always keep their outer probe row on the perimeter so supporting " +
                        "walls are measured individually. Support Layers can exclude characters, debris " +
                        "and triggers.",
                        QDUI.HelpText);
                }

                if (EditorGUI.EndChangeCheck()) EditorUtility.SetDirty(p);
                QDUI.EndCard();
            }

            QDUI.BeginCard();
            QDUI.Header("Collapse Result");
            EditorGUILayout.BeginHorizontal();
            QDUI.StatBlock($"{p.Health:0}", "STRENGTH");
            QDUI.StatBlock($"{p.Brittleness * 100f:0}%", "BRITTLENESS", QDUI.Amber);
            QDUI.StatBlock($"{p.FragmentCount}", "BAKED PIECES", QDUI.Blue);
            EditorGUILayout.EndHorizontal();
            GUILayout.Label(
                p.Pattern == FracturePattern.RadialGlass
                    ? "Strength / Health is the damage required to break. Brittleness multiplies " +
                      "collision damage. Glass piece count is driven by Spokes × Rings in the " +
                      "Fracture workspace."
                    : "Strength / Health is the damage required to break. Brittleness multiplies " +
                      "collision damage. Max Parts controls the baked piece-size target: more " +
                      "fragments means smaller pieces.",
                QDUI.HelpText);
            GUILayout.Space(4f);
            if (!p.CollisionDamage)
            {
                QDUI.WarnBox(
                    "Collision Damage is disabled. Unsupported objects will fall but will not " +
                    "automatically fracture when they hit the ground.");
            }
            else
            {
                QDUI.InfoBox(
                    "Runtime flow: support falls below threshold  >  intact Rigidbody is released  >  " +
                    "landing impulse is converted to damage  >  Health reaches zero  >  baked fragments activate.");
            }
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Edit Impact Response", QDUI.GhostBtn))
                m_workspace = Workspace.Physics;
            if (GUILayout.Button("Edit Strength & Brittleness", QDUI.GhostBtn))
                m_workspace = Workspace.Runtime;
            EditorGUILayout.EndHorizontal();
            GUILayout.Label(
                p.SecondaryBreakup == SecondaryBreakupLevel.Off
                    ? "Secondary Breakup is Off, so this profile uses one fracture generation. " +
                      "Enable it in Fracture to let selected large chunks split once more."
                    : "Selected large chunks contain a pre-baked second level. Level-two pieces " +
                      "do not split into a third generation.",
                QDUI.HelpText);
            QDUI.EndCard();
        }

        // ------------------------------------------------------------------ runtime

        static readonly string[] ImpactSpreadFalloffNames = { "Damage and Impulse", "Damage Only", "Impulse Only", "None (Uniform)" };

        void DrawRuntimeTab()
        {
            DrawSceneDebrisBudget();
            if (!EnsureProfileUI()) return;
            var p = m_profile;


            QDUI.BeginCard();
            QDUI.Header("Strength & Impact Damage");
            EditorGUI.BeginChangeCheck();
            Undo.RecordObject(p, "Edit Destruction Profile");
            p.Health = QDUI.SliderRow(
                "Strength / Health", p.Health, 1f, DestructionProfile.MaxHealth, "0");
            EditorGUILayout.BeginHorizontal();
            QDUI.DrawFieldLabel("Minimum Hit Damage",
                GUILayout.Width(QDUI.LabelWidth));
            p.MinimumHitDamage = Mathf.Max(0f, EditorGUILayout.FloatField(p.MinimumHitDamage));
            EditorGUILayout.EndHorizontal();
            GUILayout.Label(
                "Total scripted or impact damage required before the intact object changes to the " +
                "fractured hierarchy. Minimum Hit Damage ignores weaker individual hits without " +
                "accumulating wear; physical push still applies. 0 accepts all hits. Set the minimum " +
                "to the strongest attack's damage to require that attack. Break Immediately and " +
                "loss of structural support still force a break.",
                QDUI.HelpText);
            p.SpreadImpactToNearbyObjects = QDUI.ToggleRow(
                "Spread Impact To Nearby Objects", p.SpreadImpactToNearbyObjects);
            using (new EditorGUI.DisabledScope(!p.SpreadImpactToNearbyObjects))
            {
                p.ImpactSpreadRadius = QDUI.SliderRow(
                    "Impact Spread Radius", p.ImpactSpreadRadius, 0f, 10f, "0.0 m");
                p.SpreadFalloff = (ImpactSpreadFalloff)QDUI.PopupRow(
                    "Spread Distance Falloff", (int)p.SpreadFalloff, ImpactSpreadFalloffNames);
                p.ImpactSpreadDamagePercent = QDUI.SliderRow(
                    "Spread Damage (%)", p.ImpactSpreadDamagePercent, 0f, 200f, "0'%'");
                p.ImpactSpreadImpulsePercent = QDUI.SliderRow(
                    "Spread Impulse (%)", p.ImpactSpreadImpulsePercent, 0f, 200f, "0'%'");
            }
            if (p.SpreadImpactToNearbyObjects)
                GUILayout.Label(
                    "Exact world radius; higher force or damage never increases it. Empty Impact Spread " +
                    "Parent limits the wave to the baked root and its children. Assign a common ancestor " +
                    "on the instance to include separate modules of the same building. " +
                    "Percentages apply to the original incoming hit. With linear damage falloff, 50% " +
                    "at the centre becomes 25% halfway to the edge and 0% at the radius. " +
                    "Direct hits retain their own local radius; recipients cannot relay the wave. " +
                    "Loss of structural support can still cause a collapse beyond the impact area.",
                    QDUI.HelpText);
            if (m_contextRoot != null)
            {
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.BeginHorizontal();
                QDUI.DrawFieldLabel("Impact Spread Parent", GUILayout.Width(QDUI.LabelWidth));
                Transform parent = (Transform)EditorGUILayout.ObjectField(
                    m_contextRoot.ImpactSpreadParent, typeof(Transform), true);
                EditorGUILayout.EndHorizontal();
                if (EditorGUI.EndChangeCheck())
                {
                    if (parent == null || m_contextRoot.transform.IsChildOf(parent))
                    {
                        Undo.RecordObject(m_contextRoot, "Change Impact Spread Parent");
                        m_contextRoot.ImpactSpreadParent = parent;
                        EditorUtility.SetDirty(m_contextRoot);
                        PrefabUtility.RecordPrefabInstancePropertyModifications(m_contextRoot);
                    }
                    else ShowStatus("Impact Spread Parent must contain this destructible.");
                }
                GUILayout.Label("Spread hierarchy: " + m_contextRoot.ResolvedImpactSpreadParent.name, QDUI.HelpText);
            }
            float rigidbodyDamageMultiplier = Mathf.Lerp(0.35f, 2.5f, p.Brittleness);
            EditorGUI.BeginChangeCheck();
            rigidbodyDamageMultiplier = QDUI.SliderRow(
                "Rigidbody Damage Multiplier", rigidbodyDamageMultiplier,
                0.35f, 2.5f, "0.00x");
            if (EditorGUI.EndChangeCheck())
                p.Brittleness = Mathf.InverseLerp(0.35f, 2.5f, rigidbodyDamageMultiplier);
            GUILayout.Label(
                p.Pattern == FracturePattern.RadialGlass
                    ? "Direct multiplier applied to damage from Rigidbody collisions. It does not " +
                      "change baked piece size; Glass piece count is driven by Spokes x Rings."
                    : "Direct multiplier applied to damage from Rigidbody collisions. It does not " +
                      "change baked piece size; use Max Parts for that.",
                QDUI.HelpText);
            if (EditorGUI.EndChangeCheck()) EditorUtility.SetDirty(p);
            QDUI.EndCard();

            QDUI.BeginCard();
            QDUI.Header("Iterative Runtime Re-Fracture");
            GUILayout.Label(
                "Optionally slices the loose fragment that receives an explicit gameplay hit. " +
                "It is enabled by default for non-glass presets, creates no runtime meshes " +
                "while disabled, and replaces the pre-baked Secondary Breakup path while enabled.",
                QDUI.HelpText);
            EditorGUI.BeginChangeCheck();
            Undo.RecordObject(p, "Edit Runtime Re-Fracture");
            bool glassRuntimeDisabled = p.Pattern == FracturePattern.RadialGlass;
            if (glassRuntimeDisabled)
            {
                p.RuntimeFragmentSplitting = false;
                m_quickRuntimeFragmentSplitting = false;
            }
            using (new EditorGUI.DisabledScope(glassRuntimeDisabled))
                p.RuntimeFragmentSplitting = QDUI.ToggleRow(
                    "Split Hit Fragments At Runtime", p.RuntimeFragmentSplitting);
            if (glassRuntimeDisabled)
                QDUI.InfoBox(
                    "GLASS: runtime re-fracture and interior scatter are disabled. " +
                    "Use the baked Spokes and Rings controls for glass breakup.");
            if (p.RuntimeFragmentSplitting)
            {
                p.SecondaryBreakup = SecondaryBreakupLevel.Off;
                m_quickRuntimeFragmentSplitting = true;
                GUILayout.Space(4f);
                p.RuntimeSplitPieces = QDUI.IntSliderRow(
                    "Pieces Per Split", p.RuntimeSplitPieces, 2, 4);
                p.RuntimeSplitMaxDepth = QDUI.IntSliderRow(
                    "Max Split Iterations", p.RuntimeSplitMaxDepth, 1, 4);
                p.RuntimeSplitMinVolume = QDUI.SliderRow(
                    "Minimum Fragment Volume", p.RuntimeSplitMinVolume, 0.0001f, 0.1f, "0.0000");
                p.RuntimeSplitSeparationForce = QDUI.SliderRow(
                    "Break Apart Force", p.RuntimeSplitSeparationForce, 0f, 20f, "0.0");

                QDUI.InfoBox(
                    $"An accepted hit creates up to {p.RuntimeSplitPieces} real clipped pieces, up to " +
                    $"iteration {p.RuntimeSplitMaxDepth}. The cutter samples up to six bounded fast planes " +
                    "inside a fixed global attempt budget, then uses at most two repair attempts. Only " +
                    "pieces above Minimum Fragment Volume split again. Automatic loose-fragment landings " +
                    "are processed one per frame, with at most eight " +
                    "accepted splits per destructible collapse cycle.");
            }
            else
            {
                m_quickRuntimeFragmentSplitting = false;
                QDUI.InfoBox(
                    "OFF: no hit search, generated Rigidbody or runtime breakup " +
                    "updates are performed by runtime re-fracture.");
            }
            if (EditorGUI.EndChangeCheck()) EditorUtility.SetDirty(p);
            QDUI.EndCard();

            QDUI.BeginCard();
            QDUI.Header("Default Hit Particles & Sound");
            GUILayout.Label(
                "These defaults belong to this Destruction Profile and are used automatically " +
                "by every baked object that references it. Custom references are optional.",
                QDUI.HelpText);
            EditorGUI.BeginChangeCheck();
            Undo.RecordObject(p, "Edit Profile Hit Effects");
            p.PlayHitEffectsOnEveryHit = QDUI.ToggleRow(
                "Play On Every Hit", p.PlayHitEffectsOnEveryHit);

            GUILayout.Space(4f);
            p.UseDefaultHitParticles = QDUI.ToggleRow(
                "Preset Particles", p.UseDefaultHitParticles);
            EditorGUILayout.BeginHorizontal();
            QDUI.DrawFieldLabel("Profile Particle Override",
                GUILayout.Width(QDUI.LabelWidth));
            p.HitParticlePrefab = (ParticleSystem)EditorGUILayout.ObjectField(
                p.HitParticlePrefab, typeof(ParticleSystem), false);
            EditorGUILayout.EndHorizontal();
            GUILayout.Label(
                DestructionProfile.IsPipelineParticleTemplate(p.HitParticlePrefab)
                    ? "IGNORED: this is a render pipeline template, not an effect. It is " +
                      "treated as an empty field, so the preset burst plays instead. Clear " +
                      "it, or assign a ParticleSystem of your own."
                    : p.HitParticlePrefab != null
                        ? "CONNECTED: the assigned ParticleSystem overrides the preset particle burst."
                        : p.UseDefaultHitParticles
                            ? $"CONNECTED: {PresetLabel(p.Pattern)} supplies its procedural particle burst."
                            : "No profile particle effect is connected.",
                QDUI.HelpText);

            GUILayout.Space(4f);
            p.UseDefaultHitSound = QDUI.ToggleRow(
                "Preset Sound", p.UseDefaultHitSound);
            EditorGUILayout.BeginHorizontal();
            QDUI.DrawFieldLabel("Profile Sound Override",
                GUILayout.Width(QDUI.LabelWidth));
            p.HitSound = (AudioClip)EditorGUILayout.ObjectField(
                p.HitSound, typeof(AudioClip), false);
            EditorGUILayout.EndHorizontal();
            GUILayout.Label(
                p.HitSound != null
                    ? "CONNECTED: the assigned AudioClip overrides the preset sound."
                    : p.UseDefaultHitSound
                        ? $"CONNECTED: {PresetLabel(p.Pattern)} supplies its synthesized hit sound."
                        : "No profile sound is connected.",
                QDUI.HelpText);

            GUILayout.Space(6f);
            QDUI.SubHeaderLabel("FRAGMENT LANDING EFFECTS");
            p.FragmentImpactEffects = QDUI.ToggleRow(
                "One-Time Ground Landing Dust", p.FragmentImpactEffects);
            if (p.FragmentImpactEffects)
            {
                EditorGUILayout.BeginHorizontal();
                QDUI.DrawFieldLabel("Ground Landing Dust",
                    GUILayout.Width(QDUI.LabelWidth));
                p.GroundDustParticlePrefab = (ParticleSystem)EditorGUILayout.ObjectField(
                    p.GroundDustParticlePrefab, typeof(ParticleSystem), false);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.BeginHorizontal();
                QDUI.DrawFieldLabel("Ground Landing Sound",
                    GUILayout.Width(QDUI.LabelWidth));
                p.GroundDustSound = (AudioClip)EditorGUILayout.ObjectField(
                    p.GroundDustSound, typeof(AudioClip), false);
                EditorGUILayout.EndHorizontal();
                p.FragmentImpactMinDropHeight = QDUI.SliderRow(
                    "Minimum Drop Height (m)", p.FragmentImpactMinDropHeight, 0f, 10f, "0.00");
                p.FragmentImpactMinSpeed = QDUI.SliderRow(
                    "Min Landing Speed", p.FragmentImpactMinSpeed, 0f, 12f);
                p.FragmentImpactStrength = QDUI.SliderRow(
                    "Landing Dust Strength", p.FragmentImpactStrength, 0f, 1f);
                GUILayout.Label(
                    "The assigned prefab is visible here and keeps its own authored colour. It plays " +
                    "once per physical fragment only after a downward fall onto external static ground; " +
                    "the selected profile sound plays alongside it at a softer level. Normal hits, " +
                    "walls, debris and runtime-piece cleanup never trigger it.",
                    QDUI.HelpText);
            }
            else
            {
                QDUI.InfoBox(
                    "Optional. When enabled, falling fragments create one dust burst on their first " +
                    "valid ground landing, separate from the hit effect.");
            }

            if (EditorGUI.EndChangeCheck()) EditorUtility.SetDirty(p);
            QDUI.InfoBox(
                "Preset effects are generated and pooled at runtime, so their Object fields stay " +
                "empty by design. Assign an override only when you want to replace a preset.");
            QDUI.EndCard();

            QDUI.BeginCard();
            QDUI.Header("Lifecycle & Pooling");
            EditorGUI.BeginChangeCheck();
            Undo.RecordObject(p, "Edit Destruction Profile");
            if (p.EnforceActiveBodyBudget)
            {
                p.MaxActiveFragments = QDUI.IntSliderRow(
                    "Max Active Fragments", p.MaxActiveFragments, 1, 250);
                GUILayout.Label(
                    "Optional expert cap of fragment Rigidbodies that may wake up simultaneously. " +
                    "Disable Enforce Active Body Budget to guarantee unrestricted destruction.",
                    QDUI.HelpText);
            }
            else
            {
                QDUI.InfoBox(
                    "COMPLETE DESTRUCTION: no active-body limit is applied. Repeated local hits can " +
                    "continue removing pieces until the baked object is completely destroyed. " +
                    "Use lifecycle cleanup below to control debris cost without blocking damage.");
            }

            GUILayout.Space(6f);
            QDUI.SubHeaderLabel("SMALLEST-FIRST DEGRADATION");
            p.DegradeSmallestOverBudget = QDUI.ToggleRow(
                "Degrade Smallest Over Budget", p.DegradeSmallestOverBudget);
            if (p.DegradeSmallestOverBudget)
            {
                p.MaxActiveFragments = QDUI.IntSliderRow(
                    "Freeze Above (active)", p.MaxActiveFragments, 1, 250);
                p.MaxVisibleFragments = QDUI.IntSliderRow(
                    "Remove Above (visible)", p.MaxVisibleFragments, 0, 1000);
                p.MaxVisibleTriangles = QDUI.IntSliderRow(
                    "Triangle Budget", p.MaxVisibleTriangles, 0, 500000);
                GUILayout.Label(
                    "Keeps heavy destruction bounded by degrading the smallest pieces first. " +
                    "Stage 1: once more than Freeze Above fragments simulate, the smallest excess " +
                    "freeze into non-interactive visual props (still visible). Stage 2: once the " +
                    "visible count or Triangle Budget is exceeded, the smallest visible pieces fade " +
                    "out (gradual shrink). Set either visible limit to 0 to disable that stage. " +
                    "Large, prominent chunks are always kept.",
                    QDUI.HelpText);
            }
            else
            {
                QDUI.InfoBox(
                    "Optional. When enabled, exceeding the fragment or triangle budget removes the " +
                    "smallest pieces first (freeze, then fade) instead of denying new destruction.");
            }

            GUILayout.Space(6f);
            QDUI.SubHeaderLabel("DEBRIS AUTO-DESPAWN");
            p.KeepDebris = QDUI.ToggleRow("Keep Debris", p.KeepDebris);
            DrawSceneCleanupOverrideNotice();
            using (new EditorGUI.DisabledScope(p.KeepDebris))
            {
                p.DespawnDebris = QDUI.ToggleRow("Despawn Debris", p.DespawnDebris);
                if (p.DespawnDebris)
                    p.DespawnDebrisAfter = QDUI.SliderRow(
                        "Despawn After (s)", p.DespawnDebrisAfter, 5f, 300f, "0");
            }
            GUILayout.Label(
                "Every broken-off piece fades out this long after it came loose, releasing " +
                "its physics, collider and rendering cost — including level-two children and " +
                "runtime split pieces. This profile timer is used only when Scene Debris Cleanup and Keep Debris " +
                "are disabled. For removal after landing, use the scene rule above. Keep Debris preserves this profile's rubble.",
                QDUI.HelpText);

            GUILayout.Space(6f);
            QDUI.SubHeaderLabel("WHEN SHOULD DEBRIS CLEAN UP?");
            GUILayout.Label("Choose one simple rule. Keep Until Reset is the default and never removes playable debris.",
                QDUI.HelpText);
            int cleanupRule = p.LimitActiveTime
                ? (p.CleanupSettledFragments ? 3 : 2)
                : (p.CleanupSettledFragments ? 1 : 0);
            EditorGUILayout.BeginHorizontal();
            bool chooseKeep = QDUI.Chip("KEEP UNTIL RESET", cleanupRule == 0);
            bool chooseRest = QDUI.Chip("AFTER IT STOPS", cleanupRule == 1);
            bool chooseTime = QDUI.Chip("AFTER TIME", cleanupRule == 2);
            bool chooseEither = QDUI.Chip("FIRST OF EITHER", cleanupRule == 3);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            int requestedRule = chooseKeep ? 0 : chooseRest ? 1 : chooseTime ? 2 : chooseEither ? 3 : cleanupRule;
            if (requestedRule != cleanupRule)
            {
                p.LimitActiveTime = requestedRule == 2 || requestedRule == 3;
                p.CleanupSettledFragments = requestedRule == 1 || requestedRule == 3;
                if (requestedRule != 0 && p.Cleanup == CleanupAction.None)
                    p.Cleanup = CleanupAction.FreezeAsProxy;
                EditorGUI.EndChangeCheck();
                EditorUtility.SetDirty(p);
                GUIUtility.ExitGUI();
            }

            bool cleanupCanRun = cleanupRule != 0;
            if (p.LimitActiveTime)
            {
                p.CleanupLifetime = QDUI.SliderRow(
                    "Clean Up After (s)", p.CleanupLifetime, 0.5f, 120f, "0.#");
                GUILayout.Label("Each piece gets its own timer when it starts moving. It is cleaned even if it is still sliding.",
                    QDUI.HelpText);
            }
            if (p.CleanupSettledFragments)
            {
                QDUI.SubHeaderLabel("REST DETECTION");
                p.SleepVelocity = QDUI.SliderRow(
                    "Movement Below (m/s)", p.SleepVelocity, 0.01f, 1f);
                p.SleepDuration = QDUI.SliderRow(
                    "Stay Calm For (s)", p.SleepDuration, 0.1f, 10f);
                GUILayout.Label("A piece cleans up only after it has moved more slowly than this value for the full calm time.",
                    QDUI.HelpText);
            }

            if (cleanupCanRun)
            {
                GUILayout.Space(4f);
                QDUI.SubHeaderLabel("WHEN CLEANUP HAPPENS");
                EditorGUILayout.BeginHorizontal();
                if (QDUI.Chip("FREEZE IN PLACE", p.Cleanup == CleanupAction.FreezeAsProxy))
                    p.Cleanup = CleanupAction.FreezeAsProxy;
                if (QDUI.Chip("DESPAWN", p.Cleanup == CleanupAction.Recycle))
                    p.Cleanup = CleanupAction.Recycle;
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();

                if (p.Cleanup == CleanupAction.Recycle)
                {
                    p.SmoothDespawn = QDUI.ToggleRow("Smooth Despawn", p.SmoothDespawn);
                    GUILayout.Label("Smooth Despawn shrinks the piece over a fixed 0.35 s, then hides it. No extra timing slider is needed.",
                        QDUI.HelpText);
                }
                else if (p.Cleanup == CleanupAction.None)
                {
                    QDUI.WarnBox("Choose Freeze In Place or Despawn to make the cleanup rule do work.");
                }
                QDUI.InfoBox(GetCleanupDescription(p.Cleanup));
            }
            else
            {
                QDUI.InfoBox("Debris stays in the scene and can still be hit again. It is only restored or removed when your game calls Reset.");
            }
            if (EditorGUI.EndChangeCheck()) EditorUtility.SetDirty(p);
            GUILayout.Label("Fragments are baked into the prefab and only toggled at runtime: " +
                            "no Instantiate/Destroy in the normal break cycle, reset is idempotent.",
                QDUI.HelpText);
            QDUI.EndCard();

        }

        /*
        void ApplyTier(QualityTier tier)
        {
            if (m_profile == null) return;
            Undo.RecordObject(m_profile, "Apply Quality Tier");
            m_profile.ApplyQualityTier(tier);
            EditorUtility.SetDirty(m_profile);
            ScheduleFracturePreview();
            ShowStatus($"{tier} tier applied. Preview shows the new whole-mesh partition; run Bake & Setup to rebuild the prefab.");
        }
        */

        // ------------------------------------------------------------------ bake

        void DrawBakeTab()
        {
            if (!EnsureProfileUI()) return;

            QDUI.BeginCard();
            QDUI.Header("Output");
            DrawStartDamageTrigger();
            GUILayout.Space(7f);
            DrawOutputBakeOptions(m_profile.Pattern);
            GUILayout.Space(7f);
            string folderLabel = m_source != null
                ? BakePipeline.GetOutputFolder(m_source, m_profile)
                : $"{BakePipeline.GeneratedRootFolder}/<source>";
            GUILayout.Label(
                $"Folder: <color=#9A9A9A>{folderLabel}/</color>\n" +
                "Contents: prefab + manifest (fragment meshes live inside the manifest asset). " +
                "The source prefab is never modified.", QDUI.Body);
            GUILayout.Space(4f);

            // Rebake freshness: compare the manifest's stored input hash with a freshly
            // computed one (the same hash function the Edit-Mode preview uses).
            if (m_source != null)
            {
                var outFilter = m_source.GetComponentInChildren<MeshFilter>();
                if (outFilter != null && outFilter.sharedMesh != null)
                {
                    string manifestPath =
                        BakePipeline.GetManifestPath(m_source, m_profile);
                    var existingManifest =
                        AssetDatabase.LoadAssetAtPath<GeneratedManifest>(manifestPath);
                    if (existingManifest != null)
                    {
                        bool outOfDate = existingManifest.InputHash !=
                            BakePipeline.ComputeInputHash(outFilter.sharedMesh, m_profile,
                                outFilter.transform.lossyScale);
                        EditorGUILayout.BeginHorizontal();
                        QDUI.StatusPill(outOfDate ? "OUT OF DATE" : "UP TO DATE",
                            outOfDate ? QDUI.Amber : QDUI.Green, 96f);
                        GUILayout.FlexibleSpace();
                        EditorGUILayout.EndHorizontal();
                        if (outOfDate)
                        {
                            QDUI.WarnBox(
                                "Output out of date vs current source/profile. Rebake to update the generated prefab.");
                        }
                    }
                }
            }

            using (new EditorGUI.DisabledScope(!CanBake()))
            {
                if (QDUI.BigButton("BAKE  &  SETUP"))
                {
                    RunBake();
                    GUIUtility.ExitGUI();
                }
            }
            if (!CanBake())
                QDUI.WarnBox(m_source == null
                    ? "Assign a source object first."
                    : "Analyze reports a blocker - fix the source before baking.");
            QDUI.EndCard();

            if (m_lastBake != null)
            {
                QDUI.BeginCard();
                QDUI.Header("Last Bake Report");
                if (m_lastBake.Success)
                {
                    EditorGUILayout.BeginHorizontal();
                    QDUI.StatBlock($"{m_lastBake.FragmentCount}", "FRAGMENTS");
                    QDUI.StatBlock($"{m_lastBake.TotalMass:0.#} kg", "TOTAL MASS");
                    QDUI.StatBlock($"{m_lastBake.Seconds:0.0}s", "BAKE TIME");
                    QDUI.StatBlock("OK", "STATUS", QDUI.Green);
                    EditorGUILayout.EndHorizontal();
                    if (m_lastBake.SecondaryFragmentCount > 0)
                    {
                        EditorGUILayout.BeginHorizontal();
                        QDUI.StatBlock(
                            $"{m_lastBake.SecondaryFragmentCount}",
                            "LEVEL-TWO PIECES", QDUI.Amber);
                        QDUI.StatBlock(
                            $"{m_lastBake.SecondaryBodyCount}",
                            "LEVEL-TWO BODIES", QDUI.Blue);
                        QDUI.StatBlock(
                            $"{m_profile.MaxActiveSecondaryBodies}",
                            "LEVEL-TWO ACTIVE CAP");
                        EditorGUILayout.EndHorizontal();
                    }
                    GUILayout.Space(4f);
                    DrawReportMessages(m_lastBake.Warnings,
                        "Bake.MeshPreparation");
                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button("Ping Prefab", QDUI.GhostBtn, GUILayout.Width(100f)))
                    {
                        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(m_lastBake.PrefabPath);
                        EditorGUIUtility.PingObject(prefab);
                    }
                    if (GUILayout.Button("Open Prefab", QDUI.GhostBtn, GUILayout.Width(100f)))
                    {
                        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(m_lastBake.PrefabPath);
                        if (prefab != null) AssetDatabase.OpenAsset(prefab);
                    }
                    if (GUILayout.Button("Select Profile", QDUI.GhostBtn, GUILayout.Width(96f)))
                    {
                        Selection.activeObject = m_profile;
                        EditorGUIUtility.PingObject(m_profile);
                    }
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.EndHorizontal();
                    GUILayout.Label(
                        "Open Prefab enters Prefab Mode, where fragment Renderers, Colliders and " +
                        "Rigidbody settings can be inspected or adjusted. Use Capture Current Fragment " +
                        "Layout in the DestructibleRoot inspector after moving pieces manually.",
                        QDUI.HelpText);
                }
                else
                {
                    string error = m_lastBake.Error ?? "Unknown error.";
                    bool fragmentSafetyStop =
                        error.StartsWith("QD-PERF-001:", System.StringComparison.Ordinal) ||
                        error.StartsWith("QD-PERF-002:", System.StringComparison.Ordinal);
                    if (fragmentSafetyStop)
                    {
                        QDUI.WarnBox(
                            "SAFETY STOP — BAKE NOT APPLIED\n" + error);
                    }
                    else
                    {
                        QDUI.ErrorBox(error);
                    }
                }
                QDUI.EndCard();
            }

            DrawManifestList();
        }

        static void DrawReportMessages(IReadOnlyList<string> messages, string foldoutKey)
        {
            if (messages == null || messages.Count == 0) return;

            var notes = new List<string>();
            var cautions = new List<string>();
            var technical = new List<string>();
            for (int i = 0; i < messages.Count; i++)
            {
                string message = messages[i];
                if (string.IsNullOrWhiteSpace(message)) continue;
                technical.Add(message);
                string friendly = FriendlyReportMessage(message);
                if (IsActionableReportMessage(message)) cautions.Add(friendly);
                else notes.Add(friendly);
            }

            if (notes.Count > 0)
                QDUI.InfoBox("Automatic mesh preparation\n- " +
                    string.Join("\n- ", notes));
            if (cautions.Count > 0)
                QDUI.WarnBox("Geometry review recommended\n- " +
                    string.Join("\n- ", cautions));

            if (technical.Count > 0)
            {
                QDUI.HelpFoldout(foldoutKey,
                    $"Technical details ({technical.Count})",
                    string.Join("\n\n", technical));
            }
        }

        static bool IsActionableReportMessage(string message)
        {
            string lower = message.ToLowerInvariant();
            return lower.Contains("non-manifold boundary") ||
                   lower.Contains("probably open or self-intersecting") ||
                   lower.Contains("fewer than 2 valid fragments") ||
                   lower.Contains("could not produce a valid second level") ||
                   lower.Contains("enabled, but no simulated primary fragment") ||
                   lower.Contains("more than 160 child pieces") ||
                   lower.Contains("level-two pieces. use this only");
        }

        static string FriendlyReportMessage(string message)
        {
            string count = LeadingNumber(message);
            string lower = message.ToLowerInvariant();
            if (lower.StartsWith("auto axis was resolved independently"))
                return $"Auto Axis aligned {count} source parts independently.";
            if (lower.StartsWith("kept ") && lower.Contains("nested surface islands"))
                return "The inner and outer shell surfaces were kept together.";
            if (lower.StartsWith("removed ") && lower.Contains("coincident cut contour"))
                return $"Removed {count} overlapping cut contours.";
            if (lower.Contains("cut segment(s) could not be chained"))
                return $"Closed {count} small numerical seams on cut surfaces.";
            if (lower.Contains("hollow cut cap(s) required"))
                return $"Repaired {count} hollow-shell cut caps.";
            if (lower.StartsWith("filled ") && lower.Contains("residual cut hole"))
                return $"Filled {count} small remaining cut holes.";
            if (lower.Contains("cut result(s) retained a non-manifold boundary"))
                return $"{count} fragments may still contain an open or non-manifold cut edge.";
            if (lower.StartsWith("intact collision uses ") &&
                lower.Contains("compound convex parts"))
                return $"Built {count} collider parts for the disconnected source elements.";
            if (lower.StartsWith("separated ") &&
                lower.Contains("disconnected mesh island"))
                return $"Separated {count} loose mesh islands into independent fragments.";
            return message;
        }

        static string LeadingNumber(string message)
        {
            if (string.IsNullOrEmpty(message)) return "the";
            int start = 0;
            while (start < message.Length && !char.IsDigit(message[start])) start++;
            int end = start;
            while (end < message.Length && char.IsDigit(message[end])) end++;
            return end > start ? message.Substring(start, end - start) : "the";
        }

        void DrawManifestList()
        {
            var manifests = BakePipeline.FindAllManifests();
            if (manifests.Count == 0) return;

            QDUI.BeginCard();
            QDUI.Header("Generated Assets");
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(
                $"{manifests.Count} bake set(s). Clean removes only manifest-owned assets; " +
                "source files and Quick Profiles are preserved.", QDUI.HelpText);
            GUILayout.FlexibleSpace();
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            if (GUILayout.Button("CLEAN ALL...", QDUI.GhostBtn, GUILayout.Width(104f)))
            {
                bool confirmed = EditorUtility.DisplayDialog(
                    "Clean All Generated Assets",
                    $"Delete all {manifests.Count} generated Quick Destruction bake set(s)?\n\n" +
                    "This removes only prefabs, embedded fragment meshes, generated materials " +
                    "and manifests owned by those bake sets. Source models, user materials, " +
                    "named profiles and generated Quick Profiles are kept.\n\n" +
                    "Generated scene instances are removed and their linked source objects " +
                    "are reactivated. The whole operation is registered in Undo.",
                    "Clean All", "Cancel");
                if (confirmed)
                {
                    EndFracturePreviews();
                    int removed = BakePipeline.CleanAllGenerated(
                        out int bakeSets, out int sceneInstances, out int sources);
                    m_lastBake = null;
                    ShowStatus(
                        $"Clean All removed {removed} assets and {sceneInstances} scene " +
                        $"instance(s), then reactivated {sources} source object(s) across " +
                        $"{bakeSets} bake set(s).");
                    GUIUtility.ExitGUI();
                }
            }
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(4f);
            foreach (var manifest in manifests)
            {
                string prefabPath = null;
                foreach (var entry in manifest.Entries)
                    if (entry.Kind == "prefab") { prefabPath = entry.AssetPath; break; }
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label($"{manifest.SourceName}  <color=#5C5C5C>({manifest.ProfileName}, " +
                                $"{manifest.FragmentCount} primary + " +
                                $"{manifest.SecondaryFragmentCount} level-two, " +
                                $"{manifest.BakeTimestampUtc} UTC)</color>",
                    QDUI.Body);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Ping", QDUI.GhostBtn, GUILayout.Width(52f)))
                    EditorGUIUtility.PingObject(manifest);
                using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(prefabPath)))
                {
                    if (GUILayout.Button("Open", QDUI.GhostBtn, GUILayout.Width(52f)))
                    {
                        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                        if (prefab != null) AssetDatabase.OpenAsset(prefab);
                    }
                }
                using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
                if (GUILayout.Button("Clean", QDUI.GhostBtn, GUILayout.Width(56f)))
                {
                    if (EditorUtility.DisplayDialog("Clean Generated",
                        $"Delete all assets baked for '{manifest.SourceName}' ({manifest.ProfileName})?\n\n" +
                        "The generated scene instance will be removed and its linked source " +
                        "object reactivated. Only manifest-owned files are deleted.",
                        "Delete", "Cancel"))
                    {
                        int removed = BakePipeline.CleanGenerated(manifest,
                            out int sceneInstances, out int sources);
                        ShowStatus($"Removed {removed} generated assets and " +
                                   $"{sceneInstances} scene instance(s); reactivated " +
                                   $"{sources} source object(s).");
                        GUIUtility.ExitGUI();
                    }
                }
                EditorGUILayout.EndHorizontal();
                GUILayout.Space(2f);
            }
            QDUI.EndCard();
        }

        bool CanBake()
        {
            if (m_source == null || m_profile == null) return false;
            if (m_report == null) RunAnalysis();
            return m_report != null && m_report.Status != AnalysisSeverity.Blocked;
        }

        void RunBake()
        {
            RestoreAssignedRebakeProfile();
            m_sourceCandidateCache = null;
            // Last line of defence. Selection adoption already restores the mode, but a bake
            // must never be the thing that discovers it was in the wrong one.
            RestoreWholeBuildingModeIfGenerated();
            if (IsWholeBuildingMode)
            {
                RunWholeBuildingBake();
                return;
            }

            // Root + Nested Meshes exists to turn one hierarchy into one destructible per
            // nested mesh, and the source list says so: "Bake will process all N selected
            // mesh objects". Baking m_source alone honoured neither - a root with several
            // mesh children produced a single destructible and silently left every other
            // child untouched - so a resolved batch runs the same loop One-Click runs.
            List<GameObject> batchSources = GetSourceCandidates();
            if (batchSources.Count > 1 ||
                (batchSources.Count == 1 && m_sourceSelectionMode == SourceSelectionMode.RootHierarchy))
            {
                if (!PrepareBatchProfile("Bake")) return;
                RunSourceBatchBake(batchSources, "Bake");
                return;
            }

            // Re-read the source immediately before the confirmation so a stale Analyze
            // card can never suppress the warning after the mesh changed.
            RunAnalysis();
            if (!TryConfirmUnsafeFractureInput(
                    m_report, m_profile, "BAKE", out bool allowUnsafeFractureInput))
            {
                ShowStatus("Bake cancelled. No generated assets were changed.");
                return;
            }

            EndFracturePreviews();
            GameObject bakedSource = m_source;
            m_isBakingSelection = true;
            try
            {
                if (IsInMemoryProfile(m_profile))
                {
                    if (!SaveInMemoryProfileToAsset())
                    {
                        ShowStatus("Bake cancelled. No profile or generated asset was changed.");
                        return;
                    }
                }
                else if (m_profile == null)
                {
                    CreateOrLoadQuickProfile();
                }
                m_lastBake = BakePipeline.Bake(m_source, m_profile, m_placeInScene,
                    allowUnsafeFractureInput);
            }
            finally
            {
                m_isBakingSelection = false;
                m_ignoreSelectionUntil = EditorApplication.timeSinceStartup + 0.5d;
                m_source = bakedSource;
            }
            if (m_lastBake.Success)
            {
                m_geometryDirty = false;
                ShowStatus($"Baked {m_lastBake.FragmentCount} primary + " +
                           $"{m_lastBake.SecondaryFragmentCount} level-two pieces in " +
                           $"{m_lastBake.Seconds:0.0}s. " +
                           "Enter Play Mode and use Play Preview to test.");
            }
            else
            {
                // A failed bake produced nothing the user can see, so the reason cannot live
                // only in a status line that clears after six seconds — that combination is
                // what "I pressed Bake and nothing happened" actually is.
                ShowStatus(m_lastBake.Error);
                EditorUtility.DisplayDialog("Quick Destruction - bake failed",
                    string.IsNullOrEmpty(m_lastBake.Error)
                        ? "The bake produced no usable output. See the Console for details."
                        : m_lastBake.Error,
                    "OK");
            }
            m_workspace = Workspace.Bake;
        }

        // ------------------------------------------------------------------ play preview

        DestructibleRoot FindPreviewTarget()
        {
            GameObject selected = Selection.activeGameObject;
            if (selected != null)
            {
                if (!selected.scene.IsValid())
                {
                    // A prefab asset picked in the Project window cannot receive
                    // play preview hits; warn once per selection instead.
                    if (m_prefabAssetWarningId != selected.GetHashCode())
                    {
                        m_prefabAssetWarningId = selected.GetHashCode();
                        ShowStatus("Selection is a prefab asset. Select a scene instance for Play Preview.");
                    }
                }
                else
                {
                    var fromSelection = selected.GetComponentInParent<DestructibleRoot>();
                    if (fromSelection != null) return fromSelection;
                }
            }
            // Last-resort fallback when the selection yields nothing. Which root comes back
            // is unspecified either way, so the cheaper unordered lookup is the right one.
#if UNITY_2023_1_OR_NEWER
            return FindAnyObjectByType<DestructibleRoot>();
#else
            return UnityEngine.Object.FindObjectOfType<DestructibleRoot>();
#endif
        }

        void PreviewImpact()
        {
            var target = FindPreviewTarget();
            if (target == null) return;
            Renderer renderer = target.IntactRoot != null
                ? target.IntactRoot.GetComponentInChildren<Renderer>()
                : target.GetComponentInChildren<Renderer>();
            Bounds bounds = renderer != null
                ? renderer.bounds
                : new Bounds(target.transform.position, Vector3.one);

            Camera sceneCamera = SceneView.lastActiveSceneView != null
                ? SceneView.lastActiveSceneView.camera
                : null;
            Vector3 direction = sceneCamera != null
                ? bounds.center - sceneCamera.transform.position
                : Vector3.forward;
            if (direction.sqrMagnitude < 1e-5f) direction = Vector3.forward;
            direction.Normalize();

            Vector3 extents = bounds.extents;
            float projectedExtent = Mathf.Abs(direction.x) * extents.x +
                                    Mathf.Abs(direction.y) * extents.y +
                                    Mathf.Abs(direction.z) * extents.z;
            Vector3 point = bounds.center - direction * (projectedExtent + 0.02f);
            float largestDimension = Mathf.Max(bounds.size.x,
                Mathf.Max(bounds.size.y, bounds.size.z));
            float radius = Mathf.Max(0.15f, largestDimension * 0.55f);
            float damage = target.Profile != null
                ? Mathf.Max(1f, target.Profile.Health * target.HealthNormalized + 1f)
                : 1000f;
            float impactEnergy = target.Profile != null
                ? Mathf.Max(1f, target.Profile.DefaultExplosionForce * 0.35f)
                : 10f;
            DamageResult result = target.ApplyDamage(new DamageContext(
                damage, point, direction, radius, 0, 0, impactEnergy));
            ShowStatus(
                $"Local impact on {target.name}: {result.ActivatedFragments} nearby fragment(s) " +
                $"activated with directional force (state: {target.State}).");
        }

        void PreviewExplosion()
        {
            var target = FindPreviewTarget();
            if (target == null) return;
            Renderer renderer = target.IntactRoot != null
                ? target.IntactRoot.GetComponentInChildren<Renderer>()
                : target.GetComponentInChildren<Renderer>();
            Vector3 center = renderer != null
                ? renderer.bounds.center
                : target.transform.position;
            target.Explode(center);
            int remaining = Mathf.Max(0,
                target.Fragments.Count - target.ActiveFragmentCount);
            ShowStatus(
                $"Radial explosion on {target.name}: {target.ActiveFragmentCount}/" +
                $"{target.Fragments.Count} fragments active, {remaining} remain in the shell. " +
                "Use Break All for complete destruction.");
        }

        void PreviewBreakAll()
        {
            var target = FindPreviewTarget();
            if (target == null) return;
            Renderer renderer = target.IntactRoot != null
                ? target.IntactRoot.GetComponentInChildren<Renderer>()
                : target.GetComponentInChildren<Renderer>();
            Vector3 center = renderer != null
                ? renderer.bounds.center
                : target.transform.position;
            float energy = target.Profile != null
                ? target.Profile.DefaultExplosionForce
                : 30f;
            target.BreakNow(new BreakContext(center, Vector3.up, 0f, energy));
            ShowStatus(
                $"Full break on {target.name}: all available baked fragments activated.");
        }

        void PreviewSecondaryHit()
        {
            var target = FindPreviewTarget();
            if (target == null) return;

            for (int i = 0; i < target.Fragments.Count; i++)
            {
                FragmentRecord fragment = target.Fragments[i];
                if (fragment?.SecondaryNode == null ||
                    !target.CanSecondaryBreak(i))
                    continue;

                float damage = target.Profile != null
                    ? target.Profile.SecondaryStrength : 35f;
                Vector3 point = fragment.Transform != null
                    ? fragment.Transform.position : target.transform.position;
                fragment.SecondaryNode.ApplyImpactDamage(
                    damage, point, Vector3.up);
                ShowStatus(
                    $"Level-two hit sent to fragment {i} of {target.name}.");
                return;
            }

            ShowStatus(
                "No active level-two parent found. Break the intact object first, then retry.");
        }

        void PreviewStress()
        {
#if UNITY_6000_4_OR_NEWER
            var roots = FindObjectsByType<DestructibleRoot>();
#elif UNITY_2023_1_OR_NEWER
            var roots = FindObjectsByType<DestructibleRoot>(FindObjectsSortMode.None);
#else
            var roots = UnityEngine.Object.FindObjectsOfType<DestructibleRoot>();
#endif
            int count = 0;
            foreach (var root in roots)
            {
                if (count >= 10) break;
                if (root.State >= DestructibleState.Fractured) continue;
                root.Explode(root.transform.position);
                count++;
            }
            ShowStatus($"Stress test: broke {count} destructibles at once.");
        }

        void PreviewReset()
        {
#if UNITY_6000_4_OR_NEWER
            var roots = FindObjectsByType<DestructibleRoot>();
#elif UNITY_2023_1_OR_NEWER
            var roots = FindObjectsByType<DestructibleRoot>(FindObjectsSortMode.None);
#else
            var roots = UnityEngine.Object.FindObjectsOfType<DestructibleRoot>();
#endif
            foreach (var root in roots)
                root.ResetDestructible(ResetMode.Instant);
            ShowStatus($"Reset {roots.Length} destructibles - transforms, health and pools restored.");
        }

        // ------------------------------------------------------------------ status bar

        void ShowStatus(string message, double durationSeconds = 6d)
        {
            m_statusMessage = message;
            m_statusUntil = EditorApplication.timeSinceStartup +
                            Mathf.Max(1f, (float)durationSeconds);
            Repaint();
        }

        void DrawStatusBar()
        {
            if (string.IsNullOrEmpty(m_statusMessage) ||
                EditorApplication.timeSinceStartup > m_statusUntil) return;
            var r = new Rect(0f, position.height - ActionFooterHeight - 22f,
                position.width, 22f);
            GUI.DrawTexture(r, QDUI.Tex(QDUI.FromHex("#0C0C0C")), ScaleMode.StretchToFill);
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 1f), QDUI.Line);
            GUI.Label(new Rect(r.x + 10f, r.y + 3f, r.width - 20f, 16f), m_statusMessage, QDUI.BodyDim);
        }
    }
}
