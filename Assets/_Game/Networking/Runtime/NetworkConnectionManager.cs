using System;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;

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
            PrepareConnections();
#if UNITY_SERVER
            StartDedicatedServer();
#else
            bool testing = Application.isEditor && (EditorCloneProfile() != null || EditorTestSession.JoinOnly);
            // Playing on this PC alone keeps the made-up account; joining a server sets the proven one (JoinChosenServer).
            KryticalNova.Core.LocalAccount.VerifiedId = null;
            if (JoinFlow.Target != null && !testing)
                StartCoroutine(JoinChosenServer(JoinFlow.Target));
            else if (autoStartInEditor && Application.isEditor && !Application.isBatchMode)
                StartCoroutine(AutoStartEditorSession());
#endif
        }

        private const float JoinTimeoutSeconds = 20f;

        /// <summary>
        /// Character select chose a server (<see cref="JoinFlow"/>): join it while the loading screen is up. In once this
        /// player's character stands in the world; a refusal (another build, a full server) or no answer goes back
        /// to character select with the reason.
        /// </summary>
        private System.Collections.IEnumerator JoinChosenServer(ServerListing server)
        {
            yield return null; // every Start in the scene has run, as for the Editor's own session
            NetworkManager nm = NetworkManager.Singleton;
            string reason = null;
            bool ended = false;
            Action<ulong> onEnded = id => { if (nm != null && id == nm.LocalClientId) ended = true; };
            if (nm != null) nm.OnClientDisconnectCallback += onEnded;
            bool started;
            string relayProblem = null;
            // Online phase 2: a server only lets in a player Unity has signed in; the game does it by itself, no screen.
            Task<bool> signIn = EnsureServicesInitializedAsync();
            while (!signIn.IsCompleted) yield return null;
            if (signIn.IsFaulted || !signIn.Result)
                relayProblem = "Could not sign in to Unity, which the server needs to know who you are. Check your internet connection and try again.";
            else // the server keeps this player's saves under the same id: the game's own copy follows it
                KryticalNova.Core.LocalAccount.VerifiedId = AccountIds.FromUnityPlayer(AuthenticationService.Instance.PlayerId);
            if (relayProblem != null) started = false;
            else if (server.ViaRelay)
            {
                // Through Unity Relay with the server's join code (online step 5): no address, no router.
                Task<string> joining = PrepareRelayJoin(server);
                while (!joining.IsCompleted) yield return null;
                relayProblem = joining.IsFaulted ? $"Could not reach {server.name} through Unity Relay." : joining.Result;
                started = relayProblem == null && StartRelayClient();
            }
            else
            {
                // The PC that runs the server joins it on itself: a router may not loop its own public address back.
                started = StartLocalClient(server.OnThisMachine ? "127.0.0.1" : server.address, (ushort)server.port);
            }
            float until = Time.realtimeSinceStartup + JoinTimeoutSeconds;
            while (started && !ended)
            {
                if (nm.IsConnectedClient && nm.LocalClient != null && nm.LocalClient.PlayerObject != null)
                {
                    nm.OnClientDisconnectCallback -= onEnded;
                    JoinFlow.Joined();
                    yield break;
                }
                if (!nm.IsListening && !nm.ShutdownInProgress) { ended = true; break; }
                if (Time.realtimeSinceStartup > until) break;
                yield return null;
            }
            if (nm != null)
            {
                nm.OnClientDisconnectCallback -= onEnded;
                reason = nm.DisconnectReason;
                if (nm.IsListening) nm.Shutdown();
                float wait = Time.realtimeSinceStartup + 3f;
                while (nm.ShutdownInProgress && Time.realtimeSinceStartup < wait) yield return null;
            }
            if (relayProblem != null) reason = relayProblem;
            if (string.IsNullOrWhiteSpace(reason))
                reason = started
                    ? $"{server.name} did not answer. It may be off, or not reachable from here."
                    : $"Could not start joining {server.name}.";
            Debug.Log($"[Network] Joining {server.name} ({(server.ViaRelay ? "Relay " + server.relay : server.address + ":" + server.port)}) failed: {reason}");
            JoinFlow.Fail(reason);
            // As when a player leaves the world: the session objects outlive scene changes and the next world brings
            // its own, so these go before character select loads.
            if (nm != null) Destroy(nm.gameObject);
            if (nm == null || gameObject != nm.gameObject) Destroy(gameObject);
        }

        /// <summary>Signs in to Unity and takes a place on the server's Relay; null when ready, else the reason to show.</summary>
        private async Task<string> PrepareRelayJoin(ServerListing server)
        {
            try
            {
                if (!await EnsureServicesInitializedAsync())
                    return $"Could not sign in to Unity to reach {server.name} through Relay. Check the internet connection.";
                Unity.Services.Relay.Models.JoinAllocation place = await Unity.Services.Relay.RelayService.Instance.JoinAllocationAsync(server.relay);
                NetworkManager nm = NetworkManager.Singleton;
                if (nm == null) return $"Could not start joining {server.name}.";
                PrepareConnections();
                var relayTransport = nm.GetComponent<UnityTransport>();
                ServerEncryption.Off(relayTransport); // Relay encrypts by itself
                relayTransport.SetRelayServerData(Unity.Services.Relay.Models.AllocationUtils.ToRelayServerData(place, "dtls"));
                return null;
            }
            catch (Unity.Services.Relay.RelayServiceException e)
            {
                Debug.Log($"[Network] Relay refused the join code {server.relay}: {e.Message}");
                // Also right after the server restarted on Relay: the published list keeps the old code for up to five minutes.
                return $"{server.name} is not on Unity Relay with that code (it may be off, or it just restarted). Press REFRESH and try again in a few minutes.";
            }
            catch (Exception e)
            {
                return $"Could not reach {server.name} through Unity Relay: {e.Message}";
            }
        }

        // The transport already holds the Relay place (PrepareRelayJoin).
        private bool StartRelayClient()
        {
            NetworkManager nm = NetworkManager.Singleton;
            if (nm == null) return false;
            OfflinePlayerHandoff.ReleaseOfflineAvatar();
            SceneObjectKeeper.KeepPlacedObjects(nm); // the map's own networked objects survive the join (scene syncing is off)
            bool success = nm.StartClient();
            if (success)
            {
                StatusChanged?.Invoke("Connecting through Unity Relay...");
                ReportWhenConnected("Connected through Unity Relay.");
            }
            else ReportError("NetworkManager failed to connect through Unity Relay.");
            return success;
        }

        /// <summary>Says so once this client is in (the "Connecting..." line would otherwise stay up); a failed try just drops it.</summary>
        private void ReportWhenConnected(string message)
        {
            NetworkManager nm = NetworkManager.Singleton;
            if (nm == null) return;
            void Done()
            {
                nm.OnClientConnectedCallback -= Connected;
                nm.OnClientDisconnectCallback -= Disconnected;
            }
            void Connected(ulong id)
            {
                if (id != nm.LocalClientId) return;
                Done();
                StatusChanged?.Invoke(message);
            }
            void Disconnected(ulong id)
            {
                if (id == nm.LocalClientId) Done();
            }
            nm.OnClientConnectedCallback += Connected;
            nm.OnClientDisconnectCallback += Disconnected;
        }

        /// <summary>
        /// Play-testing in the Editor: the main Editor hosts as soon as it reaches the world, and every Multiplayer
        /// Play Mode clone joins it. A clone that gets there first keeps trying until the host is up.
        /// </summary>
        private System.Collections.IEnumerator AutoStartEditorSession()
        {
            // One frame, so every Start in the scene (the runtime NavMesh among them) has run before a player spawns.
            yield return null;

            // A clone joins; so does the main Editor while a test session says a dedicated server is up.
            bool isClone = EditorCloneProfile() != null || EditorTestSession.JoinOnly;
            if (EditorTestSession.JoinOnly)
            {
                // A dedicated test server lets in only players Unity has signed in (online phase 2). A clone joining the
                // Editor's own game is taken as before, under its made-up account, so its test characters stay.
                Task<bool> signIn = EnsureServicesInitializedAsync();
                float giveUp = Time.realtimeSinceStartup + 15f;
                while (!signIn.IsCompleted && Time.realtimeSinceStartup < giveUp) yield return null;
                if (signIn.IsCompleted && !signIn.IsFaulted && signIn.Result)
                    KryticalNova.Core.LocalAccount.VerifiedId = AccountIds.FromUnityPlayer(AuthenticationService.Instance.PlayerId);
            }
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
                        // A server already running on this PC (the dedicated server on 7777, say) holds the port: hosting
                        // would only fail with four red errors. Say so once and let the player join it from F1.
                        ushort port = ResolvePort(0);
                        if (UdpPortInUse(port))
                        {
                            Debug.LogWarning($"[Network] Port {port} is in use on this PC (a server is running?), so Play does not host. " +
                                             $"To join that server: F1, IP 127.0.0.1, Port {port}, JOIN.");
                            yield break;
                        }
                        StartLocalHost();
                        yield break;
                    }
                    // A dedicated test server (the test session's join=1) encrypts; an Editor hosting its own test does not.
                    StartLocalClient("", 0, EditorTestSession.JoinOnly);
                }
                yield return retry;
            }
        }

        /// <summary>
        /// Signs this game in to Unity the way joining does, without a session manager: the menus' questions about
        /// characters (<see cref="RosterLink"/>) need the same proof. False when it could not.
        /// </summary>
        public static async Task<bool> SignInAsync()
        {
            if (Instance != null) return await Instance.EnsureServicesInitializedAsync();
            try
            {
                if (UnityServices.State != ServicesInitializationState.Initialized)
                {
                    var options = new InitializationOptions();
                    string profile = LocalProfile();
                    if (profile != null) options.SetProfile(profile);
                    await UnityServices.InitializeAsync(options);
                }
                if (!AuthenticationService.Instance.IsSignedIn) await AuthenticationService.Instance.SignInAnonymouslyAsync();
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Network] Unity sign-in failed: {ex.Message}");
                return false;
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
            PrepareConnections();
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
            PrepareConnections();
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
        /// <param name="encrypted">The server is a dedicated server (it encrypts direct connections); false for an
        /// Editor hosting its own test session.</param>
        public bool StartLocalClient(string ip = "", ushort port = 0, bool encrypted = true)
        {
            PrepareConnections();
            if (NetworkManager.Singleton == null)
            {
                ReportError("Cannot start local client: NetworkManager.Singleton is missing.");
                return false;
            }

            string address = string.IsNullOrWhiteSpace(ip) ? defaultListenAddress : ip;
            ushort targetPort = ResolvePort(port);

            ConfigureTransport(address, targetPort);
            var clientTransport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            if (clientTransport != null)
            {
                if (encrypted) ServerEncryption.UseOnClient(clientTransport);
                else ServerEncryption.Off(clientTransport);
            }
            OfflinePlayerHandoff.ReleaseOfflineAvatar();
            SceneObjectKeeper.KeepPlacedObjects(NetworkManager.Singleton); // the map's own networked objects survive the join (scene syncing is off)
            bool success = NetworkManager.Singleton.StartClient();
            if (success)
            {
                StatusChanged?.Invoke($"Connecting to {address}:{targetPort}...");
                ReportWhenConnected($"Connected to {address}:{targetPort}.");
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
                PrepareConnections();
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
                PrepareConnections();
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

        // ------------------------------------------------------------------ who may join (online step 4)

        // Players the session takes; the dedicated server's comes from its settings.
        private int maxPlayersNow;

        /// <summary>
        /// Every game and server asks and answers the same way: a joining game sends its build with its request
        /// (<see cref="JoinRules"/>), and the side that hosts approves it or says why not before the player is in.
        /// Both sides must have it on (Netcode compares the setting), so it is set here and nowhere else.
        /// </summary>
        private void PrepareConnections()
        {
            NetworkManager nm = NetworkManager.Singleton;
            if (nm == null) return;
            nm.NetworkConfig.ConnectionApproval = true;
            // The sign-in token (when signed in) proves who this player is; the old made-up account id lets the server carry
            // that account's characters over to the proven one, once.
            string token = UnityServices.State == ServicesInitializationState.Initialized && AuthenticationService.Instance.IsSignedIn
                ? AuthenticationService.Instance.AccessToken : string.Empty;
            nm.NetworkConfig.ConnectionData = JoinRules.Payload(GameBuild.Version, Application.isEditor, token,
                KryticalNova.Core.LocalAccount.MadeUpId);
            nm.ConnectionApprovalCallback = Approve;
            if (maxPlayersNow <= 0) maxPlayersNow = defaultMaxPlayers;
        }

        /// <summary>Players a server refuses when Unity's proof of who they are is missing or wrong.</summary>
        private const string NoProof = "Your game could not prove who you are (Unity sign-in). Check your internet connection and join again.";

        private void Approve(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            NetworkManager nm = NetworkManager.Singleton;
            response.CreatePlayerObject = true; // the world's player prefab (set in each map's NetworkManager); PlayerSpawnService places it
            if (nm != null && request.ClientNetworkId == nm.LocalClientId)
            {
                response.Approved = true; // the host's own player
                return;
            }
            JoinRules.TryRead(request.Payload, out _, out _, out string token, out string oldAccount, out string roster);
            int online = nm != null ? nm.ConnectedClientsIds.Count : 0;
            // A question about characters is answered on a full server too: it never takes a place.
            string refusal = JoinRules.Refusal(GameBuild.Version, Application.isEditor, request.Payload, online,
                string.IsNullOrEmpty(roster) ? maxPlayersNow : 0);
            if (refusal != null)
            {
                Refuse(request.ClientNetworkId, response, refusal);
                return;
            }
            if (!string.IsNullOrEmpty(roster))
            {
                AnswerRoster(request.ClientNetworkId, response, token, oldAccount, roster);
                return;
            }
            if (Prove(request.ClientNetworkId, token, oldAccount, out string why))
            {
                if (!Banned(request.ClientNetworkId, response)) response.Approved = true;
                return;
            }
            if (!RequireProof) // the Editor hosting a test: a game that cannot prove itself plays as before
            {
                if (!Banned(request.ClientNetworkId, response, oldAccount)) response.Approved = true;
                return;
            }
            if (why != null)
            {
                Refuse(request.ClientNetworkId, response, NoProof, $"no proof of who they are ({why})");
                return;
            }
            // Unity's keys are not here yet (the server just started, or Unity signed with a new key): wait for them.
            response.Pending = true;
            ulong client = request.ClientNetworkId;
            UnityKeys.EnsureAsync(UnityKeys.KeyIdOf(token)).ContinueWith(_ =>
            {
                if (Prove(client, token, oldAccount, out string late))
                {
                    if (!Banned(client, response)) response.Approved = true;
                }
                else Refuse(client, response, NoProof, $"no proof of who they are ({late ?? "Unity's keys could not be fetched"})");
                response.Pending = false;
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        /// <summary>
        /// A game asking about its characters (online phase 4, milestone 5; <see cref="RosterLink"/>): proven like a join,
        /// answered by the roster (<see cref="ServerHooks.Roster"/>) for the proven account, and never let in: the answer
        /// is the reason the connection is closed with. Without proof (the Editor hosting a test) the game's own account
        /// is used, as for a join.
        /// </summary>
        private static void AnswerRoster(ulong client, NetworkManager.ConnectionApprovalResponse response, string token, string oldAccount, string packed)
        {
            response.CreatePlayerObject = false;
            if (Prove(client, token, oldAccount, out string why)) { AnswerRosterFor(client, response, oldAccount, packed); return; }
            if (!RequireProof) { AnswerRosterFor(client, response, oldAccount, packed, oldAccount); return; }
            if (why != null)
            {
                Refuse(client, response, NoProof, $"no proof of who they are ({why}), asking about characters");
                return;
            }
            response.Pending = true; // Unity's keys are not here yet: wait for them, as for a join
            UnityKeys.EnsureAsync(UnityKeys.KeyIdOf(token)).ContinueWith(_ =>
            {
                if (Prove(client, token, oldAccount, out string late)) AnswerRosterFor(client, response, oldAccount, packed);
                else Refuse(client, response, NoProof, $"no proof of who they are ({late ?? "Unity's keys could not be fetched"}), asking about characters");
                response.Pending = false;
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        private static void AnswerRosterFor(ulong client, NetworkManager.ConnectionApprovalResponse response, string oldAccount, string packed, string unproven = null)
        {
            string account = ServerAccounts.TryGet(client, out ServerAccounts.Entry proven) ? proven.Account : unproven;
            ServerAccounts.Forget(client); // this connection never plays
            DateTime now = DateTime.UtcNow;
            if (ServerBans.IsBanned(account, now, out BanEntry ban))
            {
                Refuse(client, response, ServerBans.Refusal(ban, now), $"banned account {account}, asking about characters");
                return;
            }
            response.Approved = false;
            string request = RosterWire.Unpack(packed);
            if (ServerHooks.Roster == null || request == null || string.IsNullOrEmpty(account))
            {
                response.Reason = ServerHooks.Roster == null ? "This server does not keep characters." : "The server could not read that request.";
                return;
            }
            string answer;
            try
            {
                answer = ServerHooks.Roster.Answer(account, oldAccount, request);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Roster] A request from {account} failed: {e}");
                answer = null;
            }
            string reply = answer != null ? RosterWire.Reply(answer) : null;
            if (reply != null && reply.Length > RosterWire.MaxReplyChars)
            {
                Debug.LogWarning($"[Roster] The answer for {account} is too long to send ({reply.Length} characters).");
                reply = null;
            }
            response.Reason = reply ?? "The server could not answer that. Try again.";
        }

        /// <summary>
        /// A banned account is turned away with the reason and how long is left (online phase 2, milestone 4). The account
        /// is the proven one, or on an Editor host test without sign-in <paramref name="unproven"/>, the game's own id.
        /// </summary>
        private static bool Banned(ulong client, NetworkManager.ConnectionApprovalResponse response, string unproven = null)
        {
            string account = ServerAccounts.TryGet(client, out ServerAccounts.Entry proven) ? proven.Account : unproven;
            DateTime now = DateTime.UtcNow;
            if (!ServerBans.IsBanned(account, now, out BanEntry ban)) return false;
            ServerAccounts.Forget(client);
            Refuse(client, response, ServerBans.Refusal(ban, now), $"banned account {account}");
            return true;
        }

        /// <summary>A dedicated server lets in only proven players; the Editor hosting a test takes anyone, as before.</summary>
        public static bool RequireProof
        {
            get
            {
#if UNITY_SERVER
                return true;
#else
                return false;
#endif
            }
        }

        /// <summary>Turns a game away: the player reads <paramref name="reason"/>; the access record has <paramref name="why"/> when given.</summary>
        private static void Refuse(ulong client, NetworkManager.ConnectionApprovalResponse response, string reason, string why = null)
        {
            response.Approved = false;
            response.Reason = reason;
            ServerRecords.Write(RecordKind.Access, $"Turned away connection {client}: {why ?? reason}" + (why != null ? $" (told: {reason})" : string.Empty));
        }

        /// <summary>
        /// True when <paramref name="token"/> proves a Unity player of this game: the connection is remembered under that
        /// player's account. False with <paramref name="why"/> when it does not; false with no reason while Unity's keys
        /// are still being fetched.
        /// </summary>
        private static bool Prove(ulong client, string token, string oldAccount, out string why)
        {
            why = null;
            string kid = UnityKeys.KeyIdOf(token);
            if (string.IsNullOrEmpty(token)) { why = "no sign-in token"; return false; }
            if (!UnityKeys.Ready || (kid != null && !UnityKeys.Current.ContainsKey(kid))) return false;
            if (!UnityToken.Verify(token, UnityKeys.Current, Application.cloudProjectId, DateTime.UtcNow, out string playerId, out why)) return false;
            ServerAccounts.Remember(client, AccountIds.FromUnityPlayer(playerId), oldAccount);
            return true;
        }

        // True when another program on this PC already listens on the UDP port.
        private static bool UdpPortInUse(ushort port)
        {
            try
            {
                using (new System.Net.Sockets.UdpClient(new System.Net.IPEndPoint(System.Net.IPAddress.Any, port))) return false;
            }
            catch (System.Net.Sockets.SocketException)
            {
                return true;
            }
        }

#if UNITY_SERVER
        // Players still on once one leaves: when the server sent them away they are already off the list, otherwise not yet.
        private static int OnlineWithout(NetworkManager nm, ulong leaving)
        {
            int online = 0;
            foreach (ulong id in nm.ConnectedClientsIds)
                if (id != leaving) online++;
            return online;
        }

        /// <summary>
        /// The dedicated server: turns away players past its limit or on another build (<see cref="Approve"/>) and waits
        /// for players. In Direct mode it listens on every network card of this machine, on the port of its settings
        /// (<see cref="ServerLaunchOptions"/>: server-settings.json or -port), and players reach it by its address and
        /// port; in Relay mode it takes a place on Unity Relay instead (<see cref="StartRelayServer"/>). Either way
        /// <see cref="ServerReach"/> keeps the published server list pointing at it.
        /// </summary>
        private void StartDedicatedServer()
        {
            // Another copy holds this server's ports: this one is quitting and publishes, backs up and listens nothing.
            if (ServerSingleCopy.AnotherCopyRuns) return;
            if (NetworkManager.Singleton == null)
            {
                ReportError("[Server] Cannot start: NetworkManager.Singleton is missing.");
                return;
            }

            ServerLaunchOptions options = ServerLaunchOptions.LoadForThisServer();
            // Saves through the save service (online phase 4): without it the server does not open, rather than quietly
            // saving somewhere else (Start Server.bat starts the service first).
            if (options.UsesSaveService && ServerHooks.Saves != null && !ServerHooks.Saves.ServiceReachable(out string savesProblem))
            {
                ReportError($"[Server] The saves cannot be reached: {savesProblem}. Start the save service (Start Server.bat starts it " +
                            $"from the SaveService folder beside the server's), then start the server again. Nobody can join until then.");
                Application.Quit(1);
                return;
            }
            if (options.UsesSaveService) Debug.Log($"[Server] Saves go to the save service at {options.saveService}.");
            if (options.MapServerProblem() is string mapProblem)
            {
                ReportError($"[Server] This map server cannot start: {mapProblem}.");
                Application.Quit(1);
                return;
            }
            ushort port = (ushort)options.port;
            ConfigureTransport(defaultListenAddress, port);
            OfflinePlayerHandoff.ReleaseOfflineAvatar();
            NetworkManager nm = NetworkManager.Singleton;
            maxPlayersNow = options.maxPlayers;
            PrepareConnections();
            // Who joined and left, with the account, is kept in access.log beside the saves (online phase 2, milestone 4).
            ServerRecords.LogFolder = options.RecordFolderPath;
            ServerRecords.MonthsKept = options.recordMonths;
            nm.OnClientConnectedCallback += id =>
            {
                Debug.Log($"[Server] Player joined (connection {id}). Online: {nm.ConnectedClientsIds.Count}.");
                ServerRecords.Joined(id);
            };
            // Every request a game sends is held to a speed limit, and foul play is flagged (online phase 2, milestone 3).
            RpcBudget.Configure(options.callLimits);
            SecurityFlags.LogFolder = options.RecordFolderPath;
            nm.OnClientDisconnectCallback += id =>
            {
                ServerRecords.Left(id);
                ServerAccounts.Forget(id);
                RpcBudget.Forget(id);
                NetworkHealth.Forget(id);
                Debug.Log($"[Server] Player left (connection {id}). Online: {OnlineWithout(nm, id)}.");
            };
            // Unity's sign-in keys, fetched now so the first player is not kept waiting (online phase 2).
            UnityKeys.EnsureAsync();
            if (ServerHooks.Saves != null) ServerHooks.Saves.CarryOverEnabled = options.carryOverOldSaves;
            // Players see one server (owner, 20 Oct 2026: "Nova is the one they see"): only the listed server publishes the
            // list; a map server is reached by travelling (online phase 5).
            if (options.IsMapServer) Debug.Log($"[Server] Map server for {options.map}: never in the server list; its records are in {options.RecordFolderPath}.");
            else ServerReach.Begin(options);
            // Online phase 5, milestone 2: every server tells the world service which map it runs and who is on it.
            WorldLink.Begin(options);
            var transport = nm.GetComponent<UnityTransport>();
            if (options.UsesRelay)
            {
                ServerEncryption.Off(transport); // Relay encrypts by itself
                // Relay drops a server it no longer hears (the internet went down): it restarts, saving first, for a new place.
                nm.OnTransportFailure += () => StartCoroutine(RestartAfterRelayLoss());
                StartRelayServer(options, nm);
                return;
            }
            // Direct connections are encrypted (online phase 2): every game insists on it, so without the certificate
            // nobody can join directly (Relay still works).
            if (ServerEncryption.TryLoadServerSecrets(options, out string certificate, out string privateKey, out string certificateProblem))
            {
                ServerEncryption.UseOnServer(transport, certificate, privateKey);
                Debug.Log("[Server] Direct connections are encrypted.");
            }
            else
            {
                ServerEncryption.Off(transport);
                ReportError($"[Server] Direct connections cannot be encrypted: {certificateProblem}. Games cannot join directly until the " +
                            "certificate is there (Server Guide, \"Encryption\"); they can still join through Relay.");
            }
            if (nm.StartServer())
            {
                Debug.Log($"[Server] {options.serverName} is listening on port {port} (UDP, every network card of this machine). Type a command (gm.help) and press Enter.");
                ServerStatusResponder.Begin(options);
                StatusChanged?.Invoke($"Dedicated server listening on port {port}");
            }
            else
            {
                ReportError($"[Server] Could not listen on port {port}. Is another server already using it?");
            }
        }

        private const float RelayRetrySeconds = 30f;
        private const int RelayMostConnections = 100; // Unity Relay's limit for one place

        /// <summary>
        /// Relay mode: signs in to Unity, asks Relay for a place for the players, and listens there; the join code goes into
        /// the server list (<see cref="ServerReach.RelayReady"/>). While Relay cannot be reached it asks again every 30
        /// seconds, without restarting. The status port still answers on this PC, for the owner's own game.
        /// </summary>
        private async void StartRelayServer(ServerLaunchOptions options, NetworkManager nm)
        {
            while (this != null)
            {
                string problem;
                try
                {
                    if (await EnsureServicesInitializedAsync())
                    {
                        int places = Mathf.Clamp(options.maxPlayers, 1, RelayMostConnections);
                        Unity.Services.Relay.Models.Allocation allocation = await Unity.Services.Relay.RelayService.Instance.CreateAllocationAsync(places);
                        string code = await Unity.Services.Relay.RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
                        if (this == null || nm == null) return;
                        nm.GetComponent<UnityTransport>().SetRelayServerData(Unity.Services.Relay.Models.AllocationUtils.ToRelayServerData(allocation, "dtls"));
                        if (!nm.StartServer())
                        {
                            ReportError("[Server] Could not start on Unity Relay.");
                            return;
                        }
                        Debug.Log($"[Server] {options.serverName} is reachable through Unity Relay ({places} places, region {allocation.Region}), " +
                                  $"join code {code}. Games read it from the server list; nobody types it. Type a command (gm.help) and press Enter.");
                        ServerReach.RelayReady(code);
                        ServerStatusResponder.Begin(options);
                        StatusChanged?.Invoke("Dedicated server on Unity Relay, join code " + code);
                        return;
                    }
                    problem = "Unity services did not sign in";
                }
                catch (Exception e)
                {
                    problem = e.Message;
                }
                ServerReach.RelayFailed(problem);
                Debug.LogWarning($"[Server] Unity Relay is not reachable ({problem}). Trying again in {RelayRetrySeconds:0} seconds.");
                await Task.Delay(TimeSpan.FromSeconds(RelayRetrySeconds));
            }
        }

        // One frame after the failure, once Netcode has closed the session: then the safe stop, which restarts.
        private System.Collections.IEnumerator RestartAfterRelayLoss()
        {
            yield return null;
            Debug.LogWarning("[Server] The link to Unity Relay was lost.");
            ServerLifetime.StopNow("The server lost its link to Unity Relay and is restarting. Join again in a minute.", restart: true);
        }
#endif

        /// <summary>The name that keeps this running copy of the game apart from others on the same machine (<see cref="KryticalNova.Core.LocalProfile.Name"/>).</summary>
        public static string LocalProfile() => KryticalNova.Core.LocalProfile.Name();

        /// <summary>A Multiplayer Play Mode clone's own profile, null elsewhere (<see cref="KryticalNova.Core.LocalProfile.EditorClone"/>).</summary>
        public static string EditorCloneProfile() => KryticalNova.Core.LocalProfile.EditorClone();

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
                    // Forced: with Unity's Dedicated Server package the transport otherwise takes the package's port
                    // (7777 when none is given) over this one. A server's -port still wins, through ServerLaunchOptions.
                    transport.SetConnectionData(true, address, port, "0.0.0.0");
                    // Unencrypted unless a dedicated server's own path, or a game joining one, switches it on after this.
                    ServerEncryption.Off(transport);
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
