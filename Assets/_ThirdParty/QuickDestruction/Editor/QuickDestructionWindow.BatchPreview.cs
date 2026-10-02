using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace RipVertices.QuickDestruction.Editor
{
    public partial class QuickDestructionWindow
    {
        sealed class BatchPreviewEntry
        {
            public GameObject Source;
            public DestructionProfile Profile;
            public FracturePreviewSession Session;
            public bool AllowUnsafe;
            public bool Started;
            public bool Finished;
        }

        readonly List<BatchPreviewEntry> m_batchPreviewEntries =
            new List<BatchPreviewEntry>();
        List<GameObject> m_batchPreviewSources;
        int m_batchPreviewSignature;
        string m_batchPreviewError;
        bool m_batchPreviewCompletionReported;
        double m_batchPreviewStartedAt;
        double m_batchPreviewCompletedAt;

        bool HasBatchFracturePreview => m_batchPreviewEntries.Count > 0;

        bool IsFracturePreviewActive()
        {
            if (HasBatchFracturePreview)
            {
                for (int i = 0; i < m_batchPreviewEntries.Count; i++)
                    if (m_batchPreviewEntries[i].Session != null &&
                        m_batchPreviewEntries[i].Session.IsActive) return true;
                return false;
            }
            return m_fracturePreview != null && m_fracturePreview.IsActive;
        }

        bool IsFracturePreviewSolving()
        {
            if (HasBatchFracturePreview)
            {
                for (int i = 0; i < m_batchPreviewEntries.Count; i++)
                    if (!m_batchPreviewEntries[i].Finished) return true;
                return false;
            }
            return m_fracturePreview != null && m_fracturePreview.IsSolving;
        }

        float FracturePreviewSolveProgress()
        {
            if (!HasBatchFracturePreview)
                return m_fracturePreview != null ? m_fracturePreview.SolveProgress : 0f;
            float progress = 0f;
            for (int i = 0; i < m_batchPreviewEntries.Count; i++)
            {
                BatchPreviewEntry entry = m_batchPreviewEntries[i];
                progress += entry.Finished ? 1f :
                    entry.Session != null ? entry.Session.SolveProgress : 0f;
            }
            return progress / Mathf.Max(1, m_batchPreviewEntries.Count);
        }

        void CancelFracturePreviewSolve()
        {
            if (HasBatchFracturePreview)
            {
                EndFracturePreviews();
                return;
            }
            m_fracturePreview?.CancelSolve();
        }

        void EndFracturePreviews()
        {
            if (HasBatchFracturePreview)
            {
                foreach (BatchPreviewEntry entry in m_batchPreviewEntries)
                {
                    entry.Session?.End();
                    if (entry.Profile != null)
                        UnityEngine.Object.DestroyImmediate(entry.Profile);
                }
                m_batchPreviewEntries.Clear();
                m_batchPreviewSources = null;
                m_batchPreviewSignature = 0;
                m_batchPreviewError = null;
                m_batchPreviewCompletionReported = false;
                m_batchPreviewStartedAt = 0d;
                m_batchPreviewCompletedAt = 0d;
            }
            m_fracturePreview?.End();
        }

        void SetFracturePreviewPresentation(FracturePreviewMaterialMode mode,
            float explode, bool wireframe)
        {
            if (HasBatchFracturePreview)
            {
                foreach (BatchPreviewEntry entry in m_batchPreviewEntries)
                    if (entry.Session != null && entry.Session.IsActive)
                        entry.Session.SetPresentation(mode, explode, wireframe);
                return;
            }
            m_fracturePreview?.SetPresentation(mode, explode, wireframe);
        }

        void SetFracturePreviewMaterialMode(FracturePreviewMaterialMode mode)
        {
            if (HasBatchFracturePreview)
            {
                foreach (BatchPreviewEntry entry in m_batchPreviewEntries)
                    if (entry.Session != null && entry.Session.IsActive)
                        entry.Session.SetMaterialMode(mode);
                return;
            }
            m_fracturePreview?.SetMaterialMode(mode);
        }

        void SetFracturePreviewExplode(float explode)
        {
            if (HasBatchFracturePreview)
            {
                foreach (BatchPreviewEntry entry in m_batchPreviewEntries)
                    if (entry.Session != null && entry.Session.IsActive)
                        entry.Session.SetExplode(explode);
                return;
            }
            m_fracturePreview?.SetExplode(explode);
        }

        void SetFracturePreviewWireframe(bool enabled)
        {
            if (HasBatchFracturePreview)
            {
                foreach (BatchPreviewEntry entry in m_batchPreviewEntries)
                    if (entry.Session != null && entry.Session.IsActive)
                        entry.Session.SetWireframe(enabled);
                return;
            }
            m_fracturePreview?.SetWireframe(enabled);
        }

        int ComputeBatchPreviewSignature(IReadOnlyList<GameObject> sources)
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + (m_profile != null
                    ? EditorJsonUtility.ToJson(m_profile).GetHashCode() : 0);
                hash = hash * 31 + GetBatchPartsPerMesh(sources);
                hash = hash * 31 + (int)m_batchPanelChoice;
                hash = hash * 31 + m_batchPanelThickness.GetHashCode();
                for (int i = 0; i < sources.Count; i++)
                {
                    GameObject source = sources[i];
                    MeshFilter filter = source != null
                        ? source.GetComponentInChildren<MeshFilter>() : null;
#if UNITY_6000_4_OR_NEWER
                    int sourceId = source != null ? source.GetEntityId().GetHashCode() : 0;
#else
                    int sourceId = source != null ? source.GetInstanceID() : 0;
#endif
                    hash = hash * 31 + sourceId;
                    if (filter != null && filter.sharedMesh != null && m_profile != null)
                        hash = hash * 31 + BakePipeline.ComputeFractureHash(
                            filter.sharedMesh, m_profile, filter.transform.lossyScale);
                }
                return hash;
            }
        }

        bool IsFracturePreviewStale(GameObject fractureSource)
        {
            if (!HasBatchFracturePreview)
                return m_fracturePreview != null && m_fracturePreview.IsActive &&
                    m_fracturePreview.IsStale(
                        fractureSource, m_profile, IsWholeBuildingMode);
            List<GameObject> sources = GetSourceCandidates();
            return !SameBatchSources(m_batchPreviewSources, sources) ||
                   m_batchPreviewSignature != ComputeBatchPreviewSignature(sources);
        }

        bool TryStartRootBatchPreview(bool showFailureDialog)
        {
            if (m_sourceSelectionMode != SourceSelectionMode.RootHierarchy ||
                IsWholeBuildingMode) return false;
            List<GameObject> sources = GetSourceCandidates();
            if (sources.Count == 0) return false;

            bool containsFlatSource = false;
            var reports = new List<AnalysisReport>(sources.Count);
            foreach (GameObject source in sources)
            {
                AnalysisReport report = MeshAnalyzer.Analyze(source);
                reports.Add(report);
                if (report == null || report.Status == AnalysisSeverity.Blocked)
                {
                    ReportPreviewFailure(
                        $"Preview stopped because '{source.name}' has an Analyze blocker.",
                        showFailureDialog);
                    return true;
                }
                containsFlatSource |= report.IsFlatSource;
            }

            if (containsFlatSource && (m_profile == null || !m_profile.Solidify) &&
                !HasBatchPanelChoice(sources))
            {
                QueueBatchPanelPrompt(true);
                ReportPreviewFailure(
                    "Choose Wall Thickness for the flat panels, then generate the preview again.",
                    false);
                return true;
            }

            EndFracturePreviews();
            m_fracturePreview ??= new FracturePreviewSession();
            int partsPerMesh = GetBatchPartsPerMesh(sources);
            bool shellChoice = HasBatchPanelChoice(sources) &&
                m_batchPanelChoice == HollowShellBakeChoice.UseHollowShell;
            bool fullSolidChoice = HasBatchPanelChoice(sources) &&
                m_batchPanelChoice == HollowShellBakeChoice.BakeFullSolidAnyway;

            for (int i = 0; i < sources.Count; i++)
            {
                GameObject source = sources[i];
                AnalysisReport report = reports[i];
                var previewProfile = Instantiate(m_profile);
                previewProfile.hideFlags = HideFlags.HideAndDontSave;
                previewProfile.FragmentCount = partsPerMesh;
                bool flat = report.IsFlatSource;
                if (flat && shellChoice)
                {
                    previewProfile.Solidify = true;
                    previewProfile.SolidifyThickness = m_batchPanelThickness;
                }
                else if (flat && fullSolidChoice)
                {
                    previewProfile.Solidify = false;
                }

                bool allowUnsafe = flat && fullSolidChoice;
                var session = i == 0
                    ? m_fracturePreview : new FracturePreviewSession();
                var entry = new BatchPreviewEntry
                {
                    Source = source,
                    Profile = previewProfile,
                    Session = session,
                    AllowUnsafe = allowUnsafe
                };
                m_batchPreviewEntries.Add(entry);
            }

            m_batchPreviewSources = new List<GameObject>(sources);
            m_batchPreviewSignature = ComputeBatchPreviewSignature(sources);
            m_batchPreviewError = null;
            m_batchPreviewCompletionReported = false;
            m_batchPreviewStartedAt = EditorApplication.timeSinceStartup;
            m_batchPreviewCompletedAt = 0d;
            if (!StartNextBatchPreviewEntry(out string startError))
            {
                EndFracturePreviews();
                ReportPreviewFailure(startError, showFailureDialog);
                return true;
            }
            m_nextSolveRepaintAt = 0d;
            ShowStatus(
                $"Generating preview for all {sources.Count} mesh objects " +
                $"({partsPerMesh} parts per mesh) in the background.");
            return true;
        }

        bool StartNextBatchPreviewEntry(out string error)
        {
            error = null;
            foreach (BatchPreviewEntry entry in m_batchPreviewEntries)
            {
                if (entry.Started || entry.Finished) continue;
                entry.Started = true;
                if (entry.Session.StartBuild(entry.Source, entry.Profile,
                        out string entryError, entry.AllowUnsafe,
                        new[] { entry.Source }, false)) return true;
                entry.Finished = true;
                error = $"Preview failed for '{entry.Source.name}'.\n{entryError}";
                return false;
            }
            return false;
        }

        bool ProcessBatchFracturePreview()
        {
            if (!HasBatchFracturePreview) return false;
            bool changed = false;
            string firstError = null;
            bool allFinished = true;
            foreach (BatchPreviewEntry entry in m_batchPreviewEntries)
            {
                if (entry.Finished) continue;
                if (!entry.Started)
                {
                    allFinished = false;
                    continue;
                }
                if (!entry.Session.HasSolvedResult)
                {
                    allFinished = false;
                    continue;
                }
                if (!entry.Session.FinishBuild(out string error) &&
                    !string.IsNullOrEmpty(error) && firstError == null)
                    firstError = $"Preview failed for '{entry.Source.name}'.\n{error}";
                entry.Finished = true;
                changed = true;
            }

            bool hasPendingEntry = false;
            bool hasRunningEntry = false;
            foreach (BatchPreviewEntry entry in m_batchPreviewEntries)
            {
                hasPendingEntry |= !entry.Started && !entry.Finished;
                hasRunningEntry |= entry.Started && !entry.Finished;
            }
            if (hasPendingEntry && !hasRunningEntry)
            {
                if (!StartNextBatchPreviewEntry(out string startError))
                {
                    m_batchPreviewError ??= startError;
                    if (firstError == null) firstError = startError;
                }
                changed = true;
            }

            if (firstError != null)
            {
                m_batchPreviewError ??= firstError;
                bool showDialog = m_showPreviewFailureDialog;
                m_showPreviewFailureDialog = false;
                ReportPreviewFailure(firstError, showDialog);
            }
            allFinished = true;
            foreach (BatchPreviewEntry entry in m_batchPreviewEntries)
                allFinished &= entry.Finished;
            if (allFinished && !m_batchPreviewCompletionReported)
            {
                m_batchPreviewCompletionReported = true;
                m_batchPreviewCompletedAt = EditorApplication.timeSinceStartup;
                m_previewError = m_batchPreviewError;
                m_showPreviewFailureDialog = false;
                if (m_previewExplode < 0.01f)
                    m_previewExplode = m_profile != null &&
                        m_profile.SecondaryBreakup != SecondaryBreakupLevel.Off
                        ? 0.2f : 0.08f;
                SetFracturePreviewPresentation(
                    m_previewMaterialMode, m_previewExplode, m_previewWireframe);
                if (m_batchPreviewError == null)
                    ShowStatus(
                        $"Preview generated for {m_batchPreviewEntries.Count} objects: " +
                        $"{BatchPreviewFragmentCount()} fragments, " +
                        $"{BatchPreviewBodyCount()} estimated rigidbodies " +
                        $"({BatchPreviewBuildSeconds():0.0}s total solve time).");
            }
            return changed;
        }

        int BatchPreviewFragmentCount()
        {
            int value = 0;
            foreach (BatchPreviewEntry entry in m_batchPreviewEntries)
                value += entry.Session?.FragmentCount ?? 0;
            return value;
        }

        int BatchPreviewTriangleCount()
        {
            int value = 0;
            foreach (BatchPreviewEntry entry in m_batchPreviewEntries)
                value += entry.Session?.TotalTriangles ?? 0;
            return value;
        }

        int BatchPreviewBodyCount()
        {
            int value = 0;
            foreach (BatchPreviewEntry entry in m_batchPreviewEntries)
                value += entry.Session?.EstimatedBodyCount ?? 0;
            return value;
        }

        float BatchPreviewMass()
        {
            float value = 0f;
            foreach (BatchPreviewEntry entry in m_batchPreviewEntries)
                value += entry.Session?.EstimatedMass ?? 0f;
            return value;
        }

        float BatchPreviewBuildSeconds()
        {
            if (m_batchPreviewStartedAt <= 0d) return 0f;
            double end = m_batchPreviewCompletedAt > 0d
                ? m_batchPreviewCompletedAt : EditorApplication.timeSinceStartup;
            return (float)Math.Max(0d, end - m_batchPreviewStartedAt);
        }

        int BatchPreviewSecondaryParents()
        {
            int value = 0;
            foreach (BatchPreviewEntry entry in m_batchPreviewEntries)
                value += entry.Session?.SecondaryEligiblePrimaryCount ?? 0;
            return value;
        }

        int BatchPreviewSecondaryFragments()
        {
            int value = 0;
            foreach (BatchPreviewEntry entry in m_batchPreviewEntries)
                value += entry.Session?.EstimatedSecondaryFragmentCount ?? 0;
            return value;
        }

        int BatchPreviewSecondaryBodies()
        {
            int value = 0;
            foreach (BatchPreviewEntry entry in m_batchPreviewEntries)
                value += entry.Session?.EstimatedSecondaryBodyCount ?? 0;
            return value;
        }

        List<string> BatchPreviewWarnings()
        {
            var warnings = new List<string>();
            foreach (BatchPreviewEntry entry in m_batchPreviewEntries)
                foreach (string warning in entry.Session.Warnings)
                    if (!warnings.Contains(warning)) warnings.Add(warning);
            return warnings;
        }
    }
}
