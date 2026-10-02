using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace RipVertices.QuickDestruction.Editor
{
    public partial class QuickDestructionWindow
    {
        List<GameObject> m_batchPanelSources;
        HollowShellBakeChoice m_batchPanelChoice;
        float m_batchPanelThickness;
        bool m_batchPanelShowIntact;
        bool m_batchPanelPromptQueued;
        List<GameObject> m_batchPartsSources;
        DestructionProfile m_batchPartsProfile;
        int m_batchPartsAuthoredCount;
        int m_batchPartsPerMesh;

        const int BatchPrimaryPartBudget = 240;
        const int BatchMinRecommendedPartsPerMesh = 16;
        const int BatchMaxRecommendedPartsPerMesh = 40;

        void SetSourceSelectionMode(SourceSelectionMode mode)
        {
            ReleaseCombinedSource();
            m_sourceSelectionMode = mode;
            RefreshSourceFromCachedSelection();
            m_report = m_autoAnalyze && m_source != null
                ? AnalyzeCurrentSource(true) : null;
            int count = IsWholeBuildingMode
                ? CollectWholeBuildingMeshObjects(GetWholeBuildingRoots()).Count
                : GetSourceCandidates().Count;
            ShowStatus(mode == SourceSelectionMode.RootHierarchy
                ? $"Root Hierarchy mode found {count} nested mesh object(s)."
                : IsWholeBuildingMode
                    ? $"Whole Building mode will merge {count} modular mesh(es) as one source."
                    : $"Selected Objects mode found {count} usable source object(s).");
            if (mode == SourceSelectionMode.RootHierarchy)
                QueueBatchPanelPrompt();
        }

        static bool SameBatchSources(IReadOnlyList<GameObject> first,
            IReadOnlyList<GameObject> second)
        {
            if (first == null || second == null || first.Count != second.Count)
                return false;
            for (int i = 0; i < first.Count; i++)
                if (first[i] == null || first[i] != second[i]) return false;
            return true;
        }

        bool HasBatchPanelChoice(IReadOnlyList<GameObject> sources)
        {
            return m_batchPanelChoice != HollowShellBakeChoice.Cancel &&
                   SameBatchSources(m_batchPanelSources, sources);
        }

        void RememberBatchPanelChoice(IReadOnlyList<GameObject> sources,
            HollowShellBakeChoice choice, float thickness, bool showIntact)
        {
            if (choice == HollowShellBakeChoice.Cancel) return;
            m_batchPanelSources = new List<GameObject>(sources);
            m_batchPanelChoice = choice;
            m_batchPanelThickness = Mathf.Clamp(thickness,
                HollowShellThicknessPrompt.MinThicknessMeters,
                HollowShellThicknessPrompt.MaxThicknessMeters);
            m_batchPanelShowIntact = showIntact;
            m_geometryDirty = true;
        }

        void QueueBatchPanelPrompt(bool force = false)
        {
            if (Application.isBatchMode || Application.isPlaying ||
                EditorApplication.isPlayingOrWillChangePlaymode ||
                m_isBakingSelection || m_batchPanelPromptQueued) return;
            var sources = new List<GameObject>(GetSourceCandidates());
            if (sources.Count == 0 || (!force && HasBatchPanelChoice(sources))) return;
            m_batchPanelPromptQueued = true;
            // Run after the current IMGUI event; there is no progress window to cover
            // this dialog, and switching selection while queued invalidates the request.
            EditorApplication.delayCall += () =>
            {
                if (this == null) return;
                m_batchPanelPromptQueued = false;
                if (Application.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode ||
                    m_isBakingSelection || m_sourceSelectionMode != SourceSelectionMode.RootHierarchy ||
                    !SameBatchSources(sources, GetSourceCandidates())) return;
                bool hasFlatPanel = false;
                foreach (GameObject source in sources)
                {
                    AnalysisReport report = MeshAnalyzer.Analyze(source);
                    if (report != null && report.Status != AnalysisSeverity.Blocked && report.IsFlatSource)
                    {
                        hasFlatPanel = true;
                        break;
                    }
                }
                if (!hasFlatPanel) return;
                bool hasChoice = HasBatchPanelChoice(sources);
                float thickness = hasChoice ? m_batchPanelThickness
                    : m_profile != null ? m_profile.SolidifyThickness : 0.08f;
                bool showIntact = hasChoice ? m_batchPanelShowIntact : m_wholeBuildingShowShellWhileIntact;
                var owner = FindSelectedRootFor(sources[0]);
                HollowShellBakeChoice choice = HollowShellThicknessPrompt.Show(
                    owner != null ? owner.name : sources[0].name,
                    thickness, showIntact, m_profile == null || !m_profile.Solidify,
                    out float chosenThickness, out bool chosenIntact,
                    bakeImmediately: false);
                if (choice == HollowShellBakeChoice.Cancel) return;
                RememberBatchPanelChoice(sources, choice, chosenThickness, chosenIntact);
                ShowStatus(choice == HollowShellBakeChoice.UseHollowShell
                    ? $"Flat panels will bake with {m_batchPanelThickness * 100f:0.##} cm wall thickness. Closed meshes keep the profile's settings."
                    : "Full Solid was kept for flat panels; their bake may have no usable fragments.", 8d);
                Repaint();
            };
        }

        internal static string BatchPartsSummary(int sourceCount, int partsPerMesh,
            bool secondary)
        {
            long total = (long)Mathf.Max(0, sourceCount) * Mathf.Max(0, partsPerMesh);
            return $"Max Parts is per mesh: {partsPerMesh} × {sourceCount} = up to {total} primary parts across this batch. " +
                   (secondary ? "Level Two adds further fragments and bake time. " : string.Empty) +
                   "Use Batch Parts / Mesh in Root mode, or lower Max Parts in Fracture settings, for a lighter bake.";
        }

        internal static int RecommendedBatchPartsPerMesh(int sourceCount,
            int authoredParts)
        {
            int safeSourceCount = Mathf.Max(1, sourceCount);
            int adaptive = Mathf.Clamp(BatchPrimaryPartBudget / safeSourceCount,
                BatchMinRecommendedPartsPerMesh, BatchMaxRecommendedPartsPerMesh);
            return Mathf.Clamp(Mathf.Min(Mathf.Max(2, authoredParts), adaptive),
                2, MeshAnalyzer.MaxSafePrimaryFragmentCount);
        }

        void EnsureBatchParts(IReadOnlyList<GameObject> sources)
        {
            if (m_profile == null || sources == null || sources.Count == 0) return;
            int authoredCount = Mathf.Max(2, m_profile.FragmentCount);
            if (m_batchPartsProfile == m_profile &&
                m_batchPartsAuthoredCount == authoredCount &&
                SameBatchSources(m_batchPartsSources, sources)) return;

            m_batchPartsSources = new List<GameObject>(sources);
            m_batchPartsProfile = m_profile;
            m_batchPartsAuthoredCount = authoredCount;
            m_batchPartsPerMesh = RecommendedBatchPartsPerMesh(
                sources.Count, authoredCount);
        }

        int GetBatchPartsPerMesh(IReadOnlyList<GameObject> sources)
        {
            if (m_sourceSelectionMode != SourceSelectionMode.RootHierarchy ||
                sources == null || sources.Count == 0 || m_profile == null)
                return m_profile != null ? Mathf.Max(2, m_profile.FragmentCount) : 2;
            EnsureBatchParts(sources);
            return Mathf.Clamp(m_batchPartsPerMesh, 2,
                Mathf.Max(2, m_batchPartsAuthoredCount));
        }

        void DrawBatchPanelSettings(IReadOnlyList<GameObject> sources)
        {
            if (IsWholeBuildingMode || sources == null || sources.Count == 0) return;
            GUILayout.Space(4f);
            if (m_profile != null && sources.Count > 1)
            {
                int partsPerMesh = GetBatchPartsPerMesh(sources);
                if (m_sourceSelectionMode == SourceSelectionMode.RootHierarchy)
                {
                    EditorGUI.BeginChangeCheck();
                    partsPerMesh = QDUI.IntSliderRow("Batch Parts / Mesh", partsPerMesh,
                        2, Mathf.Max(2, m_batchPartsAuthoredCount));
                    if (EditorGUI.EndChangeCheck())
                    {
                        m_batchPartsPerMesh = partsPerMesh;
                        m_geometryDirty = true;
                        EndFracturePreviews();
                    }
                    GUILayout.Label(
                        $"This Root + Nested batch uses {partsPerMesh} parts per mesh; " +
                        $"the profile remains at {m_batchPartsAuthoredCount}.", QDUI.HelpText);
                }
                GUILayout.Label(BatchPartsSummary(sources.Count, partsPerMesh,
                    m_profile.SecondaryBreakup != SecondaryBreakupLevel.Off), QDUI.HelpText);
            }
            if (m_sourceSelectionMode != SourceSelectionMode.RootHierarchy) return;
            if (HasBatchPanelChoice(sources))
                GUILayout.Label(m_batchPanelChoice == HollowShellBakeChoice.UseHollowShell
                    ? $"Flat panels: HOLLOW SHELL, {m_batchPanelThickness * 100f:0.##} cm. Applied to this batch when baking."
                    : "Flat panels: FULL SOLID was explicitly selected.", QDUI.HelpText);
            if (GUILayout.Button("CONFIGURE FLAT PANEL THICKNESS", QDUI.GhostBtn))
                QueueBatchPanelPrompt(true);
        }
    }
}
