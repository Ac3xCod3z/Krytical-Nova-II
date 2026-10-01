using System;
using System.Collections.Generic;
using KryticalNova.Core;
using KryticalNova.Progression;
using Unity.Cinemachine;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace KryticalNova.Player
{
    /// <summary>Moves to clicked ground, orbits during right-button drags, and zooms with the mouse wheel.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NavMeshAgent), typeof(PlayerInput))]
    public sealed class PointAndClickPlayer : NetworkBehaviour
    {
        private const string ActionMapName = "PointAndClick";
        private const string PointerActionName = "PointerPosition";
        private const string MoveActionName = "MoveToPoint";
        private const string OrbitHeldActionName = "OrbitHeld";
        private const string OrbitDeltaActionName = "OrbitDelta";
        private const string CancelOrbitActionName = "CancelOrbit";
        private const string ZoomActionName = "Zoom";
        private const string ToggleWeaponActionName = "ToggleWeapon";
        private const string GroundLayerName = "Ground";
        private const float DefaultRayDistance = 1000f;
        private const float DefaultSampleRadius = 1f;
        private const float SpawnSampleRadius = 3f;
        private const float DefaultOrbitSensitivity = 0.15f;
        private const float DefaultMinimumZoomDistance = 3f;
        private const float DefaultMaximumZoomDistance = 18f;
        private const float DefaultZoomSensitivity = 1f;
        private const float FixedRadialMultiplier = 1f;
        private const float MinimumFacingSpeedSquared = 0.01f;
        private const float MinimumPositiveValue = 0.01f;
        private const float PausedTimeScale = 0f;
        private const int InitialUiHitCapacity = 8;

        [Header("Scene References")]
        [SerializeField] private Camera gameplayCamera;
        [SerializeField] private CinemachineOrbitalFollow orbitCamera;

        [Header("Click Movement")]
        [Tooltip("Only the first raycast hit on these layers can be a destination.")]
        [SerializeField] private LayerMask groundLayers;
        [Tooltip("Include walls as well as ground so clicks cannot pass through obstacles. Exclude the player.")]
        [SerializeField] private LayerMask raycastLayers = Physics.DefaultRaycastLayers;
        [SerializeField, Min(MinimumPositiveValue)] private float maximumRayDistance = DefaultRayDistance;
        [SerializeField, Min(MinimumPositiveValue)] private float destinationSampleRadius = DefaultSampleRadius;
        [Tooltip("Use 180 for a model facing local -Z; use 0 for a model facing local +Z.")]
        [SerializeField] private float modelForwardOffset;

        [Header("Right-Button Orbit")]
        [Tooltip("Degrees per mouse pixel; mouse delta is already frame-integrated.")]
        [SerializeField, Min(MinimumPositiveValue)] private float orbitSensitivity = DefaultOrbitSensitivity;
        [SerializeField] private bool invertVerticalOrbit;
        [SerializeField] private bool lockCursorWhileOrbiting = true;

        [Header("Mouse-Wheel Zoom")]
        [Tooltip("Closest requested distance from the camera target. Obstacle avoidance may pull the camera closer.")]
        [SerializeField, Min(MinimumPositiveValue)] private float minimumZoomDistance = DefaultMinimumZoomDistance;
        [Tooltip("Farthest requested distance from the camera target.")]
        [SerializeField, Min(MinimumPositiveValue)] private float maximumZoomDistance = DefaultMaximumZoomDistance;
        [Tooltip("World units per normalized scroll increment. Positive scroll zooms in. Uses the Input System's default uniform scroll range.")]
        [SerializeField, Min(MinimumPositiveValue)] private float zoomSensitivity = DefaultZoomSensitivity;

        private readonly List<RaycastResult> uiHits = new List<RaycastResult>(InitialUiHitCapacity);
        private NavMeshAgent agent;
        private PlayerInput playerInput;
        private PlayerMovementSkills movementSkills;
        private PlayerResources resources;
        private PlayerJumpMotor jumpMotor;
        private CharacterCombatStats combatStats;
        private CharacterBuffManager buffManager;
        private PlayerAnimancerMotor animancerMotor;
        private KryticalNova.Combat.PlayerMeleeCombat meleeCombat;
        private MovementSkillId previousMovementMode;
        public event Action<string> CommandMessage;
        private NavMeshPath requestedPath;
        private InputActionMap gameplayActions;
        private InputAction pointerAction;
        private InputAction moveAction;
        private InputAction orbitHeldAction;
        private InputAction orbitDeltaAction;
        private InputAction cancelOrbitAction;
        private InputAction zoomAction;
        private InputAction toggleWeaponAction;
        private PlayerSkillHotbar hotbar;
        private EventSystem pointerEventSystem;
        private PointerEventData pointerEventData;
        private bool isOrbiting;
        private bool ownsCursor;
        private CursorLockMode previousCursorLock;
        private bool previousCursorVisibility;
        private Vector2 previousCursorPosition;

        private void Reset()
        {
            groundLayers = LayerMask.GetMask(GroundLayerName);
        }

        private void OnValidate()
        {
            ValidateZoomSettings();
        }

        private void ValidateZoomSettings()
        {
            minimumZoomDistance = Mathf.Max(MinimumPositiveValue, minimumZoomDistance);
            maximumZoomDistance = Mathf.Max(minimumZoomDistance, maximumZoomDistance);
            zoomSensitivity = Mathf.Max(MinimumPositiveValue, zoomSensitivity);
        }

        private void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            // Runtime navigation is built in Start; do not attach the agent before it exists.
            agent.enabled = false;
            playerInput = GetComponent<PlayerInput>();
            movementSkills = GetComponent<PlayerMovementSkills>();
            resources = GetComponent<PlayerResources>();
            jumpMotor = GetComponent<PlayerJumpMotor>();
            combatStats = GetComponent<CharacterCombatStats>();
            buffManager = GetComponent<CharacterBuffManager>();
            animancerMotor = GetComponent<PlayerAnimancerMotor>();
            meleeCombat = GetComponent<KryticalNova.Combat.PlayerMeleeCombat>();
            hotbar = GetComponent<PlayerSkillHotbar>();
            if (meleeCombat != null)
            {
                meleeCombat.OnCombatMessage += msg => CommandMessage?.Invoke(msg);
            }
            requestedPath = new NavMeshPath();
            agent.updateRotation = false;
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            if (!IsOwner)
            {
                // Disable input and camera controls on remote player proxies
                if (playerInput != null) playerInput.enabled = false;
                if (!IsServer && agent != null)
                {
                    // Proxies on clients follow NetworkTransform; disable NavMeshAgent so it does not fight replication
                    agent.enabled = false;
                }
                return;
            }

            // For the local owner player: free the keyboard/mouse from the offline scene avatar first.
            // (PlayerInput ships disabled on the prefab so it never pairs before this point.)
            KryticalNova.Networking.OfflinePlayerHandoff.ReleaseOfflineAvatar();
            if (playerInput != null) playerInput.enabled = true;
            SetupLocalCamera();
        }

        private void SetupLocalCamera()
        {
            if (orbitCamera == null)
            {
                orbitCamera = FindAnyObjectByType<CinemachineOrbitalFollow>();
            }
            if (orbitCamera != null)
            {
                var cameraTarget = transform.Find("Camera Target");
                Transform targetTransform = cameraTarget != null ? cameraTarget : transform;
                var cinemachineCamera = orbitCamera.GetComponent<CinemachineCamera>();
                if (cinemachineCamera != null)
                {
                    cinemachineCamera.Target.TrackingTarget = targetTransform;
                }
            }

        }

        private void Start()
        {
            if (gameplayCamera == null)
                gameplayCamera = Camera.main;

            if (IsSpawned && !IsOwner)
            {
                // Remote proxy does not need input action maps or camera handling
                return;
            }

            SetupLocalCamera();

            // Offline avatar: PlayerInput ships disabled on the prefab, so the local, unspawned player enables it.
            if (!IsSpawned && playerInput != null)
                playerInput.enabled = true;

            if (playerInput == null || playerInput.actions == null)
            {
                Debug.LogError("PointAndClickPlayer requires PlayerInput with configured actions.", this);
                enabled = false;
                return;
            }

            gameplayActions = playerInput.actions.FindActionMap(ActionMapName, true);
            pointerAction = gameplayActions.FindAction(PointerActionName, true);
            moveAction = gameplayActions.FindAction(MoveActionName, true);
            orbitHeldAction = gameplayActions.FindAction(OrbitHeldActionName, true);
            orbitDeltaAction = gameplayActions.FindAction(OrbitDeltaActionName, true);
            cancelOrbitAction = gameplayActions.FindAction(CancelOrbitActionName, true);
            zoomAction = gameplayActions.FindAction(ZoomActionName, true);
            toggleWeaponAction = gameplayActions.FindAction(ToggleWeaponActionName, true);

            if (orbitCamera != null)
            {
                // No recentering or input acceleration: releasing RMB keeps the chosen orbit angles.
                orbitCamera.HorizontalAxis.Recentering.Enabled = false;
                orbitCamera.VerticalAxis.Recentering.Enabled = false;
                orbitCamera.RadialAxis.Recentering.Enabled = false;

                // Control distance through Radius, with a fixed multiplier so limits use world units.
                ValidateZoomSettings();
                orbitCamera.RadialAxis.Range = Vector2.one * FixedRadialMultiplier;
                orbitCamera.RadialAxis.Value = FixedRadialMultiplier;
                orbitCamera.RadialAxis.Center = FixedRadialMultiplier;
                orbitCamera.RadialAxis.Wrap = false;
                orbitCamera.Radius = Mathf.Clamp(orbitCamera.Radius, minimumZoomDistance, maximumZoomDistance);
            }

            if (!IsSpawned || IsServer)
            {
                if (!PlaceAgentOnNavigation())
                    enabled = false;
            }
        }

        private bool PlaceAgentOnNavigation()
        {
            if (agent.isActiveAndEnabled && agent.isOnNavMesh)
                return true;

            var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };
            Vector3 feetPosition = transform.position - Vector3.up * agent.baseOffset;
            if (!NavMesh.SamplePosition(feetPosition, out NavMeshHit spawnHit, SpawnSampleRadius, filter))
            {
                Debug.LogError("No NavMesh below the player. Enable RuntimeNavMeshBuilder or bake the scene's NavMeshSurface.", this);
                return false;
            }

            transform.position = spawnHit.position + Vector3.up * agent.baseOffset;
            agent.enabled = true;
            if (agent.isOnNavMesh)
                return true;

            Debug.LogError("The player could not attach to the NavMesh. Check the agent type and spawn position.", this);
            agent.enabled = false;
            return false;
        }

        private void Update()
        {
            if (IsSpawned && !IsOwner && !IsServer)
                return;

            if (!IsSpawned || IsServer)
            {
                ApplyMovementSettings();
                TurnTowardsTravel();
            }

            if (IsSpawned && !IsOwner)
                return;

            if (gameplayActions == null || !gameplayActions.enabled || !playerInput.inputIsActive
                || !Application.isFocused || Time.timeScale <= PausedTimeScale)
            {
                EndOrbit();
                return;
            }

            bool inputSuppressed = hotbar != null && hotbar.InputSuppressed;

            // Escape (CancelOrbit action) is the single cancel source: ends orbit and stops any melee attack.
            if (cancelOrbitAction.WasPressedThisFrame())
            {
                EndOrbit();
                if (!inputSuppressed) meleeCombat?.CancelAttack();
                return;
            }

            Vector2 screenPosition = pointerAction.ReadValue<Vector2>();
            if (orbitCamera != null)
            {
                ApplyZoom(screenPosition);
            }

            if (isOrbiting)
            {
                if (!orbitHeldAction.IsPressed()
                    || (ownsCursor && Cursor.lockState != CursorLockMode.Locked))
                    EndOrbit();
                else
                    ApplyOrbitDelta();

                return;
            }

            if (orbitHeldAction.WasPressedThisFrame())
            {
                if (!IsPointerOverUi(screenPosition))
                    BeginOrbit(screenPosition);

                // Discard the initial frame's delta, including any cursor-lock warp.
                return;
            }

            if (!inputSuppressed && toggleWeaponAction.WasPressedThisFrame())
            {
                animancerMotor?.RequestToggleWeaponDraw();
            }

            if (!orbitHeldAction.IsPressed() && moveAction.WasPressedThisFrame()
                && !IsPointerOverUi(screenPosition))
                TryMoveToPointer(screenPosition);
        }

        private void ApplyZoom(Vector2 screenPosition)
        {
            if (orbitCamera == null || gameplayCamera == null) return;

            orbitCamera.Radius = Mathf.Clamp(orbitCamera.Radius, minimumZoomDistance, maximumZoomDistance);
            float scrollDelta = zoomAction.ReadValue<float>();
            if (Mathf.Approximately(scrollDelta, 0f))
                return;

            // A locked orbit drag already owns the pointer; otherwise UI consumes scrolling.
            bool hasCapturedPointer = isOrbiting && ownsCursor && orbitHeldAction.IsPressed()
                && Cursor.lockState == CursorLockMode.Locked;
            if (!hasCapturedPointer
                && (!gameplayCamera.pixelRect.Contains(screenPosition) || IsPointerOverUi(screenPosition)))
                return;

            // Scroll is a per-update delta. Multiplying by deltaTime would make zoom frame-rate dependent.
            orbitCamera.Radius = Mathf.Clamp(
                orbitCamera.Radius - scrollDelta * zoomSensitivity,
                minimumZoomDistance,
                maximumZoomDistance);
        }

        private void TryMoveToPointer(Vector2 screenPosition)
        {
            if (resources != null && !resources.IsAlive) { CommandMessage?.Invoke("No HP remaining. Restore HP before moving."); return; }
            if (jumpMotor != null && jumpMotor.IsBusy) { CommandMessage?.Invoke("Finish this jump before choosing another destination."); return; }
            if (gameplayCamera == null || !gameplayCamera.pixelRect.Contains(screenPosition))
                return;

            Ray ray = gameplayCamera.ScreenPointToRay(screenPosition);
            if (!Physics.Raycast(ray, out RaycastHit hit, maximumRayDistance, raycastLayers, QueryTriggerInteraction.Ignore))
                return;

            var targetEntity = hit.collider.GetComponentInParent<IDamageable>();
            if (targetEntity != null && targetEntity.Transform != transform)
            {
                if (!targetEntity.IsAlive)
                {
                    CommandMessage?.Invoke($"{targetEntity.EntityName} is defeated.");
                    return;
                }

                if (meleeCombat != null)
                {
                    meleeCombat.StartAttackTarget(targetEntity);
                    if (IsSpawned && !IsServer)
                    {
                        var targetNetObj = targetEntity.Transform.GetComponent<NetworkObject>();
                        if (targetNetObj != null)
                        {
                            RequestAttackEntityServerRpc(targetNetObj.NetworkObjectId);
                        }
                    }
                    return;
                }

                if (IsSpawned && !IsServer)
                {
                    var targetNetObj = targetEntity.Transform.GetComponent<NetworkObject>();
                    if (targetNetObj != null)
                    {
                        RequestAttackEntityServerRpc(targetNetObj.NetworkObjectId);
                        return;
                    }
                }

                ExecuteAttackOnTarget(targetEntity);
                return;
            }

            // Clicking ground or platform clears active combat attack
            meleeCombat?.CancelAttack();

            if ((groundLayers.value & (1 << hit.collider.gameObject.layer)) == 0)
            { CommandMessage?.Invoke("Choose a walkable ground or platform surface."); return; }

            // Moving interrupts channeled skills (e.g. Heavenly Serenity meditation)
            if (buffManager == null) buffManager = GetComponent<CharacterBuffManager>();
            if (buffManager != null && buffManager.IsChanneling)
            {
                buffManager.InterruptChanneled();
            }

            if (movementSkills != null && movementSkills.IsJumpSelected)
            {
                if (jumpMotor == null) { CommandMessage?.Invoke("Jump controller is missing."); return; }
                if (!jumpMotor.TryJump(hit.point, raycastLayers, modelForwardOffset, out string reason)) CommandMessage?.Invoke(reason);
                return;
            }

            if (IsSpawned && !IsServer)
            {
                bool isRunning = movementSkills != null && movementSkills.IsRunning;
                RequestMoveDestinationServerRpc(hit.point, isRunning);
                return;
            }

            ExecuteLocalMove(hit.point);
        }

        private void ExecuteLocalMove(Vector3 destinationPoint)
        {
            if (!agent.isActiveAndEnabled || !agent.isOnNavMesh) return;

            var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };
            if (!NavMesh.SamplePosition(destinationPoint, out NavMeshHit destination, destinationSampleRadius, filter)
                || !agent.CalculatePath(destination.position, requestedPath) || requestedPath.status != NavMeshPathStatus.PathComplete)
            { CommandMessage?.Invoke("No complete walking path to that point."); return; }
            if (Vector3.Distance(agent.nextPosition, destination.position + Vector3.up * agent.baseOffset) <= agent.stoppingDistance)
                return;

            float paidCost = 0f;
            if (movementSkills != null && movementSkills.IsRunning
                && !movementSkills.TryPayForCommand(MovementSkillId.Run, out paidCost, out string paymentReason))
            { CommandMessage?.Invoke(paymentReason); return; }
            agent.isStopped = false;
            if (!agent.SetPath(requestedPath))
            {
                resources?.RestoreChi(paidCost);
                CommandMessage?.Invoke("The path changed. Chi was not consumed.");
            }
        }

        [ServerRpc]
        private void RequestMoveDestinationServerRpc(Vector3 destinationPoint, bool isRunning)
        {
            if (resources != null && !resources.IsAlive) return;
            if (jumpMotor != null && jumpMotor.IsBusy) return;
            if (agent == null || !agent.isActiveAndEnabled || !agent.isOnNavMesh) return;

            var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };
            if (!NavMesh.SamplePosition(destinationPoint, out NavMeshHit destination, destinationSampleRadius, filter))
                return;

            if (!agent.CalculatePath(destination.position, requestedPath) || requestedPath.status != NavMeshPathStatus.PathComplete)
                return;

            if (Vector3.Distance(agent.nextPosition, destination.position + Vector3.up * agent.baseOffset) <= agent.stoppingDistance)
                return;

            float paidCost = 0f;
            if (isRunning && movementSkills != null
                && !movementSkills.TryPayForCommand(MovementSkillId.Run, out paidCost, out _))
            {
                return;
            }

            agent.isStopped = false;
            if (!agent.SetPath(requestedPath))
            {
                resources?.RestoreChi(paidCost);
            }
        }

        [ServerRpc]
        private void RequestAttackEntityServerRpc(ulong targetNetworkObjectId)
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(targetNetworkObjectId, out var targetNetObj))
                return;

            var targetEntity = targetNetObj.GetComponent<IDamageable>();
            if (targetEntity == null || !targetEntity.IsAlive) return;

            if (meleeCombat != null)
            {
                meleeCombat.StartAttackTarget(targetEntity);
                return;
            }

            ExecuteAttackOnTarget(targetEntity);
        }

        private void ExecuteAttackOnTarget(IDamageable targetEntity)
        {
            if (meleeCombat != null)
            {
                meleeCombat.StartAttackTarget(targetEntity);
                return;
            }

            if (agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh)
            {
                agent.isStopped = true;
                agent.ResetPath();
            }

            Vector3 targetDir = targetEntity.Transform.position - transform.position;
            targetDir.y = 0f;
            if (targetDir.sqrMagnitude > 0.01f)
            {
                transform.rotation = Quaternion.LookRotation(targetDir, Vector3.up) * Quaternion.AngleAxis(modelForwardOffset, Vector3.up);
            }

            var attackerStats = combatStats != null ? combatStats : GetComponent<CharacterCombatStats>();
            if (attackerStats != null && targetEntity.CombatStats != null)
            {
                if (buffManager == null) buffManager = GetComponent<CharacterBuffManager>();
                var defenderBuffs = targetEntity.Transform.GetComponent<CharacterBuffManager>();

                float refChance = 0f;
                float shatChance = 0f;
                float dispelChance = 0f;
                bool defenderReflect = defenderBuffs != null && defenderBuffs.HasReflection(out refChance, out _);
                bool attackerShatter = buffManager != null && buffManager.HasShieldShatter(out shatChance, out dispelChance);

                var result = CombatCalculator.CalculateAttack(
                    attackerStats,
                    targetEntity.CombatStats,
                    1.0f,
                    defenderReflect,
                    refChance,
                    attackerShatter,
                    shatChance);

                targetEntity.TakeDamage(result, attackerStats);

                if (attackerShatter && dispelChance > 0f && defenderBuffs != null)
                {
                    if (UnityEngine.Random.Range(0f, 100f) <= dispelChance)
                    {
                        defenderBuffs.DispelStanceBuffs();
                    }
                }

                ClientPresentationRegistry.Feedback?.ShowCombatFeedback(targetEntity.Transform.position, result);

                if (result.IsEvaded)
                    CommandMessage?.Invoke($"Attack EVADED by {targetEntity.EntityName}!");
                else if (result.IsReflected)
                    CommandMessage?.Invoke($"Attack REFLECTED by {targetEntity.EntityName}'s Heavenly Repulsion!");
                else if (result.IsCritical)
                    CommandMessage?.Invoke($"CRITICAL HIT on {targetEntity.EntityName} for {result.DamageDealt:0} dmg! (HP: {targetEntity.CurrentHealth:0}/{targetEntity.MaxHealth:0})");
                else
                    CommandMessage?.Invoke($"Hit {targetEntity.EntityName} for {result.DamageDealt:0} dmg! (HP: {targetEntity.CurrentHealth:0}/{targetEntity.MaxHealth:0})");
            }
        }

        private void TurnTowardsTravel()
        {
            if (agent == null || !agent.isActiveAndEnabled || !agent.isOnNavMesh)
                return;

            Vector3 direction = agent.desiredVelocity;
            direction.y = 0f;
            if (direction.sqrMagnitude < MinimumFacingSpeedSquared)
                return;

            Quaternion facing = Quaternion.LookRotation(direction, Vector3.up)
                * Quaternion.AngleAxis(modelForwardOffset, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, facing, agent.angularSpeed * Time.deltaTime);
        }

        /// <summary>Releases an active camera drag when a UI window takes control of the cursor.</summary>
        public void ReleaseCameraControl()
        {
            EndOrbit();
        }

        private void ApplyMovementSettings()
        {
            if (movementSkills == null || !movementSkills.isActiveAndEnabled || movementSkills.MovementSettings == null)
                return;
            if (resources != null && !resources.IsAlive)
            {
                jumpMotor?.CancelJump();
                if (agent.isActiveAndEnabled && agent.isOnNavMesh) agent.ResetPath();
                movementSkills.SelectWalk();
            }
            if (jumpMotor != null && jumpMotor.IsBusy) return;
            MovementSkillId currentMode = movementSkills.SelectedSkill;
            if (currentMode != previousMovementMode)
            {
                // Selecting Jump stops an old walking route so the next click is measured from a stable takeoff point.
                if (currentMode == MovementSkillId.Jump && agent.isActiveAndEnabled && agent.isOnNavMesh) agent.ResetPath();
                // Hotbar selection is free. Chi is charged only when a destination click is accepted.
                previousMovementMode = movementSkills.SelectedSkill;
            }
            MovementSettings settings = movementSkills.MovementSettings;
            if (agent != null && agent.isActiveAndEnabled)
            {
                agent.speed = movementSkills.CurrentSpeed;
                agent.acceleration = settings.acceleration;
                agent.angularSpeed = settings.turnSpeed;
                agent.stoppingDistance = settings.stoppingDistance;
            }
        }

        private bool IsPointerOverUi(Vector2 screenPosition)
        {
            var pointerBlock = ClientPresentationRegistry.PointerBlock;
            if (pointerBlock != null && pointerBlock.BlocksWorldPointer(screenPosition))
                return true;
            EventSystem currentEventSystem = EventSystem.current;
            if (currentEventSystem == null)
                return false;

            if (pointerEventSystem != currentEventSystem)
            {
                pointerEventSystem = currentEventSystem;
                pointerEventData = new PointerEventData(currentEventSystem);
            }

            // A fresh raycast avoids depending on EventSystem.Update ordering for this click.
            pointerEventData.Reset();
            pointerEventData.position = screenPosition;
            uiHits.Clear();
            currentEventSystem.RaycastAll(pointerEventData, uiHits);
            foreach (RaycastResult result in uiHits)
            {
                if (!(result.module is PhysicsRaycaster) && !(result.module is Physics2DRaycaster))
                    return true;
            }

            return false;
        }

        private void BeginOrbit(Vector2 screenPosition)
        {
            isOrbiting = true;
            if (!lockCursorWhileOrbiting)
                return;

            previousCursorLock = Cursor.lockState;
            previousCursorVisibility = Cursor.visible;
            previousCursorPosition = screenPosition;
            ownsCursor = true;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private void ApplyOrbitDelta()
        {
            if (orbitCamera == null) return;
            Vector2 delta = orbitDeltaAction.ReadValue<Vector2>() * orbitSensitivity;
            orbitCamera.HorizontalAxis.Value = orbitCamera.HorizontalAxis.ClampValue(orbitCamera.HorizontalAxis.Value + delta.x);
            float verticalDelta = invertVerticalOrbit ? delta.y : -delta.y;
            orbitCamera.VerticalAxis.Value = orbitCamera.VerticalAxis.ClampValue(orbitCamera.VerticalAxis.Value + verticalDelta);
        }

        private void EndOrbit()
        {
            isOrbiting = false;
            if (!ownsCursor)
                return;

            ownsCursor = false;
            Cursor.lockState = previousCursorLock;
            Cursor.visible = previousCursorVisibility;
            if (Application.isFocused && previousCursorLock != CursorLockMode.Locked && Mouse.current != null)
                Mouse.current.WarpCursorPosition(previousCursorPosition);
        }

        public void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus)
                EndOrbit();
        }

        public override void OnDestroy()
        {
            EndOrbit();
            base.OnDestroy();
        }

        private void OnEnable()
        {
            // Re-enabled offline avatar (e.g. after leaving a network session): reclaim the camera.
            if (didStart && !IsSpawned)
                SetupLocalCamera();
        }

        private void OnDisable()
        {
            EndOrbit();
            jumpMotor?.CancelJump();
            if (agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh)
                agent.ResetPath();
        }
    }
}

