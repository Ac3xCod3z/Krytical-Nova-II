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
        private const string FlipCameraActionName = "FlipCamera";
        private const string GroundLayerName = "Ground";
        private const float DefaultRayDistance = 1000f;
        private const float DefaultSampleRadius = 1f;
        private const float SpawnSampleRadius = 3f;
        private const float DefaultOrbitSensitivity = 0.15f;
        private const float DefaultMinimumZoomDistance = 3f;
        private const float DefaultMaximumZoomDistance = 24f;
        private const float DefaultZoomSensitivity = 1f;
        private const float FixedRadialMultiplier = 1f;
        private const float MinimumFacingSpeedSquared = 0.01f;
        private const float MinimumPositiveValue = 0.01f;
        private const float PausedTimeScale = 0f;
        private const int InitialUiHitCapacity = 8;
        private const float PendingInteractTimeout = 20f;
        private const float TravelSampleRadius = 6f;     // a map point is far coarser than a click on the ground
        private const float TravelArrivalDistance = 1.5f;
        private const float TravelStallSeconds = 2.5f;   // standing still this long mid-journey ends it (a cast, a blocked way)
        private const float TravelStallSpeed = 0.2f;
        private const float FriendlyApproachDistance = 1.5f;

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

        [Header("Camera Pitch")]
        [Tooltip("Lowest orbit pitch (negative looks up at the sky). The ground stop below is the real limit.")]
        [SerializeField, Range(-89f, 0f)] private float minimumPitch = -80f;
        [SerializeField, Range(10f, 89f)] private float maximumPitch = 75f;
        [Tooltip("The camera never orbits closer than this to the ground beneath it.")]
        [SerializeField, Min(0f)] private float groundClearance = 0.6f;
        [Tooltip("Once the camera scrapes the ground, dragging further down raises its aim point by up to this many metres, " +
                 "tilting the view up to the sky (the camera itself never goes lower).")]
        [SerializeField, Min(0f)] private float maximumSkyLookHeight = 40f;
        [Tooltip("Metres of aim lift per degree of downward drag while the camera is held at the ground.")]
        [SerializeField, Min(0f)] private float skyLookPerDegree = 0.8f;

        [Header("Occlusion")]
        [Tooltip("Props between the camera and the player dither-fade with this shader (KryticalNova/Occluder Dither).")]
        [SerializeField] private Shader occluderDitherShader;

        private readonly List<RaycastResult> uiHits = new List<RaycastResult>(InitialUiHitCapacity);
        private NavMeshAgent agent;
        private PlayerInput playerInput;
        private PlayerMovementSkills movementSkills;
        private PlayerResources resources;
        private const float SelectionRefreshInterval = 0.25f;
        private WorldTargetKind selectedKind;
        private float nextSelectionRefresh;
        private PlayerJumpMotor jumpMotor;
        private PlayerVaultMotor vaultMotor;
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
        private InputAction flipCameraAction;
        private Transform cameraTrackingTarget;
        private readonly RaycastHit[] groundHit = new RaycastHit[1];
        private readonly RaycastHit[] pointerHits = new RaycastHit[16];
        private PlayerSkillHotbar hotbar;
        private KryticalNova.Rendering.OccluderFader occluderFader;
        private EventSystem pointerEventSystem;
        private PointerEventData pointerEventData;
        private bool isOrbiting;
        private CinemachineRotationComposer orbitComposer;
        private bool pitchAtGroundStop;
        private float skyLook;          // requested aim lift (m)
        private float skyLookSmoothed;  // applied aim lift (m)
        private bool ownsCursor;
        private CursorLockMode previousCursorLock;
        private bool previousCursorVisibility;
        private Vector2 previousCursorPosition;
        private IInteractable pendingInteract;
        private float pendingInteractExpiry;
        private bool isTravelling;
        private Vector3 travelDestination;
        private string travelName;
        private bool travelStopsShort;   // no walkable way reaches the place itself: the journey ends where the way does
        private Vector3 travelLastPosition;
        private float travelStalledFor;
        // First click on a unit selects it; a second click on the selected unit attacks / talks / walks to it.
        private Transform selectedTarget;

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
            vaultMotor = GetComponent<PlayerVaultMotor>();
            if (vaultMotor != null) vaultMotor.Landed += ResumeMoveAfterVault;
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

            // The server picks where every player appears, before its first Start attaches it to the NavMesh.
            if (IsServer) KryticalNova.Networking.PlayerSpawnService.PlaceSpawnedPlayer(transform);

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
                cameraTrackingTarget = targetTransform;
                var cinemachineCamera = orbitCamera.GetComponent<CinemachineCamera>();
                if (cinemachineCamera != null)
                {
                    cinemachineCamera.Target.TrackingTarget = targetTransform;
                }
            }

            // Fade props that block the view of this (local) player.
            var cam = gameplayCamera != null ? gameplayCamera : Camera.main;
            if (cam != null)
            {
                if (!cam.TryGetComponent<KryticalNova.Rendering.OccluderFader>(out var fader))
                    fader = cam.gameObject.AddComponent<KryticalNova.Rendering.OccluderFader>();
                fader.Configure(transform, occluderDitherShader);
                occluderFader = fader;
            }

            // Soft hero light so the local character stays readable at night.
            PlayerNightLight.AttachTo(transform);
        }

        private void Start()
        {
            if (gameplayCamera == null)
                gameplayCamera = Camera.main;

            if (IsSpawned && !IsOwner)
            {
                // Another player's character needs no input or camera here, but the server moves it: without an
                // agent on the NavMesh every move, attack and jump that player asks for would be dropped.
                if (IsServer && !PlaceAgentOnNavigation())
                    enabled = false;
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
            flipCameraAction = gameplayActions.FindAction(FlipCameraActionName, false);

            if (orbitCamera != null)
            {
                // No recentering or input acceleration: releasing RMB keeps the chosen orbit angles.
                orbitCamera.HorizontalAxis.Recentering.Enabled = false;
                orbitCamera.VerticalAxis.Recentering.Enabled = false;
                orbitCamera.RadialAxis.Recentering.Enabled = false;
                orbitCamera.VerticalAxis.Range = new Vector2(minimumPitch, maximumPitch);

                // Control distance through Radius, with a fixed multiplier so limits use world units.
                ValidateZoomSettings();
                orbitCamera.RadialAxis.Range = Vector2.one * FixedRadialMultiplier;
                orbitCamera.RadialAxis.Value = FixedRadialMultiplier;
                orbitCamera.RadialAxis.Center = FixedRadialMultiplier;
                orbitCamera.RadialAxis.Wrap = false;
                orbitCamera.Radius = Mathf.Clamp(orbitCamera.Radius, minimumZoomDistance, maximumZoomDistance);
                orbitComposer = orbitCamera.GetComponent<CinemachineRotationComposer>();
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
                UpdateArrival();
                TurnTowardsTravel();
            }

            if (IsSpawned && !IsOwner)
                return;

            UpdatePendingInteract();
            UpdateTravel();

            if (gameplayActions == null || !gameplayActions.enabled || !playerInput.inputIsActive
                || !Application.isFocused || Time.timeScale <= PausedTimeScale)
            {
                EndOrbit();
                return;
            }

            bool inputSuppressed = hotbar != null && hotbar.InputSuppressed;
            RefreshSelectionKind();

            // Escape (CancelOrbit action) is the single cancel source: ends orbit and stops any melee attack.
            if (cancelOrbitAction.WasPressedThisFrame())
            {
                EndOrbit();
                if (!inputSuppressed)
                {
                    CancelAttack();
                    ClearSelection();
                    StopTravel();
                }
                return;
            }

            Vector2 screenPosition = pointerAction.ReadValue<Vector2>();
            if (orbitCamera != null)
            {
                ApplyZoom(screenPosition);
                if (flipCameraAction != null && flipCameraAction.WasPressedThisFrame() && !IsPointerOverUi(screenPosition))
                    FlipCamera();
                ClampPitchAboveGround();
                ApplySkyLook();
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

            if (!inputSuppressed && (resources == null || resources.IsAlive) && toggleWeaponAction.WasPressedThisFrame())
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

        // Units win over props: a vendor inside a stall sits within the canopy's convex hull, so the
        // nearest hit is the canopy. Walk the hits front to back and take the first unit, stopping at
        // walkable ground so nothing can be picked through the floor. Otherwise use the nearest hit.
        // Props faded to keep the player in view are skipped: what can be seen through can be clicked through.
        // Players sit on a layer the click mask leaves out, so a click never lands on yourself. The ray takes
        // that layer in as well and keeps only the other units on it, which is what makes other players clickable.
        private bool TryPickPointerHit(Ray ray, out RaycastHit hit)
        {
            int clickLayers = raycastLayers.value | (1 << gameObject.layer);
            int count = Physics.RaycastNonAlloc(ray, pointerHits, maximumRayDistance, clickLayers, QueryTriggerInteraction.Ignore);
            hit = default;
            if (count == 0) return false;

            System.Array.Sort(pointerHits, 0, count, HitDistanceComparer.Instance);
            bool found = false;

            for (int i = 0; i < count; i++)
            {
                Collider c = pointerHits[i].collider;
                if (occluderFader != null && occluderFader.IsFaded(c)) continue;
                var damageable = c.GetComponentInParent<IDamageable>();
                bool isOtherUnit = damageable != null && damageable.Transform != transform;
                if (!isOtherUnit && (raycastLayers.value & (1 << c.gameObject.layer)) == 0) continue;
                if (!found)
                {
                    hit = pointerHits[i];
                    found = true;
                }
                if (isOtherUnit || c.GetComponentInParent<IInteractable>() != null)
                {
                    hit = pointerHits[i];
                    return true;
                }
                if ((groundLayers.value & (1 << c.gameObject.layer)) != 0)
                    break;
            }
            return found;
        }

        private sealed class HitDistanceComparer : System.Collections.Generic.IComparer<RaycastHit>
        {
            public static readonly HitDistanceComparer Instance = new HitDistanceComparer();
            public int Compare(RaycastHit a, RaycastHit b) => a.distance.CompareTo(b.distance);
        }

        private void TryMoveToPointer(Vector2 screenPosition)
        {
            if (resources != null && !resources.IsAlive) { CommandMessage?.Invoke("No HP remaining. Restore HP before moving."); return; }
            if (jumpMotor != null && jumpMotor.IsBusy) { CommandMessage?.Invoke("Finish this jump before choosing another destination."); return; }
            if (vaultMotor != null && vaultMotor.IsBusy) return; // back on the ground in a moment
            if (gameplayCamera == null || !gameplayCamera.pixelRect.Contains(screenPosition))
                return;

            Ray ray = gameplayCamera.ScreenPointToRay(screenPosition);
            if (!TryPickPointerHit(ray, out RaycastHit hit))
                return;

            // Any new click replaces a walk-to-talk in progress, and a vault that was planned for the old route.
            pendingInteract = null;
            if (vaultMotor != null) vaultMotor.Disarm();

            var targetEntity = hit.collider.GetComponentInParent<IDamageable>();
            if (targetEntity != null && targetEntity.Transform != transform)
            {
                bool hostile = KryticalNova.Combat.CombatHostility.IsHostile(transform, targetEntity);

                // First click only selects.
                if (!IsSelected(targetEntity.Transform))
                {
                    Select(targetEntity.Transform, hostile ? WorldTargetKind.Hostile : WorldTargetKind.Friendly);
                    if (!targetEntity.IsAlive) CommandMessage?.Invoke($"{targetEntity.EntityName} is defeated.");
                    return;
                }

                // Second click on a friendly unit (another player): walk over to them.
                if (!hostile)
                {
                    EndTravel(false);
                    CancelAttack();
                    WalkToUnit(targetEntity.Transform);
                    return;
                }

                if (!targetEntity.IsAlive)
                {
                    CommandMessage?.Invoke($"{targetEntity.EntityName} is defeated.");
                    return;
                }

                EndTravel(false);
                ClientPresentationRegistry.Markers?.HideMoveMarker();

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

            var interactable = hit.collider.GetComponentInParent<IInteractable>();
            if (interactable != null)
            {
                Transform unit = interactable is Component interactableComponent ? interactableComponent.transform : null;
                if (unit != null && !IsSelected(unit))
                {
                    Select(unit, WorldTargetKind.Friendly);
                    return;
                }

                EndTravel(false);
                CancelAttack();
                ClientPresentationRegistry.Markers?.HideMoveMarker();
                BeginInteract(interactable);
                return;
            }

            // Clicking ground or platform stops an active attack and drops the selection (its ring fades out).
            CancelAttack();
            if (selectedTarget != null) ClearSelection();

            if ((groundLayers.value & (1 << hit.collider.gameObject.layer)) == 0)
            { CommandMessage?.Invoke("Choose a walkable ground or platform surface."); return; }

            // A new destination on the ground replaces one chosen on the map.
            EndTravel(false);

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
                else ClientPresentationRegistry.Markers?.ShowMoveMarker(transform, hit.point, hit.normal);
                return;
            }

            if (IsSpawned && !IsServer)
            {
                RequestMoveDestinationServerRpc(hit.point);
                // The server validates the path; the marker is the client's best guess at the destination.
                ClientPresentationRegistry.Markers?.ShowMoveMarker(transform, hit.point, hit.normal);
                return;
            }

            if (ExecuteLocalMove(hit.point, out Vector3 acceptedDestination))
                ClientPresentationRegistry.Markers?.ShowMoveMarker(transform, acceptedDestination, hit.normal);
        }

        /// <summary>The unit this player has clicked (skills aim at it), or null.</summary>
        public Transform SelectedTarget => selectedTarget;

        // ------------------------------------------------------------------ travel (ordered from the map)

        /// <summary>The player is being led to a place chosen on the map. Any new order on the ground, or Escape, ends it.</summary>
        public bool IsTravelling => isTravelling;
        public Vector3 TravelDestination => travelDestination;
        /// <summary>Raised when a journey ends; true = the player arrived.</summary>
        public event Action<bool> TravelEnded;

        /// <summary>
        /// Leads this player along the NavMesh to <paramref name="worldPoint"/> (owner side). It is an ordinary move
        /// order with a destination found on the map instead of under the pointer, so it costs and behaves the same;
        /// it always walks or runs, whatever movement skill is selected.
        /// </summary>
        public bool TravelTo(Vector3 worldPoint, string destinationName)
        {
            if (IsSpawned && !IsOwner) return false;
            if (resources != null && !resources.IsAlive) { CommandMessage?.Invoke("No HP remaining. Restore HP before moving."); return false; }
            if (jumpMotor != null && jumpMotor.IsBusy) { CommandMessage?.Invoke("Finish this jump before choosing another destination."); return false; }
            if (vaultMotor != null && vaultMotor.IsBusy) return false;
            if (agent == null) return false;

            var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };
            if (!NavMesh.SamplePosition(worldPoint, out NavMeshHit destination, TravelSampleRadius, filter)
                || !TryResolveTravelGoal(destination.position, filter, out Vector3 goal, out bool stopsShort))
            { CommandMessage?.Invoke($"There is no way to walk to {destinationName}."); return false; }

            // The same clean slate a click on the ground gives.
            EndTravel(false);
            pendingInteract = null;
            if (vaultMotor != null) vaultMotor.Disarm();
            CancelAttack();
            if (selectedTarget != null) ClearSelection();
            if (buffManager == null) buffManager = GetComponent<CharacterBuffManager>();
            if (buffManager != null && buffManager.IsChanneling) buffManager.InterruptChanneled();

            if (IsSpawned && !IsServer)
                RequestMoveDestinationServerRpc(goal);
            else if (!ExecuteLocalMove(goal, out _))
                return false;

            isTravelling = true;
            travelDestination = goal;
            travelName = destinationName;
            travelStopsShort = stopsShort;
            travelLastPosition = transform.position;
            travelStalledFor = 0f;
            ClientPresentationRegistry.Markers?.ShowMoveMarker(transform, goal, Vector3.up, untilArrival: true);
            CommandMessage?.Invoke(stopsShort
                ? $"No walkable way reaches {destinationName}: travelling as close as the way goes. Click the ground or press Escape to stop."
                : $"Travelling to {destinationName}. Click the ground or press Escape to stop.");
            return true;
        }

        /// <summary>
        /// Where a journey to <paramref name="destination"/> can actually end. Usually the destination itself; when
        /// the walkable ground stops short of it (a marker dropped inside a building, a place across water with no
        /// bridge) the journey ends where the way does. False when there is no way toward it at all.
        /// </summary>
        private bool TryResolveTravelGoal(Vector3 destination, NavMeshQueryFilter filter, out Vector3 goal, out bool stopsShort)
        {
            goal = destination;
            stopsShort = false;
            Vector3 feet = transform.position - Vector3.up * agent.baseOffset;
            if (!NavMesh.SamplePosition(feet, out NavMeshHit from, TravelSampleRadius, filter)) return true; // let the move itself decide
            if (!NavMesh.CalculatePath(from.position, destination, filter, requestedPath)) return false;
            if (requestedPath.status == NavMeshPathStatus.PathComplete) return true;

            Vector3[] corners = requestedPath.corners;
            if (requestedPath.status != NavMeshPathStatus.PathPartial || corners.Length < 2) return false;
            goal = corners[corners.Length - 1];
            stopsShort = true;
            return true;
        }

        /// <summary>Stops a journey where the player stands (Escape).</summary>
        private void StopTravel()
        {
            if (!isTravelling) return;
            if (IsSpawned && !IsServer) RequestStopMoveServerRpc();
            else StopMoving();
            ClientPresentationRegistry.Markers?.HideMoveMarker();
            CommandMessage?.Invoke($"Travel to {travelName} stopped.");
            EndTravel(false);
        }

        [ServerRpc]
        private void RequestStopMoveServerRpc() => StopMoving();

        private void StopMoving()
        {
            if (agent == null || !agent.isActiveAndEnabled || !agent.isOnNavMesh) return;
            if (vaultMotor != null) vaultMotor.Disarm();
            agent.ResetPath();
            agent.velocity = Vector3.zero;
        }

        private void EndTravel(bool arrived)
        {
            if (!isTravelling) return;
            isTravelling = false;
            if (arrived) CommandMessage?.Invoke(travelStopsShort ? $"This is as close to {travelName} as the way goes." : $"Arrived at {travelName}.");
            TravelEnded?.Invoke(arrived);
        }

        /// <summary>Owner side, every frame: has the journey been completed, or come to a halt for some other reason?</summary>
        private void UpdateTravel()
        {
            if (!isTravelling) return;
            if (resources != null && !resources.IsAlive) { EndTravel(false); return; }

            Vector3 offset = travelDestination + Vector3.up * (agent != null ? agent.baseOffset : 0f) - transform.position;
            offset.y = 0f;
            if (offset.sqrMagnitude <= TravelArrivalDistance * TravelArrivalDistance) { EndTravel(true); return; }

            // Something else stopped the walk (a skill cast halts the agent, the way became blocked): the journey is over.
            float delta = Time.deltaTime;
            if (delta <= 0f) return;
            Vector3 moved = transform.position - travelLastPosition;
            moved.y = 0f;
            travelLastPosition = transform.position;
            bool airborne = (jumpMotor != null && jumpMotor.IsBusy) || (vaultMotor != null && vaultMotor.IsBusy);
            travelStalledFor = moved.magnitude / delta > TravelStallSpeed || airborne ? 0f : travelStalledFor + delta;
            if (travelStalledFor >= TravelStallSeconds)
            {
                ClientPresentationRegistry.Markers?.HideMoveMarker();
                EndTravel(false);
            }
        }

        private bool IsSelected(Transform unit) => unit != null && selectedTarget == unit;

        private void Select(Transform unit, WorldTargetKind kind)
        {
            selectedTarget = unit;
            selectedKind = kind;
            ClientPresentationRegistry.Markers?.SetTarget(unit, kind);
        }

        private void ClearSelection()
        {
            selectedTarget = null;
            ClientPresentationRegistry.Markers?.ClearTarget();
        }

        /// <summary>
        /// A selected player can turn from friend to foe and back while selected (a duel starts or ends, the GM
        /// switches faction PvP): the ring follows.
        /// </summary>
        private void RefreshSelectionKind()
        {
            if (selectedTarget == null || Time.unscaledTime < nextSelectionRefresh) return;
            nextSelectionRefresh = Time.unscaledTime + SelectionRefreshInterval;
            var entity = selectedTarget.GetComponentInParent<IDamageable>();
            if (entity == null) return;
            WorldTargetKind kind = KryticalNova.Combat.CombatHostility.IsHostile(transform, entity) ? WorldTargetKind.Hostile : WorldTargetKind.Friendly;
            if (kind == selectedKind) return;
            selectedKind = kind;
            ClientPresentationRegistry.Markers?.SetTarget(selectedTarget, kind);
        }

        /// <summary>Walks to just short of another unit (friendly player) without interacting.</summary>
        private void WalkToUnit(Transform unit)
        {
            Vector3 fromUnit = transform.position - unit.position;
            fromUnit.y = 0f;
            Vector3 destination = fromUnit.sqrMagnitude > FriendlyApproachDistance * FriendlyApproachDistance
                ? unit.position + fromUnit.normalized * FriendlyApproachDistance
                : transform.position;
            if ((destination - transform.position).sqrMagnitude < 0.01f) return;

            ClientPresentationRegistry.Markers?.HideMoveMarker();
            if (IsSpawned && !IsServer)
            {
                RequestMoveDestinationServerRpc(destination);
                return;
            }

            ExecuteLocalMove(destination, out _);
        }

        /// <summary>Talks right away when in range; otherwise walks over and talks on arrival.</summary>
        private void BeginInteract(IInteractable interactable)
        {
            if (IsWithinInteractRange(interactable))
            {
                interactable.Interact(gameObject);
                return;
            }

            pendingInteract = interactable;
            pendingInteractExpiry = Time.time + PendingInteractTimeout;
            Vector3 destination = interactable.InteractPoint;
            if (IsSpawned && !IsServer)
            {
                RequestMoveDestinationServerRpc(destination);
                return;
            }

            ExecuteLocalMove(destination, out _);
        }

        private void UpdatePendingInteract()
        {
            if (pendingInteract == null) return;
            if (Time.time > pendingInteractExpiry || (resources != null && !resources.IsAlive))
            {
                pendingInteract = null;
                return;
            }

            if (!IsWithinInteractRange(pendingInteract)) return;
            IInteractable target = pendingInteract;
            pendingInteract = null;
            target.Interact(gameObject);
        }

        private bool IsWithinInteractRange(IInteractable interactable)
        {
            Vector3 offset = interactable.Position - transform.position;
            offset.y = 0f;
            return offset.sqrMagnitude <= interactable.InteractRange * interactable.InteractRange;
        }

        /// <summary>Returns true when the agent accepted a new path; <paramref name="acceptedDestination"/> is the NavMesh point.</summary>
        private bool ExecuteLocalMove(Vector3 destinationPoint, out Vector3 acceptedDestination)
        {
            acceptedDestination = destinationPoint;
            if (!agent.isActiveAndEnabled || !agent.isOnNavMesh) return false;

            var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };
            if (!NavMesh.SamplePosition(destinationPoint, out NavMeshHit destination, destinationSampleRadius, filter)
                || !TryPlanRoute(destination.position))
            { CommandMessage?.Invoke("No complete walking path to that point."); return false; }
            if (Vector3.Distance(agent.nextPosition, destination.position + Vector3.up * agent.baseOffset) <= agent.stoppingDistance)
                return false;

            float paidCost = 0f;
            if (movementSkills != null && movementSkills.IsRunning
                && !movementSkills.TryPayForCommand(MovementSkillId.Run, out paidCost, out string paymentReason))
            { CommandMessage?.Invoke(paymentReason); return false; }
            agent.isStopped = false;
            if (!agent.SetPath(requestedPath))
            {
                resources?.RestoreChi(paidCost);
                CommandMessage?.Invoke("The path changed. Chi was not consumed.");
                return false;
            }
            SnapVelocityToPath();
            acceptedDestination = destination.position;
            return true;
        }

        [ServerRpc]
        private void RequestMoveDestinationServerRpc(Vector3 destinationPoint)
        {
            if (resources != null && !resources.IsAlive) return;
            if (jumpMotor != null && jumpMotor.IsBusy) return;
            if (vaultMotor != null && vaultMotor.IsBusy) return;
            if (agent == null || !agent.isActiveAndEnabled || !agent.isOnNavMesh) return;
            // The owner ended its own copy of a channel when it clicked; the authoritative one ends here.
            if (buffManager != null && buffManager.IsChanneling) buffManager.InterruptChanneled();

            var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };
            if (!NavMesh.SamplePosition(destinationPoint, out NavMeshHit destination, destinationSampleRadius, filter))
                return;

            if (!TryPlanRoute(destination.position))
                return;

            if (Vector3.Distance(agent.nextPosition, destination.position + Vector3.up * agent.baseOffset) <= agent.stoppingDistance)
                return;

            // The server knows which movement skill is selected; the request only names the place.
            float paidCost = 0f;
            if (movementSkills != null && movementSkills.IsRunning
                && !movementSkills.TryPayForCommand(MovementSkillId.Run, out paidCost, out _))
            {
                return;
            }

            agent.isStopped = false;
            if (!agent.SetPath(requestedPath))
            {
                resources?.RestoreChi(paidCost);
                return;
            }
            SnapVelocityToPath();
        }

        /// <summary>
        /// Fills <see cref="requestedPath"/> with the way to <paramref name="destination"/>. When a low obstacle on the
        /// straight line can be vaulted and that is shorter, the path only leads to the vault's take-off point: the
        /// vault motor takes over there and <see cref="ResumeMoveAfterVault"/> carries on from the landing.
        /// </summary>
        private bool TryPlanRoute(Vector3 destination)
        {
            if (vaultMotor != null) vaultMotor.Disarm();
            bool walkable = agent.CalculatePath(destination, requestedPath) && requestedPath.status == NavMeshPathStatus.PathComplete;
            // With no way round, a vault that gets there is the route.
            if (vaultMotor == null) return walkable;
            if (!vaultMotor.TryArm(destination, walkable ? requestedPath : null, raycastLayers, modelForwardOffset, out Vector3 takeoff))
            {
                // Nothing to go over from here; there may be once the walk has cleared whatever is in the line now.
                if (walkable) vaultMotor.Watch(destination, raycastLayers, modelForwardOffset);
                return walkable;
            }
            if (agent.CalculatePath(takeoff, requestedPath) && requestedPath.status == NavMeshPathStatus.PathComplete) return true;
            vaultMotor.Disarm();
            return walkable && agent.CalculatePath(destination, requestedPath) && requestedPath.status == NavMeshPathStatus.PathComplete;
        }

        /// <summary>The rest of a move that went over an obstacle. The run was paid for when the move was ordered.</summary>
        private void ResumeMoveAfterVault(Vector3 destination)
        {
            if (!agent.isActiveAndEnabled || !agent.isOnNavMesh) return;
            if (resources != null && !resources.IsAlive) return;
            var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };
            if (!NavMesh.SamplePosition(destination, out NavMeshHit target, destinationSampleRadius, filter) || !TryPlanRoute(target.position)) return;
            if (Vector3.Distance(agent.nextPosition, target.position + Vector3.up * agent.baseOffset) <= agent.stoppingDistance) return;
            agent.isStopped = false;
            if (agent.SetPath(requestedPath)) SnapVelocityToPath();
        }

        /// <summary>
        /// Points the agent's momentum straight down the new path at full speed, so re-clicking mid-run turns
        /// the character immediately instead of skidding through a wide arc.
        /// </summary>
        private void SnapVelocityToPath()
        {
            if (!agent.isActiveAndEnabled || !agent.isOnNavMesh) return;
            Vector3[] corners = agent.path.corners;
            if (corners == null || corners.Length < 2) return;
            Vector3 direction = corners[1] - agent.nextPosition;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f) return;
            float speed = movementSkills != null ? movementSkills.CurrentSpeed : agent.speed;
            agent.velocity = direction.normalized * speed;
        }

        /// <summary>Stops dead on the destination instead of coasting (autoBraking is off; we ease out ourselves).</summary>
        private void UpdateArrival()
        {
            if (agent == null || !agent.isActiveAndEnabled || !agent.isOnNavMesh) return;
            if (!agent.hasPath || agent.pathPending) return;
            if (RemainingPathDistance() > agent.stoppingDistance) return;
            agent.ResetPath();
            agent.velocity = Vector3.zero;
        }

        /// <summary>
        /// NavMeshAgent.remainingDistance reads 0 until the agent's first update after SetPath, which would look like
        /// instant arrival; never report less than the straight-line distance to the destination.
        /// </summary>
        private float RemainingPathDistance()
        {
            Vector3 toDestination = agent.destination - agent.nextPosition;
            toDestination.y = 0f;
            return Mathf.Max(agent.remainingDistance, toDestination.magnitude);
        }

        /// <summary>
        /// Calls the basic attack off, here and where it is decided. On a client the server's copy of this player
        /// is the one walking in and striking: left running, it would walk the player straight back to the target.
        /// </summary>
        private void CancelAttack()
        {
            if (meleeCombat == null) return;
            bool wasAttacking = meleeCombat.IsAttacking;
            meleeCombat.CancelAttack();
            if (wasAttacking && IsSpawned && !IsServer) RequestCancelAttackServerRpc();
        }

        [ServerRpc]
        private void RequestCancelAttackServerRpc()
        {
            if (meleeCombat == null || !meleeCombat.IsAttacking) return;
            meleeCombat.CancelAttack();
            StopMoving(); // it may have been walking in; a new order, if any, arrives right behind this
        }

        [ServerRpc]
        private void RequestAttackEntityServerRpc(ulong targetNetworkObjectId)
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(targetNetworkObjectId, out var targetNetObj))
                return;

            var targetEntity = targetNetObj.GetComponent<IDamageable>();
            if (targetEntity == null || !targetEntity.IsAlive) return;
            if (!KryticalNova.Combat.CombatHostility.IsHostile(transform, targetEntity)) return;

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
                agent.velocity = Vector3.zero;
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

            // Face where we are actually travelling; desiredVelocity swings ahead of the body on re-paths.
            Vector3 direction = agent.velocity;
            direction.y = 0f;
            if (direction.sqrMagnitude < MinimumFacingSpeedSquared)
            {
                direction = agent.desiredVelocity;
                direction.y = 0f;
            }
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
            if (vaultMotor != null && vaultMotor.IsBusy) return;
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
                float speed = movementSkills.CurrentSpeed;
                agent.autoBraking = false;
                agent.acceleration = Mathf.Max(settings.acceleration, speed * settings.accelerationPerSpeed);
                agent.angularSpeed = settings.turnSpeed;
                agent.stoppingDistance = settings.stoppingDistance;
                // Full speed until the last stretch, then ease off a little before stopping on the point.
                float factor = agent.isOnNavMesh && agent.hasPath && !agent.pathPending
                    ? settings.ArrivalSpeedFactor(RemainingPathDistance(), speed) : 1f;
                agent.speed = speed * factor;
            }
        }

        private bool IsPointerOverUi(Vector2 screenPosition)
        {
            if (ClientPresentationRegistry.BlocksWorldPointer(screenPosition))
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

        /// <summary>Middle mouse: look the opposite way instantly (the rotation composer has no damping).</summary>
        private void FlipCamera()
        {
            orbitCamera.HorizontalAxis.Value = orbitCamera.HorizontalAxis.ClampValue(orbitCamera.HorizontalAxis.Value + 180f);
        }

        /// <summary>
        /// Lets the orbit dip below the target to look up at the sky, but stops where the camera would meet the ground:
        /// with a sphere orbit the camera sits radius·sin(pitch) above the target, so the lowest allowed pitch is
        /// asin((groundY + clearance − targetY) / radius), sampled at the camera's ground position.
        /// </summary>
        private void ClampPitchAboveGround()
        {
            if (cameraTrackingTarget == null || gameplayCamera == null) return;
            float radius = Mathf.Max(orbitCamera.Radius, MinimumPositiveValue);
            Vector3 target = cameraTrackingTarget.position;
            Vector3 flat = gameplayCamera.transform.position - target;
            flat.y = 0f;
            if (flat.sqrMagnitude < 1e-4f) return;
            flat.Normalize();

            float pitch = orbitCamera.VerticalAxis.Value;
            pitchAtGroundStop = pitch <= minimumPitch + 0.5f;
            for (int i = 0; i < 2; i++) // the ground sample moves a little with pitch; two passes settle it
            {
                Vector3 probe = target + flat * (radius * Mathf.Cos(pitch * Mathf.Deg2Rad));
                probe.y = target.y + radius + 50f;
                if (Physics.RaycastNonAlloc(probe, Vector3.down, groundHit, radius + 200f, groundLayers,
                        QueryTriggerInteraction.Ignore) == 0)
                    return; // no ground under the camera (off the map edge): leave it be
                float sin = Mathf.Clamp((groundHit[0].point.y + groundClearance - target.y) / radius, -1f, 1f);
                float lowest = Mathf.Asin(sin) * Mathf.Rad2Deg;
                if (pitch <= lowest + 0.5f) pitchAtGroundStop = true;
                if (pitch >= lowest) break;
                pitch = lowest;
            }
            if (pitch > orbitCamera.VerticalAxis.Value)
                orbitCamera.VerticalAxis.Value = Mathf.Min(pitch, maximumPitch);
        }

        private void ApplyOrbitDelta()
        {
            if (orbitCamera == null) return;
            Vector2 delta = orbitDeltaAction.ReadValue<Vector2>() * orbitSensitivity;
            orbitCamera.HorizontalAxis.Value = orbitCamera.HorizontalAxis.ClampValue(orbitCamera.HorizontalAxis.Value + delta.x);
            float verticalDelta = invertVerticalOrbit ? delta.y : -delta.y;

            // Scraping the ground: keep dragging down to tilt the view up to the sky; dragging back up undoes the tilt
            // before the orbit rises again.
            if (verticalDelta < 0f && pitchAtGroundStop && skyLookPerDegree > 0f)
            {
                skyLook = Mathf.Min(maximumSkyLookHeight, skyLook - verticalDelta * skyLookPerDegree);
                verticalDelta = 0f;
            }
            else if (verticalDelta > 0f && skyLook > 0f && skyLookPerDegree > 0f)
            {
                float used = Mathf.Min(skyLook, verticalDelta * skyLookPerDegree);
                skyLook -= used;
                verticalDelta -= used / skyLookPerDegree;
            }
            orbitCamera.VerticalAxis.Value = orbitCamera.VerticalAxis.ClampValue(orbitCamera.VerticalAxis.Value + verticalDelta);
        }

        /// <summary>Raises the composer's aim point by the sky-look lift (smoothed), so the ground-held camera looks up.</summary>
        private void ApplySkyLook()
        {
            if (orbitComposer == null) return;
            skyLookSmoothed = Mathf.Lerp(skyLookSmoothed, skyLook, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 10f));
            if (skyLookSmoothed < 0.001f && skyLook <= 0f) skyLookSmoothed = 0f;
            orbitComposer.TargetOffset = new Vector3(0f, skyLookSmoothed, 0f);
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

        private static readonly List<PointAndClickPlayer> ActivePlayers = new List<PointAndClickPlayer>(16);
        /// <summary>Enabled player characters (local, remote proxies and the offline avatar), for map markers.</summary>
        public static IReadOnlyList<PointAndClickPlayer> Active => ActivePlayers;

        /// <summary>Yaw (degrees) the character actually faces: the root yaw minus the model's forward offset.</summary>
        public float FacingYaw => transform.eulerAngles.y - modelForwardOffset;

        private void OnEnable()
        {
            ActivePlayers.Add(this);
            // Re-enabled offline avatar (e.g. after leaving a network session): reclaim the camera.
            if (didStart && !IsSpawned)
                SetupLocalCamera();
        }

        private void OnDisable()
        {
            ActivePlayers.Remove(this);
            EndOrbit();
            jumpMotor?.CancelJump();
            if (agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh)
                agent.ResetPath();
        }
    }
}

