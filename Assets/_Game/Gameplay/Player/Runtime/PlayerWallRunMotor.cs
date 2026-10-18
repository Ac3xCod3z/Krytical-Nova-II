using KryticalNova.Networking;
using System;
using System.Collections.Generic;
using KryticalNova.Core;
using KryticalNova.Progression;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using Kinemation.MotionWarping.Runtime.Core;
using Kinemation.MotionWarping.Runtime.Utility;
using WarpComponent = Kinemation.MotionWarping.Runtime.Core.MotionWarping;

namespace KryticalNova.Player
{
    /// <summary>Where a climb is, as every machine is told it.</summary>
    public enum WallRunPhase : byte
    {
        None,
        /// <summary>The feet meet the foot of the surface (or the surface, out of a jump).</summary>
        Plant,
        /// <summary>Running up it.</summary>
        Climb,
        /// <summary>Over the edge onto the walkable top.</summary>
        Mantle,
        /// <summary>Into the tree, to the place to stand.</summary>
        ToPerch,
        /// <summary>Standing in a tree, off the ground.</summary>
        Perched,
        /// <summary>Kicked off a surface with no top, down to the ground in front of it.</summary>
        KickOff,
        /// <summary>Down from a tree to a place on the ground.</summary>
        DropDown,
        /// <summary>Walls as a second ground: running along a wall, the body standing on it (PlayerWallRunMotor.WallWalk).</summary>
        WallMove,
        /// <summary>Standing still on a wall.</summary>
        WallHold,
        /// <summary>Off a wall in a jump with a flip: out of Grip, or a jump clicked away from it.</summary>
        PushOff,
    }

    /// <summary>The phase every machine plays, and how it faces.</summary>
    public struct WallRunPose : INetworkSerializable, IEquatable<WallRunPose>
    {
        public WallRunPhase Phase;
        public float Yaw;
        public float Duration;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Phase);
            serializer.SerializeValue(ref Yaw);
            serializer.SerializeValue(ref Duration);
        }

        public bool Equals(WallRunPose other) => Phase == other.Phase && Mathf.Approximately(Yaw, other.Yaw) && Mathf.Approximately(Duration, other.Duration);
        public override bool Equals(object obj) => obj is WallRunPose other && Equals(other);
        public override int GetHashCode() => ((int)Phase << 24) ^ Yaw.GetHashCode() ^ Duration.GetHashCode();
    }

    /// <summary>
    /// Wallstride (owner, 16 Oct 2026): the player runs up walls and into trees, to get the high ground, break off
    /// a fight or wait out a chase and buff up again. It opens at level 60 with Run and Jump at rank 20
    /// (<see cref="WallstrideSettings"/>), and <see cref="WallRunRules"/> decides each surface: steep, met head-on,
    /// in reach, with walkable ground at the top (or a place to stand in a tree), and Flux to pay.
    ///
    /// A climb starts three ways: a click on a wall or a tree with Run or Jump selected; a run whose way is a wall
    /// (<see cref="TryArmTowards"/>: the destination is up it and the walk round is much longer, or there is none);
    /// and a jump that meets a climbable surface, which catches it (<see cref="TryPlanCatch"/>) and runs on up from
    /// where it met it. Built like the jump and the vault: decided and moved where movement is authoritative, the
    /// agent off the NavMesh for the whole climb and given back at the top; NetworkTransform carries the body to the
    /// other machines and the phase (a NetworkVariable, so a late joiner sees a player in a tree as one) tells them
    /// what to play. Killed on a wall or in a tree, the body lets go and falls clear (<see cref="ReleaseForDeath"/>).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NavMeshAgent), typeof(PlayerMovementSkills), typeof(PlayerResources))]
    public sealed partial class PlayerWallRunMotor : NetworkBehaviour
    {
        private const float WallScanStep = 0.25f;
        // The face is scanned from this far out, so a ledge or balcony up to this deep is seen from outside it.
        private const float WallScanOut = 1.2f;
        private const float WallScanLength = 1.8f;
        private const float BaseSampleRadius = 1.2f;
        private const float GroundProbe = 60f;
        private const float BodySkin = 0.05f;
        private const float ArmTrigger = 0.45f;
        private const float RearmSeconds = 0.15f;
        private const float RemoteFade = 0.12f;
        private const float MinimumDuration = 0.05f;

        [SerializeField] private WallstrideSettings settings;

        [Header("Animations (stand-ins until a climbing set exists; any clip can be put in)")]
        [Tooltip("The feet meet the surface (a vault's plant).")]
        [SerializeField] private AnimationClip plantClip;
        [Tooltip("Running up the surface (looped).")]
        [SerializeField] private AnimationClip climbClip;
        [SerializeField, Min(0.1f)] private float climbClipSpeed = 1.3f;
        [Tooltip("Over the edge onto the top, or into the tree.")]
        [SerializeField] private AnimationClip mantleClip;
        [Tooltip("Standing in a tree (looped).")]
        [SerializeField] private AnimationClip perchClip;
        [Tooltip("Coming down from a tree or off a wall. Empty: the jump's own airborne pose.")]
        [SerializeField] private AnimationClip dropClip;
        [SerializeField, Min(0.01f)] private float fadeIn = 0.1f;
        [SerializeField, Min(0.01f)] private float fadeOut = 0.15f;

        /// <summary>One climb, worked out from the surface (authority).</summary>
        private struct Plan
        {
            public ClimbableKind Kind;
            public Vector3 Normal;
            public Vector3 BaseFeet;     // where the feet meet the surface's foot
            public Vector3 ClimbEndFeet; // where the feet stop climbing (hands at the edge)
            public Vector3 TopFeet;      // the walkable top, or the place to stand in the tree
            public float Height;
            public bool HasTop, Room;
        }

        private static readonly List<PlayerWallRunMotor> active = new List<PlayerWallRunMotor>(16);
        private static readonly RaycastHit[] Hits = new RaycastHit[8];

        private readonly NetworkVariable<WallRunPose> netPose = new NetworkVariable<WallRunPose>(
            default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private NavMeshAgent agent;
        private PlayerMovementSkills movementSkills;
        private PlayerResources resources;
        private PlayerCharacterProgression progression;
        private PlayerAnimancerMotor animancerMotor;
        private PlayerJumpMotor jumpMotor;

        // Authority.
        private WallRunPhase phase;
        private Plan plan;
        // The climb as a route of legs, carried by Motion Warping (the warp the jump and the vault use).
        private const int MaxLegs = 6;
        private const float RouteClockSeconds = 30f;
        private const int EaseSamples = 8;
        private readonly Leg[] legs = new Leg[MaxLegs];
        private int legCount, legIndex;
        private float routeClock;
        private bool routeActive, warpOwned;
        private WallRunPhase afterRoute;
        private WarpComponent warp;
        private MotionWarpingAsset warpAsset;
        private AnimationClip routeClock01;
        private Vector3 legFromFeet;
        private bool armed;
        private float armedAt;
        private Plan armedPlan;
        private bool catchPending;
        private Plan catchPlan;
        private float resumeSpeed;

        // Every machine: what is being played.
        private WallRunPhase shownPhase;
        private bool playingAction;

        /// <summary>The Wallstride numbers this motor plays by (tree proxies read them too).</summary>
        public WallstrideSettings Settings => settings;
        /// <summary>Every motor in the game on this machine: the tree proxies are made around them.</summary>
        public static IReadOnlyList<PlayerWallRunMotor> Active => active;
        /// <summary>The phase as every machine sees it.</summary>
        public WallRunPhase Phase => HasAuthority ? phase : netPose.Value.Phase;
        /// <summary>Climbing, mantling, dropping: the body is the motor's.</summary>
        public bool IsBusy => Phase != WallRunPhase.None;
        /// <summary>Standing in a tree.</summary>
        public bool IsPerched => Phase == WallRunPhase.Perched;
        /// <summary>The motor has a climb lined up and the agent is walking to its foot.</summary>
        public bool IsArmed => armed;
        /// <summary>True on this machine when it should hold the tree colliders round this player (the server, or its own player).</summary>
        public bool WantsTreeProxies => !IsSpawned || IsServer || IsOwner;
        /// <summary>Wallstride is open for this character: level 60, Run and Jump at rank 20.</summary>
        public bool WallstrideOpen => movementSkills != null && progression != null
            && WallRunRules.IsOpen(progression.CurrentLevel, movementSkills.RunRank, movementSkills.JumpRank, settings);
        /// <summary>A line for the player: why the last climb did or did not start.</summary>
        public event Action<string> Message;

        private bool HasAuthority => !IsSpawned || IsServer;
        private float Offset => agent != null ? agent.baseOffset : 0f;
        private float Radius => agent != null ? agent.radius : 0.5f;
        private Vector3 Feet => transform.position - transform.up * Offset; // the body may stand on a wall

        private void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            movementSkills = GetComponent<PlayerMovementSkills>();
            resources = GetComponent<PlayerResources>();
            progression = GetComponent<PlayerCharacterProgression>();
            animancerMotor = GetComponent<PlayerAnimancerMotor>();
            jumpMotor = GetComponent<PlayerJumpMotor>();
            warp = GetComponentInChildren<WarpComponent>(true);
            // The warp's length is its clip's: a silent clip long enough for any climb, played by nothing.
            routeClock01 = new AnimationClip { legacy = true, name = "Wallstride route clock" };
            routeClock01.SetCurve(string.Empty, typeof(Transform), "localPosition.x", AnimationCurve.Constant(0f, RouteClockSeconds, 0f));
            warpAsset = ScriptableObject.CreateInstance<MotionWarpingAsset>();
            warpAsset.name = "Wallstride route (runtime)";
            warpAsset.animation = routeClock01;
        }

        public override void OnDestroy()
        {
            if (warpAsset != null) Destroy(warpAsset);
            if (routeClock01 != null) Destroy(routeClock01);
            base.OnDestroy();
        }

        private void OnEnable() { if (!active.Contains(this)) active.Add(this); }

        private void OnDisable()
        {
            active.Remove(this);
            armed = false;
            catchPending = false;
        }

        public override void OnNetworkSpawn()
        {
            netPose.OnValueChanged += OnPoseChanged;
            if (!IsServer) Show(netPose.Value); // a late joiner sees a player in a tree as one
        }

        public override void OnNetworkDespawn() => netPose.OnValueChanged -= OnPoseChanged;

        // ------------------------------------------------------------------ starting a climb

        /// <summary>
        /// A click on a surface with Run or Jump selected (the owner's game asks; the server decides). Run: the player
        /// runs to its foot and up. Jump: the player jumps at it and catches it.
        /// </summary>
        /// <returns>True when the click was on something climbable and has been dealt with (started or refused, with the reason told).</returns>
        public bool TryClimbClicked(Vector3 point, Vector3 normal, Collider surface, int obstacleLayers, float modelForwardOffset)
        {
            if (surface == null || !IsClimbableObject(surface, out _)) return false;
            if (WantsWallWalk(point, normal)) return TryWallWalkClicked(point, normal);
            if (!HasAuthority)
            {
                // Checked here first, so a bad click is refused at once.
                bool jump = movementSkills != null && movementSkills.IsJumpSelected;
                if (!Decide(point, normal, surface, Feet, fromJump: jump, out _, out string early)) { Message?.Invoke(early); return true; }
                RequestClimbServerRpc(point, normal);
                return true;
            }
            if (!ServerClimbClicked(point, normal, surface, obstacleLayers, modelForwardOffset, out string reason)) Message?.Invoke(reason);
            return true;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void RequestClimbServerRpc(Vector3 point, Vector3 normal)
        {
            if (!RpcBudget.Allow(this, CallKind.Move, nameof(RequestClimbServerRpc))) return;
            if (!RpcBudget.PointFits(this, nameof(RequestClimbServerRpc), point) || !RpcBudget.PointFits(this, nameof(RequestClimbServerRpc), normal)) return;
            // The layers and the model's turn are the server's own: a game may only say where it clicked (online phase 2).
            var player = GetComponent<PointAndClickPlayer>();
            if (player == null) return;
            int obstacleLayers = player.RaycastLayers.value;
            float modelForwardOffset = player.ModelForwardOffset;
            // The click is found again here: the server trusts the point, never a collider the client named.
            Collider surface = FindSurface(point, normal);
            string reason = "That cannot be climbed.";
            if (surface == null || !ServerClimbClicked(point, normal, surface, obstacleLayers, modelForwardOffset, out reason)) ClimbRefusedRpc(reason);
        }

        [Rpc(SendTo.Owner)]
        private void ClimbRefusedRpc(string reason) { if (!string.IsNullOrEmpty(reason)) Message?.Invoke(reason); }

        private bool ServerClimbClicked(Vector3 point, Vector3 normal, Collider surface, int obstacleLayers, float modelForwardOffset, out string reason)
        {
            reason = string.Empty;
            if (movementSkills != null && movementSkills.IsJumpSelected && jumpMotor != null)
            {
                // Jump selected: a jump at the surface, caught where it meets it.
                if (!Decide(point, normal, surface, Feet, fromJump: true, out Plan jumpAt, out reason)) return false;
                catchPlan = jumpAt;
                catchPending = true;
                Vector3 catchFeet = CatchPointOn(jumpAt, Feet);
                if (!jumpMotor.TryJumpToCatch(catchFeet, obstacleLayers, modelForwardOffset, out reason)) { catchPending = false; return false; }
                return true;
            }
            if (!Decide(point, normal, surface, Feet, fromJump: false, out Plan climb, out reason)) return false;
            return ArmOrBegin(climb);
        }

        /// <summary>
        /// Where movement is decided: a move order towards <paramref name="destination"/> while running. When the way is
        /// up a climbable surface in front (the destination stands on it, and walking round is much longer or impossible),
        /// the climb is lined up and <paramref name="goal"/> is its foot, where the agent should be sent instead.
        /// </summary>
        public bool TryArmTowards(Vector3 destination, NavMeshPath walkPath, out Vector3 goal)
        {
            goal = destination;
            armed = false;
            if (!HasAuthority || settings == null || movementSkills == null || !movementSkills.IsRunning || !WallstrideOpen) return false;
            Vector3 feet = Feet;
            Vector3 flat = destination - feet;
            flat.y = 0f;
            float straight = flat.magnitude;
            if (straight < 0.5f || destination.y - feet.y < settings.minWallHeight * 0.5f) return false;
            bool noWayRound = walkPath == null || walkPath.status != NavMeshPathStatus.PathComplete;
            if (!noWayRound && PathLength(walkPath) < straight * settings.autoDetourFactor) return false;

            Vector3 direction = flat / straight;
            Vector3 origin = feet + Vector3.up * 1f;
            int count = Physics.RaycastNonAlloc(origin, direction, Hits, Mathf.Min(straight, settings.autoLookAhead), SurfaceMask(), QueryTriggerInteraction.Ignore);
            if (!Nearest(count, out RaycastHit hit)) return false;
            if (!Decide(hit.point, hit.normal, hit.collider, feet, fromJump: false, out Plan climb, out _)) return false;
            // The top must be where the move was going, near enough.
            if (Mathf.Abs(climb.TopFeet.y - destination.y) > 2f) return false;
            armedPlan = climb;
            armed = true;
            armedAt = Time.time;
            goal = climb.BaseFeet;
            return true;
        }

        /// <summary>
        /// The jump motor, planning a jump whose arc is blocked: is the thing in the way a surface the player can catch?
        /// When it is, <paramref name="catchFeet"/> is where the jump should end (against it), and the climb runs on
        /// from there when the jump lands (<see cref="BeginCaught"/>).
        /// </summary>
        public bool TryPlanCatch(Vector3 startFeet, Vector3 aimFeet, int layers, out Vector3 catchFeet)
        {
            catchFeet = default;
            catchPending = false;
            if (settings == null || !WallstrideOpen) return false;
            Vector3 flat = aimFeet - startFeet;
            flat.y = 0f;
            float distance = flat.magnitude;
            if (distance < 0.3f) return false;
            Vector3 origin = startFeet + Vector3.up * 1f;
            int count = Physics.SphereCastNonAlloc(origin, Radius * 0.6f, flat / distance, Hits, distance, layers & SurfaceMask(), QueryTriggerInteraction.Ignore);
            if (!Nearest(count, out RaycastHit hit)) return false;
            if (!Decide(hit.point, hit.normal, hit.collider, startFeet, fromJump: true, out Plan climb, out _)) return false;
            catchPlan = climb;
            catchPending = true;
            catchFeet = CatchPointOn(climb, startFeet);
            return true;
        }

        /// <summary>Drops a climb lined up for a route that is no longer taken.</summary>
        public void Disarm() => armed = false;

        /// <summary>Forgets a catch planned for a jump that did not happen.</summary>
        public void ForgetCatch() => catchPending = false;

        /// <summary>The jump motor: the jump planned to catch a surface has met it. The climb runs on from here.</summary>
        public bool BeginCaught()
        {
            if (!HasAuthority || !catchPending) return false;
            catchPending = false;
            if (!Pay()) return false;
            plan = catchPlan;
            resumeSpeed = GroundSpeed();
            if (agent != null) agent.enabled = false; // already off for the jump; the warp owns the root until the top
            StartRoute(fromJump: true);
            return true;
        }

        /// <summary>Perched in a tree: a click on the ground below drops down to it.</summary>
        public bool TryDropTo(Vector3 point, out string reason)
        {
            reason = string.Empty;
            if (!IsPerched) return false;
            if (!HasAuthority) { RequestDropServerRpc(point); return true; }
            return ServerDropTo(point, out reason);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void RequestDropServerRpc(Vector3 point)
        {
            if (!RpcBudget.Allow(this, CallKind.Move, nameof(RequestDropServerRpc))) return;
            if (!RpcBudget.PointFits(this, nameof(RequestDropServerRpc), point)) return;
            if (!ServerDropTo(point, out string reason)) ClimbRefusedRpc(reason);
        }

        private bool ServerDropTo(Vector3 point, out string reason)
        {
            reason = string.Empty;
            if (phase != WallRunPhase.Perched || settings == null) return false;
            var filter = Filter();
            if (!NavMesh.SamplePosition(point, out NavMeshHit landing, 1.5f, filter) || !OnOpenGround(landing.position)) { reason = "Choose a walkable place to drop down to."; return false; }
            Vector3 flat = landing.position - Feet;
            flat.y = 0f;
            if (flat.magnitude > settings.maxDropDistance) { reason = $"Too far: drop within {settings.maxDropDistance:0} m of the tree."; return false; }
            legCount = 0;
            AddArc(WallRunPhase.DropDown, Feet, landing.position, 0.4f, Facing(landing.position - Feet));
            afterRoute = WallRunPhase.None;
            PlayRoute();
            return true;
        }

        // ------------------------------------------------------------------ deciding (every machine can ask)

        /// <summary>The canRunUpWall check of one surface, and the climb it would be.</summary>
        private bool Decide(Vector3 point, Vector3 normal, Collider surface, Vector3 fromFeet, bool fromJump, out Plan climb, out string reason)
        {
            climb = default;
            // A click on a ledge, a sill or an awning of a wall is meant for the wall: its face straight ahead is used.
            if (settings != null && surface != null && WallRunRules.SurfaceAngle(normal) < settings.minSurfaceAngle
                && FaceAhead(point, surface, fromFeet, out Vector3 facePoint, out Vector3 faceNormal))
            {
                point = facePoint;
                normal = faceNormal;
            }
            bool eligible = IsClimbableObject(surface, out ClimbableSurface marker);
            ClimbableKind kind = marker != null ? marker.kind : ClimbableKind.Wall;
            bool stride = fromJump || (movementSkills != null && (movementSkills.IsRunning || movementSkills.IsJumpSelected));
            bool shaped = eligible && kind != ClimbableKind.Never && Shape(point, normal, kind, marker, fromFeet, out climb);
            var query = new ClimbQuery
            {
                Unlocked = WallstrideOpen,
                Busy = resources == null || !resources.IsAlive || IsBusy || (!fromJump && jumpMotor != null && jumpMotor.IsBusy),
                Stride = stride,
                FromJump = fromJump,
                Eligible = eligible && (shaped || kind == ClimbableKind.Never),
                Kind = kind,
                Normal = shaped ? climb.Normal : normal,
                Travel = (shaped ? climb.BaseFeet : point) - fromFeet,
                Height = shaped ? climb.Height : 0f,
                HasTop = shaped && climb.HasTop,
                RoomAtTop = shaped && climb.Room,
                CanPay = resources != null && resources.CanSpendChi(settings != null ? settings.fluxCost : 0f),
            };
            ClimbVerdict verdict = WallRunRules.Evaluate(query, settings);
            reason = WallRunRules.Reason(verdict, settings);
            return verdict == ClimbVerdict.CanRunUpWall;
        }

        /// <summary>
        /// The upright face of the same building as <paramref name="surface"/>, straight ahead of the player at chest
        /// height towards <paramref name="point"/>: what a click on one of its ledges is aimed at.
        /// </summary>
        private bool FaceAhead(Vector3 point, Collider surface, Vector3 fromFeet, out Vector3 facePoint, out Vector3 faceNormal)
        {
            facePoint = point;
            faceNormal = Vector3.up;
            Vector3 flat = point - fromFeet;
            flat.y = 0f;
            float distance = flat.magnitude;
            if (distance < 0.1f) return false;
            Vector3 origin = fromFeet + Vector3.up * FaceProbeHeight;
            int count = Physics.RaycastNonAlloc(origin, flat / distance, Hits, distance + 2f, SurfaceMask(), QueryTriggerInteraction.Ignore);
            if (!Nearest(count, out RaycastHit face)) return false;
            if (face.collider.transform.root != surface.transform.root) return false; // something else is in the way
            if (WallRunRules.SurfaceAngle(face.normal) < settings.minSurfaceAngle) return false;
            facePoint = face.point;
            faceNormal = face.normal;
            return true;
        }

        private const float FaceProbeHeight = 1.2f;
        private const float FaceGap = 2f; // metres a face may go missing (a window, a door) and still be the same face

        /// <summary>Still and solid, not a unit or something used, not a player's own, and on a wall layer or marked.</summary>
        private bool IsClimbableObject(Collider surface, out ClimbableSurface marker)
        {
            marker = surface != null ? surface.GetComponentInParent<ClimbableSurface>() : null;
            if (surface == null || surface.isTrigger || surface.transform.IsChildOf(transform)) return false;
            if (marker != null) return marker.kind != ClimbableKind.Never;
            if (settings == null || (settings.wallLayers.value & (1 << surface.gameObject.layer)) == 0) return false;
            Rigidbody body = surface.attachedRigidbody;
            if (body != null && !body.isKinematic) return false;
            if (surface.GetComponentInParent<IDamageable>() != null) return false;
            if (surface.GetComponentInParent<IInteractable>() != null) return false;
            return true;
        }

        /// <summary>
        /// The climb a surface makes: its foot on the walkable ground, how high it goes and where it ends (the walkable
        /// top over its edge, or the place to stand in a tree).
        /// </summary>
        private bool Shape(Vector3 point, Vector3 normal, ClimbableKind kind, ClimbableSurface marker, Vector3 fromFeet, out Plan climb)
        {
            climb = new Plan { Kind = kind };
            if (settings == null) return false;
            var filter = Filter();

            if (kind == ClimbableKind.Tree && marker != null)
            {
                Vector3 trunk = marker.TrunkBase;
                // A trunk is round: it is climbed on the side the player comes from. Where the click met it is the
                // camera's side, which would make most clicks on a tree read as not head-on.
                Vector3 side = fromFeet - trunk;
                side.y = 0f;
                if (side.sqrMagnitude < 1e-4f) side = point - trunk;
                side.y = 0f;
                if (side.sqrMagnitude < 1e-4f) side = -transform.forward;
                side.Normalize();
                climb.Normal = side;
                Vector3 foot = trunk + side * (marker.trunkRadius + settings.wallStandOff);
                if (!NavMesh.SamplePosition(foot, out NavMeshHit treeBase, BaseSampleRadius + marker.trunkRadius, filter)) return false;
                climb.BaseFeet = new Vector3(foot.x, treeBase.position.y, foot.z);
                climb.TopFeet = marker.PerchFor(side, settings.perchHeightShare, Radius);
                climb.Height = climb.TopFeet.y - climb.BaseFeet.y;
                climb.ClimbEndFeet = new Vector3(foot.x, Mathf.Max(climb.BaseFeet.y, climb.TopFeet.y - 0.6f), foot.z);
                climb.HasTop = climb.Height > 0.5f;
                climb.Room = climb.HasTop && !BodyBlocked(climb.TopFeet, marker.GetComponent<Collider>());
                return true;
            }

            Vector3 n = new Vector3(normal.x, 0f, normal.z);
            if (n.sqrMagnitude < 1e-4f) { climb.Normal = normal; return true; } // a top, not a face: too flat
            n.Normalize();
            climb.Normal = normal.normalized;

            // The foot of the wall, on the ground in front of it.
            Vector3 probe = point + n * WallScanOut;
            if (!Physics.Raycast(probe + Vector3.up * 0.5f, Vector3.down, out RaycastHit ground, GroundProbe, GroundMask(), QueryTriggerInteraction.Ignore)) return false;
            Vector3 wallFoot = new Vector3(point.x, ground.point.y, point.z);

            // Up the face until it ends for good: that is the edge. A window, a door or a vent is a gap in the face, not
            // its end, so the face may go missing for a little and carry on. On the way, how far the face stands out
            // anywhere (a ledge, a window frame, a balcony): the climb keeps the whole body outside the outermost part.
            float edge = -1f, outermost = 0f, lastFace = 0f;
            int mask = SurfaceMask();
            for (float h = WallScanStep; h <= settings.maxClimbHeight + WallScanStep; h += WallScanStep)
            {
                Vector3 origin = wallFoot + Vector3.up * h + n * WallScanOut;
                if (Physics.Raycast(origin, -n, out RaycastHit face, WallScanLength, mask, QueryTriggerInteraction.Ignore))
                {
                    outermost = Mathf.Max(outermost, WallScanOut - face.distance);
                    lastFace = h;
                    continue;
                }
                if (h - lastFace < FaceGap) continue; // an opening in the face; it may carry on above
                edge = lastFace + WallScanStep;
                break;
            }
            // Ran out of reach while the face was still there, or only in a gap: the face's last height decides.
            if (edge < 0f && lastFace < settings.maxClimbHeight - FaceGap) edge = lastFace + WallScanStep;
            climb.Height = edge < 0f ? settings.maxClimbHeight + 1f : edge;

            // The climb's line: the body's round plus clearance off the outermost part of the face.
            Vector3 standAt = wallFoot + n * (settings.wallStandOff + outermost);
            if (!NavMesh.SamplePosition(standAt, out NavMeshHit baseHit, BaseSampleRadius, filter) || !OnOpenGround(baseHit.position)) return false;
            climb.BaseFeet = new Vector3(standAt.x, baseHit.position.y, standAt.z);
            if (edge < 0f) return true; // runs on past the reach: too high

            // The walkable top, just over the edge.
            Vector3 over = wallFoot - n * settings.mantleReach + Vector3.up * (edge + 0.3f);
            if (NavMesh.SamplePosition(over, out NavMeshHit top, settings.topSampleRadius + 0.6f, filter)
                && Mathf.Abs(top.position.y - (wallFoot.y + edge)) <= 0.8f && OnOpenGround(top.position))
            {
                climb.TopFeet = top.position;
                climb.HasTop = true;
                climb.Room = !BodyBlocked(top.position, null);
            }
            climb.ClimbEndFeet = new Vector3(climb.BaseFeet.x, Mathf.Max(climb.BaseFeet.y, wallFoot.y + edge - settings.edgeBelowHands), climb.BaseFeet.z);
            return true;
        }

        private Vector3 CatchPointOn(Plan climb, Vector3 startFeet)
        {
            float y = Mathf.Clamp(startFeet.y + settings.catchHeight, climb.BaseFeet.y, climb.ClimbEndFeet.y);
            return new Vector3(climb.BaseFeet.x, y, climb.BaseFeet.z);
        }

        // ------------------------------------------------------------------ the climb (authority)

        private bool ArmOrBegin(Plan climb)
        {
            if (Flat(Feet, climb.BaseFeet) <= ArmTrigger + Radius)
            {
                Begin(climb);
                return true;
            }
            // Run to its foot first, then up.
            if (agent == null || !agent.isActiveAndEnabled || !agent.isOnNavMesh) return false;
            var path = new NavMeshPath();
            if (!agent.CalculatePath(climb.BaseFeet, path) || path.status != NavMeshPathStatus.PathComplete) return false;
            agent.SetPath(path);
            agent.isStopped = false;
            armedPlan = climb;
            armed = true;
            armedAt = Time.time;
            return true;
        }

        private void Begin(Plan climb)
        {
            armed = false;
            if (!Pay()) { Message?.Invoke(WallRunRules.Reason(ClimbVerdict.NoFlux, settings)); return; }
            plan = climb;
            resumeSpeed = GroundSpeed();
            if (agent.isActiveAndEnabled && agent.isOnNavMesh)
            {
                agent.isStopped = true;
                agent.ResetPath();
                agent.velocity = Vector3.zero;
            }
            agent.enabled = false; // the warp owns the root until the top
            StartRoute(fromJump: false);
        }

        private bool Pay() => settings != null && resources != null && (settings.fluxCost <= 0f || resources.TrySpendChi(settings.fluxCost));

        // ------------------------------------------------------------------ the route: one warp, leg by leg

        /// <summary>How a leg covers its height (Up) or its ground distance (Along): evenly, setting off, coming to rest.</summary>
        private enum Ease : byte { Linear, In, Out, InOut }

        /// <summary>One stretch of a climb: where the feet go, how the body faces on arrival, how long it takes, how it moves.</summary>
        private struct Leg
        {
            public WallRunPhase Phase;
            public Vector3 ToFeet;
            public Quaternion Facing;
            public float Seconds;
            public Ease Up, Along;
        }

        /// <summary>
        /// Lays the climb out as legs and starts it. Each leg's easing is picked so the body never changes speed in a
        /// step: the plant ends rising at the climb's own speed, the climb goes on at it, going over the edge starts at
        /// it and comes to rest above the top while the ground travel sets off, and the move in over the top carries
        /// that on and settles. Motion Warping carries the body along them; Animancer fades clip into clip on the
        /// same clock (Show).
        /// </summary>
        private void StartRoute(bool fromJump)
        {
            legCount = 0;
            Vector3 start = Feet;
            float climbSpeed = WallRunRules.ClimbSpeed(resumeSpeed, settings);
            Quaternion upright = Facing(-plan.Normal);
            Quaternion leaned = upright * Quaternion.Euler(-settings.climbLean, 0f, 0f);

            // Plant: into the foot of the surface, the feet setting off up it.
            float plantSeconds = settings.plantSeconds;
            if (fromJump)
            {
                // Caught out of a jump: quick when it met the surface head-on, longer the wider it came at it, so the
                // turn to face it eases in rather than snapping.
                float slant = WallRunRules.ApproachAngle(transform.forward, plan.Normal);
                plantSeconds = Mathf.Lerp(Mathf.Min(settings.plantSeconds, 0.12f), settings.plantSeconds + 0.12f,
                    Mathf.Clamp01(slant / Mathf.Max(1f, settings.maxJumpApproachAngle)));
            }
            float plantRise = climbSpeed * plantSeconds * 0.5f; // eased in: ends rising at the climb's speed
            Vector3 plantTo = new Vector3(plan.BaseFeet.x, Mathf.Max(start.y, plan.BaseFeet.y) + plantRise, plan.BaseFeet.z);
            plantTo.y = Mathf.Min(plantTo.y, Mathf.Max(plan.ClimbEndFeet.y, plan.BaseFeet.y));
            AddLeg(WallRunPhase.Plant, plantTo, leaned, plantSeconds, Ease.In, Ease.Out); // leaned by the time the feet are on it

            // Up the face, evenly.
            Vector3 from = plantTo;
            float rise = plan.ClimbEndFeet.y - plantTo.y;
            if (rise > 0.05f)
            {
                AddLeg(WallRunPhase.Climb, plan.ClimbEndFeet, leaned, rise / climbSpeed, Ease.Linear, Ease.Linear);
                from = plan.ClimbEndFeet;
            }

            if (plan.HasTop)
            {
                WallRunPhase over = plan.Kind == ClimbableKind.Tree ? WallRunPhase.ToPerch : WallRunPhase.Mantle;
                Vector3 inward = plan.TopFeet - from;
                inward.y = 0f;
                float inDistance = inward.magnitude;
                Vector3 inDirection = inDistance > 1e-3f ? inward / inDistance : Vector3.zero;
                // Up past the edge, slowing to rest above the top, while the move in sets off.
                float liftIn = Mathf.Min(0.35f, inDistance * 0.3f);
                Vector3 lifted = new Vector3(from.x, Mathf.Max(from.y, plan.TopFeet.y) + settings.mantleClearance, from.z) + inDirection * liftIn;
                float liftSeconds = Mathf.Max(MinimumDuration, 2f * (lifted.y - from.y) / climbSpeed);
                AddLeg(over, lifted, upright, liftSeconds, Ease.Out, Ease.In);
                // In over the top at the pace the lift left off, settling onto it.
                float alongSpeed = 2f * liftIn / liftSeconds;
                float rest = Mathf.Max(0f, inDistance - liftIn);
                float overSeconds = alongSpeed > 0.1f ? 2f * rest / alongSpeed : settings.mantleSeconds;
                overSeconds = Mathf.Clamp(overSeconds, settings.mantleSeconds * 0.5f, settings.mantleSeconds * 1.5f);
                AddLeg(over, plan.TopFeet, upright, overSeconds, Ease.In, Ease.Out);
                afterRoute = plan.Kind == ClimbableKind.Tree ? WallRunPhase.Perched : WallRunPhase.None;
            }
            else
            {
                // No top in reach: kick off it, down to the ground in front of it.
                Vector3 outward = plan.Normal;
                outward.y = 0f;
                outward = outward.sqrMagnitude > 1e-4f ? outward.normalized : -transform.forward;
                Vector3 aim = from + outward * settings.kickOffDistance;
                Vector3 landing = OpenLanding(NavMesh.SamplePosition(new Vector3(aim.x, plan.BaseFeet.y, aim.z), out NavMeshHit hit, 2f, Filter()) ? hit.position : plan.BaseFeet);
                AddArc(WallRunPhase.KickOff, from, landing, settings.kickOffLift, Facing(outward));
                afterRoute = WallRunPhase.None;
            }
            PlayRoute();
        }

        private void AddLeg(WallRunPhase legPhase, Vector3 toFeet, Quaternion facing, float seconds, Ease up, Ease along)
        {
            if (legCount >= MaxLegs) return;
            legs[legCount++] = new Leg { Phase = legPhase, ToFeet = toFeet, Facing = facing, Seconds = Mathf.Max(MinimumDuration, seconds), Up = up, Along = along };
        }

        /// <summary>A fall under gravity from one place to another, over an apex <paramref name="lift"/> above the higher of them.</summary>
        private void AddArc(WallRunPhase legPhase, Vector3 from, Vector3 to, float lift, Quaternion facing)
        {
            float g = settings != null ? settings.dropGravity : 14f;
            float apexY = Mathf.Max(from.y, to.y) + Mathf.Max(0f, lift);
            float up = Mathf.Sqrt(2f * Mathf.Max(0f, apexY - from.y) / g), down = Mathf.Sqrt(2f * Mathf.Max(0.05f, apexY - to.y) / g);
            if (up >= MinimumDuration)
            {
                Vector3 apex = Vector3.Lerp(from, to, up / (up + down));
                apex.y = apexY;
                AddLeg(legPhase, apex, facing, up, Ease.Out, Ease.Linear);
            }
            AddLeg(legPhase, to, facing, Mathf.Max(0.3f, down), Ease.In, Ease.Linear);
        }

        private static Quaternion Facing(Vector3 direction)
        {
            direction.y = 0f;
            return direction.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(direction.normalized, Vector3.up) : Quaternion.identity;
        }

        private void PlayRoute()
        {
            if (legCount == 0) return;
            routeClock = 0f;
            legIndex = -1;
            legFromFeet = Feet;
            routeActive = true;
            if (warp != null && warpAsset != null)
            {
                ShapeWarp();
                var points = new WarpPoint[legCount];
                for (int i = 0; i < legCount; i++) points[i] = new WarpPoint { position = legs[i].ToFeet + legs[i].Facing * Vector3.up * Offset, rotation = legs[i].Facing };
                warp.Play(warpAsset, points);
                warpOwned = true;
            }
            EnterLeg(0);
        }

        /// <summary>
        /// Writes the route into the warp: a phase per leg, aimed at its end, and root curves that rise by one over each
        /// leg in that leg's easing. With nothing linear and no animation added, the warp moves the body the share of
        /// the way the curve has covered, so the easing is exactly the leg's.
        /// </summary>
        private void ShapeWarp()
        {
            var along = new List<Keyframe>(legCount * (EaseSamples + 1));
            var up = new List<Keyframe>(legCount * (EaseSamples + 1));
            warpAsset.warpPhases ??= new List<WarpPhase>(MaxLegs);
            warpAsset.warpPhases.Clear();
            float t = 0f;
            for (int i = 0; i < legCount; i++)
            {
                AppendEase(along, t, legs[i].Seconds, i, legs[i].Along);
                AppendEase(up, t, legs[i].Seconds, i, legs[i].Up);
                warpAsset.warpPhases.Add(new WarpPhase
                {
                    // The first just before zero, so it is entered on the warp's first update rather than its second.
                    startTime = i == 0 ? -0.001f : t,
                    endTime = t + legs[i].Seconds,
                    minRate = 1f,
                    maxRate = 1f,
                    totalRootMotion = Vector3.one,
                });
                t += legs[i].Seconds;
            }
            warpAsset.rootX = new AnimationCurve(along.ToArray());
            warpAsset.rootZ = new AnimationCurve(along.ToArray());
            warpAsset.rootY = new AnimationCurve(up.ToArray());
            warpAsset.useLinear = new VectorBool(false, false, false);
            warpAsset.useAnimation = new VectorBool(false, false, false);
            warpAsset.useWarping = VectorBool.Enabled;
            warpAsset.useCollision = false;
            warpAsset.playRateBasis = 1f;
            warpAsset.phasesAmount = legCount;
        }

        private static void AppendEase(List<Keyframe> keys, float start, float seconds, int index, Ease ease)
        {
            for (int j = 0; j <= EaseSamples; j++)
            {
                float s = j / (float)EaseSamples;
                float slope = EaseSlope(ease, s) / seconds;
                if (j == 0 && keys.Count > 0)
                {
                    // The leg before ends here at the same value: keep its way in, take this leg's way out.
                    Keyframe last = keys[keys.Count - 1];
                    last.outTangent = slope;
                    keys[keys.Count - 1] = last;
                    continue;
                }
                keys.Add(new Keyframe(start + s * seconds, index + EaseValue(ease, s), slope, slope));
            }
        }

        private static float EaseValue(Ease ease, float s)
        {
            switch (ease)
            {
                case Ease.In: return s * s;
                case Ease.Out: return 1f - (1f - s) * (1f - s);
                case Ease.InOut: return s * s * (3f - 2f * s);
                default: return s;
            }
        }

        private static float EaseSlope(Ease ease, float s)
        {
            switch (ease)
            {
                case Ease.In: return 2f * s;
                case Ease.Out: return 2f * (1f - s);
                case Ease.InOut: return 6f * s * (1f - s);
                default: return 1f;
            }
        }

        private void Update()
        {
            if (!HasAuthority) return;
            if (armed) { WatchArmed(); return; }
            if (wallArmed) { WatchWallArmed(); return; }
            if (!routeActive && IsOnWallPhase(phase)) { UpdateWall(); return; }
            if (!routeActive)
            {
                // A player standing in a tree that is no longer there (out of range of every proxy) drops to the ground.
                if (phase == WallRunPhase.Perched && !PerchStillStands()) FallFromPerch();
                return;
            }
            if (resources != null && !resources.IsAlive) return; // the death motor takes the body (ReleaseForDeath)

            routeClock += Time.deltaTime;
            float legStart = 0f;
            int index = 0;
            for (; index < legCount; index++)
            {
                if (routeClock < legStart + legs[index].Seconds) break;
                legStart += legs[index].Seconds;
            }
            if (index >= legCount) { EndRoute(); return; }
            if (index != legIndex) EnterLeg(index);
            if (!warpOwned)
            {
                // No warp on this character: the same legs, moved here.
                Leg leg = legs[index];
                float s = Mathf.Clamp01((routeClock - legStart) / leg.Seconds);
                float a = EaseValue(leg.Along, s), u = EaseValue(leg.Up, s);
                Vector3 feet = new Vector3(Mathf.LerpUnclamped(legFromFeet.x, leg.ToFeet.x, a), Mathf.LerpUnclamped(legFromFeet.y, leg.ToFeet.y, u),
                    Mathf.LerpUnclamped(legFromFeet.z, leg.ToFeet.z, a));
                Quaternion turned = Quaternion.Slerp(transform.rotation, leg.Facing, s);
                transform.SetPositionAndRotation(feet + turned * Vector3.up * Offset, turned);
            }
        }

        private void EnterLeg(int index)
        {
            if (legIndex >= 0 && legIndex < legCount) legFromFeet = legs[legIndex].ToFeet;
            legIndex = index;
            Leg leg = legs[index];
            if (leg.Phase == phase) return;
            // The clip is fitted to every leg of the phase together (going over an edge is two legs).
            float seconds = 0f;
            for (int i = index; i < legCount && legs[i].Phase == leg.Phase; i++) seconds += legs[i].Seconds;
            SetPhase(leg.Phase, seconds);
        }

        private void EndRoute()
        {
            routeActive = false;
            StopWarp();
            Leg last = legs[legCount - 1];
            if (afterRoute == WallRunPhase.WallMove) { BeginWallMove(last); return; }
            if (afterRoute == WallRunPhase.Perched)
            {
                transform.SetPositionAndRotation(last.ToFeet + Vector3.up * Offset, Quaternion.Euler(0f, last.Facing.eulerAngles.y, 0f));
                SetPhase(WallRunPhase.Perched, 0f);
                return;
            }
            Land(last.ToFeet);
        }

        private void StopWarp()
        {
            if (warpOwned && warp != null && warp.IsActive()) warp.Stop();
            warpOwned = false;
        }

        private void FallFromPerch()
        {
            Vector3 below = OpenLanding(NavMesh.SamplePosition(Feet, out NavMeshHit hit, settings.maxClimbHeight + 2f, Filter()) ? hit.position : plan.BaseFeet);
            legCount = 0;
            AddArc(WallRunPhase.DropDown, Feet, below, 0f, Facing(transform.forward));
            afterRoute = WallRunPhase.None;
            PlayRoute();
        }

        private void Land(Vector3 feet)
        {
            transform.SetPositionAndRotation(feet + Vector3.up * Offset, Quaternion.Euler(0f, transform.eulerAngles.y, 0f));
            agent.enabled = true;
            if (!agent.isOnNavMesh && NavMesh.SamplePosition(feet, out NavMeshHit hit, 1.5f, Filter()))
            {
                agent.enabled = false;
                transform.position = hit.position + Vector3.up * Offset;
                agent.enabled = true;
            }
            if (agent.isOnNavMesh)
            {
                agent.ResetPath();
                agent.isStopped = false;
            }
            else agent.enabled = false; // PointAndClickPlayer puts a lost agent back after a moment
            SetPhase(WallRunPhase.None, 0f);
            WalkOnAfterLanding();
        }

        /// <summary>The phase every machine shows (the warp, not this, moves the body).</summary>
        private void SetPhase(WallRunPhase next, float seconds)
        {
            phase = next;
            var pose = new WallRunPose { Phase = next, Yaw = transform.eulerAngles.y, Duration = seconds };
            if (IsSpawned) netPose.Value = pose;
            Show(pose);
        }

        private void WatchArmed()
        {
            if (agent == null || !agent.isActiveAndEnabled || !agent.isOnNavMesh) { armed = false; return; }
            // Another order took the route over.
            if (Time.time - armedAt > RearmSeconds && Flat(agent.destination, armedPlan.BaseFeet) > 0.6f) { armed = false; return; }
            if (Flat(Feet, armedPlan.BaseFeet) > ArmTrigger + agent.stoppingDistance + agent.velocity.magnitude * Time.deltaTime * 2f) return;
            Begin(armedPlan);
        }

        private bool PerchStillStands()
        {
            int count = Physics.OverlapSphereNonAlloc(plan.TopFeet, Radius + 1.5f, Overlaps, SurfaceMask(), QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (Overlaps[i] != null && Overlaps[i].GetComponentInParent<ClimbableSurface>() != null) return true;
            return false;
        }

        private static readonly Collider[] Overlaps = new Collider[8];

        // ------------------------------------------------------------------ death, and being taken out of it

        /// <summary>
        /// Killed on a wall or in a tree (or on the way up): let go of the body, pushed out from the surface, so it
        /// falls clear of it. The death motor brings it down from there.
        /// </summary>
        public void ReleaseForDeath(bool pushOff = true)
        {
            armed = false;
            catchPending = false;
            bool onWall = IsOnWallPhase(phase);
            ForgetWall();
            if (!HasAuthority || phase == WallRunPhase.None) return;
            routeActive = false;
            StopWarp();
            if (onWall && !pushOff)
            {
                // Killed standing on a wall: the body stays where it is, as it is (the de-rez takes it there).
                phase = WallRunPhase.None;
                if (IsSpawned) netPose.Value = default;
                Show(default);
                return;
            }
            Vector3 outFrom = new Vector3(plan.Normal.x, 0f, plan.Normal.z);
            if (pushOff && outFrom.sqrMagnitude > 1e-4f && settings != null)
                transform.position += outFrom.normalized * settings.deathPushOff;
            transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f); // upright again off the lean
            phase = WallRunPhase.None;
            if (IsSpawned) netPose.Value = default;
            Show(default);
        }

        /// <summary>Stops a climb where it is and puts the agent back (a GM reset, a map change).</summary>
        public void Cancel()
        {
            armed = false;
            catchPending = false;
            ForgetWall();
            if (!HasAuthority || phase == WallRunPhase.None) return;
            Vector3 ground = OpenLanding(NavMesh.SamplePosition(Feet, out NavMeshHit hit, settings != null ? settings.maxClimbHeight + 2f : 16f, Filter()) ? hit.position : plan.BaseFeet);
            routeActive = false;
            StopWarp();
            Land(ground);
        }

        // ------------------------------------------------------------------ what every machine plays

        private void OnPoseChanged(WallRunPose previous, WallRunPose current)
        {
            if (IsServer) return; // the server shows its own as it sets it
            Show(current);
        }

        private void Show(WallRunPose pose)
        {
            if (pose.Phase == shownPhase) return;
            shownPhase = pose.Phase;
            var feel = ClientPresentationRegistry.Feel;
            if (animancerMotor == null) return;
            switch (pose.Phase)
            {
                case WallRunPhase.None:
                    if (playingAction) animancerMotor.EndWarpedAction(fadeOut, resumeSpeed);
                    else animancerMotor.ResetJumpAnimation();
                    playingAction = false;
                    if (feel != null) { feel.Airborne(transform, false); feel.Landed(transform, !IsSpawned || IsOwner); }
                    break;
                case WallRunPhase.Plant:
                    Play(plantClip, pose.Duration, fallbackAirborne: true);
                    if (feel != null) { feel.JumpTakeoff(transform, !IsSpawned || IsOwner); feel.Airborne(transform, true); }
                    break;
                case WallRunPhase.Climb:
                    Play(climbClip, 0f, fallbackAirborne: true, speed: climbClipSpeed);
                    break;
                case WallRunPhase.Mantle:
                case WallRunPhase.ToPerch:
                    Play(mantleClip, pose.Duration, fallbackAirborne: false);
                    break;
                case WallRunPhase.Perched:
                    Play(perchClip, 0f, fallbackAirborne: false);
                    if (feel != null) feel.Airborne(transform, false);
                    break;
                case WallRunPhase.WallMove:
                case WallRunPhase.WallHold:
                    // Standing on the wall: the game's own idle, walk and run play from the body's travel.
                    if (playingAction) animancerMotor.EndWarpedAction(fadeOut, pose.Phase == WallRunPhase.WallMove ? GroundSpeed() : 0f);
                    else animancerMotor.ResetJumpAnimation();
                    playingAction = false;
                    if (feel != null) feel.Airborne(transform, false);
                    break;
                case WallRunPhase.PushOff:
                    Play(animancerMotor.WarpedJumpClip(1), pose.Duration, fallbackAirborne: true);
                    if (feel != null) { feel.JumpTakeoff(transform, !IsSpawned || IsOwner); feel.Airborne(transform, true); }
                    break;
                default:
                    Play(dropClip, 0f, fallbackAirborne: true);
                    if (feel != null) feel.Airborne(transform, true);
                    break;
            }
        }

        /// <summary>Plays a clip over the body (fitted to <paramref name="seconds"/> when given), or the jump's airborne pose.</summary>
        private void Play(AnimationClip clip, float seconds, bool fallbackAirborne, float speed = 1f)
        {
            if (clip != null)
            {
                float rate = seconds > MinimumDuration ? clip.length / seconds : speed;
                animancerMotor.PlayWarpedAction(clip, 0f, rate, IsSpawned && !IsServer ? RemoteFade : fadeIn);
                playingAction = true;
            }
            else if (fallbackAirborne) animancerMotor.PlayJumpAirborne();
        }

        // ------------------------------------------------------------------ helpers

        private Collider FindSurface(Vector3 point, Vector3 normal)
        {
            Vector3 n = normal.sqrMagnitude > 1e-4f ? normal.normalized : Vector3.up;
            int count = Physics.RaycastNonAlloc(point + n * 0.5f, -n, Hits, 1f, SurfaceMask(), QueryTriggerInteraction.Ignore);
            return Nearest(count, out RaycastHit hit) ? hit.collider : null;
        }

        private bool Nearest(int count, out RaycastHit nearest)
        {
            nearest = default;
            float best = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                if (Hits[i].collider == null || Hits[i].collider.transform.IsChildOf(transform)) continue;
                if (Hits[i].distance >= best) continue;
                best = Hits[i].distance;
                nearest = Hits[i];
            }
            return best < float.PositiveInfinity;
        }

        /// <summary>Every layer a surface can be on: the wall layers, and whatever a tree proxy uses.</summary>
        private int SurfaceMask() => (settings != null ? settings.wallLayers.value : 1) | 1;

        private int GroundMask() => Physics.DefaultRaycastLayers & ~(1 << gameObject.layer);

        private bool BodyBlocked(Vector3 feet, Collider ignore)
        {
            Vector3 bottom = feet + Vector3.up * (Radius + 0.1f), top = feet + Vector3.up * Mathf.Max(Radius + 0.1f, (agent != null ? agent.height : 2f) - Radius);
            int count = Physics.OverlapCapsuleNonAlloc(bottom, top, Radius - BodySkin, Overlaps, GroundMask(), QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider c = Overlaps[i];
                if (c == null || c == ignore || c.transform.IsChildOf(transform)) continue;
                if (c.GetComponentInParent<IDamageable>() != null) continue; // another character steps aside
                return true;
            }
            return false;
        }

        /// <summary>
        /// False for a place inside a building: the town's buildings have walkable floors inside them (their own NavMesh,
        /// not joined to the street), and a climb must never start, end or land in one. Inside is a ceiling overhead
        /// with walls round on at least three sides; an awning over the street has no walls round it.
        /// </summary>
        private bool OnOpenGround(Vector3 feet)
        {
            int mask = GroundMask();
            Vector3 head = feet + Vector3.up * ((agent != null ? agent.height : 2f) + 0.2f);
            if (!Physics.Raycast(head, Vector3.up, IndoorProbe, mask, QueryTriggerInteraction.Ignore)) return true;
            int walls = 0;
            Vector3 chest = feet + Vector3.up * 1f;
            for (int i = 0; i < 4; i++)
            {
                Vector3 direction = Quaternion.Euler(0f, i * 90f, 0f) * Vector3.forward;
                if (Physics.Raycast(chest, direction, IndoorProbe, mask, QueryTriggerInteraction.Ignore)) walls++;
            }
            return walls < 3;
        }

        private const float IndoorProbe = 6f;

        /// <summary>A landing on open ground: the place asked for, or the climb's own foot when that one is indoors.</summary>
        private Vector3 OpenLanding(Vector3 wanted) => OnOpenGround(wanted) ? wanted : plan.BaseFeet;

        private NavMeshQueryFilter Filter() => new NavMeshQueryFilter { agentTypeID = agent != null ? agent.agentTypeID : 0, areaMask = agent != null ? agent.areaMask : NavMesh.AllAreas };

        private float GroundSpeed() => movementSkills != null ? movementSkills.CurrentSpeed : 6f;

        private float FallSeconds(float peakY, float endY)
        {
            float g = settings != null ? settings.dropGravity : 14f;
            float up = Mathf.Max(0f, peakY - Feet.y), down = Mathf.Max(0.05f, peakY - endY);
            return Mathf.Max(0.3f, Mathf.Sqrt(2f * up / g) + Mathf.Sqrt(2f * down / g));
        }

        private static float Flat(Vector3 a, Vector3 b)
        {
            a.y = b.y = 0f;
            return Vector3.Distance(a, b);
        }

        private static float PathLength(NavMeshPath path)
        {
            float length = 0f;
            Vector3[] corners = path.corners;
            for (int i = 1; i < corners.Length; i++) length += Vector3.Distance(corners[i - 1], corners[i]);
            return length;
        }
    }
}
