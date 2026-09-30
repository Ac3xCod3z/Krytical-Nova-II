using System;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;

#if UNITY_SERVER
using Unity.Services.Multiplayer.Server;
#endif

namespace KryticalNova.Networking
{
    /// <summary>
    /// Manages network lifecycle, connection modes (Local Host/Client, Mediated Cloud Sessions, and Headless Dedicated Server),
    /// and service authentication for Krytical Nova II.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NetworkConnectionManager : MonoBehaviour
    {
        public static NetworkConnectionManager Instance { get; private set; }

        [Header("Local Network Settings")]
        [SerializeField] private string defaultListenAddress = "127.0.0.1";
        [SerializeField] private ushort defaultPort = 7777;
        [SerializeField] private int defaultMaxPlayers = 100;

        [Header("State (Read-Only)")]
        [SerializeField] private string activeSessionId;
        [SerializeField] private string activeSessionCode;
        [SerializeField] private bool isServicesInitialized;

        private ISession activeSession;

        public event Action<string> StatusChanged;
        public event Action<string> ErrorReported;

        public string ActiveSessionCode => activeSessionCode;
        public string ActiveSessionId => activeSessionId;
        public bool IsServicesInitialized => isServicesInitialized;
        public bool IsConnected => NetworkManager.Singleton != null && (NetworkManager.Singleton.IsClient || NetworkManager.Singleton.IsServer);

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
#if UNITY_SERVER
            StartDedicatedServerAsync();
#endif
        }

        /// <summary>Initializes cloud gaming services and anonymous authentication if not already active.</summary>
        public async Task<bool> EnsureServicesInitializedAsync()
        {
            if (isServicesInitialized && AuthenticationService.Instance.IsSignedIn)
                return true;

            try
            {
                if (UnityServices.State != ServicesInitializationState.Initialized)
                {
                    StatusChanged?.Invoke("Initializing Unity Services...");
                    await UnityServices.InitializeAsync();
                }

                if (!AuthenticationService.Instance.IsSignedIn)
                {
                    StatusChanged?.Invoke("Signing in player anonymously...");
                    await AuthenticationService.Instance.SignInAnonymouslyAsync();
                }

                isServicesInitialized = true;
                StatusChanged?.Invoke($"Player signed in. Player ID: {AuthenticationService.Instance.PlayerId}");
                return true;
            }
            catch (Exception ex)
            {
                string message = $"Services initialization error: {ex.Message}";
                Debug.LogError(message, this);
                ErrorReported?.Invoke(message);
                return false;
            }
        }

        /// <summary>Starts a local host directly without requiring cloud connectivity (ideal for instant development testing).</summary>
        public bool StartLocalHost(ushort port = 0)
        {
            if (NetworkManager.Singleton == null)
            {
                ReportError("Cannot start local host: NetworkManager.Singleton is missing.");
                return false;
            }

            ConfigureTransport(defaultListenAddress, port > 0 ? port : defaultPort);
            bool success = NetworkManager.Singleton.StartHost();
            if (success)
            {
                StatusChanged?.Invoke($"Local Host started on {defaultListenAddress}:{port}");
            }
            else
            {
                ReportError("NetworkManager failed to start Local Host.");
            }
            return success;
        }

        /// <summary>Connects as a client to a local or direct IP/port server.</summary>
        public bool StartLocalClient(string ip = "", ushort port = 0)
        {
            if (NetworkManager.Singleton == null)
            {
                ReportError("Cannot start local client: NetworkManager.Singleton is missing.");
                return false;
            }

            string address = string.IsNullOrWhiteSpace(ip) ? defaultListenAddress : ip;
            ushort targetPort = port > 0 ? port : defaultPort;

            ConfigureTransport(address, targetPort);
            bool success = NetworkManager.Singleton.StartClient();
            if (success)
            {
                StatusChanged?.Invoke($"Connecting to {address}:{targetPort}...");
            }
            else
            {
                ReportError($"NetworkManager failed to connect to {address}:{targetPort}.");
            }
            return success;
        }

        /// <summary>Creates a mediated cloud session with brokered connectivity, starting the host upon creation.</summary>
        public async Task<bool> StartMediatedHostAsync(int maxPlayers = 0)
        {
            if (!await EnsureServicesInitializedAsync())
                return false;

            try
            {
                StatusChanged?.Invoke("Creating mediated multiplayer session...");
                int capacity = maxPlayers > 0 ? maxPlayers : defaultMaxPlayers;

                var options = new SessionOptions
                {
                    MaxPlayers = capacity,
                    Name = $"KryticalNova_Zone_{Guid.NewGuid().ToString().Substring(0, 8)}"
                }.WithRelayNetwork();

                var hostSession = await MultiplayerService.Instance.CreateSessionAsync(options);
                activeSession = hostSession;
                activeSessionId = hostSession.Id;
                activeSessionCode = hostSession.Code;

                StatusChanged?.Invoke($"Host session online! Join Code: {activeSessionCode}");
                return true;
            }
            catch (Exception ex)
            {
                ReportError($"Failed to create mediated session: {ex.Message}");
                return false;
            }
        }

        /// <summary>Joins an active mediated cloud session using a session join code.</summary>
        public async Task<bool> JoinMediatedSessionByCodeAsync(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                ReportError("Join code cannot be empty.");
                return false;
            }

            if (!await EnsureServicesInitializedAsync())
                return false;

            try
            {
                StatusChanged?.Invoke($"Joining session with code: {code}...");
                var joinOptions = new JoinSessionOptions();
                var session = await MultiplayerService.Instance.JoinSessionByCodeAsync(code.Trim(), joinOptions);

                activeSession = session;
                activeSessionId = session.Id;
                activeSessionCode = session.Code;

                StatusChanged?.Invoke($"Successfully joined session: {activeSessionCode}");
                return true;
            }
            catch (Exception ex)
            {
                ReportError($"Failed to join session: {ex.Message}");
                return false;
            }
        }

        /// <summary>Disconnects any active network session and shuts down NetworkManager.</summary>
        public async Task DisconnectAsync()
        {
            StatusChanged?.Invoke("Disconnecting...");

            if (activeSession != null)
            {
                try
                {
                    await activeSession.LeaveAsync();
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"Exception leaving session: {ex.Message}");
                }
                activeSession = null;
                activeSessionId = string.Empty;
                activeSessionCode = string.Empty;
            }

            if (NetworkManager.Singleton != null && (NetworkManager.Singleton.IsClient || NetworkManager.Singleton.IsServer))
            {
                NetworkManager.Singleton.Shutdown();
            }

            StatusChanged?.Invoke("Disconnected.");
        }

#if UNITY_SERVER
        private async void StartDedicatedServerAsync()
        {
            try
            {
                StatusChanged?.Invoke("Starting headless dedicated server...");
                if (!await EnsureServicesInitializedAsync())
                {
                    Debug.LogError("Dedicated server failed to initialize services. Falling back to local server listen.");
                    StartDirectServerFallback();
                    return;
                }

                var options = new SessionOptions
                {
                    MaxPlayers = defaultMaxPlayers,
                    Name = $"ZoneServer_{Environment.MachineName}"
                }.WithDirectNetwork(defaultListenAddress, defaultPort);

                var serverSession = await MultiplayerServerService.Instance.CreateSessionAsync(options);
                activeSession = serverSession;
                activeSessionId = serverSession.Id;
                StatusChanged?.Invoke($"Dedicated server session created: {serverSession.Id}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Dedicated server session creation failed: {ex.Message}. Falling back to direct listen.");
                StartDirectServerFallback();
            }
        }

        private void StartDirectServerFallback()
        {
            if (NetworkManager.Singleton != null)
            {
                ConfigureTransport(defaultListenAddress, defaultPort);
                NetworkManager.Singleton.StartServer();
            }
        }
#endif

        private void ConfigureTransport(string address, ushort port)
        {
            if (NetworkManager.Singleton != null)
            {
                var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
                if (transport != null)
                {
                    transport.SetConnectionData(address, port, "0.0.0.0");
                }
            }
        }

        private void ReportError(string error)
        {
            Debug.LogError(error, this);
            ErrorReported?.Invoke(error);
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }
    }
}
