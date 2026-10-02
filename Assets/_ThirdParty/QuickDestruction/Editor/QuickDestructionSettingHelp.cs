using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace RipVertices.QuickDestruction.Editor
{
    /// <summary>Shared field help for the custom window, including labels that differ
    /// from the serialized profile field. Profile tooltips remain the source of truth.</summary>
    internal static class QuickDestructionSettingHelp
    {
        static readonly Dictionary<string, string> s_help = Build();

        internal static string GetTooltip(string label) =>
            s_help.TryGetValue(Key(label), out string text) ? text : string.Empty;

        static string Key(string label)
        {
            var key = new StringBuilder();
            foreach (char c in label ?? string.Empty)
                if (char.IsLetterOrDigit(c)) key.Append(char.ToLowerInvariant(c));
            return key.ToString();
        }

        static Dictionary<string, string> Build()
        {
            var help = new Dictionary<string, string>();
            foreach (FieldInfo field in typeof(DestructionProfile).GetFields())
            {
                TooltipAttribute tip = field.GetCustomAttribute<TooltipAttribute>();
                if (tip != null) help[Key(field.Name)] = tip.tooltip;
            }
            void Text(string label, string text) => help[Key(label)] = text;
            void Alias(string label, string field)
            {
                if (help.TryGetValue(Key(field), out string text)) Text(label, text);
            }

            Alias("Activate All Pieces On First Break", "ActivateAllOnFirstBreak");
            Alias("Activation Radius", "DefaultExplosionRadius");
            Alias("Angular Jitter", "RadialJitter");
            Alias("Body Mode", "IntactBody");
            Alias("Break Apart Force", "RuntimeSplitSeparationForce");
            Alias("Break Push (Launch Force)", "FragmentPushMultiplier");
            Alias("Cell Irregularity", "BrickJitter");
            Alias("Cell Size X / Y / Z", "BrickCellSize");
            Alias("Check Interval (s)", "SupportCheckInterval");
            Alias("Children Per Parent", "SecondaryPiecesPerFragment");
            Alias("Clean Up After (s)", "CleanupLifetime");
            Alias("Collapse Delay (s)", "SupportGraceTime");
            Alias("Collision Detection", "FragmentCollisionDetection");
            Alias("Column Irregularity", "ColumnJitter");
            Alias("Debris Must Be Worth", "DebrisImpactSignificance");
            Alias("Density (kg/m3)", "Density");
            Alias("Despawn After (s)", "DespawnDebrisAfter");
            Alias("Spread Distance Falloff", "SpreadFalloff");
            Text("Impact Spread Parent", "Optional common ancestor of the struck object and its building modules. Empty uses this baked Destructible Root and its children. Objects outside this hierarchy are never affected by impact spread.");
            Alias("Spread Damage (%)", "ImpactSpreadDamagePercent");
            Alias("Spread Impulse (%)", "ImpactSpreadImpulsePercent");
            Alias("Directional Impact Force", "DirectionalImpactMultiplier");
            Alias("Edge Inset", "SupportProbeInset");
            Alias("Eligible Parent Limit", "SecondaryEligibleFragments");
            Alias("Explosion Force", "DefaultExplosionForce");
            Alias("Final Destruction Iteration", "RuntimeSplitMaxDepth");
            Alias("Freeze Above (active)", "MaxActiveFragments");
            Alias("Glass Rings", "RadialRings");
            Alias("Glass Spokes", "RadialSpokes");
            Alias("Ground Landing Dust", "GroundDustParticlePrefab");
            Alias("Ground Landing Sound", "GroundDustSound");
            Alias("Impact Energy (kJ) -> Damage", "ImpulseToDamage");
            Alias("Impact Speed → Damage", "SecondaryImpulseToDamage");
            Alias("Inherit Parent Velocity", "SecondaryInheritVelocity");
            Alias("Interior Splinter Scatter", "WoodSplitIrregularity");
            Alias("Interior Stone Scatter", "ConcreteIrregularity");
            Alias("Interpolation", "FragmentInterpolation");
            Alias("Landing Dust Strength", "FragmentImpactStrength");
            Alias("Landing Sound Variants", "GroundDustSoundVariants");
            Alias("Layer Count", "SlabLayers");
            Alias("Layer Irregularity", "SlabJitter");
            Alias("Local Eject Impulse", "LocalImpactEjectSpeed");
            Alias("Max Active Level-Two", "MaxActiveSecondaryBodies");
            Alias("Max Parts", "FragmentCount");
            Alias("Max Split Iterations", "RuntimeSplitMaxDepth");
            Alias("Max Whole Body Push Speed", "MaxWholeBodyLaunchSpeed");
            Alias("Maximum Unsupported Span (m)", "MaxUnsupportedSpan");
            Alias("Min Landing Speed", "FragmentImpactMinSpeed");
            Alias("Minimum Landing Speed", "FragmentImpactMinSpeed");
            Alias("Min Velocity (m/s)", "MinCollisionVelocity");
            Alias("Minimum Drop Height (m)", "FragmentImpactMinDropHeight");
            Alias("Minimum Fragment Volume", "RuntimeSplitMinVolume");
            Alias("Minimum Impact (m/s)", "SecondaryMinImpactVelocity");
            Alias("Minimum Parent Size", "SecondaryMinParentVolumeRatio");
            Alias("Minimum Support Remaining (%)", "RequiredSupportRatio");
            Alias("Movement Below (m/s)", "SleepVelocity");
            Alias("One-Time Ground Landing Dust", "FragmentImpactEffects");
            Alias("Particle Override", "HitParticlePrefab");
            Alias("Pieces Per Split", "RuntimeSplitPieces");
            Alias("Play On Every Hit", "PlayHitEffectsOnEveryHit");
            Alias("Post-Break Wake Radius", "PostFractureImpactRadius");
            Alias("Preset Particles", "UseDefaultHitParticles");
            Alias("Preset Sound", "UseDefaultHitSound");
            Alias("Probe Distance", "SupportProbeDistance");
            Alias("Probe Grid", "SupportProbeGrid");
            Alias("Profile Particle Override", "HitParticlePrefab");
            Alias("Profile Sound Override", "HitSound");
            Alias("Release Unsupported Islands", "ReleaseUnsupportedGlass");
            Alias("Remove Above (visible)", "MaxVisibleFragments");
            Alias("Rings", "RadialRings");
            Alias("Scatter Length", "InteriorScatterLength");
            Alias("Scatter Style", "InteriorScatterStyle");
            Alias("Separation Force", "SecondarySeparationForce");
            Alias("Settling Grace (s)", "SecondaryImpactGrace");
            Alias("Shell Thickness", "SolidifyThickness");
            Alias("Show Hollow Shell Thickness While Intact", "ShowSolidifiedShellWhileIntact");
            Alias("Sound Override", "HitSound");
            Alias("Sound Variants", "HitSoundVariants");
            Alias("Splinter End Sharpness", "WoodEndSharpness");
            Alias("Split Hit Fragments At Runtime", "RuntimeFragmentSplitting");
            Alias("Spokes", "RadialSpokes");
            Alias("Stay Calm For (s)", "SleepDuration");
            Alias("Strategy", "Colliders");
            Alias("Collider Mode", "Colliders");
            Alias("Strength / Health", "Health");
            Alias("Tall Piece Topple", "TallFragmentToppleSpeed");
            Alias("Triangle Budget", "MaxVisibleTriangles");
            Alias("Upward Lift", "ExplosionUpwardModifier");
            Alias("Use Cell Dimensions", "BrickUseCellSize");
            Alias("Use Source Material", "InteriorUseSourceMaterial");
            Alias("Wall Thickness", "SolidifyThickness");
            Alias("Wall Thickness (m)", "SolidifyThickness");
            Alias("Width X / Height Y / Depth Z", "BrickCellSize");

            Text("Auto Analyze Source", "Analyze source mesh health automatically after changing the source selection. This does not bake or replace scene objects.");
            Text("Auto Refresh After Changes", "Refresh the edit-mode fracture preview after settings change. Disable to refresh manually; baked objects are unaffected until baked again.");
            Text("Cut Face UV Layout", "Choose how newly cut interior faces map their texture coordinates. This affects the cut surface material, not the original exterior UVs.");
            Text("Damage Cooldown (s)", "Minimum time in seconds between accepted damage events on this receiver. In Secondary Breakup it applies to each pre-baked secondary parent.");
            Text("Directional Influence", "How strongly fracture cuts follow the chosen material axis. Increase for more aligned, directional pieces.");
            Text("Lengthwise Split Bias", "Bias wood cuts along its length so fragments resemble long splinters. Adjust together with the grain axis.");
            Text("Exploded View", "Separate preview fragments visually to inspect their shape and cut surfaces. This does not add runtime explosion force.");
            Text("Impact Position", "Position of the preview impact focus used by the fracture pattern. Place the point on the source to concentrate cuts there.");
            Text("Place Impact Point", "Enable picking an impact focus on the source in Scene View. This changes the fracture preview focus, not gameplay damage.");
            Text("Irregularity / Jitter", "Variation in the current pattern's cuts. Higher values make its cells, rings, columns or layers less regular.");
            Text("Mode", "Choose how selected source meshes are treated: individual objects or a combined whole-building structure.");
            Text("Particle Source", "Use the preset's generated hit particles or assign your own ParticleSystem override.");
            Text("Sound Source", "Use the preset's generated impact sound or assign your own audio override.");
            Text("Place Result In Scene", "Create a scene instance of the baked destructible result. The saved prefab can still be reused elsewhere.");
            Text("Profile", "DestructionProfile that stores fracture, physics and runtime settings. Editing a shared asset affects other objects using that profile.");
            Text("Profile Asset", "Assign a reusable destruction profile asset for this source. Shared profiles apply their settings to every object that references them.");
            Text("Rigidbody Damage Multiplier", "Multiplier applied to Health damage calculated from collision energy. Higher values make impact damage stronger without changing baked piece size or physical impulse.");
            Text("Source", "The mesh object to analyze, preview and bake into a destructible prefab.");
            Text("Volume Mode", "Solid fractures a closed volume. Hollow Shell gives thin or open surfaces inward thickness before fracture.");
            Text("Wireframe Overlay", "Draw the fracture preview edges in Scene View to inspect the generated geometry.");
            Text("Scene Debris Cleanup", "Use one cleanup rule across this scene, except profiles with Keep Debris enabled. Overrides profile timers and waits for grounded rest before removal. Disable to use profile settings.");
            Text("Max Scene Debris", "Maximum retained loose fragments across the scene, including secondary and runtime-split debris. Smallest excess pieces are removed first. 0 disables this count limit.");
            Text("Scene Triangle Budget", "Maximum main-mesh triangles in retained loose debris across this scene. 0 disables this limit. Cleanup satisfies both the count and triangle limits.");
            Text("Removals Per Frame", "Maximum fragments that begin removal in one frame. Lower values spread cleanup work out; bursts may exceed the limit briefly.");
            Text("Debris Cleanup Rule", "Choose what disappears after grounded rest. New managers use All Debris After Rest by default. Other options: scene limits, keep all, small debris only, dynamic debris only, or small and runtime debris.");
            Text("Scene Despawn After (s)", "Seconds of continuous rest on a supporting surface before removal is allowed. Default 30 seconds. Movement or loss of support resets the timer. Also protects airborne debris when scene limits are exceeded.");
            Text("Small Debris Volume (m3)", "Baked debris at or below this world-space volume is eligible for small-piece rules. The Small and Runtime rule also removes runtime-generated pieces of any size after rest. 0.125 cubic metres equals a cube with sides of 0.5 metres; object scale is included.");
            Text("Show Hollow Shell Thickness While Intact", "Display the generated shell thickness before the object breaks. This helps inspect open edges on hollow objects without fracturing them first.");
            Text("Interior Material", "Material used on newly exposed cut faces inside the broken object. The original exterior keeps its source materials.");
            Text("Glass Rings", "Number of concentric fracture rings in the glass pattern. More rings create more, smaller glass fragments.");
            Text("Rings", help[Key("Glass Rings")]);
            Text("Glass Spokes", "Number of radial cracks around the glass impact centre. More spokes divide the pane into narrower fragments.");
            Text("Spokes", help[Key("Glass Spokes")]);
            Text("Collider Mode", "Strategy used to bake fragment collision shapes. Simpler shapes cost less physics work; more detailed shapes follow the mesh more closely.");
            Text("Strategy", help[Key("Collider Mode")]);
            Text("Pattern", "Fracture pattern used to generate pieces, such as concrete, wood, glass or brick. Each pattern exposes its own shape controls.");
            Text("Seed", "Seed for deterministic fracture variation. The same mesh, settings and seed reproduce the same generated pattern.");
            Text("Angular Jitter", "Variation in the spacing of radial glass cracks. Increase for less uniform wedges around the impact centre.");
            Text("Activation Radius", "Radius in metres used by the profile's default targeted explosion. It selects fragments around the blast origin.");
            Text("Explosion Force", "Default radial impulse for the profile's targeted explosion. Heavier fragments gain less speed from the same impulse.");
            Text("Collision Layers", "Layers accepted as sources of collision damage. The optional impact-spread wave also uses this mask to choose nearby recipients.");
            Text("Angular Drag", "Damping that slows fragment rotation over time. Higher values make spinning debris settle sooner.");
            Text("Drag", "Damping that slows fragment movement over time. Higher values reduce how long debris keeps travelling.");
            Text("Max Fragment Mass", "Upper limit in kilograms for the mass assigned to one baked fragment from material density and volume.");
            Text("Min Fragment Mass", "Lower limit in kilograms for the mass assigned to one baked fragment, preventing very small pieces from becoming excessively light.");
            Text("Pane Normal Axis", "Axis perpendicular to the glass pane. Auto derives the thin dimension; choose an axis explicitly if the pane orientation is ambiguous.");
            Text("Pillar Length Axis", "Long axis of the column or pillar. Fracture cuts follow this orientation; Auto estimates it from the source geometry.");
            Text("Layer Normal Axis", "Axis perpendicular to the slab layers. Changing it changes the direction in which the layers stack.");
            Text("Grain / Length Axis", "Direction of the wood grain used by lengthwise fracture cuts. Auto estimates it from the source proportions.");
            Text("Plate Length Axis", "Preferred length direction of plate fragments. Used with directional influence and lengthwise split bias.");
            return help;
        }
    }
}
