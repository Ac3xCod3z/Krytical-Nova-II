using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using RipVertices.QuickDestruction.Runtime;

namespace RipVertices.QuickDestruction.Editor
{
    internal enum FracturePreviewMaterialMode
    {
        Colorized,
        SourceAndInterior,
        InteriorCuts
    }

    /// <summary>
    /// Temporarily suppresses every renderer belonging to the preview source and restores
    /// each renderer's exact previous state afterwards. Keeping this separate from the
    /// generated preview hierarchy makes the edit-mode transaction safe for nested meshes,
    /// LODs and prefab instances.
    /// </summary>
    internal sealed class PreviewSourceVisibility
    {
        readonly List<Renderer> m_renderers = new List<Renderer>();
        readonly List<bool> m_forceRenderingOff = new List<bool>();

        internal int RendererCount => m_renderers.Count;

        internal void Hide(GameObject source)
        {
            Hide(source != null ? new[] { source } : null);
        }

        internal void Hide(IEnumerable<GameObject> sources)
        {
            Restore();
            if (sources == null) return;

            var seen = new HashSet<Renderer>();
            foreach (GameObject source in sources)
            {
                if (source == null) continue;
                Renderer[] renderers = source.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < renderers.Length; i++)
                {
                    Renderer renderer = renderers[i];
                    if (renderer == null || !seen.Add(renderer)) continue;
                    m_renderers.Add(renderer);
                    m_forceRenderingOff.Add(renderer.forceRenderingOff);
                    renderer.forceRenderingOff = true;
                }
            }
        }

        internal void EnsureHidden()
        {
            // Prefab/selection refreshes can rewrite transient renderer state while the
            // preview object itself survives. Reassert the preview invariant on SceneView
            // repaint so the intact mesh can never bleed through intermittently.
            for (int i = 0; i < m_renderers.Count; i++)
            {
                Renderer renderer = m_renderers[i];
                if (renderer != null) renderer.forceRenderingOff = true;
            }
        }

        internal void Restore()
        {
            int count = Mathf.Min(m_renderers.Count, m_forceRenderingOff.Count);
            for (int i = 0; i < count; i++)
            {
                Renderer renderer = m_renderers[i];
                if (renderer != null)
                    renderer.forceRenderingOff = m_forceRenderingOff[i];
            }
            m_renderers.Clear();
            m_forceRenderingOff.Clear();
        }
    }

    /// <summary>
    /// Temporary, non-destructive Edit Mode fracture preview. It runs the real fracture solver,
    /// keeps every generated Mesh in memory only and never writes assets or adds physics objects.
    /// </summary>
    internal sealed class FracturePreviewSession
    {
        // MeshSlicer owns static diagnostics. A cancelled preview can still be finishing
        // its current slice when the replacement starts, so all background preview solves
        // share one gate. The new job waits off the main thread and the attempt-level
        // cancellation check releases the gate promptly.
        static readonly object s_solverGate = new object();

        /// <summary>
        /// One background solver run. The worker thread only writes Progress and reads
        /// Cancel; every Unity object it captured is touched again exclusively by the
        /// main thread inside FinishBuild.
        /// </summary>
        sealed class SolveJob
        {
            public GameObject Source;
            public DestructionProfile Profile;
            public Task<FractureResult> Task;
            public Stopwatch Timer;
            public string SafetyNotice;
            public IReadOnlyList<GameObject> VisibilitySources;
            public bool MergedAssembly;
            public volatile bool Cancel;
            public volatile float Progress;
        }

        SolveJob m_job;
        readonly List<Mesh> m_meshes = new List<Mesh>();
        readonly List<Mesh> m_scatterMeshes = new List<Mesh>();
        readonly List<Transform> m_fragmentTransforms = new List<Transform>();
        readonly List<Vector3> m_basePositions = new List<Vector3>();
        readonly List<MeshRenderer> m_renderers = new List<MeshRenderer>();
        readonly List<MeshRenderer> m_scatterRenderers = new List<MeshRenderer>();
        readonly MaterialPropertyBlock m_propertyBlock = new MaterialPropertyBlock();
        readonly PreviewSourceVisibility m_sourceVisibility =
            new PreviewSourceVisibility();

        GameObject m_root;
        GameObject m_source;
        DestructionProfile m_profile;
        Mesh m_sourceMesh;
        MeshRenderer m_sourceRenderer;
        Material[] m_sourceMaterials;
        Material m_colorMaterial;
        Material m_neutralInteriorMaterial;
        Material m_interiorHighlightMaterial;
        Material m_wireMaterial;
        Mesh m_combinedWireMesh;
        Bounds m_localBounds;
        int m_materialHash;
        int m_scatterHash;
        bool m_mergedAssembly;

        // Last completed solve kept for instant refreshes. When neither the mesh nor
        // any fracture input changed, the preview is reassembled from this cache and
        // the background solve is skipped entirely. FinishBuild rebases the fragments
        // it receives, so the cache always stores a deep copy and hands out clones.
        FractureResult m_cachedSolve;
        int m_cachedSolveHash;

        public bool IsActive => m_root != null;
        public int InputHash { get; private set; }
        public int FragmentCount => m_meshes.Count;
        public int TotalTriangles { get; private set; }
        public int EstimatedBodyCount { get; private set; }
        public int SecondaryEligiblePrimaryCount { get; private set; }
        public int EstimatedSecondaryFragmentCount { get; private set; }
        public int EstimatedSecondaryBodyCount { get; private set; }
        public float EstimatedMass { get; private set; }
        public float BuildSeconds { get; private set; }
        public float Explode { get; private set; } = 0.08f;
        public FracturePreviewMaterialMode MaterialMode { get; private set; } =
            FracturePreviewMaterialMode.Colorized;

        internal static DestructionProfileData CreatePreviewSolverProfile(
            AnalysisReport report, DestructionProfile profile, Vector3 lossyScale,
            out string safetyNotice)
        {
            DestructionProfileData data = MeshAnalyzer.CreateSafeSolverProfile(
                report, profile, out safetyNotice);
            data = SolverProfileSpace.ForMeshTransform(data, lossyScale);
            data.IsPreview = true;
            return data;
        }
        public bool Wireframe { get; private set; } = true;
        public readonly List<string> Warnings = new List<string>();

        public bool IsSolving => m_job != null && !m_job.Task.IsCompleted;
        public bool HasSolvedResult => m_job != null && m_job.Task.IsCompleted;
        public float SolveProgress => m_job != null ? m_job.Progress : 0f;

        /// <summary>
        /// Abandons the in-flight background solve, if any. The worker aborts at its
        /// next progress callback; the current on-screen preview is left untouched.
        /// </summary>
        public void CancelSolve()
        {
            if (m_job != null) m_job.Cancel = true;
            m_job = null;
        }

        /// <summary>
        /// Validates the source, snapshots its geometry and launches the fracture solver
        /// on a background thread. The Editor stays responsive during the whole solve;
        /// poll <see cref="HasSolvedResult"/> and call <see cref="FinishBuild"/> from the
        /// main thread to assemble the preview. The previous preview stays visible until
        /// the new result replaces it. Starting a new build supersedes an in-flight one.
        /// </summary>
        public bool StartBuild(GameObject source, DestructionProfile profile,
            out string error, bool allowUnsafeOpenFullSolid = false,
            IReadOnlyList<GameObject> visibilitySources = null,
            bool mergedAssembly = false)
        {
            error = null;
            if (source == null || profile == null)
            {
                error = "Assign a source object and Destruction Profile first.";
                return false;
            }

            var filter = source.GetComponentInChildren<MeshFilter>();
            if (filter == null || filter.sharedMesh == null)
            {
                error = "The source has no valid MeshFilter.";
                return false;
            }

            AnalysisReport safetyReport = MeshAnalyzer.Analyze(source);
            if (safetyReport.Status == AnalysisSeverity.Blocked)
            {
                AnalysisItem blocker = safetyReport.Items.Find(
                    item => item != null && item.Severity == AnalysisSeverity.Blocked);
                error = blocker != null
                    ? $"{blocker.Code}: {blocker.Title}\nWhy: {blocker.Why}\nFix: {blocker.Fix}"
                    : "Analyze reports a blocker. Fix the source before previewing.";
                return false;
            }
            if (!MeshAnalyzer.ValidateClosedFractureInput(
                    safetyReport, profile, allowUnsafeOpenFullSolid,
                    out string fractureInputError))
            {
                error = fractureInputError;
                return false;
            }

            // Identical inputs produce an identical partition, so a hash match lets
            // FinishBuild reassemble the cached result instead of re-running the whole
            // background solve on every preview refresh.
            int inputHash = BakePipeline.ComputeFractureHash(
                filter.sharedMesh, profile, filter.transform.lossyScale);
            if (mergedAssembly) inputHash = unchecked(inputHash * 31 + 1);

            CancelSolve();

            if (m_cachedSolve != null && inputHash == m_cachedSolveHash)
            {
                var cachedJob = new SolveJob
                {
                    Source = source,
                    Profile = profile,
                    VisibilitySources = visibilitySources,
                    MergedAssembly = mergedAssembly,
                    Task = Task.FromResult(CloneFractureResult(m_cachedSolve)),
                    Timer = Stopwatch.StartNew()
                };
                m_job = cachedJob;
                return true;
            }

            Mesh solverMesh = ReadableMeshUtility.AcquireForFracture(
                filter.sharedMesh, profile.Solidify,
                out bool ownsSolverMesh, out string readableError);
            if (solverMesh == null)
            {
                error = $"Preview cannot read the source mesh. {readableError}";
                return false;
            }
            WorkMesh solverInput;
            try
            {
                solverInput = WorkMesh.FromMesh(solverMesh);
            }
            finally
            {
                ReadableMeshUtility.Release(solverMesh, ownsSolverMesh);
            }

            var profileData = CreatePreviewSolverProfile(safetyReport, profile,
                filter.transform.lossyScale, out string safetyNotice);
            profileData.MergeContactingSourceShells = mergedAssembly;

            var job = new SolveJob
            {
                Source = source,
                Profile = profile,
                SafetyNotice = safetyNotice,
                VisibilitySources = visibilitySources,
                MergedAssembly = mergedAssembly,
                Timer = Stopwatch.StartNew()
            };
            var tcs = new System.Threading.Tasks.TaskCompletionSource<FractureResult>();
            var thread = new System.Threading.Thread(() =>
            {
                try
                {
                    FractureResult res;
                    lock (s_solverGate)
                    {
                        if (job.Cancel)
                        {
                            tcs.SetResult(new FractureResult { Cancelled = true });
                            return;
                        }
                        res = FractureEngine.Fracture(solverInput, profileData, value =>
                        {
                            job.Progress = value;
                            return !job.Cancel;
                        });
                    }
                    tcs.SetResult(res);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            })
            {
                IsBackground = true,
                Priority = System.Threading.ThreadPriority.BelowNormal
            };
            thread.Start();
            Task<FractureResult> task = tcs.Task;
            job.Task = task;
            m_job = job;
            return true;
        }

        /// <summary>
        /// Consumes a finished background solve and builds the in-memory preview objects.
        /// Main thread only. Returns false with a null error when the solve was cancelled
        /// or superseded; the previous preview then remains active.
        /// </summary>
        public bool FinishBuild(out string error)
        {
            error = null;
            SolveJob job = m_job;
            if (job == null || !job.Task.IsCompleted) return false;
            m_job = null;

            if (job.Task.IsFaulted)
            {
                Exception exception = job.Task.Exception?.GetBaseException();
                error = $"Preview failed: {exception?.Message}";
                if (exception != null) UnityEngine.Debug.LogException(exception);
                return false;
            }

            FractureResult fracture = job.Task.Result;
            if (job.Cancel || fracture.Cancelled) return false;

            GameObject source = job.Source;
            DestructionProfile profile = job.Profile;
            if (source == null || profile == null)
            {
                error = "The preview source disappeared while the solver was running.";
                return false;
            }
            var filter = source.GetComponentInChildren<MeshFilter>();
            if (filter == null || filter.sharedMesh == null)
            {
                error = "The source has no valid MeshFilter.";
                return false;
            }
            if (fracture.Fragments.Count < 2)
            {
                AnalysisReport failureReport = MeshAnalyzer.Analyze(source);
                error = MeshAnalyzer.BuildFractureFailureMessage(
                    failureReport, fracture, profile);
                return false;
            }
            if (!MeshAnalyzer.ValidatePrimaryFragmentCount(
                    fracture.Fragments.Count, profile.FragmentCount,
                    profile.Pattern,
                    job.MergedAssembly
                        ? MeshAnalyzer.MaxMergedAssemblyFragmentCount
                        : MeshAnalyzer.MaxSafePrimaryFragmentCount,
                    out string primaryFragmentBudgetError))
            {
                error = primaryFragmentBudgetError;
                return false;
            }
            if (!MeshAnalyzer.ValidateGeneratedFragments(fracture.Fragments,
                    "Primary fracture", out string generatedSafetyError))
            {
                error = generatedSafetyError;
                return false;
            }

            // A finished solve replaces the whole in-memory result. Do not repaint the
            // now-empty Scene between teardown and reconstruction; the window applies
            // presentation and requests exactly one repaint after FinishBuild succeeds.
            End(false);
            try
            {
                m_source = source;
                m_profile = profile;
                m_sourceMesh = filter.sharedMesh;
                m_sourceRenderer = filter.GetComponent<MeshRenderer>();
                m_sourceMaterials = m_sourceRenderer != null
                    ? m_sourceRenderer.sharedMaterials
                    : Array.Empty<Material>();
                m_materialHash = ComputeMaterialHash(
                    m_sourceMaterials, EffectiveInterior(profile, m_sourceMaterials));
                m_scatterHash = BakePipeline.ComputeScatterHash(profile);
                m_localBounds = m_sourceMesh.bounds;
                InputHash = BakePipeline.ComputeFractureHash(
                    m_sourceMesh, profile, filter.transform.lossyScale);
                if (job.MergedAssembly)
                    InputHash = unchecked(InputHash * 31 + 1);
                m_mergedAssembly = job.MergedAssembly;
                // Cache a deep copy before the rebase loop below mutates the fragments,
                // so a refresh with identical inputs can skip the solve entirely.
                m_cachedSolve = CloneFractureResult(fracture);
                m_cachedSolveHash = InputHash;
                Warnings.AddRange(fracture.Warnings);
                if (!string.IsNullOrEmpty(job.SafetyNotice))
                    Warnings.Add(job.SafetyNotice);

                m_colorMaterial = CreatePreviewMaterial(
                    "QD Preview Color", Color.white);
                m_neutralInteriorMaterial = CreatePreviewMaterial(
                    "QD Preview Interior", new Color(0.42f, 0.4f, 0.38f));
                m_interiorHighlightMaterial = CreatePreviewMaterial(
                    "QD Preview Interior Highlight", QDUI.Accent);
                m_wireMaterial = CreatePreviewMaterial(
                    "QD Preview Wireframe", new Color(0.05f, 0.05f, 0.05f, 1f));

                m_root = new GameObject("[Quick Destruction Fracture Preview]")
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                m_root.transform.SetPositionAndRotation(
                    filter.transform.position, filter.transform.rotation);
                m_root.transform.localScale = filter.transform.lossyScale;

                float averageVolume = 0f;
                foreach (float volume in fracture.Volumes) averageVolume += volume;
                averageVolume /= Mathf.Max(1, fracture.Volumes.Count);
                float scaleVolume = Mathf.Abs(
                    filter.transform.lossyScale.x *
                    filter.transform.lossyScale.y *
                    filter.transform.lossyScale.z);
                if (scaleVolume < 1e-8f) scaleVolume = 1f;

                List<int> secondaryParents = BakePipeline.SelectSecondaryParents(
                    fracture.Volumes, profile);
                SecondaryEligiblePrimaryCount = secondaryParents.Count;
                if (profile.SecondaryBreakup != SecondaryBreakupLevel.Off &&
                    secondaryParents.Count == 0)
                {
                    Warnings.Add(
                        "Secondary breakup is enabled, but no primary fragment meets " +
                        "the Minimum Parent Size threshold.");
                }
                EstimatedSecondaryFragmentCount =
                    SecondaryEligiblePrimaryCount *
                    profile.SecondaryPiecesPerFragment;
                EstimatedSecondaryBodyCount = EstimatedSecondaryFragmentCount;
                if (EstimatedSecondaryFragmentCount > 160)
                {
                    Warnings.Add(
                        "Secondary breakup may bake more than 160 child pieces. " +
                        "Reduce eligible parents or pieces per parent for general-purpose use.");
                }

                var secondaryParentSet = new HashSet<int>(secondaryParents);
                int previewedSecondaryFragments = 0;
                int previewedSecondaryBodies = 0;

                void AddPreviewFragment(WorkMesh previewWork, float volume,
                    string name)
                {
                    Vector3 centroid = previewWork.ComputeBounds().center;
                    previewWork.Rebase(centroid);
                    // The same render-only detail the bake will produce: stone chips on
                    // concrete, long splinters on wood. The preview used to skip it, so
                    // Interior Stone Scatter appeared to do nothing until after a bake.
                    Mesh scatterMesh = InteriorScatterMeshBuilder.Build(
                        previewWork, profile, name + " Scatter");
                    Mesh mesh = previewWork.ToMesh(name);
                    mesh.hideFlags = HideFlags.HideAndDontSave;

                    var fragment = new GameObject(name)
                    {
                        hideFlags = HideFlags.HideAndDontSave
                    };
                    fragment.transform.SetParent(m_root.transform, false);
                    fragment.transform.localPosition = centroid;

                    var meshFilter = fragment.AddComponent<MeshFilter>();
                    meshFilter.sharedMesh = mesh;
                    var renderer = fragment.AddComponent<MeshRenderer>();
                    MeshRenderer scatterRenderer = null;
                    if (scatterMesh != null)
                    {
                        scatterMesh.hideFlags = HideFlags.HideAndDontSave;
                        var scatter = new GameObject("Interior Scatter")
                        {
                            hideFlags = HideFlags.HideAndDontSave
                        };
                        scatter.transform.SetParent(fragment.transform, false);
                        var scatterFilter = scatter.AddComponent<MeshFilter>();
                        scatterFilter.sharedMesh = scatterMesh;
                        scatterRenderer = scatter.AddComponent<MeshRenderer>();
                        m_scatterMeshes.Add(scatterMesh);
                    }

                    m_meshes.Add(mesh);
                    m_fragmentTransforms.Add(fragment.transform);
                    m_basePositions.Add(centroid);
                    m_renderers.Add(renderer);
                    m_scatterRenderers.Add(scatterRenderer);
                    TotalTriangles += previewWork.TriangleCount;
                    if (scatterMesh != null)
                        TotalTriangles += scatterMesh.triangles.Length / 3;

                    EstimatedBodyCount++;
                    EstimatedMass += Mathf.Clamp(
                        volume * scaleVolume * profile.Density,
                        profile.MinFragmentMass,
                        profile.MaxFragmentMass);
                }

                for (int i = 0; i < fracture.Fragments.Count; i++)
                {
                    WorkMesh work = fracture.Fragments[i];
                    if (secondaryParentSet.Contains(i))
                    {
                        FractureResult secondary = FractureEngine.FractureSecondary(
                            work, profile, BakePipeline.SecondarySeed(profile.Seed, i),
                            profile.SecondaryPiecesPerFragment, null);
                        if (!secondary.Cancelled && secondary.Fragments.Count >= 2)
                        {
                            if (!MeshAnalyzer.ValidateGeneratedFragments(
                                    secondary.Fragments, "Secondary fracture", out string secondarySafetyError))
                            {
                                Warnings.Add(secondarySafetyError);
                                AddPreviewFragment(work, fracture.Volumes[i],
                                    $"Preview Fragment {i:000}");
                                continue;
                            }
                            float secondaryTotal = 0f;
                            for (int child = 0; child < secondary.Volumes.Count; child++)
                                secondaryTotal += secondary.Volumes[child];
                            for (int child = 0; child < secondary.Fragments.Count; child++)
                            {
                                AddPreviewFragment(secondary.Fragments[child],
                                    secondary.Volumes[child],
                                    $"Preview Fragment {i:000} / Level Two {child:00}");
                                previewedSecondaryFragments++;
                                previewedSecondaryBodies++;
                            }
                            foreach (string warning in secondary.Warnings)
                                Warnings.Add($"Secondary parent {i}: {warning}");
                            continue;
                        }

                        Warnings.Add($"Primary fragment {i} could not produce a valid second level; showing the primary fragment instead.");
                    }

                    AddPreviewFragment(work, fracture.Volumes[i],
                        $"Preview Fragment {i:000}");
                }

                EstimatedSecondaryFragmentCount = previewedSecondaryFragments;
                EstimatedSecondaryBodyCount = previewedSecondaryBodies;

                if (source.scene.IsValid())
                    m_sourceVisibility.Hide(job.VisibilitySources != null &&
                                            job.VisibilitySources.Count > 0
                        ? job.VisibilitySources
                        : new[] { source });

                SceneView.duringSceneGui -= DrawWireframe;
                SceneView.duringSceneGui += DrawWireframe;
                BuildSeconds = (float)job.Timer.Elapsed.TotalSeconds;
#pragma warning disable 0162
                if (FractureEngine.VerboseSolverProfiling)
                UnityEngine.Debug.Log($"[QD Profile] === PREVIEW COMPLETED IN {job.Timer.ElapsedMilliseconds} ms ({BuildSeconds:F2} s) === " +
                    $"(Fragments: {m_meshes.Count})");
                return true;
            }
            catch (Exception exception)
            {
                error = $"Preview failed: {exception.Message}";
                UnityEngine.Debug.LogException(exception);
                End();
                return false;
            }
        }

        public bool IsStale(GameObject source, DestructionProfile profile,
            bool mergedAssembly = false)
        {
            if (!IsActive || source != m_source || profile != m_profile ||
                mergedAssembly != m_mergedAssembly)
                return true;
            var filter = source != null ? source.GetComponentInChildren<MeshFilter>() : null;
            int currentHash = filter != null && filter.sharedMesh != null
                ? BakePipeline.ComputeFractureHash(filter.sharedMesh, profile,
                    filter.transform.lossyScale)
                : 0;
            if (mergedAssembly) currentHash = unchecked(currentHash * 31 + 1);
            if (filter == null || filter.sharedMesh == null || currentHash != InputHash)
                return true;
            if (BakePipeline.ComputeScatterHash(profile) != m_scatterHash)
                return true;
            Material[] materials = filter.GetComponent<MeshRenderer>() != null
                ? filter.GetComponent<MeshRenderer>().sharedMaterials
                : Array.Empty<Material>();
            return ComputeMaterialHash(
                materials, EffectiveInterior(profile, materials)) != m_materialHash;
        }

        public void SetExplode(float value)
        {
            Explode = Mathf.Clamp(value, 0f, 0.5f);
            ApplyExplode();
            RebuildCombinedWireMesh();
            SceneView.RepaintAll();
        }

        void ApplyExplode()
        {
            float distance = m_localBounds.size.magnitude * Explode;
            Vector3 center = m_localBounds.center;
            for (int i = 0; i < m_fragmentTransforms.Count; i++)
            {
                Vector3 direction = m_basePositions[i] - center;
                if (direction.sqrMagnitude < 1e-8f)
                {
                    float angle = i * 2.3999632f;
                    direction = new Vector3(
                        Mathf.Cos(angle), Mathf.Sin(angle * 0.73f), Mathf.Sin(angle));
                }
                m_fragmentTransforms[i].localPosition =
                    m_basePositions[i] + direction.normalized * distance;
            }
        }

        /// <summary>Applies all visual presentation changes as one transaction. Automatic
        /// preview refresh previously requested several full Scene repaints in sequence.</summary>
        public void SetPresentation(FracturePreviewMaterialMode mode,
            float explode, bool wireframe)
        {
            MaterialMode = mode;
            Explode = Mathf.Clamp(explode, 0f, 0.5f);
            Wireframe = wireframe;
            ApplyMaterials();
            ApplyExplode();
            RebuildCombinedWireMesh();
            SceneView.RepaintAll();
        }

        public void SetMaterialMode(FracturePreviewMaterialMode mode)
        {
            MaterialMode = mode;
            ApplyMaterials();
            SceneView.RepaintAll();
        }

        public void SetWireframe(bool enabled)
        {
            Wireframe = enabled;
            RebuildCombinedWireMesh();
            SceneView.RepaintAll();
        }

        public void Frame()
        {
            if (!IsActive || m_renderers.Count == 0) return;
            Bounds bounds = m_renderers[0].bounds;
            for (int i = 1; i < m_renderers.Count; i++)
                bounds.Encapsulate(m_renderers[i].bounds);
            SceneView.lastActiveSceneView?.Frame(bounds, false);
        }

        public void End()
        {
            End(true);
        }

        void End(bool repaint)
        {
            CancelSolve();
            SceneView.duringSceneGui -= DrawWireframe;
            m_sourceVisibility.Restore();

            if (m_root != null) UnityEngine.Object.DestroyImmediate(m_root);
            foreach (Mesh mesh in m_meshes)
                if (mesh != null) UnityEngine.Object.DestroyImmediate(mesh);
            foreach (Mesh mesh in m_scatterMeshes)
                if (mesh != null) UnityEngine.Object.DestroyImmediate(mesh);
            if (m_combinedWireMesh != null)
                UnityEngine.Object.DestroyImmediate(m_combinedWireMesh);
            DestroyMaterial(ref m_colorMaterial);
            DestroyMaterial(ref m_neutralInteriorMaterial);
            DestroyMaterial(ref m_interiorHighlightMaterial);
            DestroyMaterial(ref m_wireMaterial);

            m_meshes.Clear();
            m_scatterMeshes.Clear();
            m_fragmentTransforms.Clear();
            m_basePositions.Clear();
            m_renderers.Clear();
            m_scatterRenderers.Clear();
            Warnings.Clear();
            m_root = null;
            m_source = null;
            m_profile = null;
            m_sourceMesh = null;
            m_sourceRenderer = null;
            m_sourceMaterials = null;
            m_combinedWireMesh = null;
            TotalTriangles = 0;
            EstimatedBodyCount = 0;
            SecondaryEligiblePrimaryCount = 0;
            EstimatedSecondaryFragmentCount = 0;
            EstimatedSecondaryBodyCount = 0;
            EstimatedMass = 0f;
            BuildSeconds = 0f;
            InputHash = 0;
            m_materialHash = 0;
            m_scatterHash = 0;
            m_mergedAssembly = false;
            if (repaint) SceneView.RepaintAll();
        }

        void DrawWireframe(SceneView sceneView)
        {
            if (!IsActive) return;
            m_sourceVisibility.EnsureHidden();
            if (!Wireframe || m_wireMaterial == null ||
                m_combinedWireMesh == null ||
                Event.current.type != EventType.Repaint)
                return;
            if (!m_wireMaterial.SetPass(0)) return;

            GL.wireframe = true;
            try
            {
                // One combined draw replaces one DrawMeshNow call per fragment. Scene
                // Repaint was otherwise the dominant cost after changing Parts.
                Graphics.DrawMeshNow(
                    m_combinedWireMesh, m_root.transform.localToWorldMatrix);
            }
            finally
            {
                GL.wireframe = false;
            }
        }

        void ApplyMaterials()
        {
            if (!IsActive || m_sourceMesh == null) return;
            int exteriorCount = m_sourceMesh.subMeshCount;
            for (int i = 0; i < m_renderers.Count; i++)
            {
                MeshRenderer renderer = m_renderers[i];
                Mesh mesh = m_meshes[i];
                MeshRenderer scatterRenderer = i < m_scatterRenderers.Count
                    ? m_scatterRenderers[i]
                    : null;
                var materials = new Material[mesh.subMeshCount];

                if (MaterialMode == FracturePreviewMaterialMode.Colorized)
                {
                    for (int m = 0; m < materials.Length; m++)
                        materials[m] = m_colorMaterial;
                    Color color = Color.HSVToRGB(
                        Mathf.Repeat(i * 0.61803398875f, 1f), 0.62f, 1f);
                    m_propertyBlock.Clear();
                    m_propertyBlock.SetColor("_BaseColor", color);
                    m_propertyBlock.SetColor("_Color", color);
                    m_propertyBlock.SetColor("_UnlitColor", color);
                    renderer.SetPropertyBlock(m_propertyBlock);
                    if (scatterRenderer != null)
                    {
                        scatterRenderer.sharedMaterial = m_colorMaterial;
                        scatterRenderer.SetPropertyBlock(m_propertyBlock);
                    }
                }
                else
                {
                    Material interior = EffectiveInterior(m_profile, m_sourceMaterials);
                    materials = FragmentMaterialUtility.BuildForFragment(
                        mesh, m_sourceMaterials, interior,
                        m_profile != null && m_profile.InteriorUseSourceMaterial);
                    for (int m = 0; m < exteriorCount && m < materials.Length; m++)
                        if (materials[m] == null) materials[m] = m_neutralInteriorMaterial;
                    if (materials.Length > exteriorCount)
                    {
                        if (MaterialMode == FracturePreviewMaterialMode.InteriorCuts)
                            materials[materials.Length - 1] = m_interiorHighlightMaterial;
                        else if (materials[materials.Length - 1] == null)
                            materials[materials.Length - 1] = m_neutralInteriorMaterial;
                    }
                    renderer.SetPropertyBlock(null);
                    if (scatterRenderer != null)
                    {
                        scatterRenderer.sharedMaterial =
                            MaterialMode == FracturePreviewMaterialMode.InteriorCuts
                                ? m_interiorHighlightMaterial
                                : materials.Length > 0
                                    ? materials[materials.Length - 1]
                                    : interior ?? m_neutralInteriorMaterial;
                        scatterRenderer.SetPropertyBlock(null);
                    }
                }

                renderer.sharedMaterials = materials;
            }
        }

        void RebuildCombinedWireMesh()
        {
            if (m_combinedWireMesh != null)
            {
                UnityEngine.Object.DestroyImmediate(m_combinedWireMesh);
                m_combinedWireMesh = null;
            }
            if (!Wireframe || m_meshes.Count == 0) return;

            var combine = new List<CombineInstance>(m_meshes.Count * 2);
            int totalVertices = 0;
            for (int i = 0; i < m_meshes.Count; i++)
            {
                Mesh mesh = m_meshes[i];
                if (mesh == null) continue;
                Matrix4x4 transform = Matrix4x4.Translate(
                    i < m_fragmentTransforms.Count && m_fragmentTransforms[i] != null
                        ? m_fragmentTransforms[i].localPosition
                        : Vector3.zero);
                for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
                {
                    combine.Add(new CombineInstance
                    {
                        mesh = mesh,
                        subMeshIndex = submesh,
                        transform = transform
                    });
                    totalVertices += mesh.vertexCount;
                }
                // Scatter is deliberately a separate renderer so it cannot affect
                // the watertight fragment mesh or collider. Include it in the
                // preview wireframe nevertheless: otherwise a correctly generated
                // small chip is almost impossible to verify in Colorized view.
                MeshRenderer scatterRenderer = i < m_scatterRenderers.Count
                    ? m_scatterRenderers[i]
                    : null;
                MeshFilter scatterFilter = scatterRenderer != null
                    ? scatterRenderer.GetComponent<MeshFilter>()
                    : null;
                Mesh scatterMesh = scatterFilter != null ? scatterFilter.sharedMesh : null;
                if (scatterMesh != null)
                {
                    combine.Add(new CombineInstance
                    {
                        mesh = scatterMesh,
                        subMeshIndex = 0,
                        transform = transform
                    });
                    totalVertices += scatterMesh.vertexCount;
                }
            }
            if (combine.Count == 0) return;

            m_combinedWireMesh = new Mesh
            {
                name = "QD Preview Combined Wire",
                hideFlags = HideFlags.HideAndDontSave,
                indexFormat = totalVertices > 65535
                    ? IndexFormat.UInt32
                    : IndexFormat.UInt16
            };
            m_combinedWireMesh.CombineMeshes(combine.ToArray(), true, true, false);
        }

        /// <summary>Material actually shown on cut faces: the source's own base material
        /// when the profile opts into that fallback, otherwise the assigned interior.</summary>
        static Material EffectiveInterior(DestructionProfile profile,
            Material[] sourceMaterials)
        {
            if (profile != null && profile.InteriorUseSourceMaterial &&
                sourceMaterials != null && sourceMaterials.Length > 0 &&
                sourceMaterials[0] != null)
                return sourceMaterials[0];
            return profile != null ? profile.InteriorMaterial : null;
        }

        static Material CreatePreviewMaterial(string name, Color color)
        {
            RenderPipelineAsset pipeline = GraphicsSettings.currentRenderPipeline;
            string pipelineName = pipeline != null
                ? pipeline.GetType().FullName ?? pipeline.GetType().Name
                : string.Empty;
            Shader shader = null;
            if (pipelineName.Contains("HighDefinition") ||
                pipelineName.Contains("HDRenderPipeline"))
                shader = Shader.Find("HDRP/Unlit");
            else if (pipelineName.Contains("Universal"))
                shader = Shader.Find("Universal Render Pipeline/Unlit");

            // Never fall back from HDRP to a URP shader merely because the URP package is
            // installed too; Shader.Find succeeds but HDRP renders that material pink.
            if (shader == null && pipeline == null)
                shader = Shader.Find("Unlit/Color");
            if (shader == null)
                shader = PresetMaterialLibrary.FindPipelineLitShader();
            if (shader == null) shader = Shader.Find("Hidden/Internal-Colored");
            var material = new Material(shader)
            {
                name = name,
                hideFlags = HideFlags.HideAndDontSave
            };
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", color);
            if (material.HasProperty("_UnlitColor"))
                material.SetColor("_UnlitColor", color);
            return material;
        }

        static void DestroyMaterial(ref Material material)
        {
            if (material != null) UnityEngine.Object.DestroyImmediate(material);
            material = null;
        }

        static int ComputeMaterialHash(Material[] materials, Material interior)
        {
            unchecked
            {
                int hash = interior != null ? interior.GetHashCode() : 0;
                if (materials == null) return hash;
                foreach (Material material in materials)
                    hash = hash * 31 + (material != null ? material.GetHashCode() : 0);
                return hash;
            }
        }

        /// <summary>Deep-copies a solve result so FinishBuild can rebase and convert the
        /// fragments it receives without invalidating the cached copy.</summary>
        static FractureResult CloneFractureResult(FractureResult source)
        {
            var clone = new FractureResult
            {
                SourceVolume = source.SourceVolume,
                Cancelled = source.Cancelled
            };
            for (int i = 0; i < source.Fragments.Count; i++)
                clone.Fragments.Add(source.Fragments[i].Clone());
            clone.Volumes.AddRange(source.Volumes);
            clone.Warnings.AddRange(source.Warnings);
            return clone;
        }
    }
}
