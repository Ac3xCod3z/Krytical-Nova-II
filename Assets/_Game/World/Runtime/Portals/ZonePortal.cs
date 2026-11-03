using System.Collections.Generic;
using KryticalNova.Core;
using Unity.Netcode;
using UnityEngine;

namespace KryticalNova.World.Portals
{
    /// <summary>
    /// A zone exit. The server (or the offline game) detects a player entering the trigger, re-checks the
    /// distance, applies a per-player cooldown and asks <see cref="TransferHandler"/> to move the player.
    /// Only the player who entered is told the result.
    /// On a server of the entity netcode (M7.3, 2 Nov 2026) NGO is not listening and its trigger would answer on the server's own screen
    /// with one cooldown for everyone: there the portal looks at the server bodies four times a second instead (<see cref="ServerLook"/>),
    /// fires when one comes inside the same radius, keeps the cooldown per <see cref="PlayerKey"/> and tells that player through
    /// <see cref="OwnerMessages"/>; the trigger then stays quiet on every machine of that session.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public sealed class ZonePortal : NetworkBehaviour
    {
        private const ulong OfflineClientId = ulong.MaxValue;
        private const float RadiusTolerance = 0.75f;

        /// <summary>Server-side transfer strategy shared by every portal. Swap when real zone travel exists.</summary>
        public static IZoneTransferHandler TransferHandler { get; set; } = new SealedPortalHandler();

        [SerializeField] private string portalId = "zone01-northeast";
        [Tooltip("Name shown on the map. Empty = this object's name.")]
        [SerializeField] private string displayName = "";
        [Tooltip("Zone this portal leads to. Empty until the next map exists. TODO(zone2)")]
        [SerializeField] private string destinationZoneId = "";
        [Tooltip("Named arrival point in the destination zone.")]
        [SerializeField] private string arrivalPointId = "";
        [Tooltip("Horizontal radius (m) a player must be within for the portal to accept them.")]
        [SerializeField, Min(0.5f)] private float activationRadius = 3f;
        [SerializeField, Min(0f)] private float cooldownSeconds = 3f;
        [SerializeField, TextArea] private string sealedMessage = "The way beyond is sealed… for now.";

        private const float ServerLookInterval = 0.25f;

        private readonly Dictionary<ulong, float> nextUseTime = new Dictionary<ulong, float>();
        private readonly Dictionary<PlayerKey, float> nextUseByKey = new Dictionary<PlayerKey, float>();
        private readonly HashSet<PlayerKey> inside = new HashSet<PlayerKey>();
        private readonly HashSet<PlayerKey> seen = new HashSet<PlayerKey>();
        private readonly List<PlayerKey> left = new List<PlayerKey>();
        private float nextServerLook;

        private static readonly List<ZonePortal> ActivePortals = new List<ZonePortal>(4);
        /// <summary>Enabled portals, for map/compass markers.</summary>
        public static IReadOnlyList<ZonePortal> Active => ActivePortals;

        private void OnEnable() => ActivePortals.Add(this);
        private void OnDisable() => ActivePortals.Remove(this);

        public string PortalId => portalId;
        public string DisplayName => string.IsNullOrEmpty(displayName) ? name : displayName;
        public string DestinationZoneId => destinationZoneId;
        public string ArrivalPointId => arrivalPointId;
        public string SealedMessage => sealedMessage;

        private void Reset()
        {
            GetComponent<Collider>().isTrigger = true;
        }

        private void Update()
        {
            if (!EntitySessionServer() || Time.time < nextServerLook) return;
            nextServerLook = Time.time + ServerLookInterval;
            ServerLook(BodyRoleTag.ServerBodies, Time.time);
        }

        /// <summary>An entity-netcode session (NGO not listening) and this process runs its server.</summary>
        private static bool EntitySessionServer()
        {
            NetworkManager manager = NetworkManager.Singleton;
            return (manager == null || !manager.IsListening) && NetRole.Process == NetRoleKind.ServerBody;
        }

        /// <summary>
        /// One look over the server bodies (entity netcode): a body that came inside the radius since the last look, and is not cooling down,
        /// is transferred and its player told. Bodies with no key are not connected players and are left alone (as NGO's trigger).
        /// </summary>
        public void ServerLook(IReadOnlyList<BodyRoleTag> bodies, float now)
        {
            seen.Clear();
            for (int i = 0; i < bodies.Count; i++)
            {
                BodyRoleTag body = bodies[i];
                if (body == null || body.Kind != NetRoleKind.ServerBody) continue;
                PlayerKey key = body.Key;
                if (key == PlayerKey.None) continue;
                Vector3 offset = body.transform.position - transform.position;
                offset.y = 0f;
                if (offset.magnitude > activationRadius + RadiusTolerance) continue;
                seen.Add(key);
                if (!inside.Add(key)) continue;                                    // still inside since an earlier look: no new entry
                if (nextUseByKey.TryGetValue(key, out float allowedAt) && now < allowedAt) continue;
                nextUseByKey[key] = now + cooldownSeconds;
                ZoneTransferResult result = TransferHandler != null
                    ? TransferHandler.TryTransfer(this, body.gameObject)
                    : new ZoneTransferResult(false, sealedMessage);
                if (!string.IsNullOrEmpty(result.Message)) OwnerMessages.TryTell(body.transform, result.Message);
            }
            left.Clear();
            foreach (PlayerKey key in inside) if (!seen.Contains(key)) left.Add(key);
            foreach (PlayerKey key in left) inside.Remove(key);
        }

        private void OnTriggerEnter(Collider other)
        {
            NetworkManager manager = NetworkManager.Singleton;
            bool networked = manager != null && manager.IsListening;
            if (networked && !manager.IsServer) return;
            if (!networked && NetRole.Process != NetRoleKind.Offline) return;   // entity netcode: the server's ServerLook answers

            IPlayerBody player = other.GetComponentInParent<IPlayerBody>();
            if (player == null) return;

            Vector3 offset = player.Transform.position - transform.position;
            offset.y = 0f;
            if (offset.magnitude > activationRadius + RadiusTolerance) return;

            ulong clientId = OfflineClientId;
            if (networked && !PlayerKeys.TryGetConnection(player.Owner, out clientId)) return; // no live key: not a connected player
            float now = Time.time;
            if (nextUseTime.TryGetValue(clientId, out float allowedAt) && now < allowedAt) return;
            nextUseTime[clientId] = now + cooldownSeconds;

            ZoneTransferResult result = TransferHandler != null
                ? TransferHandler.TryTransfer(this, player.Transform.gameObject)
                : new ZoneTransferResult(false, sealedMessage);
            if (string.IsNullOrEmpty(result.Message)) return;

            if (networked && IsSpawned)
                PortalMessageRpc(result.Message, RpcTarget.Single(clientId, RpcTargetUse.Temp));
            else
                ClientPresentationRegistry.Messages?.ShowMessage(result.Message);
        }

        [Rpc(SendTo.SpecifiedInParams)]
        private void PortalMessageRpc(string message, RpcParams rpcParams)
        {
            ClientPresentationRegistry.Messages?.ShowMessage(message);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.3f, 0.9f, 1f, 0.6f);
            Gizmos.DrawWireSphere(transform.position, activationRadius);
        }
    }
}
