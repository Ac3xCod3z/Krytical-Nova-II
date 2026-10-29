using System;

namespace KryticalNova.Core
{
    /// <summary>What this process is in the network session. Plain C#: no netcode type appears in Core.</summary>
    public enum NetMode
    {
        /// <summary>No session is running (the title, character select, a game with no network).</summary>
        Offline,
        /// <summary>Connected to a server somewhere else.</summary>
        Client,
        /// <summary>A server with no player on it (a dedicated server).</summary>
        Server,
        /// <summary>A server that also has a local player.</summary>
        Host,
    }

    /// <summary>
    /// What the netcode of the day can tell about the running session. Networking implements it over whatever carries the
    /// game (Netcode for GameObjects today) and hands it to <see cref="NetSession.Use"/>; Core, UI and GameFeel read the
    /// <see cref="NetSession"/> and never name a netcode type.
    /// </summary>
    public interface INetSessionSource
    {
        /// <summary>A session is listening or connected (not still starting, not shut down).</summary>
        bool IsRunning { get; }
        bool IsServer { get; }
        bool IsClient { get; }
        bool IsHost { get; }
        bool IsConnectedClient { get; }
        /// <summary>The session is closing down.</summary>
        bool IsShuttingDown { get; }
        /// <summary>Players connected, counting a host's own.</summary>
        int PlayerCount { get; }
        /// <summary>Objects the session keeps in step; -1 when it cannot say.</summary>
        int NetworkedObjectCount { get; }
        /// <summary>Ticks a second of the session; 0 when it has none.</summary>
        int TickRate { get; }
        /// <summary>Round trip to the server, for a pure client.</summary>
        bool TryGetRttMs(out long milliseconds);
    }

    /// <summary>
    /// Whether a session is running and what this process is in it. Answers "offline" until Networking registers its
    /// source, so Editor tests and a game with no network read it safely.
    /// </summary>
    public static class NetSession
    {
        private static INetSessionSource source;

        // Enter Play Mode runs without a domain reload, so statics must be reset by hand.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => source = null;

        /// <summary>Networking hands over what answers for the running session (null puts the offline answers back).</summary>
        public static void Use(INetSessionSource newSource) => source = newSource;

        /// <summary>True once a source has been handed over.</summary>
        public static bool HasSource => source != null;

        public static bool IsRunning => source != null && source.IsRunning;
        public static bool IsServer => source != null && source.IsServer;
        public static bool IsClient => source != null && source.IsClient;
        public static bool IsHost => source != null && source.IsHost;
        public static bool IsConnectedClient => source != null && source.IsConnectedClient;
        public static bool IsShuttingDown => source != null && source.IsShuttingDown;

        /// <summary>A server with nobody playing on it.</summary>
        public static bool IsDedicatedServer => IsRunning && IsServer && !IsHost;

        /// <summary>A pure client: connected to a server that runs elsewhere.</summary>
        public static bool IsRemoteClient => IsClient && !IsServer;

        /// <summary>True where the game is decided: a server, a host, or a game with no session at all.</summary>
        public static bool DecidesGame => !IsRunning || IsServer;

        public static NetMode Mode => !IsRunning ? NetMode.Offline : IsHost ? NetMode.Host : IsServer ? NetMode.Server : NetMode.Client;

        /// <summary>"none", "host", "server" or "client" (the words of the performance log).</summary>
        public static string ModeWord
        {
            get
            {
                switch (Mode)
                {
                    case NetMode.Host: return "host";
                    case NetMode.Server: return "server";
                    case NetMode.Client: return "client";
                    default: return "none";
                }
            }
        }

        /// <summary>Players in the session: the server counts all connected, a client counts itself, offline none.</summary>
        public static int PlayerCount => !IsRunning ? 0 : IsServer ? source.PlayerCount : 1;

        public static int NetworkedObjectCount => IsRunning ? source.NetworkedObjectCount : 0;

        public static int TickRate => IsRunning ? source.TickRate : 0;

        public static bool TryGetRttMs(out long milliseconds)
        {
            milliseconds = 0;
            return IsRunning && source.TryGetRttMs(out milliseconds);
        }
    }
}
