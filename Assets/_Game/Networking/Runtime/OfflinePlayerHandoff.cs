using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace KryticalNova.Networking
{
    /// <summary>
    /// The scene's Player is the offline avatar. It is hidden for as long as any network session runs on this
    /// peer (Host, Client or Server), so there is exactly one controllable character and the server never
    /// network-spawns it as a scene object. The host's networked player takes over its position, and when the
    /// session ends the offline avatar returns where the networked player last stood. A headless dedicated
    /// server never shows it at all.
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

        private static bool IsHeadlessServer
        {
            get
            {
#if UNITY_SERVER
                return true;
#else
                return Application.isBatchMode;
#endif
            }
        }

        /// <summary>
        /// Called before a session starts on this peer and by the local owner's networked player right before it
        /// enables its PlayerInput, so the keyboard/mouse are free to pair with it.
        /// </summary>
        public static void ReleaseOfflineAvatar()
        {
            if (instance != null) instance.Hide();
        }

        private void Awake()
        {
            instance = this;
            // Nobody plays on a headless server: the avatar must be gone before the first session starts.
            if (IsHeadlessServer && offlinePlayer != null) offlinePlayer.SetActive(false);
        }

        private void Start()
        {
            subscribedManager = NetworkManager.Singleton != null ? NetworkManager.Singleton : GetComponent<NetworkManager>();
            if (subscribedManager == null) return;
            subscribedManager.OnServerStarted += Hide;
            subscribedManager.OnClientStarted += Hide;
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
            if (subscribedManager == null) return;
            subscribedManager.OnServerStarted -= Hide;
            subscribedManager.OnClientStarted -= Hide;
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
            if (offlinePlayer == null || IsHeadlessServer) return;

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

            // A session is running or about to (still connecting, or a server with no local player): stay hidden.
            if (nm != null && nm.IsListening) return;
            var connection = NetworkConnectionManager.Instance;
            if (connection != null && connection.IsStartingSession) return;

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
                NetworkPlacement.Place(localPlayer.transform, offlinePlayer.transform.position, offlinePlayer.transform.rotation);

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
                NetworkPlacement.Place(offlinePlayer.transform, lastNetworkedPosition, lastNetworkedRotation);
            hasNetworkedPose = false;
            offlinePlayer.SetActive(true);
        }
    }
}
