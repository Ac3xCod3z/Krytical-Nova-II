using System;
using System.Collections;
using System.Collections.Generic;
using Kinemation.MotionWarping.Runtime.Core;
using Kinemation.MotionWarping.Runtime.Utility;
using KryticalNova.Combat;
using KryticalNova.Core;
using KryticalNova.Networking;
using KryticalNova.Progression;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using WarpComponent = Kinemation.MotionWarping.Runtime.Core.MotionWarping;

namespace KryticalNova.Player
{
    /// <summary>
    /// What happens to a player's body when it dies, and how it comes back. The killing blow picks the animation:
    /// a blow from the front half turns the body to face it and throws it over backwards; a blow from behind is
    /// taken as the body stands and throws it down forwards, never spun round first. A critical blow uses the big
    /// knockdowns and launches the body away from the killer. As with the vault, Animancer plays the pose (a
    /// knockdown, then the loop the body is left lying in) while Motion Warping moves the root on the same clock,
    /// following the travel baked from the clip's own root motion. Only the authority (server, or offline) moves
    /// the body; every other machine plays the same clips while NetworkTransform carries it. After a delay the
    /// server puts the player back on a spawn point of the map with full health.
    /// </summary>
    [DisallowMultipleComponent]
    // Motion Warping runs at order 100 and rewrites the root every LateUpdate; the turn toward the blow is laid over it.
    [DefaultExecutionOrder(150)]
    public sealed class PlayerDeathMotor : NetworkBehaviour
    {
        private const float GroundSampleRadius = 4f;
        private const float LandingSampleRadius = 1.5f;
        private const float MinimumLift = 0.001f;
        private const float BodyRadius = 0.3f;
        private const float ChestHeight = 0.9f;
        /// <summary>Killed this far (m) above where it will land, the body is in the air and falls.</summary>
        private const float AirborneHeight = 0.15f;
        /// <summary>How far below the body the ground is looked for when it dies high up (a wall, a tree).</summary>
        private const float FallProbe = 80f;
        private const int HeightCurveSteps = 24;

        /// <summary>One way of going down: the knockdown clip, the loop it ends in, and how the clip travels.</summary>
        [Serializable]
        public sealed class DeathChain
        {
            [Tooltip("The knockdown or flight, played once.")]
            public AnimationClip fall;
            [Tooltip("The lying pose the body is left in.")]
            public AnimationClip loop;
            [Tooltip("0..1 over the fall clip's time: how far along its travel the body is. Baked from the clip's own root " +
                     "motion (DeathAnimationSetup), so the feet and the ground agree.")]
            public AnimationCurve travel = AnimationCurve.Linear(0f, 0f, 1f, 1f);
            [Tooltip("Metres the clip itself carries the body.")]
            [Min(0f)] public float distance = 0.5f;
            [Tooltip("Seconds into the fall at which the body hits the ground (dust, thud).")]
            [Min(0f)] public float impactTime = 0.5f;

            public bool IsReady => fall != null && loop != null && travel != null && travel.length >= 2;
        }

        private enum Phase : byte { None, Falling, Down }

        // What a late joiner needs to show a body that is already lying there.
        private enum Pose : byte { Alive = 0, OnBack = 1, FaceDown = 2, Gone = 3 }

        [Header("Normal death (the blow's side picks the set)")]
        [SerializeField] private DeathChain front = new DeathChain();
        [SerializeField] private DeathChain back = new DeathChain();

        [Header("Critical death (launched away from the killer)")]
        [SerializeField] private DeathChain critFront = new DeathChain();
        [SerializeField] private DeathChain critBack = new DeathChain();
        [Tooltip("Metres a critical blow throws the body. The clip's own travel is stretched to it.")]
        [SerializeField, Min(0.5f)] private float critDistance = 5.5f;
        [Tooltip("Extra height (m) put on the flight on top of what the clip itself has.")]
        [SerializeField, Min(0f)] private float critLift = 0.55f;

        [Header("Blend")]
        [SerializeField, Min(0.01f)] private float fallFadeIn = 0.08f;
        [SerializeField, Min(0.01f)] private float loopFade = 0.18f;
        [Tooltip("Seconds a blow from the front half takes to turn the body to face it. Blows from behind never turn it.")]
        [SerializeField, Min(0.01f)] private float turnSeconds = 0.12f;
        [SerializeField, Min(0.01f)] private float reviveFade = 0.25f;

        [Header("Landing")]
        [Tooltip("What stops a thrown body (walls, props). Other characters never do.")]
        [SerializeField] private LayerMask blockingLayers = Physics.DefaultRaycastLayers;
        [Tooltip("Metres kept between the body and whatever stopped it, so it does not lie inside a wall.")]
        [SerializeField, Min(0f)] private float wallClearance = 0.9f;

        [Header("Motion Warping")]
        [Tooltip("Template for the fall: the motor writes the travel, the height and the phase for each death.")]
        [SerializeField] private MotionWarpingAsset warpTemplate;
        [Tooltip("The warp component shared with the jump and the vault; it moves this root.")]
        [SerializeField] private WarpComponent motionWarping;

        [Header("Killed off the ground (owner, 17 Oct 2026)")]
        [Tooltip("Standing this high over the ground with no walk down to it (a roof), the body de-rezzes instead of falling.")]
        [SerializeField, Min(0.5f)] private float roofHeight = 2.5f;
        [Tooltip("The death clip plays this slowly while the body de-rezzes: caught in the blow as it is deleted.")]
        [SerializeField, Range(0.05f, 1f)] private float derezClipSpeed = 0.2f;

        [Header("Respawn")]
        [Tooltip("Seconds the body lies there before the server puts the player back on a spawn point.")]
        [SerializeField, Min(0f)] private float respawnDelay = 8f;

        private readonly NetworkVariable<byte> netPose = new NetworkVariable<byte>(writePerm: NetworkVariableWritePermission.Server);

        private static readonly RaycastHit[] WallHits = new RaycastHit[8];

        private NavMeshAgent agent;
        private PlayerAnimancerMotor animancerMotor;
        private PlayerJumpMotor jumpMotor;
        private PlayerVaultMotor vaultMotor;
        private PlayerWallRunMotor wallRunMotor;
        private IMeleeAttacker meleeCombat;
        private PlayerResources resources;
        private CombatEntity combatEntity;
        private CharacterBuffManager buffManager;
        private MotionWarpingAsset warpAsset;
        private Coroutine respawnRoutine;

        private Phase phase;
        private DeathChain chain;
        private Animancer.AnimancerState fallState;
        private bool critical;
        private bool involvesLocalPlayer;
        private bool impactShown;
        private float impactTime;
        private float clock;
        private bool turning;
        private Quaternion turnFrom;
        private Quaternion turnTo;
        private Vector3 landingRoot;

        /// <summary>Raised where the death is decided (server or offline), with the blow that caused it.</summary>
        public event Action<KillingBlow> Died;
        /// <summary>Raised where the death is decided: whole seconds left until the respawn (at the start, then 3, 2, 1).</summary>
        public event Action<int> RespawnCountdown;
        /// <summary>Raised where the death is decided, once the player is back on a spawn point with full health.</summary>
        public event Action Respawned;

        public bool IsDead => phase != Phase.None;
        private bool HasAuthority => !IsSpawned || IsServer;
        private bool Ready => motionWarping != null && warpAsset != null && animancerMotor != null
            && front.IsReady && back.IsReady && critFront.IsReady && critBack.IsReady;

        private void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            animancerMotor = GetComponent<PlayerAnimancerMotor>();
            jumpMotor = GetComponent<PlayerJumpMotor>();
            vaultMotor = GetComponent<PlayerVaultMotor>();
            wallRunMotor = GetComponent<PlayerWallRunMotor>();
            meleeCombat = GetComponent<IMeleeAttacker>();
            resources = GetComponent<PlayerResources>();
            combatEntity = GetComponent<CombatEntity>();
            buffManager = GetComponent<CharacterBuffManager>();
            if (warpTemplate != null)
            {
                warpAsset = Instantiate(warpTemplate);
                warpAsset.name = warpTemplate.name + " (runtime)";
            }
        }

        private void OnEnable()
        {
            if (combatEntity != null) combatEntity.Killed += OnKilled;
        }

        private void OnDisable()
        {
            if (combatEntity != null) combatEntity.Killed -= OnKilled;
        }

        public override void OnDestroy()
        {
            if (warpAsset != null) Destroy(warpAsset);
            base.OnDestroy();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            // Joined while this player was already down: show the body lying there, without the fall; or, de-rezzed, nothing.
            if (!IsServer && netPose.Value == (byte)Pose.Gone) StartCoroutine(HideWhenReady());
            else if (!IsServer && netPose.Value != (byte)Pose.Alive) StartCoroutine(ShowBodyWhenReady((Pose)netPose.Value));
        }

        // ------------------------------------------------------------------ dying (authority)

        private void OnKilled(KillingBlow blow)
        {
            if (!HasAuthority || IsDead) return;
            if (!Ready)
            {
                Debug.LogWarning("[PlayerDeathMotor] Death clips or warp asset missing: run Krytical Nova ▸ Player ▸ Set Up Death Animations.", this);
                return;
            }

            Vector3 direction = Flat(blow.Direction, -transform.forward);
            // In the air, on a wall, in a tree or on a roof: the body de-rezzes where it is instead of falling.
            if (IsOffTheGround())
            {
                Disintegrate(blow, direction);
                return;
            }
            // The blow came from the front half when its source is ahead of the body.
            bool fromFront = Vector3.Dot(Flat(transform.forward, Vector3.forward), -direction) >= 0f;
            bool crit = blow.IsCritical;
            DeathChain picked = Pick(fromFront, crit);

            // Whatever was moving the body lets go of it where it is.
            if (jumpMotor != null) jumpMotor.ReleaseForDeath();
            if (vaultMotor != null) vaultMotor.ReleaseForDeath();
            // On a wall or in a tree: pushed out from it, so the body falls clear.
            if (wallRunMotor != null) wallRunMotor.ReleaseForDeath();
            if (buffManager != null) buffManager.ClearAllBuffs();
            if (agent.isActiveAndEnabled && agent.isOnNavMesh)
            {
                agent.isStopped = true;
                agent.ResetPath();
                agent.velocity = Vector3.zero;
            }
            agent.enabled = false; // the warp owns the root now; the respawn puts the agent back

            Vector3 landing = FindLanding(direction, crit ? critDistance : picked.distance);
            // Front half: face the blow. Back half: stay as you stand.
            float facing = fromFront ? Quaternion.LookRotation(-direction, Vector3.up).eulerAngles.y : transform.eulerAngles.y;

            if (IsSpawned) netPose.Value = (byte)(fromFront ? Pose.OnBack : Pose.FaceDown);
            Begin(fromFront, crit, direction, landing, facing, blow.Attacker != null ? blow.Attacker.transform : null);
            if (IsSpawned)
            {
                NetworkObjectReference killer = default;
                if (blow.Attacker != null && blow.Attacker.TryGetComponent(out NetworkObject killerObject) && killerObject.IsSpawned)
                    killer = killerObject;
                DeathClientRpc(fromFront, crit, direction, landing, facing, killer);
            }

            Died?.Invoke(blow);
            if (respawnRoutine != null) StopCoroutine(respawnRoutine);
            respawnRoutine = StartCoroutine(RespawnRoutine());
        }

        /// <summary>
        /// Up a wall or a tree, in a jump or a vault, or standing on a roof: high off the ground with no walk down to
        /// it. Decided where the death is decided, before the motors let go of the body.
        /// </summary>
        private bool IsOffTheGround()
        {
            if ((wallRunMotor != null && wallRunMotor.IsBusy) || (jumpMotor != null && jumpMotor.IsBusy) || (vaultMotor != null && vaultMotor.IsBusy))
                return true;
            Vector3 feet = transform.position - Vector3.up * agent.baseOffset;
            int groundMask = LayerMask.GetMask(GroundLayerName);
            if (groundMask == 0 || !Physics.Raycast(feet + Vector3.up * 0.3f, Vector3.down, out RaycastHit ground, FallProbe, groundMask, QueryTriggerInteraction.Ignore))
                return false;
            if (feet.y - ground.point.y < roofHeight) return false;
            // High up: a raised plaza or a walkway joined to the streets is still ground; a roof is not.
            var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };
            // Nothing walkable right under the feet: in the air.
            if (!NavMesh.SamplePosition(feet, out NavMeshHit standing, StandingSampleRadius, filter) || Mathf.Abs(standing.position.y - feet.y) > 0.5f) return true;
            if (!NavMesh.SamplePosition(ground.point, out NavMeshHit street, 3f, filter)) return true;
            var path = new NavMeshPath();
            return !NavMesh.CalculatePath(standing.position, street.position, filter, path) || path.status != NavMeshPathStatus.PathComplete;
        }

        private const string GroundLayerName = "Ground";
        private const float StandingSampleRadius = 0.75f;

        /// <summary>Authority: the body stays where it was killed and de-rezzes on every machine; the respawn timer runs as for any death.</summary>
        private void Disintegrate(KillingBlow blow, Vector3 direction)
        {
            if (jumpMotor != null) jumpMotor.ReleaseForDeath();
            if (vaultMotor != null) vaultMotor.ReleaseForDeath();
            if (wallRunMotor != null) wallRunMotor.ReleaseForDeath(pushOff: false); // nothing falls: it stays where it was hit
            if (buffManager != null) buffManager.ClearAllBuffs();
            if (motionWarping != null && motionWarping.IsActive()) motionWarping.Stop();
            if (agent.isActiveAndEnabled && agent.isOnNavMesh)
            {
                agent.isStopped = true;
                agent.ResetPath();
                agent.velocity = Vector3.zero;
            }
            agent.enabled = false; // the respawn puts the agent back

            if (IsSpawned) netPose.Value = (byte)Pose.Gone;
            BeginDisintegrate(direction, blow.Attacker != null ? blow.Attacker.transform : null);
            if (IsSpawned)
            {
                NetworkObjectReference killer = default;
                if (blow.Attacker != null && blow.Attacker.TryGetComponent(out NetworkObject killerObject) && killerObject.IsSpawned)
                    killer = killerObject;
                DisintegrateClientRpc(direction, killer);
            }

            Died?.Invoke(blow);
            if (respawnRoutine != null) StopCoroutine(respawnRoutine);
            respawnRoutine = StartCoroutine(RespawnRoutine());
        }

        [ClientRpc]
        private void DisintegrateClientRpc(Vector3 direction, NetworkObjectReference killer)
        {
            if (IsServer || !Ready) return; // the server is already running it
            Transform killerTransform = killer.TryGet(out NetworkObject killerObject) ? killerObject.transform : null;
            BeginDisintegrate(direction, killerTransform);
        }

        /// <summary>Every machine: the body is caught in the blow while it de-rezzes, then gone until the respawn.</summary>
        private void BeginDisintegrate(Vector3 direction, Transform killer)
        {
            chain = front;
            critical = false;
            turning = false;
            clock = 0f;
            phase = Phase.Down; // dead, with no fall to play out
            if (meleeCombat != null) meleeCombat.CancelAttack();
            fallState = animancerMotor.PlayDeathClip(front.fall, fallFadeIn);
            if (fallState != null) fallState.Speed = derezClipSpeed;

            bool victimIsLocal = !IsSpawned || IsOwner;
            bool killerIsLocal = killer != null && IsLocalPlayer(killer);
            involvesLocalPlayer = victimIsLocal || killerIsLocal;
            var feel = ClientPresentationRegistry.Feel;
            if (feel == null) return;
            feel.Airborne(transform, false);
            feel.Disintegrate(new DeathFeel
            {
                Point = transform.position,
                Direction = direction,
                Target = transform,
                Killer = killer,
                IsCritical = false,
                VictimIsLocalPlayer = victimIsLocal,
                KillerIsLocalPlayer = killerIsLocal,
            });
        }

        [ClientRpc]
        private void DeathClientRpc(bool fromFront, bool crit, Vector3 direction, Vector3 landing, float facing, NetworkObjectReference killer)
        {
            if (IsServer || !Ready) return; // the server is already running it
            Transform killerTransform = killer.TryGet(out NetworkObject killerObject) ? killerObject.transform : null;
            Begin(fromFront, crit, direction, landing, facing, killerTransform);
        }

        private DeathChain Pick(bool fromFront, bool crit) => crit ? (fromFront ? critFront : critBack) : (fromFront ? front : back);

        /// <summary>
        /// Starts the fall. Where the player is simulated this moves the body with the warp; every other machine
        /// plays the same clips on the same clock while NetworkTransform carries the body.
        /// </summary>
        private void Begin(bool fromFront, bool crit, Vector3 direction, Vector3 landing, float facing, Transform killer)
        {
            chain = Pick(fromFront, crit);
            critical = crit;
            impactShown = false;
            clock = 0f;
            phase = Phase.Falling;
            landingRoot = landing;

            // The combo this character was in the middle of stops on every machine that was showing it.
            if (meleeCombat != null) meleeCombat.CancelAttack();
            fallState = animancerMotor.PlayDeathClip(chain.fall, fallFadeIn);

            // Killed in the air (a jump, a vault): the body drops to the ground under gravity while the clip plays,
            // and the thud comes when it gets there.
            float drop = transform.position.y - landing.y;
            if (drop < AirborneHeight) drop = 0f;
            AnimationCurve height = HeightCurve(crit ? critLift : 0f, drop, out float fallSeconds);
            impactTime = Mathf.Max(chain.impactTime, fallSeconds);

            if (HasAuthority)
            {
                turnFrom = transform.rotation;
                turnTo = Quaternion.Euler(0f, facing, 0f);
                turning = fromFront && Quaternion.Angle(turnFrom, turnTo) > 0.5f;

                ShapeWarp(height);
                // The warp keeps the rotation it starts with; the turn toward the blow is laid over it in LateUpdate.
                motionWarping.Play(warpAsset, new[] { new WarpPoint { position = landing, rotation = transform.rotation } });
            }

            bool victimIsLocal = !IsSpawned || IsOwner;
            bool killerIsLocal = killer != null && IsLocalPlayer(killer);
            involvesLocalPlayer = victimIsLocal || killerIsLocal;
            var feel = ClientPresentationRegistry.Feel;
            if (feel != null)
            {
                feel.Airborne(transform, false);
                feel.Death(new DeathFeel
                {
                    Point = transform.position,
                    Direction = direction,
                    Target = transform,
                    Killer = killer,
                    IsCritical = crit,
                    VictimIsLocalPlayer = victimIsLocal,
                    KillerIsLocalPlayer = killerIsLocal,
                });
            }
        }

        // This machine's own player: the offline avatar, or the networked player it owns. Never an NPC or a dummy.
        private static bool IsLocalPlayer(Transform actor)
        {
            if (actor.GetComponent<PlayerResources>() == null) return false;
            var netObject = actor.GetComponent<NetworkObject>();
            return netObject == null || !netObject.IsSpawned || netObject.IsOwner;
        }

        /// <summary>
        /// The root's height over the fall, relative to where it starts: an arc of <paramref name="lift"/> metres for
        /// a thrown body, and for a body killed <paramref name="drop"/> metres up, the fall to the ground under
        /// gravity (ending <paramref name="drop"/> lower, where the landing is).
        /// </summary>
        private AnimationCurve HeightCurve(float lift, float drop, out float fallSeconds)
        {
            float length = chain.fall.length;
            fallSeconds = 0f;
            if (lift <= 0f && drop <= 0f) return AnimationCurve.Constant(0f, length, 0f);

            float gravity = Physics.gravity.magnitude;
            if (drop > 0f)
            {
                fallSeconds = Mathf.Sqrt(2f * drop / gravity);
                // From very high up the body must still be down before the clip ends: it falls faster instead.
                float latest = Mathf.Max(0.1f, length - 0.1f);
                if (fallSeconds > latest)
                {
                    fallSeconds = latest;
                    gravity = 2f * drop / (fallSeconds * fallSeconds);
                }
            }

            float land = Mathf.Clamp(chain.impactTime, 0.1f, length);
            var keys = new Keyframe[HeightCurveSteps + 1];
            for (int i = 0; i <= HeightCurveSteps; i++)
            {
                float t = length * i / HeightCurveSteps;
                float y = 0f;
                if (lift > 0f && t < land)
                {
                    float u = t / land;
                    y += lift * 4f * u * (1f - u);
                }
                if (drop > 0f) y -= Mathf.Min(drop, 0.5f * gravity * t * t);
                keys[i] = new Keyframe(t, y);
            }
            var curve = new AnimationCurve(keys);
            for (int i = 0; i < keys.Length; i++) curve.SmoothTangents(i, 0f);
            return curve;
        }

        /// <summary>
        /// Writes this death into the warp asset: one phase over the whole fall, travel along the ground following the
        /// clip's own profile so it ends exactly on the landing, and the height curve (a thrown body's arc, an
        /// airborne body's drop).
        /// </summary>
        private void ShapeWarp(AnimationCurve height)
        {
            float length = chain.fall.length;
            float lift = 0f;
            for (int i = 0; i < height.length; i++) lift = Mathf.Max(lift, Mathf.Abs(height[i].value));
            warpAsset.animation = chain.fall; // gives the warp its length
            warpAsset.rootX = chain.travel;
            warpAsset.rootZ = chain.travel;
            warpAsset.rootY = height;
            warpAsset.useLinear = new VectorBool(false, true, false);    // ground travel follows the clip's profile
            warpAsset.useAnimation = new VectorBool(false, true, false); // only the height curve is taken as is
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
                endTime = length,
                minRate = 1f,
                maxRate = 1f,
                // The travel profile runs from 0 to 1, so its accumulated motion is the share of the way covered.
                totalRootMotion = new Vector3(1f, Mathf.Max(MinimumLift, lift * 2f), 1f),
            });
        }

        /// <summary>
        /// Where the body ends up: <paramref name="distance"/> metres along the blow from the ground under it,
        /// stopped short of walls and of the edge of the walkable ground. Returns the root position.
        /// </summary>
        private Vector3 FindLanding(Vector3 direction, float distance)
        {
            var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };
            Vector3 feet = transform.position - Vector3.up * agent.baseOffset;
            // High off the ground (up a wall, in a tree, at the top of a jump): the fall starts from the ground below.
            if (!NavMesh.SamplePosition(feet, out NavMeshHit _, GroundSampleRadius, filter)
                && Physics.Raycast(feet + Vector3.up * 0.1f, Vector3.down, out RaycastHit below, FallProbe,
                    blockingLayers.value & ~(1 << gameObject.layer), QueryTriggerInteraction.Ignore))
                feet = below.point;
            Vector3 from = NavMesh.SamplePosition(feet, out NavMeshHit ground, GroundSampleRadius, filter) ? ground.position : feet;

            float allowed = distance;
            int mask = blockingLayers.value & ~(1 << gameObject.layer);
            int count = Physics.SphereCastNonAlloc(from + Vector3.up * ChestHeight, BodyRadius, direction, WallHits, distance, mask,
                QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider hit = WallHits[i].collider;
                if (hit == null || hit.transform.IsChildOf(transform)) continue;
                if (hit.GetComponentInParent<IDamageable>() != null) continue; // bodies fly past other characters
                allowed = Mathf.Min(allowed, Mathf.Max(0f, WallHits[i].distance - wallClearance));
            }

            Vector3 to = from + direction * allowed;
            if (allowed > 0.01f && NavMesh.Raycast(from, to, out NavMeshHit edge, filter)) to = edge.position;
            if (NavMesh.SamplePosition(to, out NavMeshHit landing, LandingSampleRadius, filter)) to = landing.position;
            return to + Vector3.up * agent.baseOffset;
        }

        // ------------------------------------------------------------------ run (every peer)

        private void Update()
        {
            if (phase == Phase.None) return;
            // Brought back by something other than the respawn timer (a GM reset, later a revive): up where it lies.
            if (HasAuthority && resources != null && resources.IsAlive)
            {
                Revive(false);
                return;
            }
            if (phase != Phase.Falling) return;
            clock += Time.deltaTime;

            if (!impactShown && clock >= impactTime)
            {
                impactShown = true;
                ClientPresentationRegistry.Feel?.BodyLanded(transform, critical, involvesLocalPlayer);
            }

            // Follow the clip's own time (a hit-stop may have held it), with the clock as the backstop.
            float played = fallState != null ? fallState.Time : clock;
            if (played >= chain.fall.length - loopFade || clock >= chain.fall.length + 0.5f) LieDown();
        }

        private void LateUpdate()
        {
            if (!turning || phase == Phase.None) return;
            float t = Mathf.Clamp01(clock / turnSeconds);
            transform.rotation = Quaternion.Slerp(turnFrom, turnTo, t * t * (3f - 2f * t));
            if (t >= 1f && !motionWarping.IsActive()) turning = false;
        }

        private void LieDown()
        {
            phase = Phase.Down;
            fallState = null;
            animancerMotor.PlayDeathClip(chain.loop, loopFade);
            if (!HasAuthority) return;
            if (motionWarping.IsActive()) motionWarping.Stop();
            transform.position = landingRoot;
            if (turning) transform.rotation = turnTo;
            turning = false;
        }

        private IEnumerator HideWhenReady()
        {
            yield return null;
            if (IsDead || netPose.Value != (byte)Pose.Gone) yield break;
            phase = Phase.Down;
            ClientPresentationRegistry.Feel?.SetDisintegrated(transform, true);
            // The pet is made a moment after its owner: hidden again once it is there.
            yield return new WaitForSeconds(1f);
            if (phase != Phase.None && netPose.Value == (byte)Pose.Gone) ClientPresentationRegistry.Feel?.SetDisintegrated(transform, true);
        }

        private IEnumerator ShowBodyWhenReady(Pose pose)
        {
            // The animation motor starts its idle in Start; the body is laid down after that.
            yield return null;
            if (!Ready || IsDead || netPose.Value == (byte)Pose.Alive) yield break;
            chain = pose == Pose.OnBack ? front : back;
            critical = false;
            phase = Phase.Down;
            animancerMotor.PlayDeathClip(chain.loop, 0f);
        }

        // ------------------------------------------------------------------ respawn (authority)

        private IEnumerator RespawnRoutine()
        {
            float left = respawnDelay;
            int announced = Mathf.CeilToInt(left);
            RespawnCountdown?.Invoke(announced);
            while (left > 0f)
            {
                yield return null;
                left -= Time.deltaTime;
                int whole = Mathf.CeilToInt(left);
                if (whole < announced && whole > 0 && whole <= 3) RespawnCountdown?.Invoke(whole);
                announced = Mathf.Min(announced, whole);
            }
            respawnRoutine = null;
            Respawn();
        }

        /// <summary>
        /// Server (or offline) only: put the dead player back on one of the map's spawn points, alive and at full
        /// health. Called by the respawn timer; a GM tool may call it early.
        /// </summary>
        public void Respawn()
        {
            if (HasAuthority && IsDead) Revive(true);
        }

        private void Revive(bool atSpawnPoint)
        {
            if (respawnRoutine != null)
            {
                StopCoroutine(respawnRoutine);
                respawnRoutine = null;
            }
            if (motionWarping != null && motionWarping.IsActive()) motionWarping.Stop();
            turning = false;

            agent.enabled = false;
            if (atSpawnPoint) PlayerSpawnService.Respawn(transform); // each map supplies its own points
            RestoreAgent();
            if (resources != null && !resources.IsAlive) resources.RestoreAll();

            if (IsSpawned) netPose.Value = (byte)Pose.Alive;
            GetUp();
            if (IsSpawned) RevivedClientRpc();
            Respawned?.Invoke();
        }

        [ClientRpc]
        private void RevivedClientRpc()
        {
            if (!IsServer) GetUp();
        }

        private void GetUp()
        {
            phase = Phase.None;
            fallState = null;
            animancerMotor.Revive(reviveFade);
            ClientPresentationRegistry.Feel?.SetDisintegrated(transform, false); // a de-rezzed body (and pet) comes back
            ClientPresentationRegistry.Feel?.Respawned(transform, !IsSpawned || IsOwner);
        }

        private void RestoreAgent()
        {
            var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };
            Vector3 feet = transform.position - Vector3.up * agent.baseOffset;
            if (NavMesh.SamplePosition(feet, out NavMeshHit hit, GroundSampleRadius, filter))
                transform.position = hit.position + Vector3.up * agent.baseOffset;
            agent.enabled = true;
            if (!agent.isOnNavMesh) return;
            agent.ResetPath();
            agent.isStopped = false;
        }

        private static Vector3 Flat(Vector3 v, Vector3 fallback)
        {
            v.y = 0f;
            if (v.sqrMagnitude > 0.0001f) return v.normalized;
            fallback.y = 0f;
            return fallback.sqrMagnitude > 0.0001f ? fallback.normalized : Vector3.forward;
        }
    }
}
