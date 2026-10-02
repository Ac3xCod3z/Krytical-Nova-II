using System;
using System.Collections.Generic;
using UnityEngine;

namespace RipVertices.QuickDestruction
{
    /// <summary>
    /// Ownership record for everything the bake pipeline generated. Rebake updates only files
    /// listed here and "Clean Generated" removes only manifest entries - never user assets.
    /// Diff-friendly on purpose: plain serialized lists, stable ordering.
    /// </summary>
    public class GeneratedManifest : ScriptableObject
    {
        public const int CurrentSchemaVersion = 3;
        // v26: residual cut-plane holes are detected and filled in a topology repair pass.
        // v34: sliver suppression (balanced-cut threshold, micro-fragment merge) and support
        // connectivity (GroundAnchor + neighbours baked for all ReleaseWhenUnsupported profiles)
        // change fracture output - existing bakes must be regenerated.
        // v35: manifold gate skips degenerate triangles, cap needle rejection with hole
        // refill, 3x3x3 neighbourhood welding and degenerate-free triangle splitting.
        // v36: needle rejection applies only to blades outside the cut contour (interior
        // slivers are kept so caps stay closed); CleanCapLoop keeps collinear points.
        // v37: pre-baked level-two pieces now receive the same render-only interior
        // scatter that Preview and primary baked fragments receive.
        // v38: scatter side faces preserve triangle aspect and parent texel density;
        // runtime cuts cheaply seal residual cut-plane loops and reject real openings.
        // v39: extreme low-volume concrete needles receive one bounded longest-axis
        // cleanup split after the normal fracture pass.
        // v42: bake and runtime reject open cut results before export, concrete spikes
        // are rejected during plane selection, and proxy-box fallbacks are removed.
        // v43: the sliver anti-needle guard no longer short-circuits Directional
        // Influence. It now only forces the cross-cut direction, so the slider scales
        // the steering proportionally and an explicit Preferred Cut Axis is honoured.
        // v44: Hollow Shell thickness is authored in scene units and converted through
        // the source MeshFilter scale before the local-space fracture solve.
        // v45: Directional Wood keeps four authored plane attempts, then uses a bounded
        // grain-aligned rescue set instead of repeating up to 24 random planes on every
        // difficult Hollow Shell fragment.
        // v47: Hollow Shell uses a true hard-corner miter and face-correct inner/rim
        // normals, removing diagonal shading and shadow seams on modular plane walls.
        // v48: structural neighbours require measurable shared-face area and serialize
        // contact weights, so weak remnants release after most bonded area is destroyed.
        // v53: generated Hollow Shell rims honour the selected cut-face UV layout and
        // rectangular modular Intact collision uses oriented box primitives.
        // v54: FitToCut Hollow Shell rims preserve authored UV density across
        // subdivided Plane boundary segments instead of repeating the full island.
        // v55: Whole Building Hollow Shell solidifies only open modular surfaces;
        // closed modules retain their authored geometry, depth and collision.
        public const int SolverVersion = 56;

        [Serializable]
        public class Entry
        {
            public string AssetPath;
            public string Kind; // manifest / prefab / material
        }

        public int SchemaVersion = CurrentSchemaVersion;
        public int BakedSolverVersion = SolverVersion;

        [Header("Source")]
        public string SourceGuid;
        public string SourceName;
        // Whole Building can render a prepared Hollow Shell while intact. Keep the
        // authored thin source beside it so turning the option off on a later rebake is
        // lossless and never fractures the already thickened visual.
        public Mesh AuthoringSourceMesh;
        public bool IntactUsesShellThickness;

        [Header("Bake Identity")]
        public int InputHash;
        public string ProfileName;
        public string BakeTimestampUtc;

        [Header("Owned Assets")]
        public List<Entry> Entries = new List<Entry>();

        [Header("Report")]
        public int FragmentCount;
        public int SecondaryFragmentCount;
        public int TotalTriangles;
        public float BakeSeconds;
        public List<string> Warnings = new List<string>();
    }
}
