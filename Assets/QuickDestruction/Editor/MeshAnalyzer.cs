using System.Collections.Generic;
using UnityEngine;
using RipVertices.QuickDestruction.Runtime;

namespace RipVertices.QuickDestruction.Editor
{
    internal enum AnalysisSeverity { Info, Warning, Blocked }

    internal class AnalysisItem
    {
        public AnalysisSeverity Severity;
        public string Code;
        public string Title;
        public string Why;
        public string Fix;

        public AnalysisItem(AnalysisSeverity severity, string code, string title, string why, string fix)
        {
            Severity = severity; Code = code; Title = title; Why = why; Fix = fix;
        }
    }

    /// <summary>
    /// Input gate for the bake pipeline. Prevents long bakes on bad data and explains risk
    /// without requiring topology knowledge. Every finding has a Why and a Fix.
    /// </summary>
    internal class AnalysisReport
    {
        public GameObject Source;
        public Mesh Mesh;
        public Renderer Renderer;
        public readonly List<AnalysisItem> Items = new List<AnalysisItem>();

        public int VertexCount;
        public int TriangleCount;
        public int SubmeshCount;
        public int SourceMeshCount;
        public Vector3 BoundsSize;
        public Vector3 LossyScale = Vector3.one;
        public int OpenEdges;
        public int NonManifoldEdges;
        public int DegenerateTriangles;
        public bool IsClosedVolume;
        public bool HasExistingColliders;
        public bool HasRigidbody;

        /// <summary>
        /// True when the source has no measurable thickness on one axis: a single quad, a
        /// billboard, a modular wall or floor panel. FULL SOLID has no interior to fill on
        /// such a mesh, so this is the one case where open-boundary advice must name HOLLOW
        /// SHELL instead of suggesting the holes be capped in a DCC — there is no hole.
        /// </summary>
        public bool IsFlatSource
        {
            get
            {
                float largest = Mathf.Max(BoundsSize.x,
                    Mathf.Max(BoundsSize.y, BoundsSize.z));
                if (largest <= 0f) return false;
                float thinnest = Mathf.Min(BoundsSize.x,
                    Mathf.Min(BoundsSize.y, BoundsSize.z));
                // 0.2% of the longest side: a 4 m wall panel counts as flat below 8 mm,
                // which no authored solid wall, kerb or slab reaches.
                return thinnest <= Mathf.Max(1e-5f, largest * 0.002f);
            }
        }

        public string BoundsLabel =>
            $"{BoundsSize.x:0.###} x {BoundsSize.y:0.###} x {BoundsSize.z:0.###} m";

        public AnalysisSeverity Status
        {
            get
            {
                var worst = AnalysisSeverity.Info;
                foreach (var i in Items)
                    if (i.Severity > worst) worst = i.Severity;
                return worst;
            }
        }

        public float EstimatedCacheMB(int fragmentCount)
        {
            // Rough: each fragment duplicates a slice of the source + cap geometry.
            float perFragmentTris = TriangleCount / Mathf.Max(1f, fragmentCount) * 1.9f + 24f;
            float bytes = fragmentCount * perFragmentTris * 3f * 44f;
            return bytes / (1024f * 1024f);
        }

        public string RiskLabel
        {
            get
            {
                if (Status == AnalysisSeverity.Blocked) return "BLOCKED";
                int warnings = 0;
                foreach (var i in Items) if (i.Severity == AnalysisSeverity.Warning) warnings++;
                if (warnings == 0 && TriangleCount <= 60000) return "LOW";
                if (warnings <= 2 && TriangleCount <= 200000) return "MEDIUM";
                return "HIGH";
            }
        }
    }

    internal static class MeshAnalyzer
    {
        public const int MaxSafePrimaryFragmentCount = 250;
        public const int MaxMergedAssemblyFragmentCount =
            CombinedMeshSource.MaxPrimaryFragmentCount;
        public const int MaxSafeGlassFragmentCount =
            FractureEngine.MaxRadialGlassFragments;

        /// <summary>
        /// The object's own mesh, or its nested meshes when it is only a container. For
        /// containers, active geometry wins; inactive geometry is consulted only when the
        /// source has no active geometry at all. A completed bake disables the original, so
        /// without the fallback every already-processed source reports "No mesh found" and
        /// the one mesh of a batch that has to be redone by hand cannot be redone at all.
        /// </summary>
        internal static MeshFilter[] CollectSourceMeshFilters(GameObject source)
        {
            if (source == null) return System.Array.Empty<MeshFilter>();
            // Root + Nested Meshes passes each mesh owner separately. A mesh on this
            // exact object must not also claim its children's meshes or fail the
            // one-mesh gate; those children have their own entries in the batch.
            MeshFilter own = source.GetComponent<MeshFilter>();
            if (own != null && own.sharedMesh != null) return new[] { own };
            MeshFilter[] active = source.GetComponentsInChildren<MeshFilter>(false);
            if (ContainsMesh(active)) return active;
            MeshFilter[] all = source.GetComponentsInChildren<MeshFilter>(true);
            return ContainsMesh(all) ? all : active;
        }

        /// <summary>
        /// The single MeshFilter <see cref="CollectSourceMeshFilters"/> resolves for a
        /// source that passed the analyzer's one-mesh gate.
        /// </summary>
        internal static MeshFilter ResolveSourceMeshFilter(GameObject source)
        {
            MeshFilter[] filters = CollectSourceMeshFilters(source);
            for (int i = 0; i < filters.Length; i++)
                if (filters[i] != null && filters[i].sharedMesh != null)
                    return filters[i];
            return null;
        }

        static bool ContainsMesh(MeshFilter[] filters)
        {
            if (filters == null) return false;
            for (int i = 0; i < filters.Length; i++)
                if (filters[i] != null && filters[i].sharedMesh != null)
                    return true;
            return false;
        }

        /// <summary>
        /// Conservative gate for cut geometry. The solver operates on a copy, but an
        /// open or non-manifold source can still produce caps across a window opening
        /// or cavity. Recoverable topology risks require an explicit one-shot UI
        /// confirmation; generated output is validated independently afterwards.
        /// </summary>
        public static bool ValidateClosedFractureInput(AnalysisReport report,
            DestructionProfile profile, out string error)
        {
            return ValidateClosedFractureInput(
                report, profile, false, out error);
        }

        /// <summary>
        /// Source-topology gates can be bypassed only by an explicit, one-shot UI
        /// confirmation. Invalid generated fragments keep their hard safety gate because
        /// there is then no trustworthy prefab to save.
        /// </summary>
        public static bool ValidateClosedFractureInput(AnalysisReport report,
            DestructionProfile profile, bool allowUnsafeOpenFullSolid,
            out string error)
        {
            error = null;
            if (report == null || profile == null) return true;
            if (report.NonManifoldEdges > 0 && !allowUnsafeOpenFullSolid)
            {
                error = "QD-GEO-SAFE-001: NON-MANIFOLD warning: the source has " +
                    $"{report.NonManifoldEdges} non-manifold edge(s). Repair or separate " +
                    "the overlapping geometry for reliable production output. Continuing " +
                    "may create missing pieces, approximate caps or fragments bridging " +
                    "across openings. You can continue anyway after confirming the risk; " +
                    "the original mesh is never modified and invalid generated fragments " +
                    "will not be saved.";
                return false;
            }
            // Checked before the generic open-boundary gate: a flat panel trips both, and
            // QD-GEO-SAFE-002 would then advise capping holes that do not exist while the
            // real answer — HOLLOW SHELL — goes unsaid.
            if (report.IsFlatSource && !profile.Solidify && !allowUnsafeOpenFullSolid)
            {
                error = "QD-GEO-SAFE-004: FLAT SOURCE warning: the source measures " +
                    $"{report.BoundsLabel}, so it is a surface with no thickness rather " +
                    "than a volume. FULL SOLID has no interior to fill here and the bake " +
                    "can finish with no usable fragments.\n\nHOLLOW SHELL (Solidify) is the " +
                    "mode for flat walls, floors and panels: it builds an inner wall from " +
                    "Wall Thickness so the surface breaks as pieces with real depth. The " +
                    "original mesh is never modified either way.";
                return false;
            }
            if (report.OpenEdges > 0 && !profile.Solidify &&
                !allowUnsafeOpenFullSolid)
            {
                error = "QD-GEO-SAFE-002: FULL SOLID warning: the source has " +
                    $"{report.OpenEdges} open boundary edge(s). FULL SOLID would interpret " +
                    "those openings as missing faces and may create caps across a barrel, " +
                    "window or cavity. Choose HOLLOW SHELL (Solidify), or close the mesh in " +
                    "your DCC before previewing/baking. You can continue anyway after " +
                    "confirming the risk; the original mesh is never modified.";
                return false;
            }
            return true;
        }

        /// <summary>True only for source-topology warnings that the solver may attempt
        /// after explicit confirmation. Missing source data and invalid generated output
        /// are not recoverable and therefore never reach an ANYWAY action.</summary>
        public static bool IsRecoverableUnsafeInput(string error)
        {
            return !string.IsNullOrEmpty(error) &&
                   (error.StartsWith("QD-GEO-SAFE-001", System.StringComparison.Ordinal) ||
                    error.StartsWith("QD-GEO-SAFE-002", System.StringComparison.Ordinal) ||
                    error.StartsWith("QD-GEO-SAFE-004", System.StringComparison.Ordinal));
        }

        /// <summary>True for the flat-source gate, which the UI can resolve for the user by
        /// switching the profile to HOLLOW SHELL rather than only offering to continue.</summary>
        public static bool IsFlatSourceWarning(string error)
        {
            return !string.IsNullOrEmpty(error) &&
                   error.StartsWith("QD-GEO-SAFE-004", System.StringComparison.Ordinal);
        }

        public static bool IsOpenSourceWarning(string error)
        {
            return !string.IsNullOrEmpty(error) &&
                   error.StartsWith("QD-GEO-SAFE-002", System.StringComparison.Ordinal);
        }

        /// <summary>
        /// Copies the profile into a solver-only struct and, for a non-manifold source,
        /// returns a notice. It does NOT alter geometry: Full Solid seals flat open
        /// boundaries in FractureEngine, and Solidify builds inner walls there too.
        /// </summary>
        public static DestructionProfileData CreateSafeSolverProfile(
            AnalysisReport report, DestructionProfile profile, out string notice)
        {
            var data = new DestructionProfileData(profile);
            notice = null;
            if (report == null || profile == null) return data;

            if (report.NonManifoldEdges > 0)
            {
                notice = "Source has non-manifold seams; the solver will preserve and " +
                    "validate the generated closed fragments before output is created.";
            }
            return data;
        }

        /// <summary>
        /// Final safety net for generated output. A closed input is necessary but not
        /// sufficient: a bad cut/cap must never reach a preview or generated prefab.
        /// </summary>
        public static bool ValidateGeneratedFragments(IList<WorkMesh> fragments,
            string levelName, out string error)
        {
            error = null;
            if (fragments == null || fragments.Count == 0)
            {
                error = $"QD-GEO-SAFE-003: {levelName} generated no fragments.";
                return false;
            }

            for (int fragmentIndex = 0; fragmentIndex < fragments.Count; fragmentIndex++)
            {
                WorkMesh fragment = fragments[fragmentIndex];
                if (fragment == null || fragment.Positions.Count < 4)
                {
                    // The solver's collection pass can leave an empty placeholder while
                    // merging micro-fragments. It is discarded before mesh export.
                    continue;
                }

                Bounds bounds = fragment.ComputeBounds();
                float cell = Mathf.Max(bounds.size.magnitude, 0.001f) * 1e-5f;
                var welded = new Dictionary<Vector3Int, int>(fragment.Positions.Count);
                var remap = new int[fragment.Positions.Count];
                int next = 0;
                for (int v = 0; v < fragment.Positions.Count; v++)
                {
                    Vector3 point = fragment.Positions[v];
                    var key = new Vector3Int(
                        Mathf.RoundToInt(point.x / cell),
                        Mathf.RoundToInt(point.y / cell),
                        Mathf.RoundToInt(point.z / cell));
                    if (!welded.TryGetValue(key, out int id))
                    {
                        id = next++;
                        welded.Add(key, id);
                    }
                    remap[v] = id;
                }

                var edgeUse = new Dictionary<long, int>();
                for (int submesh = 0; submesh < fragment.Submeshes.Length; submesh++)
                {
                    List<int> triangles = fragment.Submeshes[submesh];
                    for (int t = 0; t + 2 < triangles.Count; t += 3)
                    {
                        int a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
                        if (a < 0 || b < 0 || c < 0 || a >= remap.Length ||
                            b >= remap.Length || c >= remap.Length ||
                            remap[a] == remap[b] || remap[b] == remap[c] ||
                            remap[a] == remap[c])
                        {
                            // Degenerate helper triangles are removed by WorkMesh.ToMesh.
                            // They must not cancel an otherwise valid preview/bake.
                            continue;
                        }
                        CountEdge(edgeUse, remap[a], remap[b]);
                        CountEdge(edgeUse, remap[b], remap[c]);
                        CountEdge(edgeUse, remap[c], remap[a]);
                    }
                }

                // WorkMesh.ToMesh removes coincident helper faces immediately before
                // export. Do not reject its intermediate edge incidence here: imported
                // hard seams and temporary cap helpers can legitimately use an edge more
                // than twice before that cleanup pass.
            }
            return true;
        }

        public static AnalysisReport Analyze(GameObject source)
        {
            var report = new AnalysisReport { Source = source };

            if (source == null)
            {
                report.Items.Add(new AnalysisItem(AnalysisSeverity.Blocked, "QD-GEO-000",
                    "No source selected",
                    "The pipeline needs a MeshRenderer prop to fracture.",
                    "Select a GameObject with a MeshFilter + MeshRenderer in the scene or project."));
                return report;
            }

            MeshFilter[] childFilters = CollectSourceMeshFilters(source);
            var validFilters = new List<MeshFilter>(childFilters.Length);
            for (int i = 0; i < childFilters.Length; i++)
            {
                MeshFilter candidate = childFilters[i];
                if (candidate != null && candidate.sharedMesh != null)
                    validFilters.Add(candidate);
            }
            report.SourceMeshCount = validFilters.Count;

            if (source.GetComponentInChildren<SkinnedMeshRenderer>(true) != null &&
                validFilters.Count == 0)
            {
                report.Items.Add(new AnalysisItem(AnalysisSeverity.Blocked, "QD-GEO-010",
                    "SkinnedMeshRenderer source",
                    "Deforming geometry is outside the 1.0 support matrix.",
                    "Bake the skinned mesh to a static snapshot first, or use a static prop."));
                return report;
            }

            if (validFilters.Count == 0)
            {
                report.Items.Add(new AnalysisItem(AnalysisSeverity.Blocked, "QD-GEO-001",
                    "No mesh found",
                    "A MeshFilter with a valid mesh is required.",
                    "Add a MeshFilter with a mesh, or select a different object."));
                return report;
            }

            if (validFilters.Count > 1)
            {
                report.Items.Add(new AnalysisItem(AnalysisSeverity.Blocked, "QD-GEO-011",
                    $"Multiple source meshes ({validFilters.Count})",
                    "Quick Destruction fractures one MeshFilter at a time. Selecting a model " +
                    "or FBX parent with several mesh children would otherwise pick the first " +
                    "child silently and produce an unrelated or invalid result.",
                    "Expand the model hierarchy and select the exact child that has one " +
                    "MeshFilter + MeshRenderer, or export/combine only the intended prop in " +
                    "your DCC before baking."));
                return report;
            }

            MeshFilter filter = validFilters[0];

            var mesh = filter.sharedMesh;
            report.Mesh = mesh;
            report.Renderer = filter.GetComponent<MeshRenderer>();
            report.VertexCount = mesh.vertexCount;
            report.SubmeshCount = mesh.subMeshCount;
            long coarseTriangleCount = 0;
            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
            {
                if (mesh.GetTopology(submesh) != MeshTopology.Triangles)
                    continue;
                coarseTriangleCount += (long)(mesh.GetIndexCount(submesh) / 3UL);
            }
            report.TriangleCount = coarseTriangleCount > int.MaxValue
                ? int.MaxValue
                : (int)coarseTriangleCount;
            report.BoundsSize = Vector3.Scale(mesh.bounds.size, filter.transform.lossyScale);
            report.LossyScale = filter.transform.lossyScale;
            report.HasExistingColliders = source.GetComponentInChildren<Collider>() != null;
            report.HasRigidbody = source.GetComponentInChildren<Rigidbody>() != null;

            if (report.Renderer == null)
            {
                report.Items.Add(new AnalysisItem(AnalysisSeverity.Blocked, "QD-GEO-009",
                    "No MeshRenderer on the mesh node",
                    "Fragments inherit exterior materials from the MeshRenderer next to the " +
                    "MeshFilter; without it the bake would produce unassigned material slots.",
                    "Add a MeshRenderer with the prop's materials to the same GameObject as the MeshFilter."));
                return report;
            }

            // Reject production-unsafe source density before acquiring a readable copy.
            // A foliage mesh can contain over a million vertices; duplicating its buffers
            // and building edge-use dictionaries merely to discover that it exceeds the
            // documented 500k-triangle limit blocks the Editor main thread for minutes.
            if (report.TriangleCount > 500000)
            {
                report.Items.Add(new AnalysisItem(AnalysisSeverity.Blocked, "QD-GEO-003",
                    $"Very high poly ({report.TriangleCount:n0} tris)",
                    "Above 500k triangles bake time and cache size are not production safe.",
                    "Use a decimated proxy mesh as the fracture source."));
                return report;
            }

            // The MeshFilter may sit on a transformed child of the source root; bake and
            // preview reproduce that offset in the generated hierarchy.
            Matrix4x4 nodeLocal = source.transform.worldToLocalMatrix *
                                  filter.transform.localToWorldMatrix;
            bool nodeIsIdentity =
                nodeLocal.GetPosition().sqrMagnitude < 1e-6f &&
                Quaternion.Angle(nodeLocal.rotation, Quaternion.identity) < 0.01f &&
                (nodeLocal.lossyScale - Vector3.one).sqrMagnitude < 1e-6f;
            if (!nodeIsIdentity)
            {
                report.Items.Add(new AnalysisItem(AnalysisSeverity.Info, "QD-IMP-004",
                    "Mesh node offset from source root",
                    "The MeshFilter is not at the source root's local origin. Its position, " +
                    "rotation and scale are baked into the generated Intact/Fractured containers.",
                    "No action needed; bake and preview handle the offset automatically."));
            }

            Mesh readableMesh = ReadableMeshUtility.Acquire(
                mesh, out bool ownsReadableMesh, out string readableError);
            if (readableMesh == null)
            {
                report.Items.Add(new AnalysisItem(AnalysisSeverity.Blocked, "QD-GEO-002",
                    "Mesh data could not be read",
                    readableError,
                    "Reimport the model. If the importer still rejects Editor access, enable " +
                    "Read/Write for this model and re-analyze."));
                return report;
            }

            if (ownsReadableMesh)
            {
                report.Items.Add(new AnalysisItem(AnalysisSeverity.Info, "QD-IMP-005",
                    "Read/Write is disabled — Editor snapshot active",
                    "Quick Destruction reads a temporary copy of the imported mesh during " +
                    "Analyze, Preview and Bake; the source asset is unchanged.",
                    "No action needed. Keeping Read/Write disabled saves runtime memory."));
            }

            int[] tris;
            try
            {
                tris = readableMesh.triangles;
                report.TriangleCount = tris.Length / 3;

            // ---- size / scale gates
            if (report.TriangleCount > 100000)
            {
                report.Items.Add(new AnalysisItem(AnalysisSeverity.Warning, "QD-GEO-004",
                    $"High poly source ({report.TriangleCount:n0} tris)",
                    "Bake will work but fragments inherit dense geometry; physics cooking gets expensive.",
                    "Consider a proxy fracture source or lower fragment count."));
            }

            Vector3 s = report.LossyScale;
            if (s.x < 0f || s.y < 0f || s.z < 0f)
            {
                report.Items.Add(new AnalysisItem(AnalysisSeverity.Warning, "QD-IMP-001",
                    "Negative scale on source",
                    "Mirrored transforms flip triangle winding and can invert fragment normals.",
                    "Apply/freeze the negative scale in your DCC or parent setup before baking."));
            }
            float maxDim = Mathf.Max(report.BoundsSize.x, report.BoundsSize.y, report.BoundsSize.z);
            if (maxDim < 0.01f || maxDim > 1000f)
            {
                report.Items.Add(new AnalysisItem(AnalysisSeverity.Warning, "QD-IMP-002",
                    $"Unusual world size ({maxDim:0.###} m)",
                    "Physics is tuned for 0.01 - 1000 m objects; outside that range simulation is unstable.",
                    "Fix the import scale factor or the transform scale."));
            }

            // ---- topology
                AnalyzeTopology(readableMesh, tris, report);
            }
            finally
            {
                ReadableMeshUtility.Release(readableMesh, ownsReadableMesh);
            }

            // ---- unity setup
            // A generated destructible owns its intact Rigidbody by design. Rebaking that
            // root is a supported workflow, so do not show a misleading import warning.
            if (report.HasRigidbody && source.GetComponent<DestructibleRoot>() == null)
            {
                report.Items.Add(new AnalysisItem(AnalysisSeverity.Warning, "QD-IMP-003",
                    "Source already has a Rigidbody",
                    "The baked prefab manages its own physics lifecycle; a source rigidbody would double-simulate.",
                    "The bake output ignores it. Remove it from the source if it is not needed."));
            }
            if (report.SubmeshCount > 1)
            {
                report.Items.Add(new AnalysisItem(AnalysisSeverity.Info, "QD-GEO-007",
                    $"{report.SubmeshCount} submeshes",
                    "Exterior material IDs are preserved; an extra Interior slot is appended.",
                    "No action needed."));
            }

            if (report.Status == AnalysisSeverity.Info)
            {
                report.Items.Insert(0, new AnalysisItem(AnalysisSeverity.Info, "QD-OK",
                    "Ready to bake",
                    "No critical issues found in geometry or import settings.",
                    "Pick a preset and press Bake & Setup."));
            }

            return report;
        }

        public static bool ValidatePrimaryFragmentCount(int actualCount,
            int requestedCount, out string error)
        {
            return ValidatePrimaryFragmentCount(actualCount, requestedCount,
                null, out error);
        }

        public static bool ValidatePrimaryFragmentCount(int actualCount,
            int requestedCount, FracturePattern? pattern, out string error)
        {
            return ValidatePrimaryFragmentCount(actualCount, requestedCount, pattern,
                MaxSafePrimaryFragmentCount, out error);
        }

        public static bool ValidatePrimaryFragmentCount(int actualCount,
            int requestedCount, FracturePattern? pattern, int maxSafePrimaryCount,
            out string error)
        {
            error = null;
            if (pattern == FracturePattern.RadialGlass &&
                actualCount > MaxSafeGlassFragmentCount)
            {
                error = "QD-PERF-002: Glass bake stopped at the fragment safety limit\n" +
                        $"Why: The solver produced {actualCount:n0} glass fragments " +
                        $"(configured maximum: {requestedCount:n0}); an immediate glass break " +
                        $"supports at most {MaxSafeGlassFragmentCount:n0}. More pieces would " +
                        "wake too many renderers, colliders and Rigidbodies in one physics " +
                        "step and cause a visible frame stall. Max Parts is a " +
                        "hard ceiling unless the source already contains more independent " +
                        "physical islands than requested; those islands are never discarded or " +
                        "bound to one Rigidbody.\n" +
                        "Result: This attempt was not written; any previous generated result " +
                        "remains unchanged.\n" +
                        "Fix: Select/export only the glass pane (without frame, blinds or " +
                        "other disconnected parts), then Preview/Bake again.";
                return false;
            }
            maxSafePrimaryCount = Mathf.Clamp(maxSafePrimaryCount,
                MaxSafePrimaryFragmentCount, MaxMergedAssemblyFragmentCount);
            if (actualCount <= maxSafePrimaryCount) return true;
            bool mergedAssemblyBudget =
                maxSafePrimaryCount > MaxSafePrimaryFragmentCount;

            error = "QD-PERF-001: Bake stopped at the fragment safety limit\n" +
                    $"Why: The solver produced {actualCount:n0} primary fragments " +
                    $"(configured maximum: {requestedCount:n0}); the supported safety limit is " +
                    $"{maxSafePrimaryCount:n0}. This usually means the selected mesh " +
                    "contains hundreds of disconnected shells. Max Parts is a hard ceiling unless " +
                    "the source already contains more independent physical islands than requested; " +
                    "those islands must be preserved. " +
                    "Activating that many renderers, colliders and Rigidbodies can stall a frame.\n" +
                    "Result: This attempt was not written; any previous generated result remains " +
                    "unchanged.\n" +
                    (mergedAssemblyBudget
                        ? "Fix: Reduce Max Parts, simplify the modular source, or divide the " +
                          "building into a few intentional structural zones. Each zone can still " +
                          "use Whole Building mode."
                        : "Fix: Select/export a single wall, pane or prop mesh, combine only parts " +
                          "that should break together, or use a simplified fracture proxy.");
            return false;
        }

        public static string BuildFractureFailureMessage(AnalysisReport report,
            FractureResult fracture, DestructionProfile profile)
        {
            int fragments = fracture?.Fragments?.Count ?? 0;
            int openEdges = report != null ? report.OpenEdges : 0;
            int nonManifoldEdges = report != null ? report.NonManifoldEdges : 0;
            int degenerateTriangles = report != null ? report.DegenerateTriangles : 0;
            float sourceVolume = fracture != null ? fracture.SourceVolume : 0f;
            string sourceMode = profile != null && profile.Solidify
                ? "Hollow Shell" : "Full Solid";

            var details = new System.Text.StringBuilder();
            if (fracture?.Warnings != null)
            {
                int detailCount = Mathf.Min(6, fracture.Warnings.Count);
                for (int i = 0; i < detailCount; i++)
                {
                    if (string.IsNullOrWhiteSpace(fracture.Warnings[i])) continue;
                    details.Append("\n- ").Append(fracture.Warnings[i]);
                }
            }

            return "QD-GEO-SAFE-003: Fracture produced fewer than 2 valid fragments\n" +
                   $"Why: The solver kept {fragments} fragment(s). Source volume: " +
                   $"{sourceVolume:0.######}; open edges: {openEdges:n0}; non-manifold " +
                   $"edges: {nonManifoldEdges:n0}; degenerate triangles: " +
                   $"{degenerateTriangles:n0}; source mode: {sourceMode}.\n" +
                   "Fix: Select one direct MeshFilter child. For an outer-surface-only mesh " +
                   "use Hollow Shell; otherwise repair open/non-manifold geometry or export a " +
                   "clean simplified fracture mesh." +
                   (details.Length > 0 ? "\nSolver details:" + details : string.Empty);
        }

        static void AnalyzeTopology(Mesh mesh, int[] tris, AnalysisReport report)
        {
            Vector3[] verts = mesh.vertices;

            // Weld duplicated positions so seams do not count as open edges.
            var weld = new Dictionary<Vector3Int, int>(verts.Length);
            var remap = new int[verts.Length];
            float cell = Mathf.Max(mesh.bounds.size.magnitude, 0.001f) * 1e-5f;
            for (int i = 0; i < verts.Length; i++)
            {
                var key = new Vector3Int(
                    Mathf.RoundToInt(verts[i].x / cell),
                    Mathf.RoundToInt(verts[i].y / cell),
                    Mathf.RoundToInt(verts[i].z / cell));
                if (weld.TryGetValue(key, out int existing)) remap[i] = existing;
                else { weld[key] = i; remap[i] = i; }
            }

            var edgeUse = new Dictionary<long, int>(tris.Length);
            var edgeDirection = new Dictionary<long, int>(tris.Length);
            int degenerate = 0;

            for (int t = 0; t < tris.Length; t += 3)
            {
                int a = remap[tris[t]], b = remap[tris[t + 1]], c = remap[tris[t + 2]];
                if (a == b || b == c || a == c) { degenerate++; continue; }

                Vector3 cross = Vector3.Cross(verts[b] - verts[a], verts[c] - verts[a]);
                if (cross.sqrMagnitude < 1e-16f) degenerate++;

                CountDirectedEdge(edgeUse, edgeDirection, a, b);
                CountDirectedEdge(edgeUse, edgeDirection, b, c);
                CountDirectedEdge(edgeUse, edgeDirection, c, a);
            }

            int open = 0, nonManifold = 0;
            foreach (var kv in edgeUse)
            {
                if (kv.Value == 1) open++;
                else if (kv.Value > 2 && edgeDirection[kv.Key] != 0) nonManifold++;
            }

            report.OpenEdges = open;
            report.NonManifoldEdges = nonManifold;
            report.DegenerateTriangles = degenerate;
            report.IsClosedVolume = open == 0;

            if (report.IsFlatSource)
            {
                // Reported instead of QD-GEO-005, not alongside it. A flat panel is open by
                // definition, and telling the user to cap holes in their DCC sends them
                // looking for a hole that does not exist.
                report.Items.Add(new AnalysisItem(AnalysisSeverity.Warning, "QD-GEO-012",
                    $"Flat source, no thickness ({report.BoundsLabel})",
                    "The mesh is a surface rather than a volume, so FULL SOLID has no " +
                    "interior to fill and can finish with no usable fragments. Modular " +
                    "walls, floors and billboards are normally authored this way.",
                    "Choose HOLLOW SHELL (Solidify) and set Wall Thickness to the thickness " +
                    "the piece should break with. Give the mesh real depth in your DCC only " +
                    "if the fragments must have authored interior geometry."));
            }
            else if (open > 0)
            {
                report.Items.Add(new AnalysisItem(AnalysisSeverity.Warning, "QD-GEO-005",
                    $"Open mesh ({open} boundary edges)",
                    "Cut caps assume a closed volume; open boundaries produce approximated interior surfaces.",
                    "For an outer-surface barrel, tank or pipe choose HOLLOW SHELL. Otherwise close/cap the holes in your DCC."));
            }
            if (degenerate > 0)
            {
                report.Items.Add(new AnalysisItem(AnalysisSeverity.Warning, "QD-GEO-008",
                    $"Degenerate triangles ({degenerate})",
                    "Zero-area faces produce NaN normals in fragments.",
                    "They are skipped by the solver; consider a mesh cleanup pass."));
            }
        }

        static void CountEdge(Dictionary<long, int> edges, int a, int b)
        {
            int lo = a < b ? a : b, hi = a < b ? b : a;
            long key = ((long)lo << 32) | (uint)hi;
            edges.TryGetValue(key, out int count);
            edges[key] = count + 1;
        }

        static void CountDirectedEdge(Dictionary<long, int> edges,
            Dictionary<long, int> directions, int a, int b)
        {
            int lo = a < b ? a : b, hi = a < b ? b : a;
            long key = ((long)lo << 32) | (uint)hi;
            edges.TryGetValue(key, out int count);
            edges[key] = count + 1;

            // Imported hard-normal/UV seams require positional welding. When two closed
            // shells merely touch along the same spatial edge, every shell still supplies
            // one forward and one reverse face edge. Keep those balanced pairs separate
            // conceptually instead of reporting their combined incidence as non-manifold.
            // A real T-junction or unpaired fan remains directionally unbalanced.
            directions.TryGetValue(key, out int direction);
            directions[key] = direction + (a == lo ? 1 : -1);
        }
    }
}
