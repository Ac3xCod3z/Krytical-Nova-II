using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RipVertices.QuickDestruction
{
    /// <summary>Lifecycle state of a destructible object. Explicit states keep reset,
    /// save/load and debugging predictable.</summary>
    public enum DestructibleState
    {
        Intact = 0,
        Damaged = 1,
        Cracked = 2,
        Released = 3,
        Fractured = 4,
        Settled = 5,
        Recycled = 6
    }

    /// <summary>Outcome of a single damage application.</summary>
    public enum DamageOutcome
    {
        Ignored = 0,
        Damaged = 1,
        Cracked = 2,
        Broken = 3,
        DelayedByBudget = 4
    }

    /// <summary>How a reset is performed.</summary>
    public enum ResetMode
    {
        /// <summary>Snap everything back instantly, fire OnReset.</summary>
        Instant = 0,
        /// <summary>Snap everything back instantly without firing events (bulk resets).</summary>
        Silent = 1
    }

    /// <summary>Fracture pattern family. A pattern is a complete rule set, not just a seed.</summary>
    public enum FracturePattern
    {
        UniformConcrete = 0,
        RadialGlass = 1,
        DirectionalWood = 2,
        BrickCells = 3,
        /// <summary>Parallel sheets along the thinnest axis, then cross cuts (slate, plaster, floors).</summary>
        LayeredSlabs = 4,
        /// <summary>Full-length prisms along the longest axis (basalt columns, pillars, posts).</summary>
        ColumnarPillars = 5,
        /// <summary>Cuts clustered around one impact point: fine debris near the hit, large chunks away.</summary>
        ImpactShatter = 6
    }

    /// <summary>Local mesh axis used by patterns that form long continuous pieces.</summary>
    public enum AxisMode
    {
        /// <summary>Choose each disconnected mesh element's longest/thinnest local axis automatically.</summary>
        Auto = 0,
        X = 1,
        Y = 2,
        Z = 3
    }

    /// <summary>How UVs are generated for the newly created cut faces.</summary>
    public enum CutSurfaceUVMode
    {
        /// <summary>Project UVs in the cut plane at a consistent scale.</summary>
        PlanarProjection = 0,
        /// <summary>Maps UVs smoothly across 2D cut space bounded strictly inside the submesh's UV atlas island.</summary>
        FitToCut = 1,
        /// <summary>Inherit 1D UVs from the sliced source boundary edge and stretch them through the cut face.</summary>
        EdgeStretch = 2
    }

    /// <summary>Cost/quality contract for pre-baked second-level fragment breakup.</summary>
    public enum SecondaryBreakupLevel
    {
        Off = 0,
        Low = 1,
        Medium = 2,
        High = 3,
        Custom = 4
    }

    /// <summary>Shape of the render-only detail generated on newly exposed cut faces.</summary>
    public enum InteriorScatterStyle
    {
        /// <summary>Wood splinters for the Wood pattern, stone chips everywhere else.</summary>
        Auto = 0,
        /// <summary>Irregular rock chips sitting on the cut face.</summary>
        Stone = 1,
        /// <summary>Long splinters running along the grain axis, as in the Wood pattern.</summary>
        Splinters = 2
    }

    /// <summary>Collider strategy applied during bake.</summary>
    public enum ColliderStrategy
    {
        ConvexMesh = 0,
        PrimitiveBox = 1,
        /// <summary>
        /// Uses the exact non-convex final mesh while static/kinematic and a compound
        /// decomposition of that same mesh whenever Unity simulates it dynamically.
        /// </summary>
        ExactMesh = 3
    }

    /// <summary>How the intact, unbroken object participates in physics.</summary>
    public enum IntactBodyMode
    {
        /// <summary>No moving intact body. The object stays fixed until it fractures.</summary>
        Static = 0,
        /// <summary>The intact object is a normal Rigidbody from the beginning.</summary>
        Dynamic = 1,
        /// <summary>The intact object stays kinematic until too much support is lost.</summary>
        ReleaseWhenUnsupported = 2
    }

    /// <summary>Where a structural node obtains its supports.</summary>
    public enum SupportDetectionMode
    {
        /// <summary>Probe the space directly below the intact collider.</summary>
        AutomaticBelow = 0,
        /// <summary>Use only DestructibleRoot references assigned on the scene instance.</summary>
        ManualList = 1,
        /// <summary>Combine automatic probes with the manually assigned list.</summary>
        AutomaticAndManual = 2
    }

    /*
    /// <summary>Quality contract: fragments, colliders, activation and cleanup change together.</summary>
    public enum QualityTier
    {
        Mobile = 0,
        Balanced = 1,
        High = 2
    }
    */

    /// <summary>Which gameplay paths are intended to damage the destructible.</summary>
    public enum DamageTriggerMode
    {
        /// <summary>Use regular Rigidbody impacts. Direct API calls remain available for special cases.</summary>
        RigidbodyImpact = 0,
        /// <summary>Ignore automatic collision damage; gameplay calls ApplyDamage or BreakNow.</summary>
        ScriptedOnly = 1,
        /// <summary>Accept both Rigidbody impacts and explicit gameplay damage calls.</summary>
        Mixed = 2
    }

    /// <summary>Optional per-instance override for the profile's first-break scope.</summary>
    public enum FirstBreakActivationOverride
    {
        UseProfile = 0,
        Local = 1,
        AllPieces = 2
    }

    /// <summary>What happens to a fragment once it settles or its lifetime expires.</summary>
    public enum CleanupAction
    {
        /// <summary>Leave the fragment where it is (gameplay owns it).</summary>
        None = 0,
        /// <summary>Freeze rigidbody as a static visual proxy.</summary>
        FreezeAsProxy = 1,
        /// <summary>Deactivate the fragment renderer + body (shown in the UI as Despawn; recycled back to the pool).</summary>
        Recycle = 2
    }

    /// <summary>Which parts of an impact wave decrease linearly with distance.</summary>
    public enum ImpactSpreadFalloff
    {
        DamageAndImpulse = 0,
        DamageOnly = 1,
        ImpulseOnly = 2,
        None = 3
    }

    /// <summary>Immutable description of an incoming damage intent.
    /// The game sends intent; Quick Destruction decides state and activation.</summary>
    public readonly struct DamageContext
    {
        public readonly float Amount;
        public readonly Vector3 Point;
        public readonly Vector3 Normal;
        public readonly float Radius;
        public readonly int DamageTypeId;
        public readonly int SourceId;
        /// <summary>Optional physical impulse used when this hit causes the break.
        /// A negative value preserves the legacy behaviour and uses Amount.</summary>
        public readonly float BreakEnergy;
        /// <summary>Optional world-space travel direction from the attacker into the hit.
        /// When supplied, profiles can add a shotgun-like directional launch independently
        /// from the radial break push. Vector3.zero preserves the legacy radial response.</summary>
        public readonly Vector3 ImpactDirection;
        /// <summary>Optional directional impulse in newton-seconds; speed depends on mass.
        /// A negative value uses BreakEnergy multiplied by the target profile setting;
        /// zero explicitly disables directional launch for this hit.</summary>
        public readonly float DirectionalForce;
        /// <summary>Exact baked structural group identified by the collider that received
        /// this direct hit. -1 keeps the request unscoped (for example an explosion).
        /// Carrying this identity explicitly prevents a local radius at a wall corner from
        /// selecting fragments that belong to a neighbouring module.</summary>
        public readonly int TargetStructuralGroup;

        public DamageContext(float amount, Vector3 point, Vector3 normal,
            float radius = 0.5f, int damageTypeId = 0, int sourceId = 0)
            : this(amount, point, normal, radius, damageTypeId, sourceId, -1f,
                Vector3.zero, -1f)
        {
        }

        /// <summary>Creates a damage hit whose Health damage and physical break impulse
        /// are intentionally independent.</summary>
        public DamageContext(float amount, Vector3 point, Vector3 normal,
            float radius, int damageTypeId, int sourceId, float breakEnergy)
            : this(amount, point, normal, radius, damageTypeId, sourceId,
                breakEnergy, Vector3.zero, -1f)
        {
        }

        /// <summary>Creates a damage hit with independent Health damage, break impulse
        /// and world-space projectile/travel direction.</summary>
        public DamageContext(float amount, Vector3 point, Vector3 normal,
            float radius, int damageTypeId, int sourceId, float breakEnergy,
            Vector3 impactDirection)
            : this(amount, point, normal, radius, damageTypeId, sourceId,
                breakEnergy, impactDirection, -1f)
        {
        }

        /// <summary>Creates a damage hit with an optional source-authored directional
        /// impulse in newton-seconds. Negative uses the target profile; zero disables it.</summary>
        public DamageContext(float amount, Vector3 point, Vector3 normal,
            float radius, int damageTypeId, int sourceId, float breakEnergy,
            Vector3 impactDirection, float directionalForce,
            int targetStructuralGroup = -1)
        {
            Amount = amount;
            Point = point;
            Normal = normal;
            Radius = radius;
            DamageTypeId = damageTypeId;
            SourceId = sourceId;
            BreakEnergy = breakEnergy;
            ImpactDirection = impactDirection;
            DirectionalForce = directionalForce;
            TargetStructuralGroup = targetStructuralGroup;
        }
    }

    /// <summary>Immutable description of an explicit break request.
    /// Radius &lt;= 0 means "all fragments"; a positive radius activates only fragments
    /// within exactly that distance of <see cref="Point"/>.</summary>
    public readonly struct BreakContext
    {
        public readonly Vector3 Point;
        public readonly Vector3 Normal;
        public readonly float Radius;
        public readonly float Energy;
        /// <summary>Radial push attenuates with distance; false keeps full force inside Radius.</summary>
        public readonly bool LinearRadialFalloff;
        internal readonly bool IsImpactSpread;
        public readonly int SourceId;
        /// <summary>Optional world-space travel direction from the attacker into the hit.</summary>
        public readonly Vector3 ImpactDirection;
        /// <summary>Optional directional impulse in newton-seconds. Negative uses the target
        /// profile multiplier; zero explicitly disables it.</summary>
        public readonly float DirectionalForce;
        /// <summary>Exact baked structural group identified by a direct-hit collider.
        /// -1 means the request is intentionally unscoped, as for radial explosions.</summary>
        public readonly int TargetStructuralGroup;

        public BreakContext(Vector3 point, Vector3 normal, float radius, float energy, int sourceId = 0)
            : this(point, normal, radius, energy, sourceId, Vector3.zero)
        {
        }

        public BreakContext(Vector3 point, Vector3 normal, float radius, float energy,
            int sourceId, Vector3 impactDirection)
            : this(point, normal, radius, energy, sourceId, impactDirection, -1f)
        {
        }

        public BreakContext(Vector3 point, Vector3 normal, float radius, float energy,
            int sourceId, Vector3 impactDirection, float directionalForce)
            : this(point, normal, radius, energy, sourceId, impactDirection,
                directionalForce, -1)
        {
        }

        public BreakContext(Vector3 point, Vector3 normal, float radius, float energy,
            int sourceId, Vector3 impactDirection, float directionalForce,
            int targetStructuralGroup, bool linearRadialFalloff = true, bool isImpactSpread = false)
        {
            Point = point;
            Normal = normal;
            Radius = radius;
            Energy = energy;
            LinearRadialFalloff = linearRadialFalloff;
            IsImpactSpread = isImpactSpread;
            SourceId = sourceId;
            ImpactDirection = impactDirection;
            DirectionalForce = directionalForce;
            TargetStructuralGroup = targetStructuralGroup;
        }
    }

    /// <summary>Result returned from ApplyDamage. Allocation free.</summary>
    public readonly struct DamageResult
    {
        public readonly DamageOutcome Outcome;
        public readonly float ConsumedDamage;
        public readonly int ActivatedFragments;

        public DamageResult(DamageOutcome outcome, float consumedDamage, int activatedFragments)
        {
            Outcome = outcome;
            ConsumedDamage = consumedDamage;
            ActivatedFragments = activatedFragments;
        }
    }

    /// <summary>Public contract implemented by <see cref="DestructibleRoot"/>.
    /// Small, allocation free in the hot path, independent from any weapon/health/VFX system.</summary>
    public interface IDamageableDestructible
    {
        DamageResult ApplyDamage(in DamageContext context);
        void BreakNow(in BreakContext context);
        void ResetDestructible(ResetMode mode);
    }

    /// <summary>Serialized record of a single baked fragment. Captured at bake time so
    /// reset never depends on runtime discovery.</summary>
    [Serializable]
    public class FragmentRecord
    {
        public Transform Transform;
        public Rigidbody Body;
        public Collider Collider;
        [Tooltip("Legacy single convex companion retained for compatibility with older bakes.")]
        public Collider DynamicCollider;
        [Tooltip("Compound convex companions generated from the final solidified fragment mesh and enabled only while an Exact Mesh fragment is dynamic.")]
        public List<Collider> DynamicColliders = new List<Collider>();
        public Renderer Renderer;
        [Tooltip("Common probe sample used while this fragment is still part of the attached shell. Cleared when the piece becomes dynamic so loose debris samples lighting at its own position.")]
        public Transform AttachedProbeAnchor;
        [Tooltip("Primary and render-only detail renderers that share Attached Probe Anchor. Serialized to switch probe sampling without hierarchy scans during a large collapse.")]
        public List<Renderer> ProbeRenderers = new List<Renderer>();
        [Tooltip("Number of leading mesh submeshes that belong to the authored exterior. The final fracture-cap submesh is excluded from the attached shadow proxy so internal cut faces cannot draw the fracture pattern onto an otherwise intact wall.")]
        public int ExteriorSubMeshCount;
        [Tooltip("Shadow modes restored when the fragment becomes loose. While attached, its renderers do not cast individually; one welded exterior-only proxy casts the shell shadow without seams.")]
        public List<ShadowCastingMode> DynamicShadowCastingModes =
            new List<ShadowCastingMode>();
        public Vector3 InitialLocalPosition;
        public Quaternion InitialLocalRotation;
        public Vector3 InitialLocalScale = Vector3.one;
        public float Volume;
        public float Mass = 1f;
        [Tooltip("Disconnected source-mesh element this fragment belongs to. Used for local structural collapse.")]
        public int StructuralGroup;
        [Tooltip("For radial glass: this piece touches the baked outer pane boundary.")]
        public bool BoundaryAnchor;
        [Tooltip("For structural release: this piece sits at the bottom of a grounded structural group and seeds the connectivity search. Carried groups (decks, beams) have no ground anchors.")]
        public bool GroundAnchor;
        [Tooltip("Indices of baked fragments sharing a load-bearing fracture face with this piece.")]
        public List<int> SupportNeighbours = new List<int>();
        [Tooltip("Shared-face areas aligned with Support Neighbours. New bakes use them to release a fragment after most of its original bonded contact has been destroyed; an empty list preserves legacy connectivity behaviour.")]
        public List<float> SupportNeighbourContactAreas = new List<float>();
        public SecondaryFragmentNode SecondaryNode;

        [NonSerialized] public bool Activated;
        [NonSerialized] public bool Settled;
        [NonSerialized] public bool Detached;
        [NonSerialized] public float ActivatedTime;
        [NonSerialized] public float SleepTimer;
        // Smallest-first budget degradation state. Frozen = kept visible but inert;
        // Despawned = removed by a visible/triangle budget. Both cleared on reset.
        [NonSerialized] public bool BudgetFrozen;
        [NonSerialized] public bool BudgetDespawned;
        // Retired by the debris auto-despawn timer (or DespawnDebrisNow). Separate from
        // BudgetDespawned because budget degradation may resurrect its victims when the
        // budget frees up; debris despawn is final until Reset.
        [NonSerialized] public bool DebrisDespawned;
        [NonSerialized] public float RuntimeHealth;
        [NonSerialized] public bool RuntimeHealthReady;
        [NonSerialized] public float LastDamageTime = -999f;
        [NonSerialized] public int LastDamageSourceId = 0;
        // Resolved once on first release. Sweep CCD in supported Unity versions protects
        // primitive colliders but not convex MeshColliders, so mesh-only bodies receive a
        // small static-ground query from their owner's central FixedUpdate loop.
        [NonSerialized] public bool GroundGuardResolved;
        [NonSerialized] public bool NeedsMeshGroundGuard;
        // Cached alongside the guard flags above. The piece's own component is what sees its
        // ground contacts, and the guard must skip a piece that already has one; resolving it
        // once here keeps that check out of the per-step loop. Null for bakes that carry no
        // FragmentRuntime, which simply leaves the guard at its unsuppressed behaviour.
        [NonSerialized] public FragmentRuntime Runtime;
        /// <summary>
        /// Separate from LastDamageTime because the impact particle and sound are deliberately
        /// allowed on hits the fragment survives, so they cannot share the damage debounce —
        /// which only advances when damage actually lands. Without a debounce of their own, a
        /// piece grinding or wedged against a surface reports every contact and produces a
        /// stream of effects in one spot.
        /// </summary>
        [NonSerialized] public float LastImpactEffectTime = -999f;
        // Lazily computed once; -1 means "not yet counted".
        [NonSerialized] public int CachedTriangleCount = -1;
    }
}
