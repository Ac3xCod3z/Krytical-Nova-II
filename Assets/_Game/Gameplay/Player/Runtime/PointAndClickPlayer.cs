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
        // Stop beside a thing that is picked up, not on top of it: an arm's length away.
        private const float InstantInteractStandOff = 0.9f;
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
        private PlayerWallRunMotor wallRunMotor;
        private CharacterCombatStats combatStats;
        private CharacterBuffManager buffManager;
        private PlayerAnimancerMotor animancerMotor;
        private PlayerMoveAnticipation moveAnticipation;
        private KryticalNova.Combat.PlayerMeleeCombat meleeCombat;
        private MovementSkillId previousMovementMode;
        public event Action<string> CommandMessage;
        /// <summary>Raised on the owner when the player clicks on the world to move, attack or interact.</summary>
        public event Action ManualCommand;
        /// <summary>The local player Shift+clicked another player: (that player, the pointer's screen position).</summary>
        public event Action<Transform, Vector2> PlayerMenuRequested;
        /// <summary>
        /// Raised where movement is decided (the server, or the offline game) when the player orders a move or a
        /// stop themselves. A move made for them (<see cref="ServerMoveTo"/> called by another system) does not raise it.
        /// </summary>
        public event Action MoveOrdered;
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
        private float screenFraming;        // where across the screen the character is wanted: 0 the middle
        private float screenFramingShown;
        private bool ownsCursor;
        private CursorLockMode previousCursorLock;
        private bool previousCursorVisibility;
        private Vector2 previousCursorPosition;
        private IInteractable pendingInteract;
        private float pendingInteractExpiry;
        private Vector3 facePoint;
        private float faceUntil;
        private bool isTravelling;
        private Vector3 travelDestination;
        private string travelName;
        private bool travelStopsShort;   // no walkable way reaches the place itself: the journey ends where the way does
        private Vector3 travelLastPosition;
        private float travelStalledFor;
        // First click on a unit selects it; a second click on the selected unit attacks / talks / walks to it.
        private Transform selectedTarget;
        // The move ordered last, where movement is decided: where it is to end, and where the path handed to the
        // agent ends (the same place, or the take-off point of a vault on the way).
        private const float SameRouteDistance = 0.3f;
        private const float ShortOfOrderDistance = 0.5f;
        private bool hasOrder, orderReplanned;
        // Jumping a gap on the route (a NavMeshLink between roofs): the move carries on once the jump has landed.
        private bool crossingLink;
        private Vector3 orderedDestination, orderedPathEnd;
        private Vector3 stallFrom;
        private float stallWatched;
        private float agentOffFor;
        private PlayerDeathMotor deathMotor;

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
            // A link (a gap between roofs, RoofLinksBuilder) is crossed by a real jump, never slid along.
            agent.autoTraverseOffMeshLink = false;
            playerInput = GetComponent<PlayerInput>();
            movementSkills = GetComponent<PlayerMovementSkills>();
            resources = GetComponent<PlayerResources>();
            jumpMotor = GetComponent<PlayerJumpMotor>();
            vaultMotor = GetComponent<PlayerVaultMotor>();
            if (vaultMotor != null) vaultMotor.Landed += ResumeMoveAfterVault;
            wallRunMotor = GetComponent<PlayerWallRunMotor>();
            if (wallRunMotor != null) wallRunMotor.Message += message => CommandMessage?.Invoke(message);
            combatStats = GetComponent<CharacterCombatStats>();
            buffManager = GetComponent<CharacterBuffManager>();
            animancerMotor = GetComponent<PlayerAnimancerMotor>();
            moveAnticipation = GetComponent<PlayerMoveAnticipation>();
            meleeCombat = GetComponent<KryticalNova.Combat.PlayerMeleeCombat>();
            hotbar = GetComponent<PlayerSkillHotbar>();
            deathMotor = GetComponent<PlayerDeathMotor>();
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

        /// <summary>
        /// Local networked player, after the session moved to another map: the old map's cameras are gone, so take
        /// the new map's orbit camera and main camera, and stand on the new map's NavMesh.
        /// </summary>
        public void RebindToScene()
        {
            orbitCamera = null;
            gameplayCamera = Camera.main;
            SetupLocalCamera();
            ConfigureOrbitCamera();
            if (agent != null && agent.isActiveAndEnabled && !agent.isOnNavMesh) agent.Warp(transform.position);
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

            ConfigureOrbitCamera();

            if (!IsSpawned || IsServer)
            {
                if (!PlaceAgentOnNavigation())
                    enabled = false;
            }
        }

        /// <summary>The orbit camera's rules for this player: called on start and again on each new map's camera.</summary>
        private void ConfigureOrbitCamera()
        {
            if (orbitCamera == null) return;
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

        private bool PlaceAgentOnNavigation(bool quiet = false)
        {
            if (agent.isActiveAndEnabled && agent.isOnNavMesh)
                return true;

            var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };
            Vector3 feetPosition = transform.position - Vector3.up * agent.baseOffset;
            if (!NavMesh.SamplePosition(feetPosition, out NavMeshHit spawnHit, SpawnSampleRadius, filter))
            {
                if (!quiet) Debug.LogError("No NavMesh below the player. Enable RuntimeNavMeshBuilder or bake the scene's NavMeshSurface.", this);
                return false;
            }

            transform.position = spawnHit.position + Vector3.up * agent.baseOffset;
            agent.enabled = true;
            if (agent.isOnNavMesh)
                return true;

            if (!quiet) Debug.LogError("The player could not attach to the NavMesh. Check the agent type and spawn position.", this);
            agent.enabled = false;
            return false;
        }

        /// <summary>
        /// A jump or a vault that could not put the body back on the NavMesh leaves the agent switched off, and with
        /// it off every order is refused without a word: the character stands where it is for good. Where movement
        /// is decided, an agent that has been off for a while with nothing holding it (no jump, no vault, not dead)
        /// is put back on the nearest walkable ground.
        /// </summary>
        private void RecoverLostAgent()
        {
            bool lost = agent != null && !agent.enabled && didStart
                && (resources == null || resources.IsAlive) && (deathMotor == null || !deathMotor.IsDead)
                && !(jumpMotor != null && jumpMotor.IsBusy) && !(vaultMotor != null && vaultMotor.IsBusy)
                && !(wallRunMotor != null && wallRunMotor.IsBusy);
            if (!lost) { agentOffFor = 0f; return; }
            agentOffFor += Time.deltaTime;
            MovementSettings settings = movementSkills != null ? movementSkills.MovementSettings : null;
            if (agentOffFor < (settings != null ? settings.agentRecoverySeconds : 1.5f)) return;
            agentOffFor = 0f;
            if (PlaceAgentOnNavigation(quiet: true)) hasOrder = false;
        }

        private void Update()
        {
            if (IsSpawned && !IsOwner && !IsServer)
                return;

            if (!IsSpawned || IsServer)
            {
                RecoverLostAgent();
                ApplyMovementSettings();
                CrossLinks();
                UpdateArrival();
                WatchForStall();
                TurnTowardsTravel();
                TurnTowardsFacePoint();
            }
            else LeadOwnStep(); // this player's own game, on a client

            if (IsSpawned && !IsOwner)
                return;

            UpdatePendingInteract();
            UpdateTravel();
            ApplyScreenFraming(); // asked for by the HUD, not by input: it runs whatever the checks below say

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
                if (!inputSuppressed && !WorldClicksParked)
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

            if (!inputSuppressed && !WorldClicksParked && (resources == null || resources.IsAlive) && toggleWeaponAction.WasPressedThisFrame())
            {
                animancerMotor?.RequestToggleWeaponDraw();
            }

            if (!WorldClicksParked && !orbitHeldAction.IsPressed() && moveAction.WasPressedThisFrame()
                && !IsPointerOverUi(screenPosition))
                TryMoveToPointer(screenPosition);
        }

        private void ApplyZoom(Vector2 screenPosition)
        {
            if (orbitCamera == null || gameplayCamera == null) return;

            // How far out, and how fast the wheel gets there, are the player's to set (Settings, Gameplay).
            float furthest = CameraPreferences.FurthestDistance(minimumZoomDistance, maximumZoomDistance);
            orbitCamera.Radius = Mathf.Clamp(orbitCamera.Radius, minimumZoomDistance, furthest);
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
                orbitCamera.Radius - scrollDelta * zoomSensitivity * CameraPreferences.ZoomSpeed,
                minimumZoomDistance,
                furthest);
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
                if (occluderFader != null && occluderFader.IsFaded(pointerHits[i])) continue;
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
            ManualCommand?.Invoke();
            if (resources != null && !resources.IsAlive) { CommandMessage?.Invoke("No HP remaining. Restore HP before moving."); return; }
            if (jumpMotor != null && jumpMotor.IsBusy) { CommandMessage?.Invoke("Finish this jump before choosing another destination."); return; }
            if (vaultMotor != null && vaultMotor.IsBusy) return; // back on the ground in a moment
            // Up a wall or on the way over its edge: on the ground (or in the tree) in a moment.
            if (wallRunMotor != null && wallRunMotor.IsBusy && !wallRunMotor.IsPerched) return;
            if (gameplayCamera == null || !gameplayCamera.pixelRect.Contains(screenPosition))
                return;

            Ray ray = gameplayCamera.ScreenPointToRay(screenPosition);
            if (!TryPickPointerHit(ray, out RaycastHit hit))
                return;

            // Standing in a tree: a click on the ground drops down to it; a click on a unit still selects it.
            if (wallRunMotor != null && wallRunMotor.IsPerched && hit.collider.GetComponentInParent<IDamageable>() == null)
            {
                if ((groundLayers.value & (1 << hit.collider.gameObject.layer)) == 0) { CommandMessage?.Invoke("Click the ground to drop down to it."); return; }
                if (!wallRunMotor.TryDropTo(hit.point, out string dropReason)) { if (!string.IsNullOrEmpty(dropReason)) CommandMessage?.Invoke(dropReason); }
                else ClientPresentationRegistry.Markers?.ShowMoveMarker(transform, hit.point, hit.normal);
                return;
            }

            // A click that gives no new order (it only selects, or it lands on something that cannot be walked on)
            // changes nothing: the move under way keeps its walk-to-talk and its vault. Each order below that does
            // replace the route says so with ReplaceRoute.
            var targetEntity = hit.collider.GetComponentInParent<IDamageable>();
            if (targetEntity != null && targetEntity.Transform != transform)
            {
                bool hostile = KryticalNova.Combat.CombatHostility.IsHostile(transform, targetEntity);

                // Shift+click on another player: select them and open the player menu (whisper, invite, trade...).
                var keyboard = Keyboard.current;
                if (keyboard != null && keyboard.shiftKey.isPressed && targetEntity.Transform.GetComponent<PlayerIdentity>() != null)
                {
                    if (!IsSelected(targetEntity.Transform)) Select(targetEntity.Transform, hostile ? WorldTargetKind.Hostile : WorldTargetKind.Friendly);
                    PlayerMenuRequested?.Invoke(targetEntity.Transform, screenPosition);
                    return;
                }

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
                    ReplaceRoute();
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

                ReplaceRoute();
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
                // An NPC is selected first and talked to on the next click; an item on the ground is simply taken.
                if (unit != null && !IsSelected(unit) && !(interactable is IInstantInteractable))
                {
                    Select(unit, WorldTargetKind.Friendly);
                    return;
                }

                ReplaceRoute();
                EndTravel(false);
                CancelAttack();
                ClientPresentationRegistry.Markers?.HideMoveMarker();
                BeginInteract(interactable);
                return;
            }

            // Wallstride: a wall or a tree clicked with Run or Jump selected is run up, or jumped at and caught.
            if ((groundLayers.value & (1 << hit.collider.gameObject.layer)) == 0 && wallRunMotor != null && movementSkills != null
                && (movementSkills.IsRunning || movementSkills.IsJumpSelected)
                && wallRunMotor.TryClimbClicked(hit.point, hit.normal, hit.collider, raycastLayers.value, modelForwardOffset))
            {
                CancelAttack();
                if (selectedTarget != null) ClearSelection();
                pendingInteract = null;
                EndTravel(false);
                return;
            }

            // Not ground: nothing was ordered, so the attack, the selection and the move under way all stand.
            if ((groundLayers.value & (1 << hit.collider.gameObject.layer)) == 0)
            { CommandMessage?.Invoke("Choose a walkable ground or platform surface."); return; }

            // Clicking ground or platform stops an active attack and drops the selection (its ring fades out).
            CancelAttack();
            if (selectedTarget != null) ClearSelection();
            // It replaces a walk-to-talk in progress. The vault planned for the old route is replaced when the new
            // route is planned, and kept if the new route is refused (ExecuteLocalMove, ServerMoveTo).
            pendingInteract = null;

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
                LeadOwnWalk(hit.point);
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
            if (wallRunMotor != null && wallRunMotor.IsBusy) { CommandMessage?.Invoke("Come down first."); return false; }
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
            {
                RequestMoveDestinationServerRpc(goal);
                LeadOwnWalk(goal);
            }
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
        private void RequestStopMoveServerRpc()
        {
            MoveOrdered?.Invoke();
            StopMoving();
        }

        private void StopMoving()
        {
            if (agent == null || !agent.isActiveAndEnabled || !agent.isOnNavMesh) return;
            if (vaultMotor != null) vaultMotor.Disarm();
            hasOrder = false;
            agent.ResetPath();
            agent.velocity = Vector3.zero;
        }

        /// <summary>A new order takes the route over: the walk-to-talk and the vault planned for the old one go.</summary>
        private void ReplaceRoute()
        {
            pendingInteract = null;
            if (vaultMotor != null) vaultMotor.Disarm();
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
            bool airborne = (jumpMotor != null && jumpMotor.IsBusy) || (vaultMotor != null && vaultMotor.IsBusy)
                || (wallRunMotor != null && wallRunMotor.IsBusy);
            travelStalledFor = moved.magnitude / delta > TravelStallSpeed || airborne ? 0f : travelStalledFor + delta;
            if (travelStalledFor >= TravelStallSeconds)
            {
                ClientPresentationRegistry.Markers?.HideMoveMarker();
                EndTravel(false);
            }
        }

        private bool IsSelected(Transform unit) => unit != null && selectedTarget == unit;

        /// <summary>
        /// Owner side: selects a hostile and attacks it, as a second click on it does. For whatever commands the
        /// player without a pointer (Autopilot); the server checks the request like any other.
        /// </summary>
        public bool CommandAttack(IDamageable targetEntity)
        {
            if (IsSpawned && !IsOwner) return false;
            if (targetEntity == null || !targetEntity.IsAlive || !KryticalNova.Combat.CombatHostility.IsHostile(transform, targetEntity)) return false;
            if (resources != null && !resources.IsAlive) return false;

            Select(targetEntity.Transform, WorldTargetKind.Hostile);
            EndTravel(false);
            if (meleeCombat != null) meleeCombat.StartAttackTarget(targetEntity);
            if (IsSpawned && !IsServer)
            {
                var targetNetObj = targetEntity.Transform.GetComponent<NetworkObject>();
                if (targetNetObj == null) return false;
                RequestAttackEntityServerRpc(targetNetObj.NetworkObjectId);
            }
            else if (meleeCombat == null) ExecuteAttackOnTarget(targetEntity);
            return true;
        }

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
                LeadOwnWalk(destination);
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
            if (interactable is IInstantInteractable) destination = ShortOf(destination, InstantInteractStandOff);
            if (IsSpawned && !IsServer)
            {
                RequestMoveDestinationServerRpc(destination);
                LeadOwnWalk(destination);
                return;
            }

            ExecuteLocalMove(destination, out _);
        }

        private void UpdatePendingInteract()
        {
            if (pendingInteract == null) return;
            // What the player was walking to may be gone by now (someone else took the item).
            if (pendingInteract is UnityEngine.Object pendingObject && pendingObject == null)
            {
                pendingInteract = null;
                return;
            }
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

        /// <summary>The point <paramref name="standOff"/> metres short of <paramref name="point"/>, on this side of it.</summary>
        private Vector3 ShortOf(Vector3 point, float standOff)
        {
            Vector3 back = transform.position - point;
            back.y = 0f;
            float distance = back.magnitude;
            return distance <= standOff ? transform.position : point + back * (standOff / distance);
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
            MoveOrdered?.Invoke();
            acceptedDestination = destinationPoint;
            if (!agent.isActiveAndEnabled || !agent.isOnNavMesh) return false;

            var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };
            // Planning the new route forgets the vault planned for the one under way. Until the new route is given
            // to the agent the old one still stands, so every refusal below puts that vault back.
            PlayerVaultMotor.Standing vaultBefore = vaultMotor != null ? vaultMotor.Keep() : default;
            if (!NavMesh.SamplePosition(destinationPoint, out NavMeshHit destination, destinationSampleRadius, filter)
                || !TryPlanRoute(destination.position))
            { KeepRoute(vaultBefore); CommandMessage?.Invoke("No complete walking path to that point."); return false; }
            if (Vector3.Distance(agent.nextPosition, destination.position + Vector3.up * agent.baseOffset) <= agent.stoppingDistance)
            { KeepRoute(vaultBefore); return false; }

            float paidCost = 0f;
            if (movementSkills != null && movementSkills.IsRunning
                && !movementSkills.TryPayForCommand(MovementSkillId.Run, out paidCost, out string paymentReason))
            { KeepRoute(vaultBefore); CommandMessage?.Invoke(paymentReason); return false; }
            agent.isStopped = false;
            if (!agent.SetPath(requestedPath))
            {
                KeepRoute(vaultBefore);
                resources?.RestoreChi(paidCost);
                CommandMessage?.Invoke("The path changed. Flux was not consumed.");
                return false;
            }
            SnapVelocityToPath();
            NoteOrder(destination.position);
            acceptedDestination = destination.position;
            // The host's own order, or the offline game's: shown as a client's is.
            if (IsSpawned) MoveOrderShownClientRpc();
            else if (animancerMotor != null) animancerMotor.RunOrderedAgain();
            return true;
        }

        [ServerRpc]
        private void RequestMoveDestinationServerRpc(Vector3 destinationPoint)
        {
            MoveOrdered?.Invoke();
            if (ServerMoveTo(destinationPoint)) MoveOrderShownClientRpc();
            else MoveRefusedRpc();
        }

        /// <summary>The player's own game: the server did not take the move it has already started walking.</summary>
        [Rpc(SendTo.Owner)]
        private void MoveRefusedRpc()
        {
            if (moveAnticipation != null) moveAnticipation.StopLeading();
        }

        /// <summary>
        /// The player's own game on a client (online phase 3): the move just asked of the server starts here at once,
        /// walked the way the server will walk it (same NavMesh, same speed), and <see cref="PlayerMoveAnticipation"/>
        /// squares it with the server's positions as they come. Nothing is decided here: no Flux is paid, no vault or
        /// climb is planned (the server's route takes those, and its positions are then followed instead).
        /// </summary>
        private void LeadOwnWalk(Vector3 destinationPoint)
        {
            if (moveAnticipation == null || !IsSpawned || IsServer || !IsOwner || agent == null) return;
            if (resources != null && !resources.IsAlive) return;
            if (deathMotor != null && deathMotor.IsDead) return;
            if ((jumpMotor != null && jumpMotor.IsBusy) || (vaultMotor != null && vaultMotor.IsBusy)
                || (wallRunMotor != null && wallRunMotor.IsBusy)) return;
            // Without the Flux for a run the server refuses it: walking off here would only be pulled back.
            if (movementSkills != null && movementSkills.IsRunning && !movementSkills.CanPayForCommand(MovementSkillId.Run, out _))
            { moveAnticipation.StopLeading(); return; }
            // The agent is on here only while this game leads a walk (PlayerMoveAnticipation switches it off after).
            if (!PlaceAgentOnNavigation(quiet: true)) return;

            var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };
            bool walks = NavMesh.SamplePosition(destinationPoint, out NavMeshHit destination, destinationSampleRadius, filter)
                && agent.CalculatePath(destination.position, requestedPath) && requestedPath.status == NavMeshPathStatus.PathComplete
                && Vector3.Distance(agent.nextPosition, destination.position + Vector3.up * agent.baseOffset) > agent.stoppingDistance;
            if (walks)
            {
                ApplyMovementSettings();
                agent.isStopped = false;
                walks = agent.SetPath(requestedPath);
            }
            if (!walks)
            {
                if (moveAnticipation.Leading) moveAnticipation.StopLeading();
                else agent.enabled = false; // switched on just now, for nothing
                return;
            }
            SnapVelocityToPath();
            moveAnticipation.BeginLeading();
        }

        /// <summary>The player's own game on a client, each frame it leads its walk: the steps the server takes for it.</summary>
        private void LeadOwnStep()
        {
            if (moveAnticipation == null || !moveAnticipation.Leading) return;
            bool held = (resources != null && !resources.IsAlive) || (deathMotor != null && deathMotor.IsDead)
                || (jumpMotor != null && jumpMotor.IsBusy) || (vaultMotor != null && vaultMotor.IsBusy)
                || (wallRunMotor != null && wallRunMotor.IsBusy);
            // A jump, a vault or a death is the server's to show: its positions take over.
            if (held || agent == null || !agent.isActiveAndEnabled || !agent.isOnNavMesh) { moveAnticipation.StopLeading(); return; }
            ApplyMovementSettings();
            UpdateArrival();
            // The walk turns the body it shows; NetworkTransform writes the server's facing over it every frame.
            transform.rotation = moveAnticipation.LeadRotation;
            TurnTowardsTravel();
            moveAnticipation.LeadRotation = transform.rotation;
        }

        /// <summary>
        /// Every machine: the player gave a move order that was taken. A run already under way takes off again
        /// (its smear), which the animation alone would not show: the run clip never stopped.
        /// </summary>
        [ClientRpc]
        private void MoveOrderShownClientRpc()
        {
            if (animancerMotor != null) animancerMotor.RunOrderedAgain();
        }

        /// <summary>
        /// Where movement is decided (the server, or the offline game): sets this player walking to
        /// <paramref name="destinationPoint"/>, over a low obstacle when that is the shorter way. A player's own order
        /// arrives here through its request; a system that moves the player for them (following a Squad member) calls
        /// it directly and says whether this move is a new order to be paid for (<paramref name="payRun"/>) or the
        /// same one being steered. False when no move was started.
        /// </summary>
        /// <summary>
        /// Server (or offline): a jump to <paramref name="landing"/> as a click there would give, with the same layers
        /// and offset, planned and paid for by this player's own jump (its rank, its Flux). Not a move order of the
        /// player's own: a Squad follower copies the member's jump through it. False, with the reason, when it cannot.
        /// </summary>
        public bool ServerJumpTo(Vector3 landing, out string reason)
        {
            reason = null;
            if (IsSpawned && !IsServer) { reason = "Only the server may do this."; return false; }
            if (jumpMotor == null) { reason = "Jump controller is missing."; return false; }
            return jumpMotor.TryJump(landing, raycastLayers, modelForwardOffset, out reason);
        }

        public bool ServerMoveTo(Vector3 destinationPoint, bool payRun = true)
        {
            if (IsSpawned && !IsServer) return false;
            if (resources != null && !resources.IsAlive) return false;
            if (jumpMotor != null && jumpMotor.IsBusy) return false;
            if (vaultMotor != null && vaultMotor.IsBusy) return false;
            if (wallRunMotor != null && wallRunMotor.IsBusy) return false;
            if (agent == null || !agent.isActiveAndEnabled || !agent.isOnNavMesh) return false;
            // The owner ended its own copy of a channel when it clicked; the authoritative one ends here.
            if (buffManager != null && buffManager.IsChanneling) buffManager.InterruptChanneled();

            var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };
            if (!NavMesh.SamplePosition(destinationPoint, out NavMeshHit destination, destinationSampleRadius, filter))
                return false;

            // As in ExecuteLocalMove: a refusal leaves the route under way with the vault that was planned for it.
            PlayerVaultMotor.Standing vaultBefore = vaultMotor != null ? vaultMotor.Keep() : default;
            if (!TryPlanRoute(destination.position))
            { KeepRoute(vaultBefore); return false; }

            if (Vector3.Distance(agent.nextPosition, destination.position + Vector3.up * agent.baseOffset) <= agent.stoppingDistance)
            { KeepRoute(vaultBefore); return false; }

            // The server knows which movement skill is selected; the request only names the place.
            float paidCost = 0f;
            if (payRun && movementSkills != null && movementSkills.IsRunning
                && !movementSkills.TryPayForCommand(MovementSkillId.Run, out paidCost, out _))
            {
                KeepRoute(vaultBefore);
                return false;
            }

            agent.isStopped = false;
            if (!agent.SetPath(requestedPath))
            {
                KeepRoute(vaultBefore);
                resources?.RestoreChi(paidCost);
                return false;
            }
            SnapVelocityToPath();
            NoteOrder(destination.position);
            return true;
        }

        /// <summary>The new route was refused: the one under way keeps the vault that was planned for it.</summary>
        private void KeepRoute(PlayerVaultMotor.Standing vaultBefore)
        {
            if (vaultMotor != null) vaultMotor.PutBack(vaultBefore);
        }

        /// <summary>A move was ordered and its path given to the agent.</summary>
        private void NoteOrder(Vector3 destination)
        {
            hasOrder = true;
            orderReplanned = false;
            orderedDestination = destination;
            NoteRoute();
        }

        /// <summary>The path now with the agent is the ordered move's (its first, or the rest of it after a vault).</summary>
        private void NoteRoute()
        {
            Vector3[] corners = requestedPath.corners;
            orderedPathEnd = corners != null && corners.Length > 0 ? corners[corners.Length - 1] : agent.destination;
            stallFrom = transform.position;
            stallWatched = 0f;
        }

        /// <summary>Plans the ordered move again from where the body is now. False when no way is found.</summary>
        private bool ReplanOrder()
        {
            if (!hasOrder || !agent.isActiveAndEnabled || !agent.isOnNavMesh) return false;
            if (resources != null && !resources.IsAlive) return false;
            if (!TryPlanRoute(orderedDestination)) return false;
            if (Vector3.Distance(agent.nextPosition, orderedDestination + Vector3.up * agent.baseOffset) <= agent.stoppingDistance) return false;
            agent.isStopped = false;
            if (!agent.SetPath(requestedPath)) return false;
            SnapVelocityToPath();
            NoteRoute();
            return true;
        }

        private static float FlatDistance(Vector3 a, Vector3 b)
        {
            a.y = b.y = 0f;
            return Vector3.Distance(a, b);
        }

        /// <summary>
        /// An ordered move that has a path and makes no headway (the body is held by something the path does not
        /// know of) would run on the spot for good: nothing else ends it. The way is planned again once; if that
        /// stalls too, the character stops where it is and says so. Only the player's own order is watched: a
        /// route another system gave the agent (walking in to attack) is that system's to end.
        /// </summary>
        private void WatchForStall()
        {
            MovementSettings settings = movementSkills != null ? movementSkills.MovementSettings : null;
            bool underWay = hasOrder && settings != null && agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh
                && agent.hasPath && !agent.pathPending && !agent.isStopped && agent.speed > 0.05f
                && !(jumpMotor != null && jumpMotor.IsBusy) && !(vaultMotor != null && vaultMotor.IsBusy)
                && !(wallRunMotor != null && (wallRunMotor.IsBusy || wallRunMotor.IsArmed))
                && FlatDistance(agent.destination, orderedPathEnd) <= SameRouteDistance;
            if (!underWay)
            {
                stallWatched = 0f;
                stallFrom = transform.position;
                return;
            }

            stallWatched += Time.deltaTime;
            float gained = FlatDistance(transform.position, stallFrom);
            if (gained >= settings.stallDistance)
            {
                stallWatched = 0f;
                stallFrom = transform.position;
                return;
            }
            if (!settings.HasStalled(gained, stallWatched)) return;

            stallWatched = 0f;
            stallFrom = transform.position;
            if (!orderReplanned)
            {
                orderReplanned = true;
                if (ReplanOrder()) return;
            }
            StopMoving();
            CommandMessage?.Invoke("Something is in the way. The move was stopped.");
        }

        /// <summary>
        /// Where movement is decided: a route that reaches a gap between roofs (a NavMeshLink) jumps it with the real
        /// Jump, then carries on to where it was going. Without Jump, or Flux for it, the move stops at the edge and
        /// says why.
        /// </summary>
        private void CrossLinks()
        {
            if (agent == null || !agent.isActiveAndEnabled) return;
            if (crossingLink)
            {
                if (jumpMotor != null && jumpMotor.IsBusy) return;
                crossingLink = false;
                if (hasOrder) ResumeMoveAfterVault(orderedDestination);
                return;
            }
            if (!agent.isOnNavMesh || !agent.isOnOffMeshLink) return;
            Vector3 across = agent.currentOffMeshLinkData.endPos;
            string reason = "Jump is needed to cross that gap.";
            if (jumpMotor != null && jumpMotor.TryJump(across, raycastLayers, modelForwardOffset, out reason))
            {
                crossingLink = true;
                return;
            }
            // Off the link, back on its near end: an agent left on a link would ask again every frame.
            PlaceOn(agent.currentOffMeshLinkData.startPos);
            StopMoving();
            CommandMessage?.Invoke(string.IsNullOrEmpty(reason) ? "Jump is needed to cross that gap." : reason);
        }

        /// <summary>Where movement is decided: stops this player where it stands.</summary>
        public void ServerStopMoving()
        {
            if (!IsSpawned || IsServer) StopMoving();
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
            // Wallstride: running at a wall the destination stands on, with no way round or a much longer one, goes up it.
            if (wallRunMotor != null && wallRunMotor.TryArmTowards(destination, walkable ? requestedPath : null, out Vector3 wallFoot))
            {
                if (agent.CalculatePath(wallFoot, requestedPath) && requestedPath.status == NavMeshPathStatus.PathComplete) return true;
                wallRunMotor.Disarm();
                walkable = agent.CalculatePath(destination, requestedPath) && requestedPath.status == NavMeshPathStatus.PathComplete;
            }
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
            if (!agent.SetPath(requestedPath)) return;
            SnapVelocityToPath();
            hasOrder = true;
            orderedDestination = target.position;
            NoteRoute();
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

            // The agent takes its step for this frame after this runs, so the question is whether that step gets
            // it there. The fast Run ranks cover more than the stopping distance in a frame: without this they
            // step over the point, turn back and circle it, and the stop never comes.
            float remaining = RemainingPathDistance();
            float step = agent.velocity.magnitude * Time.deltaTime;
            MovementSettings settings = movementSkills != null ? movementSkills.MovementSettings : null;
            bool arrives = settings != null
                ? settings.ArrivesThisFrame(remaining, agent.velocity.magnitude, Time.deltaTime)
                : remaining <= agent.stoppingDistance + step;
            if (!arrives && !HasPassedDestination(remaining, step)) return;

            Vector3 destination = agent.destination;
            // Less than a frame's travel away: the body ends on the point rather than short of it or past it. Placed
            // there, not moved there: a move would read as speed for a frame and set the run going again.
            if (remaining > agent.stoppingDistance) PlaceOn(destination);
            agent.ResetPath();
            agent.velocity = Vector3.zero;
            FinishRoute(destination);
        }

        /// <summary>
        /// Puts the agent on a NavMesh point and the root over it in the same step. Warp alone leaves the root on
        /// the point itself, the agent's base offset too low, until the agent next updates: the body was drawn sunk
        /// to the waist in the ground for a frame at the end of every run, and the camera dropped with it.
        /// </summary>
        private void PlaceOn(Vector3 navMeshPoint)
        {
            if (!agent.Warp(navMeshPoint)) return;
            transform.position = navMeshPoint + Vector3.up * agent.baseOffset;
        }

        /// <summary>
        /// The agent's path has ended at <paramref name="arrivedAt"/>. When that was the ordered move's path and it
        /// ended short of the order with no vault taking over (the path led to a take-off point and the vault is
        /// no longer planned), the rest of the way is planned from here, once: the character does not stop in
        /// front of the obstacle.
        /// </summary>
        private void FinishRoute(Vector3 arrivedAt)
        {
            if (!hasOrder) return;
            if (FlatDistance(arrivedAt, orderedPathEnd) > SameRouteDistance) { hasOrder = false; return; } // not the order's path
            if (vaultMotor != null && (vaultMotor.IsArmed || vaultMotor.IsBusy)) return; // it carries on from the landing
            if (wallRunMotor != null && (wallRunMotor.IsArmed || wallRunMotor.IsBusy)) return; // up the wall from its foot
            bool there = FlatDistance(arrivedAt, orderedDestination) <= agent.stoppingDistance + ShortOfOrderDistance;
            if (there || orderReplanned) { hasOrder = false; return; }
            orderReplanned = true;
            if (!ReplanOrder()) hasOrder = false;
        }

        /// <summary>On the last leg, right at the point, and travelling away from it: it was stepped over.</summary>
        private bool HasPassedDestination(float remaining, float step)
        {
            if (remaining > agent.stoppingDistance + step * 2f) return false;
            if ((agent.steeringTarget - agent.destination).sqrMagnitude > 0.01f) return false;
            Vector3 toDestination = agent.destination - agent.nextPosition;
            toDestination.y = 0f;
            Vector3 travel = agent.velocity;
            travel.y = 0f;
            return Vector3.Dot(travel, toDestination) < 0f;
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
                    CommandMessage?.Invoke($"Attack REFLECTED by {targetEntity.EntityName}'s Backlash!");
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

        /// <summary>
        /// Server (or offline): turns the body to a point while it stands, for <paramref name="seconds"/> at most
        /// (bending down for an item). Walking off ends it: travel then decides the facing.
        /// </summary>
        public void FacePoint(Vector3 point, float seconds)
        {
            facePoint = point;
            faceUntil = Time.time + Mathf.Max(0f, seconds);
        }

        private void TurnTowardsFacePoint()
        {
            if (Time.time >= faceUntil) return;
            bool hasAgent = agent != null && agent.isActiveAndEnabled;
            if (hasAgent && agent.velocity.sqrMagnitude >= MinimumFacingSpeedSquared) return; // travel is turning the body

            Vector3 direction = facePoint - transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude < MinimumFacingSpeedSquared) return;
            Quaternion facing = Quaternion.LookRotation(direction, Vector3.up)
                * Quaternion.AngleAxis(modelForwardOffset, Vector3.up);
            float degreesPerSecond = hasAgent ? agent.angularSpeed : 720f;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, facing, degreesPerSecond * Time.deltaTime);
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
            if (wallRunMotor != null && wallRunMotor.IsBusy) return;
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
            // The player's own taste (Settings, Gameplay) goes over the rig's numbers.
            Vector2 delta = CameraPreferences.Turn(orbitDeltaAction.ReadValue<Vector2>(), orbitSensitivity, invertVerticalOrbit);
            orbitCamera.HorizontalAxis.Value = orbitCamera.HorizontalAxis.ClampValue(orbitCamera.HorizontalAxis.Value + delta.x);
            float verticalDelta = delta.y;

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

        private const float ScreenFramingLimit = 0.4f;
        private const float ScreenFramingEase = 5f;

        /// <summary>
        /// Stands the character to one side of the screen: 0 is the middle, -0.5 the left edge, +0.5 the right. The
        /// camera eases there. The HUD uses it while a service window covers the middle (see ServicePanelPlacement).
        /// </summary>
        public void SetScreenFraming(float across) => screenFraming = Mathf.Clamp(across, -ScreenFramingLimit, ScreenFramingLimit);

        /// <summary>
        /// Set by a tool that takes the left button and Escape for itself (Arrange, while a Hall is furnished): a
        /// click no longer walks or selects and Shift no longer draws the weapon. The camera is left alone, so the
        /// right button still turns it and the wheel still zooms.
        /// </summary>
        public bool WorldClicksParked { get; set; }

        /// <summary>
        /// A click on the world made on the player's behalf by the tool that has parked the clicks: it walks, or
        /// picks what is under the cursor, exactly as the player's own click would.
        /// </summary>
        public void ClickWorld(Vector2 screenPosition) => TryMoveToPointer(screenPosition);

        /// <summary>The camera the game is seen through on this machine, or null.</summary>
        public Camera GameplayCamera => gameplayCamera;

        private void ApplyScreenFraming()
        {
            if (orbitComposer == null) return;
            screenFramingShown = Mathf.Lerp(screenFramingShown, screenFraming, 1f - Mathf.Exp(-Time.unscaledDeltaTime * ScreenFramingEase));
            if (Mathf.Abs(screenFramingShown - screenFraming) < 0.0005f) screenFramingShown = screenFraming;
            var composition = orbitComposer.Composition;
            if (Mathf.Approximately(composition.ScreenPosition.x, screenFramingShown)) return;
            composition.ScreenPosition = new Vector2(screenFramingShown, composition.ScreenPosition.y);
            orbitComposer.Composition = composition;
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

