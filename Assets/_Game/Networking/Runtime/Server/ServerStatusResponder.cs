#if UNITY_SERVER
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Unity.Netcode;
using UnityEngine;

namespace KryticalNova.Networking
{
    /// <summary>
    /// Answers "how are you?" on the status port (game port + 1 by default) for the Servers window: the server's name,
    /// build, players and limit (<see cref="ServerStatusProtocol"/>). The answer is never longer than the question,
    /// and one address is answered at most four times a second. Only in a server build.
    /// </summary>
    public sealed class ServerStatusResponder : MonoBehaviour
    {
        private const int PerAddressPerSecond = 4;

        private static ServerStatusResponder instance;
        private ServerLaunchOptions options;
        private UdpClient socket;
        private Thread listener;
        private volatile byte[] reply;
        private float nextRefresh;
        private readonly Dictionary<IPAddress, (long second, int count)> recent = new Dictionary<IPAddress, (long, int)>();

        /// <summary>Starts answering, once the server listens.</summary>
        public static void Begin(ServerLaunchOptions options)
        {
            if (instance != null) return;
            var host = new GameObject("Server Status Responder");
            DontDestroyOnLoad(host);
            instance = host.AddComponent<ServerStatusResponder>();
            instance.options = options;
            instance.Refresh();
            try
            {
                instance.socket = new UdpClient(new IPEndPoint(IPAddress.Any, options.StatusPort));
                instance.listener = new Thread(instance.Listen) { IsBackground = true, Name = "Server status" };
                instance.listener.Start();
                Debug.Log($"[Server] Status answers on UDP port {options.StatusPort} (the Servers window reads players and build there).");
            }
            catch (SocketException e)
            {
                Debug.LogWarning($"[Server] The status port {options.StatusPort} could not be opened ({e.Message}): the Servers window will show this server as unknown, joining still works.");
            }
        }

        private void Update()
        {
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + 1f;
            Refresh();
        }

        // Built on the game's thread: the player count is Netcode's.
        private void Refresh()
        {
            NetworkManager network = NetworkManager.Singleton;
            int players = network != null && network.IsServer ? network.ConnectedClientsIds.Count : 0;
            reply = ServerStatusProtocol.Reply(new ServerStatusProtocol.Status
            {
                name = options.serverName,
                build = GameBuild.Version,
                players = players,
                max = options.maxPlayers,
                relay = ServerReach.RelayCode,
            });
        }

        private void Listen()
        {
            var from = new IPEndPoint(IPAddress.Any, 0);
            while (socket != null)
            {
                try
                {
                    byte[] data = socket.Receive(ref from);
                    if (!ServerStatusProtocol.IsRequest(data, data.Length) || !Allowed(from.Address)) continue;
                    byte[] answer = reply;
                    if (answer != null) socket.Send(answer, answer.Length, from);
                }
                catch (SocketException)
                {
                    // a probe that went away, or the socket closing: keep going until it is gone
                    if (socket == null) return;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
            }
        }

        private bool Allowed(IPAddress address)
        {
            long second = DateTime.UtcNow.Ticks / TimeSpan.TicksPerSecond;
            if (recent.Count > 4096) recent.Clear();
            recent.TryGetValue(address, out (long second, int count) seen);
            int count = seen.second == second ? seen.count + 1 : 1;
            recent[address] = (second, count);
            return count <= PerAddressPerSecond;
        }

        private void OnDestroy()
        {
            UdpClient closing = socket;
            socket = null;
            closing?.Close();
            if (instance == this) instance = null;
        }
    }
}
#endif
