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

        public bool IsWeaponDrawn => isWeaponDrawn.Value;
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
            AttachKatanaToSocket(isWeaponDrawn.Value);
        }

        public override void OnNetworkDespawn()
        {
            isWeaponDrawn.OnValueChanged -= HandleWeaponStanceChanged;
        }

        private void Start()
        {
            AttachKatanaToSocket(isWeaponDrawn.Value);
            if (animancer != null)
            {
                bool drawn = isWeaponDrawn.Value;
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

            bool drawn = isWeaponDrawn.Value;
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
            RequestSetWeaponDraw(!isWeaponDrawn.Value);
        }

        public void RequestSetWeaponDraw(bool draw, Action onComplete = null)
        {
            if (isWeaponDrawn.Value == draw)
            {
                onComplete?.Invoke();
                return;
            }

            if (IsSpawned && !IsServer)
            {
                RequestToggleWeaponDrawServerRpc(draw);
            }
            else
            {
                ExecuteToggleWeaponDraw(draw);
            }
        }

        [ServerRpc]
        private void RequestToggleWeaponDrawServerRpc(bool draw)
        {
            ExecuteToggleWeaponDraw(draw);
        }

        private void ExecuteToggleWeaponDraw(bool draw)
        {
            isWeaponDrawn.Value = draw;
            if (IsSpawned)
            {
                PlayWeaponDrawClientRpc(draw);
            }
            else
            {
                PlayWeaponDrawAnimation(draw);
            }
        }

        [ClientRpc]
        private void PlayWeaponDrawClientRpc(bool draw)
        {
            if (IsServer) return; // Server already ran or triggered it
            PlayWeaponDrawAnimation(draw);
        }

        private void PlayWeaponDrawAnimation(bool draw)
        {
            if (activeActionRoutine != null)
            {
                StopCoroutine(activeActionRoutine);
                activeActionRoutine = null;
            }

            activeActionRoutine = StartCoroutine(WeaponDrawRoutine(draw));
        }

        private IEnumerator WeaponDrawRoutine(bool draw)
        {
            currentState = draw ? CharacterAnimState.Equipping : CharacterAnimState.Unequipping;
            AnimationClip clip = draw ? equipClip : unequipClip;

            if (animancer != null && clip != null)
            {
                var state = animancer.Play(clip, 0.15f);
                state.Time = 0f;

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
            if (activeActionRoutine != null)
            {
                StopCoroutine(activeActionRoutine);
                activeActionRoutine = null;
            }

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
            if (activeActionRoutine != null)
            {
                StopCoroutine(activeActionRoutine);
                activeActionRoutine = null;
            }

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
            if (activeActionRoutine != null)
            {
                StopCoroutine(activeActionRoutine);
                activeActionRoutine = null;
            }
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
                if (activeActionRoutine != null)
                {
                    StopCoroutine(activeActionRoutine);
                    activeActionRoutine = null;
                }
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
