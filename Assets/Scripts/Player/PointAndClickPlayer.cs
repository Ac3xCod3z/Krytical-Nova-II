using System;
using System.Collections.Generic;
using KryticalNova.Progression;
using KryticalNova.UI;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace KryticalNova.Player
{
    /// <summary>Moves to clicked ground, orbits during right-button drags, and zooms with the mouse wheel.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NavMeshAgent), typeof(PlayerInput))]
    public sealed class PointAndClickPlayer : MonoBehaviour
    {
        private const string ActionMapName = "PointAndClick";
        private const string PointerActionName = "PointerPosition";
        private const string MoveActionName = "MoveToPoint";
        private const string OrbitHeldActionName = "OrbitHeld";
        private const string OrbitDeltaActionName = "OrbitDelta";
        private const string CancelOrbitActionName = "CancelOrbit";
        private const string ZoomActionName = "Zoom";
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
        [SerializeField] private ProgressionHUD progressionUI;

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
            requestedPath = new NavMeshPath();
            agent.updateRotation = false;
        }

        private void Start()
        {
            if (gameplayCamera == null)
                gameplayCamera = Camera.main;

            if (gameplayCamera == null || orbitCamera == null || playerInput.actions == null)
            {
                Debug.LogError("PointAndClickPlayer requires a gameplay camera, Cinemachine orbital follow, and PlayerInput actions.", this);
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

            if (!PlaceAgentOnNavigation())
                enabled = false;
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
            ApplyMovementSettings();
            TurnTowardsTravel();

            if (gameplayActions == null || !gameplayActions.enabled || !playerInput.inputIsActive
                || !Application.isFocused || Time.timeScale <= PausedTimeScale)
            {
                EndOrbit();
                return;
            }

            if (cancelOrbitAction.WasPressedThisFrame())
            {
                EndOrbit();
                return;
            }

            Vector2 screenPosition = pointerAction.ReadValue<Vector2>();
            ApplyZoom(screenPosition);

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

            if (!orbitHeldAction.IsPressed() && moveAction.WasPressedThisFrame()
                && !IsPointerOverUi(screenPosition))
                TryMoveToPointer(screenPosition);
        }

        private void ApplyZoom(Vector2 screenPosition)
        {
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
            if (!agent.isActiveAndEnabled || !agent.isOnNavMesh || !gameplayCamera.pixelRect.Contains(screenPosition))
                return;

            Ray ray = gameplayCamera.ScreenPointToRay(screenPosition);
            if (!Physics.Raycast(ray, out RaycastHit hit, maximumRayDistance, raycastLayers, QueryTriggerInteraction.Ignore))
                return;
            if ((groundLayers.value & (1 << hit.collider.gameObject.layer)) == 0)
            { CommandMessage?.Invoke("Choose a walkable ground or platform surface."); return; }

            if (movementSkills != null && movementSkills.IsJumpSelected)
            {
                if (jumpMotor == null) { CommandMessage?.Invoke("Jump controller is missing."); return; }
                if (!jumpMotor.TryJump(hit.point, raycastLayers, modelForwardOffset, out string reason)) CommandMessage?.Invoke(reason);
                return;
            }

            var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };
            if (!NavMesh.SamplePosition(hit.point, out NavMeshHit destination, destinationSampleRadius, filter)
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
            agent.speed = movementSkills.CurrentSpeed;
            agent.acceleration = settings.acceleration;
            agent.angularSpeed = settings.turnSpeed;
            agent.stoppingDistance = settings.stoppingDistance;
        }

        private bool IsPointerOverUi(Vector2 screenPosition)
        {
            if (progressionUI != null && progressionUI.BlocksWorldPointer(screenPosition))
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

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus)
                EndOrbit();
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
