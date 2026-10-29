#if UNITY_SERVER
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace KryticalNova.Networking
{
    /// <summary>
    /// <c>POST /stop</c> on the status port (game port + 1, TCP; the status answers use the same number on UDP), for a
    /// service manager that must stop the server without losing a save (Linux cloud servers plan, item 16: a Linux Unity
    /// player ends on SIGTERM without saving). Listens on 127.0.0.1 only, needs the header <c>X-KN-Key</c> equal to the save
    /// service key, answers before the server stops, then runs <see cref="ServerLifetime.StopNow"/> (the control page's
    /// Stop path: save everyone, back up, leave the world, quit with exit code 0). Rules: <see cref="StopEndpointRules"/>.
    /// Needs a save service key; without one it is off. Only in a server build.
    /// </summary>
    public sealed class ServerStopEndpoint : MonoBehaviour
    {
        private static ServerStopEndpoint instance;

        private readonly StopEndpointRules rules = new StopEndpointRules();
        private TcpListener listener;
        private volatile bool running;
        private int stopRequested; // set by a thread of the listener, read by the game's
        private string key = string.Empty; // never logged

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => instance = null;

        /// <summary>Starts listening, once the server listens.</summary>
        public static void Begin(ServerLaunchOptions options)
        {
            if (instance != null) return;
            string keyText = string.Empty;
            try
            {
                if (options.UsesSaveService && System.IO.File.Exists(options.SaveServiceKeyFile))
                    keyText = System.IO.File.ReadAllText(options.SaveServiceKeyFile).Trim();
            }
            catch (Exception) { /* no key: the endpoint stays off */ }
            if (keyText.Length == 0)
            {
                Debug.Log("[Server] The stop endpoint is off: this server has no save service key (saveService / saveServiceKeyFile in server-settings.json).");
                return;
            }
            var listening = new TcpListener(IPAddress.Loopback, options.StatusPort);
            try { listening.Start(); }
            catch (Exception e)
            {
                Debug.LogWarning($"[Server] The stop endpoint could not open 127.0.0.1:{options.StatusPort} ({e.Message}): stopping through it is not possible.");
                return;
            }
            var host = new GameObject("Server Stop Endpoint");
            DontDestroyOnLoad(host);
            instance = host.AddComponent<ServerStopEndpoint>();
            instance.key = keyText;
            instance.listener = listening;
            instance.running = true;
            new Thread(instance.AcceptLoop) { IsBackground = true, Name = "Server stop endpoint" }.Start();
            Debug.Log($"[Server] Stop endpoint: POST http://127.0.0.1:{options.StatusPort}/stop (this machine only, needs the save service key).");
        }

        private void Update()
        {
            if (Interlocked.Exchange(ref stopRequested, 0) == 1)
                ServerLifetime.StopNow("The server was stopped by its service manager.");
        }

        private void OnDestroy()
        {
            running = false;
            try { listener?.Stop(); } catch (Exception) { /* closing anyway */ }
            if (instance == this) instance = null;
        }

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
                    bool loopback = client.Client.RemoteEndPoint is IPEndPoint remote && IPAddress.IsLoopback(remote.Address);
                    if (!loopback) { Reply(stream, new StopEndpointRules.Decision(403, string.Empty, false)); return; }
                    ControlPageRules.Request request = ControlPageRules.Read(stream, out bool empty);
                    if (empty) return;
                    if (request == null) { Reply(stream, new StopEndpointRules.Decision(400, string.Empty, false)); return; }
                    StopEndpointRules.Decision decision = rules.Decide(request.Method, request.Path, loopback,
                        request.Header(StopEndpointRules.KeyHeader), key, ServerLifetime.Stopping);
                    Reply(stream, decision); // the answer goes out first; the stop starts on the game's thread after it
                    if (decision.BeginStop)
                    {
                        Debug.Log("[Server] Stop requested through the stop endpoint.");
                        Interlocked.Exchange(ref stopRequested, 1);
                    }
                }
                catch (Exception e)
                {
                    if (!ControlPageRules.IsClientHangUp(e)) Debug.LogWarning("[Server] Stop endpoint request failed: " + e.GetType().Name);
                }
            }
        }

        private static void Reply(NetworkStream stream, StopEndpointRules.Decision decision)
        {
            string reason = decision.Status == 200 ? "OK" : decision.Status == 202 ? "Accepted" : decision.Status == 405 ? "Method Not Allowed"
                : decision.Status == 404 ? "Not Found" : decision.Status == 400 ? "Bad Request" : "Forbidden";
            byte[] body = Encoding.UTF8.GetBytes(decision.Body ?? string.Empty);
            string head = "HTTP/1.1 " + decision.Status + " " + reason + "\r\n" +
                          (body.Length > 0 ? "Content-Type: application/json\r\n" : string.Empty) +
                          "Content-Length: " + body.Length + "\r\n" +
                          (decision.Status == 405 ? "Allow: POST\r\n" : string.Empty) +
                          "Connection: close\r\n\r\n";
            byte[] headBytes = Encoding.ASCII.GetBytes(head);
            stream.Write(headBytes, 0, headBytes.Length);
            if (body.Length > 0) stream.Write(body, 0, body.Length);
            stream.Flush();
        }
    }
}
#endif
