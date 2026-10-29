using UnityEngine;

namespace KryticalNova.Core
{
    /// <summary>
    /// What a body (or this whole process) is in the network, in the words rule code needs, without any Netcode type
    /// (DOTS migration, seam NetRole). <see cref="Offline"/> is the single-player scene avatar or a process with no session;
    /// <see cref="ServerBody"/> is where the rules run and may change state (a dedicated server, or the host); and
    /// <see cref="ClientView"/> only presents what the server decided. The future ECS build keeps the same three meanings.
    /// </summary>
    public enum NetRoleKind : byte
    {
        Offline = 0,
        ServerBody = 1,
        ClientView = 2,
    }

    /// <summary>What the network layer answers for <see cref="NetRole"/>. Networking implements and installs it.</summary>
    public interface INetRoleAdapter
    {
        /// <summary>This process: Offline (no session listening), ServerBody (server or host) or ClientView (client only).</summary>
        NetRoleKind Process { get; }

        /// <summary>This object: Offline when it is not spawned, else ServerBody on the server and ClientView elsewhere.</summary>
        NetRoleKind Of(Component networked);

        /// <summary>Spawned and owned by this process.</summary>
        bool IsOwner(Component networked);

        /// <summary>Owned by the server's own player (the host's body).</summary>
        bool IsOwnedByServer(Component networked);

        /// <summary>Spawned and owned by a connected client that is not the server's own.</summary>
        bool IsRemoteOwned(Component networked);

        /// <summary>The connection id of the owner (0 when not networked).</summary>
        ulong OwnerId(Component networked);

        /// <summary>Drops the connection of this object's owner, telling it why.</summary>
        void DisconnectOwner(Component networked, string reason);
    }

    /// <summary>
    /// The one place rule code asks "may I change this?" and "is this mine?" instead of reading <c>IsServer</c>,
    /// <c>IsOwner</c>, <c>IsSpawned</c> and the like off the network object. With no adapter installed (EditMode tests,
    /// tools) every answer is the offline one, which is what the game does without a session.
    /// </summary>
    public static class NetRole
    {
        private static INetRoleAdapter adapter;

        /// <summary>The installed adapter, or null.</summary>
        public static INetRoleAdapter Adapter => adapter;

        // Enter Play Mode runs without a domain reload, so statics must be reset by hand. Networking installs its
        // adapter later (BeforeSceneLoad), so a reset never leaves a stale one.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => adapter = null;

        /// <summary>Called by the network layer at startup; null removes it.</summary>
        public static void Install(INetRoleAdapter value) => adapter = value;

        // ------------------------------------------------------------------ this process

        public static NetRoleKind Process => adapter != null ? adapter.Process : NetRoleKind.Offline;

        /// <summary>No session is listening: single player, the title, tools.</summary>
        public static bool IsOffline => Process == NetRoleKind.Offline;

        /// <summary>Rules may change state here: offline, or the server (host included).</summary>
        public static bool IsServerAuthority => Process != NetRoleKind.ClientView;

        /// <summary>A client of someone else's server: only presents what the server decided.</summary>
        public static bool IsClientView => Process == NetRoleKind.ClientView;

        // ------------------------------------------------------------------ one object

        public static NetRoleKind Of(Component networked) => adapter != null && networked != null ? adapter.Of(networked) : NetRoleKind.Offline;

        /// <summary>Spawned in a session (the object has a network identity).</summary>
        public static bool IsNetworked(Component networked) => Of(networked) != NetRoleKind.Offline;

        /// <summary>Not spawned: the single-player avatar.</summary>
        public static bool IsUnspawned(Component networked) => Of(networked) == NetRoleKind.Offline;

        /// <summary>Spawned on the server side (a dedicated server's or the host's copy).</summary>
        public static bool IsServerBody(Component networked) => Of(networked) == NetRoleKind.ServerBody;

        /// <summary>Spawned on a client that is not the server.</summary>
        public static bool IsClientView(Component networked) => Of(networked) == NetRoleKind.ClientView;

        /// <summary>This object's rules may change its state: not spawned (offline) or the server's copy.</summary>
        public static bool MayMutate(Component networked) => Of(networked) != NetRoleKind.ClientView;

        /// <summary>Spawned and owned by this process.</summary>
        public static bool IsOwner(Component networked) => adapter != null && networked != null && adapter.IsOwner(networked);

        /// <summary>The body this machine controls: the owned one when spawned, the offline avatar when no session is listening.</summary>
        public static bool IsLocalOwner(Component networked) => IsNetworked(networked) ? IsOwner(networked) : IsOffline;

        /// <summary>The same question for a body the world knows only as <see cref="IPlayerBody"/>.</summary>
        public static bool IsLocalOwner(IPlayerBody body) => body != null && body.IsLocal;

        public static bool IsOwnedByServer(Component networked) => adapter != null && networked != null && adapter.IsOwnedByServer(networked);

        /// <summary>Spawned and owned by a connected client other than the server's own player.</summary>
        public static bool IsRemoteOwned(Component networked) => adapter != null && networked != null && adapter.IsRemoteOwned(networked);

        public static ulong OwnerId(Component networked) => adapter != null && networked != null ? adapter.OwnerId(networked) : 0UL;

        /// <summary>Drops the owner's connection with a reason it can read; does nothing offline.</summary>
        public static void DisconnectOwner(Component networked, string reason)
        {
            if (adapter != null && networked != null) adapter.DisconnectOwner(networked, reason);
        }
    }
}
