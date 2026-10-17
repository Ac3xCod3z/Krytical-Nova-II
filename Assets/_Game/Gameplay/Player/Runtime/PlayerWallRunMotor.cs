using System;
using System.Collections.Generic;
using KryticalNova.Core;
using KryticalNova.Progression;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

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
    public sealed class PlayerWallRunMotor : NetworkBehaviour
    {
        private const float WallScanStep = 0.25f;
        private const float WallScanOut = 0.6f;
        private const float WallScanLength = 1.2f;
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
        private float clock, duration;
        private Vector3 fromFeet, toFeet;
        private float arcLift;
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
        private Vector3 Feet => transform.position - Vector3.up * Offset;

        private void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            movementSkills = GetComponent<PlayerMovementSkills>();
            resources = GetComponent<PlayerResources>();
            progression = GetComponent<PlayerCharacterProgression>();
            animancerMotor = GetComponent<PlayerAnimancerMotor>();
            jumpMotor = GetComponent<PlayerJumpMotor>();
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
            if (!HasAuthority)
            {
                // Checked here first, so a bad click is refused at once.
                bool jump = movementSkills != null && movementSkills.IsJumpSelected;
                if (!Decide(point, normal, surface, Feet, fromJump: jump, out _, out string early)) { Message?.Invoke(early); return true; }
                RequestClimbServerRpc(point, normal, obstacleLayers, modelForwardOffset);
                return true;
            }
            if (!ServerClimbClicked(point, normal, surface, obstacleLayers, modelForwardOffset, out string reason)) Message?.Invoke(reason);
            return true;
        }

        [ServerRpc]
        private void RequestClimbServerRpc(Vector3 point, Vector3 normal, int obstacleLayers, float modelForwardOffset)
        {
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
            if (agent != null) agent.enabled = false; // already off for the jump; the motor owns the root until the top
            StartPhase(WallRunPhase.Plant, Feet, Feet, Mathf.Min(settings.plantSeconds, 0.12f));
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

        [ServerRpc]
        private void RequestDropServerRpc(Vector3 point)
        {
            if (!ServerDropTo(point, out string reason)) ClimbRefusedRpc(reason);
        }

        private bool ServerDropTo(Vector3 point, out string reason)
        {
            reason = string.Empty;
            if (phase != WallRunPhase.Perched || settings == null) return false;
            var filter = Filter();
            if (!NavMesh.SamplePosition(point, out NavMeshHit landing, 1.5f, filter)) { reason = "Choose a walkable place to drop down to."; return false; }
            Vector3 flat = landing.position - Feet;
            flat.y = 0f;
            if (flat.magnitude > settings.maxDropDistance) { reason = $"Too far: drop within {settings.maxDropDistance:0} m of the tree."; return false; }
            arcLift = 0.4f;
            StartPhase(WallRunPhase.DropDown, Feet, landing.position, FallSeconds(Feet.y + arcLift, landing.position.y));
            return true;
        }

        // ------------------------------------------------------------------ deciding (every machine can ask)

        /// <summary>The canRunUpWall check of one surface, and the climb it would be.</summary>
        private bool Decide(Vector3 point, Vector3 normal, Collider surface, Vector3 fromFeet, bool fromJump, out Plan climb, out string reason)
        {
            climb = default;
            bool eligible = IsClimbableObject(surface, out ClimbableSurface marker);
            ClimbableKind kind = marker != null ? marker.kind : ClimbableKind.Wall;
            bool stride = fromJump || (movementSkills != null && (movementSkills.IsRunning || movementSkills.IsJumpSelected));
            bool shaped = eligible && kind != ClimbableKind.Never && Shape(point, normal, kind, marker, out climb);
            var query = new ClimbQuery
            {
                Unlocked = WallstrideOpen,
                Busy = resources == null || !resources.IsAlive || IsBusy || (!fromJump && jumpMotor != null && jumpMotor.IsBusy),
                Stride = stride,
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
        private bool Shape(Vector3 point, Vector3 normal, ClimbableKind kind, ClimbableSurface marker, out Plan climb)
        {
            climb = new Plan { Kind = kind };
            if (settings == null) return false;
            var filter = Filter();
            float stand = Radius + BodySkin;

            if (kind == ClimbableKind.Tree && marker != null)
            {
                Vector3 trunk = marker.TrunkBase;
                Vector3 side = point - trunk;
                side.y = 0f;
                if (side.sqrMagnitude < 1e-4f) side = -transform.forward;
                side.Normalize();
                climb.Normal = side;
                Vector3 foot = trunk + side * (marker.trunkRadius + stand);
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
            Vector3 standAt = wallFoot + n * stand;
            if (!NavMesh.SamplePosition(standAt, out NavMeshHit baseHit, BaseSampleRadius, filter)) return false;
            climb.BaseFeet = new Vector3(standAt.x, baseHit.position.y, standAt.z);

            // Up the face until it ends: that is the edge.
            float edge = -1f;
            int mask = SurfaceMask();
            for (float h = WallScanStep; h <= settings.maxClimbHeight + WallScanStep; h += WallScanStep)
            {
                Vector3 origin = wallFoot + Vector3.up * h + n * WallScanOut;
                if (Physics.Raycast(origin, -n, WallScanLength, mask, QueryTriggerInteraction.Ignore)) continue;
                edge = h;
                break;
            }
            climb.Height = edge < 0f ? settings.maxClimbHeight + 1f : edge;
            if (edge < 0f) return true; // runs on past the reach: too high

            // The walkable top, just over the edge.
            Vector3 over = wallFoot - n * settings.mantleReach + Vector3.up * (edge + 0.3f);
            if (NavMesh.SamplePosition(over, out NavMeshHit top, settings.topSampleRadius + 0.6f, filter)
                && Mathf.Abs(top.position.y - (wallFoot.y + edge)) <= 0.8f)
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
            agent.enabled = false; // the motor owns the root until the top
            StartPhase(WallRunPhase.Plant, Feet, climb.BaseFeet, settings.plantSeconds);
        }

        private bool Pay() => settings != null && resources != null && (settings.fluxCost <= 0f || resources.TrySpendChi(settings.fluxCost));

        private void Update()
        {
            if (!HasAuthority) return;
            if (armed) { WatchArmed(); return; }
            if (phase == WallRunPhase.None || phase == WallRunPhase.Perched)
            {
                // A player standing in a tree that is no longer there (out of range of every proxy) drops to the ground.
                if (phase == WallRunPhase.Perched && !PerchStillStands()) FallFromPerch();
                return;
            }
            if (resources != null && !resources.IsAlive) return; // the death motor takes the body (ReleaseForDeath)

            clock += Time.deltaTime;
            float t = duration > 0f ? Mathf.Clamp01(clock / duration) : 1f;
            Vector3 feet;
            switch (phase)
            {
                case WallRunPhase.Plant:
                    feet = Vector3.Lerp(fromFeet, toFeet, Smooth(t));
                    break;
                case WallRunPhase.Climb:
                    feet = Vector3.Lerp(fromFeet, toFeet, t);
                    break;
                case WallRunPhase.Mantle:
                case WallRunPhase.ToPerch:
                    feet = Vector3.Lerp(fromFeet, toFeet, Smooth(t)) + Vector3.up * (Mathf.Sin(t * Mathf.PI) * arcLift);
                    break;
                default: // KickOff, DropDown: an arc under gravity
                    feet = Arc(t);
                    break;
            }
            transform.position = feet + Vector3.up * Offset;
            if (t >= 1f) NextPhase();
        }

        private void NextPhase()
        {
            switch (phase)
            {
                case WallRunPhase.Plant:
                    StartPhase(WallRunPhase.Climb, Feet, plan.ClimbEndFeet,
                        Mathf.Max(MinimumDuration, (plan.ClimbEndFeet.y - Feet.y) / WallRunRules.ClimbSpeed(resumeSpeed, settings)));
                    break;
                case WallRunPhase.Climb:
                    arcLift = settings.mantleLift;
                    if (plan.Kind == ClimbableKind.Tree) StartPhase(WallRunPhase.ToPerch, Feet, plan.TopFeet, settings.mantleSeconds);
                    else if (plan.HasTop) StartPhase(WallRunPhase.Mantle, Feet, plan.TopFeet, settings.mantleSeconds);
                    else KickOff();
                    break;
                case WallRunPhase.Mantle:
                    Land(plan.TopFeet);
                    break;
                case WallRunPhase.ToPerch:
                    StartPhase(WallRunPhase.Perched, plan.TopFeet, plan.TopFeet, 0f);
                    break;
                case WallRunPhase.KickOff:
                case WallRunPhase.DropDown:
                    Land(toFeet);
                    break;
            }
        }

        private void KickOff()
        {
            Vector3 out_ = new Vector3(plan.Normal.x, 0f, plan.Normal.z).normalized;
            Vector3 aim = Feet + out_ * settings.kickOffDistance;
            Vector3 landing = NavMesh.SamplePosition(new Vector3(aim.x, plan.BaseFeet.y, aim.z), out NavMeshHit hit, 2f, Filter()) ? hit.position : plan.BaseFeet;
            arcLift = settings.kickOffLift;
            StartPhase(WallRunPhase.KickOff, Feet, landing, FallSeconds(Feet.y + arcLift, landing.y));
        }

        private void FallFromPerch()
        {
            Vector3 below = NavMesh.SamplePosition(Feet, out NavMeshHit hit, settings.maxClimbHeight + 2f, Filter()) ? hit.position : plan.BaseFeet;
            arcLift = 0f;
            StartPhase(WallRunPhase.DropDown, Feet, below, FallSeconds(Feet.y, below.y));
        }

        private void Land(Vector3 feet)
        {
            transform.position = feet + Vector3.up * Offset;
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
            StartPhase(WallRunPhase.None, feet, feet, 0f);
        }

        private void StartPhase(WallRunPhase next, Vector3 from, Vector3 to, float seconds)
        {
            phase = next;
            fromFeet = from;
            toFeet = to;
            clock = 0f;
            duration = seconds;
            Vector3 facing = next == WallRunPhase.KickOff || next == WallRunPhase.DropDown ? to - from : -plan.Normal;
            facing.y = 0f;
            float yaw = facing.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(facing, Vector3.up).eulerAngles.y : transform.eulerAngles.y;
            if (next != WallRunPhase.None) transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            var pose = new WallRunPose { Phase = next, Yaw = yaw, Duration = seconds };
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
        public void ReleaseForDeath()
        {
            armed = false;
            catchPending = false;
            if (!HasAuthority || phase == WallRunPhase.None) return;
            Vector3 outFrom = new Vector3(plan.Normal.x, 0f, plan.Normal.z);
            if (outFrom.sqrMagnitude > 1e-4f && settings != null)
                transform.position += outFrom.normalized * settings.deathPushOff;
            phase = WallRunPhase.None;
            if (IsSpawned) netPose.Value = default;
            Show(default);
        }

        /// <summary>Stops a climb where it is and puts the agent back (a GM reset, a map change).</summary>
        public void Cancel()
        {
            armed = false;
            catchPending = false;
            if (!HasAuthority || phase == WallRunPhase.None) return;
            Vector3 ground = NavMesh.SamplePosition(Feet, out NavMeshHit hit, settings != null ? settings.maxClimbHeight + 2f : 16f, Filter()) ? hit.position : plan.BaseFeet;
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

        private NavMeshQueryFilter Filter() => new NavMeshQueryFilter { agentTypeID = agent != null ? agent.agentTypeID : 0, areaMask = agent != null ? agent.areaMask : NavMesh.AllAreas };

        private float GroundSpeed() => movementSkills != null ? movementSkills.CurrentSpeed : 6f;

        private float FallSeconds(float peakY, float endY)
        {
            float g = settings != null ? settings.dropGravity : 14f;
            float up = Mathf.Max(0f, peakY - Feet.y), down = Mathf.Max(0.05f, peakY - endY);
            return Mathf.Max(0.3f, Mathf.Sqrt(2f * up / g) + Mathf.Sqrt(2f * down / g));
        }

        private Vector3 Arc(float t)
        {
            Vector3 feet = Vector3.Lerp(fromFeet, toFeet, t);
            feet.y = Mathf.Lerp(fromFeet.y, toFeet.y, t * t) + Mathf.Sin(t * Mathf.PI) * arcLift;
            return feet;
        }

        private static float Smooth(float t) => t * t * (3f - 2f * t);

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
