using System;
using System.Collections;
using Animancer;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace KryticalNova.Player
{
    public enum CharacterAnimState
    {
        Locomotion,
        Jumping,
        Equipping,
        Unequipping,
        Attacking
    }

    /// <summary>
    /// Animancer-driven motor controlling character locomotion, multi-stage jump,
    /// and weapon draw/sheath animations with server-authoritative state replication.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerAnimancerMotor : NetworkBehaviour
    {
        [Header("Animancer")]
        [SerializeField] private AnimancerComponent animancer;

        [Header("Locomotion Clips (Sheathed)")]
        [SerializeField] private AnimationClip idleClip;
        [SerializeField] private AnimationClip walkClip;
        [SerializeField] private AnimationClip runClip;

        [Header("Locomotion Clips (Drawn / Unsheathed)")]
        [SerializeField] private AnimationClip drawnIdleClip;
        [SerializeField] private AnimationClip drawnWalkClip;
        [SerializeField] private AnimationClip drawnRunClip;

        [Header("Locomotion Tuning")]
        [SerializeField, Min(0.1f)] private float walkSpeedThreshold = 2.5f;
        [SerializeField, Min(0.01f)] private float locomotionFadeDuration = 0.2f;
        [Tooltip("Speed below which the character counts as stopped.")]
        [SerializeField, Min(0.01f)] private float moveSpeedThreshold = 0.15f;
        [Tooltip("Run only drops to walk once speed is this far below Walk Speed Threshold, so braking doesn't flash the walk clip.")]
        [SerializeField, Min(0f)] private float runToWalkHysteresis = 0.6f;
        [Tooltip("How long a slower gait must persist before switching to it.")]
        [SerializeField, Min(0f)] private float gaitChangeDelay = 0.15f;
        [Tooltip("Humanoid foot IK on idle/walk/run too. It must match the stop clips: blending an IK clip into a non-IK idle " +
                 "drops the feet ~6cm at the end of the fade, which reads as the lower legs jerking down.")]
        [SerializeField] private bool locomotionFootIK = true;
        [Tooltip("Scale walk/run playback to the actual travel speed using each clip's root-motion speed (clips without root motion are left at 1x).")]
        [SerializeField] private bool matchClipSpeedToVelocity = true;
        [SerializeField] private Vector2 locomotionPlaybackRange = new Vector2(0.8f, 1.6f);

        [Header("Stop Transitions (Move -> Idle)")]
        [SerializeField] private AnimationClip runStopClip;
        [SerializeField] private AnimationClip walkStopClip;
        [SerializeField] private AnimationClip drawnRunStopClip;
        [SerializeField] private AnimationClip drawnWalkStopClip;
        [SerializeField, Min(0.01f)] private float stopFadeDuration = 0.12f;
        [Tooltip("Idle fades in over this many seconds once the stop clip reaches its end time.")]
        [SerializeField, Min(0.01f)] private float stopToIdleFadeDuration = 0.2f;
        [Tooltip("Run stop plays only this window (seconds into the clip). The Run_Fast_To_Idle clips open with ~0.45s of " +
                 "full in-place running strides and are fully settled by ~0.95s.")]
        [SerializeField] private Vector2 runStopWindow = new Vector2(0.45f, 0.95f);
        [Tooltip("Walk stop window (seconds into the clip); the last step lands at ~0.52s.")]
        [SerializeField] private Vector2 walkStopWindow = new Vector2(0f, 0.52f);
        [SerializeField, Range(0.5f, 2f)] private float stopPlaybackSpeed = 1.25f;
        [Tooltip("Humanoid foot IK on the stop clips; without it the feet dip ~6cm below the ground on this rig.")]
        [SerializeField] private bool stopFootIK = true;
        [Tooltip("Start the stop clip this far (m) before a NavMesh destination, so the stopping steps line up with arrival.")]
        [SerializeField, Min(0f)] private float stopLeadDistance = 0.35f;

        [Header("Jump Clips")]
        [SerializeField] private AnimationClip jumpStartClip;
        [SerializeField] private AnimationClip jumpLoopClip;
        [SerializeField] private AnimationClip jumpLandClip;
        [SerializeField, Min(0.01f)] private float jumpFadeDuration = 0.1f;

        [Header("Katana Weapon Handling")]
        [SerializeField] private AnimationClip equipClip;
        [SerializeField] private AnimationClip unequipClip;
        [SerializeField] private Transform sheathSocket;
        [SerializeField] private Transform handSocket;
        [SerializeField] private Transform katanaTransform;
        [SerializeField, Range(0.1f, 0.9f)] private float equipReparentNormalizedTime = 0.38f;
        [SerializeField, Range(0.1f, 0.9f)] private float unequipReparentNormalizedTime = 0.45f;
        [Tooltip("Humanoid foot IK on the draw/sheath clips. They come from the Big Sword set and hover or sink on this rig without it.")]
        [SerializeField] private bool weaponDrawFootIK = true;
        [Tooltip("Play draw/sheath on an upper-body layer so the legs keep walking/running underneath (no sliding when combat auto-draws mid-approach).")]
        [SerializeField] private bool drawOnUpperBodyLayer = true;
        [SerializeField, Min(0.01f)] private float weaponDrawFadeDuration = 0.15f;

        [Header("Components")]
        [SerializeField] private NavMeshAgent agent;

        // Synchronize weapon stance across all clients
        private readonly NetworkVariable<bool> isWeaponDrawn = new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private const int UpperBodyLayerIndex = 1;

        private CharacterAnimState currentState = CharacterAnimState.Locomotion;
        private AnimationClip currentLocomotionClip;
        private AnimancerState currentLocomotionState;
        private AvatarMask upperBodyMask;
        private bool drawOnUpperBody;
        private AnimancerState stopState;
        private Vector2 stopWindow;
        private float slowerGaitTimer;
        private Coroutine activeActionRoutine;
        private Vector3 lastPosition;
        private float currentSpeed;

        // Stance this instance is presenting. Leads the NetworkVariable while a draw/sheath plays
        // (the owner predicts it locally) and follows it otherwise.
        private bool presentedWeaponDrawn;

        public bool IsWeaponDrawn => presentedWeaponDrawn;
        /// <summary>Sheathed idle, also used by the equipment preview doll.</summary>
        public AnimationClip IdleClip => idleClip;
        public CharacterAnimState CurrentState => currentState;
        public bool IsActionBusy => currentState == CharacterAnimState.Equipping ||
                                     currentState == CharacterAnimState.Unequipping ||
                                     currentState == CharacterAnimState.Attacking;

        public event Action<bool> OnWeaponStanceChanged;

        private void Awake()
        {
            if (agent == null) agent = GetComponent<NavMeshAgent>();
            if (animancer == null) animancer = GetComponentInChildren<AnimancerComponent>();
            lastPosition = transform.position;
        }

        public override void OnNetworkSpawn()
        {
            isWeaponDrawn.OnValueChanged += HandleWeaponStanceChanged;
            if (IsServer)
            {
                // Carry over a stance chosen before the session started (e.g. drawn while offline)
                isWeaponDrawn.Value = presentedWeaponDrawn;
            }
            else
            {
                presentedWeaponDrawn = isWeaponDrawn.Value;
                AttachKatanaToSocket(presentedWeaponDrawn);
                currentLocomotionClip = null;
            }
        }

        public override void OnNetworkDespawn()
        {
            isWeaponDrawn.OnValueChanged -= HandleWeaponStanceChanged;
        }

        private void Start()
        {
            AttachKatanaToSocket(presentedWeaponDrawn);
            SetupUpperBodyLayer();
            if (animancer != null)
            {
                bool drawn = presentedWeaponDrawn;
                AnimationClip initialIdle = (drawn && drawnIdleClip != null) ? drawnIdleClip : idleClip;
                if (initialIdle != null)
                {
                    animancer.Play(initialIdle);
                    currentLocomotionClip = initialIdle;
                }
            }
        }

        public override void OnDestroy()
        {
            if (upperBodyMask != null) Destroy(upperBodyMask);
            base.OnDestroy();
        }

        /// <summary>Layer 1 masked to spine, head and arms; the base layer keeps driving the legs.</summary>
        private void SetupUpperBodyLayer()
        {
            if (!drawOnUpperBodyLayer || animancer == null || animancer.Animator == null || !animancer.Animator.isHuman) return;

            upperBodyMask = new AvatarMask { name = "Upper Body (runtime)" };
            for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++)
            {
                var part = (AvatarMaskBodyPart)i;
                bool upper = part == AvatarMaskBodyPart.Body || part == AvatarMaskBodyPart.Head
                    || part == AvatarMaskBodyPart.LeftArm || part == AvatarMaskBodyPart.RightArm
                    || part == AvatarMaskBodyPart.LeftFingers || part == AvatarMaskBodyPart.RightFingers
                    || part == AvatarMaskBodyPart.LeftHandIK || part == AvatarMaskBodyPart.RightHandIK;
                upperBodyMask.SetHumanoidBodyPartActive(part, upper);
            }
            animancer.Layers[UpperBodyLayerIndex].Mask = upperBodyMask;
            drawOnUpperBody = true;
        }

        private void Update()
        {
            UpdateSpeed();

            // While a draw/sheath plays on the upper-body layer, the legs keep following movement.
            if (currentState == CharacterAnimState.Locomotion || (drawOnUpperBody && IsWeaponDrawActionPlaying))
            {
                UpdateLocomotion();
            }
            UpdateLocomotionPlaybackSpeed();
        }

        private void UpdateLocomotionPlaybackSpeed()
        {
            if (!matchClipSpeedToVelocity || currentLocomotionState == null || stopState != null) return;
            AnimationClip clip = currentLocomotionClip;
            if (clip == null || clip == idleClip || clip == drawnIdleClip) return;
            float nativeSpeed = clip.averageSpeed.magnitude;
            if (nativeSpeed < 0.1f) return; // in-place clip: no reference speed
            currentLocomotionState.Speed = Mathf.Clamp(currentSpeed / nativeSpeed, locomotionPlaybackRange.x, locomotionPlaybackRange.y);
        }

        private void UpdateSpeed()
        {
            if (agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh)
            {
                currentSpeed = agent.velocity.magnitude;
            }
            else
            {
                float delta = Time.deltaTime > 0f ? Time.deltaTime : 0.02f;
                currentSpeed = ((transform.position - lastPosition) / delta).magnitude;
            }
            lastPosition = transform.position;
        }

        private void UpdateLocomotion()
        {
            if (animancer == null) return;

            bool drawn = presentedWeaponDrawn;
            AnimationClip targetIdle = (drawn && drawnIdleClip != null) ? drawnIdleClip : idleClip;
            AnimationClip targetWalk = (drawn && drawnWalkClip != null) ? drawnWalkClip : walkClip;
            AnimationClip targetRun = (drawn && drawnRunClip != null) ? drawnRunClip : runClip;

            // --- Moving: pick a gait, with hysteresis so braking doesn't flash run -> walk -> idle ---
            if (IsMoving())
            {
                AnimationClip targetClip = currentSpeed >= walkSpeedThreshold ? targetRun : targetWalk;

                bool wasRunning = currentLocomotionClip == runClip || currentLocomotionClip == drawnRunClip;
                if (wasRunning && targetClip == targetWalk)
                {
                    slowerGaitTimer += Time.deltaTime;
                    if (currentSpeed > walkSpeedThreshold - runToWalkHysteresis || slowerGaitTimer < gaitChangeDelay)
                        targetClip = targetRun;
                }
                else
                {
                    slowerGaitTimer = 0f;
                }

                stopState = null;
                PlayLocomotionClip(targetClip, locomotionFadeDuration);
                return;
            }

            slowerGaitTimer = 0f;

            // --- Stopping: play the move -> idle clip for the gait we were in, then settle into idle ---
            if (stopState == null && currentLocomotionClip != targetIdle && currentLocomotionClip != null)
            {
                AnimationClip stopClip = GetStopClip(currentLocomotionClip, out stopWindow);
                if (stopClip != null)
                {
                    stopState = animancer.Layers[0].Play(stopClip, stopFadeDuration);
                    stopState.Time = Mathf.Clamp(stopWindow.x, 0f, stopClip.length);
                    stopState.Speed = stopPlaybackSpeed;
                    stopState.ApplyFootIK = stopFootIK;
                    currentLocomotionClip = stopClip;
                    currentLocomotionState = stopState;
                    return;
                }
            }

            if (stopState != null)
            {
                float endTime = Mathf.Min(stopWindow.y > stopWindow.x ? stopWindow.y : stopState.Length, stopState.Length);
                if (stopState.IsPlaying && stopState.Time < endTime)
                    return;

                stopState = null;
                PlayLocomotionClip(targetIdle, stopToIdleFadeDuration);
                return;
            }

            PlayLocomotionClip(targetIdle, locomotionFadeDuration);
        }

        private void PlayLocomotionClip(AnimationClip clip, float fade)
        {
            if (clip == null || clip == currentLocomotionClip) return;
            currentLocomotionClip = clip;
            currentLocomotionState = animancer.Layers[0].Play(clip, fade);
            currentLocomotionState.ApplyFootIK = locomotionFootIK;
            currentLocomotionState.Speed = 1f;
        }

        private AnimationClip GetStopClip(AnimationClip fromClip, out Vector2 window)
        {
            bool fromRun = fromClip == runClip || fromClip == drawnRunClip;
            window = fromRun ? runStopWindow : walkStopWindow;
            if (fromClip == runClip) return runStopClip;
            if (fromClip == drawnRunClip) return drawnRunStopClip;
            if (fromClip == walkClip) return walkStopClip;
            if (fromClip == drawnWalkClip) return drawnWalkStopClip;
            return null;
        }

        /// <summary>
        /// Moving = actual speed above threshold. When this instance drives a NavMeshAgent, arrival is
        /// predicted from the remaining path so the stop clip begins just before the agent halts.
        /// </summary>
        private bool IsMoving()
        {
            if (currentSpeed <= moveSpeedThreshold) return false;

            if (agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh && !agent.pathPending && agent.hasPath)
            {
                // remainingDistance reads 0 for the first frame after SetPath; don't mistake that for arrival.
                Vector3 toDestination = agent.destination - agent.nextPosition;
                toDestination.y = 0f;
                float remaining = Mathf.Max(agent.remainingDistance, toDestination.magnitude);
                if (remaining <= agent.stoppingDistance + stopLeadDistance)
                    return false;
            }

            return true;
        }

        #region Weapon Draw & Sheath

        public void RequestToggleWeaponDraw()
        {
            RequestSetWeaponDraw(!presentedWeaponDrawn);
        }

        public void RequestSetWeaponDraw(bool draw, Action onComplete = null)
        {
            if (presentedWeaponDrawn == draw)
            {
                onComplete?.Invoke();
                return;
            }

            // Offline, host and owning client all play the draw/sheath immediately;
            // remote observers get it from PlayWeaponDrawClientRpc.
            PlayWeaponDrawAnimation(draw);

            if (!IsSpawned)
            {
                isWeaponDrawn.Value = draw;
            }
            else if (IsServer)
            {
                ServerApplyWeaponDraw(draw);
            }
            else
            {
                RequestToggleWeaponDrawServerRpc(draw);
            }
        }

        [ServerRpc]
        private void RequestToggleWeaponDrawServerRpc(bool draw)
        {
            if (isWeaponDrawn.Value == draw) return;

            // The server's own view of this remote player
            PlayWeaponDrawAnimation(draw);
            ServerApplyWeaponDraw(draw);
        }

        private void ServerApplyWeaponDraw(bool draw)
        {
            isWeaponDrawn.Value = draw;
            PlayWeaponDrawClientRpc(draw);
        }

        [ClientRpc]
        private void PlayWeaponDrawClientRpc(bool draw)
        {
            if (IsServer) return;  // server/host already played it in RequestSetWeaponDraw or the ServerRpc
            if (IsOwner) return;   // owner predicted it locally when requesting
            PlayWeaponDrawAnimation(draw);
        }

        private void PlayWeaponDrawAnimation(bool draw)
        {
            StopActiveActionRoutine();

            bool changed = presentedWeaponDrawn != draw;
            presentedWeaponDrawn = draw;
            activeActionRoutine = StartCoroutine(WeaponDrawRoutine(draw));
            if (changed) OnWeaponStanceChanged?.Invoke(draw);
        }

        /// <summary>
        /// Stops the running action coroutine. An interrupted draw/sheath (jump, attack) still
        /// finishes its katana swap so the blade is never left on the wrong socket.
        /// </summary>
        private void StopActiveActionRoutine()
        {
            if (activeActionRoutine != null)
            {
                StopCoroutine(activeActionRoutine);
                activeActionRoutine = null;
            }

            if (IsWeaponDrawActionPlaying)
            {
                AttachKatanaToSocket(presentedWeaponDrawn);
                currentState = CharacterAnimState.Locomotion;
                currentLocomotionClip = null;
                FadeOutUpperBody();
            }
        }

        private void FadeOutUpperBody()
        {
            if (!drawOnUpperBody || animancer == null) return;
            animancer.Layers[UpperBodyLayerIndex].StartFade(0f, weaponDrawFadeDuration);
        }

        private bool IsWeaponDrawActionPlaying =>
            currentState == CharacterAnimState.Equipping || currentState == CharacterAnimState.Unequipping;

        private IEnumerator WeaponDrawRoutine(bool draw)
        {
            currentState = draw ? CharacterAnimState.Equipping : CharacterAnimState.Unequipping;
            AnimationClip clip = draw ? equipClip : unequipClip;

            if (animancer != null && clip != null)
            {
                // The katana starts on the socket it is being taken from and only swaps at the reparent point
                AttachKatanaToSocket(!draw);

                var state = drawOnUpperBody
                    ? animancer.Layers[UpperBodyLayerIndex].Play(clip, weaponDrawFadeDuration)
                    : animancer.Play(clip, weaponDrawFadeDuration);
                state.Time = 0f;
                state.ApplyFootIK = !drawOnUpperBody && weaponDrawFootIK;

                float reparentNorm = draw ? equipReparentNormalizedTime : unequipReparentNormalizedTime;
                float reparentTime = clip.length * reparentNorm;
                float elapsed = 0f;
                bool reparented = false;

                while (elapsed < clip.length)
                {
                    elapsed += Time.deltaTime;
                    if (!reparented && elapsed >= reparentTime)
                    {
                        reparented = true;
                        AttachKatanaToSocket(draw);
                    }
                    yield return null;
                }

                if (!reparented)
                {
                    AttachKatanaToSocket(draw);
                }
            }
            else
            {
                AttachKatanaToSocket(draw);
            }

            currentState = CharacterAnimState.Locomotion;
            if (drawOnUpperBody)
            {
                // The legs never left locomotion; just hand the arms back. Re-pick the clip set for the new stance.
                FadeOutUpperBody();
            }
            currentLocomotionClip = null;
            UpdateLocomotion();
            activeActionRoutine = null;
        }

        private void HandleWeaponStanceChanged(bool oldVal, bool newVal)
        {
            // A draw/sheath animation that is playing owns the katana and presented stance; it finishes on its own.
            if (IsWeaponDrawActionPlaying) return;
            if (presentedWeaponDrawn == newVal) return;

            // Authoritative correction (late join, rejected prediction, missed RPC): snap to the server's stance
            presentedWeaponDrawn = newVal;
            AttachKatanaToSocket(newVal);
            currentLocomotionClip = null;
            if (currentState == CharacterAnimState.Locomotion)
            {
                UpdateLocomotion();
            }
            OnWeaponStanceChanged?.Invoke(newVal);
        }

        public void AttachKatanaToSocket(bool inHand)
        {
            if (katanaTransform == null) return;

            Transform targetSocket = inHand ? handSocket : sheathSocket;
            if (targetSocket == null) return;

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                katanaTransform.position = targetSocket.position;
                katanaTransform.rotation = targetSocket.rotation;
                return;
            }
#endif

            katanaTransform.SetParent(targetSocket, false);
            katanaTransform.localPosition = Vector3.zero;
            katanaTransform.localRotation = Quaternion.identity;
            katanaTransform.localScale = Vector3.one;
        }

        #endregion

        #region Jump Animations

        public void PlayJumpTakeoff()
        {
            StopActiveActionRoutine();

            activeActionRoutine = StartCoroutine(JumpTakeoffRoutine());
        }

        private IEnumerator JumpTakeoffRoutine()
        {
            currentState = CharacterAnimState.Jumping;
            if (animancer != null && jumpStartClip != null)
            {
                var state = animancer.Play(jumpStartClip, jumpFadeDuration);
                state.Time = 0f;
                yield return new WaitForSeconds(jumpStartClip.length);
            }

            PlayJumpAirborne();
        }

        public void PlayJumpAirborne()
        {
            if (currentState != CharacterAnimState.Jumping) return;

            if (animancer != null && jumpLoopClip != null)
            {
                animancer.Play(jumpLoopClip, 0.15f);
            }
        }

        public void PlayJumpLanding()
        {
            StopActiveActionRoutine();

            activeActionRoutine = StartCoroutine(JumpLandingRoutine());
        }

        private IEnumerator JumpLandingRoutine()
        {
            if (animancer != null && jumpLandClip != null)
            {
                var state = animancer.Play(jumpLandClip, jumpFadeDuration);
                state.Time = 0f;
                yield return new WaitForSeconds(jumpLandClip.length);
            }

            FinishJump();
            activeActionRoutine = null;
        }

        public void ResetJumpAnimation()
        {
            StopActiveActionRoutine();
            FinishJump();
        }

        private void FinishJump()
        {
            currentState = CharacterAnimState.Locomotion;
            currentLocomotionClip = null;
        }

        #endregion

        #region Attack Action Hooks

        public void SetAttackingState(bool attacking)
        {
            if (attacking)
            {
                StopActiveActionRoutine();
                currentState = CharacterAnimState.Attacking;
            }
            else if (currentState == CharacterAnimState.Attacking)
            {
                currentState = CharacterAnimState.Locomotion;
                currentLocomotionClip = null;
            }
        }

        #endregion

        public void ConfigureReferences(
            AnimancerComponent animancerComp,
            AnimationClip idle,
            AnimationClip walk,
            AnimationClip run,
            AnimationClip drawnIdle,
            AnimationClip drawnWalk,
            AnimationClip drawnRun,
            AnimationClip jStart,
            AnimationClip jLoop,
            AnimationClip jLand,
            AnimationClip eq,
            AnimationClip unEq,
            Transform sheath,
            Transform hand,
            Transform katana)
        {
            animancer = animancerComp;
            idleClip = idle;
            walkClip = walk;
            runClip = run;
            drawnIdleClip = drawnIdle;
            drawnWalkClip = drawnWalk;
            drawnRunClip = drawnRun;
            jumpStartClip = jStart;
            jumpLoopClip = jLoop;
            jumpLandClip = jLand;
            equipClip = eq;
            unequipClip = unEq;
            sheathSocket = sheath;
            handSocket = hand;
            katanaTransform = katana;
        }
    }
}
