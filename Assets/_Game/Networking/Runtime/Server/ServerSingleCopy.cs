#if UNITY_SERVER || UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using UnityEngine;

namespace KryticalNova.Networking
{
    /// <summary>
    /// One server per set of ports on a PC. Before anything else the server checks that its game port (UDP, Direct mode)
    /// and its control page port (TCP) are free. When another copy holds them (Start Server.bat opened twice, 17 Oct
    /// 2026) this copy says so in its window and quits with <see cref="AlreadyRunningExitCode"/>, before it publishes
    /// the server list, makes a backup or loads the world, so the copy that runs is never disturbed. The rules are
    /// apart from the sockets so they can be tested; the check itself runs only in a server build.
    /// </summary>
    public static class ServerSingleCopy
    {
        /// <summary>The exit code of a copy that found another running. Start Server.bat restarts only on 3.</summary>
        public const int AlreadyRunningExitCode = 4;

        /// <summary>A port the server needs on this PC.</summary>
        public readonly struct NeededPort
        {
            public readonly string What;
            public readonly int Number;
            public readonly bool Tcp;

            public NeededPort(string what, int number, bool tcp)
            {
                What = what;
                Number = number;
                Tcp = tcp;
            }

            public override string ToString() => $"{(Tcp ? "TCP" : "UDP")} {Number} ({What})";
        }

        /// <summary>
        /// The ports another copy would hold: the game port in Direct mode (in Relay mode players come through Unity
        /// Relay, not to this PC's port) and the control page's port unless the page is off. The status port is left
        /// out: when it cannot be opened the server still runs, as before.
        /// </summary>
        public static List<NeededPort> PortsOf(ServerLaunchOptions options)
        {
            var ports = new List<NeededPort>(2);
            if (!options.UsesRelay) ports.Add(new NeededPort("game", options.port, tcp: false));
            if (options.pagePort > 0) ports.Add(new NeededPort("control page", options.pagePort, tcp: true));
            return ports;
        }

        /// <summary>The needed ports that are taken, by the checks given (the real sockets, or a test's).</summary>
        public static List<NeededPort> Taken(ServerLaunchOptions options, Func<int, bool> udpTaken, Func<int, bool> tcpTaken)
        {
            var taken = new List<NeededPort>(2);
            foreach (NeededPort port in PortsOf(options))
                if (port.Tcp ? tcpTaken(port.Number) : udpTaken(port.Number))
                    taken.Add(port);
            return taken;
        }

        /// <summary>What the window says when ports are taken (two lines), or null when none is.</summary>
        public static string Message(ServerLaunchOptions options, IReadOnlyList<NeededPort> taken, string address)
        {
            if (taken == null || taken.Count == 0) return null;
            string page = options.pagePort > 0 ? $" (control page http://{address}:{options.pagePort})" : string.Empty;
            return $"[Server] A Krytical Nova server is already running on this PC{page}. This copy stops.\n" +
                   $"[Server] In use: {string.Join(", ", taken)}. Use the server that runs, or stop it first (its window or its " +
                   $"control page). A second server on this PC needs ports of its own, for example -port {options.port + 10} " +
                   $"-pagePort {(options.pagePort > 0 ? options.pagePort + 1 : 8091)}.";
        }

#if UNITY_SERVER
        /// <summary>True when another copy runs and this one is quitting: nothing else of the server starts.</summary>
        public static bool AnotherCopyRuns { get; private set; }

        // Before every other part of the server (they boot at BeforeSceneLoad or later).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Check()
        {
            ServerLaunchOptions options = ServerLaunchOptions.LoadForThisServer();
            List<NeededPort> taken = Taken(options, UdpTaken, TcpTaken);
            if (taken.Count == 0) return;
            AnotherCopyRuns = true;
            List<string> addresses = ControlPageRules.HomeAddresses();
            Debug.Log(Message(options, taken, addresses[0]));
            Application.Quit(AlreadyRunningExitCode);
        }

        // Asked again once the first scene is in, should the first ask have come before the player could take it.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void QuitAgain()
        {
            if (AnotherCopyRuns) Application.Quit(AlreadyRunningExitCode);
        }

        // Another program on this PC is bound to the UDP port.
        private static bool UdpTaken(int port)
        {
            try
            {
                using (new UdpClient(new IPEndPoint(IPAddress.Any, port))) return false;
            }
            catch (SocketException)
            {
                return true;
            }
        }

        // Something listens on the TCP port: it cannot be opened, and it answers on this PC. Only refusing the open is
        // not enough: connections of a server that just restarted may linger on the port for a while without anyone
        // listening, and the page then warns as before instead of stopping the server.
        private static bool TcpTaken(int port)
        {
            try
            {
                var listener = new TcpListener(IPAddress.Any, port);
                listener.Start();
                listener.Stop();
                return false;
            }
            catch (SocketException)
            {
                // taken: see whether something answers there
            }
            try
            {
                using (var client = new TcpClient())
                    return client.ConnectAsync(IPAddress.Loopback, port).Wait(1000) && client.Connected;
            }
            catch (Exception)
            {
                return false;
            }
        }
#endif
    }
}
#endif
