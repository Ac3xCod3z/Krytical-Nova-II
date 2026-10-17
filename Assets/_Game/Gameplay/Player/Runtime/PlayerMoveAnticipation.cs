using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace KryticalNova.Player
{
    /// <summary>
    /// A player's own character moves the moment they click (online phase 3, step 1: client anticipation). The server
    /// still decides where everyone is and NetworkTransform still carries its positions; on a click the player's own
    /// game also walks the route the server will walk (<see cref="PointAndClickPlayer"/> plans it with the same NavMesh
    /// and speed) and shows that walk instead of the server's positions, which arrive a round trip later. Its agent
    /// walks without moving the body (NetworkTransform writes the body every frame, and an agent that sees its body
    /// moved under it loses its speed); after NetworkTransform has written the server's position, the body is put where
    /// the walk is. When the walk has ended and the server's body has come to rest, or the two are further apart than a
    /// round trip explains (a refusal, a vault the server took, a teleport), the body is eased onto the server's
    /// position and follows it again. The server never takes a position from a player's game.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-200)] // before the camera follows the body in LateUpdate
    public sealed class PlayerMoveAnticipation : NetworkBehaviour
    {
        [Tooltip("Metres. Apart further than this (plus what a round trip at this speed explains), the server's word stands.")]
        [SerializeField, Min(0f)] private float snapDistance = 3f;
        [Tooltip("Seconds to ease from this game's walk onto the server's position.")]
        [SerializeField, Min(0.01f)] private float easeSeconds = 0.2f;
        [Tooltip("Seconds the server's body must stand still, after this game's walk ended, before it is taken as arrived.")]
        [SerializeField, Min(0f)] private float restSeconds = 0.15f;
        [Tooltip("Seconds at most this game keeps leading after its walk ended, whatever the server is doing.")]
        [SerializeField, Min(0f)] private float settleSeconds = 1.5f;
        [Tooltip("Seconds the server's positions are shown behind (NetworkTransform's interpolation), added to the round trip.")]
        [SerializeField, Min(0f)] private float interpolationSeconds = 0.15f;

        private NavMeshAgent agent;
        private Quaternion leadRotation;
        private float walkEndedAt = -1f;
        private Vector3 lastServerShown;
        private float serverStillSince;
        private Vector3 easeFromPosition;
        private Quaternion easeFromRotation;
        private float easeClock = -1f;

        /// <summary>True while this player's own game leads its character (since a click, until it has settled).</summary>
        public bool Leading { get; private set; }

        /// <summary>The way this game's walk faces (its own turning: NetworkTransform overwrites the body's every frame).</summary>
        public Quaternion LeadRotation
        {
            get => leadRotation;
            set => leadRotation = value;
        }

        private void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
        }

        public override void OnNetworkDespawn()
        {
            if (Leading && agent != null) agent.updatePosition = true;
            Leading = false;
            easeClock = -1f;
            base.OnNetworkDespawn();
        }

        /// <summary>
        /// The player's own game has given its agent a route it planned itself (a click it also sent to the server):
        /// from now the agent walks it without moving the body, and the body is shown where the agent is.
        /// </summary>
        public void BeginLeading()
        {
            if (!IsSpawned || !IsOwner || IsServer || agent == null || !agent.isActiveAndEnabled) return;
            if (!Leading)
            {
                leadRotation = transform.rotation;
                lastServerShown = transform.position;
                serverStillSince = Time.time;
            }
            agent.updatePosition = false;
            Leading = true;
            walkEndedAt = -1f;
            easeClock = -1f;
        }

        /// <summary>
        /// Stops leading at once (the move was refused, a jump or vault took over, the body died, the walk has settled):
        /// the body is eased onto the server's position and follows it again. The agent that walked the route is switched
        /// off: left on, it would hold the body to the NavMesh while the server carries it through the air.
        /// </summary>
        public void StopLeading()
        {
            if (!Leading) return;
            Leading = false;
            easeFromPosition = transform.position;
            easeFromRotation = transform.rotation;
            easeClock = 0f;
            if (agent == null) return;
            if (agent.isActiveAndEnabled && agent.isOnNavMesh) agent.ResetPath();
            agent.updatePosition = true;
            if (IsSpawned && IsOwner && !IsServer) agent.enabled = false;
        }

        // NetworkTransform has just written the server's position (it runs before LateUpdate): the body is put where it
        // belongs before anything draws it.
        private void LateUpdate()
        {
            if (!IsSpawned || IsServer || !IsOwner) return;
            if (Leading) Lead();
            else if (easeClock >= 0f) EaseOntoServer();
        }

        private void Lead()
        {
            if (agent == null || !agent.isActiveAndEnabled) { StopLeading(); EaseOntoServer(); return; }
            Vector3 serverShown = transform.position;
            Vector3 mine = agent.nextPosition;

            // How long the server's body has stood where it stands.
            if ((serverShown - lastServerShown).sqrMagnitude > 0.0004f)
            {
                lastServerShown = serverShown;
                serverStillSince = Time.time;
            }

            bool walking = agent.hasPath && !agent.isStopped && agent.velocity.sqrMagnitude > 0.01f;
            if (walking) walkEndedAt = -1f;
            else if (walkEndedAt < 0f) walkEndedAt = Time.time;

            // Further apart than the server's delay explains: the server's route is another one.
            float behind = agent.speed * ((float)RoundTripSeconds() + interpolationSeconds);
            bool apart = (mine - serverShown).magnitude > snapDistance + behind;
            // Both have stopped: the server's word is taken (eased) and the walk is over.
            bool settled = walkEndedAt >= 0f
                && (Time.time - serverStillSince >= restSeconds || Time.time - walkEndedAt >= settleSeconds);

            // Eased from where this body was shown, onto where the server has it.
            if (apart || settled)
            {
                Vector3 server = serverShown;
                Quaternion serverFacing = transform.rotation;
                transform.SetPositionAndRotation(mine, leadRotation);
                StopLeading();
                transform.SetPositionAndRotation(server, serverFacing);
                EaseOntoServer();
                return;
            }
            transform.SetPositionAndRotation(mine, leadRotation);
        }

        // Called after NetworkTransform has written the server's position this frame.
        private void EaseOntoServer()
        {
            easeClock += Time.deltaTime;
            float t = Mathf.Clamp01(easeClock / easeSeconds);
            if (t >= 1f) { easeClock = -1f; return; } // NetworkTransform's position stands as written
            float smooth = t * t * (3f - 2f * t);
            transform.SetPositionAndRotation(Vector3.Lerp(easeFromPosition, transform.position, smooth),
                Quaternion.Slerp(easeFromRotation, transform.rotation, smooth));
        }

        private double RoundTripSeconds()
        {
            NetworkManager manager = NetworkManager;
            if (manager == null || manager.NetworkConfig == null || manager.NetworkConfig.NetworkTransport == null) return 0.1;
            return manager.NetworkConfig.NetworkTransport.GetCurrentRtt(NetworkManager.ServerClientId) / 1000.0;
        }
    }
}
