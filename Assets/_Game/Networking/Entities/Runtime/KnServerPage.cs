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

            public Reply(int status, string contentType, string body, bool beginStop)
            {
                Status = status;
                ContentType = contentType;
                Body = body ?? string.Empty;
                BeginStop = beginStop;
            }
        }

        public static Reply Handle(string method, string path, bool fromLoopback, string givenKey, string expectedKey,
            KnStatusSnapshot status, StopEndpointRules stop, bool serverAlreadyStopping)
        {
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

        public static string ReasonPhrase(int status)
        {
            switch (status)
            {
                case 200: return "OK";
                case 202: return "Accepted";
                case 400: return "Bad Request";
                case 403: return "Forbidden";
                case 404: return "Not Found";
                case 405: return "Method Not Allowed";
                case 503: return "Service Unavailable";
                default: return "Error";
            }
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

        /// <param name="stopKey">The save service key. Empty = <c>POST /stop</c> is refused with 403 (no key, no stop).</param>
        public KnServerPage(string stopKey)
        {
            m_Key = stopKey ?? string.Empty;
        }

        /// <summary>Starts listening. Port 0 picks a free one. False with <paramref name="problem"/> when the port cannot be opened.</summary>
        public bool Start(int port, bool listenOnAllAddresses, bool udpStatus, out string problem)
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
            new Thread(AcceptLoop) { IsBackground = true, Name = "KN server page" }.Start();
            if (udpStatus && TcpPort > 0)
            {
                try
                {
                    m_Udp = new UdpClient(new IPEndPoint(IPAddress.Any, TcpPort));
                    new Thread(UdpLoop) { IsBackground = true, Name = "KN server status" }.Start();
                }
                catch (SocketException e)
                {
                    m_Udp = null;
                    problem = $"the UDP status answer on {TcpPort} could not start ({e.Message}); the page still works";
                }
            }
            return true;
        }

        public void Dispose()
        {
            m_Running = false;
            try { m_Tcp?.Stop(); } catch (Exception) { /* closing anyway */ }
            UdpClient closing = m_Udp;
            m_Udp = null;
            try { closing?.Close(); } catch (Exception) { /* closing anyway */ }
        }

        // ------------------------------------------------------------------ TCP

        private void AcceptLoop()
        {
            while (m_Running)
            {
                TcpClient client;
                try { client = m_Tcp.AcceptTcpClient(); }
                catch (Exception) { if (!m_Running) return; continue; }
                if (Interlocked.Increment(ref m_Active) > MaxConcurrentRequests)
                {
                    Interlocked.Decrement(ref m_Active);
                    client.Close();
                    continue;
                }
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try { Handle(client); }
                    finally { Interlocked.Decrement(ref m_Active); }
                });
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
                    bool loopback = client.Client.RemoteEndPoint is IPEndPoint remote && IPAddress.IsLoopback(remote.Address);
                    ControlPageRules.Request request = ControlPageRules.Read(stream, out bool empty);
                    if (empty) return;
                    KnPageRules.Reply reply = request == null
                        ? new KnPageRules.Reply(400, null, string.Empty, false)
                        : KnPageRules.Handle(request.Method, request.Path, loopback, request.Header(StopEndpointRules.KeyHeader), m_Key,
                            KnServerStatus.Current, m_Stop, KnStopRequests.Stopping);
                    Write(stream, reply, request != null && request.Method == "HEAD");
                    if (reply.BeginStop)
                    {
                        Debug.Log("[KnServer] Stop requested through the stop endpoint.");
                        KnStopRequests.Request(KnStopSource.Http); // the answer has gone out; the main thread saves and quits
                    }
                }
                catch (Exception e)
                {
                    if (!ControlPageRules.IsClientHangUp(e)) Debug.LogWarning("[KnServer] Page request failed: " + e.GetType().Name);
                }
            }
        }

        private static void Write(NetworkStream stream, KnPageRules.Reply reply, bool headOnly)
        {
            byte[] body = Encoding.UTF8.GetBytes(reply.Body);
            string head = "HTTP/1.1 " + reply.Status + " " + KnPageRules.ReasonPhrase(reply.Status) + "\r\n" +
                          (reply.ContentType != null ? "Content-Type: " + reply.ContentType + "\r\n" : string.Empty) +
                          "Content-Length: " + body.Length + "\r\n" +
                          (reply.Status == 405 ? "Allow: GET, HEAD, POST\r\n" : string.Empty) +
                          "Cache-Control: no-store\r\n" +
                          "Connection: close\r\n\r\n";
            byte[] headBytes = Encoding.ASCII.GetBytes(head);
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
