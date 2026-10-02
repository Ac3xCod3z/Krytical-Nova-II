using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;

namespace KryticalNova.Networking
{
    /// <summary>
    /// The scene's Player is the offline avatar. When this peer gets its own networked player
    /// (Host or Client), the offline avatar is hidden so there is exactly one controllable character,
    /// and the host's networked player takes over its position. When the session ends the offline
    /// avatar returns where the networked player last stood. A dedicated Server has no local player,
    /// so the offline avatar is left alone there.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OfflinePlayerHandoff : MonoBehaviour
    {
        [Tooltip("The Player placed in the scene for offline play.")]
        [SerializeField] private GameObject offlinePlayer;

        private NetworkObject handedOffTo;
        // Tracked separately: once the networked player is destroyed, handedOffTo compares equal to null.
        private bool isHandedOff;
        private Vector3 lastNetworkedPosition;
        private Quaternion lastNetworkedRotation;
        private bool hasNetworkedPose;

        private static OfflinePlayerHandoff instance;
        private NetworkManager subscribedManager;

        /// <summary>
        /// Called by the local owner's networked player right before it enables its PlayerInput,
        /// so the keyboard/mouse are free to pair with it.
        /// </summary>
        public static void ReleaseOfflineAvatar()
        {
            if (instance != null) instance.Hide();
        }

        private void Awake()
        {
            instance = this;
        }

        private void Start()
        {
            subscribedManager = NetworkManager.Singleton != null ? NetworkManager.Singleton : GetComponent<NetworkManager>();
            if (subscribedManager == null) return;
            subscribedManager.OnServerStarted += HideEarly;
            subscribedManager.OnClientStarted += HideEarly;
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
            if (subscribedManager == null) return;
            subscribedManager.OnServerStarted -= HideEarly;
            subscribedManager.OnClientStarted -= HideEarly;
        }

        // Hide the offline avatar as soon as this peer will own a networked player (Host/Client), before
        // that player's PlayerInput enables and finds the keyboard/mouse already taken.
        private void HideEarly()
        {
            var nm = subscribedManager != null ? subscribedManager : NetworkManager.Singleton;
            if (nm != null && nm.IsClient) Hide();
        }

        private void Hide()
        {
            if (offlinePlayer == null || !offlinePlayer.activeSelf) return;

            isHandedOff = true;
            handedOffTo = null;
            offlinePlayer.SetActive(false);
        }

        private void Update()
        {
            if (offlinePlayer == null) return;

            var nm = NetworkManager.Singleton;
            NetworkObject localPlayer = nm != null && nm.IsClient && nm.LocalClient != null ? nm.LocalClient.PlayerObject : null;

            if (localPlayer != null)
            {
                if (!isHandedOff || handedOffTo != localPlayer)
                    HandOff(nm, localPlayer);

                lastNetworkedPosition = localPlayer.transform.position;
                lastNetworkedRotation = localPlayer.transform.rotation;
                hasNetworkedPose = true;
                return;
            }

            // Still connecting as a client: keep the offline avatar until the networked one exists.
            if (nm != null && nm.IsClient && nm.IsListening) return;

            if (isHandedOff)
                Restore();
        }

        private void HandOff(NetworkManager nm, NetworkObject localPlayer)
        {
            handedOffTo = localPlayer;
            isHandedOff = true;

            // Only the server may move a player; on a pure client the server chose the spawn point.
            // (The offline avatar's transform is still valid while it is inactive.)
            if (nm.IsServer)
                MovePlayer(localPlayer.transform, offlinePlayer.transform.position, offlinePlayer.transform.rotation);

            offlinePlayer.SetActive(false);

            // Safety net: if the networked PlayerInput enabled while the offline avatar still held the
            // keyboard/mouse, it has no devices. Re-enabling pairs it now that they are free.
            if (localPlayer.TryGetComponent<PlayerInput>(out var input) && input.enabled && input.devices.Count == 0)
            {
                input.enabled = false;
                input.enabled = true;
            }
        }

        private void Restore()
        {
            handedOffTo = null;
            isHandedOff = false;
            if (hasNetworkedPose)
                MovePlayer(offlinePlayer.transform, lastNetworkedPosition, lastNetworkedRotation);
            hasNetworkedPose = false;
            offlinePlayer.SetActive(true);
        }

        private static void MovePlayer(Transform player, Vector3 position, Quaternion rotation)
        {
            if (player.TryGetComponent<NavMeshAgent>(out var agent) && agent.isActiveAndEnabled && agent.isOnNavMesh)
                agent.Warp(position);
            player.SetPositionAndRotation(position, rotation);
        }
    }
}
