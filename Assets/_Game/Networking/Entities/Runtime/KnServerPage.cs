#if UNITY_SERVER || UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace KryticalNova.Networking.Entities
{
    /// <summary>
    /// What the page answers, decided apart from any socket so it can be tested: <c>GET /status</c> (also <c>/</c> and
    /// <c>/health</c>) the latest <see cref="KnStatusSnapshot"/> as JSON, readable from anywhere; <c>POST /stop</c> through
    /// Core's <see cref="StopEndpointRules"/> (loopback only, header <c>X-KN-Key</c> equal to the save service key, the first good
    /// call 200 and the rest 202). Nothing here touches the entity world.
    /// </summary>
    public static class KnPageRules
    {
        public readonly struct Reply
        {
            public readonly int Status;
            public readonly string ContentType;
            public readonly string Body;
            public readonly bool BeginStop;
            /// <summary>Non-zero: the PlayerKey of a player the main thread must now disconnect.</summary>
            public readonly ulong KickKey;
            /// <summary>Not null: the answer is this work's, run on the game's thread (a command, a GM button); <see cref="Body"/> is unused.</summary>
            public readonly Func<string> Work;
            /// <summary>The work's answer is JSON already (the GM list); otherwise it is text, sent as <c>{"answer":"..."}</c>.</summary>
            public readonly bool WorkIsJson;
            /// <summary>Not null: the body as bytes (a file of the browser page), sent instead of <see cref="Body"/>.</summary>
            public readonly byte[] Bytes;
            /// <summary>Extra header lines of the browser page (cookie, security headers), each ending in CRLF; null = none.</summary>
            public readonly string ExtraHeaders;

            public Reply(int status, string contentType, string body, bool beginStop, ulong kickKey = 0, Func<string> work = null, bool workIsJson = false,
                byte[] bytes = null, string extraHeaders = null)
            {
                Bytes = bytes;
                ExtraHeaders = extraHeaders;
                Status = status;
                ContentType = contentType;
                Body = body ?? string.Empty;
                BeginStop = beginStop;
                KickKey = kickKey;
                Work = work;
                WorkIsJson = workIsJson;
            }
        }

        /// <summary>
        /// The owner's GM tools on this page (M7.4, owner 2 Nov 2026: the command box and the GM list first): run on the game's thread, the words
        /// and rules NGO's control page has. Filled by the entity netcode's player side; empty, the page answers 503.
        /// </summary>
        public interface ICommands
        {
            string Command(string line, string from);
            /// <summary>The GMs as JSON: <c>[{"name":..,"bound":..,"signedIn":..}]</c>.</summary>
            string GmListJson();
            string GmAdd(string name);
            /// <summary>Binds the GM <paramref name="name"/> to the account of the player <paramref name="playerKey"/> (from the player list).</summary>
            string GmBind(string name, ulong playerKey);
            string GmUnbind(string name);
            string GmRemove(string name);
        }

        public static ICommands Commands;

        public const string CommandPath = "/api/command";
        public const string GmPath = "/api/gm";

        [Serializable]
        private sealed class Body
        {
            public string line = string.Empty;
            public string name = string.Empty;
            public string player = string.Empty;
        }

        public const string PlayersPath = "/players";
        public const string KickPath = "/kick";

        public static Reply Handle(string method, string path, bool fromLoopback, string givenKey, string expectedKey,
            KnStatusSnapshot status, StopEndpointRules stop, bool serverAlreadyStopping, string query = null, string body = null, bool authorized = false)
        {
            // authorized: the browser page (KnPageBrowser) already proved the caller by password and session; the owner's check is not asked again
            bool owner = authorized || (fromLoopback && !string.IsNullOrEmpty(expectedKey) && ControlPageRules.SecretEquals(givenKey, expectedKey));
            if (path == CommandPath || path == GmPath || path.StartsWith(GmPath + "/", StringComparison.Ordinal))
            {
                // The owner's, like the player list: this machine only, and the same key as the stop endpoint.
                if (!owner)
                    return new Reply(403, null, string.Empty, false);
                return Tools(method, path, body);
            }
            if (KnPageDesk.Handles(path))
            {
                // the rest of NGO's control page (8 Nov 2026 port): save, announce, bans, log, flags; the owner's, as the GM tools
                if (!owner)
                    return new Reply(403, null, string.Empty, false);
                return KnPageDesk.Handle(method, path, query, body);
            }
            if (path == PlayersPath || path == KickPath)
            {
                // The player list and the kick are the server owner's: this machine only, and the same key as the stop endpoint.
                if (!owner)
                    return new Reply(403, null, string.Empty, false);
                if (path == PlayersPath)
                    return method == "GET" || method == "HEAD"
                        ? new Reply(200, "application/json", (status ?? new KnStatusSnapshot()).PlayersJson(), false)
                        : new Reply(405, null, string.Empty, false);
                if (method != "POST") return new Reply(405, null, string.Empty, false);
                if (!TryReadPlayer(query, out ulong key)) return new Reply(400, "application/json", "{\"error\":\"give ?player=<key> from the player list\"}", false);
                if (status == null || !status.HasPlayer(key)) return new Reply(404, "application/json", "{\"error\":\"no such player online\"}", false);
                return new Reply(202, "application/json", "{\"kick\":true,\"player\":\"" + key.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\"}", false, key);
            }
            if (path == StopEndpointRules.StopPath)
            {
                StopEndpointRules.Decision d = stop.Decide(method, path, fromLoopback, givenKey, expectedKey, serverAlreadyStopping);
                return new Reply(d.Status, d.Body.Length > 0 ? "application/json" : null, d.Body, d.BeginStop);
            }
            if (path == "/" || path == "/status" || path == "/health")
            {
                if (method != "GET" && method != "HEAD") return new Reply(405, null, string.Empty, false);
                bool stopping = serverAlreadyStopping || (status != null && status.Stopping);
                if (path == "/health" && stopping) return new Reply(503, "application/json", "{\"ok\":false,\"stopping\":true}", false);
                if (path == "/health") return new Reply(200, "application/json", "{\"ok\":true}", false);
                return new Reply(200, "application/json", (status ?? new KnStatusSnapshot()).ToJson(), false);
            }
            return new Reply(404, null, string.Empty, false);
        }

        private static Reply Tools(string method, string path, string body)
        {
            ICommands tools = Commands;
            if (tools == null) return new Reply(503, "application/json", "{\"error\":\"GM tools are not ready yet.\"}", false);
            if (path == GmPath)
                return method == "GET" || method == "HEAD"
                    ? new Reply(200, "application/json", string.Empty, false, 0, tools.GmListJson, true)
                    : new Reply(405, null, string.Empty, false);
            if (method != "POST") return new Reply(405, null, string.Empty, false);
            Body b;
            try { b = string.IsNullOrWhiteSpace(body) ? new Body() : JsonUtility.FromJson<Body>(body) ?? new Body(); }
            catch (Exception) { return new Reply(400, "application/json", "{\"error\":\"The request is not JSON.\"}", false); }
            string line = (b.line ?? string.Empty).Trim(), name = b.name ?? string.Empty;
            switch (path)
            {
                case CommandPath:
                    if (line.Length == 0) return Answer("Type a command first.");
                    if (line.Length > 500) line = line.Substring(0, 500);
                    return new Reply(200, "application/json", string.Empty, false, 0, () => tools.Command(line, "Control page (this machine)"));
                case GmPath + "/add": return new Reply(200, "application/json", string.Empty, false, 0, () => tools.GmAdd(name));
                case GmPath + "/bind":
                    if (!ulong.TryParse(b.player, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out ulong key) || key == 0)
                        return Answer("Pick the player from the player list.");
                    return new Reply(200, "application/json", string.Empty, false, 0, () => tools.GmBind(name, key));
                case GmPath + "/unbind": return new Reply(200, "application/json", string.Empty, false, 0, () => tools.GmUnbind(name));
                case GmPath + "/remove": return new Reply(200, "application/json", string.Empty, false, 0, () => tools.GmRemove(name));
            }
            return new Reply(404, "application/json", "{\"error\":\"No such call.\"}", false);
        }

        /// <summary>The game's thread did not answer in time.</summary>
        public static Reply Reply504() => new Reply(504, "application/json", "{\"answer\":\"The server did not answer in time.\"}", false);

        /// <summary>A text answer as the page's JSON.</summary>
        public static Reply Answer(string text) => new Reply(200, "application/json", "{\"answer\":" + ControlPageRules.Json(text ?? string.Empty) + "}", false);

        /// <summary>Reads <c>player=&lt;decimal key&gt;</c> out of a query string. Zero is not a player.</summary>
        public static bool TryReadPlayer(string query, out ulong key)
        {
            key = 0;
            if (string.IsNullOrEmpty(query)) return false;
            foreach (string pair in query.Split('&'))
            {
                int eq = pair.IndexOf('=');
                if (eq > 0 && pair.Substring(0, eq) == "player")
                    return ulong.TryParse(pair.Substring(eq + 1), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out key) && key != 0;
            }
            return false;
        }

        public static string ReasonPhrase(int status)
        {
            switch (status)
            {
                case 200: return "OK";
                case 202: return "Accepted";
                case 400: return "Bad Request";
                case 401: return "Unauthorized";
                case 403: return "Forbidden";
                case 404: return "Not Found";
                case 405: return "Method Not Allowed";
                case 429: return "Too Many Requests";
                case 503: return "Service Unavailable";
                case 504: return "Gateway Timeout";
                default: return "Error";
            }
        }
    }


    /// <summary>
    /// Page request tracing to a plain file (only when the command line has <c>-knout &lt;folder&gt;</c>, as the probe players do): one line per stage
    /// with the thread id and the time, written with <c>File.AppendAllText</c> BEFORE any Unity call, so a request that stops is visible even if
    /// the game's own log is what blocks. Found necessary in the first real-socket run (M2c). No-op otherwise.
    /// </summary>
    public static class KnPageTrace
    {
        private static readonly object s_Lock = new object();
        private static readonly System.Diagnostics.Stopwatch s_Clock = System.Diagnostics.Stopwatch.StartNew();
        private static string s_Path;
        private static bool s_Looked;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() { s_Path = null; s_Looked = false; }

        public static void Write(string stage)
        {
            try
            {
                lock (s_Lock)
                {
                    if (!s_Looked)
                    {
                        s_Looked = true;
                        string[] args = Environment.GetCommandLineArgs();
                        for (int i = 0; i < args.Length - 1; i++)
                            if (args[i] == "-knout") { s_Path = System.IO.Path.Combine(args[i + 1], "page-trace.txt"); break; }
                    }
                    if (s_Path == null) return;
                    System.IO.File.AppendAllText(s_Path, $"{s_Clock.ElapsedMilliseconds,8} ms  thread {Thread.CurrentThread.ManagedThreadId,3}  {stage}" + Environment.NewLine);
                }
            }
            catch (Exception) { /* tracing must never break the page */ }
        }
    }

    /// <summary>
    /// The server's page: a TCP listener on the page port (game port + 1 in the host-layer contract) with its own threads, and
    /// the UDP "how are you?" answer the Servers window reads (<see cref="ServerStatusProtocol"/>) on the same number. Managed
    /// code outside the entity world: it reads <see cref="KnServerStatus.Current"/> and, for a good <c>POST /stop</c>, only sets
    /// <see cref="KnStopRequests"/>; the main thread does the rest. Never logs on a worker thread except through Unity's
    /// thread-safe <c>Debug.Log</c>.
    /// </summary>
    public sealed class KnServerPage : IDisposable
    {
        private const int MaxConcurrentRequests = 16;
        private const int UdpPerAddressPerSecond = 4;

        private readonly StopEndpointRules m_Stop = new StopEndpointRules();
        private readonly string m_Key;
        private TcpListener m_Tcp;
        private UdpClient m_Udp;
        private volatile bool m_Running;
        private int m_Active;
        private readonly Dictionary<IPAddress, (long second, int count)> m_Recent = new Dictionary<IPAddress, (long, int)>();

        /// <summary>The port the TCP listener is bound to (useful when 0 was asked for).</summary>
        public int TcpPort { get; private set; }
        /// <summary>The UDP port the status answer listens on (0 until it starts).</summary>
        public int UdpPort { get; private set; }

        /// <param name="stopKey">The save service key. Empty = <c>POST /stop</c> is refused with 403 (no key, no stop).</param>
        public KnServerPage(string stopKey)
        {
            m_Key = stopKey ?? string.Empty;
        }

        /// <summary>
        /// Starts listening. Port 0 picks a free one. False with <paramref name="problem"/> when the port cannot be opened. The game's UDP
        /// status question is answered on <paramref name="udpPort"/>, or on the page's own port when that is 0 (the test lane). The real
        /// server names 7778 (cutover, 12 Nov 2026): the router already forwards it, and the page's port is never opened to the internet.
        /// </summary>
        public bool Start(int port, bool listenOnAllAddresses, bool udpStatus, out string problem, int udpPort = 0)
        {
            problem = null;
            try
            {
                m_Tcp = new TcpListener(listenOnAllAddresses ? IPAddress.Any : IPAddress.Loopback, port);
                m_Tcp.Start();
                TcpPort = ((IPEndPoint)m_Tcp.LocalEndpoint).Port;
            }
            catch (Exception e)
            {
                problem = $"the page port {port} could not be opened ({e.Message})";
                m_Tcp = null;
                return false;
            }
            m_Running = true;
            // The page closes as the game starts to quit, before the engine shuts down: a request thread still starting when IL2CPP
            // aborts every managed thread on exit is aborted outside the try of its start, an unhandled C++ exception (0xe06d7363
            // from GameAssembly, a crash dump on every stop: the Server app asks for /status every 400 ms; 12 Nov 2026).
            Application.quitting -= Dispose;
            Application.quitting += Dispose;
            new Thread(AcceptLoop) { IsBackground = true, Name = "KN server page" }.Start();
            if (udpStatus && TcpPort > 0)
            {
                try
                {
                    UdpPort = udpPort > 0 ? udpPort : TcpPort;
                    m_Udp = new UdpClient(new IPEndPoint(IPAddress.Any, UdpPort));
                    new Thread(UdpLoop) { IsBackground = true, Name = "KN server status" }.Start();
                }
                catch (SocketException e)
                {
                    m_Udp = null;
                    problem = $"the UDP status answer on {UdpPort} could not start ({e.Message}); the page still works";
                }
            }
            return true;
        }

        public void Dispose()
        {
            Application.quitting -= Dispose;
            m_Running = false;
            try { m_Tcp?.Stop(); } catch (Exception) { /* closing anyway */ }
            UdpClient closing = m_Udp;
            m_Udp = null;
            try { closing?.Close(); } catch (Exception) { /* closing anyway */ }
        }

        // ------------------------------------------------------------------ TCP

        private void AcceptLoop()
        {
            Debug.Log("[KnServer] Page accept loop started on TCP " + TcpPort + ".");
            int failures = 0, accepted = 0;
            while (m_Running)
            {
                TcpClient client;
                try { client = m_Tcp.AcceptTcpClient(); }
                catch (Exception e)
                {
                    if (!m_Running) return;
                    // Never spin silently: say what failed (a few times), then slow down.
                    if (failures++ < 5) Debug.LogWarning("[KnServer] Page accept failed: " + e.GetType().Name + " " + e.Message);
                    Thread.Sleep(failures < 5 ? 50 : 1000);
                    continue;
                }
                if (!m_Running) { client.Close(); return; } // closing: no new request thread
                KnPageTrace.Write("accepted");
                if (accepted++ < 3) Debug.Log("[KnServer] Page request accepted.");
                if (Interlocked.Increment(ref m_Active) > MaxConcurrentRequests)
                {
                    Interlocked.Decrement(ref m_Active);
                    client.Close();
                    continue;
                }
                // A thread of its own per request (at most MaxConcurrentRequests): the shared thread pool of the game can be busy or
                // starved by blocking calls elsewhere (found in the first real-socket run: connections were accepted and never answered).
                new Thread(() =>
                {
                    try { Handle(client); }
                    finally { Interlocked.Decrement(ref m_Active); }
                }) { IsBackground = true, Name = "KN server page request" }.Start();
            }
        }

        private void Handle(TcpClient client)
        {
            KnPageTrace.Write("handle: start");
            using (client)
            {
                try
                {
                    client.ReceiveTimeout = 5000;
                    client.SendTimeout = 5000;
                    KnPageTrace.Write("handle: timeouts set");
                    NetworkStream stream = client.GetStream();
                    bool loopback = client.Client.RemoteEndPoint is IPEndPoint remote && IPAddress.IsLoopback(remote.Address);
                    KnPageTrace.Write("handle: before read, loopback=" + loopback);
                    ControlPageRules.Request request = ControlPageRules.Read(stream, out bool empty);
                    KnPageTrace.Write("handle: after read, empty=" + empty + ", request=" + (request == null ? "null" : request.Method + " " + request.Path));
                    if (empty) return;
                    KnPageTrace.Write("handle: before status");
                    KnStatusSnapshot status = KnServerStatus.Current;
                    KnPageTrace.Write("handle: status taken, null=" + (status == null));
                    bool stopping = KnStopRequests.Stopping;
                    KnPageTrace.Write("handle: stopping=" + stopping);
                    IPAddress remoteAddress = (client.Client.RemoteEndPoint as IPEndPoint)?.Address ?? IPAddress.Loopback;
                    KnPageRules.Reply reply = request == null
                        ? new KnPageRules.Reply(400, null, string.Empty, false)
                        : KnPageBrowser.Wants(request)
                        // the browser page (8 Nov 2026 port of ServerControlPage): files, password login, session buttons
                        ? KnPageBrowser.Route(KnPageBrowser.Shared, KnPageBrowser.Host, request, remoteAddress, status, m_Stop, stopping)
                        : KnPageRules.Handle(request.Method, request.Path, loopback, request.Header(StopEndpointRules.KeyHeader), m_Key,
                            status, m_Stop, stopping, request.Query, request.Body);
                    if (reply.Work != null) reply = KnPageWork.Run(reply);   // waits for the game's thread (a command, a GM button)
                    KnPageTrace.Write("handle: reply decided, status " + reply.Status + ", body " + reply.Body.Length + " chars");
                    Write(stream, reply, request != null && request.Method == "HEAD");
                    KnPageTrace.Write("handle: written and flushed");
                    if (reply.KickKey != 0) KnKickRequests.Request(reply.KickKey); // the answer has gone out; the main thread closes the connection
                    if (reply.BeginStop)
                    {
                        KnPageTrace.Write("handle: stop requested");
                        Debug.Log("[KnServer] Stop requested through the stop endpoint.");
                        KnStopRequests.Request(KnStopSource.Http); // the answer has gone out; the main thread saves and quits
                    }
                }
                catch (Exception e)
                {
                    KnPageTrace.Write("handle: exception " + e.GetType().Name + ": " + e.Message);
                    if (!ControlPageRules.IsClientHangUp(e)) Debug.LogWarning("[KnServer] Page request failed: " + e.GetType().Name);
                }
            }
            KnPageTrace.Write("handle: closed");
        }

        /// <summary>
        /// The status line and headers of a reply, ending with the blank line. Includes <see cref="KnPageRules.Reply.ExtraHeaders"/>: the
        /// login's <c>Set-Cookie</c> and the page's security headers (8 Nov 2026: they were never written, so the browser kept no
        /// cookie, the page's first call after login got 401 and it fell back to the sign-in screen).
        /// </summary>
        public static string HeadOf(KnPageRules.Reply reply, int bodyLength) =>
            "HTTP/1.1 " + reply.Status + " " + KnPageRules.ReasonPhrase(reply.Status) + "\r\n" +
            (reply.ContentType != null ? "Content-Type: " + reply.ContentType + "\r\n" : string.Empty) +
            "Content-Length: " + bodyLength + "\r\n" +
            (reply.Status == 405 ? "Allow: GET, HEAD, POST\r\n" : string.Empty) +
            (reply.ExtraHeaders ?? string.Empty) +
            "Cache-Control: no-store\r\n" +
            "Connection: close\r\n\r\n";

        private static void Write(NetworkStream stream, KnPageRules.Reply reply, bool headOnly)
        {
            byte[] body = reply.Bytes ?? Encoding.UTF8.GetBytes(reply.Body);
            byte[] headBytes = Encoding.ASCII.GetBytes(HeadOf(reply, body.Length));
            stream.Write(headBytes, 0, headBytes.Length);
            if (body.Length > 0 && !headOnly) stream.Write(body, 0, body.Length);
            stream.Flush();
        }

        // ------------------------------------------------------------------ UDP "how are you?"

        private void UdpLoop()
        {
            var from = new IPEndPoint(IPAddress.Any, 0);
            while (m_Udp != null)
            {
                try
                {
                    UdpClient socket = m_Udp;
                    if (socket == null) return;
                    byte[] data = socket.Receive(ref from);
                    if (!ServerStatusProtocol.IsRequest(data, data.Length) || !Allowed(from.Address)) continue;
                    KnStatusSnapshot s = KnServerStatus.Current;
                    byte[] answer = ServerStatusProtocol.Reply(new ServerStatusProtocol.Status
                    {
                        name = s.Name,
                        build = s.Build,
                        players = s.Players,
                        max = s.MaxPlayers,
                        relay = string.Empty,
                    });
                    socket.Send(answer, answer.Length, from);
                }
                catch (SocketException) { if (m_Udp == null) return; }
                catch (ObjectDisposedException) { return; }
            }
        }

        private bool Allowed(IPAddress address)
        {
            long second = DateTime.UtcNow.Ticks / TimeSpan.TicksPerSecond;
            if (m_Recent.Count > 4096) m_Recent.Clear();
            m_Recent.TryGetValue(address, out (long second, int count) seen);
            int count = seen.second == second ? seen.count + 1 : 1;
            m_Recent[address] = (second, count);
            return count <= UdpPerAddressPerSecond;
        }
    }
}
#endif
