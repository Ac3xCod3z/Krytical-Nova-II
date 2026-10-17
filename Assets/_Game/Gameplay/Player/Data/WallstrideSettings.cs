using UnityEngine;

namespace KryticalNova.Player
{
    /// <summary>
    /// Wallstride (owner, 16 Oct 2026): running up walls and into trees. When it opens, what a surface must be to be
    /// climbed, how the climb moves, and how near a player a tree is solid. Every number of the system is here.
    /// The rule that reads them is <see cref="WallRunRules"/>; the motor is <see cref="PlayerWallRunMotor"/>.
    /// </summary>
    [CreateAssetMenu(menuName = "Krytical Nova/Player/Wallstride Settings", fileName = "WallstrideSettings")]
    public sealed class WallstrideSettings : ScriptableObject
    {
        [Header("When it opens (all three)")]
        [Min(1)] public int requiredLevel = 60;
        [Tooltip("Run at this rank (its highest is 20).")]
        [Min(1)] public int requiredRunRank = 20;
        [Tooltip("Jump at this rank (its highest is 20).")]
        [Min(1)] public int requiredJumpRank = 20;

        [Header("What can be climbed")]
        [Tooltip("Layers whose still, solid surfaces count as walls: buildings, town walls, rocks. Anything that moves, " +
                 "can be hurt or used is never climbed, and a ClimbableSurface set to Never rules a surface out.")]
        public LayerMask wallLayers = 1; // Default
        [Tooltip("A surface must stand at least this steep: degrees between its face and the ground (90 is upright).")]
        [Range(45f, 90f)] public float minSurfaceAngle = 70f;
        [Tooltip("...and may lean out over the climber by no more than this past upright.")]
        [Range(0f, 45f)] public float maxOverhang = 20f;
        [Tooltip("How far from head-on (degrees) a run or a jump may meet the surface and still go up it.")]
        [Range(5f, 85f)] public float maxApproachAngle = 45f;
        [Tooltip("Lower than this (metres) is vaulted or walked, not climbed. Kept equal to the vault's tallest " +
                 "(PlayerVaultMotor.maximumHeight, 2.2 m) so every height is one or the other.")]
        [Min(0.5f)] public float minWallHeight = 2.2f;
        [Tooltip("Higher than this (metres from the foot) is out of reach.")]
        [Min(1f)] public float maxClimbHeight = 14f;
        [Tooltip("How far past the edge the top is looked for (metres): the walkable ground the climb ends on.")]
        [Min(0.3f)] public float mantleReach = 1.1f;
        [Tooltip("How far from where it is looked for the walkable top may be (metres).")]
        [Min(0.1f)] public float topSampleRadius = 0.7f;

        [Header("The climb")]
        [Tooltip("Climbing speed as a share of the player's speed on the ground (Run's rank, Move Speed).")]
        [Range(0.1f, 2f)] public float climbSpeedShare = 0.55f;
        [Min(0.5f)] public float minClimbSpeed = 4f;
        [Min(0.5f)] public float maxClimbSpeed = 14f;
        [Tooltip("Seconds the feet take to meet the surface at its foot.")]
        [Min(0.02f)] public float plantSeconds = 0.18f;
        [Tooltip("Seconds over the edge onto the top, or into the tree.")]
        [Min(0.05f)] public float mantleSeconds = 0.4f;
        [Tooltip("How high over the edge the body rises as it goes over (metres).")]
        [Min(0f)] public float mantleLift = 0.35f;
        [Tooltip("Where the hands are when the feet stop climbing: this far below the edge (metres).")]
        [Min(0.2f)] public float edgeBelowHands = 1.1f;
        [Tooltip("Room between the body's round and the surface while climbing (metres), measured from the face's outermost " +
                 "part over the whole climb (ledges, frames, balconies). The run's reach of knees, feet and hands stays " +
                 "outside the building (owner, 17 Oct 2026: no body inside a building it is scaling).")]
        [Min(0f)] public float wallClearance = 0.3f;
        [Tooltip("How far the body leans back from the surface while running up it (degrees), so head and hands stay clear.")]
        [Range(0f, 45f)] public float climbLean = 12f;
        [Tooltip("Over the edge, the body first rises this high above the top (metres), then moves in over it: it never " +
                 "cuts through the corner.")]
        [Min(0f)] public float mantleClearance = 0.25f;
        [Tooltip("Flux one climb costs, paid as it starts.")]
        [Min(0f)] public float fluxCost = 12f;

        [Header("Starting on its own")]
        [Tooltip("A run whose way ahead meets a climbable surface within this many metres goes up it when the walk round is " +
                 "much longer (below) or there is none.")]
        [Min(1f)] public float autoLookAhead = 8f;
        [Tooltip("The walk round counts as much longer when it is this many times the straight way.")]
        [Min(1f)] public float autoDetourFactor = 2f;

        [Header("Jumping into a surface")]
        [Tooltip("A jump blocked by a climbable surface catches it this far above the take-off (metres), within the climb.")]
        [Min(0f)] public float catchHeight = 1.4f;
        [Tooltip("A surface met with no top within reach is kicked off: the body comes down this far out from it (metres).")]
        [Min(0.5f)] public float kickOffDistance = 2.5f;
        [Min(0f)] public float kickOffLift = 0.8f;

        [Header("Coming down")]
        [Tooltip("Gravity of a drop from a tree or a kick off a wall (m/s²).")]
        [Min(1f)] public float dropGravity = 14f;
        [Tooltip("Farthest a drop from a tree may land from it (metres along the ground).")]
        [Min(1f)] public float maxDropDistance = 14f;
        [Tooltip("Killed on a surface: the body is pushed this far out from it before it falls.")]
        [Min(0f)] public float deathPushOff = 0.6f;

        [Header("Trees (TreeProxyField)")]
        [Tooltip("A tree is solid, and can be climbed, only within this many metres of a player. Farther trees have no collider " +
                 "at all. Always nearer than where trees turn into their far picture (WorldLodSettings), so a tree that can be " +
                 "climbed is always drawn whole.")]
        [Min(5f)] public float treeProxyRadius = 35f;
        [Tooltip("Seconds between two looks at which trees are near a player.")]
        [Range(0.05f, 2f)] public float treeProxyRefresh = 0.25f;
        [Tooltip("Most tree colliders alive at once in a map, for every player together.")]
        [Min(8)] public int treeProxyCap = 512;
        [Tooltip("Tree instances shorter than this (bushes, ferns) are never solid.")]
        [Min(0.5f)] public float minTreeHeight = 3f;
        [Tooltip("Where in a tree's height the player stands once up it.")]
        [Range(0.2f, 0.95f)] public float perchHeightShare = 0.6f;

        private void OnValidate()
        {
            maxClimbSpeed = Mathf.Max(maxClimbSpeed, minClimbSpeed);
            maxClimbHeight = Mathf.Max(maxClimbHeight, minWallHeight + 0.5f);
        }
    }
}
