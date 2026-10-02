using System.Collections.Generic;
using UnityEngine;

namespace RipVertices.QuickDestruction
{
    /// <summary>
    /// A destruction profile is a complete behaviour contract: it configures fracture geometry,
    /// physics, runtime lifecycle and budgets together so users cannot accidentally combine an
    /// expensive setup. Presets (Glass / Wood / Concrete / Brick) are factory configurations
    /// of this asset, versioned in the project like any other asset.
    /// </summary>
    [CreateAssetMenu(fileName = "DestructionProfile", menuName = "Quick Destruction/Destruction Profile")]
    public class DestructionProfile : ScriptableObject
    {
        public const int CurrentSchemaVersion = 49;
        public const float DefaultHealth = 200f;
        public const float MaxHealth = 2000f;
        public const float DefaultFragmentLaunchSpeed = 20f;
        public const float DefaultWholeBodyLaunchSpeed = 20f;

        const float DefaultLightEjectSpeed = 2.5f;
        const float DefaultMineralEjectSpeed = 4f;
        const float DefaultLightPushMultiplier = 1f;
        const float DefaultMineralPushMultiplier = 1.35f;

        [HideInInspector] public int SchemaVersion = CurrentSchemaVersion;

        [Header("Fracture")]
        public FracturePattern Pattern = FracturePattern.UniformConcrete;
        [Range(2, 600)]
        [Tooltip("Maximum number of primary physical pieces requested from the solver. Standard objects are safety-limited to 250; the editor's Whole Building mode allows up to 600 with runtime fragment budgets recommended. It may return fewer when additional cuts would create invalid or extremely small geometry.")]
        public int FragmentCount = 80;
        public int Seed = 12345;
        [Tooltip("Creates an inward-offset inner surface before fracture so a thin/open mesh breaks as a hollow shell instead of a completely filled solid. Leave off when the source already has authored inner geometry.")]
        public bool Solidify;
        [Tooltip("Wall thickness in metres generated inward when Hollow Shell is enabled (1 Unity unit = 1 m; 0.2 m = 20 cm). Mesh Transform scale is handled automatically.")]
        [Min(0.001f)] public float SolidifyThickness = 0.08f;

        [Header("Secondary Breakup")]
        [Tooltip("Pre-bakes a second fracture level inside selected large primary fragments.")]
        public SecondaryBreakupLevel SecondaryBreakup = SecondaryBreakupLevel.Off;
        [Tooltip("Maximum number of the largest primary fragments that receive a second baked level.")]
        [Range(1, 250)] public int SecondaryEligibleFragments = 8;
        [Tooltip("Requested child-piece count inside each eligible primary fragment.")]
        [Range(2, 12)] public int SecondaryPiecesPerFragment = 4;
        [Tooltip("A primary fragment must be at least this multiple of average primary volume.")]
        [Range(0f, 3f)] public float SecondaryMinParentVolumeRatio = 0.8f;
        [Tooltip("Irregularity of the second-level cuts.")]
        [Range(0f, 1f)] public float SecondaryIrregularity = 0.4f;
        [Tooltip("Accumulated impact damage required to split an eligible primary fragment.")]
        [Min(0.1f)] public float SecondaryStrength = 35f;
        [Tooltip("Minimum relative velocity before a primary fragment receives secondary impact damage.")]
        [Min(0f)] public float SecondaryMinImpactVelocity = 4.5f;
        [Tooltip("Maps impact speed in m/s (collision impulse divided by the fragment's mass) " +
                 "to secondary-fracture damage. Speed rather than raw impulse, so a heavy " +
                 "block and a light chip need the same hit to break rather than the heavy one " +
                 "shattering the instant it touches the ground.")]
        public AnimationCurve SecondaryImpulseToDamage =
            AnimationCurve.Linear(0f, 0f, 25f, 50f);
        [Tooltip("Settling time granted to a fragment after it comes loose, during which it " +
                 "takes no secondary impact damage. Without it the collapse itself breaks every " +
                 "pre-baked fragment at once, because pieces separate touching each other.")]
        [Range(0f, 3f)] public float SecondaryImpactGrace = 0.6f;
        [Tooltip("Minimum time between accepted secondary impact events.")]
        [Range(0f, 2f)] public float SecondaryDamageCooldown = 0.2f;
        [Tooltip("Small separation impulse applied when child pieces replace their parent.")]
        [Range(0f, 50f)] public float SecondarySeparationForce = 3f;
        [Tooltip("Per-destructible cap of active second-level Rigidbodies.")]
        [Range(1, 128)] public int MaxActiveSecondaryBodies = 24;
        [Tooltip("Child pieces inherit the parent's linear and angular velocity.")]
        public bool SecondaryInheritVelocity = true;

        [Header("Runtime Iterative Fracture")]
        [Tooltip("Runtime re-fracture. A hit loose fragment is sliced dynamically instead of requiring pre-baked secondary children.")]
        public bool RuntimeFragmentSplitting = true;
        [Tooltip("Requested children produced by one runtime split.")]
        [Range(2, 4)] public int RuntimeSplitPieces = 2;
        [Tooltip("Final runtime destruction iteration. 1 is the first split; pieces at this generation no longer split again.")]
        [Range(1, 4)] public int RuntimeSplitMaxDepth = 2;
        [Tooltip("Per-fragment HP as a fraction of the object's Health. In Dynamic mode every loose primary fragment absorbs this much damage before a hit may split it. 0 = splits on the first hit. 0.5 = a fragment takes roughly half as many hits as the whole object.")]
        [Range(0f, 2f)] public float FragmentHealthFraction = 0.5f;
        [HideInInspector] public float RuntimeSplitStrength = 25f;
        [Tooltip("Fragments with a local bounds volume below this value are not split again.")]
        [Min(0.000001f)] public float RuntimeSplitMinVolume = 0.002f;
        [HideInInspector] public int RuntimeSplitMaxActivePieces = 32;
        [HideInInspector] public int MaxSplitsPerFrame = 1;
        [HideInInspector] public int RuntimeSplitMaxSourceTriangles = 5000;
        [HideInInspector] public int RuntimeSplitMaxQueuedRequests = 16;
        [HideInInspector] public float RuntimeSplitCollisionCooldown = 0.2f;
        [HideInInspector] public int RuntimeSplitConvexColliderDepth = 1;
        [Tooltip("Small separation impulse applied to newly sliced runtime pieces.")]
        [Range(0f, 20f)] public float RuntimeSplitSeparationForce = 1.5f;
        [HideInInspector] public bool RuntimeSplitPiecesDisappear;
        [HideInInspector] public float RuntimeSplitDisappearAfter = 8f;
        [HideInInspector] public int MaxActiveTriangles = 80000;
        // Retained only so profiles created by the previous preview build deserialize
        // without data loss. Material-specific Chunk Irregularity now owns sparse
        // render-only interior scatter, so this legacy value is intentionally unused.
        [HideInInspector] public float InteriorCutJaggedness = 0f;

        [Header("Default Hit Particles And Sound")]
        [Tooltip("Use the material-aware procedural particle burst supplied by the selected fracture preset when no profile particle override is assigned.")]
        public bool UseDefaultHitParticles = true;
        [Tooltip("Use the material-aware synthesized sound supplied by the selected fracture preset when no profile sound override is assigned.")]
        public bool UseDefaultHitSound = true;
        [Tooltip("Play the profile effects for every ApplyDamage hit, including later hits on fractured pieces.")]
        public bool PlayHitEffectsOnEveryHit = true;
        [Tooltip("Optional profile-level ParticleSystem override. When empty, the selected preset supplies its procedural particle burst.")]
        public ParticleSystem HitParticlePrefab;
        [Tooltip("Optional profile-level AudioClip override. When empty, the selected preset supplies its synthesized hit sound.")]
        public AudioClip HitSound;
        [Tooltip("Extra sound variants. Every break picks one at random from the override above plus this list, so repeated hits do not replay the same clip.")]
        public List<AudioClip> HitSoundVariants = new List<AudioClip>();
        [Tooltip("Optional profile-level ParticleSystem for one-time ground landing dust. It plays only after a primary fragment falls far enough onto a static upward-facing surface.")]
        public ParticleSystem GroundDustParticlePrefab;
        [Tooltip("Optional sound for one-time ground landing dust. When empty, uses the bundled QD_stone falls clip rather than the normal hit sound.")]
        public AudioClip GroundDustSound;
        [Tooltip("Extra landing-sound variants. Every landing picks one at random from the clip above plus this list.")]
        public List<AudioClip> GroundDustSoundVariants = new List<AudioClip>();
        [Tooltip("Play a one-time dust burst when a large primary fragment lands on static ground after a real fall. This is independent from hit particles and never inherits surface colour.")]
        public bool FragmentImpactEffects = true;
        [Min(0f), Tooltip("Minimum downward landing speed (m/s) before ground dust plays.")]
        public float FragmentImpactMinSpeed = 1.5f;
        [Min(0f), Tooltip("Minimum vertical fall distance (metres) required before a primary fragment may play its one-time ground dust.")]
        public float FragmentImpactMinDropHeight = 0.75f;
        [Range(0f, 1f), Tooltip("Loudness and size of ground landing dust relative to piece size.")]
        public float FragmentImpactStrength = 0.6f;

        [Header("Uniform (Concrete)")]
        [Tooltip("Changes the actual Uniform Concrete cut layout and fragment silhouettes. 0 keeps cuts close to fragment centres for more even, block-like pieces; 1 allows stronger cut-point wander and more varied angular pieces. This changes baked geometry and requires Bake & Setup.")]
        [Range(0f, 1f)] public float FragmentShapeIrregularity = 0.3f;
        [Tooltip("Interior Stone Scatter. Higher values add many irregular closed 3D stone chips on new interior faces. It never changes the seed, cut layout, closed fragment mesh or collider.")]
        [Range(0f, 1f)] public float ConcreteIrregularity = 0.3f;

        [Header("Radial (Glass)")]
        [Tooltip("Pane normal used by the radial shatter. Auto uses the thinnest local axis of each disconnected mesh element independently.")]
        public AxisMode GlassNormalAxis = AxisMode.Auto;
        [Range(3, 24)] public int RadialSpokes = 6;
        [Range(1, 8)] public int RadialRings = 2;
        [Range(0f, 1f)] public float RadialJitter = 0.35f;
        [Tooltip("After a local glass hit, releases every fragment island no longer connected to the baked pane boundary.")]
        public bool ReleaseUnsupportedGlass = true;

        [Header("Directional Cut Bias")]
        [Tooltip("How strongly free-form cuts follow the selected local length/width frame. 0 preserves the pattern's original cuts; 1 fully follows the preferred local direction. Wood defaults to 0.7. Concrete and slab cross-cuts may opt in without changing material type. Glass, bricks and impact shatter use their own pattern-specific direction rules.")]
        [Range(0f, 1f)] public float DirectionalCutStrength = 0f;
        [Tooltip("Preferred local length direction for directional cuts. Auto uses the longest local axis of each disconnected mesh element independently. The serialized field name is retained for profile compatibility.")]
        public AxisMode WoodAxis = AxisMode.Auto;
        [Tooltip("Relative split direction for every affected element. 0 cuts mostly across its local width, 1 splits mostly along its local length, and intermediate values mix both directions.")]
        [Range(0f, 1f)] public float FiberCutShare = 0.8f;
        public AxisMode PreferredCutAxis
        {
            get => WoodAxis;
            set => WoodAxis = value;
        }
        public float LengthwiseSplitBias
        {
            get => FiberCutShare;
            set => FiberCutShare = value;
        }
        [Tooltip("Interior Splinter Scatter. Higher values add a few longer render-only wood splinters on new interior faces. It never changes the seed, cut layout, closed fragment mesh or collider.")]
        [Range(0f, 1f)] public float WoodSplitIrregularity = 0.35f;
        [Tooltip("Tilts widthwise cross-cuts into more wedge-like, sharp splinter ends. Has the strongest effect when Lengthwise Split Bias is below 1.")]
        [Range(0f, 1f)] public float WoodEndSharpness = 0.6f;

        [Header("Brick / Cells")]
        [Tooltip("Use explicit local-space cell width/height/depth instead of deriving the grid from Max Parts.")]
        public bool BrickUseCellSize = true;
        [Tooltip("Target brick/cell dimensions in source-mesh local units (X width, Y height, Z depth).")]
        public Vector3 BrickCellSize = new Vector3(0.5f, 0.25f, 0.25f);
        [Tooltip("Random offset of grid planes. 0 creates regular cells; 1 creates visibly uneven masonry.")]
        [Range(0f, 1f)] public float BrickJitter = 0.35f;

        [Header("Layered Slabs")]
        [Tooltip("Normal of the stacked layers. Auto uses the thinnest local axis of each disconnected mesh element independently.")]
        public AxisMode SlabNormalAxis = AxisMode.Auto;
        [Tooltip("Number of parallel sheets cut along the thinnest mesh axis before cross cuts run.")]
        [Range(2, 16)] public int SlabLayers = 6;
        [Tooltip("Random offset of the layer planes. 0 keeps perfectly even sheets.")]
        [Range(0f, 1f)] public float SlabJitter = 0.25f;

        [Header("Columnar / Pillars")]
        [Tooltip("Axis along which every pillar stays continuous. Auto follows every disconnected element's own longest local axis; choose X, Y or Z to force one shared direction.")]
        public AxisMode PillarAxis = AxisMode.Auto;
        [Tooltip("Random offset of the column grid. 0 creates perfectly regular prisms.")]
        [Range(0f, 1f)] public float ColumnJitter = 0.35f;

        [Header("Impact Shatter")]
        [Tooltip("How strongly cuts cluster around the impact point. Higher values concentrate small debris near the hit.")]
        [Range(0f, 1f)] public float ImpactFocus = 0.65f;
        [Tooltip("Place the hit yourself instead of letting the seed choose it. Drag the handle in the Scene view.")]
        public bool ImpactPointOverride;
        [Tooltip("Hit position as a fraction of the source bounds on each axis. 0.5 is the centre.")]
        public Vector3 ImpactPointLocal = new Vector3(0.5f, 0.5f, 1f);

        [Header("Interior Scatter")]
        [Tooltip("Shape of the render-only detail on cut faces. Auto uses splinters for Wood and stone chips for every other pattern.")]
        public InteriorScatterStyle InteriorScatterStyle = InteriorScatterStyle.Auto;
        [Tooltip("Length of the render-only chips and splinters on cut faces. 1 is the pattern's natural size.")]
        [Range(0.2f, 3f)] public float InteriorScatterLength = 1f;

        [Header("Interior Surface")]
        public Material InteriorMaterial;
        [Tooltip("Cut faces reuse each fragment's dominant source-submesh material instead of a separate interior material (for example roof pieces use roof, wall pieces use wall).")]
        public bool InteriorUseSourceMaterial = true;
        [Tooltip("How UVs are generated on newly exposed cut faces. Projected Tiling uses a new planar projection; Edge Stretch inherits UVs from the exact cut boundary and stretches them through the generated face like an extrusion.")]
        public CutSurfaceUVMode CutSurfaceUVMode = CutSurfaceUVMode.FitToCut;
        [Tooltip("World-space tiling multiplier for planar cut UVs. 1 is neutral; 0.1 makes the texture features 10x larger, 10 repeats them 10x more densely.")]
        public float InteriorUVScale = 1f;

        [Header("Physics")]
        public ColliderStrategy Colliders = ColliderStrategy.ConvexMesh;
        [Tooltip("Mass = volume x density, clamped below.")]
        public float Density = 600f;
        public float MinFragmentMass = 0.05f;
        public float MaxFragmentMass = 400f;
        [Range(0f, 5f)] public float Drag = 0.05f;
        [Range(0f, 5f)] public float AngularDrag = 0.25f;
        [Tooltip("Whether activated fragment rigidbodies are affected by gravity.")]
        public bool UseGravity = true;
        [Tooltip("Collision detection used by generated fragment rigidbodies.")]
        public CollisionDetectionMode FragmentCollisionDetection =
            CollisionDetectionMode.ContinuousSpeculative;
        [Tooltip("Interpolation used by generated fragment rigidbodies.")]
        public RigidbodyInterpolation FragmentInterpolation = RigidbodyInterpolation.None;

        [Header("Structural Body")]
        [Tooltip("Static stays fixed. Dynamic simulates immediately. Release When Unsupported stays kinematic until its supports are lost.")]
        public IntactBodyMode IntactBody = IntactBodyMode.ReleaseWhenUnsupported;
        [Tooltip("Scales the intact Rigidbody mass calculated from fractured volume x density.")]
        [Range(0.05f, 10f)] public float IntactMassMultiplier = 1f;

        [Header("Structural Supports")]
        [Tooltip("Automatic probes below the object, a manual list on the generated component, or both.")]
        public SupportDetectionMode SupportDetection = SupportDetectionMode.AutomaticBelow;
        [Tooltip("Fraction of support that must remain. World-contact probes from every wall/post form one shared foundation threshold that can collapse the whole construction. Carried floors, roofs and local height sections keep their own group-scoped threshold.")]
        [Range(0.05f, 1f)] public float RequiredSupportRatio = 0.35f;
        [Tooltip("Maximum distance below the intact collider searched for stable supports. Dynamic Rigidbody debris and bullets never count as support.")]
        [Min(0.01f)] public float SupportProbeDistance = 0.35f;
        [Tooltip("Number of automatic probe points per horizontal axis.")]
        [Range(2, 5)] public int SupportProbeGrid = 3;
        [Tooltip("Moves automatic probe points away from the collider edge. Expressed as a fraction of its width/depth.")]
        [Range(0f, 0.45f)] public float SupportProbeInset = 0.08f;
        [Tooltip("How long support may stay below the threshold before the body is released.")]
        [Range(0f, 3f)] public float SupportGraceTime = 0.15f;
        [Tooltip("How far support may travel sideways from the material that carries it, " +
                 "in metres. Reachability alone cannot tell a two metre arch from a wall " +
                 "left hanging off one surviving corner brick, because a neighbour beside " +
                 "a fragment counts exactly as much as one beneath it. The limit engages " +
                 "after a grounded element falls below Required Support Ratio, so the first " +
                 "local hit cannot drop a healthy remote section. Set this to the widest " +
                 "span a heavily damaged structure may bridge unsupported. Zero " +
                 "keeps the previous behaviour: any path back to the ground holds, " +
                 "however long and however sideways.")]
        [Min(0f)] public float MaxUnsupportedSpan = 3f;
        [Tooltip("Seconds between support checks.")]
        [Range(0.02f, 1f)] public float SupportCheckInterval = 0.1f;
        [Tooltip("Layers accepted by automatic support probes.")]
        public LayerMask SupportLayers = ~0;

        [Header("Damage")]
        [Tooltip("Choose whether this object is damaged by Rigidbody impacts, explicit gameplay calls, or both. Mixed is the recommended default.")]
        public DamageTriggerMode DamageTrigger = DamageTriggerMode.Mixed;
        [Tooltip("Total health of the intact object.")]
        [Range(1f, MaxHealth)] public float Health = DefaultHealth;
        [Tooltip("Minimum damage from one hit before it can reduce Health or break attached fragments. Uses the same damage units as Strength / Health, after collision energy and material multipliers. Weaker hits still push the object but never accumulate damage. 0 disables the threshold. Explicit BreakNow / Break Immediately and loss of structural support still force a break.")]
        [Min(0f)] public float MinimumHitDamage = 0f;

        public bool AcceptsHitDamage(float amount) =>
            MinimumHitDamage <= 0f || amount >= MinimumHitDamage;

        [Header("Impact Spread")]
        [Tooltip("Send one damage and impulse wave inside this destructible's hierarchy, or its explicitly assigned Impact Spread Parent. Only objects within Impact Spread Radius on Collision Layers are affected. Disabled by default. Neighbours cannot relay the wave; explicit explosions do not create extra waves.")]
        public bool SpreadImpactToNearbyObjects = false;
        [Tooltip("Exact maximum spread distance in world metres from the hit point to a collider surface. Force and excess damage never enlarge it. Default 3 m; 0 disables spread. The direct hit retains its separate local radius.")]
        [Range(0f, 10f)] public float ImpactSpreadRadius = 3f;
        [Tooltip("Percentage of the incoming hit's Health damage carried by the wave before the selected distance falloff. Default 50%. A 2x Health hit supplies 1x Health at full influence; linear falloff reduces this farther away. 0% causes no collateral fracture; excess damage never enlarges the radius.")]
        [Range(0f, 200f)] public float ImpactSpreadDamagePercent = 50f;
        [Tooltip("Percentage of the incoming physical impulse carried by the wave before the selected distance falloff and recipient material response. Default 50%. 0% disables collateral push. Mass still controls motion. Independent of damage and radius.")]
        [Range(0f, 200f)] public float ImpactSpreadImpulsePercent = 50f;
        [Tooltip("Choose what decreases linearly from full configured percentage at the centre to zero at the radius: damage and impulse (default), damage only, impulse only, or neither. All modes stop at the exact radius and stay inside the chosen hierarchy.")]
        public ImpactSpreadFalloff SpreadFalloff = ImpactSpreadFalloff.DamageAndImpulse;

        // Keep the existing API signature for integrations. Impulse no longer scales reach.
        public float ResolveImpactSpreadRadius(float impulse)
        {
            if (!SpreadImpactToNearbyObjects || float.IsNaN(ImpactSpreadRadius)) return 0f;
            return Mathf.Clamp(ImpactSpreadRadius, 0f, 10f);
        }

        [Header("Collision Damage")]
        [Tooltip("Collision fragility. Higher values multiply impact damage; fracture size is still controlled by Max Parts.")]
        [Range(0f, 1f)] public float Brittleness = 0.2f;
        [Tooltip("Enable collision impulses as a damage source on the intact object.")]
        public bool CollisionDamage = true;
        [Tooltip("Allow a Dynamic or structurally released intact Rigidbody to damage itself when it strikes static world geometry. Self-fall damage is calibrated from landing speed/fall height rather than total baked mass, so a larger copy is not automatically more fragile. Projectiles and other Rigidbody impacts still use physical mass.")]
        public bool FallDamage = true;
        [Tooltip("Minimum relative velocity (m/s) before a collision deals damage.")]
        public float MinCollisionVelocity = 5.0f;
        [Tooltip("Maps the kinetic energy of an impact, in kilojoules, to Health damage. " +
                 "Energy rather than impulse, so a heavy slow mass and a light fast one are " +
                 "told apart the way physics tells them apart. For scale: a thrown crate is " +
                 "well under 1 kJ, a 100 kg ball dropped two metres is about 2 kJ, a car at " +
                 "city speed is over 100 kJ. Past the last key the curve keeps rising as a " +
                 "square root, so bigger blows stay ordered without running away.")]
        public AnimationCurve ImpulseToDamage = AnimationCurve.Linear(0f, 0f, 20f, 200f);
        [Tooltip("Seconds between accepted collision damage events (anti micro-collision hysteresis).")]
        public float DamageCooldown = 0.15f;
        [Tooltip("When Health reaches zero, activate every baked primary fragment immediately. Disable for progressive local destruction driven by the hit radius.")]
        public bool ActivateAllOnFirstBreak = false;
        [Tooltip("Radius around a post-fracture Rigidbody hit that wakes remaining baked kinematic fragments. This is the footprint of a blow that only just breaks through; damage worth several times Health widens it by the cube root of that overshoot, so a longer fall or a lower Health destroys visibly more.")]
        [Min(0.05f)] public float PostFractureImpactRadius = 0.8f;
        [Tooltip("Outward separation impulse in N s when a local hit activates a fragment. Heavier pieces gain less speed. The serialized field name is retained for existing profiles.")]
        [Range(0f, 10f)] public float LocalImpactEjectSpeed = DefaultMineralEjectSpeed;
        [Tooltip("How much of this object's Health a piece of loose debris must be worth " +
                 "before its impact counts as damage rather than a bump. Debris is not a " +
                 "weapon: a plank tumbling into a fence should clatter off it, not break it, " +
                 "unless it arrives genuinely fast. Physics still pushes whatever debris " +
                 "lands on, at any speed. Brittle materials are barely affected — a stone " +
                 "landing on glass is worth many times the pane's whole strength either way. " +
                 "Raise it if debris still breaks too much, lower it for fragile props.")]
        [Range(0f, 1f)] public float DebrisImpactSignificance = 0.35f;
        [Tooltip("Upper limit on how fast a break may launch one fragment, in m/s. Launch " +
                 "impulse divided by mass is a speed, so without a cap a light material — " +
                 "wood at a tenth of concrete's density — leaves the same break ten times " +
                 "faster and behaves like a projectile. The default leaves room for a " +
                 "powerful weapon response while the 50 m/s ceiling prevents runaway debris.")]
        [Range(1f, 50f)] public float MaxFragmentLaunchSpeed = DefaultFragmentLaunchSpeed;
        [Tooltip("Upper limit on the launch speed of the complete Dynamic Rigidbody or its connected surviving cluster. This is intentionally separate from Max Fragment Launch Speed: a heavy barrel may react strongly to a weapon without allowing tiny debris to become dangerous projectiles.")]
        [Range(1f, 50f)] public float MaxWholeBodyLaunchSpeed = DefaultWholeBodyLaunchSpeed;
        public LayerMask CollisionLayers = ~0;

        /// <summary>
        /// Spin limit for one piece of debris, in rad/s, derived from the authored launch
        /// speed and the piece's own size rather than from a separate authored number.
        ///
        /// An off-centre hit is applied with AddForceAtPosition, whose angular part goes
        /// through the inverse inertia tensor while its linear part does not. The ratio
        /// mass * radius^2 / inertia is roughly ten for a slab-shaped fragment and grows as
        /// pieces get smaller, so every hit drove the spin straight into whatever ceiling
        /// existed. Unity leaves that ceiling at the project-wide Default Max Angular Speed,
        /// which has nothing to do with how this destructible was authored; on a project
        /// that raised it, debris spun like a top.
        ///
        /// The rule here is the physical one already implied by Max Fragment Launch Speed:
        /// a fragment's surface may not travel faster than the whole fragment is allowed to
        /// travel. That is also exactly the speed that decides tunnelling, because Unity's
        /// continuous collision detection is linear only — a rim moving faster than its own
        /// thickness per physics step passes through a floor no matter which CCD mode is set.
        /// Lowering Max Fragment Launch Speed therefore calms travel and spin together.
        /// </summary>
        /// <param name="boundingRadius">Half the diagonal of the fragment's world bounds.</param>
        public float ResolveMaxAngularSpeed(float boundingRadius)
        {
            // Not a cap on the authored value: it bounds the lever arm, so debris a few
            // millimetres across cannot divide the launch speed by nearly zero.
            float radius = Mathf.Max(0.05f, boundingRadius);
            return Mathf.Max(0.01f, MaxFragmentLaunchSpeed) / radius;
        }

        /// <summary>Convenience overload taking the fragment's world bounds directly.</summary>
        public float ResolveMaxAngularSpeed(Bounds worldBounds)
        {
            return ResolveMaxAngularSpeed(worldBounds.extents.magnitude);
        }

        /// <summary>
        /// Trims the motion a piece inherits from the body it broke off, to the speed this
        /// profile allows a whole prop to travel at.
        ///
        /// Every launch the package applies goes through a cap, but inherited motion is
        /// written straight onto the new Rigidbody, and it does not come from the package at
        /// all: it is whatever Unity's own collision response left on the intact body. A prop
        /// set to the Rigidbody body type that has just been hit by something heavy can be
        /// moving at tens of metres per second, and every fragment then starts there — far
        /// past anything Max Fragment Launch Speed would have permitted, and fast enough to
        /// cross a floor between two physics steps.
        ///
        /// Max Whole Body Push Speed is the right bound because this IS whole-body motion: the
        /// piece is still travelling as part of the object it belonged to a moment ago. A prop
        /// thrown at a sane speed keeps its momentum through the break, which is what makes
        /// debris look like it came off something moving.
        /// </summary>
        public Vector3 ClampInheritedVelocity(Vector3 velocity)
        {
            float maxSpeed = Mathf.Max(0.01f, MaxWholeBodyLaunchSpeed);
            float speed = velocity.magnitude;
            return speed <= maxSpeed ? velocity : velocity * (maxSpeed / speed);
        }

        [Header("Runtime Lifecycle")]
        // public QualityTier Tier = QualityTier.Balanced;
        [Tooltip("Optional expert optimization. Disabled means destruction is never denied by an active-body limit.")]
        public bool EnforceActiveBodyBudget = false;
        [Tooltip("Cap of simultaneously simulating (dynamic) fragments. Used by Enforce Active Body Budget (denies new activations) and by Degrade Smallest Over Budget (freezes the smallest excess to visual-only).")]
        [Range(1, 250)] public int MaxActiveFragments = 40;

        [Tooltip("Reactively degrade the smallest active fragments when a budget is exceeded, instead of only denying activations. Stage 1: past Max Active Fragments the smallest simulating pieces freeze to non-interactive visual props (still visible). Stage 2: past the visible caps below the smallest visible pieces are removed with a gradual shrink.")]
        public bool DegradeSmallestOverBudget = false;
        [Tooltip("Past this many visible fragments the smallest are removed (gradual shrink). 0 disables count-based removal. Set at or above Max Active Fragments.")]
        [Range(0, 1000)] public int MaxVisibleFragments = 160;
        [Tooltip("Past this many total triangles across all visible fragments the smallest are removed (gradual shrink). 0 disables the triangle budget.")]
        [Min(0)] public int MaxVisibleTriangles = 150000;
        [Tooltip("When enabled, a fragment is forcibly processed by Cleanup Action after Cleanup Lifetime. Disabled means no hard time limit.")]
        public bool LimitActiveTime = false;
        [Tooltip("Hard active-time limit in seconds. Used only when Limit Active Time is enabled.")]
        [Min(0.1f)] public float CleanupLifetime = 8f;
        [Tooltip("When enabled, fragments that remain below Sleep Velocity for Sleep Duration are processed by Cleanup Action.")]
        public bool CleanupSettledFragments = false;
        [Tooltip("Fragments slower than this are considered candidates for settling.")]
        public float SleepVelocity = 0.08f;
        [Tooltip("Seconds a fragment must stay slow before it settles.")]
        public float SleepDuration = 1.5f;
        [Tooltip("What happens when a time limit or settle-cleanup condition is reached.")]
        public CleanupAction Cleanup = CleanupAction.FreezeAsProxy;
        [Tooltip("When Despawn is selected, shrink the settled fragment over a short fixed transition before disabling it.")]
        public bool SmoothDespawn = true;
        [Tooltip("Retire debris automatically: every broken-off piece fades out this many " +
                 "seconds after it came loose, releasing its physics, collider and draw cost. " +
                 "Uncheck to keep rubble in the level permanently. Recommended for scenes " +
                 "with many destructibles.")]
        public bool DespawnDebris = true;
        [Tooltip("Keep this profile's debris until an explicit reset or DespawnDebrisNow call. Overrides scene cleanup, profile timers and automatic budget eviction. Physics may still settle or freeze; full split budgets refuse further subdivision.")]
        public bool KeepDebris;
        [Tooltip("Seconds a loose piece stays before it fades out. Timed per piece, so " +
                 "late-broken fragments still get their full time on screen.")]
        [Min(1f)] public float DespawnDebrisAfter = 60f;

        [Header("Budget Group")]
        [Tooltip("When the optional active-body budget is enabled, destructibles sharing this name share its cap.")]
        public string BudgetGroup = "Default";
        [Tooltip("Optional global cap for this group. Used only when Enforce Active Body Budget is enabled.")]
        [Range(4, 512)] public int GroupMaxActiveBodies = 96;

        [Header("FX Hooks")]
        [Tooltip("Free-form material tag passed to FX/audio adapters (e.g. concrete, glass, wood).")]
        public string MaterialTag = "concrete";

        [Header("Explosion Defaults")]
        public float DefaultExplosionForce = 30f;
        public float DefaultExplosionRadius = 3f;
        [Tooltip("How strongly fragments are kicked outward when the object breaks. 0 adds no outward kick, 1 is the physical default, and values above 1 create a stronger cinematic launch.")]
        [Range(0f, 5f)] public float FragmentPushMultiplier = DefaultMineralPushMultiplier;
        [Tooltip("Multiplies the hit impulse along the incoming projectile, ray or Rigidbody travel direction. Impulse is divided by the receiving mass, so heavier bodies move less. Max Fragment Launch Speed remains the safety cap.")]
        [Range(0f, 5f)] public float DirectionalImpactMultiplier = 0f;
        [Tooltip("Angular and sideways impulse for tall fragments. Mass and inertia determine the resulting motion. The serialized field name is retained for existing profiles.")]
        [Range(0f, 5f)] public float TallFragmentToppleSpeed = 1.25f;
        [Tooltip("How far the blast lifts material, as a share of the explosion radius. 0 pushes every piece straight away from the origin, so debris level with the blast leaves sideways; 0.25 throws the same debris up and out. Negative values drive it into the ground.")]
        public float ExplosionUpwardModifier = 0.25f;
        // Retained for serialized/API compatibility. All launch responses now use impulses.
        [HideInInspector] public ForceMode ExplosionForceMode = ForceMode.Impulse;

        void OnEnable()
        {
            UpgradeSchema();
            ClampAuthoringLimits();
        }

        void OnValidate()
        {
            UpgradeSchema();
            ClampAuthoringLimits();
        }

        void ClampAuthoringLimits()
        {
            Health = Mathf.Clamp(Health, 1f, MaxHealth);
            MinimumHitDamage = Mathf.Max(0f, MinimumHitDamage);
            ImpactSpreadRadius = Mathf.Clamp(ImpactSpreadRadius, 0f, 10f);
            if ((int)SpreadFalloff < 0 || (int)SpreadFalloff > 3)
                SpreadFalloff = ImpactSpreadFalloff.DamageAndImpulse;
            ImpactSpreadDamagePercent = Mathf.Clamp(ImpactSpreadDamagePercent, 0f, 200f);
            ImpactSpreadImpulsePercent = Mathf.Clamp(ImpactSpreadImpulsePercent, 0f, 200f);
            MaxFragmentLaunchSpeed = Mathf.Clamp(MaxFragmentLaunchSpeed, 1f, 50f);
            MaxWholeBodyLaunchSpeed = Mathf.Clamp(MaxWholeBodyLaunchSpeed, 1f, 50f);
        }

        void UpgradeSchema()
        {
            if (SchemaVersion >= CurrentSchemaVersion) return;

            if (SchemaVersion < 2)
            {
                if (IntactMassMultiplier <= 0f) IntactMassMultiplier = 1f;
                if (RequiredSupportRatio <= 0f) RequiredSupportRatio = 0.35f;
                if (SupportProbeDistance <= 0f) SupportProbeDistance = 0.35f;
                if (SupportProbeGrid < 2) SupportProbeGrid = 3;
                if (SupportProbeInset <= 0f) SupportProbeInset = 0.08f;
                if (SupportCheckInterval <= 0f) SupportCheckInterval = 0.1f;
                if (Brittleness <= 0f) Brittleness = 0.3f;
            }
            if (SchemaVersion < 3)
            {
                if (SecondaryEligibleFragments <= 0) SecondaryEligibleFragments = 8;
                if (SecondaryPiecesPerFragment < 2) SecondaryPiecesPerFragment = 4;
                if (SecondaryMinParentVolumeRatio <= 0f)
                    SecondaryMinParentVolumeRatio = 0.8f;
                if (SecondaryIrregularity <= 0f) SecondaryIrregularity = 0.4f;
                if (SecondaryStrength <= 0f) SecondaryStrength = 35f;
                if (SecondaryMinImpactVelocity <= 0f)
                    SecondaryMinImpactVelocity = 4.5f;
                if (SecondaryImpulseToDamage == null ||
                    SecondaryImpulseToDamage.length == 0)
                    SecondaryImpulseToDamage = AnimationCurve.Linear(0f, 0f, 25f, 50f);
                if (SecondaryDamageCooldown <= 0f) SecondaryDamageCooldown = 0.2f;
                if (SecondarySeparationForce <= 0f) SecondarySeparationForce = 3f;
                if (MaxActiveSecondaryBodies <= 0) MaxActiveSecondaryBodies = 24;
                SecondaryInheritVelocity = true;
            }
            if (SchemaVersion < 4)
            {
                if (BrickCellSize.x <= 0f || BrickCellSize.y <= 0f || BrickCellSize.z <= 0f)
                    BrickCellSize = new Vector3(0.5f, 0.25f, 0.25f);
                BrickUseCellSize = Pattern == FracturePattern.BrickCells;
            }
            if (SchemaVersion < 5)
            {
                if (PostFractureImpactRadius <= 0f)
                    PostFractureImpactRadius = 0.8f;
            }
            if (SchemaVersion < 6)
            {
                ReleaseUnsupportedGlass = true;
                if (Pattern == FracturePattern.RadialGlass)
                    MaxActiveFragments = Mathf.Max(MaxActiveFragments, FragmentCount);
            }
            if (SchemaVersion < 7)
            {
                // Older profiles used hard physics caps by default. They could leave a
                // partially fractured shell permanently immune after the shared group
                // filled up. Upgrade to complete destruction; an expert may explicitly
                // re-enable budgeting afterwards.
                EnforceActiveBodyBudget = false;
            }
            if (SchemaVersion < 8)
            {
                // The serialized default for a newly added float is zero on existing
                // assets; preserve their previous 1:1 launch response.
                FragmentPushMultiplier = 1f;
            }
            if (SchemaVersion < 9)
            {
                // Fields introduced with the Slabs / Pillars / Impact patterns.
                if (SlabLayers < 2) SlabLayers = 6;
                if (SlabJitter <= 0f) SlabJitter = 0.25f;
                if (ColumnJitter <= 0f) ColumnJitter = 0.35f;
                if (ImpactFocus <= 0f) ImpactFocus = 0.65f;
            }
            if (SchemaVersion < 10)
            {
                // Existing profiles should get the friendlier Despawn behaviour instead
                // of abruptly popping pooled pieces out of view.
                SmoothDespawn = true;
            }
            if (SchemaVersion < 11)
            {
                // Newly introduced wood controls were zero on serialized older assets.
                // Preserve the visual character of the wood preset after upgrade.
                if (WoodSplitIrregularity <= 0f) WoodSplitIrregularity = 0.35f;
                if (WoodEndSharpness <= 0f) WoodEndSharpness = 0.6f;
            }
            if (SchemaVersion < 12)
            {
                // Earlier profiles only stored the collision toggle. Their public damage
                // API was always available, so enabled collision damage maps naturally to
                // the new beginner-friendly Mixed default.
                DamageTrigger = CollisionDamage
                    ? DamageTriggerMode.Mixed
                    : DamageTriggerMode.ScriptedOnly;
            }
            if (SchemaVersion < 13)
            {
                // The serialized default is Auto, which preserves the previous
                // longest-axis behaviour for existing pillar profiles.
                PillarAxis = AxisMode.Auto;
            }
            if (SchemaVersion < 14)
            {
                // All added orientation controls default to Auto, preserving the
                // previous longest-axis / thinnest-axis behaviour.
                GlassNormalAxis = AxisMode.Auto;
                WoodAxis = AxisMode.Auto;
                SlabNormalAxis = AxisMode.Auto;
            }
            if (SchemaVersion < 15)
            {
                // Existing bakes used planar cap UVs, so preserve that exact appearance.
                CutSurfaceUVMode = CutSurfaceUVMode.PlanarProjection;
            }
            if (SchemaVersion < 16)
            {
                // Complete destruction is the beginner-friendly contract. Progressive
                // local activation remains available as an explicit advanced choice.
                ActivateAllOnFirstBreak = true;
            }
            if (SchemaVersion < 17)
            {
                // Surface-based activation and guaranteed separation now make local
                // destruction reliable, so impacts can return to the more useful
                // progressive default. Full activation remains an explicit option.
                ActivateAllOnFirstBreak = false;
            }
            if (SchemaVersion < 18)
            {
                LocalImpactEjectSpeed = 2.5f;
            }
            if (SchemaVersion < 19)
            {
                // Existing sources keep the old full-solid interpretation until the
                // author explicitly opts into generated shell thickness.
                Solidify = false;
                SolidifyThickness = 0.08f;
            }
            if (SchemaVersion < 20)
            {
                TallFragmentToppleSpeed = 1.25f;
            }
            if (SchemaVersion < 21 && (int)Colliders == 2)
            {
                // The retired collider mode used serialized value 2. Existing profiles
                // using that value become normal physical convex fragments.
                Colliders = ColliderStrategy.ConvexMesh;
            }
            if (SchemaVersion < 23)
            {
                // Existing non-wood profiles retain their exact fracture output. Existing
                // wood profiles opt into the generalized directional solver at full strength,
                // which reproduces their previous lengthwise behaviour.
                DirectionalCutStrength = Pattern == FracturePattern.DirectionalWood ? 1f : 0f;
            }
            if (SchemaVersion < 24)
            {
                // Dynamic slicing is intentionally opt-in for existing and new profiles.
                RuntimeFragmentSplitting = false;
                RuntimeSplitPieces = 2;
                RuntimeSplitMaxDepth = 2;
                RuntimeSplitStrength = 25f;
                RuntimeSplitMinVolume = 0.002f;
                RuntimeSplitMaxActivePieces = 64;
                RuntimeSplitSeparationForce = 1.5f;
                RuntimeSplitPiecesDisappear = false;
                RuntimeSplitDisappearAfter = 8f;
            }
            if (SchemaVersion < 25)
            {
                // Existing profiles gain the same ready-to-use effects that new profiles
                // receive. Object references remain optional overrides.
                UseDefaultHitParticles = true;
                UseDefaultHitSound = true;
                PlayHitEffectsOnEveryHit = true;
                HitParticlePrefab = null;
                HitSound = null;
            }
            if (SchemaVersion < 26)
            {
                // Quick Destruction now exposes one reliable support workflow. Legacy
                // manual and hybrid profiles migrate to the automatic downward probes.
                SupportDetection = SupportDetectionMode.AutomaticBelow;
            }
            if (SchemaVersion < 28)
            {
                // Runtime slicing used to admit any mesh and only trim budgets after
                // allocating child meshes. Give existing assets safe bounded defaults;
                // the runtime feature itself remains opt-in.
                RuntimeSplitMaxSourceTriangles = 5000;
                RuntimeSplitMaxQueuedRequests = 16;
                RuntimeSplitCollisionCooldown = 0.2f;
                RuntimeSplitConvexColliderDepth = 1;
                RuntimeSplitMaxActivePieces = Mathf.Min(RuntimeSplitMaxActivePieces, 32);
                MaxSplitsPerFrame = Mathf.Min(MaxSplitsPerFrame, 1);
            }
            if (SchemaVersion < 29)
            {
                // Previous builds used any fragment collision for dust. Existing profiles
                // now receive a conservative fall threshold without changing their asset
                // reference or hit-particle setup.
                FragmentImpactMinDropHeight = 0.75f;
            }
            if (SchemaVersion < 30)
            {
                // Keep old profiles within the stricter runtime re-fracture limits.
                // This avoids a serialized value above the editor range bypassing the
                // performance contract at runtime.
                RuntimeSplitPieces = Mathf.Clamp(RuntimeSplitPieces, 2, 4);
                RuntimeSplitMaxDepth = Mathf.Clamp(RuntimeSplitMaxDepth, 1, 4);
                RuntimeSplitMaxActivePieces = Mathf.Clamp(RuntimeSplitMaxActivePieces, 2, 64);
            }
            if (SchemaVersion < 31)
            {
                // Dynamic slicing graduates from opt-in to the default behaviour, on the
                // same bounded budget it already shipped with (two pieces, two
                // generations), so existing assets gain it without a manual visit.
                RuntimeFragmentSplitting = true;
                RuntimeSplitPieces = Mathf.Clamp(RuntimeSplitPieces, 2, 4);
                RuntimeSplitMaxDepth = Mathf.Clamp(RuntimeSplitMaxDepth, 1, 4);

                // Secondary impact damage is now driven by impact SPEED instead of raw
                // impulse. The units changed, so a curve authored against impulse would be
                // sampled far past its last key and return maximum damage on every touch —
                // which is why pre-baked fragments all burst on the lightest contact. The
                // shipped default is restored rather than rescaled, because no rescaling of
                // an impulse curve is meaningful once the input is a velocity.
                SecondaryImpulseToDamage = AnimationCurve.Linear(0f, 0f, 25f, 50f);
                SecondaryImpactGrace = 0.6f;
            }
            if (SchemaVersion < 32)
            {
                // Debris auto-despawn ships enabled: loose pieces fade out a minute after
                // they broke off, so a scene full of destructibles does not accumulate
                // rubble cost forever. Existing profiles adopt the same default as new
                // ones; unchecking Despawn Debris restores permanent rubble.
                DespawnDebris = true;
                DespawnDebrisAfter = 60f;
            }
            if (SchemaVersion < 33)
            {
                // Collision damage is now driven by the impact's kinetic energy in
                // kilojoules instead of raw impulse. The units changed, so a curve authored
                // against impulse would be read at the wrong scale entirely — every ordinary
                // collision landed as maximum damage before, which is why one wrecking ball
                // could level a building. No rescaling of an impulse curve is meaningful
                // once the input is an energy, so the shipped default is restored.
                // Calibrated per material: the energy a pane of glass needs is orders of
                // magnitude below what masonry needs, and that difference IS its brittleness.
                ImpulseToDamage = DefaultImpactDamageCurve(Pattern);

                // New field: a deserialized old profile reads it as zero, which would stop
                // fragments dead instead of merely keeping them sane.
                MaxFragmentLaunchSpeed = 7f;
            }
            if (SchemaVersion < 34)
            {
                // A profile that already migrated to 33 received the generic, concrete
                // calibrated curve. Re-derive it per material so glass is brittle again.
                ImpulseToDamage = DefaultImpactDamageCurve(Pattern);
                if (MaxFragmentLaunchSpeed <= 0f) MaxFragmentLaunchSpeed = 7f;
            }
            if (SchemaVersion < 35)
            {
                // New field: zero on a deserialized profile would let any debris contact
                // count as an attack, which is the behaviour this floor exists to stop.
                DebrisImpactSignificance = 0.35f;
            }
            if (SchemaVersion < 36 && UsesMineralLaunchDefaults(Pattern))
            {
                // Heavy stone/concrete pieces looked glued into the original shell with
                // the old universal defaults. Upgrade only values that still equal those
                // defaults, preserving explicitly authored launch settings.
                if (Mathf.Approximately(LocalImpactEjectSpeed, DefaultLightEjectSpeed))
                    LocalImpactEjectSpeed = DefaultMineralEjectSpeed;
                if (Mathf.Approximately(FragmentPushMultiplier, DefaultLightPushMultiplier))
                    FragmentPushMultiplier = DefaultMineralPushMultiplier;
            }
            if (SchemaVersion < 37)
            {
                // Concrete and impact-shattered stone previously shipped above the
                // Strength slider's 1000 maximum. Upgrade only the exact old preset
                // values so explicitly authored stronger structures remain untouched.
                if (Pattern == FracturePattern.UniformConcrete &&
                    Mathf.Approximately(Health, 4000f))
                    Health = 1000f;
                else if (Pattern == FracturePattern.ImpactShatter &&
                         Mathf.Approximately(Health, 3200f))
                    Health = 1000f;
            }
            if (SchemaVersion < 38 && IsPipelineParticleTemplate(HitParticlePrefab))
            {
                // The effects UI previously wrote the generic pipeline particle template
                // into this override. That made every material bypass its procedural
                // preset configuration, so Wood played the same dusty burst as masonry.
                HitParticlePrefab = null;
            }
            if (SchemaVersion < 39)
            {
                // Ground impacts were already part of collision damage before this
                // control existed, so existing profiles keep that behaviour by default.
                FallDamage = true;
            }
            if (SchemaVersion < 40)
            {
                // The direction is optional in DamageContext/BreakContext, so legacy
                // gameplay calls retain their exact radial response. Sources which can
                // identify travel direction gain a conservative material-aware default.
                DirectionalImpactMultiplier =
                    V40DirectionalImpactMultiplier(Pattern);
            }
            if (SchemaVersion < 41)
            {
                // v40 expressed directional launch as a mass-dependent impulse. v41
                // expresses it as a capped speed change so dense stone fragments can
                // visibly follow a weapon without making light wood excessive. Upgrade
                // only untouched v40 defaults; explicitly authored values stay intact.
                float legacyDefault = V40DirectionalImpactMultiplier(Pattern);
                if (Mathf.Approximately(DirectionalImpactMultiplier, legacyDefault))
                    DirectionalImpactMultiplier =
                        V41DirectionalImpactMultiplier(Pattern);
            }
            if (SchemaVersion < 42)
            {
                // Directional weapon launch is opt-in from v42 onward. This preserves
                // the established radial preset response until the developer raises the
                // slider, while the 5x end of the range still reaches the speed cap.
                float previousDefault = V41DirectionalImpactMultiplier(Pattern);
                if (Mathf.Approximately(DirectionalImpactMultiplier, previousDefault))
                    DirectionalImpactMultiplier =
                        DefaultDirectionalImpactMultiplier(Pattern);
            }
            if (SchemaVersion < 43)
            {
                // Seven metres per second was the old universal debris cap. It made
                // medium and powerful hits saturate almost immediately and therefore
                // look identical. Upgrade only that exact untouched default; custom
                // authored limits remain unchanged. MaxWholeBodyLaunchSpeed was added
                // just before this schema and deserializes as zero in older assets.
                if (Mathf.Approximately(MaxFragmentLaunchSpeed, 7f))
                    MaxFragmentLaunchSpeed = DefaultFragmentLaunchSpeed;
                if (MaxWholeBodyLaunchSpeed <= 0f)
                    MaxWholeBodyLaunchSpeed = DefaultWholeBodyLaunchSpeed;
            }
            if (SchemaVersion < 44)
            {
                // Uniform Concrete previously used this exact value as a hard-coded
                // solver constant. Migrate every existing profile to the old result so
                // exposing the control does not silently change already-authored bakes.
                FragmentShapeIrregularity = 0.3f;
            }

            SchemaVersion = CurrentSchemaVersion;
        }

        /// <summary>
        /// True for one of this package's own generic pipeline particle templates. They are
        /// renderer templates rather than authored art, and which one a profile happens to
        /// hold records nothing but the render pipeline it was authored under — so honouring
        /// it after the project moves to another pipeline draws the burst with a magenta
        /// material and skips the material-aware preset configuration entirely.
        /// Built-In belongs here as much as the other two; leaving it out meant a profile
        /// authored without a scriptable pipeline kept its template through the migration.
        /// </summary>
        public static bool IsPipelineParticleTemplate(ParticleSystem particle)
        {
            if (particle == null || particle.gameObject == null) return false;
            string objectName = particle.gameObject.name;
            return objectName == "Particle_URP" || objectName == "Particle_HDRP" ||
                   objectName == "Particle_Built-In";
        }

        /*
        /// <summary>Applies the quality tier contract: fragments, colliders, activation and cleanup together.</summary>
        public void ApplyQualityTier(QualityTier tier)
        {
            Tier = tier;
            // Quality tiers tune geometry, collider cost and cleanup, but never make an
            // object only partially destructible. Hard body caps are an explicit expert
            // override, independent from the selected tier.
            EnforceActiveBodyBudget = false;
            switch (tier)
            {
                case QualityTier.Mobile:
                    FragmentCount = Mathf.Clamp(FragmentCount, 8, 24);
                    Colliders = ColliderStrategy.PrimitiveBox;
                    MaxActiveFragments = 12;
                    CleanupLifetime = 3.5f;
                    Cleanup = CleanupAction.Recycle;
                    LimitActiveTime = true;
                    CleanupSettledFragments = true;
                    GroupMaxActiveBodies = 32;
                    SecondaryEligibleFragments =
                        Mathf.Clamp(SecondaryEligibleFragments, 1, 4);
                    SecondaryPiecesPerFragment =
                        Mathf.Clamp(SecondaryPiecesPerFragment, 2, 3);
                    MaxActiveSecondaryBodies =
                        Mathf.Clamp(MaxActiveSecondaryBodies, 1, 12);
                    break;
                case QualityTier.Balanced:
                    FragmentCount = Mathf.Clamp(FragmentCount, 24, 80);
                    Colliders = ColliderStrategy.ConvexMesh;
                    MaxActiveFragments = 40;
                    CleanupLifetime = 8f;
                    Cleanup = CleanupAction.FreezeAsProxy;
                    LimitActiveTime = false;
                    CleanupSettledFragments = false;
                    GroupMaxActiveBodies = 96;
                    SecondaryEligibleFragments =
                        Mathf.Clamp(SecondaryEligibleFragments, 1, 10);
                    SecondaryPiecesPerFragment =
                        Mathf.Clamp(SecondaryPiecesPerFragment, 2, 5);
                    MaxActiveSecondaryBodies =
                        Mathf.Clamp(MaxActiveSecondaryBodies, 1, 32);
                    break;
                case QualityTier.High:
                    FragmentCount = Mathf.Clamp(FragmentCount, 80, 250);
                    Colliders = ColliderStrategy.ConvexMesh;
                    MaxActiveFragments = 110;
                    CleanupLifetime = 14f;
                    Cleanup = CleanupAction.FreezeAsProxy;
                    LimitActiveTime = false;
                    CleanupSettledFragments = false;
                    GroupMaxActiveBodies = 220;
                    SecondaryEligibleFragments =
                        Mathf.Clamp(SecondaryEligibleFragments, 1, 24);
                    SecondaryPiecesPerFragment =
                        Mathf.Clamp(SecondaryPiecesPerFragment, 2, 8);
                    MaxActiveSecondaryBodies =
                        Mathf.Clamp(MaxActiveSecondaryBodies, 1, 96);
                    break;
            }
        }
        */

        /// <summary>Applies a bounded second-level breakup cost/quality preset.</summary>
        public void ApplySecondaryPreset(SecondaryBreakupLevel level)
        {
            SecondaryBreakup = level;
            switch (level)
            {
                case SecondaryBreakupLevel.Low:
                    SecondaryEligibleFragments = Mathf.Clamp(Mathf.RoundToInt(FragmentCount * 0.30f), 2, 64);
                    SecondaryPiecesPerFragment = 3;
                    SecondaryMinParentVolumeRatio = 0.4f;
                    SecondaryIrregularity = 0.3f;
                    SecondaryStrength = 45f;
                    SecondaryMinImpactVelocity = 6f;
                    SecondarySeparationForce = 2f;
                    MaxActiveSecondaryBodies = 16;
                    break;
                case SecondaryBreakupLevel.Medium:
                    SecondaryEligibleFragments = Mathf.Clamp(Mathf.RoundToInt(FragmentCount * 0.50f), 4, 128);
                    SecondaryPiecesPerFragment = 4;
                    SecondaryMinParentVolumeRatio = 0.2f;
                    SecondaryIrregularity = 0.4f;
                    SecondaryStrength = 35f;
                    SecondaryMinImpactVelocity = 4.5f;
                    SecondarySeparationForce = 3f;
                    MaxActiveSecondaryBodies = 32;
                    break;
                case SecondaryBreakupLevel.High:
                    SecondaryEligibleFragments = Mathf.Clamp(Mathf.RoundToInt(FragmentCount * 0.75f), 6, 200);
                    SecondaryPiecesPerFragment = 5;
                    SecondaryMinParentVolumeRatio = 0.05f;
                    SecondaryIrregularity = 0.55f;
                    SecondaryStrength = 25f;
                    SecondaryMinImpactVelocity = 3.5f;
                    SecondarySeparationForce = 4f;
                    MaxActiveSecondaryBodies = 64;
                    break;
            }
        }

        /// <summary>Configures this profile as one of the four built-in behaviour presets.</summary>
        /// <summary>
        /// Impact energy, in kilojoules, that a material needs before a collision does full
        /// damage. This is where brittleness physically lives: a pane of glass gives way to
        /// a fraction of a kilojoule — a thrown stone, a dropped tool — while masonry needs
        /// tens of them. One shared curve calibrated for concrete made glass effectively
        /// tougher than brick, because the same falling chunk landed at a tenth of a percent
        /// of the threshold. Brittleness still multiplies on top; it scales the response,
        /// this sets the scale it is measured against.
        /// </summary>
        public static AnimationCurve DefaultImpactDamageCurve(FracturePattern pattern)
        {
            float fullDamageKilojoules;
            switch (pattern)
            {
                case FracturePattern.RadialGlass: fullDamageKilojoules = 0.4f; break;
                case FracturePattern.DirectionalWood: fullDamageKilojoules = 5f; break;
                case FracturePattern.BrickCells: fullDamageKilojoules = 12f; break;
                default: fullDamageKilojoules = 20f; break;
            }
            return AnimationCurve.Linear(0f, 0f, fullDamageKilojoules, 200f);
        }

        static bool UsesMineralLaunchDefaults(FracturePattern pattern) =>
            pattern != FracturePattern.RadialGlass &&
            pattern != FracturePattern.DirectionalWood;

        public static float DefaultDirectionalImpactMultiplier(
            FracturePattern pattern)
        {
            return 0f;
        }

        static float V41DirectionalImpactMultiplier(
            FracturePattern pattern)
        {
            switch (pattern)
            {
                case FracturePattern.DirectionalWood:
                    return 1f;
                case FracturePattern.RadialGlass: return 0.8f;
                case FracturePattern.ImpactShatter: return 0.65f;
                default:
                    return 0.4f;
            }
        }

        static float V40DirectionalImpactMultiplier(
            FracturePattern pattern)
        {
            switch (pattern)
            {
                case FracturePattern.DirectionalWood:
                case FracturePattern.ImpactShatter:
                    return 1f;
                case FracturePattern.RadialGlass:
                    return 0.8f;
                default:
                    return 0.65f;
            }
        }

        public void ApplyPreset(FracturePattern preset)
        {
            Pattern = preset;
            ImpulseToDamage = DefaultImpactDamageCurve(preset);
            RuntimeFragmentSplitting = true;
            RuntimeSplitMaxDepth = 2;
            // Presets default to persistent, wakeable debris. Time- or rest-based cleanup
            // is an explicit optimization chosen in Lifecycle & Pooling.
            LimitActiveTime = false;
            CleanupSettledFragments = false;
            CollisionDamage = true;
            FallDamage = true;
            DamageTrigger = DamageTriggerMode.Mixed;
            MinimumHitDamage = 0f;
            SpreadImpactToNearbyObjects = false;
            ImpactSpreadRadius = 3f;
            ImpactSpreadDamagePercent = 50f;
            ImpactSpreadImpulsePercent = 50f;
            SpreadFalloff = ImpactSpreadFalloff.DamageAndImpulse;
            ActivateAllOnFirstBreak = false;
            PostFractureImpactRadius = 0.8f;
            bool mineralLaunch = UsesMineralLaunchDefaults(preset);
            LocalImpactEjectSpeed = mineralLaunch
                ? DefaultMineralEjectSpeed
                : DefaultLightEjectSpeed;
            ReleaseUnsupportedGlass = true;
            EnforceActiveBodyBudget = false;
            UseGravity = true;
            FragmentCollisionDetection = CollisionDetectionMode.ContinuousSpeculative;
            FragmentInterpolation = RigidbodyInterpolation.None;
            ExplosionForceMode = ForceMode.Impulse;
            FragmentPushMultiplier = mineralLaunch
                ? DefaultMineralPushMultiplier
                : DefaultLightPushMultiplier;
            MaxFragmentLaunchSpeed = DefaultFragmentLaunchSpeed;
            MaxWholeBodyLaunchSpeed = DefaultWholeBodyLaunchSpeed;
            DirectionalImpactMultiplier =
                DefaultDirectionalImpactMultiplier(preset);
            TallFragmentToppleSpeed = 1.25f;
            // Structural objects should remain fixed only while they are supported.
            // This is the safe default for buildings, bridges, decks and columns;
            // users can still explicitly choose Static or Dynamic in Structure.
            IntactBody = IntactBodyMode.ReleaseWhenUnsupported;
            IntactMassMultiplier = 1f;
            MaxUnsupportedSpan = 3f;
            SecondaryBreakup = SecondaryBreakupLevel.Off;
            // Edge Stretch is the default for new profiles and freshly applied presets.
            // Profiles are otherwise not migrated over a saved choice.
            CutSurfaceUVMode = CutSurfaceUVMode.FitToCut;
            DirectionalCutStrength = 0f;
            PreferredCutAxis = AxisMode.Auto;
            LengthwiseSplitBias = 0.5f;
            FragmentShapeIrregularity = 0.3f;
            ApplyLandingDustPresetDefaults(preset);
            switch (preset)
            {
                case FracturePattern.RadialGlass:
                    RuntimeFragmentSplitting = false;
                    SecondaryBreakup = SecondaryBreakupLevel.Off;
                    GlassNormalAxis = AxisMode.Auto;
                    FragmentCount = 12;
                    // A pane shatters as one event, so the first hit activates every
                    // non-anchored shard at once instead of a local patch. Keeping the
                    // active cap at the fragment count means all of them can wake in a
                    // single step, and thin flat shards cook as cheap box colliders, so
                    // "all at once" stays affordable. Boundary shards still hold in the
                    // frame through their anchors, which the radial pattern bakes.
                    ActivateAllOnFirstBreak = true;
                    Colliders = ColliderStrategy.PrimitiveBox;
                    MaxActiveFragments = 16;
                    ConcreteIrregularity = 0.3f;
                    Density = 180f;
                    Drag = 0.02f;
                    AngularDrag = 0.1f;
                    UseGravity = true;
                    Health = 3f;
                    Brittleness = 0.95f;
                    MinCollisionVelocity = 0.8f;
                    CleanupLifetime = 5f;
                    Cleanup = CleanupAction.Recycle;
                    DefaultExplosionForce = 18f;
                    DefaultExplosionRadius = 2.5f;
                    ExplosionUpwardModifier = 0.1f;
                    MaterialTag = "glass";
                    break;
                case FracturePattern.DirectionalWood:
                    FragmentCount = 80;
                    MaxActiveFragments = 64;
                    ConcreteIrregularity = 0.3f;
                    // 0.7 keeps a clear grain direction without forcing every cut perfectly
                    // parallel, which looked too regular/comb-like at full strength.
                    DirectionalCutStrength = 0.7f;
                    PreferredCutAxis = AxisMode.Auto;
                    LengthwiseSplitBias = 0.8f;
                    WoodSplitIrregularity = 0.35f;
                    WoodEndSharpness = 0.6f;
                    Density = 200f;
                    Drag = 0.1f;
                    AngularDrag = 0.6f;
                    UseGravity = true;
                    Health = 100f;
                    Brittleness = 0.15f;
                    MinCollisionVelocity = 4.5f;
                    CleanupLifetime = 9f;
                    Cleanup = CleanupAction.FreezeAsProxy;
                    DefaultExplosionForce = 24f;
                    DefaultExplosionRadius = 2.5f;
                    ExplosionUpwardModifier = 0.15f;
                    MaterialTag = "wood";
                    break;
                case FracturePattern.UniformConcrete:
                    FragmentCount = 55;
                    MaxActiveFragments = 72;
                    ConcreteIrregularity = 0.3f;
                    DirectionalCutStrength = 0f;
                    PreferredCutAxis = AxisMode.Auto;
                    Density = 1400f;
                    Drag = 0.05f;
                    AngularDrag = 0.3f;
                    UseGravity = true;
                    Health = DefaultHealth;
                    Brittleness = 0.15f;
                    MinCollisionVelocity = 7.0f;
                    CleanupLifetime = 8f;
                    Cleanup = CleanupAction.FreezeAsProxy;
                    DefaultExplosionForce = 35f;
                    DefaultExplosionRadius = 3f;
                    ExplosionUpwardModifier = 0.25f;
                    MaterialTag = "concrete";
                    break;
                case FracturePattern.BrickCells:
                    FragmentCount = 55;
                    MaxActiveFragments = 64;
                    BrickUseCellSize = true;
                    BrickCellSize = new Vector3(0.5f, 0.25f, 0.25f);
                    BrickJitter = 0.35f;
                    Density = 1100f;
                    Drag = 0.05f;
                    AngularDrag = 0.4f;
                    UseGravity = true;
                    Health = 200f;
                    Brittleness = 0.20f;
                    MinCollisionVelocity = 6.0f;
                    CleanupLifetime = 8f;
                    Cleanup = CleanupAction.FreezeAsProxy;
                    DefaultExplosionForce = 30f;
                    DefaultExplosionRadius = 3f;
                    ExplosionUpwardModifier = 0.2f;
                    MaterialTag = "brick";
                    break;
                case FracturePattern.LayeredSlabs:
                    FragmentCount = 55;
                    MaxActiveFragments = 64;
                    SlabLayers = 6;
                    SlabNormalAxis = AxisMode.Auto;
                    SlabJitter = 0.25f;
                    Density = 1600f;
                    Drag = 0.06f;
                    AngularDrag = 0.45f;
                    UseGravity = true;
                    Health = 225f;
                    Brittleness = 0.18f;
                    MinCollisionVelocity = 6.0f;
                    CleanupLifetime = 8f;
                    Cleanup = CleanupAction.FreezeAsProxy;
                    DefaultExplosionForce = 30f;
                    DefaultExplosionRadius = 3f;
                    ExplosionUpwardModifier = 0.2f;
                    MaterialTag = "stone";
                    break;
                case FracturePattern.ColumnarPillars:
                    FragmentCount = 55;
                    MaxActiveFragments = 64;
                    PillarAxis = AxisMode.Auto;
                    ColumnJitter = 0.35f;
                    Density = 1800f;
                    Drag = 0.05f;
                    AngularDrag = 0.5f;
                    UseGravity = true;
                    Health = 250f;
                    Brittleness = 0.12f;
                    MinCollisionVelocity = 8.0f;
                    CleanupLifetime = 10f;
                    Cleanup = CleanupAction.FreezeAsProxy;
                    DefaultExplosionForce = 32f;
                    DefaultExplosionRadius = 3f;
                    ExplosionUpwardModifier = 0.15f;
                    MaterialTag = "stone";
                    break;
                case FracturePattern.ImpactShatter:
                    FragmentCount = 55;
                    MaxActiveFragments = 72;
                    MaxActiveFragments = 72;
                    ImpactFocus = 0.65f;
                    ConcreteIrregularity = 0.4f;
                    Density = 1300f;
                    Drag = 0.05f;
                    AngularDrag = 0.3f;
                    UseGravity = true;
                    Health = DefaultHealth;
                    Brittleness = 0.20f;
                    MinCollisionVelocity = 6.0f;
                    CleanupLifetime = 8f;
                    Cleanup = CleanupAction.FreezeAsProxy;
                    DefaultExplosionForce = 35f;
                    DefaultExplosionRadius = 3f;
                    ExplosionUpwardModifier = 0.25f;
                    MaterialTag = "stone";
                    break;
            }

            // Override fields remain null by default so preset defaults are resolved dynamically.
            // The sound variant list is part of the same override, so a preset switch clears
            // it too; wood splinter clips must not survive onto a glass profile.
            HitParticlePrefab = null;
            HitSound = null;
            HitSoundVariants?.Clear();
            UseDefaultHitParticles = true;
            UseDefaultHitSound = true;
        }

        /// <summary>
        /// Configures the landing-dust defaults as part of applying a material preset.
        /// Wood splinters and glass shards deliberately stay quiet/clean by default;
        /// all other built-in presets use the shared neutral fog asset. Keeping this in
        /// ApplyPreset means an explicit author override remains untouched until they
        /// intentionally choose another preset.
        /// </summary>
        void ApplyLandingDustPresetDefaults(FracturePattern preset)
        {
            bool suppressLandingDust = preset == FracturePattern.RadialGlass ||
                                       preset == FracturePattern.DirectionalWood;
            FragmentImpactEffects = !suppressLandingDust;
            FragmentImpactMinDropHeight = 0.75f;
            FragmentImpactMinSpeed = 2.5f;
            FragmentImpactStrength = 0.5f;

            if (suppressLandingDust)
            {
                // Clear the references as well as the toggle so Wood/Glass profiles
                // accurately communicate their default behaviour in the Inspector. The
                // sound variant list is part of the same override and goes with them.
                GroundDustParticlePrefab = null;
                GroundDustSound = null;
                GroundDustSoundVariants?.Clear();
                return;
            }

            GameObject fogPrefab = DestructionPresetEffectLibrary.LoadPipelineParticlePrefab("Grey Volume Fog");
            GroundDustParticlePrefab = fogPrefab != null
                ? fogPrefab.GetComponent<ParticleSystem>()
                : null;
            GroundDustSound = Resources.Load<AudioClip>("Sounds/QD_stone falls");
        }
    }
}
