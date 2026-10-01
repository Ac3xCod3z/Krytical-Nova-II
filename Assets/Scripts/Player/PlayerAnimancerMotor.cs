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

        [Header("Components")]
        [SerializeField] private NavMeshAgent agent;

        // Synchronize weapon stance across all clients
        private readonly NetworkVariable<bool> isWeaponDrawn = new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private CharacterAnimState currentState = CharacterAnimState.Locomotion;
        private AnimationClip currentLocomotionClip;
        private Coroutine activeActionRoutine;
        private Vector3 lastPosition;
        private float currentSpeed;

        // Stance this instance is presenting. Leads the NetworkVariable while a draw/sheath plays
        // (the owner predicts it locally) and follows it otherwise.
        private bool presentedWeaponDrawn;

        public bool IsWeaponDrawn => presentedWeaponDrawn;
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

        private void Update()
        {
            UpdateSpeed();

            if (currentState == CharacterAnimState.Locomotion)
            {
                UpdateLocomotion();
            }
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

            AnimationClip targetClip = targetIdle;
            if (currentSpeed > 0.15f)
            {
                targetClip = currentSpeed >= walkSpeedThreshold ? targetRun : targetWalk;
            }

            if (targetClip != null && targetClip != currentLocomotionClip)
            {
                currentLocomotionClip = targetClip;
                animancer.Play(targetClip, locomotionFadeDuration);
            }
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
            }
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

                var state = animancer.Play(clip, 0.15f);
                state.Time = 0f;
                state.ApplyFootIK = weaponDrawFootIK;

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
