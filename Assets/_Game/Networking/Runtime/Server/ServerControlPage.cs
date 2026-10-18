#if UNITY_SERVER
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using KryticalNova.Factions;
using KryticalNova.Persistence;
using KryticalNova.Player;
using KryticalNova.Progression;
using Unity.Netcode;
using UnityEngine;

namespace KryticalNova.Networking
{
    /// <summary>
    /// The server's control page: a small web page on the home network (<c>http://&lt;this PC&gt;:8090</c>) to see and run
    /// the server from a PC or a phone. It serves the files in the <c>ControlPage</c> folder beside the server and a few
    /// calls behind a password. Only the home network may reach it, a request must name this machine (not a web site),
    /// a button press must come from the page itself (a token only the page holds), and five wrong passwords lock an
    /// address out for five minutes. Nothing is fetched from the internet and it needs no administrator rights.
    /// Only in a server build; <c>pagePort</c> 0 in the settings switches it off.
    /// </summary>
    public sealed class ServerControlPage : MonoBehaviour
    {
        /// <summary>Runs a GM command line typed at (line, where from) and returns the answer (set by ServerConsoleInput, which has the GM console).</summary>
        public static Func<string, string, string> CommandRunner;

        private const string CookieName = "kn_page";
        private const string FolderName = "ControlPage";
        private const int LogKept = 500;
        private const int MainThreadWaitMs = 10000;
        private static readonly Dictionary<string, string> Types = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { ".html", "text/html; charset=utf-8" }, { ".css", "text/css; charset=utf-8" }, { ".js", "text/javascript; charset=utf-8" },
            { ".ttf", "font/ttf" }, { ".otf", "font/otf" }, { ".png", "image/png" }, { ".svg", "image/svg+xml" }, { ".ico", "image/x-icon" },
        };

        private static ServerControlPage instance;
        private static readonly object LogGate = new object();
        private static readonly LinkedList<(long Id, string Text)> Log = new LinkedList<(long, string)>();
        private static long nextLogId = 1;

        private readonly ControlPageRules.Lockout lockout = new ControlPageRules.Lockout();
        private readonly Dictionary<string, (string Check, DateTime Expires)> sessions = new Dictionary<string, (string, DateTime)>();
        private readonly ConcurrentQueue<Action> mainThread = new ConcurrentQueue<Action>();
        private ServerLaunchOptions options;
        private string password, folder;
        private TcpListener listener;
        private volatile bool running;
        private volatile string statusJson = "{}";
        private float nextStatus;
        private List<string> addresses = new List<string>();

        // From the first frame, so the page's log shows the server starting.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void HookLog() => Application.logMessageReceivedThreaded += OnLog;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (instance != null || ServerSingleCopy.AnotherCopyRuns) return;
            ServerLaunchOptions options = ServerLaunchOptions.LoadForThisServer();
            if (options.pagePort <= 0)
            {
                Debug.Log("[Server] The control page is off (pagePort 0 in server-settings.json).");
                return;
            }
            var host = new GameObject("Server Control Page");
            DontDestroyOnLoad(host);
            instance = host.AddComponent<ServerControlPage>();
            instance.options = options;
        }

        private void Start()
        {
            folder = Path.Combine(options.ServerFolder, FolderName);
            password = options.EnsurePagePassword(out bool isNew);
            if (isNew) Debug.Log("[Server] The control page had no password: one was made and written into server-settings.json (pagePassword).");
            addresses = ControlPageRules.HomeAddresses();
            try
            {
                listener = new TcpListener(IPAddress.Any, options.pagePort);
                listener.Start();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Server] The control page could not open port {options.pagePort} ({e.Message}). Change pagePort in server-settings.json.");
                listener = null;
                return;
            }
            running = true;
            new Thread(AcceptLoop) { IsBackground = true, Name = "Control page" }.Start();
            foreach (string address in addresses) Debug.Log($"[Server] Control page: http://{address}:{options.pagePort} (home network only).");
            if (!Directory.Exists(folder)) Debug.LogWarning($"[Server] The control page's files are missing ({folder}): build the server again.");
        }

        private void Update()
        {
            while (mainThread.TryDequeue(out Action action)) action();
            if (Time.realtimeSinceStartup >= nextStatus)
            {
                nextStatus = Time.realtimeSinceStartup + 1f;
                statusJson = BuildStatus();
            }
        }

        private void OnDestroy() => StopListening();
        private void OnApplicationQuit() => StopListening();

        private void StopListening()
        {
            running = false;
            try { listener?.Stop(); } catch (Exception) { /* closing anyway */ }
        }

        // ------------------------------------------------------------------ the log

        private static void OnLog(string message, string stack, LogType type)
        {
            if (string.IsNullOrEmpty(message)) return;
            if (message.Length > 2000) message = message.Substring(0, 2000) + "...";
            if (type == LogType.Error || type == LogType.Exception) message = "(error) " + message;
            lock (LogGate)
            {
                Log.AddLast((nextLogId++, message));
                while (Log.Count > LogKept) Log.RemoveFirst();
            }
        }

        private static string LogAfter(long after)
        {
            var sb = new StringBuilder("{\"lines\":[");
            long last = after;
            lock (LogGate)
            {
                bool first = true;
                foreach (var (id, text) in Log)
                {
                    if (id <= after) continue;
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append("{\"id\":").Append(id).Append(",\"text\":").Append(ControlPageRules.Json(text)).Append('}');
                    last = id;
                }
            }
            return sb.Append("],\"last\":").Append(last).Append('}').ToString();
        }

        // ------------------------------------------------------------------ the server's state (built on the game's thread)

        private string BuildStatus()
        {
            var sb = new StringBuilder(1024);
            NetworkManager network = NetworkManager.Singleton;
            var players = new List<string>();
            if (network != null && network.IsServer)
                foreach (NetworkClient client in network.ConnectedClientsList)
                {
                    if (client.PlayerObject == null) continue;
                    GameObject player = client.PlayerObject.gameObject;
                    string name = player.TryGetComponent(out PlayerIdentity identity) ? identity.CharacterName : player.name;
                    string faction = identity != null && identity.Faction != FactionId.None ? identity.Faction.ToString() : "-";
                    int level = player.TryGetComponent(out PlayerCharacterProgression progression) ? progression.CurrentLevel : 0;
                    players.Add("{\"id\":" + client.ClientId + ",\"name\":" + ControlPageRules.Json(name) + ",\"level\":" + level +
                                ",\"faction\":" + ControlPageRules.Json(faction) + ",\"map\":" + ControlPageRules.Json(player.scene.name) +
                                ",\"gm\":" + ControlPageRules.Json(GmRoster.NameOf(client.ClientId) ?? string.Empty) +
                                ",\"ping\":" + NetworkHealth.PingMs(client.ClientId) +
                                ",\"lateHits\":" + NetworkHealth.Count(client.ClientId, LagEvent.LateHit) +
                                ",\"goneTargets\":" + NetworkHealth.Count(client.ClientId, LagEvent.GoneTarget) + "}");
                }
            // The Game Masters by name and whether they are signed in now; their passwords never leave the settings file.
            var masters = new List<string>();
            foreach (GmEntry entry in GmRoster.All)
                masters.Add("{\"name\":" + ControlPageRules.Json(entry.name) + ",\"online\":" + (GmRoster.IsSignedIn(entry.name) ? "true" : "false") +
                            ",\"bound\":" + (entry.IsBound ? "true" : "false") + "}");
            sb.Append("{\"name\":").Append(ControlPageRules.Json(options.serverName))
              .Append(",\"listening\":").Append(network != null && network.IsListening ? "true" : "false")
              .Append(",\"port\":").Append(options.port)
              .Append(",\"maxPlayers\":").Append(options.maxPlayers)
              .Append(",\"map\":").Append(ControlPageRules.Json(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name))
              .Append(",\"uptime\":").Append(Mathf.RoundToInt(ServerLifetime.UptimeSeconds))
              .Append(",\"nextBackup\":").Append(Mathf.RoundToInt(ServerLifetime.NextBackupSeconds))
              .Append(",\"lastBackup\":").Append(ControlPageRules.Json(ServerLifetime.LastBackup))
              .Append(",\"backupsKept\":").Append(options.backupsKept)
              .Append(",\"shutdownIn\":").Append(Mathf.CeilToInt(ServerLifetime.ShutdownSeconds))
              .Append(",\"restarting\":").Append(ServerLifetime.ShutdownRestarts ? "true" : "false")
              .Append(",\"stopping\":").Append(ServerLifetime.Stopping ? "true" : "false")
              .Append(",\"version\":").Append(ControlPageRules.Json(Application.version))
              .Append(",\"addresses\":[");
            for (int i = 0; i < addresses.Count; i++) sb.Append(i > 0 ? "," : string.Empty).Append(ControlPageRules.Json(addresses[i]));
            sb.Append("],\"players\":[").Append(string.Join(",", players))
              .Append("],\"gms\":[").Append(string.Join(",", masters))
              .Append("],\"bans\":[").Append(BansJson())
              .Append("],\"serverList\":[").Append(ListJson())
              .Append("],\"listPublished\":").Append(ControlPageRules.Json(ServerReach.LastPublished))
              .Append(",\"statusPort\":").Append(options.StatusPort)
              .Append(",\"mode\":").Append(ControlPageRules.Json(options.connectionMode))
              .Append(",\"publicAddress\":").Append(ControlPageRules.Json(ServerReach.PublicAddress))
              .Append(",\"relayCode\":").Append(ControlPageRules.Json(ServerReach.RelayCode))
              .Append(",\"relayNote\":").Append(ControlPageRules.Json(ServerReach.RelayNote))
              .Append(",\"autoNote\":").Append(ControlPageRules.Json(ServerReach.AutoNote))
              .Append(",\"load\":").Append(ControlPageRules.Json(ServerPerformanceLog.Summary))
              .Append(",\"loadSlow\":").Append(ServerPerformanceLog.LastSlowFrames)
              .Append(",\"maxGms\":").Append(GmRoster.MaxGameMasters).Append('}');
            return sb.ToString();
        }

        // Bans in force (online phase 2, milestone 4): the account id is needed to end one from the page.
        private static string BansJson()
        {
            DateTime now = DateTime.UtcNow;
            var rows = new List<string>();
            foreach (BanEntry ban in ServerBans.All(now))
                rows.Add("{\"account\":" + ControlPageRules.Json(ban.account) + ",\"character\":" + ControlPageRules.Json(ban.character) +
                         ",\"by\":" + ControlPageRules.Json(ban.by) + ",\"reason\":" + ControlPageRules.Json(ban.reason) +
                         ",\"since\":" + ControlPageRules.Json(new DateTime(ban.sinceUtc, DateTimeKind.Utc).ToLocalTime().ToString("d MMM HH:mm", System.Globalization.CultureInfo.InvariantCulture)) +
                         ",\"left\":" + ControlPageRules.Json(ban.IsForever ? "for good" : ServerBans.Describe(new DateTime(ban.untilUtc, DateTimeKind.Utc) - now)) + "}");
            return string.Join(",", rows);
        }

        // ------------------------------------------------------------------ the socket (a thread of its own)

        private void AcceptLoop()
        {
            while (running)
            {
                TcpClient client;
                try { client = listener.AcceptTcpClient(); }
                catch (Exception) { if (!running) return; continue; }
                ThreadPool.QueueUserWorkItem(_ => Handle(client));
            }
        }

        private void Handle(TcpClient client)
        {
            using (client)
            {
                try
                {
                    client.ReceiveTimeout = 5000;
                    client.SendTimeout = 5000;
                    NetworkStream stream = client.GetStream();
                    IPAddress from = ((IPEndPoint)client.Client.RemoteEndPoint).Address;
                    if (!ControlPageRules.IsHomeNetwork(from)) { Send(stream, 403, "Forbidden", "text/plain", "The control page answers the home network only."); return; }
                    ControlPageRules.Request request = ControlPageRules.Read(stream, out bool empty);
                    if (empty) return; // a browser's spare connection, closed unused: nobody to answer
                    if (request == null) { Send(stream, 400, "Bad Request", "text/plain", "Bad request."); return; }
                    if (!ControlPageRules.IsAcceptableHost(request.Header("Host"))) { Send(stream, 403, "Forbidden", "text/plain", "Open the page by this PC's address."); return; }
                    Route(stream, request, from.ToString());
                }
                catch (Exception e)
                {
                    // A browser that went away before the answer is not a failure of the page.
                    if (!ControlPageRules.IsClientHangUp(e)) Debug.LogWarning("[Server] Control page request failed: " + e.Message);
                }
            }
        }

        private void Route(NetworkStream stream, ControlPageRules.Request request, string from)
        {
            if (!request.Path.StartsWith("/api/", StringComparison.Ordinal))
            {
                if (request.Method != "GET") { Send(stream, 405, "Method Not Allowed", "text/plain", "No."); return; }
                ServeFile(stream, request.Path == "/" ? "/index.html" : request.Path);
                return;
            }

            if (request.Path == "/api/login" && request.Method == "POST")
            {
                Login(stream, request, from);
                return;
            }

            string token = request.Cookie(CookieName);
            string check = SessionCheck(token);
            if (check == null) { SendJson(stream, 401, "{\"error\":\"Log in first.\"}"); return; }

            if (request.Method == "GET")
            {
                switch (request.Path)
                {
                    case "/api/session": SendJson(stream, 200, "{\"check\":" + ControlPageRules.Json(check) + "}"); return;
                    case "/api/status": SendJson(stream, 200, statusJson); return;
                    case "/api/log":
                        long.TryParse(QueryValue(request.Query, "after"), out long after);
                        SendJson(stream, 200, LogAfter(after));
                        return;
                    case "/api/flags":
                        long.TryParse(QueryValue(request.Query, "after"), out long afterFlag);
                        SendJson(stream, 200, SecurityFlags.JsonAfter(afterFlag));
                        return;
                }
                SendJson(stream, 404, "{\"error\":\"No such call.\"}");
                return;
            }

            // A button press: only from the page itself, which alone holds the session's check.
            if (request.Method != "POST" || !ControlPageRules.IsSameOrigin(request) || !ControlPageRules.SecretEquals(request.Header("X-KN-Check"), check))
            {
                SendJson(stream, 403, "{\"error\":\"That did not come from the control page.\"}");
                return;
            }
            Body body = ParseBody(request.Body);
            string answer;
            switch (request.Path)
            {
                case "/api/logout":
                    lock (sessions) sessions.Remove(token);
                    SendJson(stream, 200, "{\"answer\":\"Logged out.\"}", "Set-Cookie: " + CookieName + "=; HttpOnly; SameSite=Strict; Path=/; Max-Age=0");
                    return;
                case "/api/save":
                    answer = OnMain(() => { ServerSave.FlushAll(); return "Everyone is saved."; });
                    break;
                case "/api/backup":
                    answer = OnMain(() => ServerLifetime.Backup("page").Replace("[Backup] ", "Backup: "));
                    break;
                case "/api/announce":
                    if (string.IsNullOrWhiteSpace(body.text)) { answer = "Type the announcement first."; break; }
                    answer = OnMain(() => $"Announced to {KryticalNova.Social.PlayerChat.ServerBroadcastGm(body.text.Trim(), "Server")} player(s).");
                    break;
                case "/api/kick":
                    answer = OnMain(() => Kick(body.id, body.reason));
                    break;
                case "/api/ban":
                    answer = OnMain(() => ServerBans.BanPlayer(body.id, body.text, body.reason, $"Control page ({from})"));
                    break;
                case "/api/unban":
                    answer = OnMain(() => ServerBans.UnbanPlayer(body.id));
                    break;
                case "/api/stop":
                    answer = OnMain(() => ServerLifetime.ScheduleShutdown(Mathf.Clamp(body.seconds, 0f, 3600f), body.reason, body.restart));
                    break;
                case "/api/cancel":
                    answer = OnMain(ServerLifetime.CancelShutdown);
                    break;
                case "/api/command":
                    if (string.IsNullOrWhiteSpace(body.line)) { answer = "Type a command first."; break; }
                    if (CommandRunner == null) { answer = "Commands are not ready yet."; break; }
                    answer = OnMain(() => CommandRunner(body.line.Trim(), $"Control page ({from})"));
                    break;
                case "/api/list/add":
                    answer = OnMain(() => AddListing(body.name, body.address, body.port));
                    break;
                case "/api/list/remove":
                    answer = OnMain(() => RemoveListing(body.id));
                    break;
                case "/api/list/publish":
                    answer = ServerReach.Publish(options, out _); // runs the upload on this request's thread, not the game's
                    break;
                case "/api/mode":
                    answer = OnMain(() => SwitchMode(body.text, body.seconds));
                    break;
                case "/api/gm/add":
                    answer = OnMain(() => AddGm(body.name));
                    break;
                case "/api/gm/bind":
                    answer = OnMain(() => BindGm(body.name, body.id));
                    break;
                case "/api/gm/unbind":
                    answer = OnMain(() => UnbindGm(body.name));
                    break;
                case "/api/gm/remove":
                    answer = OnMain(() => RemoveGm(body.name));
                    break;
                default:
                    SendJson(stream, 404, "{\"error\":\"No such call.\"}");
                    return;
            }
            // Every button press is a GM action and goes in the GM record with its answer (online phase 2, milestone 4).
            // The command box writes its own line.
            if (request.Path != "/api/command")
            {
                string pressed = Pressed(request.Path, body);
                string said = ServerRecords.Short(answer);
                OnMain(() => { ServerRecords.Write(RecordKind.Gm, $"Control page ({from}): {pressed} -> {said}"); return null; });
            }
            SendJson(stream, 200, "{\"answer\":" + ControlPageRules.Json(answer) + "}");
        }

        // "kick id=4 reason=spam": the button's call and what it was given, for the record. Never the password.
        private static string Pressed(string path, Body body)
        {
            var sb = new StringBuilder(path.Substring("/api/".Length));
            void Add(string field, string value)
            {
                if (!string.IsNullOrWhiteSpace(value)) sb.Append(' ').Append(field).Append("=\"").Append(value.Trim()).Append('"');
            }
            Add("id", body.id);
            Add("name", body.name);
            Add("text", body.text);
            Add("reason", body.reason);
            Add("address", body.address);
            if (body.port != 0) sb.Append(" port=").Append(body.port);
            if (body.seconds != 0f) sb.Append(" seconds=").Append(Mathf.RoundToInt(body.seconds));
            if (body.restart) sb.Append(" restart");
            return sb.ToString();
        }

        [Serializable]
        private sealed class Body
        {
            public string password = string.Empty, text = string.Empty, reason = string.Empty, line = string.Empty, id = string.Empty, name = string.Empty, address = string.Empty;
            public int port;
            public float seconds;
            public bool restart;
        }

        private static Body ParseBody(string json)
        {
            try { return string.IsNullOrWhiteSpace(json) ? new Body() : JsonUtility.FromJson<Body>(json) ?? new Body(); }
            catch (Exception) { return new Body(); }
        }

        private void Login(NetworkStream stream, ControlPageRules.Request request, string from)
        {
            DateTime now = DateTime.UtcNow;
            if (lockout.IsLocked(from, now, out TimeSpan left))
            {
                SendJson(stream, 429, "{\"error\":" + ControlPageRules.Json($"Too many wrong passwords. Try again in {Math.Ceiling(left.TotalMinutes)} minute(s).") + "}");
                return;
            }
            if (!ControlPageRules.IsSameOrigin(request)) { SendJson(stream, 403, "{\"error\":\"That did not come from the control page.\"}"); return; }
            Body body = ParseBody(request.Body);
            if (!ControlPageRules.SecretEquals(body.password, password))
            {
                Thread.Sleep(400); // slows guessing a little more
                int tries = lockout.Wrong(from, now);
                Debug.LogWarning($"[Server] Wrong control page password from {from}.");
                string error = tries > 0 ? $"Wrong password. {tries} tr{(tries == 1 ? "y" : "ies")} left before a 5-minute lock." : "Wrong password. Locked for 5 minutes.";
                SendJson(stream, 401, "{\"error\":" + ControlPageRules.Json(error) + "}");
                return;
            }
            lockout.Right(from);
            string token = ControlPageRules.NewToken(), check = ControlPageRules.NewToken(16);
            lock (sessions) sessions[token] = (check, now + ControlPageRules.SessionTime);
            Debug.Log($"[Server] Control page opened from {from}.");
            SendJson(stream, 200, "{\"check\":" + ControlPageRules.Json(check) + "}",
                $"Set-Cookie: {CookieName}={token}; HttpOnly; SameSite=Strict; Path=/; Max-Age={(int)ControlPageRules.SessionTime.TotalSeconds}");
        }

        /// <summary>The session's check token, or null when the session is unknown or old.</summary>
        private string SessionCheck(string token)
        {
            if (string.IsNullOrEmpty(token)) return null;
            lock (sessions)
            {
                if (!sessions.TryGetValue(token, out var session)) return null;
                if (session.Expires > DateTime.UtcNow) return session.Check;
                sessions.Remove(token);
                return null;
            }
        }

        private static string Kick(string who, string reason)
        {
            NetworkManager network = NetworkManager.Singleton;
            if (network == null || !network.IsServer) return "The server is not running a session.";
            if (!ulong.TryParse(who, out ulong id) || !network.ConnectedClients.ContainsKey(id)) return "That player is no longer here.";
            if (id == NetworkManager.ServerClientId) return "That is the host's own player: the host cannot be sent away.";
            string told = string.IsNullOrWhiteSpace(reason) ? "A GM removed you from the server." : "A GM removed you from the server: " + reason.Trim();
            string removed = ServerRecords.Who(id);
            network.DisconnectClient(id, told);
            return $"Removed {removed}.";
        }

        // ------------------------------------------------------------------ the servers players see (online step 4)

        private string ListJson()
        {
            var rows = new List<string>();
            ServerListing[] list = options.serverList ?? new ServerListing[0];
            for (int i = 0; i < list.Length; i++)
                rows.Add("{\"id\":" + i + ",\"name\":" + ControlPageRules.Json(list[i].name) + ",\"address\":" + ControlPageRules.Json(list[i].address) +
                         ",\"port\":" + list[i].port + ",\"auto\":" + (list[i].IsAuto ? "true" : "false") + "}");
            return string.Join(",", rows);
        }

        // Direct or Relay (online step 5): saved, then the server restarts into it; the "auto" entry is published again
        // by itself once it runs.
        private string SwitchMode(string mode, float seconds)
        {
            string wanted = (mode ?? string.Empty).Trim().ToLowerInvariant();
            if (wanted != ServerLaunchOptions.Direct && wanted != ServerLaunchOptions.Relay) return "Choose direct or relay.";
            if (wanted == options.connectionMode) return $"The server already runs in {wanted} mode.";
            if (!options.SaveConnectionMode(wanted, out string error)) return error;
            string reason = wanted == ServerLaunchOptions.Relay
                ? "The server is switching to Unity Relay. Join again in a minute."
                : "The server is switching to direct connections. Join again in a minute.";
            ServerLifetime.ScheduleShutdown(Mathf.Clamp(seconds, 0f, 3600f), reason, true);
            return $"Saved: {wanted} mode. The server restarts into it in {Mathf.CeilToInt(Mathf.Clamp(seconds, 0f, 3600f))} seconds.";
        }

        private string AddListing(string name, string address, int port)
        {
            var list = new List<ServerListing>(options.serverList ?? new ServerListing[0]);
            if (port == 0) port = 7777;
            string problem = ServerListFile.Problem(name, address, port);
            if (problem != null) return problem;
            if (list.Count >= ServerListFile.MaxServers) return $"The list holds {ServerListFile.MaxServers} servers at most.";
            var entry = new ServerListing { name = name.Trim(), address = address.Trim(), port = port };
            foreach (ServerListing other in list)
                if (other.Key == entry.Key) return $"{other.address} : {other.port} is on the list already ({other.name}).";
            list.Add(entry);
            if (!options.SaveServerList(list.ToArray(), out string error)) return error;
            return $"{entry.name} is on the list. Press PUBLISH LIST so players see it.";
        }

        private string RemoveListing(string id)
        {
            var list = new List<ServerListing>(options.serverList ?? new ServerListing[0]);
            if (!int.TryParse(id, out int index) || index < 0 || index >= list.Count) return "That server is no longer on the list.";
            string gone = list[index].name;
            list.RemoveAt(index);
            if (!options.SaveServerList(list.ToArray(), out string error)) return error;
            return $"{gone} is off the list. Press PUBLISH LIST so players stop seeing it.";
        }

        /// <summary>Adds a Game Master and writes the list into the settings file.</summary>
        private string AddGm(string name)
        {
            var list = new List<GmEntry>(GmRoster.All);
            string problem = GmRoster.ValidateNew(list, name);
            if (problem != null) return problem;
            name = name.Trim();
            list.Add(new GmEntry { name = name });
            if (!options.SaveGameMasters(list.ToArray(), out string error)) return error;
            GmRoster.SetAll(list);
            Debug.Log($"[GM] {name} was made a GM on the control page.");
            return $"{name} is a GM. Bind them: pick the player beside their name while they are in the game.";
        }

        /// <summary>
        /// Binds a GM to the account of a player in the game now (online phase 2, owner: GMs are bound on the control page,
        /// no password in the game). They are GM at once and on every later join.
        /// </summary>
        private string BindGm(string name, string who)
        {
            GmEntry entry = GmRoster.Find((name ?? string.Empty).Trim());
            if (entry == null) return $"There is no GM called {name}.";
            NetworkManager network = NetworkManager.Singleton;
            if (network == null || !network.IsServer || !ulong.TryParse(who, out ulong client) || !network.ConnectedClients.ContainsKey(client))
                return "That player is no longer here.";
            if (!ServerAccounts.TryGet(client, out ServerAccounts.Entry proven)) return "That player's account is not proven (they joined without Unity sign-in).";
            GmEntry already = GmRoster.FindByAccount(proven.Account);
            if (already != null && already != entry) return $"That player is already GM {already.name}.";
            var list = new List<GmEntry>(GmRoster.All);
            foreach (GmEntry each in list)
                if (each == entry) each.account = proven.Account;
            if (!options.SaveGameMasters(list.ToArray(), out string error)) return error;
            GmRoster.SetAll(list);
            GmRoster.SignInBound(client, out _);
            string player = PlayerName(client);
            Debug.Log($"[GM] {entry.name} was bound to {player}'s account on the control page.");
            return $"{entry.name} is now {player}'s account: GM at once, and every time they join.";
        }

        private string UnbindGm(string name)
        {
            GmEntry entry = GmRoster.Find((name ?? string.Empty).Trim());
            if (entry == null) return $"There is no GM called {name}.";
            if (!entry.IsBound) return $"{entry.name} is not bound to anyone.";
            var list = new List<GmEntry>(GmRoster.All);
            foreach (GmEntry each in list)
                if (each == entry) each.account = string.Empty;
            if (!options.SaveGameMasters(list.ToArray(), out string error)) return error;
            GmRoster.SetAll(list);
            Debug.Log($"[GM] {entry.name} was unbound on the control page.");
            return $"{entry.name} is bound to no one now; their GM rights ended.";
        }

        private static string PlayerName(ulong client)
        {
            NetworkManager network = NetworkManager.Singleton;
            if (network != null && network.ConnectedClients.TryGetValue(client, out NetworkClient c) && c.PlayerObject != null
                && c.PlayerObject.TryGetComponent(out PlayerIdentity identity)) return identity.CharacterName;
            return "connection " + client;
        }

        /// <summary>Removes a Game Master; if they are signed in, their GM rights end at once.</summary>
        private string RemoveGm(string name)
        {
            GmEntry entry = GmRoster.Find((name ?? string.Empty).Trim());
            if (entry == null) return $"There is no GM called {name}.";
            bool online = GmRoster.IsSignedIn(entry.name);
            var list = new List<GmEntry>(GmRoster.All);
            list.Remove(entry);
            if (!options.SaveGameMasters(list.ToArray(), out string error)) return error;
            GmRoster.SetAll(list);
            Debug.Log($"[GM] {entry.name} is no longer a GM (control page).");
            return $"{entry.name} is no longer a GM." + (online ? " Their GM rights ended at once." : string.Empty);
        }

        /// <summary>Runs <paramref name="work"/> on the game's thread and waits for its answer.</summary>
        private string OnMain(Func<string> work)
        {
            string answer = "The server did not answer in time.";
            using (var done = new ManualResetEventSlim(false))
            {
                mainThread.Enqueue(() =>
                {
                    try { answer = work(); }
                    catch (Exception e) { answer = "Failed: " + e.Message; }
                    finally { done.Set(); }
                });
                done.Wait(MainThreadWaitMs);
            }
            return answer;
        }

        // ------------------------------------------------------------------ answers

        private void ServeFile(NetworkStream stream, string path)
        {
            // Only plain names under the page's folder: no "..", no hidden files, known types only.
            string relative = Uri.UnescapeDataString(path).TrimStart('/').Replace('\\', '/');
            string extension = Path.GetExtension(relative);
            if (relative.Contains("..") || relative.Contains(":") || relative.StartsWith(".", StringComparison.Ordinal) || !Types.TryGetValue(extension, out string type))
            {
                Send(stream, 404, "Not Found", "text/plain", "Not found.");
                return;
            }
            string full = Path.GetFullPath(Path.Combine(folder, relative));
            if (!full.StartsWith(Path.GetFullPath(folder), StringComparison.OrdinalIgnoreCase) || !File.Exists(full))
            {
                Send(stream, 404, "Not Found", "text/plain", "Not found.");
                return;
            }
            // Asked again every time: the files are small, and a new build's page must never meet an old script.
            Send(stream, 200, "OK", type, File.ReadAllBytes(full), "Cache-Control: no-cache");
        }

        private static void SendJson(NetworkStream stream, int code, string json, string extraHeader = null) =>
            Send(stream, code, code == 200 ? "OK" : "Error", "application/json; charset=utf-8", Encoding.UTF8.GetBytes(json), "Cache-Control: no-store", extraHeader);

        private static void Send(NetworkStream stream, int code, string reason, string type, string text) =>
            Send(stream, code, reason, type + "; charset=utf-8", Encoding.UTF8.GetBytes(text), "Cache-Control: no-store");

        private static void Send(NetworkStream stream, int code, string reason, string type, byte[] body, params string[] extraHeaders)
        {
            var head = new StringBuilder();
            head.Append("HTTP/1.1 ").Append(code).Append(' ').Append(reason).Append("\r\n");
            head.Append("Content-Type: ").Append(type).Append("\r\n");
            head.Append("Content-Length: ").Append(body.Length).Append("\r\n");
            head.Append("Connection: close\r\n");
            head.Append("X-Content-Type-Options: nosniff\r\n");
            head.Append("X-Frame-Options: DENY\r\n");
            head.Append("Referrer-Policy: no-referrer\r\n");
            head.Append("Content-Security-Policy: default-src 'self'; img-src 'self' data:; frame-ancestors 'none'; base-uri 'none'; form-action 'none'\r\n");
            foreach (string header in extraHeaders)
                if (!string.IsNullOrEmpty(header)) head.Append(header).Append("\r\n");
            head.Append("\r\n");
            byte[] headBytes = Encoding.ASCII.GetBytes(head.ToString());
            stream.Write(headBytes, 0, headBytes.Length);
            stream.Write(body, 0, body.Length);
            stream.Flush();
        }

        private static string QueryValue(string query, string name)
        {
            foreach (string pair in (query ?? string.Empty).Split('&'))
            {
                int equals = pair.IndexOf('=');
                if (equals > 0 && pair.Substring(0, equals) == name) return Uri.UnescapeDataString(pair.Substring(equals + 1));
            }
            return null;
        }
    }
}
#endif
