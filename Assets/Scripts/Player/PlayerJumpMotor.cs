using System;
using DG.Tweening;
using KryticalNova.Progression;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace KryticalNova.Player
{
    /// <summary>Validated click-to-jump traversal with DOTween timing, Animancer flip, and safe NavMesh restoration.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NavMeshAgent), typeof(PlayerMovementSkills), typeof(PlayerResources))]
    public sealed class PlayerJumpMotor : NetworkBehaviour
    {
        private const float MinimumTravelDistance = 0.15f;
        private const float MinimumTime = 0.01f;
        private const float LandingHeightTolerance = 0.2f;
        private const float FlipFinishMargin = 0.04f;
        private const float MaximumSweepTimeStep = 0.02f;
        [SerializeField] private JumpMotionData motionData;
        [SerializeField] private PlayerJumpPresentation presentation;
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
        public bool IsBusy { get; private set; }
        public event Action<string> Message;

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
            if (IsSpawned && !IsServer)
            {
                reason = string.Empty;
                RequestJumpServerRpc(clickedPoint, obstacleLayers.value, modelForwardOffset);
                return true;
            }

            return ExecuteJumpInternal(clickedPoint, obstacleLayers.value, modelForwardOffset, out reason);
        }

        [ServerRpc]
        private void RequestJumpServerRpc(Vector3 clickedPoint, int obstacleLayersValue, float modelForwardOffset)
        {
            if (ExecuteJumpInternal(clickedPoint, obstacleLayersValue, modelForwardOffset, out _))
            {
                TriggerJumpClientRpc(clickedPoint, obstacleLayersValue, modelForwardOffset);
            }
        }

        [ClientRpc]
        private void TriggerJumpClientRpc(Vector3 clickedPoint, int obstacleLayersValue, float modelForwardOffset)
        {
            if (IsServer) return; // Server already running it
            ExecuteJumpInternal(clickedPoint, obstacleLayersValue, modelForwardOffset, out _);
        }

        private bool ExecuteJumpInternal(Vector3 clickedPoint, int obstacleLayersValue, float modelForwardOffset, out string reason)
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
            peakHeight = Mathf.Max(clearanceApex, startFeet.y + jumpH);
            ascentTime = Mathf.Sqrt(2f * (peakHeight - startFeet.y) / motionData.gravity);
            descentTime = Mathf.Sqrt(2f * (peakHeight - landingFeet.y) / motionData.gravity);
            flightTime = ascentTime + motionData.apexHoldDuration + descentTime;
            if (!ValidateTrajectory()) { reason = "The jump arc or landing area is blocked."; return false; }

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
            return true;
        }

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
            if (IsBusy) AbortJump("Jump cancelled.");
        }

        private void AbortJump(string reason)
        {
            if (cancelling) return;
            cancelling = true;
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
