using System;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;

// Server-only multiplayer types (assembly Unity.Services.Multiplayer.Server) live in the
// Unity.Services.Multiplayer namespace imported above; there is no ".Server" namespace.

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

        [Header("Editor Play-Testing")]
        [Tooltip("In the Editor, the main Editor starts a local host on entering the world and Multiplayer Play Mode clones join it.")]
        [SerializeField] private bool autoStartInEditor = true;
        private const float AutoJoinRetrySeconds = 2f;

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
        /// <summary>An online session is being created or joined; the NetworkManager has not started yet.</summary>
        public bool IsStartingSession { get; private set; }
        /// <summary>
        /// When this player last asked to leave the session (unscaled time). A session that ends right after was
        /// their own doing; one that ends at any other moment ended under them.
        /// </summary>
        public float LastDisconnectRequest { get; private set; } = float.NegativeInfinity;

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
#else
            if (autoStartInEditor && Application.isEditor && !Application.isBatchMode)
                StartCoroutine(AutoStartEditorSession());
#endif
        }

        /// <summary>
        /// Play-testing in the Editor: the main Editor hosts as soon as it reaches the world, and every Multiplayer
        /// Play Mode clone joins it. A clone that gets there first keeps trying until the host is up.
        /// </summary>
        private System.Collections.IEnumerator AutoStartEditorSession()
        {
            // One frame, so every Start in the scene (the runtime NavMesh among them) has run before a player spawns.
            yield return null;

            bool isClone = EditorCloneProfile() != null;
            var retry = new WaitForSecondsRealtime(AutoJoinRetrySeconds);
            while (true)
            {
                var nm = NetworkManager.Singleton;
                if (nm == null) yield break;

                // Already in a session, by this or by hand from the network panel.
                if (nm.IsServer || nm.IsConnectedClient) yield break;

                if (!nm.IsListening && !nm.ShutdownInProgress && !IsStartingSession)
                {
                    if (!isClone)
                    {
                        StartLocalHost();
                        yield break;
                    }
                    StartLocalClient();
                }
                yield return retry;
            }
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
                    var options = new InitializationOptions();
                    string profile = LocalProfile();
                    if (profile != null) options.SetProfile(profile);
                    await UnityServices.InitializeAsync(options);
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

            ushort listenPort = ResolvePort(port);
            ConfigureTransport(defaultListenAddress, listenPort);
            OfflinePlayerHandoff.ReleaseOfflineAvatar();
            bool success = NetworkManager.Singleton.StartHost();
            if (success)
            {
                StatusChanged?.Invoke($"Local Host started on {defaultListenAddress}:{listenPort}");
            }
            else
            {
                ReportError("NetworkManager failed to start Local Host.");
            }
            return success;
        }

        /// <summary>Starts a server with no local player (the in-Editor stand-in for a dedicated server).</summary>
        public bool StartLocalServer(ushort port = 0)
        {
            if (NetworkManager.Singleton == null)
            {
                ReportError("Cannot start local server: NetworkManager.Singleton is missing.");
                return false;
            }

            ushort listenPort = ResolvePort(port);
            ConfigureTransport(defaultListenAddress, listenPort);
            // The offline avatar is a scene NetworkObject: it must be hidden before the server sweeps the scene.
            OfflinePlayerHandoff.ReleaseOfflineAvatar();
            bool success = NetworkManager.Singleton.StartServer();
            if (success)
            {
                StatusChanged?.Invoke($"Local Server started on {defaultListenAddress}:{listenPort}");
            }
            else
            {
                ReportError("NetworkManager failed to start Local Server.");
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
            ushort targetPort = ResolvePort(port);

            ConfigureTransport(address, targetPort);
            OfflinePlayerHandoff.ReleaseOfflineAvatar();
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

                // The session starts the NetworkManager itself; the offline avatar has to be out of the way first.
                IsStartingSession = true;
                OfflinePlayerHandoff.ReleaseOfflineAvatar();
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
            finally
            {
                IsStartingSession = false;
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
                IsStartingSession = true;
                OfflinePlayerHandoff.ReleaseOfflineAvatar();
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
            finally
            {
                IsStartingSession = false;
            }
        }

        /// <summary>Disconnects any active network session and shuts down NetworkManager.</summary>
        public async Task DisconnectAsync()
        {
            LastDisconnectRequest = Time.unscaledTime;
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
                }.WithDirectNetwork(defaultListenAddress, defaultListenAddress, defaultPort); // (listenIp, publishIp, port)

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
                OfflinePlayerHandoff.ReleaseOfflineAvatar();
                NetworkManager.Singleton.StartServer();
            }
        }
#endif

        private const string ProfileArgument = "-profile";
        private const int MaxProfileLength = 30;

        /// <summary>
        /// The name that keeps this running copy of the game apart from others on the same machine: its sign-in,
        /// its saved characters and its interface settings. A build started with "-profile &lt;name&gt;" uses that
        /// name; a Multiplayer Play Mode clone uses its own (<see cref="EditorCloneProfile"/>). Null otherwise,
        /// which is the machine's default save.
        /// </summary>
        public static string LocalProfile()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (!string.Equals(args[i], ProfileArgument, StringComparison.OrdinalIgnoreCase)) continue;
                string named = CleanProfile(args[i + 1]);
                if (named != null) return named;
            }
            return EditorCloneProfile();
        }

        /// <summary>
        /// Editor instances cloned for multiplayer testing (Multiplayer Play Mode virtual players) share this
        /// machine's sign-in cache. Each gets its own authentication profile, so they are different players.
        /// Also used to keep each clone's saved characters apart. Null in the main Editor and in builds.
        /// </summary>
        public static string EditorCloneProfile()
        {
#if UNITY_EDITOR
            const string cloneFolder = "/Library/VP/";
            string path = Application.dataPath.Replace('\\', '/');
            int at = path.IndexOf(cloneFolder, StringComparison.OrdinalIgnoreCase);
            if (at < 0) return null;

            string id = path.Substring(at + cloneFolder.Length);
            int end = id.IndexOf('/');
            if (end >= 0) id = id.Substring(0, end);

            return CleanProfile(id);
#else
            return null;
#endif
        }

        // Authentication profiles accept letters, digits, '-' and '_', up to 30 characters.
        private static string CleanProfile(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return null;
            var profile = new System.Text.StringBuilder(MaxProfileLength);
            foreach (char c in raw)
            {
                if (profile.Length == MaxProfileLength) break;
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_') profile.Append(c);
            }
            return profile.Length > 0 ? profile.ToString() : null;
        }

        /// <summary>
        /// The port a local session really uses: the one asked for, else the default. An Editor test session
        /// (<see cref="EditorTestSession"/>) overrides both, so nothing started while it is on can reach the default port.
        /// </summary>
        private ushort ResolvePort(ushort requested)
        {
            if (EditorTestSession.TryGetPort(out ushort testPort)) return testPort;
            return requested > 0 ? requested : defaultPort;
        }

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
