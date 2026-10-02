using System.Collections.Generic;
using Workspace = RipVertices.QuickDestruction.Editor.QuickDestructionWindow.Workspace;

namespace RipVertices.QuickDestruction.Editor
{
    /// <summary>
    /// Every control the window draws, as its label plus the card and tab that own it.
    /// The search box resolves a typed word to this table and jumps straight to the
    /// setting, so a user does not have to remember which of the six tabs holds it.
    ///
    /// This is data, not documentation. SettingsSearchCoverageTests parses the window
    /// source and fails when a control is added without an entry here, so the table
    /// cannot drift out of sync with the UI it describes.
    /// </summary>
    internal static class QuickDestructionSettingsIndex
    {
        internal readonly struct Entry
        {
            public readonly string Label;
            public readonly string Section;
            public readonly Workspace Workspace;

            public Entry(string label, string section, Workspace workspace)
            {
                Label = label;
                Section = section;
                Workspace = workspace;
            }

            /// <summary>"Structure > Support Detection", or just the tab when the control
            /// sits directly on the page.</summary>
            public string Path => string.IsNullOrEmpty(Section)
                ? QuickDestructionWindow.WorkspaceTitle(Workspace)
                : QuickDestructionWindow.WorkspaceTitle(Workspace) + "  ›  " + Section;
        }

        sealed class IndexState
        {
            public List<Entry> Entries;
        }

        static readonly IndexState s_state = new IndexState();

        public static IReadOnlyList<Entry> All => s_state.Entries ??= Build();

        /// <summary>
        /// Entries whose label or card contains every whitespace-separated term, ranked so
        /// a label match beats a card match and a prefix beats a match in the middle. The
        /// result order is what makes a two-letter query useful rather than noisy.
        /// </summary>
        public static List<Entry> Search(string query, int limit)
        {
            var results = new List<Entry>();
            if (string.IsNullOrWhiteSpace(query)) return results;
            string[] terms = query.Trim().ToLowerInvariant().Split(
                new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);

            var scored = new List<KeyValuePair<int, Entry>>();
            foreach (Entry entry in All)
            {
                string label = entry.Label.ToLowerInvariant();
                string section = entry.Section.ToLowerInvariant();
                int score = 0;
                bool matchedAll = true;
                foreach (string term in terms)
                {
                    int inLabel = label.IndexOf(term, System.StringComparison.Ordinal);
                    int inSection = section.IndexOf(term, System.StringComparison.Ordinal);
                    if (inLabel < 0 && inSection < 0) { matchedAll = false; break; }
                    if (inLabel == 0) score += 6;
                    else if (inLabel > 0) score += 4;
                    else if (inSection == 0) score += 2;
                    else score += 1;
                }
                if (matchedAll) scored.Add(new KeyValuePair<int, Entry>(score, entry));
            }

            scored.Sort((a, b) => b.Key != a.Key
                ? b.Key.CompareTo(a.Key)
                : string.Compare(a.Value.Label, b.Value.Label,
                    System.StringComparison.Ordinal));
            for (int i = 0; i < scored.Count && results.Count < limit; i++)
                results.Add(scored[i].Value);
            return results;
        }

        /// <summary>Total matches, so the list can honestly say how many it is hiding.</summary>
        public static int CountMatches(string query)
        {
            return Search(query, int.MaxValue).Count;
        }

        static List<Entry> Build()
        {
            var list = new List<Entry>(161);
            void Add(string label, string section, Workspace workspace) =>
                list.Add(new Entry(label, section, workspace));
            Add("Width X / Height Y / Depth Z", "BRICK SIZE", Workspace.Analyze);

            Add("Mode", "SELECT SOURCE OBJECTS / WHOLE BUILDING", Workspace.Analyze);
            Add("Show Hollow Shell Thickness While Intact",
                "SELECT SOURCE OBJECTS / WHOLE BUILDING", Workspace.Analyze);

            Add("Cut Face UV Layout", "Cut Faces & Interior Material", Workspace.Analyze);
            Add("Interior Material", "Cut Faces & Interior Material", Workspace.Analyze);
            Add("Use Source Material", "Cut Faces & Interior Material", Workspace.Analyze);

            Add("Despawn After (s)", "Debris & Runtime Re-Fracture", Workspace.Analyze);
            Add("Despawn Debris", "Debris & Runtime Re-Fracture", Workspace.Analyze);
            Add("Final Destruction Iteration", "Debris & Runtime Re-Fracture", Workspace.Analyze);
            Add("Split Hit Fragments At Runtime", "Debris & Runtime Re-Fracture", Workspace.Analyze);

            Add("Ground Landing Dust", "Effects — Particles & Sound", Workspace.Analyze);
            Add("Landing Sound Variants", "Effects — Particles & Sound", Workspace.Analyze);
            Add("Sound Variants", "Effects — Particles & Sound", Workspace.Analyze);
            Add("Ground Landing Sound", "Effects — Particles & Sound", Workspace.Analyze);
            Add("Landing Dust Strength", "Effects — Particles & Sound", Workspace.Analyze);
            Add("Minimum Drop Height (m)", "Effects — Particles & Sound", Workspace.Analyze);
            Add("Minimum Landing Speed", "Effects — Particles & Sound", Workspace.Analyze);
            Add("One-Time Ground Landing Dust", "Effects — Particles & Sound", Workspace.Analyze);
            Add("Particle Override", "Effects — Particles & Sound", Workspace.Analyze);
            Add("Particle Source", "Effects — Particles & Sound", Workspace.Analyze);
            Add("Play On Every Hit", "Effects — Particles & Sound", Workspace.Analyze);
            Add("Preset Particles", "Effects — Particles & Sound", Workspace.Analyze);
            Add("Preset Sound", "Effects — Particles & Sound", Workspace.Analyze);
            Add("Sound Override", "Effects — Particles & Sound", Workspace.Analyze);
            Add("Sound Source", "Effects — Particles & Sound", Workspace.Analyze);

            Add("Activate All Pieces On First Break", "HOW THIS OBJECT TAKES DAMAGE", Workspace.Analyze);

            Add("Break Push (Launch Force)", "Launch & Advanced Shape Controls", Workspace.Analyze);
            Add("Directional Impact Force", "Launch & Advanced Shape Controls", Workspace.Analyze);
            Add("Local Eject Impulse", "Launch & Advanced Shape Controls", Workspace.Analyze);
            Add("Max Fragment Launch Speed", "Launch & Advanced Shape Controls", Workspace.Analyze);
            Add("Max Whole Body Push Speed", "Launch & Advanced Shape Controls", Workspace.Analyze);
            Add("Tall Piece Topple", "Launch & Advanced Shape Controls", Workspace.Analyze);

            Add("Auto Analyze Source", "Output & Bake Options", Workspace.Analyze);
            Add("Place Result In Scene", "Output & Bake Options", Workspace.Analyze);

            Add("Profile Asset", "PROFILE — QUICK OR REUSABLE", Workspace.Analyze);

            Add("Brick Cell Size", "QUICK SHAPE CONTROLS", Workspace.Analyze);
            Add("Directional Influence", "QUICK SHAPE CONTROLS", Workspace.Analyze);
            Add("Exploded View", "QUICK SHAPE CONTROLS", Workspace.Analyze);
            Add("Fragment Shape Irregularity", "QUICK SHAPE CONTROLS", Workspace.Analyze);
            Add("Glass Rings", "QUICK SHAPE CONTROLS", Workspace.Analyze);
            Add("Glass Spokes", "QUICK SHAPE CONTROLS", Workspace.Analyze);
            Add("Impact Focus", "QUICK SHAPE CONTROLS", Workspace.Analyze);
            Add("Impact Position", "QUICK SHAPE CONTROLS", Workspace.Analyze);
            Add("Interior Splinter Scatter", "QUICK SHAPE CONTROLS", Workspace.Analyze);
            Add("Interior Stone Scatter", "QUICK SHAPE CONTROLS", Workspace.Analyze);
            Add("Irregularity / Jitter", "QUICK SHAPE CONTROLS", Workspace.Analyze);
            Add("Lengthwise Split Bias", "QUICK SHAPE CONTROLS", Workspace.Analyze);
            Add("Max Parts", "QUICK SHAPE CONTROLS", Workspace.Analyze);
            Add("Place Impact Point", "QUICK SHAPE CONTROLS", Workspace.Analyze);
            Add("Scatter Length", "QUICK SHAPE CONTROLS", Workspace.Analyze);
            Add("Scatter Style", "QUICK SHAPE CONTROLS", Workspace.Analyze);
            Add("Pane Normal Axis", "QUICK SHAPE CONTROLS", Workspace.Analyze);
            Add("Pillar Length Axis", "QUICK SHAPE CONTROLS", Workspace.Analyze);
            Add("Layer Normal Axis", "QUICK SHAPE CONTROLS", Workspace.Analyze);
            Add("Grain / Length Axis", "QUICK SHAPE CONTROLS", Workspace.Analyze);
            Add("Plate Length Axis", "QUICK SHAPE CONTROLS", Workspace.Analyze);
            Add("Slab Layers", "QUICK SHAPE CONTROLS", Workspace.Analyze);
            Add("Splinter End Sharpness", "QUICK SHAPE CONTROLS", Workspace.Analyze);

            Add("Collider Mode", "Volume & Colliders", Workspace.Analyze);
            Add("Wall Thickness", "Volume & Colliders", Workspace.Analyze);
            Add("Wall Thickness (m)", "Volume & Colliders", Workspace.Analyze);

            Add("Activate All Pieces On First Break", "HOW THIS OBJECT TAKES DAMAGE", Workspace.Bake);

            Add("Auto Analyze Source", "Output & Bake Options", Workspace.Bake);
            Add("Place Result In Scene", "Output & Bake Options", Workspace.Bake);

            Add("Cell Irregularity", "BRICK / CELLS", Workspace.Fracture);
            Add("Use Cell Dimensions", "BRICK / CELLS", Workspace.Fracture);

            Add("Column Irregularity", "COLUMNAR / PILLARS", Workspace.Fracture);

            Add("Cut Face UV Layout", "CUT FACES / INTERIOR MATERIAL", Workspace.Fracture);

            Add("Interior Splinter Scatter", "DIRECTIONAL / WOOD", Workspace.Fracture);
            Add("Splinter End Sharpness", "DIRECTIONAL / WOOD", Workspace.Fracture);

            Add("Auto Refresh After Changes", "Edit-Mode Preview — Options", Workspace.Fracture);
            Add("Wireframe Overlay", "Edit-Mode Preview — Options", Workspace.Fracture);

            Add("Profile", "Fracture", Workspace.Fracture);
            Add("Source", "Fracture", Workspace.Fracture);

            Add("Pattern", "Fracture Settings — Full Control", Workspace.Fracture);
            Add("Seed", "Fracture Settings — Full Control", Workspace.Fracture);
            Add("Shell Thickness", "Fracture Settings — Full Control", Workspace.Fracture);
            Add("Volume Mode", "Fracture Settings — Full Control", Workspace.Fracture);

            Add("Interior Material", "Interior Surface", Workspace.Fracture);
            Add("Use Source Material", "Interior Surface", Workspace.Fracture);

            Add("Layer Irregularity", "LAYERED / SLABS", Workspace.Fracture);

            Add("Cell Size  X / Y / Z", "PATTERN", Workspace.Fracture);
            Add("Directional Influence", "PATTERN", Workspace.Fracture);
            Add("Impact Focus", "PATTERN", Workspace.Fracture);
            Add("Fragment Shape Irregularity", "PATTERN", Workspace.Fracture);
            Add("Layer Count", "PATTERN", Workspace.Fracture);
            Add("Lengthwise Split Bias", "PATTERN", Workspace.Fracture);
            Add("Max Parts", "PATTERN", Workspace.Fracture);
            Add("Rings", "PATTERN", Workspace.Fracture);
            Add("Spokes", "PATTERN", Workspace.Fracture);
            Add("Pane Normal Axis", "PATTERN", Workspace.Fracture);
            Add("Pillar Length Axis", "PATTERN", Workspace.Fracture);
            Add("Layer Normal Axis", "PATTERN", Workspace.Fracture);
            Add("Grain / Length Axis", "PATTERN", Workspace.Fracture);
            Add("Plate Length Axis", "PATTERN", Workspace.Fracture);

            Add("Exploded View", "PREVIEW SURFACE", Workspace.Fracture);

            Add("Angular Jitter", "RADIAL / GLASS", Workspace.Fracture);
            Add("Release Unsupported Islands", "RADIAL / GLASS", Workspace.Fracture);

            Add("Break Apart Force", "Secondary Breakup", Workspace.Fracture);
            Add("Children Per Parent", "Secondary Breakup", Workspace.Fracture);
            Add("Damage Cooldown (s)", "Secondary Breakup", Workspace.Fracture);
            Add("Eligible Parent Limit", "Secondary Breakup", Workspace.Fracture);
            Add("Impact Speed → Damage", "Secondary Breakup", Workspace.Fracture);
            Add("Inherit Parent Velocity", "Secondary Breakup", Workspace.Fracture);
            Add("Max Active Level-Two", "Secondary Breakup", Workspace.Fracture);
            Add("Max Split Iterations", "Secondary Breakup", Workspace.Fracture);
            Add("Minimum Fragment Volume", "Secondary Breakup", Workspace.Fracture);
            Add("Minimum Impact (m/s)", "Secondary Breakup", Workspace.Fracture);
            Add("Minimum Parent Size", "Secondary Breakup", Workspace.Fracture);
            Add("Pieces Per Split", "Secondary Breakup", Workspace.Fracture);
            Add("Secondary Irregularity", "Secondary Breakup", Workspace.Fracture);
            Add("Secondary Strength", "Secondary Breakup", Workspace.Fracture);
            Add("Separation Force", "Secondary Breakup", Workspace.Fracture);
            Add("Settling Grace (s)", "Secondary Breakup", Workspace.Fracture);

            Add("Interior Stone Scatter", "UNIFORM / CONCRETE", Workspace.Fracture);
            Add("Fragment Shape Irregularity", "UNIFORM / CONCRETE", Workspace.Fracture);

            Add("Activation Radius", "Break Force Response", Workspace.Physics);
            Add("Break Push (Launch Force)", "Break Force Response", Workspace.Physics);
            Add("Directional Impact Force", "Break Force Response", Workspace.Physics);
            Add("Explosion Force", "Break Force Response", Workspace.Physics);
            Add("Local Eject Impulse", "Break Force Response", Workspace.Physics);
            Add("Max Fragment Launch Speed", "Break Force Response", Workspace.Physics);
            Add("Max Whole Body Push Speed", "Break Force Response", Workspace.Physics);
            Add("Tall Piece Topple", "Break Force Response", Workspace.Physics);
            Add("Upward Lift", "Break Force Response", Workspace.Physics);

            Add("Strategy", "Collider Baker", Workspace.Physics);

            Add("Collision Layers", "Collision Damage Source", Workspace.Physics);
            Add("Damage Cooldown (s)", "Collision Damage Source", Workspace.Physics);
            Add("Damage Trigger", "Collision Damage Source", Workspace.Physics);
            Add("Debris Must Be Worth", "Collision Damage Source", Workspace.Physics);
            Add("Impact Energy (kJ) -> Damage", "Collision Damage Source", Workspace.Physics);
            Add("Min Velocity (m/s)", "Collision Damage Source", Workspace.Physics);
            Add("Post-Break Wake Radius", "Collision Damage Source", Workspace.Physics);

            Add("Angular Drag", "Mass & Damping", Workspace.Physics);
            Add("Collision Detection", "Mass & Damping", Workspace.Physics);
            Add("Density (kg/m3)", "Mass & Damping", Workspace.Physics);
            Add("Drag", "Mass & Damping", Workspace.Physics);
            Add("Interpolation", "Mass & Damping", Workspace.Physics);
            Add("Max Fragment Mass", "Mass & Damping", Workspace.Physics);
            Add("Min Fragment Mass", "Mass & Damping", Workspace.Physics);
            Add("Use Gravity", "Mass & Damping", Workspace.Physics);

            Add("Despawn After (s)", "DEBRIS AUTO-DESPAWN", Workspace.Runtime);
            Add("Despawn Debris", "DEBRIS AUTO-DESPAWN", Workspace.Runtime);

            Add("Play On Every Hit", "Default Hit Particles & Sound", Workspace.Runtime);
            Add("Preset Particles", "Default Hit Particles & Sound", Workspace.Runtime);
            Add("Preset Sound", "Default Hit Particles & Sound", Workspace.Runtime);
            Add("Profile Particle Override", "Default Hit Particles & Sound", Workspace.Runtime);
            Add("Profile Sound Override", "Default Hit Particles & Sound", Workspace.Runtime);

            Add("Ground Landing Dust", "FRAGMENT LANDING EFFECTS", Workspace.Runtime);
            Add("Ground Landing Sound", "FRAGMENT LANDING EFFECTS", Workspace.Runtime);
            Add("Landing Dust Strength", "FRAGMENT LANDING EFFECTS", Workspace.Runtime);
            Add("Min Landing Speed", "FRAGMENT LANDING EFFECTS", Workspace.Runtime);
            Add("Minimum Drop Height (m)", "FRAGMENT LANDING EFFECTS", Workspace.Runtime);
            Add("One-Time Ground Landing Dust", "FRAGMENT LANDING EFFECTS", Workspace.Runtime);

            Add("Break Apart Force", "Iterative Runtime Re-Fracture", Workspace.Runtime);
            Add("Max Split Iterations", "Iterative Runtime Re-Fracture", Workspace.Runtime);
            Add("Minimum Fragment Volume", "Iterative Runtime Re-Fracture", Workspace.Runtime);
            Add("Pieces Per Split", "Iterative Runtime Re-Fracture", Workspace.Runtime);
            Add("Split Hit Fragments At Runtime", "Iterative Runtime Re-Fracture", Workspace.Runtime);

            Add("Max Active Fragments", "Lifecycle & Pooling", Workspace.Runtime);

            Add("Movement Below (m/s)", "REST DETECTION", Workspace.Runtime);
            Add("Stay Calm For (s)", "REST DETECTION", Workspace.Runtime);

            Add("Degrade Smallest Over Budget", "SMALLEST-FIRST DEGRADATION", Workspace.Runtime);
            Add("Freeze Above (active)", "SMALLEST-FIRST DEGRADATION", Workspace.Runtime);
            Add("Remove Above (visible)", "SMALLEST-FIRST DEGRADATION", Workspace.Runtime);
            Add("Triangle Budget", "SMALLEST-FIRST DEGRADATION", Workspace.Runtime);

            Add("Rigidbody Damage Multiplier", "Strength & Impact Damage", Workspace.Runtime);
            Add("Strength / Health", "Strength & Impact Damage", Workspace.Runtime);
            Add("Minimum Hit Damage", "Strength & Impact Damage", Workspace.Runtime);
            Add("Scene Debris Cleanup", "Scene Debris Budget", Workspace.Runtime);
            Add("Max Scene Debris", "Scene Debris Budget", Workspace.Runtime);
            Add("Scene Triangle Budget", "Scene Debris Budget", Workspace.Runtime);
            Add("Removals Per Frame", "Scene Debris Budget", Workspace.Runtime);
            Add("Debris Cleanup Rule", "Scene Debris Budget", Workspace.Runtime);
            Add("Keep Debris", "DEBRIS AUTO-DESPAWN", Workspace.Runtime);
            Add("Scene Despawn After (s)", "Scene Debris Budget", Workspace.Runtime);
            Add("Small Debris Volume (m3)", "Scene Debris Budget", Workspace.Runtime);
            Add("Spread Impact To Nearby Objects", "Strength & Impact Damage", Workspace.Runtime);
            Add("Impact Spread Radius", "Strength & Impact Damage", Workspace.Runtime);
            Add("Spread Distance Falloff", "Strength & Impact Damage", Workspace.Runtime);
            Add("Impact Spread Parent", "Strength & Impact Damage", Workspace.Runtime);
            Add("Spread Damage (%)", "Strength & Impact Damage", Workspace.Runtime);
            Add("Spread Impulse (%)", "Strength & Impact Damage", Workspace.Runtime);

            Add("Smooth Despawn", "WHEN CLEANUP HAPPENS", Workspace.Runtime);

            Add("Clean Up After (s)", "WHEN SHOULD DEBRIS CLEAN UP?", Workspace.Runtime);

            Add("Body Mode", "Intact Structural Body", Workspace.Structure);
            Add("Fall Damage", "Intact Structural Body", Workspace.Structure);
            Add("Intact Mass Multiplier", "Intact Structural Body", Workspace.Structure);

            Add("Check Interval (s)", "Support Detection", Workspace.Structure);
            Add("Collapse Delay (s)", "Support Detection", Workspace.Structure);
            Add("Maximum Unsupported Span (m)", "Support Detection", Workspace.Structure);
            Add("Edge Inset", "Support Detection", Workspace.Structure);
            Add("Minimum Support Remaining (%)", "Support Detection", Workspace.Structure);
            Add("Probe Distance", "Support Detection", Workspace.Structure);
            Add("Probe Grid", "Support Detection", Workspace.Structure);
            Add("Support Layers", "Support Detection", Workspace.Structure);
            return list;
        }
    }
}
