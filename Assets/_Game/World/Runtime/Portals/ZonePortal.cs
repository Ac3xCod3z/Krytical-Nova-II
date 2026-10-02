using System.Collections.Generic;
using KryticalNova.Core;
using KryticalNova.Player;
using Unity.Netcode;
using UnityEngine;

namespace KryticalNova.World.Portals
{
    /// <summary>
    /// A zone exit. The server (or the offline game) detects a player entering the trigger, re-checks the
    /// distance, applies a per-player cooldown and asks <see cref="TransferHandler"/> to move the player.
    /// Only the player who entered is told the result.
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
        [Tooltip("Zone this portal leads to. Empty until the next map exists. TODO(zone2)")]
        [SerializeField] private string destinationZoneId = "";
        [Tooltip("Named arrival point in the destination zone.")]
        [SerializeField] private string arrivalPointId = "";
        [Tooltip("Horizontal radius (m) a player must be within for the portal to accept them.")]
        [SerializeField, Min(0.5f)] private float activationRadius = 3f;
        [SerializeField, Min(0f)] private float cooldownSeconds = 3f;
        [SerializeField, TextArea] private string sealedMessage = "The way beyond is sealed… for now.";

        private readonly Dictionary<ulong, float> nextUseTime = new Dictionary<ulong, float>();

        private static readonly List<ZonePortal> ActivePortals = new List<ZonePortal>(4);
        /// <summary>Enabled portals, for map/compass markers.</summary>
        public static IReadOnlyList<ZonePortal> Active => ActivePortals;

        private void OnEnable() => ActivePortals.Add(this);
        private void OnDisable() => ActivePortals.Remove(this);

        public string PortalId => portalId;
        public string DestinationZoneId => destinationZoneId;
        public string ArrivalPointId => arrivalPointId;
        public string SealedMessage => sealedMessage;

        private void Reset()
        {
            GetComponent<Collider>().isTrigger = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            NetworkManager manager = NetworkManager.Singleton;
            bool networked = manager != null && manager.IsListening;
            if (networked && !manager.IsServer) return;

            var player = other.GetComponentInParent<PointAndClickPlayer>();
            if (player == null) return;

            Vector3 offset = player.transform.position - transform.position;
            offset.y = 0f;
            if (offset.magnitude > activationRadius + RadiusTolerance) return;

            ulong clientId = networked ? player.OwnerClientId : OfflineClientId;
            float now = Time.time;
            if (nextUseTime.TryGetValue(clientId, out float allowedAt) && now < allowedAt) return;
            nextUseTime[clientId] = now + cooldownSeconds;

            ZoneTransferResult result = TransferHandler != null
                ? TransferHandler.TryTransfer(this, player.gameObject)
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
