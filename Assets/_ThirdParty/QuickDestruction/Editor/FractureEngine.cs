using System;
using System.Collections.Generic;
using UnityEngine;
using RipVertices.QuickDestruction.Runtime;

namespace RipVertices.QuickDestruction.Editor
{
    internal class DestructionProfileData
    {
        public FracturePattern Pattern;
        public int FragmentCount;
        public int Seed;
        public bool Solidify;
        public float SolidifyThickness;
        public int SecondaryPiecesPerFragment;
        public float Density;
        public float MinFragmentMass;
        public float MaxFragmentMass;
        public float FragmentShapeIrregularity;
        public float ConcreteIrregularity;
        public AxisMode GlassNormalAxis;
        public int RadialSpokes;
        public int RadialRings;
        public float RadialJitter;
        public float DirectionalCutStrength;
        public AxisMode PreferredCutAxis;
        public float LengthwiseSplitBias;
        public float WoodSplitIrregularity;
        public float WoodEndSharpness;
        public bool BrickUseCellSize;
        public Vector3 BrickCellSize;
        public float BrickJitter;
        public AxisMode SlabNormalAxis;
        public int SlabLayers;
        public float SlabJitter;
        public AxisMode PillarAxis;
        public float ColumnJitter;
        public float ImpactFocus;
        public bool ImpactPointOverride;
        public Vector3 ImpactPointLocal;
        public float InteriorScatterLength;
        public float InteriorUVScale;
        public CutSurfaceUVMode CutSurfaceUVMode;
        public float SecondaryIrregularity;
        public bool IsPreview;
        // Dedicated authoring mode: adjacent modular solids are intentionally treated as
        // one building shell when their contact faces match exactly. Normal prop fracture
        // keeps the conservative per-shell behavior.
        public bool MergeContactingSourceShells;
        // Bake-only opt-in. Preview and ordinary fracture runs avoid retaining a second
        // full source mesh unless the generated Intact visual explicitly needs it.
        public bool CapturePreparedSource;

        public DestructionProfileData(DestructionProfile p)
        {
            if (p == null) return;
            Pattern = p.Pattern;
            FragmentCount = p.FragmentCount;
            Seed = p.Seed;
            Solidify = p.Solidify;
            SolidifyThickness = p.SolidifyThickness;
            SecondaryPiecesPerFragment = p.SecondaryPiecesPerFragment;
            Density = p.Density;
            MinFragmentMass = p.MinFragmentMass;
            MaxFragmentMass = p.MaxFragmentMass;
            FragmentShapeIrregularity = p.FragmentShapeIrregularity;
            ConcreteIrregularity = p.ConcreteIrregularity;
            GlassNormalAxis = p.GlassNormalAxis;
            RadialSpokes = p.RadialSpokes;
            RadialRings = p.RadialRings;
            RadialJitter = p.RadialJitter;
            DirectionalCutStrength = p.DirectionalCutStrength;
            PreferredCutAxis = p.PreferredCutAxis;
            LengthwiseSplitBias = p.LengthwiseSplitBias;
            WoodSplitIrregularity = p.WoodSplitIrregularity;
            WoodEndSharpness = p.WoodEndSharpness;
            BrickUseCellSize = p.BrickUseCellSize;
            BrickCellSize = p.BrickCellSize;
            BrickJitter = p.BrickJitter;
            SlabNormalAxis = p.SlabNormalAxis;
            SlabLayers = p.SlabLayers;
            SlabJitter = p.SlabJitter;
            PillarAxis = p.PillarAxis;
            ColumnJitter = p.ColumnJitter;
            ImpactFocus = p.ImpactFocus;
            ImpactPointOverride = p.ImpactPointOverride;
            ImpactPointLocal = p.ImpactPointLocal;
            InteriorScatterLength = p.InteriorScatterLength;
            InteriorUVScale = p.InteriorUVScale;
            CutSurfaceUVMode = p.CutSurfaceUVMode;
            SecondaryIrregularity = p.SecondaryIrregularity;
        }

        public DestructionProfileData(DestructionProfileData p)
        {
            if (p == null) return;
            Pattern = p.Pattern;
            FragmentCount = p.FragmentCount;
            Seed = p.Seed;
            Solidify = p.Solidify;
            SolidifyThickness = p.SolidifyThickness;
            SecondaryPiecesPerFragment = p.SecondaryPiecesPerFragment;
            Density = p.Density;
            MinFragmentMass = p.MinFragmentMass;
            MaxFragmentMass = p.MaxFragmentMass;
            FragmentShapeIrregularity = p.FragmentShapeIrregularity;
            ConcreteIrregularity = p.ConcreteIrregularity;
            GlassNormalAxis = p.GlassNormalAxis;
            RadialSpokes = p.RadialSpokes;
            RadialRings = p.RadialRings;
            RadialJitter = p.RadialJitter;
            DirectionalCutStrength = p.DirectionalCutStrength;
            PreferredCutAxis = p.PreferredCutAxis;
            LengthwiseSplitBias = p.LengthwiseSplitBias;
            WoodSplitIrregularity = p.WoodSplitIrregularity;
            WoodEndSharpness = p.WoodEndSharpness;
            BrickUseCellSize = p.BrickUseCellSize;
            BrickCellSize = p.BrickCellSize;
            BrickJitter = p.BrickJitter;
            SlabNormalAxis = p.SlabNormalAxis;
            SlabLayers = p.SlabLayers;
            SlabJitter = p.SlabJitter;
            PillarAxis = p.PillarAxis;
            ColumnJitter = p.ColumnJitter;
            ImpactFocus = p.ImpactFocus;
            ImpactPointOverride = p.ImpactPointOverride;
            ImpactPointLocal = p.ImpactPointLocal;
            InteriorScatterLength = p.InteriorScatterLength;
            InteriorUVScale = p.InteriorUVScale;
            CutSurfaceUVMode = p.CutSurfaceUVMode;
            SecondaryIrregularity = p.SecondaryIrregularity;
            IsPreview = p.IsPreview;
            MergeContactingSourceShells = p.MergeContactingSourceShells;
            CapturePreparedSource = p.CapturePreparedSource;
        }
    }

    /// <summary>
    /// Converts author-facing scene-unit distances into the local coordinate space used by
    /// the fracture solver. Imported FBX meshes are commonly tiny in mesh space and restored
    /// to their intended size by an 86x/100x Transform; feeding a scene-unit shell thickness
    /// directly to such a mesh makes the generated inner wall cross the whole object.
    /// </summary>
    internal static class SolverProfileSpace
    {
        public static DestructionProfileData ForMeshTransform(
            DestructionProfileData source, Vector3 meshWorldScale)
        {
            if (source == null) return null;
            var local = new DestructionProfileData(source);
            if (local.Solidify)
                local.SolidifyThickness = WorldToMeshDistance(
                    source.SolidifyThickness, meshWorldScale);
            return local;
        }

        public static float WorldToMeshDistance(float worldDistance,
            Vector3 meshWorldScale)
        {
            float greatestAxisScale = Mathf.Max(
                Mathf.Abs(meshWorldScale.x),
                Mathf.Max(Mathf.Abs(meshWorldScale.y),
                    Mathf.Abs(meshWorldScale.z)));
            if (greatestAxisScale < 1e-6f ||
                float.IsNaN(greatestAxisScale) ||
                float.IsInfinity(greatestAxisScale))
                return worldDistance;

            // A scalar local offset cannot be perfectly uniform under non-uniform scale.
            // Dividing by the largest axis is conservative: no transformed wall normal can
            // become thicker than the value the artist requested or fold through the source.
            return worldDistance / greatestAxisScale;
        }
    }

    /// <summary>Result of a fracture run.</summary>
    internal class FractureResult
    {
        public List<WorkMesh> Fragments = new List<WorkMesh>();
        public List<float> Volumes = new List<float>();
        public float SourceVolume;
        public List<string> Warnings = new List<string>();
        public bool Cancelled;
        // Authoring surface after Whole Building seam repair, but before Hollow Shell
        // generation or fracture cuts. Bake uses this exact topology for intact collider
        // islands and structural probe groups so a repaired modular wall cannot fracture
        // as one surface in Preview yet receive separate support groups at runtime.
        public WorkMesh StructuralSource;
        public WorkMesh PreparedSource;
    }

    /// <summary>
    /// Deterministic fracture solver. The same seed + pattern + input always produces the same
    /// partition. Each pattern is a complete rule set, not just a different Voronoi seed:
    ///  - Uniform / Concrete: recursive largest-volume splits with random planes.
    ///  - Radial / Glass: spokes + concentric ring cuts around an impact origin.
    ///  - Directional / Wood: cuts biased along the fiber (long) axis producing splinters.
    ///  - Brick / Cells: jittered axis-aligned grid cuts.
    /// </summary>
    internal static class FractureEngine
    {
        internal const int MaxRadialGlassFragments = 64;

        /// <summary>Half-volumes below this fraction of the parent fragment are
        /// slivers; such cuts are retried with another plane before one is accepted.</summary>
        const float MinBalancedVolumeRatio = 0.10f;

        /// <summary>Fragments below this fraction of the source volume are merged into
        /// the neighbor with the largest shared contact area instead of staying flakes
        /// or being dropped, both of which left visible gaps between fragments.</summary>
        const float MicroFragmentMergeRatio = 1e-4f;
        /// <summary>Per-split profiling logs fire from the background solver thread and
        /// are very expensive in the Editor; enable only while actively profiling.</summary>
        const bool VerboseSplitProfiling = false;
        /// <summary>Solver timing summaries. Off for shipped builds: a user baking their
        /// own props should get a clean Console, not our profiling trace.</summary>
        internal const bool VerboseSolverProfiling = false;

        // Geometry imported from a large FBX can be stored in centimetre-sized mesh
        // coordinates and compensated by a 100x transform in the hierarchy. Absolute
        // weld/cap tolerances must not make that representation fracture differently
        // from the same object exported on its own in metre-sized coordinates.
        const float MinStableSolverExtent = 0.1f;
        const float MaxStableSolverExtent = 100f;
        const float NormalizedSolverExtent = 3f;

        /// <param name="progress">Called with 0..1; return false to cancel.</param>
        public static FractureResult Fracture(Mesh source, DestructionProfile profile,
            Func<float, bool> progress = null)
        {
            return Fracture(source, new DestructionProfileData(profile), progress);
        }

        public static FractureResult Fracture(Mesh source, DestructionProfileData profile,
            Func<float, bool> progress = null)
        {
            return Fracture(WorkMesh.FromMesh(source), profile, progress);
        }

        public static FractureResult Fracture(WorkMesh root, DestructionProfile profile,
            Func<float, bool> progress = null)
        {
            return Fracture(root, new DestructionProfileData(profile), progress);
        }

        public static FractureResult Fracture(WorkMesh root, DestructionProfileData profile,
            Func<float, bool> progress = null)
        {
            float solverScale = GetSolverNormalizationScale(root);
            if (Mathf.Approximately(solverScale, 1f))
                return FractureNormalized(root, profile, progress);

            WorkMesh normalizedRoot = ScaleWorkMesh(root, solverScale);
            DestructionProfileData normalizedProfile =
                ScaleSpatialProfile(profile, solverScale);
            FractureResult result =
                FractureNormalized(normalizedRoot, normalizedProfile, progress);
            RestoreResultScale(result, solverScale);
            return result;
        }

        static FractureResult FractureNormalized(WorkMesh root,
            DestructionProfileData profile, Func<float, bool> progress)
        {
            var swTotal = System.Diagnostics.Stopwatch.StartNew();
            var swStep = System.Diagnostics.Stopwatch.StartNew();
            var result = new FractureResult();
            MeshSlicer.ResetDiagnostics();
            var rng = new System.Random(profile.Seed);

            // A modular wall is often placed by eye or arrives through an exporter with
            // sub-centimetre transform drift. Exact topology then sees every authored row
            // as a separate open surface, Hollow Shell caps each row independently, and
            // the preview exposes a ruler-straight strip boundary. Whole Building is the
            // explicit opt-in for one continuous source, so repair only complete matching
            // boundary edges here. Assembly scale, wall thickness and the matching edge
            // length all cap the automatic tolerance, preserving authored openings and
            // nearby parallel walls.
            if (profile.MergeContactingSourceShells)
            {
                Bounds stitchBounds = root.ComputeBounds();
                float stitchDiagonal = Mathf.Max(stitchBounds.size.magnitude, 1e-3f);
                float conservativeTolerance = stitchDiagonal * 0.001f;
                // Stock Unity Plane rims are subdivided into one-metre edges. Capping the
                // repair at 0.5% of each small edge made a visually continuous ten-metre
                // wall fail after only 5 mm of placement drift. Whole Building may heal up
                // to 0.5% of the assembly scale, but never more than a quarter of the wall
                // thickness; StitchMatchingBoundaryEdges applies a second 2% edge cap.
                float thicknessTolerance = profile.Solidify &&
                                           profile.SolidifyThickness > 1e-5f
                    ? profile.SolidifyThickness * 0.25f
                    : stitchDiagonal * 0.005f;
                float stitchTolerance = Mathf.Max(conservativeTolerance,
                    Mathf.Min(stitchDiagonal * 0.005f, thicknessTolerance));
                stitchTolerance = Mathf.Max(stitchTolerance, 1e-5f);
                int stitchedEdges = root.StitchMatchingBoundaryEdges(stitchTolerance);
                if (stitchedEdges > 0)
                    result.Warnings.Add(
                        $"Whole Building stitched {stitchedEdges} near-matching modular " +
                        $"seam edge(s) within {stitchTolerance:0.####} source units so " +
                        "fracture pieces can cross authored segment boundaries.");
            }

            // Dropping coincident back-to-back caps is destructive and must stay the last
            // resort. It rewrites the contact rim from a four-face edge (wall + cap of the
            // lower shell, wall + cap of the upper one) into an ordinary two-face edge, so
            // the plain manifold rule in SplitConnectedComponents then unions a column's
            // wall straight onto the arch's wall and fuses solids the author kept apart.
            // Worse, only caps that match triangle for triangle are removed, so the rest
            // survive with both neighbours gone and fall out as orphaned zero-thickness
            // sheets. Whenever the split already separates the shells, per-shell fracture
            // keeps each one watertight WITH its own cap and the doubled wall stays
            // harmlessly buried inside the solid — there is nothing to gain by removing it.
            // Closedness is deliberately NOT required to consider the shells separable: a
            // single stray non-manifold seam anywhere in the source must not push an
            // otherwise cleanly separable model down the fusing path.
            List<WorkMesh> probeShells = root.SplitConnectedComponents();
            int separableShellCount = probeShells.Count;
            if (separableShellCount <= 1 || profile.MergeContactingSourceShells)
            {
                int mergedWalls = root.RemoveInternalContactFaces(
                    profile.MergeContactingSourceShells);
                if (mergedWalls > 0)
                    result.Warnings.Add(
                        profile.MergeContactingSourceShells
                            ? $"Whole Building mode merged {mergedWalls / 2} exactly matching " +
                              "module contact wall(s) into one continuous fracture shell."
                            : $"Merged {mergedWalls / 2} coincident internal wall(s): the source is a " +
                              "single connected shell, so its doubled interior faces were removed.");
            }
            // Classify disconnected source elements before any shell generation. Looking
            // only at face direction relative to the bounds of the whole object makes a
            // fence's outward-facing planks look like the inner wall of a barrel. A
            // disconnected authored shell must also contain a genuinely nested bounds
            // pair; otherwise its islands are independent fracture elements.
            List<WorkMesh> originalSourceComponents =
                root.SplitConnectedComponents();
            // Keep a stable copy before the solver mutates or solidifies root. Rebuilding
            // these components later from the original Unity Mesh would throw away the
            // Whole Building stitching above and recreate authored row boundaries as
            // separate support-probe groups.
            result.StructuralSource = root.Clone();
            bool hasInwardSurface = root.HasLikelyAuthoredInnerShell(
                out float authoredInwardRatio);
            // The inward-area heuristic weighs every face against the bounds CENTRE of the
            // whole object, which carries no meaning for a non-convex assembly: on an arch
            // spanning two columns that centre falls in the empty air under the arch, so
            // the facing sides of both columns and the whole underside of the arch read as
            // "inward" and a completely solid prop is classified as hollow. Bounds nesting
            // cannot break the tie either — an arch encloses its columns' bounds exactly
            // like an outer wall encloses an inner one. An authored inner shell is built
            // with reversed normals and therefore reports NEGATIVE signed volume, which no
            // independent solid ever does. Demanding that evidence keeps the heuristic
            // working for genuine hollow meshes while a Pi-shaped assembly of solids stays
            // on the per-shell path. A single component keeps the old test: a one-piece
            // tube is hollow yet still sums to a positive volume.
            bool preserveNestedShell = hasInwardSurface &&
                (originalSourceComponents.Count <= 1 ||
                 (HasNestedComponentBounds(originalSourceComponents) &&
                  AnyInwardFacingComponent(originalSourceComponents)));
            List<WorkMesh> independentlyPreparedComponents = null;
            bool rootWasSolidified = false;
            if (profile.Solidify)
            {
                if (profile.MergeContactingSourceShells)
                {
                    // A modular building may deliberately mix zero-thickness Plane walls
                    // with ordinary watertight walls that already have authored depth.
                    // Hollow Shell is the assembly-wide profile mode, but it is not an
                    // instruction to hollow every closed module a second time. Prepare
                    // each topological component independently, then restore the same
                    // combined solver source used by Whole Building.
                    List<WorkMesh> preparedComponents =
                        PrepareMergedHollowShellComponents(
                            originalSourceComponents,
                            profile.SolidifyThickness,
                            profile.CutSurfaceUVMode,
                            profile.InteriorUVScale,
                            out int generatedShellCount,
                            out int preservedSolidCount);
                    if (preparedComponents.Count > 1)
                    {
                        // A modular building is one destructible object, not one solver
                        // shell.  Each continuous coplanar wall/floor has its own Hollow
                        // Shell cavity.  Combining those disconnected cavities before a
                        // global Concrete/Brick cut lets one exported fragment contain an
                        // island on the right wall and another on the left wall or floor.
                        // A hit then activates both remote islands as one Rigidbody and
                        // exposes long open-looking strips.  Wood happened to avoid this
                        // through its Auto Axis path; preserve the prepared surfaces for
                        // every pattern so all presets obey the same spatial contract.
                        independentlyPreparedComponents = preparedComponents;
                    }
                    else
                    {
                        root = CombinePreparedMeshes(preparedComponents);
                        rootWasSolidified = generatedShellCount > 0;
                    }

                    // At least one generated inner surface must stay coupled to its outer
                    // surface during global fracture. A building containing only authored
                    // closed solids follows the ordinary per-solid route instead.
                    preserveNestedShell = generatedShellCount > 0;
                    result.Warnings.Add(
                        $"Whole Building Hollow Shell added Wall Thickness to " +
                        $"{generatedShellCount} open surface component(s) and preserved " +
                        $"the authored thickness of {preservedSolidCount} closed " +
                        "volumetric component(s).");
                }
                else if (preserveNestedShell)
                {
                    result.Warnings.Add(
                        $"Solidify was skipped: approximately {authoredInwardRatio:P0} of the source " +
                        "surface faces inward, which indicates an authored inner shell. " +
                        "Generating another inner wall would create overlapping faces.");
                }
                else
                {
                    // Preserve original element boundaries before Solidify creates a
                    // disconnected inner surface. Each plank/post then remains one
                    // logical shell and resolves Auto Axis from its own bounds.
                    if (UsesAutomaticAxis(profile))
                    {
                        List<WorkMesh> originalComponents =
                            originalSourceComponents;
                        if (originalComponents.Count > 1)
                        {
                            independentlyPreparedComponents =
                                new List<WorkMesh>(originalComponents.Count);
                            for (int i = 0; i < originalComponents.Count; i++)
                                independentlyPreparedComponents.Add(
                                    originalComponents[i].Solidified(
                                        profile.SolidifyThickness,
                                        profile.CutSurfaceUVMode,
                                        profile.InteriorUVScale));
                        }
                    }

                    // Do not build the same shell twice. When Auto Axis needs the
                    // disconnected source elements, those independently solidified
                    // elements are the actual fracture inputs. Re-solidifying the full
                    // source here used to duplicate all offset/rim work and could also
                    // make touching planks influence one another's averaged normals.
                    if (independentlyPreparedComponents == null)
                    {
                        root = root.Solidified(profile.SolidifyThickness,
                            profile.CutSurfaceUVMode, profile.InteriorUVScale);
                        rootWasSolidified = true;
                    }
                    // A generated inner surface is one logical shell even when a closed
                    // source has no boundary rim that physically shares its vertices.
                    preserveNestedShell = true;
                }
            }

            // Full Solid means "this is a solid": a flat open boundary such as the top of
            // a post that butts against a roof is meant to be capped, not preserved. The
            // planar fill only touches single, flat loops and leaves authored openings —
            // windows, doorways, cavities (all caught by preserveNestedShell) — alone, so
            // it seals the incidental hole without filling deliberate ones. Only runs in
            // Full Solid, so the source was never Solidified. Both the whole root (used
            // when one axis serves all) and the split components (used when Auto Axis
            // fractures each element) are sealed before volume is measured; only one set
            // is actually fractured.
            if (!profile.Solidify)
            {
                int sealedLoops = MeshSlicer.FillPlanarSourceHoles(
                    root, profile.InteriorUVScale, profile.CutSurfaceUVMode);
                for (int i = 0; i < originalSourceComponents.Count; i++)
                    sealedLoops += MeshSlicer.FillPlanarSourceHoles(
                        originalSourceComponents[i], profile.InteriorUVScale,
                        profile.CutSurfaceUVMode);
                if (sealedLoops > 0)
                    result.Warnings.Add(
                        $"Full Solid sealed {sealedLoops} flat open boundary(s) on the " +
                        "source (for example a post top hidden by a roof) so cut caps close.");
            }

            if (profile.CapturePreparedSource)
            {
                result.PreparedSource = independentlyPreparedComponents != null
                    ? CombinePreparedMeshes(independentlyPreparedComponents)
                    : root.Clone();
            }

            if (independentlyPreparedComponents != null)
            {
                result.SourceVolume = 0f;
                for (int i = 0; i < independentlyPreparedComponents.Count; i++)
                    result.SourceVolume +=
                        independentlyPreparedComponents[i].ComputeVolume();
            }
            else
            {
                result.SourceVolume = root.ComputeVolume();
            }

            List<WorkMesh> sourceComponents =
                independentlyPreparedComponents ?? originalSourceComponents;

            // A connected shell that encloses no volume is not a fragment, it is a leftover
            // cap sheet from a contact seam. Left in the list it is handed a fragment
            // budget of its own (one piece, never split, because every share is weighted by
            // volume) and ships as a flat zero-thickness object that z-fights its former
            // neighbour and cooks into a degenerate convex hull.
            int discardedSheets = DiscardDegenerateShells(sourceComponents);
            if (discardedSheets > 0)
                result.Warnings.Add(
                    $"Discarded {discardedSheets} zero-thickness surface sheet(s) carrying no " +
                    "volume; they would otherwise have shipped as flat fragments.");

            for (int i = 0; i < sourceComponents.Count; i++)
                sourceComponents[i].StructuralGroup = i;

            // Two closed manifolds combined into one object — an arch spanning columns, a
            // lid on a box — touch along a plane where each keeps its own cap. Fracturing
            // the merged whole cuts through that internal double-wall and a fragment then
            // shows the hidden cap as a stray flipped sheet, or a partial-contact seam
            // tears a hole. SplitConnectedComponents already keeps them apart (a contact
            // edge has four incident faces, so it is never joined). Fracturing each
            // component on its own keeps every one watertight, with no internal wall to
            // expose and nothing to hole.
            //
            // Connectivity alone decides this. Two earlier conditions have been removed
            // because both punished the whole model for one bad part:
            //   * Auto Axis was never relevant — the merge artefact has nothing to do with
            //     how the cut axis is resolved, as the paragraph above already stated.
            //   * Watertightness was all-or-nothing, so two torn shells out of seven
            //     dragged the five healthy ones onto the merged path. A torn shell still
            //     fractures best on its own; it simply gets approximated caps, and that
            //     damage stays confined to the part that is actually broken.
            // The one remaining reason to fracture disconnected islands together is an
            // authored hollow shell, where the inner wall must share the outer wall's
            // planes so the generated caps connect them instead of sealing the cavity.
            bool multipleClosedShells = sourceComponents.Count > 1 &&
                                        AllClosedManifolds(sourceComponents) &&
                                        AllOutwardFacing(sourceComponents);
            bool modularGlassSurfaces =
                profile.Pattern == FracturePattern.RadialGlass &&
                profile.MergeContactingSourceShells &&
                independentlyPreparedComponents != null;
            bool perComponent = sourceComponents.Count > 1 &&
                                (modularGlassSurfaces ||
                                 (profile.Pattern != FracturePattern.RadialGlass &&
                                  (!preserveNestedShell ||
                                   independentlyPreparedComponents != null ||
                                   multipleClosedShells)));

            // Whether each authored shell is fractured on its own decides if a shared cap
            // can be torn off as a stray sheet, so the reason is reported rather than
            // guessed at from the result.
            if (sourceComponents.Count > 1)
            {
                int closedCount = 0;
                for (int i = 0; i < sourceComponents.Count; i++)
                    if (sourceComponents[i] != null && sourceComponents[i].IsClosedManifold())
                        closedCount++;

                // When the shell count does not match what the author expects, the only
                // way to tell a real extra part from an artefact is to see the parts. Size
                // and watertightness per shell say immediately whether the extra ones are
                // genuine props, loose trim, or slivers that should never have been split.
                var shellReport = new System.Text.StringBuilder(
                    "Shell breakdown (triangles, volume, watertight): ");
                for (int i = 0; i < sourceComponents.Count; i++)
                {
                    WorkMesh shell = sourceComponents[i];
                    if (i > 0) shellReport.Append("; ");
                    if (shell == null) { shellReport.Append($"#{i + 1} empty"); continue; }
                    shellReport.Append(
                        $"#{i + 1} {shell.TriangleCount} tri, vol {shell.ComputeVolume():0.###}, " +
                        (shell.IsClosedManifold() ? "closed" : "OPEN"));
                }
                result.Warnings.Add(shellReport.ToString());

                result.Warnings.Add(
                    $"Source has {sourceComponents.Count} separate mesh shells " +
                    $"({closedCount} closed, {separableShellCount} found before preparation). " +
                    "Per-shell fracture: " +
                    (perComponent ? "ON" : "OFF (shells fracture merged)") +
                    (perComponent
                        ? closedCount < sourceComponents.Count
                            ? " — the shells that are not watertight still fracture on their own, " +
                              "with approximated caps confined to those parts."
                            : ""
                        : profile.Pattern == FracturePattern.RadialGlass
                            ? " — ordinary multi-island glass keeps one radial pane pattern; " +
                              "Whole Building Hollow Shell panes are separated instead."
                        : preserveNestedShell
                            ? " — classified as one hollow shell (an inner surface faces inward)."
                            : " — the source uses one merged fracture shell."));
            }
            var fragments = perComponent
                ? new List<WorkMesh>()
                : new List<WorkMesh> { root };
            // Parts is a hard physical-piece ceiling. Earlier Hollow Shell Wood solves
            // requested a 17% surplus to compensate for later micro-fragment merging;
            // that made an authored 80 legitimately bake as 85+ and, on difficult cuts,
            // could amplify island separation far beyond the requested count.
            int solverFragmentTarget = profile.FragmentCount;
            long prepMs = swStep.ElapsedMilliseconds;
            swStep.Restart();

            if (perComponent)
            {
                int[] targets = AllocateComponentTargets(
                    sourceComponents, solverFragmentTarget);
                for (int i = 0; i < sourceComponents.Count; i++)
                {
                    var componentFragments = new List<WorkMesh> { sourceComponents[i] };
                    int componentIndex = i;
                    Func<float, bool> componentProgress = progress == null
                        ? null
                        : value => progress((componentIndex + Mathf.Clamp01(value)) /
                                            sourceComponents.Count);

                    switch (profile.Pattern)
                    {
                        case FracturePattern.UniformConcrete:
                            FractureUniform(componentFragments, profile, rng,
                                componentProgress, result, targets[i],
                                profile.FragmentShapeIrregularity);
                            break;
                        case FracturePattern.DirectionalWood:
                            FractureDirectional(componentFragments, profile, rng,
                                componentProgress, result, targets[i]);
                            break;
                        case FracturePattern.RadialGlass:
                            FractureRadial(componentFragments, profile, rng,
                                componentProgress, result, targets[i]);
                            break;
                        case FracturePattern.BrickCells:
                            FractureBrick(componentFragments, profile, rng,
                                componentProgress, result, targets[i]);
                            break;
                        case FracturePattern.LayeredSlabs:
                            FractureSlabs(componentFragments, profile, rng,
                                componentProgress, result, targets[i]);
                            break;
                        case FracturePattern.ColumnarPillars:
                            FractureColumnar(componentFragments, profile, rng,
                                componentProgress, result, targets[i]);
                            break;
                        case FracturePattern.ImpactShatter:
                            FractureImpact(componentFragments, profile, rng,
                                componentProgress, result, targets[i]);
                            break;
                    }

                    if (result.Cancelled) return result;
                    fragments.AddRange(componentFragments);
                }

                result.Warnings.Add(
                    $"Fractured {sourceComponents.Count} disconnected mesh elements " +
                    "independently, so no cut plane crosses from one part into another" +
                    (UsesAutomaticAxis(profile)
                        ? ", and each resolved Auto Axis from its own local longest/thinnest " +
                          "dimension."
                        : "."));
            }
            else
            {
                if (preserveNestedShell)
                {
                    int islandCount = rootWasSolidified ? originalSourceComponents.Count * 2 : originalSourceComponents.Count;
                    if (islandCount > 1)
                    {
                        result.Warnings.Add(
                            $"Kept {islandCount} nested surface islands together as one " +
                            "hollow shell. Auto Axis and every fracture plane are shared by the " +
                            "outer and inner walls so generated caps connect them instead of " +
                            "closing the cavity separately.");
                    }
                }

                switch (profile.Pattern)
                {
                    case FracturePattern.UniformConcrete:
                        FractureUniform(fragments, profile, rng, progress, result,
                            profile.FragmentCount, profile.FragmentShapeIrregularity);
                        break;

                    case FracturePattern.DirectionalWood:
                        FractureDirectional(fragments, profile, rng, progress, result,
                            solverFragmentTarget);
                        break;

                    case FracturePattern.RadialGlass:
                        FractureRadial(fragments, profile, rng, progress, result);
                        break;

                    case FracturePattern.BrickCells:
                        FractureBrick(fragments, profile, rng, progress, result,
                            solverFragmentTarget);
                        break;

                    case FracturePattern.LayeredSlabs:
                        FractureSlabs(fragments, profile, rng, progress, result);
                        break;

                    case FracturePattern.ColumnarPillars:
                        FractureColumnar(fragments, profile, rng, progress, result);
                        break;

                    case FracturePattern.ImpactShatter:
                        FractureImpact(fragments, profile, rng, progress, result);
                        break;
                }
            }

            long patternMs = swStep.ElapsedMilliseconds;
            swStep.Restart();
            if (result.Cancelled) return result;
            CollectValidFragments(fragments, result,
                // In the per-element route every WorkMesh already represents exactly
                // one authored plank/post. Splitting it again by edge incidence can tear
                // a cut cap away when a repaired boundary temporarily has three welded
                // edge users, leaving a visible open end. Preserve the complete fragment
                // topology, including all cap triangles, as one result.
                perComponent || profile.Pattern == FracturePattern.RadialGlass ||
                preserveNestedShell);
            long collectMs = swStep.ElapsedMilliseconds;
            swStep.Restart();
            // Never delete individual cap triangles from a completed fragment. Doing so
            // after the slicer's manifold validation turns a closed cut into an open mesh.
            // Difficult topology is rejected while choosing the split instead.
            if (profile.Pattern != FracturePattern.RadialGlass && !preserveNestedShell)
            {
                SeparateDisconnectedIslandsInResult(result);
            }

            AppendConcreteTargetWarning(result, profile.Pattern,
                profile.FragmentCount);
            AppendCapChainWarning(result);
            long postMs = swStep.ElapsedMilliseconds;
            swTotal.Stop();
#pragma warning disable 0162
            if (VerboseSolverProfiling)
            UnityEngine.Debug.Log($"[QD Profile] FractureEngine.Fracture overall: {swTotal.ElapsedMilliseconds}ms " +
                $"(Prep/Solidify: {prepMs}ms, Pattern Solve: {patternMs}ms, Collect/Merge: {collectMs}ms, PostProcess: {postMs}ms). " +
                $"Produced {result.Fragments.Count} fragments.");
            return result;
        }

        /// <summary>
        /// Stores both children of a cut only when their real disconnected-island count
        /// fits the requested physical-piece ceiling. Islands are discovered once and
        /// reused for storage, keeping the guard cheap enough for Wood preview/bake.
        /// </summary>
        static bool TryStoreSplitChildren(List<WorkMesh> fragments,
            List<float> volumes, List<float> aspectRatios,
            List<int> retryCounts, int replaceIndex, WorkMesh a, WorkMesh b,
            int targetCount)
        {
            List<WorkMesh> aIslands = ExportableIslands(a);
            List<WorkMesh> bIslands = ExportableIslands(b);
            int aCount = aIslands != null ? aIslands.Count : 1;
            int bCount = bIslands != null ? bIslands.Count : 1;
            if (fragments.Count - 1 + aCount + bCount > targetCount)
                return false;

            StoreChild(replaceIndex, a, aIslands);
            StoreChild(-1, b, bIslands);
            return true;

            void StoreChild(int index, WorkMesh piece,
                List<WorkMesh> islands)
            {
                if (islands == null)
                {
                    Store(index, piece, piece.ComputeVolume());
                    return;
                }
                for (int i = 0; i < islands.Count; i++)
                {
                    WorkMesh island = islands[i];
                    Store(i == 0 ? index : -1, island,
                        island.ComputeVolume());
                }
            }

            void Store(int index, WorkMesh mesh, float meshVolume)
            {
                if (index >= 0)
                {
                    fragments[index] = mesh;
                    volumes[index] = meshVolume;
                    aspectRatios[index] = GetAspectRatio(mesh);
                    retryCounts[index] = 0;
                    return;
                }
                fragments.Add(mesh);
                volumes.Add(meshVolume);
                aspectRatios.Add(GetAspectRatio(mesh));
                retryCounts.Add(0);
            }
        }

        static List<WorkMesh> ExportableIslands(WorkMesh piece)
        {
            List<WorkMesh> islands = piece.SplitConnectedComponents();
            if (islands == null || islands.Count <= 1) return null;

            var valid = new List<WorkMesh>(islands.Count);
            for (int i = 0; i < islands.Count; i++)
                if (islands[i] != null && islands[i].TriangleCount >= 4)
                    valid.Add(islands[i]);
            // Keep the original child when every detected island is degenerate.
            return valid.Count > 0 ? valid : null;
        }

        /// <summary>
        /// Splits multi-island pieces produced by a global plane pass, so the iterative
        /// phase that follows starts from the true piece count and can aim at the
        /// requested Fragment Count instead of adding to an already inflated list.
        /// </summary>
        static void SeparateIslandsInList(List<WorkMesh> fragments)
        {
            if (fragments == null) return;
            for (int i = fragments.Count - 1; i >= 0; i--)
            {
                WorkMesh piece = fragments[i];
                if (piece == null) continue;
                List<WorkMesh> islands = piece.SplitConnectedComponents();
                if (islands == null || islands.Count <= 1) continue;

                bool first = true;
                for (int k = 0; k < islands.Count; k++)
                {
                    WorkMesh island = islands[k];
                    if (island == null || island.TriangleCount < 4) continue;
                    if (first)
                    {
                        fragments[i] = island;
                        first = false;
                    }
                    else
                    {
                        fragments.Add(island);
                    }
                }
            }
        }

        static void SeparateDisconnectedIslandsInResult(FractureResult result)
        {
            if (result == null || result.Fragments == null || result.Fragments.Count == 0)
                return;

            var finalFragments = new List<WorkMesh>();
            var finalVolumes = new List<float>();

            for (int i = 0; i < result.Fragments.Count; i++)
            {
                WorkMesh frag = result.Fragments[i];
                if (frag == null || frag.TriangleCount < 4) continue;

                List<WorkMesh> islands = frag.SplitConnectedComponents();
                if (islands != null && islands.Count > 1)
                {
                    for (int k = 0; k < islands.Count; k++)
                    {
                        WorkMesh island = islands[k];
                        if (island == null || island.TriangleCount < 4) continue;
                        finalFragments.Add(island);
                        finalVolumes.Add(island.ComputeVolume());
                    }
                }
                else
                {
                    finalFragments.Add(frag);
                    finalVolumes.Add(result.Volumes.Count > i ? result.Volumes[i] : frag.ComputeVolume());
                }
            }

            result.Fragments = finalFragments;
            result.Volumes = finalVolumes;
        }

        /// <summary>
        /// Uniformly fractures an already sliced primary WorkMesh while preserving its existing
        /// exterior and interior submesh layout. Used for deterministic pre-baked level two.
        /// </summary>
        public static FractureResult FractureSecondary(WorkMesh source,
            DestructionProfile profile, int seed, int targetCount,
            Func<float, bool> progress = null)
        {
            return FractureSecondary(source, new DestructionProfileData(profile), seed, targetCount, progress);
        }

        public static FractureResult FractureSecondary(WorkMesh source,
            DestructionProfileData profile, int seed, int targetCount,
            Func<float, bool> progress = null)
        {
            float solverScale = GetSolverNormalizationScale(source);
            if (Mathf.Approximately(solverScale, 1f))
                return FractureSecondaryNormalized(
                    source, profile, seed, targetCount, progress);

            WorkMesh normalizedSource = ScaleWorkMesh(source, solverScale);
            DestructionProfileData normalizedProfile =
                ScaleSpatialProfile(profile, solverScale);
            FractureResult result = FractureSecondaryNormalized(
                normalizedSource, normalizedProfile, seed, targetCount, progress);
            RestoreResultScale(result, solverScale);
            return result;
        }

        static FractureResult FractureSecondaryNormalized(WorkMesh source,
            DestructionProfileData profile, int seed, int targetCount,
            Func<float, bool> progress)
        {
            var result = new FractureResult();
            MeshSlicer.ResetDiagnostics();
            var rng = new System.Random(seed);
            var root = source.Clone();
            result.SourceVolume = root.ComputeVolume();
            var fragments = new List<WorkMesh> { root };

            FractureUniform(fragments, profile, rng, progress, result,
                Mathf.Max(2, targetCount), profile.SecondaryIrregularity,
                forceCutRepair: true);

            if (result.Cancelled) return result;
            CollectValidFragments(fragments, result, false, profile.InteriorUVScale, profile.CutSurfaceUVMode);
            SeparateDisconnectedIslandsInResult(result);
            AppendConcreteTargetWarning(result, profile.Pattern,
                Mathf.Max(2, targetCount));
            AppendCapChainWarning(result);
            return result;
        }

        static float GetSolverNormalizationScale(WorkMesh source)
        {
            if (source == null || source.Positions.Count == 0) return 1f;
            Vector3 size = source.ComputeBounds().size;
            float extent = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            if (extent <= 0f || float.IsNaN(extent) || float.IsInfinity(extent) ||
                (extent >= MinStableSolverExtent && extent <= MaxStableSolverExtent))
                return 1f;
            return NormalizedSolverExtent / extent;
        }

        static WorkMesh ScaleWorkMesh(WorkMesh source, float scale)
        {
            WorkMesh scaled = source.Clone();
            for (int i = 0; i < scaled.Positions.Count; i++)
                scaled.Positions[i] *= scale;
            return scaled;
        }

        static WorkMesh CombinePreparedMeshes(IReadOnlyList<WorkMesh> meshes)
        {
            int submeshCount = 1;
            for (int i = 0; i < meshes.Count; i++)
                if (meshes[i] != null)
                    submeshCount = Mathf.Max(submeshCount, meshes[i].SubmeshCount);

            var combined = new WorkMesh(submeshCount);
            for (int i = 0; i < meshes.Count; i++)
            {
                WorkMesh source = meshes[i];
                if (source == null) continue;
                int vertexOffset = combined.Positions.Count;
                combined.Positions.AddRange(source.Positions);
                combined.Normals.AddRange(source.Normals);
                combined.UVs.AddRange(source.UVs);
                for (int submesh = 0; submesh < source.SubmeshCount; submesh++)
                {
                    List<int> sourceTriangles = source.Submeshes[submesh];
                    List<int> targetTriangles = combined.Submeshes[submesh];
                    for (int triangle = 0; triangle < sourceTriangles.Count; triangle++)
                        targetTriangles.Add(sourceTriangles[triangle] + vertexOffset);
                }
            }
            return combined;
        }

        /// <summary>
        /// Applies an assembly-wide Hollow Shell profile without overwriting authored
        /// module thickness. Open surfaces need Solidify; closed manifolds already own a
        /// real volume and are copied unchanged. Kept internal so the editor regression
        /// suite can verify mixed modular sources without running the random fracture
        /// solver.
        /// </summary>
        internal static List<WorkMesh> PrepareMergedHollowShellComponents(
            IReadOnlyList<WorkMesh> sourceComponents, float thickness,
            CutSurfaceUVMode uvMode, float uvScale,
            out int generatedShellCount, out int preservedSolidCount)
        {
            generatedShellCount = 0;
            preservedSolidCount = 0;
            var prepared = new List<WorkMesh>(
                sourceComponents != null ? sourceComponents.Count : 0);
            if (sourceComponents == null) return prepared;

            for (int i = 0; i < sourceComponents.Count; i++)
            {
                WorkMesh component = sourceComponents[i];
                if (component == null || component.TriangleCount == 0) continue;
                if (component.IsClosedManifold())
                {
                    prepared.Add(component.Clone());
                    preservedSolidCount++;
                }
                else
                {
                    prepared.Add(component.Solidified(thickness, uvMode, uvScale));
                    generatedShellCount++;
                }
            }
            return prepared;
        }

        static DestructionProfileData ScaleSpatialProfile(
            DestructionProfileData source, float scale)
        {
            var scaled = new DestructionProfileData(source)
            {
                SolidifyThickness = source.SolidifyThickness * scale,
                BrickCellSize = source.BrickCellSize * scale,
                // Planar UVs multiply positions by this value. Counter-scaling keeps
                // authored cut-material tiling unchanged after numeric normalization.
                InteriorUVScale = source.InteriorUVScale / scale
            };
            return scaled;
        }

        static void RestoreResultScale(FractureResult result, float solverScale)
        {
            if (result == null || Mathf.Approximately(solverScale, 1f)) return;
            float inverse = 1f / solverScale;
            float inverseVolume = inverse * inverse * inverse;
            for (int fragmentIndex = 0;
                 fragmentIndex < result.Fragments.Count; fragmentIndex++)
            {
                WorkMesh fragment = result.Fragments[fragmentIndex];
                if (fragment == null) continue;
                for (int vertexIndex = 0;
                     vertexIndex < fragment.Positions.Count; vertexIndex++)
                    fragment.Positions[vertexIndex] *= inverse;
            }
            if (result.PreparedSource != null)
            {
                for (int vertexIndex = 0;
                     vertexIndex < result.PreparedSource.Positions.Count; vertexIndex++)
                    result.PreparedSource.Positions[vertexIndex] *= inverse;
            }
            if (result.StructuralSource != null)
            {
                for (int vertexIndex = 0;
                     vertexIndex < result.StructuralSource.Positions.Count; vertexIndex++)
                    result.StructuralSource.Positions[vertexIndex] *= inverse;
            }
            for (int i = 0; i < result.Volumes.Count; i++)
                result.Volumes[i] *= inverseVolume;
            result.SourceVolume *= inverseVolume;
        }

        static void AppendCapChainWarning(FractureResult result)
        {
            if (MeshSlicer.DuplicateCapLoopCount > 0)
            {
                result.Warnings.Add(
                    $"Removed {MeshSlicer.DuplicateCapLoopCount} coincident cut contour(s) " +
                    "before cap triangulation to prevent overlapping faces and z-fighting.");
            }
            if (MeshSlicer.UnchainedSegmentCount > 0)
            {
                result.Warnings.Add(
                    $"{MeshSlicer.UnchainedSegmentCount} cut segment(s) could not be chained " +
                    "within the normal weld tolerance; affected caps preserved their real " +
                    "edge order and closed the final numerical seam.");
            }
            if (MeshSlicer.HollowCapFailureCount > 0)
            {
                result.Warnings.Add(
                    $"{MeshSlicer.HollowCapFailureCount} hollow cut cap(s) required the " +
                    "watertight ring-stitch fallback. The cavity was preserved and the " +
                    "affected fragment boundaries were closed.");
            }
            if (MeshSlicer.FilledCutHoleCount > 0)
            {
                result.Warnings.Add(
                    $"Filled {MeshSlicer.FilledCutHoleCount} residual cut hole(s) after the " +
                    "primary cap pass. The repair reused the exact cut-plane boundary.");
            }
            if (MeshSlicer.RejectedNeedleCapCount > 0)
            {
                result.Warnings.Add(
                    $"Rejected {MeshSlicer.RejectedNeedleCapCount} needle cap triangle(s) " +
                    "that pointed outside the cut contour. Interior slivers were kept " +
                    "so the caps stay closed.");
            }
            if (MeshSlicer.UnresolvedCutBoundaryCount > 0)
            {
                result.Warnings.Add(
                    $"{MeshSlicer.UnresolvedCutBoundaryCount} cut result(s) retained a " +
                    "non-manifold boundary after both fill passes. The fracture was kept " +
                    "instead of silently returning an unbroken object.");
            }
        }

        static bool IsFlatSliver(WorkMesh mesh)
        {
            if (mesh == null || mesh.Positions.Count < 4) return true;
            Bounds bounds = mesh.ComputeBounds();
            Vector3 size = bounds.size;
            float longest = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            float shortest = Mathf.Min(size.x, Mathf.Min(size.y, size.z));
            if (longest <= 1e-5f) return true;
            if (shortest <= 0.003f || shortest <= longest * 0.015f) return true;
            return false;
        }

        static void AppendConcreteTargetWarning(FractureResult result,
            FracturePattern pattern, int requestedCount)
        {
            if (result == null || pattern != FracturePattern.UniformConcrete ||
                result.Fragments.Count >= requestedCount)
                return;
            result.Warnings.Add(
                $"Concrete fracture safely exported {result.Fragments.Count} of " +
                $"the configured maximum of {requestedCount}. The remaining candidates " +
                "would create an open boundary, an extreme needle or an invalid " +
                "micro-fragment merge, so they were not exported.");
        }

        /// <summary>
        /// Detects a cut-created concrete needle without modifying the mesh. The gate is
        /// deliberately conservative: it requires an extremely empty elongated bound, an
        /// isolated end, or a long tail whose transverse support tapers against the body.
        /// </summary>
        static bool HasExtremeConcreteNeedleShape(WorkMesh mesh,
            bool useVolumeHeuristics, float parentLongestAxis = 0f)
        {
            if (mesh == null || mesh.Positions.Count < 8) return false;
            Bounds bounds = mesh.ComputeBounds();
            Vector3 size = bounds.size;
            float shortest = Mathf.Min(size.x, Mathf.Min(size.y, size.z));
            float longest = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            float middle = size.x + size.y + size.z - shortest - longest;
            float boxVolume = size.x * size.y * size.z;
            if (shortest <= 1e-5f || middle <= 1e-5f ||
                boxVolume <= 1e-10f) return false;

            float volumeFill = Mathf.Abs(mesh.ComputeVolume()) / boxVolume;
            float longToMiddle = longest / middle;
            float longToShort = longest / shortest;
            if (useVolumeHeuristics && shortest < 0.10f * longest &&
                ((longToMiddle >= 3.75f && volumeFill <= 0.22f) ||
                 (longToShort >= 9f && volumeFill <= 0.12f)))
                return true;

            // A broad body can hide one absurdly long point from an AABB aspect
            // test. Weld render duplicates, aim from their centroid at the most
            // distant point, and look only for a large gap near either end of
            // that one-dimensional projection. This is linear apart from sorting
            // a small vertex list and runs only once during bake.
            float weldCell = Mathf.Max(bounds.size.magnitude * 1e-5f, 1e-6f);
            var weldedKeys = new HashSet<Vector3Int>();
            var points = new List<Vector3>();
            for (int i = 0; i < mesh.Positions.Count; i++)
            {
                Vector3 relative = mesh.Positions[i] - bounds.min;
                var key = new Vector3Int(
                    Mathf.RoundToInt(relative.x / weldCell),
                    Mathf.RoundToInt(relative.y / weldCell),
                    Mathf.RoundToInt(relative.z / weldCell));
                if (weldedKeys.Add(key)) points.Add(mesh.Positions[i]);
            }

            // Sparse spikes can still report a deceptively high signed volume
            // when their long side triangles form a large tetrahedral fan.
            if (points.Count >= 10)
            {
                Vector3 centroid = Vector3.zero;
                for (int i = 0; i < points.Count; i++) centroid += points[i];
                centroid /= points.Count;

                Vector3 farthest = points[0];
                float farthestSqr = -1f;
                for (int i = 0; i < points.Count; i++)
                {
                    float sqr = (points[i] - centroid).sqrMagnitude;
                    if (sqr <= farthestSqr) continue;
                    farthestSqr = sqr;
                    farthest = points[i];
                }

                Vector3 direction = farthest - centroid;
                if (direction.sqrMagnitude > 1e-10f)
                {
                    direction.Normalize();
                    var projections = new List<float>(points.Count);
                    for (int i = 0; i < points.Count; i++)
                        projections.Add(Vector3.Dot(points[i], direction));
                    projections.Sort();

                    float span = projections[projections.Count - 1] - projections[0];
                    int maxTail = Mathf.Max(3,
                        Mathf.FloorToInt(points.Count * 0.14f));
                    float bestGap = 0f;
                    for (int i = 1; i < projections.Count - 2; i++)
                    {
                        int leftCount = i + 1;
                        int rightCount = projections.Count - leftCount;
                        if (Mathf.Min(leftCount, rightCount) > maxTail) continue;
                        float gap = projections[i + 1] - projections[i];
                        if (gap <= bestGap) continue;
                        bestGap = gap;
                    }

                    if (span > 1e-5f && bestGap >= span * 0.20f)
                        return true;
                    if (span > 1e-5f && HasTaperedProjectionTail(points,
                            projections, centroid, direction, span, true))
                        return true;
                    if (span > 1e-5f && HasTaperedProjectionTail(points,
                            projections, centroid, direction, span, false))
                        return true;
                }
            }

            return false;
        }

        static bool HasTaperedProjectionTail(List<Vector3> points,
            List<float> sortedProjections, Vector3 centroid, Vector3 direction,
            float span, bool highEnd)
        {
            int count = sortedProjections.Count;
            int tailIndex = Mathf.RoundToInt((count - 1) * (highEnd ? 0.80f : 0.20f));
            int bodyIndex = Mathf.RoundToInt((count - 1) * (highEnd ? 0.70f : 0.30f));
            float tailCut = sortedProjections[tailIndex];
            float bodyCut = sortedProjections[bodyIndex];
            float tailLength = highEnd
                ? sortedProjections[count - 1] - tailCut
                : tailCut - sortedProjections[0];
            if (tailLength < span * 0.28f) return false;

            float tailRadius = 0f;
            float bodyRadius = 0f;
            for (int i = 0; i < points.Count; i++)
            {
                float projection = Vector3.Dot(points[i], direction);
                Vector3 delta = points[i] - centroid;
                float axial = Vector3.Dot(delta, direction);
                float radius = Mathf.Sqrt(Mathf.Max(0f,
                    delta.sqrMagnitude - axial * axial));
                bool inTail = highEnd ? projection >= tailCut : projection <= tailCut;
                bool inBody = highEnd ? projection <= bodyCut : projection >= bodyCut;
                if (inTail) tailRadius = Mathf.Max(tailRadius, radius);
                if (inBody) bodyRadius = Mathf.Max(bodyRadius, radius);
            }

            if (bodyRadius <= 1e-6f || tailRadius <= 1e-6f) return false;
            return tailRadius <= bodyRadius * 0.50f &&
                   span >= tailRadius * 6f &&
                   tailLength >= bodyRadius * 0.55f;
        }

        static void CollectValidFragments(List<WorkMesh> fragments, FractureResult result,
            bool keepSingleNestedShellTogether = false,
            float uvScale = 1f, CutSurfaceUVMode uvMode = CutSurfaceUVMode.PlanarProjection)
        {
            // Validate and collect. Components below the merge threshold are not
            // dropped outright: they are welded into the neighbor sharing the
            // largest contact area, which removes both the slivers themselves and
            // the gaps their rejection used to leave between fragments.
            float minVolume = result.SourceVolume * 1e-6f;
            float targetCount = fragments.Count > 0 ? Mathf.Max(2, fragments.Count) : 20;
            float mergeVolume = Mathf.Max(result.SourceVolume * 0.0005f, result.SourceVolume / (targetCount * 25f));
            float volumeSum = 0f;
            int separatedIslands = 0;
            var accepted = new List<WorkMesh>();
            var acceptedVolumes = new List<float>();
            var micro = new List<WorkMesh>();
            var microVolumes = new List<float>();
            foreach (var frag in fragments)
            {
                if (frag == null || frag.TriangleCount < 4) continue;
                List<WorkMesh> components = keepSingleNestedShellTogether
                    ? new List<WorkMesh> { frag }
                    : frag.SplitConnectedComponents();
                if (components.Count > 1) separatedIslands += components.Count - 1;
                for (int i = 0; i < components.Count; i++)
                {
                    WorkMesh component = components[i];
                    if (component == null || component.TriangleCount < 4) continue;
                    float vol = component.ComputeVolume();
                    bool isSliver = IsFlatSliver(component);
                    if (vol < mergeVolume || isSliver)
                    {
                        micro.Add(component);
                        microVolumes.Add(vol);
                        continue;
                    }
                    accepted.Add(component);
                    acceptedVolumes.Add(vol);
                }
            }

            var topologyProtectedMicro = new HashSet<WorkMesh>();
            MergeMicroFragmentsIntoNeighbors(accepted, acceptedVolumes,
                micro, microVolumes, topologyProtectedMicro);

            for (int i = 0; i < accepted.Count; i++)
            {
                WorkMesh acceptedMesh = accepted[i];
                List<WorkMesh> islands = keepSingleNestedShellTogether
                    ? new List<WorkMesh> { acceptedMesh }
                    : acceptedMesh.SplitConnectedComponents();

                for (int j = 0; j < islands.Count; j++)
                {
                    WorkMesh island = islands[j];
                    float vol = island.ComputeVolume();
                    result.Fragments.Add(island);
                    result.Volumes.Add(vol);
                    volumeSum += vol;
                }
            }
            // A merge candidate without any shared-edge neighbor keeps the previous
            // behavior: kept above the noise floor if valid, dropped if flat sliver.
            for (int i = 0; i < micro.Count; i++)
            {
                bool forceKeep = topologyProtectedMicro.Contains(micro[i]);
                if (!forceKeep &&
                    (microVolumes[i] < minVolume || IsFlatSliver(micro[i])))
                    continue;
                List<WorkMesh> islands = keepSingleNestedShellTogether
                    ? new List<WorkMesh> { micro[i] }
                    : micro[i].SplitConnectedComponents();

                for (int j = 0; j < islands.Count; j++)
                {
                    WorkMesh island = islands[j];
                    float vol = island.ComputeVolume();
                    result.Fragments.Add(island);
                    result.Volumes.Add(vol);
                    volumeSum += vol;
                }
            }

            if (separatedIslands > 0)
            {
                result.Warnings.Add(
                    $"Separated {separatedIslands} disconnected mesh island(s) into independent " +
                    "fragments so posts, trim and other loose elements receive their own " +
                    "Rigidbody and collider.");
            }

            if (result.SourceVolume > 0f)
            {
                float ratio = volumeSum / result.SourceVolume;
                if (ratio < 0.9f || ratio > 1.1f)
                    result.Warnings.Add(
                        $"Fragment volume sum is {ratio:P0} of the source volume. " +
                        "The source mesh is probably open or self-intersecting; caps were approximated.");
            }

            if (result.Fragments.Count < 2)
                result.Warnings.Add("Fracture produced fewer than 2 valid fragments.");
        }

        /// <summary>
        /// Welds sliver components into the accepted neighbor that shares the largest
        /// contact area. Adjacency uses one welded-edge pass over every triangle, the
        /// same incidence test SplitConnectedComponents uses, so detached cap islands
        /// and thin flakes rejoin the fragment they were cut from instead of becoming
        /// holes or dust. Merged entries are removed from the micro lists.
        /// </summary>
        static void MergeMicroFragmentsIntoNeighbors(List<WorkMesh> accepted,
            List<float> acceptedVolumes, List<WorkMesh> micro,
            List<float> microVolumes,
            HashSet<WorkMesh> topologyProtectedMicro)
        {
            if (micro.Count == 0 || accepted.Count == 0) return;

            // One global position weld across all candidates lets edges from
            // different fragments be compared by a single integer key.
            Bounds totalBounds = accepted[0].ComputeBounds();
            for (int i = 1; i < accepted.Count; i++)
                totalBounds.Encapsulate(accepted[i].ComputeBounds());
            for (int i = 0; i < micro.Count; i++)
                totalBounds.Encapsulate(micro[i].ComputeBounds());
            float weldCell = Mathf.Max(totalBounds.size.magnitude * 1e-6f, 1e-7f);

            // Neighborhood weld matching the slicer's PositionWelder: micro-fragment
            // edges straddling a quantization cell boundary must still find their owner,
            // or mergeable slivers look detached and survive as dust.
            var welder = new PositionWelder(weldCell);
            int Weld(Vector3 point) => welder.Weld(point);

            // Map every accepted-fragment edge to its owner. Two accepted fragments
            // sharing one geometric edge cannot happen after a plane partition.
            var edgeOwner = new Dictionary<long, int>();
            void RegisterEdge(int owner, int a, int b)
            {
                if (a == b) return;
                int lo = Mathf.Min(a, b);
                int hi = Mathf.Max(a, b);
                edgeOwner[((long)lo << 32) | (uint)hi] = owner;
            }

            for (int i = 0; i < accepted.Count; i++)
            {
                WorkMesh mesh = accepted[i];
                for (int s = 0; s < mesh.SubmeshCount; s++)
                {
                    List<int> triangles = mesh.Submeshes[s];
                    for (int t = 0; t + 2 < triangles.Count; t += 3)
                    {
                        int a = Weld(mesh.Positions[triangles[t]]);
                        int b = Weld(mesh.Positions[triangles[t + 1]]);
                        int c = Weld(mesh.Positions[triangles[t + 2]]);
                        RegisterEdge(i, a, b);
                        RegisterEdge(i, b, c);
                        RegisterEdge(i, c, a);
                    }
                }
            }

            void AppendTriangles(WorkMesh targetMesh, WorkMesh piece)
            {
                var vertexMap = new Dictionary<int, int>();
                for (int s = 0; s < targetMesh.SubmeshCount; s++)
                {
                    List<int> sourceTris = piece.Submeshes[s];
                    List<int> targetTris = targetMesh.Submeshes[s];
                    for (int i = 0; i < sourceTris.Count; i++)
                    {
                        int sourceIndex = sourceTris[i];
                        if (!vertexMap.TryGetValue(sourceIndex, out int targetIndex))
                        {
                            targetIndex = targetMesh.AddVertex(
                                piece.Positions[sourceIndex],
                                piece.Normals[sourceIndex],
                                piece.UVs[sourceIndex]);
                            vertexMap.Add(sourceIndex, targetIndex);
                        }
                        targetTris.Add(targetIndex);
                    }
                }
            }

            var merged = new bool[micro.Count];
            for (int i = 0; i < micro.Count; i++)
            {
                WorkMesh piece = micro[i];
                // Contact area per neighbor; one triangle counts once toward each
                // neighbor it touches, however many edges they share.
                var contactArea = new Dictionary<int, float>();
                for (int s = 0; s < piece.SubmeshCount; s++)
                {
                    List<int> triangles = piece.Submeshes[s];
                    for (int t = 0; t + 2 < triangles.Count; t += 3)
                    {
                        Vector3 va = piece.Positions[triangles[t]];
                        Vector3 vb = piece.Positions[triangles[t + 1]];
                        Vector3 vc = piece.Positions[triangles[t + 2]];
                        float area = Vector3.Cross(vb - va, vc - va).magnitude * 0.5f;
                        if (area <= 0f) continue;
                        int a = Weld(va);
                        int b = Weld(vb);
                        int c = Weld(vc);
                        int n0 = FindOwner(a, b);
                        int n1 = FindOwner(b, c);
                        int n2 = FindOwner(c, a);
                        if (n0 >= 0) AddContact(contactArea, n0, area);
                        if (n1 >= 0 && n1 != n0) AddContact(contactArea, n1, area);
                        if (n2 >= 0 && n2 != n0 && n2 != n1)
                            AddContact(contactArea, n2, area);
                    }
                }

                // Prefer a neighbor from the same structural group so merged slivers
                // do not blur the support-connectivity boundaries between elements.
                int bestNeighbor = -1;
                float bestArea = 0f;
                foreach (KeyValuePair<int, float> pair in contactArea)
                {
                    if (accepted[pair.Key].StructuralGroup != piece.StructuralGroup) continue;
                    if (pair.Value <= bestArea) continue;
                    bestArea = pair.Value;
                    bestNeighbor = pair.Key;
                }
                if (bestNeighbor < 0)
                {
                    // No same-group contact: fall back to the largest cross-group contact.
                    foreach (KeyValuePair<int, float> pair in contactArea)
                    {
                        if (pair.Value <= bestArea) continue;
                        bestArea = pair.Value;
                        bestNeighbor = pair.Key;
                    }
                }
                if (bestNeighbor < 0)
                {
                    // Fall back to finding the nearest accepted fragment by centroid distance,
                    // but ONLY if within immediate physical proximity (touching/adjacent).
                    Bounds pieceBounds = piece.ComputeBounds();
                    Vector3 pieceCenter = pieceBounds.center;
                    float maxDist = Mathf.Max(pieceBounds.size.magnitude * 1.5f, 0.05f);
                    float minSqDist = maxDist * maxDist;
                    for (int k = 0; k < accepted.Count; k++)
                    {
                        if (accepted[k].SubmeshCount != piece.SubmeshCount) continue;
                        float sqDist = (accepted[k].ComputeBounds().center - pieceCenter).sqrMagnitude;
                        if (sqDist < minSqDist)
                        {
                            minSqDist = sqDist;
                            bestNeighbor = k;
                        }
                    }
                }
                if (bestNeighbor < 0) continue; // no candidate within proximity: do not merge across empty space
                WorkMesh targetMesh = accepted[bestNeighbor];
                if (targetMesh.SubmeshCount != piece.SubmeshCount) continue;
                // Appending two touching closed volumes can turn their shared edge into
                // a four-use non-manifold edge. Build the combined candidate off to the
                // side and keep the micro fragment separate when that would happen.
                WorkMesh combined = targetMesh.Clone();
                AppendTriangles(combined, piece);
                if (targetMesh.IsClosedManifold() && piece.IsClosedManifold() &&
                    !combined.IsClosedManifold())
                {
                    topologyProtectedMicro?.Add(piece);
                    continue;
                }
                accepted[bestNeighbor] = combined;
                acceptedVolumes[bestNeighbor] += microVolumes[i];
                merged[i] = true;
            }

            for (int i = micro.Count - 1; i >= 0; i--)
            {
                if (!merged[i]) continue;
                micro.RemoveAt(i);
                microVolumes.RemoveAt(i);
            }

            int FindOwner(int a, int b)
            {
                if (a == b) return -1;
                int lo = Mathf.Min(a, b);
                int hi = Mathf.Max(a, b);
                return edgeOwner.TryGetValue(((long)lo << 32) | (uint)hi,
                    out int owner)
                    ? owner
                    : -1;
            }

            void AddContact(Dictionary<int, float> contactArea, int owner,
                float area)
            {
                contactArea.TryGetValue(owner, out float total);
                contactArea[owner] = total + area;
            }
        }

        readonly struct MeshSlicerDiagnostics
        {
            readonly int m_unchainedSegments;
            readonly int m_hollowCapFailures;
            readonly int m_duplicateCapLoops;
            readonly int m_filledCutHoles;
            readonly int m_filledSourceHoles;
            readonly int m_unresolvedCutBoundaries;
            readonly int m_rejectedNeedleCaps;

            MeshSlicerDiagnostics(bool capture)
            {
                m_unchainedSegments = MeshSlicer.UnchainedSegmentCount;
                m_hollowCapFailures = MeshSlicer.HollowCapFailureCount;
                m_duplicateCapLoops = MeshSlicer.DuplicateCapLoopCount;
                m_filledCutHoles = MeshSlicer.FilledCutHoleCount;
                m_filledSourceHoles = MeshSlicer.FilledSourceHoleCount;
                m_unresolvedCutBoundaries =
                    MeshSlicer.UnresolvedCutBoundaryCount;
                m_rejectedNeedleCaps = MeshSlicer.RejectedNeedleCapCount;
            }

            public static MeshSlicerDiagnostics Capture() =>
                new MeshSlicerDiagnostics(true);

            public int UnresolvedCutBoundaries => m_unresolvedCutBoundaries;

            public void Restore()
            {
                MeshSlicer.RestoreDiagnostics(m_unchainedSegments,
                    m_hollowCapFailures, m_duplicateCapLoops, m_filledCutHoles,
                    m_filledSourceHoles, m_unresolvedCutBoundaries,
                    m_rejectedNeedleCaps);
            }
        }

        // ------------------------------------------------------------------ iterative core

        static void FractureIterative(List<WorkMesh> fragments, int targetCount, System.Random rng,
            float uvScale, CutSurfaceUVMode uvMode, Func<WorkMesh, Plane> planePicker, Func<float, bool> progress,
            FractureResult result, int maxPlaneAttempts = 8,
            int safetyMultiplier = 6, bool repairCutTopology = true,
            bool allowFallback = true, float bakedInteriorJaggedness = 0f,
            BakedCutDetailStyle bakedCutDetailStyle = BakedCutDetailStyle.None,
            // A splinter is a bad fragment in every pattern, not only in Concrete: it
            // cooks into a degenerate collider, weighs almost nothing and reads as a
            // glitch. The shape test is fill-based, so genuine slabs and planks — which
            // are elongated but fill their box — are never mistaken for one.
            bool rejectNeedleShapes = true,
            bool isPreview = false,
            // Elongated pieces are normally re-cut first, because in rubble they are the
            // defect. Under a grain pattern they are the product, so the boost is off.
            bool boostElongated = true,
            int largePieceAttemptFloor = 24,
            Func<WorkMesh, int, Plane> rescuePlanePicker = null,
            bool enforceClosedChildren = true,
            // A modular Hollow Shell is authored from open panels and becomes a real
            // closed volume only in this solver.  Once it is closed, exporting an open
            // child is never an acceptable way to reach Max Parts: that child renders as
            // an empty wall strip and its collider no longer matches Preview.  Ordinary
            // props retain the historical late-retry relaxation so difficult legacy
            // meshes can still make progress.
            bool neverRelaxClosedChildren = false)
        {
            // Callers may pass an already partitioned list (e.g. slab sheets awaiting
            // cross cuts), so seed the volume cache from every existing fragment.
            var volumes = new List<float>(fragments.Count);
            var retryCounts = new List<int>(fragments.Count);
            var aspectRatios = new List<float>(fragments.Count);
            for (int i = 0; i < fragments.Count; i++)
            {
                volumes.Add(fragments[i].ComputeVolume());
                retryCounts.Add(0);
                aspectRatios.Add(GetAspectRatio(fragments[i]));
            }

            int safety = targetCount * Mathf.Max(1, safetyMultiplier);
            // A piece far above the per-fragment volume budget must never be abandoned:
            // the retry penalty exists to stop the loop from grinding on one stubborn
            // piece, but letting it bury an oversized one produces the signature failure
            // of one huge slab standing among correctly sized rubble.
            float totalVolume = 0f;
            for (int i = 0; i < volumes.Count; i++)
                totalVolume += Mathf.Max(0f, volumes[i]);
            float sourceVolume = result != null && result.SourceVolume > 0f
                ? result.SourceVolume
                : totalVolume;
            float oversizedVolume = 2.5f * sourceVolume / Mathf.Max(1, targetCount);
            var swIterative = System.Diagnostics.Stopwatch.StartNew();
            int totalPlaneAttempts = 0;
            int successfulSplits = 0;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (fragments.Count < targetCount && safety-- > 0)
            {
                sw.Restart();
                if (progress != null && !progress((float)fragments.Count / targetCount))
                {
                    result.Cancelled = true;
                    return;
                }

                int largest = 0;
                float bestScore = float.NegativeInfinity;
                for (int i = 0; i < fragments.Count; i++)
                {
                    float vol = volumes[i];
                    if (vol <= 0f) continue;

                    float retries = retryCounts[i];
                    // Oversized pieces keep most of their priority through retries.
                    float penalty = vol > oversizedVolume ? 0.25f : 1.5f;
                    float score = vol / (1f + retries * penalty);

                    float aspect = aspectRatios[i];
                    if (boostElongated && aspect > 3.5f)
                    {
                        score *= 1.5f;
                    }

                    if (score > bestScore)
                    {
                        bestScore = score;
                        largest = i;
                    }
                }

                var target = fragments[largest];
                float parentVolume = volumes[largest];
                // If the parent is a closed solid, accept only children that remain
                // completely closed. This catches holes away from the current cut plane
                // that a cut-boundary-only check cannot see.
                // Relax manifold requirements if we are struggling to split a large piece.
                bool requireClosedChildren = enforceClosedChildren &&
                    target.IsClosedManifold() &&
                    (neverRelaxClosedChildren || retryCounts[largest] < 5);
                bool split = false;

                float sourceVolCheck = result != null && result.SourceVolume > 0f ? result.SourceVolume : parentVolume;
                int effectiveMaxAttempts = maxPlaneAttempts;
                if (parentVolume > sourceVolCheck * 0.015f)
                {
                    effectiveMaxAttempts = isPreview
                        ? Mathf.Clamp(maxPlaneAttempts, 4, 6)
                        : Mathf.Max(effectiveMaxAttempts, largePieceAttemptFloor);
                }
                // An oversized piece that already burned one round gets a longer leash
                // even in the preview: leaving it huge costs more than the extra planes.
                if (parentVolume > oversizedVolume && retryCounts[largest] >= 1)
                    effectiveMaxAttempts = Mathf.Max(effectiveMaxAttempts, 12);
                int currentRetries = retryCounts[largest];
                bool currentRejectNeedles = rejectNeedleShapes &&
                    (currentRetries < 2 || parentVolume <= sourceVolCheck * 0.02f);

                // Reject lopsided cuts that shave a thin flake off the parent, but
                // remember the most balanced result so a stubborn fragment still
                // splits once every plane attempt has been spent.
                WorkMesh bestA = null;
                WorkMesh bestB = null;
                float bestAVolume = 0f;
                float bestBVolume = 0f;
                float bestBalance = -1f;
                MeshSlicerDiagnostics bestDiagnostics = default;
                bool hasBestDiagnostics = false;

                Bounds targetBounds = target.ComputeBounds();
                float parentLongest = Mathf.Max(targetBounds.size.x, Mathf.Max(targetBounds.size.y, targetBounds.size.z));

                for (int attempt = 0;
                     attempt < Mathf.Max(1, effectiveMaxAttempts) && !split; attempt++)
                {
                    // Preview replacement/cancel must not wait for a stubborn fragment's
                    // entire plane budget. Bake uses the same callback for its Cancel
                    // button, so this also bounds how long that button takes to respond.
                    if (attempt > 0 && progress != null &&
                        !progress((float)fragments.Count / targetCount))
                    {
                        result.Cancelled = true;
                        return;
                    }
                    MeshSlicerDiagnostics attemptDiagnostics =
                        MeshSlicerDiagnostics.Capture();
                    // Preserve every pattern's authored/random planes first. A pattern
                    // may optionally provide a few cheap, deterministic rescue planes
                    // once those have all failed. This avoids spending a 24-plane round
                    // repeatedly rediscovering the same bad seam on difficult shells.
                    bool rescuePlane = rescuePlanePicker != null &&
                                       attempt >= maxPlaneAttempts;
                    Plane plane = rescuePlane
                        ? rescuePlanePicker(target, attempt - maxPlaneAttempts)
                        : planePicker(target);
                    // Several patterns anchor every plane at the centroid, so failed
                    // attempts only ever vary the normal and a cut that hits one bad
                    // feature (a window arch, a non-manifold seam) fails identically all
                    // round. Sliding later attempts along the normal looks for a nearby
                    // parallel cut that misses the feature entirely. The slide is bounded
                    // by the piece's own extent along the normal — a fraction of the
                    // longest side can push the plane clean out of a flat piece, wasting
                    // every remaining attempt on cuts through empty air.
                    if (attempt >= 2 && !rescuePlane)
                    {
                        Vector3 n = plane.normal;
                        float extent = 0.5f * (
                            Mathf.Abs(n.x) * targetBounds.size.x +
                            Mathf.Abs(n.y) * targetBounds.size.y +
                            Mathf.Abs(n.z) * targetBounds.size.z);
                        float slide = ((float)rng.NextDouble() - 0.5f) *
                            Mathf.Min(0.2f + 0.06f * attempt, 0.6f) * extent;
                        plane.distance += slide;
                    }
                    totalPlaneAttempts++;
                    // Slice with requireClosedChildren: false first, to capture the meshes even if they fail manifold checks.
                    // Enforce clean caps on the first 4 attempt planes so normal fragments do not accept missing/broken caps.
                    bool requireCleanCap = attempt < 4 && currentRetries < 3;
                    bool sliceAccepted = TrySliceWithFallback(target, plane, uvScale, uvMode,
                            repairCutTopology, allowFallback, out var a, out var b,
                            bakedInteriorJaggedness, bakedCutDetailStyle,
                            requireClosedChildren: false,
                            requireCleanCap: requireCleanCap);

                    if (a == null || b == null || a.Positions.Count < 4 || b.Positions.Count < 4)
                    {
                        attemptDiagnostics.Restore();
                        continue;
                    }

                    if (currentRejectNeedles &&
                        (HasExtremeConcreteNeedleShape(a,
                             useVolumeHeuristics: requireClosedChildren, parentLongest) ||
                         HasExtremeConcreteNeedleShape(b,
                             useVolumeHeuristics: requireClosedChildren, parentLongest)))
                    {
                        attemptDiagnostics.Restore();
                        continue;
                    }

                    if (!sliceAccepted)
                    {
                        attemptDiagnostics.Restore();
                        continue;
                    }

                    // Closed-manifold verification comes BEFORE the candidate is
                    // remembered for the lopsided-cut fallback. The fallback used to keep
                    // the best-balanced pair regardless, so a cut whose cap failed to
                    // triangulate could be exported through it: a tiny needle sliced off a
                    // wall, and the wall left with an unfilled hole where the cap should
                    // be. An uncut larger chunk is always better than an open fragment.
                    if (requireClosedChildren && (!a.IsClosedManifold() || !b.IsClosedManifold()))
                    {
                        attemptDiagnostics.Restore();
                        continue;
                    }

                    float aVolume = a.ComputeVolume();
                    float bVolume = b.ComputeVolume();
                    float balance = Mathf.Min(aVolume, bVolume) /
                                    Mathf.Max(parentVolume, 1e-12f);
                    if (balance > bestBalance)
                    {
                        bestA = a;
                        bestB = b;
                        bestAVolume = aVolume;
                        bestBVolume = bVolume;
                        bestBalance = balance;
                        bestDiagnostics = MeshSlicerDiagnostics.Capture();
                        hasBestDiagnostics = true;
                    }

                    if (balance < MinBalancedVolumeRatio)
                    {
                        attemptDiagnostics.Restore();
                        continue;
                    }

                    if (!TryStoreSplitChildren(fragments, volumes, aspectRatios,
                            retryCounts, largest, a, b, targetCount))
                    {
                        attemptDiagnostics.Restore();
                        continue;
                    }
                    split = true;
                }

                if (!split && bestA != null && bestBalance >= 0.05f)
                {
                    if (TryStoreSplitChildren(fragments, volumes, aspectRatios,
                            retryCounts, largest, bestA, bestB, targetCount))
                    {
                        if (hasBestDiagnostics) bestDiagnostics.Restore();
                        split = true;
                    }
                }
                if (split) successfulSplits++;

                if (!split)
                {
                    int retries = retryCounts[largest] + 1;
                    retryCounts[largest] = retries;
                    if (retries >= 20 || (retries >= 8 && parentVolume <= sourceVolCheck * 0.005f))
                    {
                        volumes[largest] = -1f;
                    }
                    bool anyPositive = false;
                    for (int i = 0; i < volumes.Count; i++)
                        if (volumes[i] > 0f) { anyPositive = true; break; }
                    if (!anyPositive) break;
                }
#pragma warning disable 0162
                if (VerboseSplitProfiling)
                    UnityEngine.Debug.Log($"[QD Profile] Split {fragments.Count}/{targetCount} took {sw.ElapsedMilliseconds}ms (split={split}, targetTris={target.TriangleCount})");
#pragma warning restore 0162
            }
            swIterative.Stop();
#pragma warning disable 0162
            if (VerboseSolverProfiling)
            UnityEngine.Debug.Log($"[QD Profile] FractureIterative finished in {swIterative.ElapsedMilliseconds}ms: " +
                $"Reached {fragments.Count}/{targetCount} pieces. " +
                $"Attempts: {totalPlaneAttempts}, Accepted splits: {successfulSplits}.");
        }

        static Vector3 RandomPointNearCentroid(
            WorkMesh mesh, System.Random rng, float irregularity)
        {
            Bounds b = mesh.ComputeBounds();
            float offset = Mathf.Clamp(irregularity, 0f, 0.45f);
            return b.center + new Vector3(
                ((float)rng.NextDouble() - 0.5f) * b.size.x * offset,
                ((float)rng.NextDouble() - 0.5f) * b.size.y * offset,
                ((float)rng.NextDouble() - 0.5f) * b.size.z * offset);
        }

        static Vector3 RandomDirection(System.Random rng)
        {
            // Rejection-free spherical direction.
            float theta = (float)(rng.NextDouble() * Math.PI * 2.0);
            float z = (float)(rng.NextDouble() * 2.0 - 1.0);
            float r = Mathf.Sqrt(Mathf.Max(0f, 1f - z * z));
            return new Vector3(r * Mathf.Cos(theta), r * Mathf.Sin(theta), z);
        }

        /// <summary>
        /// Slices a prepared mesh through one deterministic route. Closed inputs use the
        /// direct cap builder; only a result that fails the manifold check is re-run with
        /// the generic boundary collector and hole filler. This is deliberately a retry,
        /// not another repair layer applied on top of already emitted faces.
        /// </summary>
        static bool TrySliceWithFallback(WorkMesh source, Plane plane,
            float uvScale, CutSurfaceUVMode uvMode, bool repairCutTopology,
            bool allowFallback, out WorkMesh positive, out WorkMesh negative,
            float bakedInteriorJaggedness = 0f,
            BakedCutDetailStyle bakedCutDetailStyle = BakedCutDetailStyle.None,
            bool requireClosedChildren = false,
            bool requireCleanCap = true)
        {
            MeshSlicerDiagnostics baseline = MeshSlicerDiagnostics.Capture();
            MeshSlicer.Slice(source, plane, uvScale, uvMode,
                out positive, out negative, repairCutTopology, bakedInteriorJaggedness,
                bakedCutDetailStyle);
            if (IsAcceptedSlice(positive, negative, baseline,
                    requireClosedChildren, requireCleanCap))
                return true;

            bool unresolved = MeshSlicer.UnresolvedCutBoundaryCount >
                              baseline.UnresolvedCutBoundaries;
            if (unresolved && bakedInteriorJaggedness > 0.0001f)
            {
                // The pre-baked facet pass is optional decoration. If it makes a
                // difficult cap fail its existing boundary validation, discard only
                // that decoration and retry the same plane with the proven planar cap.
                // Do not retain a transient warning once the safe retry succeeds.
                baseline.Restore();
                MeshSlicer.Slice(source, plane, uvScale, uvMode,
                    out positive, out negative, repairCutTopology,
                    bakedInteriorJaggedness: 0f,
                    bakedCutDetailStyle: BakedCutDetailStyle.None);
                if (IsAcceptedSlice(positive, negative, baseline,
                        requireClosedChildren, requireCleanCap))
                    return true;
            }

            if (!allowFallback)
            {
                baseline.Restore();
                positive = null;
                negative = null;
                return false;
            }

            // Retry from the exact pre-attempt diagnostics state. The fallback is
            // transactional: warnings from a discarded cut never leak into the
            // manifest and only the accepted geometry contributes counters.
            baseline.Restore();
            MeshSlicer.Slice(source, plane, uvScale, uvMode,
                out positive, out negative, repairCutTopology: true,
                bakedInteriorJaggedness: 0f,
                bakedCutDetailStyle: BakedCutDetailStyle.None);
            if (IsAcceptedSlice(positive, negative, baseline,
                    requireClosedChildren: false, requireCleanCap: false))
                return true;

            if (positive != null && negative != null &&
                positive.Positions.Count >= 4 && negative.Positions.Count >= 4)
            {
                return false;
            }

            baseline.Restore();
            positive = null;
            negative = null;
            return false;
        }

        static bool IsAcceptedSlice(WorkMesh positive, WorkMesh negative,
            MeshSlicerDiagnostics baseline, bool requireClosedChildren,
            bool requireCleanCap = true)
        {
            if (positive == null || negative == null)
                return false;

            if (requireCleanCap && MeshSlicer.UnresolvedCutBoundaryCount > baseline.UnresolvedCutBoundaries)
                return false;

            if (requireClosedChildren)
            {
                return positive.IsClosedManifold() && negative.IsClosedManifold();
            }

            return true;
        }

        // ------------------------------------------------------------------ optional directional frame

        static void FractureUniform(List<WorkMesh> fragments,
            DestructionProfileData profile, System.Random rng,
            Func<float, bool> progress, FractureResult result,
            int targetCount, float irregularity,
            bool forceCutRepair = false)
        {
            WorkMesh reference = fragments[0];
            ResolveDirectionalFrame(reference, profile,
                out Vector3 length, out Vector3 width, out Vector3 thickness);
            FractureIterative(fragments, targetCount, rng,
                profile.InteriorUVScale, profile.CutSurfaceUVMode, f =>
                {
                    Vector3 point = RandomPointNearCentroid(f, rng, irregularity);
                    Vector3 baseDir = RandomDirection(rng);
                    Vector3 tilt = RandomDirection(rng) * (irregularity * 1.8f);
                    Vector3 dir = (baseDir + tilt).normalized;
                    Vector3 normal = DirectionallyBiasedNormal(f, profile, rng,
                        dir, length, width, thickness);
                    return new Plane(normal, point);
                }, progress, result,
                repairCutTopology: forceCutRepair || !profile.Solidify,
                allowFallback: forceCutRepair || !profile.Solidify,
                bakedInteriorJaggedness: GetBakedCutDetail(profile),
                bakedCutDetailStyle: GetBakedCutDetailStyle(profile),
                isPreview: profile.IsPreview,
                neverRelaxClosedChildren:
                    profile.MergeContactingSourceShells && profile.Solidify);
        }

        static float GetBakedCutDetail(DestructionProfileData profile)
        {
            // The fracture mesh stays planar and watertight. Raised cap ridges were
            // tried here and read as MORE angular, not less: in flat-shaded and
            // wireframe views every ridge adds hard edges. Organic surface detail is
            // the interior scatter's job — a render-only layer with its own strength
            // control — while the felt shape of the rubble comes from where the cut
            // points land (Fragment Shape Irregularity), not from bending the caps.
            return 0f;
        }

        static BakedCutDetailStyle GetBakedCutDetailStyle(DestructionProfileData profile)
        {
            if (profile == null || profile.Pattern == FracturePattern.RadialGlass)
                return BakedCutDetailStyle.None;
            return profile.Pattern == FracturePattern.DirectionalWood
                ? BakedCutDetailStyle.WoodSplinters
                : BakedCutDetailStyle.ConcreteChips;
        }

        static void ResolveDirectionalFrame(WorkMesh reference,
            DestructionProfileData profile, out Vector3 length,
            out Vector3 width, out Vector3 thickness)
        {
            Bounds bounds = reference.ComputeBounds();
            length = profile.PreferredCutAxis == AxisMode.Auto
                ? LongestPrincipalAxis(reference)
                : ResolveAxis(profile.PreferredCutAxis, bounds.size);
            width = SecondaryPrincipalAxis(reference, length);
            thickness = Vector3.Cross(length, width).normalized;
        }

        static Vector3 DirectionallyBiasedNormal(WorkMesh fragment,
            DestructionProfileData profile, System.Random rng, Vector3 original,
            Vector3 length, Vector3 width, Vector3 thickness)
        {
            float strength = profile.Pattern == FracturePattern.UniformConcrete
                ? 0f
                : Mathf.Clamp01(profile.DirectionalCutStrength);

            bool forceCrossCut = false;
            float shapeSteering = 0f;
            Vector3 shapeLongAxis = length;
            if (fragment != null && fragment.Positions.Count >= 4)
            {
                // Proportion control, applied continuously instead of as a rescue at the
                // last moment. Splitting a piece along its long side leaves two pieces
                // worse-proportioned than the parent, and the greedy loop feeds the
                // biggest one straight back in, so a purely random normal lets shapes
                // drift from chunky to wedge over successive cuts and never back. The
                // steering starts as soon as a piece leaves square-ish proportions and
                // reaches full strength at the sliver ratio the old guard waited for, so
                // a well-proportioned fragment keeps its completely free random cut.
                Bounds fBounds = fragment.ComputeBounds();
                Vector3 fSize = fBounds.size;
                float aspect;
                bool grainPattern =
                    profile.Pattern == FracturePattern.DirectionalWood ||
                    profile.Pattern == FracturePattern.ColumnarPillars;
                if (grainPattern)
                {
                    // A splinter or a column is supposed to be long: measuring it against
                    // its own length would call every good result a defect and chop the
                    // plank into a grid. Only the cross-section counts here, so a ribbon
                    // is still corrected while a long, squarely-sectioned splinter is
                    // left exactly as the pattern intends.
                    float acrossWidth = ExtentAlong(fSize, width);
                    float acrossThickness = ExtentAlong(fSize, thickness);
                    float wideSide = Mathf.Max(acrossWidth, acrossThickness);
                    float thinSide = Mathf.Min(acrossWidth, acrossThickness);
                    aspect = thinSide > 1e-6f ? wideSide / thinSide : 1f;
                }
                else
                {
                    float fLongestVal = Mathf.Max(fSize.x, Mathf.Max(fSize.y, fSize.z));
                    float fShortestVal = Mathf.Min(fSize.x, Mathf.Min(fSize.y, fSize.z));
                    aspect = fShortestVal > 1e-6f
                        ? fLongestVal / fShortestVal
                        : 1f;
                }
                // The dimension that triggered the correction is the one the corrective
                // cut has to cross. Using the profile's Preferred Length Axis here would
                // aim the fix at a direction the piece is not even long in: with the axis
                // pinned to X, a fragment that grew tall in Y could only ever be sliced
                // into more tall fragments, which is why the debris ended up half upright
                // and half flat.
                shapeLongAxis = grainPattern
                    // Correct a ribbon across its wide cross-grain side, never across the
                    // grain itself: cutting a splinter shorter does not make it thicker.
                    ? (ExtentAlong(fSize, width) >= ExtentAlong(fSize, thickness)
                        ? width
                        : thickness)
                    : fSize.x >= fSize.y && fSize.x >= fSize.z
                        ? Vector3.right
                        : fSize.y >= fSize.z ? Vector3.up : Vector3.forward;
                float shapeBias = Mathf.SmoothStep(0f, 1f,
                    Mathf.InverseLerp(SquarishAspect, SliverAspect, aspect));
                shapeSteering = CrossCutSteering * shapeBias;
                // An explicit Directional Influence is an artistic instruction and keeps
                // priority: proportion control then only steps in once a piece is past
                // the sliver ratio, where the shape is a defect rather than a style.
                float overrideChance = shapeBias >= 1f
                    ? 0.95f
                    : (0.4f + 0.55f * shapeBias) * (1f - strength);
                forceCrossCut = shapeBias > 0f && rng.NextDouble() < overrideChance;
            }

            // Directional Influence 0 means "no preferred direction", not "no shape
            // control at all". A free random plane through an already elongated piece
            // halves it lengthwise about as often as across, and each such cut leaves it
            // thinner and longer — that is where the long wedges come from, not from
            // noise. A well-proportioned fragment keeps its fully free cut; only one that
            // already reads as a sliver is steered, and only far enough to cut it across.
            float steering = forceCrossCut
                ? Mathf.Max(strength, shapeSteering)
                : strength;
            bool sharpWoodEnds = profile.Pattern == FracturePattern.DirectionalWood &&
                                 profile.WoodEndSharpness > 0.0001f;
            if (steering <= 0.0001f && !sharpWoodEnds) return original.normalized;

            // Wood and Slabs own an explicit lengthwise/crosswise split control. Every
            // other pattern derives it from the slider itself, so Directional Influence
            // produces a visible trend instead of only sharpening two competing cut
            // families: at 0 the choice is a coin flip and the steering is zero anyway,
            // and the higher it goes, the more cuts run along the preferred axis and the
            // more the debris reads as grain in one consistent direction.
            float bias = (profile.Pattern == FracturePattern.DirectionalWood ||
                          profile.Pattern == FracturePattern.LayeredSlabs)
                ? profile.LengthwiseSplitBias
                : Mathf.Lerp(0.5f, 0.85f, strength);

            bool alongLength = !forceCrossCut && rng.NextDouble() < bias;
            Vector3 preferred;
            if (alongLength)
            {
                // Any normal perpendicular to the length axis produces a cut plane that
                // contains it, so the piece stays long in that direction. Wood keeps a
                // width-facing preference, because a plank really does split along its
                // flat face. Everything else rotates freely around the axis: snapping to
                // the width made the plane family jump between two orientations whenever
                // the per-fragment secondary axis flipped, and the debris came out half
                // upright and half flat even though one axis was explicitly chosen.
                if (profile.Pattern == FracturePattern.DirectionalWood)
                {
                    float deviation = ((float)rng.NextDouble() * 2f - 1f) * 0.12f;
                    preferred = (width + thickness * deviation).normalized;
                }
                else
                {
                    float angle = (float)(rng.NextDouble() * Math.PI * 2.0);
                    preferred = (width * Mathf.Cos(angle) +
                                 thickness * Mathf.Sin(angle)).normalized;
                }
            }
            else
            {
                // A length-facing normal cuts the element across its local width. When the
                // cut was requested by proportion control rather than by the style bias,
                // it crosses the axis the piece is actually too long in instead.
                Vector3 crossAxis = forceCrossCut ? shapeLongAxis : length;
                Vector3 tilt = (width * ((float)rng.NextDouble() * 2f - 1f) +
                    thickness * ((float)rng.NextDouble() * 2f - 1f)).normalized;
                if (tilt.sqrMagnitude < 1e-6f) tilt = width;
                tilt = Vector3.ProjectOnPlane(tilt, crossAxis);
                if (tilt.sqrMagnitude < 1e-6f) tilt = Vector3.zero;
                else tilt = tilt.normalized;

                // Without an angular cone every cross cut of one piece shares one exact
                // normal, and repeated proportion corrections slice it into a stack of
                // parallel slabs — bread, not rubble. Wood keeps its authored sharpness.
                bool woodEnd = profile.Pattern == FracturePattern.DirectionalWood;
                float sharpness = Mathf.Clamp01(profile.WoodEndSharpness);
                float tiltStrength = woodEnd ? sharpness * 1.35f : 0.4f;
                preferred = (crossAxis + tilt * tiltStrength).normalized;

                // The oblique end only survives if the plane is actually steered toward
                // it. Directional Influence normally decides that, so at a middling
                // setting the sharpness slider was mostly averaged away against the
                // random normal and felt like it did nothing. A break end is a local
                // shape decision, not a global direction, so it carries its own weight.
                if (woodEnd && sharpness > 0.0001f)
                    steering = Mathf.Max(steering, Mathf.Lerp(0.35f, 0.9f, sharpness));
            }

            original = original.sqrMagnitude > 1e-8f ? original.normalized : preferred;
            if (Vector3.Dot(original, preferred) < 0f) preferred = -preferred;
            return Vector3.Slerp(original, preferred, steering).normalized;
        }

        /// <summary>
        /// How hard a sliver is steered back into a cross cut when the profile itself asks
        /// for no directional bias. Enough to dominate the random plane, short of 1 so the
        /// result still varies.
        /// </summary>
        const float CrossCutSteering = 0.75f;

        /// <summary>Long/short ratio still treated as chunky: cuts stay fully free.</summary>
        const float SquarishAspect = 1.6f;

        /// <summary>Long/short ratio treated as a splinter: cuts are steered fully across.</summary>
        const float SliverAspect = 4.5f;

        // ------------------------------------------------------------------ directional / wood

        static void FractureDirectional(List<WorkMesh> fragments, DestructionProfileData profile,
            System.Random rng, Func<float, bool> progress, FractureResult result,
            int targetCount = -1)
        {
            ResolveDirectionalFrame(fragments[0], profile,
                out Vector3 fiber, out Vector3 width, out Vector3 thickness);
            bool repairCutTopology = !profile.Solidify;

            // The first four planes below retain the fully authored Wood distribution.
            // Only a fragment that rejected all four enters this rescue set. Covering
            // the two grain-preserving planes and one cross-grain plane deterministically
            // succeeds on curved/open shells far more often than another twenty random
            // normals, without changing pieces that already fractured normally.
            Plane RescuePlane(WorkMesh fragment, int rescueIndex)
            {
                Bounds bounds = fragment.ComputeBounds();
                bool lengthwiseFirst = profile.LengthwiseSplitBias >= 0.5f;
                int family = rescueIndex % 3;
                Vector3 normal;
                if (lengthwiseFirst)
                    normal = family == 0 ? width : family == 1 ? thickness : fiber;
                else
                    normal = family == 0 ? fiber : family == 1 ? width : thickness;

                int pass = rescueIndex / 3;
                float signedOffset = pass == 0
                    ? 0f
                    : (pass % 2 == 1 ? 0.16f : -0.16f);
                float extent = 0.5f * (
                    Mathf.Abs(normal.x) * bounds.size.x +
                    Mathf.Abs(normal.y) * bounds.size.y +
                    Mathf.Abs(normal.z) * bounds.size.z);
                Vector3 point = bounds.center + normal * (signedOffset * extent);
                return new Plane(normal, point);
            }

            FractureIterative(fragments,
                targetCount > 0 ? targetCount : profile.FragmentCount,
                rng, profile.InteriorUVScale, profile.CutSurfaceUVMode, f =>
            {
                Bounds b = f.ComputeBounds();
                Vector3 normal = DirectionallyBiasedNormal(f, profile, rng,
                    RandomDirection(rng), fiber, width, thickness);
                // WoodSplitIrregularity is presentation-only scatter. Keep the
                // underlying split placement fixed for a given seed.
                const float splitOffset = 0.24f;
                Vector3 point = b.center + new Vector3(
                    ((float)rng.NextDouble() - 0.5f) * b.size.x * splitOffset,
                    ((float)rng.NextDouble() - 0.5f) * b.size.y * splitOffset,
                    ((float)rng.NextDouble() - 0.5f) * b.size.z * splitOffset);
                return new Plane(normal, point);
            }, progress, result, maxPlaneAttempts: 4, safetyMultiplier: 3,
                repairCutTopology: repairCutTopology,
                allowFallback: !profile.Solidify,
                bakedInteriorJaggedness: GetBakedCutDetail(profile),
                bakedCutDetailStyle: GetBakedCutDetailStyle(profile),
                boostElongated: false,
                isPreview: profile.IsPreview,
                largePieceAttemptFloor: 10,
                rescuePlanePicker: RescuePlane,
                // Ordinary legacy Solidify sources retain the permissive retry route.
                // Whole Building Hollow Shell is different: its open modular panels were
                // deliberately turned into closed physical walls/floors, so Brick may
                // never trade that closure for reaching its requested cell count.
                enforceClosedChildren:
                    !profile.Solidify || profile.MergeContactingSourceShells,
                neverRelaxClosedChildren:
                    profile.Solidify && profile.MergeContactingSourceShells);
        }

        // ------------------------------------------------------------------ radial / glass

        static void FractureRadial(List<WorkMesh> fragments, DestructionProfileData profile,
            System.Random rng, Func<float, bool> progress, FractureResult result,
            int fragmentCeiling = MaxRadialGlassFragments)
        {
            fragmentCeiling = Mathf.Clamp(fragmentCeiling, 1,
                MaxRadialGlassFragments);
            Bounds bounds = fragments[0].ComputeBounds();
            Vector3 axis = ResolvePaneNormal(
                fragments[0], profile.GlassNormalAxis);
            Vector3 origin = bounds.center;

            Vector3 basisU = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.95f ? Vector3.up : Vector3.right).normalized;
            Vector3 basisV = Vector3.Cross(axis, basisU);

            int spokes = profile.RadialSpokes;
            int rings = profile.RadialRings;

            // 1. Spokes: Planes containing the axis, through the impact origin.
            float baseOffset = (float)rng.NextDouble() * Mathf.PI * 2f;
            var spokeAngles = new List<float>();
            float avgSpokeAngle = Mathf.PI * 2f / spokes;
            for (int i = 0; i < spokes; i++)
            {
                float baseAng = baseOffset + i * avgSpokeAngle;
                // Increased from 1.5f to 2.5f for much stronger irregularity
                float jitter = ((float)rng.NextDouble() - 0.5f) * profile.RadialJitter * avgSpokeAngle * 2.5f;
                spokeAngles.Add(baseAng + jitter);
            }
            spokeAngles.Sort();

            float minSpokeSpacing = avgSpokeAngle * 0.15f;
            for (int i = 1; i < spokes; i++)
            {
                if (spokeAngles[i] < spokeAngles[i - 1] + minSpokeSpacing)
                    spokeAngles[i] = spokeAngles[i - 1] + minSpokeSpacing;
            }
            if (spokeAngles[0] + Mathf.PI * 2f < spokeAngles[spokes - 1] + minSpokeSpacing)
                spokeAngles[spokes - 1] = spokeAngles[0] + Mathf.PI * 2f - minSpokeSpacing;

            var spokeBisectors = new List<Vector3>();
            var spokePlanes = new List<Plane>();
            for (int i = 0; i < spokes; i++)
            {
                float ang = spokeAngles[i];
                float nextAng = spokeAngles[(i + 1) % spokes];
                if (i == spokes - 1) nextAng += Mathf.PI * 2f;

                Vector3 normal = basisU * Mathf.Sin(ang) - basisV * Mathf.Cos(ang);
                spokePlanes.Add(new Plane(normal, origin));

                float midAng = (ang + nextAng) * 0.5f;
                spokeBisectors.Add((basisU * Mathf.Cos(midAng) + basisV * Mathf.Sin(midAng)).normalized);
            }

            // Apply spoke planes first to partition source mesh into spoke wedges
            ApplyGlobalPlanes(fragments, spokePlanes, profile.InteriorUVScale, profile.CutSurfaceUVMode, progress, result,
                repairCutTopology: true,
                allowFallback: true,
                bakedInteriorJaggedness: GetBakedCutDetail(profile),
                bakedCutDetailStyle: GetBakedCutDetailStyle(profile),
                fragmentCeiling: fragmentCeiling,
                reportCeiling: false);

            if (result.Cancelled || rings <= 0) return;

            // 2. Rings: Slice each spoke wedge locally using aligned spoke bisectors
            var finalFragments = new List<WorkMesh>(fragments.Count * (rings + 1));
            bool reachedGlassCeiling = fragments.Count >= fragmentCeiling;
            for (int f = 0; f < fragments.Count; f++)
            {
                WorkMesh wedge = fragments[f];
                int unprocessedWedges = fragments.Count - f - 1;
                int wedgeCeiling = fragmentCeiling -
                                   finalFragments.Count - unprocessedWedges;
                // Reserve one output for every remaining spoke wedge. This keeps the
                // complete pane visible while spending only the spare budget on rings.
                if (wedgeCeiling <= 1)
                {
                    finalFragments.Add(wedge);
                    reachedGlassCeiling |= rings > 0;
                    continue;
                }
                MeasurePaneRadii(wedge, origin, basisU, basisV, out float minRadius, out float maxRadius);
                if (maxRadius <= minRadius + 1e-4f)
                {
                    finalFragments.Add(wedge);
                    continue;
                }

                // Determine radial direction vector for THIS wedge from its centroid
                Vector3 centroid = wedge.ComputeBounds().center;
                Vector3 offset = Vector3.ProjectOnPlane(centroid - origin, axis);
                Vector3 radialDir = offset.sqrMagnitude > 1e-6f ? offset.normalized : basisU;

                // Match to the closest bisector so ring cuts align seamlessly across spokes
                float bestDot = -2f;
                Vector3 bestBisector = radialDir;
                for (int b = 0; b < spokeBisectors.Count; b++)
                {
                    float d = Vector3.Dot(radialDir, spokeBisectors[b]);
                    if (d > bestDot)
                    {
                        bestDot = d;
                        bestBisector = spokeBisectors[b];
                    }
                }

                var radii = new List<float>();
                for (int j = 1; j <= rings; j++)
                {
                    float t = (j - 0.5f) / rings;
                    // Increased from 2.0f to 3.5f for much stronger irregularity
                    float jitter = ((float)rng.NextDouble() - 0.5f) * profile.RadialJitter * 3.5f / rings;
                    t = Mathf.Clamp01(t + jitter);
                    
                    float curveT = Mathf.Pow(t, 1.2f);
                    radii.Add(Mathf.Lerp(minRadius, maxRadius, curveT));
                }
                radii.Sort();

                float minSpacing = (maxRadius - minRadius) * 0.1f / Mathf.Max(1, rings);
                for (int j = 1; j < radii.Count; j++)
                {
                    if (radii[j] < radii[j - 1] + minSpacing)
                        radii[j] = radii[j - 1] + minSpacing;
                }

                var ringPlanes = new List<Plane>();
                for (int j = 0; j < radii.Count; j++)
                {
                    ringPlanes.Add(new Plane(bestBisector, origin + bestBisector * radii[j]));
                }

                var wedgeList = new List<WorkMesh> { wedge };
                ApplyGlobalPlanes(wedgeList, ringPlanes, profile.InteriorUVScale, profile.CutSurfaceUVMode, null, result,
                    repairCutTopology: true,
                    allowFallback: true,
                    bakedInteriorJaggedness: GetBakedCutDetail(profile),
                    bakedCutDetailStyle: GetBakedCutDetailStyle(profile),
                    fragmentCeiling: wedgeCeiling,
                    reportCeiling: false);

                finalFragments.AddRange(wedgeList);
                reachedGlassCeiling |= wedgeList.Count >= wedgeCeiling;
            }

            fragments.Clear();
            fragments.AddRange(finalFragments);
            if (reachedGlassCeiling)
            {
                result.Warnings.Add(
                    $"Radial Glass reached the {fragmentCeiling}-piece immediate " +
                    "physics limit. Some ring cuts were skipped, but the complete pane " +
                    "still breaks in one frame without queued activation.");
            }
        }

        /// <summary>
        /// In-plane distance band actually occupied by the pane, measured from the radial
        /// origin. A solid pane reports a band starting near zero; an arch or any frame
        /// with an opening reports the inner edge, which is where its first ring belongs.
        /// </summary>
        static void MeasurePaneRadii(WorkMesh mesh, Vector3 origin,
            Vector3 basisU, Vector3 basisV, out float minRadius, out float maxRadius)
        {
            minRadius = 0f;
            maxRadius = 1f;
            if (mesh == null || mesh.Positions.Count == 0) return;

            var radii = new List<float>(mesh.Positions.Count);
            for (int i = 0; i < mesh.Positions.Count; i++)
            {
                Vector3 offset = mesh.Positions[i] - origin;
                float u = Vector3.Dot(offset, basisU);
                float v = Vector3.Dot(offset, basisV);
                radii.Add(Mathf.Sqrt(u * u + v * v));
            }
            radii.Sort();

            float absoluteMin = radii[0];
            maxRadius = radii[Mathf.Clamp(
                Mathf.RoundToInt(radii.Count * 0.96f), 0, radii.Count - 1)];

            // If the mesh has geometry near the impact origin, rings must start from 0
            if (absoluteMin < maxRadius * 0.20f)
            {
                minRadius = 0f;
            }
            else
            {
                minRadius = radii[Mathf.Clamp(
                    Mathf.RoundToInt(radii.Count * 0.02f), 0, radii.Count - 1)];
            }

            if (maxRadius <= minRadius + 1e-5f)
                maxRadius = minRadius + Mathf.Max(minRadius, 1e-3f);
        }

        // ------------------------------------------------------------------ brick / grid

        static void FractureBrick(List<WorkMesh> fragments, DestructionProfileData profile,
            System.Random rng, Func<float, bool> progress, FractureResult result,
            int targetCount = -1)
        {
            Bounds bounds = fragments[0].ComputeBounds();
            Vector3 size = bounds.size;
            Vector3Int grid = GetBrickGrid(bounds, profile, targetCount);
            int nx = grid.x;
            int ny = grid.y;
            int nz = grid.z;

            var planes = new List<Plane>();
            AddGridPlanes(planes, Vector3.right, bounds.min.x, size.x, nx, rng, profile.BrickJitter);
            AddGridPlanes(planes, Vector3.up, bounds.min.y, size.y, ny, rng, profile.BrickJitter);
            AddGridPlanes(planes, Vector3.forward, bounds.min.z, size.z, nz, rng, profile.BrickJitter);

            ApplyGlobalPlanes(fragments, planes, profile.InteriorUVScale, profile.CutSurfaceUVMode, progress, result,
                repairCutTopology: !profile.Solidify,
                allowFallback: !profile.Solidify,
                bakedInteriorJaggedness: GetBakedCutDetail(profile),
                bakedCutDetailStyle: GetBakedCutDetailStyle(profile));
        }

        internal static Vector3Int GetBrickGrid(Bounds bounds, DestructionProfile profile)
        {
            return GetBrickGrid(bounds, new DestructionProfileData(profile),
                profile != null ? profile.FragmentCount : -1);
        }

        internal static Vector3Int GetBrickGrid(Bounds bounds, DestructionProfileData profile)
        {
            return GetBrickGrid(bounds, profile,
                profile != null ? profile.FragmentCount : -1);
        }

        static Vector3Int GetBrickGrid(Bounds bounds,
            DestructionProfileData profile, int targetCount)
        {
            Vector3 size = bounds.size;
            int nx;
            int ny;
            int nz;
            if (profile.BrickUseCellSize &&
                profile.BrickCellSize.x > 1e-5f &&
                profile.BrickCellSize.y > 1e-5f &&
                profile.BrickCellSize.z > 1e-5f)
            {
                nx = Mathf.Max(1, Mathf.RoundToInt(size.x / profile.BrickCellSize.x));
                ny = Mathf.Max(1, Mathf.RoundToInt(size.y / profile.BrickCellSize.y));
                nz = Mathf.Max(1, Mathf.RoundToInt(size.z / profile.BrickCellSize.z));
            }
            else
            {
                // Distribute the requested count proportionally to bounds dimensions.
                float volume = Mathf.Max(size.x * size.y * size.z, 1e-6f);
                int requested = targetCount > 0
                    ? targetCount
                    : profile.FragmentCount;
                float k = Mathf.Pow(Mathf.Max(1, requested) / volume, 1f / 3f);
                nx = Mathf.Max(1, Mathf.RoundToInt(size.x * k));
                ny = Mathf.Max(1, Mathf.RoundToInt(size.y * k));
                nz = Mathf.Max(1, Mathf.RoundToInt(size.z * k));
            }

            // Max Parts remains the physical-piece ceiling even when Brick Cell Size is
            // enabled. This matters especially for Whole Building: each disconnected wall
            // receives only its allocated share of the assembly target. Reusing the full
            // profile count for every wall multiplied the preview/bake density by the module
            // count and made Brick look unrelated to the requested result.
            int maxCells = Mathf.Clamp(targetCount > 0
                ? targetCount
                : profile.FragmentCount, 1, 250);
            long product = (long)nx * ny * nz;
            if (product > maxCells)
            {
                float scale = Mathf.Pow(maxCells / (float)product, 1f / 3f);
                nx = Mathf.Max(1, Mathf.FloorToInt(nx * scale));
                ny = Mathf.Max(1, Mathf.FloorToInt(ny * scale));
                nz = Mathf.Max(1, Mathf.FloorToInt(nz * scale));
                while ((long)nx * ny * nz > maxCells)
                {
                    if (nx >= ny && nx >= nz && nx > 1) nx--;
                    else if (ny >= nz && ny > 1) ny--;
                    else if (nz > 1) nz--;
                    else break;
                }
            }
            return new Vector3Int(nx, ny, nz);
        }

        static void AddGridPlanes(List<Plane> planes, Vector3 axis, float min, float size,
            int cells, System.Random rng, float jitter)
        {
            for (int i = 1; i < cells; i++)
            {
                float amount = Mathf.Clamp01(jitter) * 0.25f;
                float t = (float)i / cells +
                          ((float)rng.NextDouble() - 0.5f) * amount / cells;
                planes.Add(new Plane(axis, axis * (min + size * t)));
            }
        }

        // ------------------------------------------------------------------ layered slabs

        static void FractureSlabs(List<WorkMesh> fragments, DestructionProfileData profile,
            System.Random rng, Func<float, bool> progress, FractureResult result,
            int targetCount = -1)
        {
            Bounds bounds = fragments[0].ComputeBounds();
            Vector3 axis = ResolveAxis(profile.SlabNormalAxis, bounds.size, false);
            float min = Vector3.Dot(axis, bounds.min);
            float size = Vector3.Dot(axis, bounds.size);

            // Sheets stacked along the thinnest axis, like slate or plaster layers.
            var planes = new List<Plane>();
            int requestedTarget = targetCount > 0
                ? targetCount
                : profile.FragmentCount;
            int layers = Mathf.Clamp(profile.SlabLayers, 2,
                Mathf.Max(2, requestedTarget));
            AddGridPlanes(planes, axis, min, size, layers, rng, profile.SlabJitter);
            ApplyGlobalPlanes(fragments, planes, profile.InteriorUVScale, profile.CutSurfaceUVMode, progress, result,
                repairCutTopology: !profile.Solidify,
                allowFallback: !profile.Solidify,
                bakedInteriorJaggedness: GetBakedCutDetail(profile),
                bakedCutDetailStyle: GetBakedCutDetailStyle(profile));
            if (result.Cancelled) return;
            // A sheet cut across a hollow or L-shaped body arrives as several islands.
            // Counting them now lets the plate pass below aim at the requested total.
            SeparateIslandsInList(fragments);

            // Cross cuts break each sheet into plates. Their normals lie inside the sheet
            // plane so every cut stays perpendicular to the layer surface.
            Vector3 basisU = Vector3.Cross(axis,
                Mathf.Abs(axis.y) < 0.95f ? Vector3.up : Vector3.right).normalized;
            Vector3 basisV = Vector3.Cross(axis, basisU);
            // Explicit Plate Length Axis is a global instruction and stays global.
            bool autoPlateAxis = profile.PreferredCutAxis == AxisMode.Auto;
            Vector3 globalPlateLength = autoPlateAxis
                ? (fragments.Count > 0
                    ? SecondaryPrincipalAxis(fragments[0], axis)
                    : basisU)
                : Vector3.ProjectOnPlane(
                    ResolveAxis(profile.PreferredCutAxis, bounds.size), axis).normalized;
            if (globalPlateLength.sqrMagnitude < 1e-8f) globalPlateLength = basisU;
            // On Auto, every sheet is measured on its own. One global plate direction
            // suits only the walls that happen to run along it: on a square tower the
            // two perpendicular walls received cross cuts along their short side and
            // came out as blocks instead of plates. The axis is cached per fragment
            // because a single cut may ask the picker for several candidate planes.
            var plateAxisCache = autoPlateAxis
                ? new Dictionary<WorkMesh, Vector3>()
                : null;
            FractureIterative(fragments, Mathf.Max(requestedTarget, fragments.Count),
                rng, profile.InteriorUVScale, profile.CutSurfaceUVMode, f =>
                {
                    Bounds b = f.ComputeBounds();
                    Vector3 plateLength = globalPlateLength;
                    if (plateAxisCache != null &&
                        !plateAxisCache.TryGetValue(f, out plateLength))
                    {
                        plateLength = SecondaryPrincipalAxis(f, axis);
                        if (plateLength.sqrMagnitude < 1e-8f)
                            plateLength = globalPlateLength;
                        plateAxisCache[f] = plateLength;
                    }
                    Vector3 plateWidth = Vector3.Cross(axis, plateLength).normalized;

                    float angle = (float)(rng.NextDouble() * Math.PI * 2.0);
                    Vector3 normal = basisU * Mathf.Cos(angle) + basisV * Mathf.Sin(angle);
                    Vector3 preferred = rng.NextDouble() < profile.LengthwiseSplitBias
                        ? plateWidth
                        : plateLength;
                    if (Vector3.Dot(normal, preferred) < 0f) preferred = -preferred;
                    normal = Vector3.Slerp(normal, preferred,
                        Mathf.Clamp01(profile.DirectionalCutStrength)).normalized;
                    Vector3 point = b.center + new Vector3(
                        ((float)rng.NextDouble() - 0.5f) * b.size.x * 0.4f,
                        ((float)rng.NextDouble() - 0.5f) * b.size.y * 0.4f,
                        ((float)rng.NextDouble() - 0.5f) * b.size.z * 0.4f);
                    return new Plane(normal, point);
            }, progress, result,
                repairCutTopology: !profile.Solidify,
                allowFallback: !profile.Solidify,
                bakedInteriorJaggedness: GetBakedCutDetail(profile),
                bakedCutDetailStyle: GetBakedCutDetailStyle(profile),
                // Without this the interactive preview spends up to 24 trial planes on
                // every large sheet, while Concrete settles for 4-6. Bake quality is
                // untouched: outside the preview the flag is false either way.
                isPreview: profile.IsPreview);
        }

        // ------------------------------------------------------------------ columnar / pillars

        static void FractureColumnar(List<WorkMesh> fragments, DestructionProfileData profile,
            System.Random rng, Func<float, bool> progress, FractureResult result,
            int targetCount = -1)
        {
            Bounds bounds = fragments[0].ComputeBounds();
            Vector3 axis = profile.PillarAxis == AxisMode.Auto
                ? LongestPrincipalAxis(fragments[0])
                : ResolveAxis(profile.PillarAxis, bounds.size);
            Vector3 basisU = Vector3.Cross(axis,
                Mathf.Abs(axis.y) < 0.95f ? Vector3.up : Vector3.right).normalized;
            Vector3 basisV = Vector3.Cross(axis, basisU);
            GetColumnGrid(bounds, profile, basisU, basisV,
                targetCount > 0 ? targetCount : profile.FragmentCount,
                out int nu, out int nv);

            // Cuts run only parallel to the long axis, leaving full-length prisms.
            var planes = new List<Plane>();
            AddGridPlanes(planes, basisU, Vector3.Dot(basisU, bounds.min),
                Vector3.Dot(basisU, bounds.size), nu, rng, profile.ColumnJitter);
            AddGridPlanes(planes, basisV, Vector3.Dot(basisV, bounds.min),
                Vector3.Dot(basisV, bounds.size), nv, rng, profile.ColumnJitter);
            ApplyGlobalPlanes(fragments, planes, profile.InteriorUVScale, profile.CutSurfaceUVMode, progress, result,
                repairCutTopology: !profile.Solidify,
                allowFallback: !profile.Solidify,
                bakedInteriorJaggedness: GetBakedCutDetail(profile),
                bakedCutDetailStyle: GetBakedCutDetailStyle(profile));
        }

        internal static void GetColumnGrid(Bounds bounds, DestructionProfile profile,
            Vector3 basisU, Vector3 basisV, out int nu, out int nv)
        {
            GetColumnGrid(bounds, new DestructionProfileData(profile), basisU, basisV, out nu, out nv);
        }

        internal static void GetColumnGrid(Bounds bounds, DestructionProfileData profile,
            Vector3 basisU, Vector3 basisV, out int nu, out int nv)
        {
            GetColumnGrid(bounds, profile, basisU, basisV,
                profile.FragmentCount, out nu, out nv);
        }

        static void GetColumnGrid(Bounds bounds, DestructionProfileData profile,
            Vector3 basisU, Vector3 basisV, int targetCount,
            out int nu, out int nv)
        {
            float sizeU = Mathf.Max(Mathf.Abs(Vector3.Dot(basisU, bounds.size)), 1e-4f);
            float sizeV = Mathf.Max(Mathf.Abs(Vector3.Dot(basisV, bounds.size)), 1e-4f);
            // Distribute the requested count over the cross-section proportionally to its
            // aspect ratio, so columns stay roughly square.
            float k = Mathf.Sqrt(Mathf.Max(1, targetCount) / (sizeU * sizeV));
            nu = Mathf.Clamp(Mathf.RoundToInt(sizeU * k), 1, 32);
            nv = Mathf.Clamp(Mathf.RoundToInt(sizeV * k), 1, 32);
            while (nu * nv > 250)
            {
                if (nu >= nv && nu > 1) nu--;
                else if (nv > 1) nv--;
                else break;
            }
        }

        // ------------------------------------------------------------------ impact shatter

        static void FractureImpact(List<WorkMesh> fragments, DestructionProfileData profile,
            System.Random rng, Func<float, bool> progress, FractureResult result,
            int requestedTargetCount = -1)
        {
            Bounds bounds = fragments[0].ComputeBounds();
            bool planarImpact = TryResolveThinPlateNormal(
                fragments[0], out Vector3 plateNormal);
            Vector3 impact = profile.ImpactPointOverride
                ? ImpactPointFromNormalized(bounds, profile.ImpactPointLocal)
                : planarImpact
                    ? GetPlanarImpactPoint(fragments[0], plateNormal, rng)
                    : GetImpactPoint(bounds, rng);
            float maxDimension = Mathf.Max(bounds.size.x,
                Mathf.Max(bounds.size.y, bounds.size.z));
            float focus = Mathf.Clamp01(profile.ImpactFocus);

            var volumes = new List<float> { fragments[0].ComputeVolume() };
            var closed = new List<bool> { fragments[0].IsClosedManifold() };
            int targetCount = requestedTargetCount > 0
                ? requestedTargetCount
                : profile.FragmentCount;
            float sourceVolume = result != null && result.SourceVolume > 0f
                ? result.SourceVolume
                : volumes[0];
            int safety = targetCount * 6;
            while (fragments.Count < targetCount && safety-- > 0)
            {
                if (progress != null && !progress((float)fragments.Count / targetCount))
                {
                    result.Cancelled = true;
                    return;
                }

                // Deterministic choice: volume weighted by proximity to the impact point,
                // so the region near the hit is subdivided far more often. Focus scales
                // how strongly distance matters, never whether a piece is eligible: at
                // high focus a far element used to sit at a score so low that the loop
                // spent its whole budget near the hit and left it as one untouched slab.
                int best = -1;
                float bestScore = 0f;
                float untouchedVolume = 3f * sourceVolume / Mathf.Max(1, targetCount);
                for (int i = 0; i < fragments.Count; i++)
                {
                    if (volumes[i] <= 0f) continue;
                    float distance = Vector3.Distance(
                        fragments[i].ComputeBounds().center, impact);
                    // Volume spans a far wider range than a linear proximity term, so a
                    // gentle falloff let the biggest piece win wherever it sat and the
                    // shatter stayed centred on the mass instead of on the hit. Focus now
                    // sharpens the falloff itself, up to a cubic one.
                    float proximity = Mathf.Pow(
                        1f / (0.15f + distance / maxDimension), 1f + 2f * focus);
                    float attenuation = Mathf.Lerp(1f, proximity, focus);
                    // The catch-up floor for oversized pieces only grows as the budget is
                    // spent. A prop built from many elements — a fence, a railing — starts
                    // out with everything above the per-fragment budget, so a constant
                    // floor made every plank equally eligible and flattened the shatter
                    // into a uniform one. Early cuts therefore follow the hit alone, and
                    // only the last part of the budget goes back for anything left whole.
                    if (volumes[i] > untouchedVolume)
                    {
                        float spent = (float)fragments.Count / Mathf.Max(1, targetCount);
                        attenuation = Mathf.Max(attenuation,
                            Mathf.Lerp(0f, 0.6f, Mathf.Clamp01(spent * 1.4f - 0.4f)));
                    }
                    float score = volumes[i] * attenuation;
                    if (best < 0 || score > bestScore)
                    {
                        best = i;
                        bestScore = score;
                    }
                }
                if (best < 0) break;

                var target = fragments[best];
                bool requireClosedChildren = closed[best];
                bool split = false;
                for (int attempt = 0; attempt < 8 && !split; attempt++)
                {
                    Bounds b = target.ComputeBounds();
                    // Impact is radial by design. Its point and focus determine the
                    // fracture character; a stale directional setting from another
                    // preset must never steer these planes.
                    Vector3 normal = RandomDirection(rng);
                    if (planarImpact)
                    {
                        normal = Vector3.ProjectOnPlane(normal, plateNormal);
                        normal = normal.sqrMagnitude > 1e-8f
                            ? normal.normalized
                            : StablePerpendicular(plateNormal);
                    }
                    Vector3 toImpact = impact - b.center;
                    // Close to the hit the cuts run through the hit itself, which is what
                    // shatters that area. Farther out they turn to face it, so the layout
                    // reads as radiating from the placed point instead of from wherever
                    // the piece happens to sit — the reason a moved hit barely showed.
                    if (toImpact.sqrMagnitude > 1e-8f)
                    {
                        Vector3 radial;
                        if (planarImpact)
                        {
                            Vector3 inPlane = Vector3.ProjectOnPlane(
                                toImpact, plateNormal);
                            radial = inPlane.sqrMagnitude > 1e-8f
                                ? Vector3.Cross(plateNormal, inPlane.normalized)
                                : normal;
                        }
                        else
                        {
                            radial = Vector3.Cross(toImpact.normalized,
                                RandomDirection(rng));
                        }
                        if (radial.sqrMagnitude > 1e-8f)
                            normal = Vector3.Slerp(normal, radial.normalized,
                                focus * 0.7f).normalized;
                    }
                    if (planarImpact)
                    {
                        normal = Vector3.ProjectOnPlane(normal, plateNormal);
                        normal = normal.sqrMagnitude > 1e-8f
                            ? normal.normalized
                            : StablePerpendicular(plateNormal);
                    }
                    // Where the plane sits decided more than anything else, and it used to
                    // be anchored at the piece's own centre and only nudged toward the
                    // hit. The first cuts act on the whole object, whose centre is the
                    // middle, so every early plane ran through the middle and seeded the
                    // dense area there no matter where the hit was placed.
                    Vector3 point;
                    if (b.Contains(impact))
                    {
                        // The hit is inside this piece: cut through it. Focus decides how
                        // tightly the planes hug the hit; at 0 they scatter across the
                        // whole piece, which is the uniform look.
                        float spread = (1f - focus) * 0.5f;
                        point = impact + new Vector3(
                            ((float)rng.NextDouble() - 0.5f) * b.size.x * spread,
                            ((float)rng.NextDouble() - 0.5f) * b.size.y * spread,
                            ((float)rng.NextDouble() - 0.5f) * b.size.z * spread);
                    }
                    else
                    {
                        // The hit is elsewhere: bias the cut to the side facing it, so
                        // debris grows finer toward the hit across the whole object.
                        Vector3 nearest = b.ClosestPoint(impact);
                        point = Vector3.Lerp(b.center, nearest,
                            0.3f + 0.5f * focus * (float)rng.NextDouble());
                    }
                    point = b.ClosestPoint(point);
                    if (!TrySliceWithFallback(target, new Plane(normal, point),
                            profile.InteriorUVScale, profile.CutSurfaceUVMode,
                            repairCutTopology: !profile.Solidify,
                            allowFallback: !profile.Solidify, out var a, out var partB,
                            bakedInteriorJaggedness: GetBakedCutDetail(profile),
                            bakedCutDetailStyle: GetBakedCutDetailStyle(profile),
                            requireClosedChildren: requireClosedChildren))
                        continue;

                    // Islands are separated as they appear, exactly as the other patterns
                    // do, so a cut that frees a detached lump counts it as its own piece
                    // instead of leaving two lumps sharing one Rigidbody.
                    ImpactStore(fragments, volumes, closed, best, a,
                        requireClosedChildren);
                    ImpactStore(fragments, volumes, closed, -1, partB,
                        requireClosedChildren);
                    split = true;
                }

                if (!split)
                {
                    volumes[best] = -1f;
                    bool anyPositive = false;
                    for (int i = 0; i < volumes.Count; i++)
                        if (volumes[i] > 0f) { anyPositive = true; break; }
                    if (!anyPositive) break;
                }
            }
        }

        static void ImpactStore(List<WorkMesh> fragments, List<float> volumes,
            List<bool> closed, int replaceIndex, WorkMesh piece, bool isClosed)
        {
            List<WorkMesh> islands = piece.SplitConnectedComponents();
            if (islands != null && islands.Count > 1)
            {
                bool first = true;
                for (int i = 0; i < islands.Count; i++)
                {
                    WorkMesh island = islands[i];
                    if (island == null || island.TriangleCount < 4) continue;
                    ImpactAssign(fragments, volumes, closed,
                        first ? replaceIndex : -1, island, island.ComputeVolume(),
                        isClosed);
                    first = false;
                }
                if (!first) return;
            }

            ImpactAssign(fragments, volumes, closed, replaceIndex, piece,
                piece.ComputeVolume(), isClosed);
        }

        static void ImpactAssign(List<WorkMesh> fragments, List<float> volumes,
            List<bool> closed, int index, WorkMesh piece, float volume, bool isClosed)
        {
            if (index >= 0)
            {
                fragments[index] = piece;
                volumes[index] = volume;
                closed[index] = isClosed;
                return;
            }
            fragments.Add(piece);
            volumes.Add(volume);
            closed.Add(isClosed);
        }

        /// <summary>
        /// Author-placed impact origin: each component is a fraction of the source bounds
        /// on that axis, so the hit stays where the user put it regardless of the mesh's
        /// scale or pivot, and the same profile works on differently sized props.
        /// </summary>
        internal static Vector3 ImpactPointFromNormalized(Bounds bounds, Vector3 normalized)
        {
            return new Vector3(
                Mathf.Lerp(bounds.min.x, bounds.max.x, Mathf.Clamp01(normalized.x)),
                Mathf.Lerp(bounds.min.y, bounds.max.y, Mathf.Clamp01(normalized.y)),
                Mathf.Lerp(bounds.min.z, bounds.max.z, Mathf.Clamp01(normalized.z)));
        }

        /// <summary>Deterministic impact origin: near the center of the largest bounds
        /// face, offset slightly so the shatter never looks perfectly symmetric.</summary>
        internal static Vector3 GetImpactPoint(Bounds bounds, System.Random rng)
        {
            Vector3 axis = SmallestAxis(bounds.size);
            Vector3 basisU = Vector3.Cross(axis,
                Mathf.Abs(axis.y) < 0.95f ? Vector3.up : Vector3.right).normalized;
            Vector3 basisV = Vector3.Cross(axis, basisU);
            Vector3 face = bounds.center +
                           axis * (Vector3.Dot(axis, bounds.extents));
            return face +
                   basisU * (((float)rng.NextDouble() - 0.5f) * 0.4f *
                             Mathf.Abs(Vector3.Dot(basisU, bounds.size))) +
                   basisV * (((float)rng.NextDouble() - 0.5f) * 0.4f *
                             Mathf.Abs(Vector3.Dot(basisV, bounds.size)));
        }

        static Vector3 GetPlanarImpactPoint(WorkMesh mesh, Vector3 normal,
            System.Random rng)
        {
            Bounds bounds = mesh.ComputeBounds();
            Vector3 center = bounds.center;
            float faceOffset = float.NegativeInfinity;
            for (int i = 0; i < mesh.Positions.Count; i++)
            {
                faceOffset = Mathf.Max(faceOffset,
                    Vector3.Dot(mesh.Positions[i] - center, normal));
            }
            if (float.IsNaN(faceOffset) || float.IsInfinity(faceOffset))
                faceOffset = 0f;

            Vector3 u = StablePerpendicular(normal);
            Vector3 v = Vector3.Cross(normal, u).normalized;
            float extentU = ProjectionSpan(mesh, u) * 0.2f;
            float extentV = ProjectionSpan(mesh, v) * 0.2f;
            return center + normal * faceOffset +
                   u * (((float)rng.NextDouble() - 0.5f) * extentU) +
                   v * (((float)rng.NextDouble() - 0.5f) * extentV);
        }

        // ------------------------------------------------------------------ global plane application

        static void ApplyGlobalPlanes(List<WorkMesh> fragments, List<Plane> planes, float uvScale,
            CutSurfaceUVMode uvMode,
            Func<float, bool> progress, FractureResult result,
            bool repairCutTopology, bool allowFallback = true,
            float bakedInteriorJaggedness = 0f,
            BakedCutDetailStyle bakedCutDetailStyle = BakedCutDetailStyle.None,
            int fragmentCeiling = 250,
            bool reportCeiling = true)
        {
            int productionFragmentCeiling = Mathf.Max(1, fragmentCeiling);
            bool reachedCeiling = false;
            // Cache each fragment's bounds across all planes; only freshly split
            // halves need a recomputation. ComputeBounds used to run once per
            // plane x fragment pair.
            var fragmentBounds = new List<Bounds>(fragments.Count);
            var fragmentClosed = new List<bool>(fragments.Count);
            for (int i = 0; i < fragments.Count; i++)
            {
                fragmentBounds.Add(fragments[i].ComputeBounds());
                fragmentClosed.Add(fragments[i].IsClosedManifold());
            }
            for (int p = 0; p < planes.Count; p++)
            {
                if (fragments.Count >= productionFragmentCeiling)
                {
                    reachedCeiling = true;
                    break;
                }
                if (progress != null && !progress((float)p / planes.Count))
                {
                    result.Cancelled = true;
                    return;
                }

                var next = new List<WorkMesh>(fragments.Count * 2);
                var nextBounds = new List<Bounds>(fragmentBounds.Count * 2);
                var nextClosed = new List<bool>(fragmentClosed.Count * 2);
                int inputCount = fragments.Count;
                for (int fragmentIndex = 0;
                     fragmentIndex < inputCount; fragmentIndex++)
                {
                    // A glass ring or brick grid plane intersects only a fraction of the
                    // cells after the first cuts. Calling the full slicer for every known
                    // fragment made global patterns approach planes x fragments work even
                    // when most planes were metres away from a fragment. The AABB test is
                    // conservative and cannot discard a real intersection.
                    WorkMesh frag = fragments[fragmentIndex];
                    Bounds bounds = fragmentBounds[fragmentIndex];
                    if (!PlaneIntersectsBounds(planes[p], bounds))
                    {
                        next.Add(frag);
                        nextBounds.Add(bounds);
                        nextClosed.Add(fragmentClosed[fragmentIndex]);
                        continue;
                    }

                    // One global plane can still touch many cells. Report progress inside
                    // that plane so Cancel remains responsive instead of waiting for the
                    // entire row/ring to finish.
                    if ((fragmentIndex & 15) == 0 && progress != null &&
                        !progress((p + (float)fragmentIndex /
                                  Mathf.Max(1, inputCount)) /
                                  Mathf.Max(1, planes.Count)))
                    {
                        result.Cancelled = true;
                        return;
                    }

                    int unprocessed = inputCount - fragmentIndex - 1;
                    bool hasRoomForSplit = next.Count + unprocessed + 2 <=
                                           productionFragmentCeiling;
                    bool requireClosedChildren = hasRoomForSplit &&
                                                 fragmentClosed[fragmentIndex];
                    if (hasRoomForSplit &&
                        TrySliceWithFallback(frag, planes[p], uvScale, uvMode,
                            repairCutTopology, allowFallback, out var a, out var b,
                            bakedInteriorJaggedness, bakedCutDetailStyle,
                            requireClosedChildren))
                    {
                        next.Add(a);
                        nextBounds.Add(a.ComputeBounds());
                        nextClosed.Add(requireClosedChildren);
                        next.Add(b);
                        nextBounds.Add(b.ComputeBounds());
                        nextClosed.Add(requireClosedChildren);
                    }
                    else
                    {
                        next.Add(frag); // plane missed this fragment
                        nextBounds.Add(bounds);
                        nextClosed.Add(fragmentClosed[fragmentIndex]);
                    }
                }
                fragments.Clear();
                fragments.AddRange(next);
                fragmentBounds = nextBounds;
                fragmentClosed = nextClosed;
            }

            if (reachedCeiling && reportCeiling)
            {
                result.Warnings.Add(
                    $"Global fracture reached the {productionFragmentCeiling}-piece " +
                    "production ceiling. Remaining planes were skipped to keep Bake and " +
                    "Edit Mode preview bounded.");
            }
        }

        static bool PlaneIntersectsBounds(Plane plane, Bounds bounds)
        {
            Vector3 normal = plane.normal;
            Vector3 extents = bounds.extents;
            float radius = Mathf.Abs(normal.x) * extents.x +
                           Mathf.Abs(normal.y) * extents.y +
                           Mathf.Abs(normal.z) * extents.z;
            float distance = plane.GetDistanceToPoint(bounds.center);
            float tolerance = Mathf.Max(bounds.size.magnitude * 1e-6f, 1e-7f);
            return Mathf.Abs(distance) <= radius + tolerance;
        }

        // ------------------------------------------------------------------ helpers

        internal static Vector3 ResolveAxis(AxisMode mode, Vector3 size,
            bool autoUsesLargest = true)
        {
            switch (mode)
            {
                case AxisMode.X: return Vector3.right;
                case AxisMode.Y: return Vector3.up;
                case AxisMode.Z: return Vector3.forward;
                default: return autoUsesLargest ? LargestAxis(size) : SmallestAxis(size);
            }
        }

        internal static Vector3 ResolvePaneNormal(WorkMesh mesh, AxisMode mode)
        {
            if (mesh == null) return Vector3.forward;
            if (mode != AxisMode.Auto)
                return ResolveAxis(mode, mesh.ComputeBounds().size, false);
            return ShortestPrincipalAxis(mesh);
        }

        static bool TryResolveThinPlateNormal(WorkMesh mesh, out Vector3 normal)
        {
            normal = Vector3.forward;
            if (mesh == null || mesh.Positions.Count < 4) return false;

            Vector3 length = LongestPrincipalAxis(mesh);
            Vector3 width = SecondaryPrincipalAxis(mesh, length);
            normal = Vector3.Cross(length, width).normalized;
            if (normal.sqrMagnitude < 0.5f) return false;

            float thickness = ProjectionSpan(mesh, normal);
            float inPlaneMinimum = Mathf.Min(
                ProjectionSpan(mesh, length), ProjectionSpan(mesh, width));
            return inPlaneMinimum > 1e-6f &&
                   thickness <= inPlaneMinimum * 0.18f;
        }

        static float ProjectionSpan(WorkMesh mesh, Vector3 axis)
        {
            if (mesh == null || mesh.Positions.Count == 0) return 0f;
            axis = axis.sqrMagnitude > 1e-8f ? axis.normalized : Vector3.right;
            float minimum = float.PositiveInfinity;
            float maximum = float.NegativeInfinity;
            for (int i = 0; i < mesh.Positions.Count; i++)
            {
                float projection = Vector3.Dot(mesh.Positions[i], axis);
                minimum = Mathf.Min(minimum, projection);
                maximum = Mathf.Max(maximum, projection);
            }
            return maximum - minimum;
        }

        /// <summary>
        /// True when every component is an independently closed, watertight solid. That is
        /// the signature of separate authored meshes combined into one object; a single
        /// mesh split at a UV/hard-normal seam would leave open pieces and fail this test,
        /// so it is never wrongly split into per-component fracture.
        /// </summary>
        /// <summary>
        /// True when every component encloses positive volume, i.e. its faces point
        /// outward. An authored inner shell is modelled with reversed normals and reports
        /// negative volume, which is what separates a genuine hollow object from several
        /// independent solids whose bounds merely nest.
        /// </summary>
        static bool AllOutwardFacing(List<WorkMesh> components)
        {
            if (components == null || components.Count == 0) return false;
            for (int i = 0; i < components.Count; i++)
                if (components[i] == null || components[i].ComputeVolume() <= 0f)
                    return false;
            return true;
        }

        static bool AllClosedManifolds(List<WorkMesh> components)
        {
            if (components == null || components.Count == 0) return false;
            for (int i = 0; i < components.Count; i++)
                if (components[i] == null || !components[i].IsClosedManifold())
                    return false;
            return true;
        }

        static bool UsesAutomaticAxis(DestructionProfileData profile)
        {
            switch (profile.Pattern)
            {
                case FracturePattern.UniformConcrete:
                    return profile.DirectionalCutStrength > 0.0001f &&
                           profile.PreferredCutAxis == AxisMode.Auto;
                case FracturePattern.DirectionalWood:
                    return profile.PreferredCutAxis == AxisMode.Auto;
                case FracturePattern.RadialGlass:
                    return profile.GlassNormalAxis == AxisMode.Auto;
                case FracturePattern.LayeredSlabs:
                    return profile.SlabNormalAxis == AxisMode.Auto;
                case FracturePattern.ColumnarPillars:
                    return profile.PillarAxis == AxisMode.Auto;
                default:
                    return false;
            }
        }

        /// <summary>
        /// True when at least one component is modelled inside-out, i.e. reports negative
        /// signed volume. That is how an authored inner shell is built and it is something
        /// an independent solid never does, so it is the evidence that tells a genuinely
        /// hollow object from an assembly whose bounds merely nest.
        /// </summary>
        static bool AnyInwardFacingComponent(List<WorkMesh> components)
        {
            if (components == null) return false;
            for (int i = 0; i < components.Count; i++)
                if (components[i] != null && components[i].ComputeVolume() < 0f)
                    return true;
            return false;
        }

        static bool HasNestedComponentBounds(List<WorkMesh> components)
        {
            if (components == null || components.Count < 2) return false;
            var bounds = new Bounds[components.Count];
            for (int i = 0; i < components.Count; i++)
                bounds[i] = components[i].ComputeBounds();

            for (int outerIndex = 0; outerIndex < bounds.Length; outerIndex++)
            {
                Bounds outer = bounds[outerIndex];
                float tolerance = Mathf.Max(outer.size.magnitude * 1e-4f, 1e-5f);
                float outerBoxVolume = Mathf.Max(
                    outer.size.x * outer.size.y * outer.size.z, 1e-12f);
                for (int innerIndex = 0; innerIndex < bounds.Length; innerIndex++)
                {
                    if (outerIndex == innerIndex) continue;
                    Bounds inner = bounds[innerIndex];
                    float innerBoxVolume = Mathf.Max(
                        inner.size.x * inner.size.y * inner.size.z, 0f);
                    if (innerBoxVolume >= outerBoxVolume * 0.995f) continue;
                    if (inner.min.x < outer.min.x - tolerance ||
                        inner.min.y < outer.min.y - tolerance ||
                        inner.min.z < outer.min.z - tolerance ||
                        inner.max.x > outer.max.x + tolerance ||
                        inner.max.y > outer.max.y + tolerance ||
                        inner.max.z > outer.max.z + tolerance)
                        continue;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Drops connected shells that enclose no volume — the flat cap sheets a contact
        /// seam leaves behind. The floor is relative to the largest shell so it holds at
        /// any model scale, and sits far below any real prop (one hundred-thousandth of
        /// the main body) while staying clear of the float noise a signed-volume sum
        /// accumulates over thousands of triangles. Never empties the list: the largest
        /// shell is always above its own floor. Returns how many were removed.
        /// </summary>
        static int DiscardDegenerateShells(List<WorkMesh> components)
        {
            if (components == null || components.Count < 2) return 0;
            var volumes = new float[components.Count];
            float largest = 0f;
            for (int i = 0; i < components.Count; i++)
            {
                volumes[i] = Mathf.Abs(components[i].ComputeVolume());
                largest = Mathf.Max(largest, volumes[i]);
            }
            if (largest <= 0f) return 0;

            float floor = largest * 1e-5f;
            int removed = 0;
            for (int i = components.Count - 1; i >= 0; i--)
            {
                if (volumes[i] > floor) continue;
                components.RemoveAt(i);
                removed++;
            }
            return removed;
        }

        /// <summary>
        /// Total triangle area of a WorkMesh. Unlike signed volume this is defined for an
        /// open shell, which is what every Plane-style building module is.
        /// </summary>
        static float ComputeSurfaceArea(WorkMesh mesh)
        {
            if (mesh == null) return 0f;
            double area = 0d;
            for (int submesh = 0; submesh < mesh.SubmeshCount; submesh++)
            {
                List<int> triangles = mesh.Submeshes[submesh];
                for (int i = 0; i + 2 < triangles.Count; i += 3)
                {
                    int ia = triangles[i], ib = triangles[i + 1], ic = triangles[i + 2];
                    if (ia < 0 || ib < 0 || ic < 0 ||
                        ia >= mesh.Positions.Count || ib >= mesh.Positions.Count ||
                        ic >= mesh.Positions.Count)
                        continue;
                    Vector3 a = mesh.Positions[ia];
                    area += Vector3.Cross(mesh.Positions[ib] - a,
                        mesh.Positions[ic] - a).magnitude * 0.5d;
                }
            }
            return (float)area;
        }

        static int[] AllocateComponentTargets(List<WorkMesh> components,
            int requestedCount)
        {
            int count = components.Count;
            var targets = new int[count];
            if (count == 0) return targets;

            int totalTarget = Mathf.Max(count, requestedCount);
            int remaining = totalTarget - count;
            float totalVolume = 0f;
            var weights = new float[count];
            var fractions = new float[count];
            for (int i = 0; i < count; i++)
            {
                targets[i] = 1;
                weights[i] = Mathf.Max(components[i].ComputeVolume(), 0f);
                totalVolume += weights[i];
            }

            if (totalVolume <= 1e-8f)
            {
                for (int i = 0; i < remaining; i++)
                    targets[i % count]++;
                return targets;
            }

            // Signed volume is only meaningful for a closed shell. A Whole Building is
            // assembled from open Plane panels — the analyzer reports NO CLOSED VOLUME —
            // and those integrate to roughly nothing, so a panel took the floor of the
            // allocation and kept its starting budget of exactly one part: the module came
            // out of the bake as a single enormous piece beside properly cut neighbours.
            // Surface area describes how much material there is to cut for an open shell
            // and for a closed one alike, so as soon as any component reads as volumeless
            // the whole allocation switches to it. Mixing the two units across components
            // would be worse than either.
            float smallestWeight = float.MaxValue;
            for (int i = 0; i < count; i++)
                smallestWeight = Mathf.Min(smallestWeight, weights[i]);
            if (smallestWeight <= totalVolume * 0.0005f)
            {
                totalVolume = 0f;
                for (int i = 0; i < count; i++)
                {
                    weights[i] = Mathf.Max(ComputeSurfaceArea(components[i]), 0f);
                    totalVolume += weights[i];
                }
                if (totalVolume <= 1e-8f)
                {
                    for (int i = 0; i < remaining; i++)
                        targets[i % count]++;
                    return targets;
                }
            }

            int assigned = 0;
            for (int i = 0; i < count; i++)
            {
                float exact = remaining * (weights[i] / totalVolume);
                int whole = Mathf.FloorToInt(exact);
                targets[i] += whole;
                assigned += whole;
                fractions[i] = exact - whole;
            }

            for (int slot = assigned; slot < remaining; slot++)
            {
                int best = 0;
                for (int i = 1; i < count; i++)
                {
                    if (fractions[i] > fractions[best]) best = i;
                }
                targets[best]++;
                fractions[best] = -1f;
            }
            return targets;
        }

        static Vector3 LargestAxis(Vector3 size)
        {
            if (size.x >= size.y && size.x >= size.z) return Vector3.right;
            if (size.y >= size.z) return Vector3.up;
            return Vector3.forward;
        }

        /// <summary>
        /// Finds the dominant geometric direction without assuming the element is aligned
        /// to mesh-local X/Y/Z. Power iteration on the position covariance is stable for
        /// long planks, diagonal braces and leaning posts; near-cubic shapes fall back to
        /// their bounds axis where no meaningful grain direction exists.
        /// </summary>
        /// <summary>Extent of an axis-aligned size along an arbitrary unit axis.</summary>
        static float ExtentAlong(Vector3 size, Vector3 axis)
        {
            return Mathf.Abs(axis.x) * size.x +
                   Mathf.Abs(axis.y) * size.y +
                   Mathf.Abs(axis.z) * size.z;
        }

        static Vector3 LongestPrincipalAxis(WorkMesh mesh)
        {
            if (mesh == null || mesh.Positions.Count < 2) return Vector3.up;

            Vector3 mean = Vector3.zero;
            for (int i = 0; i < mesh.Positions.Count; i++)
                mean += mesh.Positions[i];
            mean /= mesh.Positions.Count;

            double xx = 0d, xy = 0d, xz = 0d;
            double yy = 0d, yz = 0d, zz = 0d;
            for (int i = 0; i < mesh.Positions.Count; i++)
            {
                Vector3 d = mesh.Positions[i] - mean;
                xx += d.x * d.x;
                xy += d.x * d.y;
                xz += d.x * d.z;
                yy += d.y * d.y;
                yz += d.y * d.z;
                zz += d.z * d.z;
            }

            Vector3 axis = LargestAxis(mesh.ComputeBounds().size);
            for (int iteration = 0; iteration < 16; iteration++)
            {
                var next = new Vector3(
                    (float)(xx * axis.x + xy * axis.y + xz * axis.z),
                    (float)(xy * axis.x + yy * axis.y + yz * axis.z),
                    (float)(xz * axis.x + yz * axis.y + zz * axis.z));
                if (next.sqrMagnitude < 1e-12f) break;
                axis = next.normalized;
            }

            if (axis.sqrMagnitude < 0.5f)
                axis = LargestAxis(mesh.ComputeBounds().size);
            // Eigenvectors have no sign. Keeping a stable dominant-positive sign makes
            // seeded bakes deterministic across mesh import/order changes.
            float ax = Mathf.Abs(axis.x), ay = Mathf.Abs(axis.y), az = Mathf.Abs(axis.z);
            float dominant = ax >= ay && ax >= az ? axis.x
                : ay >= az ? axis.y : axis.z;
            return dominant < 0f ? -axis : axis;
        }

        /// <summary>Second-largest principal direction after removing the supplied
        /// length axis. For a board this is its local width, regardless of rotation.</summary>
        static Vector3 SecondaryPrincipalAxis(WorkMesh mesh, Vector3 lengthAxis)
        {
            if (mesh == null || mesh.Positions.Count < 2)
                return StablePerpendicular(lengthAxis);

            lengthAxis = lengthAxis.sqrMagnitude > 1e-8f
                ? lengthAxis.normalized
                : Vector3.right;
            Vector3 mean = Vector3.zero;
            for (int i = 0; i < mesh.Positions.Count; i++)
                mean += mesh.Positions[i];
            mean /= mesh.Positions.Count;

            double xx = 0d, xy = 0d, xz = 0d;
            double yy = 0d, yz = 0d, zz = 0d;
            for (int i = 0; i < mesh.Positions.Count; i++)
            {
                Vector3 d = mesh.Positions[i] - mean;
                xx += d.x * d.x;
                xy += d.x * d.y;
                xz += d.x * d.z;
                yy += d.y * d.y;
                yz += d.y * d.z;
                zz += d.z * d.z;
            }

            // Start on the strongest direction in the plane orthogonal to length.
            // A single StablePerpendicular seed can land exactly on the thinnest
            // eigenvector of an axis-aligned board and power iteration will then never
            // leave it, swapping width and thickness. Test all world basis projections
            // and keep the one with the greatest covariance before iterating.
            Vector3 axis = StablePerpendicular(lengthAxis);
            float bestVariance = float.NegativeInfinity;
            Vector3[] seeds = { Vector3.right, Vector3.up, Vector3.forward };
            for (int seedIndex = 0; seedIndex < seeds.Length; seedIndex++)
            {
                Vector3 candidate = seeds[seedIndex] - lengthAxis *
                    Vector3.Dot(seeds[seedIndex], lengthAxis);
                if (candidate.sqrMagnitude < 1e-8f) continue;
                candidate.Normalize();
                Vector3 covarianceTimesCandidate = new Vector3(
                    (float)(xx * candidate.x + xy * candidate.y + xz * candidate.z),
                    (float)(xy * candidate.x + yy * candidate.y + yz * candidate.z),
                    (float)(xz * candidate.x + yz * candidate.y + zz * candidate.z));
                float variance = Vector3.Dot(candidate,
                    covarianceTimesCandidate);
                if (variance <= bestVariance) continue;
                bestVariance = variance;
                axis = candidate;
            }
            for (int iteration = 0; iteration < 16; iteration++)
            {
                var next = new Vector3(
                    (float)(xx * axis.x + xy * axis.y + xz * axis.z),
                    (float)(xy * axis.x + yy * axis.y + yz * axis.z),
                    (float)(xz * axis.x + yz * axis.y + zz * axis.z));
                next -= lengthAxis * Vector3.Dot(next, lengthAxis);
                if (next.sqrMagnitude < 1e-12f) break;
                axis = next.normalized;
            }

            axis -= lengthAxis * Vector3.Dot(axis, lengthAxis);
            if (axis.sqrMagnitude < 1e-8f)
                axis = StablePerpendicular(lengthAxis);
            else
                axis.Normalize();

            float ax = Mathf.Abs(axis.x), ay = Mathf.Abs(axis.y), az = Mathf.Abs(axis.z);
            float dominant = ax >= ay && ax >= az ? axis.x
                : ay >= az ? axis.y : axis.z;
            return dominant < 0f ? -axis : axis;
        }

        static Vector3 ShortestPrincipalAxis(WorkMesh mesh)
        {
            Vector3 length = LongestPrincipalAxis(mesh);
            Vector3 width = SecondaryPrincipalAxis(mesh, length);
            Vector3 axis = Vector3.Cross(length, width);
            if (axis.sqrMagnitude < 1e-8f)
                axis = SmallestAxis(mesh.ComputeBounds().size);
            else
                axis.Normalize();

            float ax = Mathf.Abs(axis.x), ay = Mathf.Abs(axis.y), az = Mathf.Abs(axis.z);
            float dominant = ax >= ay && ax >= az ? axis.x
                : ay >= az ? axis.y : axis.z;
            return dominant < 0f ? -axis : axis;
        }

        static Vector3 StablePerpendicular(Vector3 axis)
        {
            axis = axis.sqrMagnitude > 1e-8f ? axis.normalized : Vector3.right;
            Vector3 reference = Mathf.Abs(axis.y) < 0.8f
                ? Vector3.up
                : Vector3.right;
            Vector3 perpendicular = Vector3.Cross(axis, reference);
            return perpendicular.sqrMagnitude > 1e-8f
                ? perpendicular.normalized
                : Vector3.forward;
        }

        static Vector3 SmallestAxis(Vector3 size)
        {
            if (size.x <= size.y && size.x <= size.z) return Vector3.right;
            if (size.y <= size.z) return Vector3.up;
            return Vector3.forward;
        }

        static float GetAspectRatio(WorkMesh mesh)
        {
            if (mesh == null || mesh.Positions.Count < 2) return 1f;
            Bounds b = mesh.ComputeBounds();
            float shortest = Mathf.Min(b.size.x, Mathf.Min(b.size.y, b.size.z));
            float longest = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
            return shortest > 0f ? (longest / shortest) : 1f;
        }
    }
}
