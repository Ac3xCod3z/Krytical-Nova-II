using System;
using DG.Tweening;
using Kinemation.MotionWarping.Runtime.Core;
using Kinemation.MotionWarping.Runtime.Utility;
using KryticalNova.Progression;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using WarpComponent = Kinemation.MotionWarping.Runtime.Core.MotionWarping;

namespace KryticalNova.Player
{
    /// <summary>
    /// Validated click-to-jump traversal with safe NavMesh restoration. With a warp asset assigned the whole jump is
    /// one animation whose root motion is warped (KINEMATION Motion Warping) onto the apex and the landing point, so
    /// pose and travel always agree. Without one it falls back to the DOTween parabola with separate jump clips.
    /// The jump is simulated where the player is (the server, or offline). Every other machine only presents it:
    /// it plays the same clips on the same clock while NetworkTransform carries the body.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NavMeshAgent), typeof(PlayerMovementSkills), typeof(PlayerResources))]
    public sealed class PlayerJumpMotor : NetworkBehaviour
    {
        private const float MinimumTravelDistance = 0.15f;
        private const float MinimumTime = 0.01f;
        private const float LandingHeightTolerance = 0.2f;
        private const float FlipFinishMargin = 0.04f;
        private const float MaximumSweepTimeStep = 0.02f;
        // A jump shown here but simulated elsewhere: the body arrives through NetworkTransform a little after the
        // order to show it, so the clips start that much later and the landing waits for the body to come down.
        private const float RemoteStartDelay = 0.1f;
        private const float RemoteLandingGrace = 0.6f;
        private const float RemoteLandingTolerance = 0.05f;
        [SerializeField] private JumpMotionData motionData;
        [SerializeField] private PlayerJumpPresentation presentation;

        [Header("Motion-warped jump (built by Krytical Nova/Player/Build Jump Warp Asset)")]
        [Tooltip("Root curves of the whole-jump clip with two phases: take-off to apex, apex to touchdown.")]
        [SerializeField] private MotionWarpingAsset warpAsset;
        [Tooltip("Lives on its own child object with Transform To Warp set to this player, so the warp component never finds " +
                 "(and toggles) the player's Animator, Rigidbody or CharacterController.")]
        [SerializeField] private WarpComponent motionWarping;

        // Warped-jump state: the clock follows the clip, whose phases say when the feet leave and meet the ground.
        private bool warping;
        private bool warpAirborne;
        private float warpClock;
        private float warpTakeoffTime;
        private float warpApexTime;
        private float warpLandTime;
        private Vector3 apexFeet;
        private Vector3 lastSweepFeet;

        private NavMeshAgent agent;
        private PlayerMovementSkills skills;
        private PlayerResources resources;
        private Sequence jumpSequence;
        private Vector3 startFeet;
        private Vector3 landingFeet;
        private Vector3 takeoffRootPosition;
        private float peakHeight;
        private float ascentTime;
        private float descentTime;
        private float flightTime;
        private float evaluatedTime;
        private float cooldownUntil;
        private float facingOffset;
        private int collisionLayers;
        private bool inFlight;
        private bool apexPlayed;
        private bool cancelling;
        private bool restoreOnEnable;
        private bool planUsesWarp;

        // A jump simulated elsewhere, shown here: only the clock and the clips run.
        private bool remoteJump;
        private bool remoteStarted;
        private bool remoteWarped;
        private bool remoteAirborne;
        private float remoteClock;
        private float remoteTakeoffTime;
        private float remoteApexTime;
        private float remoteLandTime;

        public bool IsBusy { get; private set; }
        public event Action<string> Message;

        /// <summary>True where this player's jump is simulated (the server, or offline).</summary>
        private bool HasAuthority => !IsSpawned || IsServer;

        private void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            skills = GetComponent<PlayerMovementSkills>();
            resources = GetComponent<PlayerResources>();
        }

        private void OnEnable()
        {
            if (!restoreOnEnable || agent == null || motionData == null) return;
            restoreOnEnable = false;
            if (!RestoreAgent(takeoffRootPosition))
                Debug.LogWarning("Jump could not reattach the player to its takeoff surface after reactivation.", this);
        }

        /// <summary>Validates a clicked landing point before interrupting the current path. Chi is charged at takeoff.</summary>
        public bool TryJump(Vector3 clickedPoint, LayerMask obstacleLayers, float modelForwardOffset, out string reason)
        {
            // An owning client checks the jump against its mirrored ranks, Chi and the same NavMesh, so a bad click
            // is refused at once; the server checks again before it starts anything.
            if (!TryPlanJump(clickedPoint, obstacleLayers.value, modelForwardOffset, out reason)) return false;

            if (!HasAuthority)
            {
                RequestJumpServerRpc(clickedPoint, obstacleLayers.value, modelForwardOffset);
                return true;
            }

            BeginJump();
            return true;
        }

        [ServerRpc]
        private void RequestJumpServerRpc(Vector3 clickedPoint, int obstacleLayersValue, float modelForwardOffset)
        {
            if (TryPlanJump(clickedPoint, obstacleLayersValue, modelForwardOffset, out string reason)) BeginJump();
            else JumpRejectedRpc(reason);
        }

        [Rpc(SendTo.Owner)]
        private void JumpRejectedRpc(string reason)
        {
            if (!string.IsNullOrEmpty(reason)) Message?.Invoke(reason);
        }

        /// <summary>Starts the planned jump where it is simulated and shows it on every other machine.</summary>
        private void BeginJump()
        {
            if (planUsesWarp) BeginWarpedJump();
            else BeginParabolaJump();
            if (IsSpawned) JumpStartedClientRpc(landingFeet, peakHeight);
        }

        [ClientRpc]
        private void JumpStartedClientRpc(Vector3 landing, float peak)
        {
            if (IsServer) return; // the server is already running it
            BeginRemoteJump(landing, peak);
        }

        [ClientRpc]
        private void JumpAbortedClientRpc(string reason)
        {
            if (IsServer) return;
            EndRemoteJump(false);
            if (IsOwner && !string.IsNullOrEmpty(reason)) Message?.Invoke(reason);
        }

        /// <summary>
        /// Checks a jump to <paramref name="clickedPoint"/> and works out its route without starting or spending anything.
        /// </summary>
        private bool TryPlanJump(Vector3 clickedPoint, int obstacleLayersValue, float modelForwardOffset, out string reason)
        {
            reason = string.Empty;
            if (IsBusy) { reason = "Finish this jump before choosing another destination."; return false; }
            if (Time.time < cooldownUntil) { reason = "Let the landing settle before jumping again."; return false; }
            if (!isActiveAndEnabled || motionData == null || presentation == null || !presentation.IsReady)
            { reason = "Jump animation is not configured."; return false; }
            if (agent != null && agent.isActiveAndEnabled && !agent.isOnNavMesh) { reason = "Jump must start on walkable ground."; return false; }
            MovementSkillRank rank = default;
            if (skills != null && !skills.TryGetLearnedRank(MovementSkillId.Jump, out rank)) { reason = "Learn Jump first."; return false; }
            if (skills != null && !skills.CanPayForCommand(MovementSkillId.Jump, out reason)) return false;
            
            float agentOffset = agent != null ? agent.baseOffset : 0f;
            var filter = new NavMeshQueryFilter { agentTypeID = agent != null ? agent.agentTypeID : 0, areaMask = agent != null ? agent.areaMask : -1 };
            if (!NavMesh.SamplePosition(clickedPoint, out var landing, motionData.landingSampleRadius, filter)
                || Mathf.Abs(landing.position.y - clickedPoint.y) > LandingHeightTolerance)
            { reason = "Choose a walkable landing surface."; return false; }
            startFeet = transform.position - Vector3.up * agentOffset;
            landingFeet = landing.position;
            Vector3 displacement = landingFeet - startFeet;
            float horizontalDistance = new Vector2(displacement.x, displacement.z).magnitude;
            if (horizontalDistance < MinimumTravelDistance) { reason = "Choose a landing point farther away."; return false; }
            float maxDist = skills != null ? rank.jumpDistance : 20f;
            if (horizontalDistance > maxDist)
            { reason = $"Too far: Jump rank reaches {maxDist:0.0} m."; return false; }
            float jumpH = skills != null ? rank.jumpHeight : 5f;
            if (displacement.y > jumpH || -displacement.y > jumpH * motionData.maximumDropMultiplier)
            { reason = $"Landing height is outside this rank's limits (+{jumpH:0.0} m / -{jumpH * motionData.maximumDropMultiplier:0.0} m)."; return false; }
            collisionLayers = obstacleLayersValue & ~(1 << gameObject.layer);
            facingOffset = modelForwardOffset;
            takeoffRootPosition = transform.position;
            float clearanceApex = Mathf.Max(startFeet.y, landingFeet.y) + motionData.apexClearance;
            // Short hops stay low; only a max-range jump climbs to the rank's full height.
            peakHeight = Mathf.Max(clearanceApex, startFeet.y + motionData.RiseForDistance(horizontalDistance, maxDist, jumpH));
            planUsesWarp = UseWarpedJump;
            if (planUsesWarp) return PlanWarpedJump(out reason);
            PlanParabola();
            if (!ValidateTrajectory()) { reason = "The jump arc or landing area is blocked."; return false; }
            return true;
        }

        private void PlanParabola()
        {
            ascentTime = Mathf.Sqrt(2f * Mathf.Max(0f, peakHeight - startFeet.y) / motionData.gravity);
            descentTime = Mathf.Sqrt(2f * Mathf.Max(0f, peakHeight - landingFeet.y) / motionData.gravity);
            flightTime = ascentTime + motionData.apexHoldDuration + descentTime;
        }

        private void BeginParabolaJump()
        {
            if (agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh)
            {
                agent.isStopped = true;
                agent.ResetPath();
            }
            IsBusy = true;
            inFlight = false;
            apexPlayed = false;
            evaluatedTime = 0f;
            presentation.Anticipate();
            jumpSequence = DOTween.Sequence();
            jumpSequence.SetTarget(this);
            jumpSequence.AppendInterval(motionData.anticipationDuration);
            jumpSequence.AppendCallback(TakeOff);
            float clock = 0f;
            jumpSequence.Append(DOTween.To(() => clock, value => { clock = value; EvaluateFlight(value); }, flightTime, flightTime).SetEase(Ease.Linear));
            jumpSequence.OnComplete(FinishJump);
        }

        // ------------------------------------------------------------------ motion-warped jump

        private bool UseWarpedJump => warpAsset != null && motionWarping != null && presentation.SupportsWarpedJump
            && warpAsset.warpPhases != null && warpAsset.warpPhases.Count == 2 && warpAsset.rootY != null;

        private bool ReadWarpPhases()
        {
            warpTakeoffTime = warpAsset.warpPhases[0].startTime;
            warpApexTime = warpAsset.warpPhases[0].endTime;
            warpLandTime = warpAsset.warpPhases[1].endTime;
            return warpApexTime > warpTakeoffTime && warpLandTime > warpApexTime;
        }

        private bool PlanWarpedJump(out string reason)
        {
            reason = string.Empty;
            if (!ReadWarpPhases())
            { reason = "Jump warp asset has no flight phases. Rebuild it from the Krytical Nova menu."; return false; }

            // Travel is even over the flight, so the apex sits where the clip's apex falls in time.
            float airTime = warpLandTime - warpTakeoffTime;
            apexFeet = Vector3.Lerp(startFeet, landingFeet, (warpApexTime - warpTakeoffTime) / airTime);
            apexFeet.y = peakHeight;
            if (!ValidateWarpedTrajectory()) { reason = "The jump arc or landing area is blocked."; return false; }
            return true;
        }

        private void BeginWarpedJump()
        {
            if (agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh)
            {
                agent.isStopped = true;
                agent.ResetPath();
                agent.velocity = Vector3.zero;
            }
            // The warp owns the root from the crouch onward (the clip's root dips before it rises), so the agent
            // lets go now; an abort before take-off puts it back on the launch surface.
            agent.enabled = false;

            // Face the landing first: the warp keeps the rotation the character has when it starts.
            Vector3 direction = landingFeet - startFeet;
            direction.y = 0f;
            transform.rotation = Quaternion.LookRotation(direction, Vector3.up) * Quaternion.AngleAxis(facingOffset, Vector3.up);

            IsBusy = true;
            inFlight = false;
            warping = true;
            warpAirborne = false;
            warpClock = 0f;
            presentation.BeginWarpedJump();

            Vector3 rootOffset = Vector3.up * agent.baseOffset;
            Quaternion rotation = transform.rotation;
            motionWarping.Play(warpAsset, new[]
            {
                new WarpPoint { position = apexFeet + rootOffset, rotation = rotation },
                new WarpPoint { position = landingFeet + rootOffset, rotation = rotation },
            });
        }

        // ------------------------------------------------------------------ presentation of a jump simulated elsewhere

        private void BeginRemoteJump(Vector3 landing, float peak)
        {
            if (remoteJump) EndRemoteJump(false);
            if (motionData == null || presentation == null || !presentation.IsReady) return;

            startFeet = transform.position - Vector3.up * (agent != null ? agent.baseOffset : 0f);
            landingFeet = landing;
            peakHeight = peak;
            remoteWarped = UseWarpedJump && ReadWarpPhases();
            if (remoteWarped)
            {
                remoteTakeoffTime = warpTakeoffTime;
                remoteApexTime = warpApexTime;
                remoteLandTime = warpLandTime;
            }
            else
            {
                PlanParabola();
                remoteTakeoffTime = motionData.anticipationDuration;
                remoteApexTime = remoteTakeoffTime + ascentTime;
                remoteLandTime = remoteTakeoffTime + flightTime;
            }

            IsBusy = true;
            remoteJump = true;
            remoteStarted = false;
            remoteAirborne = false;
            apexPlayed = false;
            remoteClock = -RemoteStartDelay;
        }

        private void UpdateRemoteJump()
        {
            remoteClock += Time.deltaTime;
            if (!remoteStarted)
            {
                if (remoteClock < 0f) return;
                remoteStarted = true;
                if (remoteWarped) presentation.BeginWarpedJump();
                else presentation.Anticipate();
            }

            if (!remoteAirborne)
            {
                if (remoteClock < remoteTakeoffTime) return;
                remoteAirborne = true;
                if (remoteWarped) presentation.WarpedTakeOff();
                else presentation.TakeOff();
                return;
            }

            if (!remoteWarped && !apexPlayed && remoteClock >= remoteApexTime)
            {
                apexPlayed = true;
                presentation.PlayApexFlip(Mathf.Max(MinimumTime, remoteLandTime - remoteClock - FlipFinishMargin));
            }
            if (remoteClock < remoteLandTime) return;
            // Touch down when the body has: until then it is still coming in through NetworkTransform.
            Vector3 feet = transform.position - Vector3.up * (agent != null ? agent.baseOffset : 0f);
            bool arrived = (feet - landingFeet).sqrMagnitude <= RemoteLandingTolerance * RemoteLandingTolerance;
            if (arrived || remoteClock >= remoteLandTime + RemoteLandingGrace) EndRemoteJump(true);
        }

        private void EndRemoteJump(bool landed)
        {
            if (!remoteJump) return;
            bool shown = remoteStarted;
            remoteJump = false;
            remoteStarted = false;
            remoteAirborne = false;
            IsBusy = false;
            cooldownUntil = Time.time + (motionData != null ? motionData.landingCooldown : 0f);
            if (presentation == null || !shown) return;
            if (!landed) { presentation.ResetPresentation(); return; }

            if (remoteWarped) presentation.WarpedLand();
            else presentation.Land();
            if (IsOwner)
                Message?.Invoke(skills.IsJumpSelected ? "Landed. Jump stays selected; choose another landing point." : "Landed. Movement ready.");
        }

        private void Update()
        {
            if (remoteJump) { UpdateRemoteJump(); return; }
            if (!warping) return;
            if (!resources.IsAlive) { AbortJump("Jump interrupted: no HP remaining."); return; }
            warpClock += Time.deltaTime;

            if (!warpAirborne)
            {
                if (warpClock < warpTakeoffTime) return;
                if (!ValidateWarpedTrajectory()) { AbortJump("Jump cancelled: the route changed before takeoff."); return; }
                if (!skills.TryPayForCommand(MovementSkillId.Jump, out _, out string reason)) { AbortJump(reason); return; }
                inFlight = true;
                warpAirborne = true;
                lastSweepFeet = SweepFeet();
                presentation.WarpedTakeOff();
                return;
            }

            Vector3 feet = SweepFeet();
            if (!IsSegmentClear(lastSweepFeet, feet)) { AbortJump("Jump interrupted by an obstacle. Returned to takeoff."); return; }
            lastSweepFeet = feet;
            if (warpClock >= warpLandTime) FinishWarpedJump();
        }

        /// <summary>
        /// The feet as the in-flight collision sweep sees them. The warp follows the clip's own root height, which sits
        /// under the ground line in the take-off crouch and can undershoot just before touchdown on a low hop. Swept as
        /// is, the ground itself would count as an obstacle and send the jump back to its start. So the sweep never goes
        /// below the straight line from take-off to landing; anything standing on that ground still blocks it.
        /// </summary>
        private Vector3 SweepFeet()
        {
            Vector3 feet = transform.position - Vector3.up * agent.baseOffset;
            float travelled = Mathf.InverseLerp(warpTakeoffTime, warpLandTime, warpClock);
            feet.y = Mathf.Max(feet.y, Mathf.Lerp(startFeet.y, landingFeet.y, travelled));
            return feet;
        }

        /// <summary>
        /// Where the warp will put the feet at a clip time. Mirrors the warp component's maths for this asset setup
        /// (horizontal travel linear in time; height follows the clip's own rise and fall, rescaled to reach the
        /// apex and the landing) so the route can be collision-checked before committing.
        /// </summary>
        private Vector3 EvaluateWarpedFeet(float clipTime)
        {
            if (clipTime <= warpTakeoffTime) return startFeet;
            AnimationCurve height = warpAsset.rootY;
            if (clipTime <= warpApexTime)
            {
                Vector3 feet = Vector3.Lerp(startFeet, apexFeet, Mathf.InverseLerp(warpTakeoffTime, warpApexTime, clipTime));
                float clipTotal = height.Evaluate(warpApexTime) - height.Evaluate(warpTakeoffTime);
                float share = Mathf.Abs(clipTotal) > MinimumTime
                    ? Mathf.Clamp01((height.Evaluate(clipTime) - height.Evaluate(warpTakeoffTime)) / clipTotal) : 1f;
                feet.y = Mathf.Lerp(startFeet.y, peakHeight, share);
                return feet;
            }
            else
            {
                clipTime = Mathf.Min(clipTime, warpLandTime);
                Vector3 feet = Vector3.Lerp(apexFeet, landingFeet, Mathf.InverseLerp(warpApexTime, warpLandTime, clipTime));
                float clipTotal = height.Evaluate(warpLandTime) - height.Evaluate(warpApexTime);
                float share = Mathf.Abs(clipTotal) > MinimumTime
                    ? Mathf.Clamp01((height.Evaluate(clipTime) - height.Evaluate(warpApexTime)) / clipTotal) : 1f;
                feet.y = Mathf.Lerp(peakHeight, landingFeet.y, share);
                return feet;
            }
        }

        private bool ValidateWarpedTrajectory()
        {
            if (!IsLandingClear()) return false;
            Vector3 previous = startFeet;
            for (int step = 1; step <= motionData.trajectorySamples; step++)
            {
                float clipTime = Mathf.Lerp(warpTakeoffTime, warpLandTime, (float)step / motionData.trajectorySamples);
                Vector3 next = EvaluateWarpedFeet(clipTime);
                if (!IsSegmentClear(previous, next)) return false;
                previous = next;
            }
            return true;
        }

        private void FinishWarpedJump()
        {
            StopWarp();
            if (!IsLandingClear() || !RestoreAgent(landingFeet + Vector3.up * agent.baseOffset))
            { AbortJump("Landing became unavailable. Returned to takeoff."); return; }
            IsBusy = false;
            inFlight = false;
            cooldownUntil = Time.time + motionData.landingCooldown;
            presentation.WarpedLand();
            Message?.Invoke(skills.IsJumpSelected ? "Landed. Jump stays selected; choose another landing point." : "Landed. Movement ready.");
        }

        private void StopWarp()
        {
            if (!warping) return;
            warping = false;
            warpAirborne = false;
            if (motionWarping != null && motionWarping.IsActive()) motionWarping.Stop();
        }

        // ------------------------------------------------------------------ parabola jump (no warp asset)

        private void TakeOff()
        {
            if (!IsBusy || !resources.IsAlive || !ValidateTrajectory())
            { AbortJump("Jump cancelled: the route changed before takeoff."); return; }
            if (!skills.TryPayForCommand(MovementSkillId.Jump, out _, out string reason))
            { AbortJump(reason); return; }
            Vector3 direction = landingFeet - startFeet;
            direction.y = 0f;
            transform.rotation = Quaternion.LookRotation(direction, Vector3.up) * Quaternion.AngleAxis(facingOffset, Vector3.up);
            agent.enabled = false;
            inFlight = true;
            presentation.TakeOff();
        }

        private Vector3 EvaluateFeet(float time)
        {
            float movingTime = time <= ascentTime ? time
                : time <= ascentTime + motionData.apexHoldDuration ? ascentTime : time - motionData.apexHoldDuration;
            Vector3 feet = Vector3.Lerp(startFeet, landingFeet, Mathf.Clamp01(movingTime / (ascentTime + descentTime)));
            if (time < ascentTime)
            {
                float remaining = ascentTime - time;
                feet.y = peakHeight - 0.5f * motionData.gravity * remaining * remaining;
            }
            else if (time <= ascentTime + motionData.apexHoldDuration) feet.y = peakHeight;
            else
            {
                float fallingTime = time - ascentTime - motionData.apexHoldDuration;
                feet.y = peakHeight - 0.5f * motionData.gravity * fallingTime * fallingTime;
            }
            return feet;
        }

        private void EvaluateFlight(float time)
        {
            if (!IsBusy || !inFlight) return;
            if (!resources.IsAlive) { AbortJump("Jump interrupted: no HP remaining."); return; }
            int steps = Mathf.Max(1, Mathf.CeilToInt((time - evaluatedTime) / MaximumSweepTimeStep));
            Vector3 previous = transform.position - Vector3.up * agent.baseOffset;
            for (int step = 1; step <= steps; step++)
            {
                Vector3 next = EvaluateFeet(Mathf.Lerp(evaluatedTime, time, (float)step / steps));
                if (!IsSegmentClear(previous, next)) { AbortJump("Jump interrupted by an obstacle. Returned to takeoff."); return; }
                previous = next;
            }
            transform.position = previous + Vector3.up * agent.baseOffset;
            if (!apexPlayed && time >= ascentTime)
            {
                apexPlayed = true;
                presentation.PlayApexFlip(Mathf.Max(MinimumTime, flightTime - time - FlipFinishMargin));
            }
            evaluatedTime = time;
        }

        private bool ValidateTrajectory()
        {
            if (!IsLandingClear()) return false;
            Vector3 previous = startFeet;
            for (int step = 1; step <= motionData.trajectorySamples; step++)
            {
                Vector3 next = EvaluateFeet(flightTime * step / motionData.trajectorySamples);
                if (!IsSegmentClear(previous, next)) return false;
                previous = next;
            }
            return true;
        }

        private void CapsuleAt(Vector3 feet, out Vector3 bottom, out Vector3 top, out float radius)
        {
            float skin = motionData.collisionSkin;
            radius = Mathf.Max(MinimumTime, agent.radius - skin);
            bottom = feet + Vector3.up * agent.radius;
            top = feet + Vector3.up * Mathf.Max(agent.radius, agent.height - agent.radius);
        }

        private bool IsSegmentClear(Vector3 from, Vector3 to)
        {
            CapsuleAt(from, out var bottom, out var top, out float radius);
            Vector3 delta = to - from;
            float distance = delta.magnitude;
            if (Physics.CheckCapsule(bottom, top, radius, collisionLayers, QueryTriggerInteraction.Ignore)) return false;
            return distance <= Mathf.Epsilon || !Physics.CapsuleCast(bottom, top, radius, delta / distance,
                distance, collisionLayers, QueryTriggerInteraction.Ignore);
        }

        private bool IsLandingClear()
        {
            CapsuleAt(landingFeet, out var bottom, out var top, out float radius);
            return !Physics.CheckCapsule(bottom, top, radius, collisionLayers, QueryTriggerInteraction.Ignore);
        }

        private bool RestoreAgent(Vector3 rootPosition)
        {
            var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };
            if (!NavMesh.SamplePosition(rootPosition - Vector3.up * agent.baseOffset, out var hit, motionData.landingSampleRadius, filter)) return false;
            agent.enabled = false;
            transform.position = hit.position + Vector3.up * agent.baseOffset;
            agent.enabled = true;
            if (!agent.isOnNavMesh) { agent.enabled = false; return false; }
            agent.ResetPath();
            agent.isStopped = false;
            return true;
        }

        private void FinishJump()
        {
            if (!IsBusy || !inFlight) return;
            jumpSequence = null;
            if (!IsLandingClear() || !RestoreAgent(landingFeet + Vector3.up * agent.baseOffset))
            { AbortJump("Landing became unavailable. Returned to takeoff."); return; }
            IsBusy = false;
            inFlight = false;
            cooldownUntil = Time.time + motionData.landingCooldown;
            presentation.Land();
            Message?.Invoke(skills.IsJumpSelected ? "Landed. Jump stays selected; choose another landing point." : "Landed. Movement ready.");
        }

        /// <summary>Cancels flight and restores the launch position when this controller is interrupted.</summary>
        public void CancelJump()
        {
            if (remoteJump) { EndRemoteJump(false); return; }
            if (IsBusy) AbortJump("Jump cancelled.");
        }

        /// <summary>
        /// The character died in the middle of a jump: let go of the body where it is instead of taking it back to
        /// the take-off. The death takes it from there (PlayerDeathMotor brings it to the ground).
        /// </summary>
        public void ReleaseForDeath()
        {
            if (remoteJump) { EndRemoteJump(false); return; }
            if (!IsBusy) return;
            if (IsSpawned && IsServer) JumpAbortedClientRpc(string.Empty);
            StopWarp();
            jumpSequence?.Kill();
            jumpSequence = null;
            if (agent != null) agent.enabled = false;
            IsBusy = false;
            inFlight = false;
            cooldownUntil = Time.time + (motionData != null ? motionData.landingCooldown : 0f);
            presentation?.ResetPresentation();
        }

        private void AbortJump(string reason)
        {
            if (cancelling) return;
            cancelling = true;
            if (IsSpawned && IsServer) JumpAbortedClientRpc(reason);
            StopWarp();
            jumpSequence?.Kill();
            jumpSequence = null;
            if (agent != null)
            {
                if (!gameObject.activeInHierarchy)
                {
                    // Deactivated characters must not remain suspended in the air on reactivation.
                    agent.enabled = false;
                    transform.position = takeoffRootPosition;
                    restoreOnEnable = true;
                }
                else if (!RestoreAgent(takeoffRootPosition))
                {
                    agent.enabled = false;
                    transform.position = takeoffRootPosition;
                    Debug.LogWarning("Jump could not restore its NavMeshAgent because the launch surface was removed.", this);
                }
            }
            IsBusy = false;
            inFlight = false;
            cooldownUntil = Time.time + (motionData != null ? motionData.landingCooldown : 0f);
            presentation?.ResetPresentation();
            Message?.Invoke(reason);
            cancelling = false;
        }

        private void OnDisable()
        {
            if (IsBusy) CancelJump();
            jumpSequence?.Kill();
            jumpSequence = null;
        }
    }
}
