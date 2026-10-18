using System;
using System.Collections.Generic;
using Kinemation.MotionWarping.Runtime.Core;
using Kinemation.MotionWarping.Runtime.Utility;
using KryticalNova.CharacterCreation;
using KryticalNova.Core;
using KryticalNova.Factions;
using KryticalNova.Progression;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using WarpComponent = Kinemation.MotionWarping.Runtime.Core.MotionWarping;

namespace KryticalNova.Player
{
    /// <summary>
    /// Vaults low obstacles that stand in the way of a move command, so the character goes over a bench, a barrier, a
    /// dumpster or a hedge instead of walking round it. Three height classes (<see cref="TraversalSettings"/>), each
    /// with its own animation, and the animations are the character's faction's and body type's own where the map
    /// has a set for them (<see cref="VaultAnimationMap"/>; the default set otherwise: a hurdle for low obstacles, a
    /// hands-on leapfrog for middling ones and a front flip for the tallest). Built like the
    /// jump: Animancer plays the pose and KINEMATION Motion Warping carries the root, both on one clock, from wherever
    /// the character is when it reaches the obstacle to a landing point on the NavMesh behind it.
    ///
    /// Going over is the rule, not the exception: whenever the straight line to the destination is blocked by
    /// something that can be vaulted and that way is no longer than walking round, the character vaults, so it never
    /// swerves round an obstacle it could clear. The move command asks <see cref="TryArm"/> for such a vault; if there
    /// is one the agent is sent to its take-off point, the vault starts as the agent gets there (without stopping),
    /// and <see cref="Landed"/> hands the rest of the journey back to the caller. A move that starts without one (a
    /// building is in the way first) is watched with <see cref="Watch"/> and gets its vault once the line has
    /// cleared. Planned and run where movement is authoritative; other peers replay it, and pick the same
    /// animation from the obstacle's height and the player's replicated faction and body type.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NavMeshAgent), typeof(PlayerMovementSkills), typeof(PlayerResources))]
    public sealed class PlayerVaultMotor : NetworkBehaviour
    {
        private const float TopSampleStep = 0.15f;
        /// <summary>How far past the NavMesh edge the obstacle may stand (a flower bed in front of a hedge is not walkable).</summary>
        private const float FaceProbeLength = 3.2f;
        /// <summary>Farthest a vault may start from the obstacle when the ground in front of it cannot be walked on.</summary>
        private const float MaximumRunUp = 3f;
        /// <summary>How far past the usual landing distance the landing may move to find walkable ground.</summary>
        private const float LandingSearch = 2.4f;
        private const float LandingSearchStep = 0.3f;
        /// <summary>How far to the side of the crossing point the character may stand and still vault from where it is.</summary>
        private const float StandingStartAside = 0.5f;
        /// <summary>A surface this close to the ground level is ground again: the obstacle has ended.</summary>
        private const float GroundTolerance = 0.12f;
        /// <summary>Tallest thing the body may come down over between the obstacle and the landing.</summary>
        private const float StepHeight = 0.3f;
        private const float MinimumRaise = 0.05f;
        private const float EdgeLead = 0.25f;
        private const float TriggerRadius = 0.3f;
        private const float RearmSeconds = 0.15f;
        private const float WatchSeconds = 0.25f;
        private const float CollisionSkin = 0.04f;

        [Header("What can be vaulted (heights above the feet)")]
        [Tooltip("Lowest obstacle that is vaulted (anything the NavMesh cuts round, down to a low crate).")]
        [SerializeField, Min(0.1f)] private float minimumHeight = 0.18f;
        [Tooltip("Tallest obstacle that can be vaulted. 2.2 m (owner, 17 Oct 2026) meets Wallstride, which runs up anything from " +
                 "2.2 m (WallstrideSettings.minWallHeight), so no height is left that can be neither vaulted nor climbed.")]
        [SerializeField, Min(0.2f)] private float maximumHeight = 2.2f;
        [Tooltip("Thickest obstacle, measured along the vault. 1.8 m takes a hedge or a dumpster across its short side only.")]
        [SerializeField, Min(0.1f)] private float maximumThickness = 1.8f;
        [Tooltip("How far from square-on the move may meet the obstacle. The vault itself always goes straight across; " +
                 "this only rules out moves that run along the obstacle rather than through it.")]
        [SerializeField, Range(5f, 85f)] private float maximumApproachAngle = 80f;
        [Tooltip("Going over must be at least this much shorter than walking round. 0 = vault whenever it is not the " +
                 "longer way, so the character never swerves round something it can clear.")]
        [SerializeField, Min(0f)] private float minimumDistanceSaved;
        [Tooltip("The landing may be this much higher or lower than the take-off.")]
        [SerializeField, Min(0f)] private float maximumLandingStep = 0.5f;

        [Header("Animations (built by Krytical Nova/Player/Build Vault Warp Asset)")]
        [Tooltip("Gap kept between the body and the top of the obstacle.")]
        [SerializeField, Min(0f)] private float clearance = 0.12f;
        [Tooltip("Which set of vault animations a faction and body type uses, and the default set.")]
        [SerializeField] private VaultAnimationMap animations;
        [Tooltip("The heights that sort an obstacle into the low, middle or tall style.")]
        [SerializeField] private TraversalSettings traversal;
        [SerializeField, Min(0.01f)] private float fadeIn = 0.08f;
        [SerializeField, Min(0.01f)] private float fadeOut = 0.15f;

        [Header("Motion Warping (built by Krytical Nova/Player/Build Vault Warp Asset)")]
        [Tooltip("Template: which axes are warped. The root height curve and the phase are written for each vault.")]
        [SerializeField] private MotionWarpingAsset warpTemplate;
        [Tooltip("The warp component the jump uses (its own child object, Transform To Warp = this player).")]
        [SerializeField] private WarpComponent motionWarping;

        internal struct Plan
        {
            public Vector3 Takeoff, Landing, NearEdge, FarEdge, Destination;
            public float Top, FacingOffset;
        }

        /// <summary>What the motor has planned or is watching for at one moment (<see cref="Keep"/>, <see cref="PutBack"/>).</summary>
        public struct Standing
        {
            internal bool Armed, Watching;
            internal Plan Plan;
            internal float ArmedAt, NextWatch, WatchFacingOffset;
            internal Vector3 WatchDestination;
            internal LayerMask WatchLayers;
        }

        private NavMeshAgent agent;
        private PlayerMovementSkills movementSkills;
        private PlayerResources resources;
        private PlayerAnimancerMotor animancerMotor;
        private PlayerMoveAnticipation moveAnticipation;
        private PlayerJumpMotor jumpMotor;
        private PlayerIdentity identity;
        private NavMeshPath scratchPath;
        private MotionWarpingAsset warpAsset;

        private bool armed;
        private Plan plan;
        private float armedAt;
        private bool watching;
        private Vector3 watchDestination;
        private LayerMask watchLayers;
        private float watchFacingOffset;
        private float nextWatch;
        private bool vaulting;
        private float clock, airTime, travelSpeed;
        private Vector3 takeoffRoot, landingFeet;
        private bool cancelling;

        /// <summary>The body is in the air over an obstacle.</summary>
        private const float RemoteLandingGrace = 0.6f;
        private const float RemoteLandingTolerance = 0.35f;
        // Shown here, simulated elsewhere: the vault starts when the body shown here reaches the server's take-off point
        // (it comes in through NetworkTransform a little later), and at the latest this long after the server's start.
        private const float RemoteTakeoffTolerance = 0.2f;
        private const float RemoteTakeoffWaitMax = 0.35f;

        // A vault the server has started, waiting here for the body to reach its take-off point.
        private bool remotePending;
        private Plan remotePlan;
        private float remoteSpeed;
        private Vector3 remoteTakeoffRoot;
        private float remotePendingSince;

        public bool IsBusy => vaulting;
        /// <summary>The vault has landed (authority only); the argument is where the move was heading.</summary>
        public event Action<Vector3> Landed;
        /// <summary>Why the last move did or did not get a vault (for tuning the limits).</summary>
        public string LastDecision { get; private set; } = string.Empty;

        private bool Ready => warpTemplate != null && motionWarping != null && animancerMotor != null
            && animations != null && animations.IsUsable && traversal != null && warpTemplate.animation != null;

        /// <summary>
        /// The style for an obstacle this tall: the character's faction's and body's own for that height, or the
        /// default set's. Faction and body type are the same on every machine, so each resolves the same clip.
        /// </summary>
        private VaultStyle StyleFor(float top)
        {
            FactionId faction = identity != null ? identity.Faction : FactionId.None;
            BodyType body = identity != null ? identity.State.bodyType : BodyType.Masculine;
            animations.TryResolve(faction, body, traversal.HeightOf(top), out VaultStyle style);
            return style;
        }
        private bool HasAuthority => !IsSpawned || IsServer;

        private void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            movementSkills = GetComponent<PlayerMovementSkills>();
            resources = GetComponent<PlayerResources>();
            animancerMotor = GetComponent<PlayerAnimancerMotor>();
            moveAnticipation = GetComponent<PlayerMoveAnticipation>();
            jumpMotor = GetComponent<PlayerJumpMotor>();
            identity = GetComponent<PlayerIdentity>();
            scratchPath = new NavMeshPath();
            if (warpTemplate != null)
            {
                warpAsset = Instantiate(warpTemplate);
                warpAsset.name = warpTemplate.name + " (runtime)";
            }
        }

        public override void OnDestroy()
        {
            if (warpAsset != null) Destroy(warpAsset);
            base.OnDestroy();
        }

        // ------------------------------------------------------------------ planning (authority)

        /// <summary>
        /// Looks for a vault on the straight line to <paramref name="destination"/> that beats <paramref name="walkPath"/>
        /// (null when there is no way round at all, so any vault that gets there is welcome). On success the motor is
        /// armed and <paramref name="takeoffPoint"/> is where the agent should be sent instead.
        /// </summary>
        public bool TryArm(Vector3 destination, NavMeshPath walkPath, LayerMask obstacleLayers, float modelForwardOffset, out Vector3 takeoffPoint) =>
            TryArm(destination, walkPath != null ? Length(walkPath) : float.PositiveInfinity, obstacleLayers, modelForwardOffset, true, out takeoffPoint);

        /// <summary>
        /// The player's own game (online phase 3): whether the server, given this order, would go over an obstacle on
        /// the way, and from where. The same check as <see cref="TryArm"/>, with nothing planned or started, so the
        /// game can walk to the same take-off point the server walks to.
        /// </summary>
        public bool WouldVault(Vector3 destination, NavMeshPath walkPath, LayerMask obstacleLayers, float modelForwardOffset, out Vector3 takeoffPoint) =>
            TryArm(destination, walkPath != null ? Length(walkPath) : float.PositiveInfinity, obstacleLayers, modelForwardOffset, false, out takeoffPoint);

        private bool TryArm(Vector3 destination, float walkLength, LayerMask obstacleLayers, float modelForwardOffset, bool arm, out Vector3 takeoffPoint)
        {
            takeoffPoint = default;
            if (arm) armed = false;
            LastDecision = "not available";
            if (!isActiveAndEnabled || (arm && !HasAuthority) || !Ready || vaulting || (jumpMotor != null && jumpMotor.IsBusy)) return false;
            if (!agent.isActiveAndEnabled || !agent.isOnNavMesh) return false;

            Vector3 feet = transform.position - Vector3.up * agent.baseOffset;
            // The straight line stays on the NavMesh: nothing to go over.
            LastDecision = "straight line is clear";
            if (!NavMesh.Raycast(feet, destination, out NavMeshHit edge, agent.areaMask)) return false;

            Vector3 travel = destination - feet;
            travel.y = 0f;
            if (travel.sqrMagnitude < 1f) return false;
            travel.Normalize();

            int layers = obstacleLayers.value & ~(1 << gameObject.layer);
            // Heights are measured from the visible ground in front of the obstacle: it can sit a little off the NavMesh,
            // and on a slope it is not the height the character stands at.
            Vector3 beforeEdge = edge.position - travel * 0.15f + Vector3.up * 0.6f;
            float groundY = Physics.Raycast(beforeEdge, Vector3.down, out RaycastHit ground, 1.5f, layers, QueryTriggerInteraction.Ignore)
                ? ground.point.y : edge.position.y;
            if (!FindObstacle(groundY, edge, travel, layers, out Vector3 near, out Vector3 across, out float top, out float thickness))
                return false;

            VaultStyle style = StyleFor(top);
            LastDecision = "no room to take off";
            if (!FindTakeoff(feet, edge.position, travel, near, across, style, out Vector3 takeoff)) return false;
            LastDecision = "no room to land";
            if (!FindLanding(groundY, near, across, thickness, style, layers, out Vector3 landing)) return false;
            LastDecision = "something is in the way above the obstacle";
            if (!IsCrossingClear(near, across, groundY + top, thickness, layers)) return false;

            // Worth it only when it really is the shorter way, and the rest of the way is walkable from the landing.
            LastDecision = "no way on from the landing";
            if (!NavMesh.CalculatePath(landing, destination, agent.areaMask, scratchPath)
                || scratchPath.status != NavMeshPathStatus.PathComplete) return false;
            float over = Flat(feet, takeoff) + Flat(takeoff, landing) + Length(scratchPath);
            LastDecision = "walking round is shorter";
            if (walkLength - over < minimumDistanceSaved) return false;
            LastDecision = "vault";
            if (!arm)
            {
                takeoffPoint = takeoff;
                return true;
            }

            plan = new Plan
            {
                Takeoff = takeoff,
                Landing = landing,
                NearEdge = near,
                FarEdge = near + across * thickness,
                Destination = destination,
                Top = top,
                FacingOffset = modelForwardOffset,
            };
            armed = true;
            watching = false;
            armedAt = Time.time;
            takeoffPoint = plan.Takeoff;
            return true;
        }

        /// <summary>
        /// Keeps looking for a vault while the agent walks a route that started without one: something tall stood in the
        /// straight line at first, but once the route has cleared it an obstacle further on may be one to go over.
        /// </summary>
        public void Watch(Vector3 destination, LayerMask obstacleLayers, float modelForwardOffset)
        {
            if (!isActiveAndEnabled || !HasAuthority || !Ready) return;
            watching = true;
            watchDestination = destination;
            watchLayers = obstacleLayers;
            watchFacingOffset = modelForwardOffset;
            nextWatch = Time.time + WatchSeconds;
        }

        /// <summary>Forgets a planned or watched-for vault (a new command took over). A vault already in the air carries on.</summary>
        public void Disarm()
        {
            armed = false;
            watching = false;
        }

        /// <summary>A vault is planned and the agent is on its way to the take-off point.</summary>
        public bool IsArmed => armed;

        /// <summary>
        /// The planned or watched-for vault as it stands. Planning a new route forgets it; a command that is then
        /// refused hands this back to <see cref="PutBack"/>, so the route still under way keeps its vault.
        /// </summary>
        public Standing Keep() => new Standing
        {
            Armed = armed,
            Watching = watching,
            Plan = plan,
            ArmedAt = armedAt,
            NextWatch = nextWatch,
            WatchFacingOffset = watchFacingOffset,
            WatchDestination = watchDestination,
            WatchLayers = watchLayers,
        };

        /// <summary>Puts back what <see cref="Keep"/> took. A vault already in the air is left alone.</summary>
        public void PutBack(Standing kept)
        {
            if (vaulting) return;
            armed = kept.Armed;
            watching = kept.Watching;
            plan = kept.Plan;
            armedAt = kept.ArmedAt;
            nextWatch = kept.NextWatch;
            watchFacingOffset = kept.WatchFacingOffset;
            watchDestination = kept.WatchDestination;
            watchLayers = kept.WatchLayers;
        }

        private void KeepWatching()
        {
            if (Time.time < nextWatch) return;
            nextWatch = Time.time + WatchSeconds;
            if (!agent.isActiveAndEnabled || !agent.isOnNavMesh || (jumpMotor != null && jumpMotor.IsBusy)) { watching = false; return; }
            if (agent.pathPending) return;
            // Arrived, stopped, or sent somewhere else: nothing left to shorten.
            if (!agent.hasPath || Flat(agent.destination, watchDestination) > 0.6f) { watching = false; return; }
            if (!TryArm(watchDestination, agent.remainingDistance, watchLayers, watchFacingOffset, true, out Vector3 takeoff)) return;
            if (!agent.SetDestination(takeoff)) Disarm();
        }

        /// <summary>
        /// Where the vault starts: where the character stands if it is already close enough, else the style's run-up
        /// distance before the obstacle. When that spot cannot be walked on (a flower bed in front of a hedge), the
        /// vault leaves from the NavMesh edge instead, as long as that is not too far out.
        /// </summary>
        private bool FindTakeoff(Vector3 feet, Vector3 navEdge, Vector3 travel, Vector3 near, Vector3 across, VaultStyle style, out Vector3 takeoff)
        {
            takeoff = feet;
            // Already standing in front of the crossing point, close enough to go from here.
            Vector3 toNear = near - feet;
            toNear.y = 0f;
            float ahead = Vector3.Dot(toNear, across);
            float aside = (toNear - across * ahead).magnitude;
            if (ahead >= 0f && ahead <= style.takeoffDistance && aside <= StandingStartAside) return true;

            Vector3 wanted = near - across * style.takeoffDistance;
            wanted.y = feet.y;
            if (NavMesh.SamplePosition(wanted, out NavMeshHit usual, 0.5f, agent.areaMask) && Flat(usual.position, wanted) <= 0.35f)
            {
                takeoff = usual.position;
                return true;
            }

            Vector3 atEdge = navEdge - travel * 0.1f;
            if (Vector3.Dot(near - atEdge, across) > MaximumRunUp) return false;
            if (!NavMesh.SamplePosition(atEdge, out NavMeshHit edgeNav, 0.3f, agent.areaMask)) return false;
            takeoff = edgeNav.position;
            return true;
        }

        /// <summary>
        /// Where the vault comes down: the style's landing distance beyond the obstacle, or the first walkable spot
        /// with standing room a little farther on when that one is not (the far side of a flower bed). Nothing may
        /// stand in the stretch the body comes down over.
        /// </summary>
        private bool FindLanding(float groundY, Vector3 near, Vector3 across, float thickness, VaultStyle style, int layers, out Vector3 landing)
        {
            landing = default;
            float castFrom = maximumHeight + 0.6f;
            for (float beyond = LandingSearchStep; beyond <= style.landingDistance + LandingSearch; beyond += LandingSearchStep)
            {
                Vector3 point = near + across * (thickness + beyond);
                point.y = groundY;
                // Something else stands behind the obstacle: the body would come down onto it.
                if (Physics.Raycast(point + Vector3.up * castFrom, Vector3.down, out RaycastHit surface, castFrom + maximumLandingStep, layers, QueryTriggerInteraction.Ignore)
                    && surface.point.y - groundY > StepHeight) return false;
                if (beyond < style.landingDistance) continue;

                if (!NavMesh.SamplePosition(point, out NavMeshHit found, 0.6f, agent.areaMask) || Flat(found.position, point) > 0.3f) continue;
                if (Mathf.Abs(found.position.y - groundY) > maximumLandingStep || !IsStandingRoomClear(found.position, layers)) continue;
                landing = found.position;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Measures the obstacle beyond the NavMesh edge: the point on its near face at ground level, the direction
        /// straight across it, its highest top along that line and how thick it is. The obstacle is looked for
        /// straight out from the NavMesh edge, not along the line of travel, so it is found however obliquely the move
        /// meets it; the vault then crosses it square-on from that point.
        /// </summary>
        private bool FindObstacle(float feetY, NavMeshHit navEdge, Vector3 travel, int layers,
            out Vector3 near, out Vector3 across, out float top, out float thickness)
        {
            near = across = default;
            top = 0f;
            thickness = 0f;

            // Out of the walkable area, on the side the move is heading.
            Vector3 outward = -navEdge.normal;
            outward.y = 0f;
            if (outward.sqrMagnitude < 0.01f) outward = travel;
            outward.Normalize();
            if (Vector3.Dot(outward, travel) < 0f) outward = -outward;

            LastDecision = "nothing low stands at the NavMesh edge";
            // Probed just under the lowest height that counts, so the lowest obstacles are still found.
            Vector3 probe = new Vector3(navEdge.position.x, feetY + minimumHeight * 0.85f, navEdge.position.z) - outward * 0.1f;
            if (!Physics.Raycast(probe, outward, out RaycastHit face, FaceProbeLength, layers, QueryTriggerInteraction.Ignore)
                && !Physics.Raycast(probe, travel, out face, FaceProbeLength, layers, QueryTriggerInteraction.Ignore)) return false;
            // Characters are never vaulted, however short they are.
            if (face.collider.GetComponentInParent<NavMeshAgent>() != null || face.collider.GetComponentInParent<IDamageable>() != null) return false;

            LastDecision = "the move runs along the obstacle, not through it";
            across = -face.normal;
            across.y = 0f;
            if (across.sqrMagnitude < 0.01f) return false;
            across.Normalize();
            if (Vector3.Angle(across, travel) > maximumApproachAngle) return false;

            near = face.point;
            near.y = feetY;

            float castFrom = maximumHeight + 0.6f;
            float castLength = castFrom + maximumLandingStep + 0.3f;
            bool ended = false;
            for (float along = 0.06f; along <= maximumThickness + TopSampleStep; along += TopSampleStep)
            {
                Vector3 above = near + across * along + Vector3.up * castFrom;
                // Nothing underneath is a drop, not an obstacle with a far side to land on.
                LastDecision = "a drop behind the obstacle";
                if (!Physics.Raycast(above, Vector3.down, out RaycastHit surface, castLength, layers, QueryTriggerInteraction.Ignore)) return false;
                float height = surface.point.y - feetY;
                LastDecision = "too tall";
                if (height > maximumHeight) return false; // too tall, or something tall stands on or behind it
                if (height <= GroundTolerance)
                {
                    thickness = along - TopSampleStep * 0.5f;
                    ended = true;
                    break;
                }
                top = Mathf.Max(top, height);
            }
            LastDecision = !ended ? "too thick" : "too low to need a vault";
            return ended && top >= minimumHeight;
        }

        private bool IsStandingRoomClear(Vector3 feet, int layers)
        {
            float radius = Mathf.Max(0.01f, agent.radius - CollisionSkin);
            Vector3 bottom = feet + Vector3.up * agent.radius;
            Vector3 topSphere = feet + Vector3.up * Mathf.Max(agent.radius, agent.height - agent.radius);
            return !Physics.CheckCapsule(bottom, topSphere, radius, layers, QueryTriggerInteraction.Ignore);
        }

        /// <summary>The space the body passes through above the obstacle, from just over its top to a crouched body height.</summary>
        private bool IsCrossingClear(Vector3 near, Vector3 across, float topY, float thickness, int layers)
        {
            const float BodyHeight = 1.1f;
            Vector3 centre = near + across * (thickness * 0.5f);
            centre.y = topY + clearance + BodyHeight * 0.5f;
            var halfExtents = new Vector3(agent.radius * 0.7f, BodyHeight * 0.5f, thickness * 0.5f + 0.2f);
            return !Physics.CheckBox(centre, halfExtents, Quaternion.LookRotation(across, Vector3.up), layers, QueryTriggerInteraction.Ignore);
        }

        private static float Flat(Vector3 a, Vector3 b)
        {
            a.y = b.y = 0f;
            return Vector3.Distance(a, b);
        }

        private static float Length(NavMeshPath path)
        {
            Vector3[] corners = path.corners;
            float length = 0f;
            for (int i = 1; i < corners.Length; i++) length += Vector3.Distance(corners[i - 1], corners[i]);
            return length;
        }

        // ------------------------------------------------------------------ run

        private void Update()
        {
            if (remotePending)
            {
                // The body shown here (NetworkTransform's, under this player's own walk) has reached the take-off point.
                Vector3 shown = moveAnticipation != null ? moveAnticipation.ServerShown : transform.position;
                bool there = Flat(shown, remoteTakeoffRoot) <= RemoteTakeoffTolerance;
                if (!there && Time.time - remotePendingSince < RemoteTakeoffWaitMax) return;
                remotePending = false;
                if (IsOwner) PlayerMoveAnticipation.Trace(this, $"vault starts here ({(there ? "body at the take-off" : "waited the most")}, {Time.time - remotePendingSince:0.00} s after the server's)");
                Begin(remotePlan, remoteSpeed);
                return;
            }
            if (vaulting)
            {
                if (HasAuthority && !resources.IsAlive) { Abort(); return; }
                clock += Time.deltaTime;
                if (clock >= airTime && (HasAuthority || RemoteBodyHasLanded())) Finish();
                return;
            }

            if (!armed)
            {
                if (watching) KeepWatching();
                return;
            }
            if (!agent.isActiveAndEnabled || !agent.isOnNavMesh || (jumpMotor != null && jumpMotor.IsBusy)) { Disarm(); return; }
            // The agent is heading somewhere else: another command took the route over. (The path is set by the
            // caller right after arming, hence the short grace period.)
            if (Time.time - armedAt > RearmSeconds && Flat(agent.destination, plan.Takeoff) > 0.6f) { Disarm(); return; }

            Vector3 feet = transform.position - Vector3.up * agent.baseOffset;
            float reach = TriggerRadius + agent.stoppingDistance + agent.velocity.magnitude * Time.deltaTime * 2f;
            if (Flat(feet, plan.Takeoff) > reach) return;

            armed = false;
            float speed = movementSkills != null ? movementSkills.CurrentSpeed : agent.speed;
            Begin(plan, speed);
            if (IsSpawned) VaultClientRpc(takeoffRoot, plan.Landing, plan.NearEdge, plan.FarEdge, plan.Top, plan.FacingOffset, plan.Destination, speed);
        }

        [ClientRpc]
        private void VaultClientRpc(Vector3 takeoff, Vector3 landing, Vector3 nearEdge, Vector3 farEdge, float top, float facingOffset, Vector3 destination, float speed)
        {
            if (IsServer || !Ready) return; // the server is already running it
            if (vaulting) Abort();
            // Started when the body shown here reaches the server's take-off point (Update), not a step before it.
            remotePlan = new Plan { Landing = landing, NearEdge = nearEdge, FarEdge = farEdge, Top = top, FacingOffset = facingOffset, Destination = destination };
            remoteSpeed = speed;
            remoteTakeoffRoot = takeoff;
            remotePendingSince = Time.time;
            remotePending = true;
        }

        /// <summary>
        /// Shown here, simulated elsewhere: the body comes in through NetworkTransform slightly behind the clip, so
        /// the touchdown waits (briefly) for it instead of running the last stretch of the vault in the air.
        /// </summary>
        private bool RemoteBodyHasLanded()
        {
            if (clock >= airTime + RemoteLandingGrace) return true;
            Vector3 feet = transform.position - Vector3.up * agent.baseOffset;
            // Measured flat: the server's body runs straight on from the landing, so it may never be shown right on the
            // point, or at its exact height. Gone past it in the vault's direction counts as landed too.
            if (Flat(feet, landingFeet) <= RemoteLandingTolerance) return true;
            Vector3 onward = landingFeet - (takeoffRoot - Vector3.up * agent.baseOffset);
            Vector3 beyond = feet - landingFeet;
            onward.y = 0f;
            beyond.y = 0f;
            return Vector3.Dot(beyond, onward) > 0f;
        }

        [ClientRpc]
        private void VaultAbortedClientRpc()
        {
            remotePending = false;
            if (!IsServer && vaulting) Abort();
        }

        /// <summary>
        /// Starts the vault. Where the player is simulated this moves the body with the warp; every other machine
        /// plays the same clip on the same clock while NetworkTransform carries the body.
        /// </summary>
        private void Begin(Plan vault, float speed)
        {
            takeoffRoot = transform.position;
            Vector3 startFeet = takeoffRoot - Vector3.up * agent.baseOffset;
            landingFeet = vault.Landing;
            travelSpeed = Mathf.Max(0.1f, speed);
            plan.Destination = vault.Destination;

            VaultStyle style = StyleFor(vault.Top);
            float distance = Mathf.Max(0.1f, Flat(startFeet, landingFeet));
            airTime = Mathf.Clamp(distance / travelSpeed, style.airTime.x, Mathf.Min(style.airTime.y, warpAsset.GetLength() - 0.02f));

            float raise = Mathf.Max(MinimumRaise, vault.Top - style.poseClearance + clearance);
            // Up before the near edge, level across the top, down after the far edge.
            float riseEnds = airTime * Mathf.Clamp((Flat(startFeet, vault.NearEdge) - EdgeLead) / distance, 0.15f, 0.45f);
            float fallStarts = airTime * Mathf.Clamp((Flat(startFeet, vault.FarEdge) + EdgeLead) / distance, 0.55f, 0.85f);

            Vector3 across = landingFeet - startFeet;
            across.y = 0f;
            Quaternion facing = Quaternion.LookRotation(across.normalized, Vector3.up) * Quaternion.AngleAxis(vault.FacingOffset, Vector3.up);

            if (HasAuthority)
            {
                if (agent.isActiveAndEnabled && agent.isOnNavMesh)
                {
                    agent.isStopped = true;
                    agent.ResetPath();
                    agent.velocity = Vector3.zero;
                }
                agent.enabled = false; // the warp owns the root until touchdown
            }

            vaulting = true;
            clock = 0f;
            float window = Mathf.Max(0.05f, style.window.y - style.window.x);
            animancerMotor.PlayWarpedAction(style.clip, style.window.x, window / airTime, fadeIn);

            if (HasAuthority)
            {
                ShapeWarp(raise, riseEnds, fallStarts);
                motionWarping.Play(warpAsset, new[]
                {
                    new WarpPoint { position = landingFeet + Vector3.up * agent.baseOffset, rotation = facing },
                });
            }

            var feel = ClientPresentationRegistry.Feel;
            if (feel != null)
            {
                feel.JumpTakeoff(transform, !IsSpawned || IsOwner);
                feel.Airborne(transform, true);
            }
        }

        /// <summary>
        /// Writes this vault into the warp asset: one phase from take-off to touchdown with even travel along the ground
        /// and a root height that rises to <paramref name="raise"/>, holds while the body is over the obstacle and
        /// comes back down.
        /// </summary>
        private void ShapeWarp(float raise, float riseEnds, float fallStarts)
        {
            float length = warpAsset.GetLength();
            warpAsset.rootX = AnimationCurve.Constant(0f, length, 0f);
            warpAsset.rootZ = AnimationCurve.Constant(0f, length, 0f);
            warpAsset.rootY = new AnimationCurve(
                new Keyframe(0f, 0f, 0f, 0f), new Keyframe(riseEnds, raise, 0f, 0f),
                new Keyframe(fallStarts, raise, 0f, 0f), new Keyframe(airTime, 0f, 0f, 0f));
            warpAsset.useLinear = new VectorBool(true, false, true);     // even travel; height follows the curve
            warpAsset.useAnimation = new VectorBool(false, true, false); // only the height curve moves the body
            warpAsset.useWarping = VectorBool.Enabled;
            warpAsset.useCollision = false;
            warpAsset.playRateBasis = 1f;
            warpAsset.phasesAmount = 1;
            if (warpAsset.warpPhases == null) warpAsset.warpPhases = new List<WarpPhase>(1);
            warpAsset.warpPhases.Clear();
            warpAsset.warpPhases.Add(new WarpPhase
            {
                // Just before zero, so the phase is entered on the warp's first update rather than its second.
                startTime = -0.001f,
                endTime = airTime,
                minRate = 1f,
                maxRate = 1f,
                totalRootMotion = new Vector3(0f, raise * 2f, 0f),
            });
        }

        private void Finish()
        {
            StopWarp();
            if (HasAuthority && !RestoreAgent(landingFeet + Vector3.up * agent.baseOffset)) { Abort(); return; }
            vaulting = false;
            animancerMotor.EndWarpedAction(fadeOut, travelSpeed);

            var feel = ClientPresentationRegistry.Feel;
            if (feel != null)
            {
                feel.Airborne(transform, false);
                feel.Landed(transform, !IsSpawned || IsOwner);
            }
            // Where it is decided, the move carries on from the landing; in the player's own game, its own walk does
            // (online phase 3), so the run starts on touchdown instead of the body sliding on in the landing pose.
            if (!HasAuthority && IsOwner) PlayerMoveAnticipation.Trace(this, $"vault landed, the walk carries on to {plan.Destination}");
            if (HasAuthority || IsOwner) Landed?.Invoke(plan.Destination);
        }

        /// <summary>
        /// The character died: drop the vault it was lined up for, or let go of the body where it is if it was in
        /// the middle of one. The death takes it from there (PlayerDeathMotor brings it to the ground).
        /// </summary>
        public void ReleaseForDeath()
        {
            Disarm();
            if (!vaulting) return;
            StopWarp();
            vaulting = false;
            if (HasAuthority)
            {
                if (IsSpawned && IsServer) VaultAbortedClientRpc();
                agent.enabled = false;
            }
            ClientPresentationRegistry.Feel?.Airborne(transform, false);
            if (animancerMotor != null) animancerMotor.ResetJumpAnimation();
        }

        /// <summary>Puts the character back where the vault started (the landing is gone).</summary>
        private void Abort()
        {
            if (cancelling) return;
            cancelling = true;
            StopWarp();
            vaulting = false;
            armed = false;
            if (HasAuthority)
            {
                if (IsSpawned && IsServer) VaultAbortedClientRpc();
                if (!gameObject.activeInHierarchy || !RestoreAgent(takeoffRoot))
                {
                    agent.enabled = false;
                    transform.position = takeoffRoot;
                }
            }
            ClientPresentationRegistry.Feel?.Airborne(transform, false);
            if (animancerMotor != null) animancerMotor.ResetJumpAnimation();
            cancelling = false;
        }

        private void StopWarp()
        {
            if (motionWarping != null && motionWarping.IsActive()) motionWarping.Stop();
        }

        private bool RestoreAgent(Vector3 rootPosition)
        {
            var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };
            if (!NavMesh.SamplePosition(rootPosition - Vector3.up * agent.baseOffset, out NavMeshHit hit, 1f, filter)) return false;
            agent.enabled = false;
            transform.position = hit.position + Vector3.up * agent.baseOffset;
            agent.enabled = true;
            if (!agent.isOnNavMesh) { agent.enabled = false; return false; }
            agent.ResetPath();
            agent.isStopped = false;
            return true;
        }

        private void OnDisable()
        {
            Disarm();
            if (vaulting) Abort();
        }
    }
}
