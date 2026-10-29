using System;
using Unity.Entities;
using UnityEngine;

namespace KryticalNova.Networking.Entities
{
    /// <summary>
    /// The dedicated server's start-up, for Linux and Windows alike (host-layer contract, plan section 2):
    /// <c>KryticalNovaServer -batchmode -nographics -role map -map &lt;name&gt; -port &lt;udp&gt; -pageport &lt;port+1&gt;</c>, with
    /// <c>server-settings.json</c> keys <c>saveService</c>, <c>saveServiceKeyFile</c>, <c>certificateFile</c>, <c>privateKeyFile</c>
    /// (<see cref="KnServerSettings"/>). Given the server-only world <see cref="KnBootstrap"/> made, it sets the join rule, tells the
    /// status system what to report, listens, opens the page (status and loopback <c>POST /stop</c>) and hooks the stop signals.
    /// The pieces that decide something are static and plain so a test can call them without sockets.
    /// </summary>
    public static class KnServerEntry
    {
        /// <summary>The options of the join rule for a server started with <paramref name="settings"/>.</summary>
        public static KnJoinCheckOptions JoinOptions(KnServerSettings settings, string serverBuild, bool isEditor)
        {
            return new KnJoinCheckOptions
            {
                ServerBuild = serverBuild ?? string.Empty,
                ServerIsEditor = isEditor,
                MaxPlayers = settings.maxPlayers,
                RequireProof = !isEditor,   // a real server lets in only proven players; the Editor hosting a test takes anyone
                AllowDevBots = settings.devBots,
            };
        }

        /// <summary>The real join rule: Unity sign-in proof, bans from Core's slot, accept-all tickets until a world service exists.</summary>
        public static KnJoinCheck BuildJoinCheck(KnServerSettings settings, string serverBuild, bool isEditor, string projectId, Func<int> online)
        {
            KnJoinCheckOptions options = JoinOptions(settings, serverBuild, isEditor);
            return new KnJoinCheck(options, new UnityAccountProof(projectId), new AcceptAllTickets(), online,
                log: line => Debug.Log("[KnServer] " + line));
        }

        /// <summary>The project the tokens must belong to: the settings' own, else this build's.</summary>
        public static string ProjectId(KnServerSettings settings) =>
            string.IsNullOrWhiteSpace(settings.cloudProjectId) ? Application.cloudProjectId : settings.cloudProjectId;

        /// <summary>Starts the server on <paramref name="server"/>. Logs what it did.</summary>
        public static void Start(World server, KnServerSettings settings)
        {
            KnNetDefaults.Apply(server);
            KnStopRequests.Reset();

            var assembler = server.GetExistingSystemManaged<KnApprovalAssembler>();
            assembler.JoinCheck = BuildJoinCheck(settings, GameBuild.Version, Application.isEditor, ProjectId(settings), () => assembler.ApprovedCount);

            KnServerStatus.Name = settings.serverName;
            KnServerStatus.Build = GameBuild.Version;
            KnServerStatus.Map = settings.map;
            KnServerStatus.Role = settings.role;
            KnServerStatus.MaxPlayers = settings.maxPlayers;

#if KN_DEV_BOTS || UNITY_EDITOR
            KnDevBots.Enabled = settings.devBots;
            if (settings.devBots) Debug.LogWarning("[KnServer] Development bots are ON: bot tokens are taken without a Unity sign-in. Never on a public server.");
#else
            if (settings.devBots) Debug.LogWarning("[KnServer] devBots is set but this build has no bots compiled in: ignored.");
#endif
            if (settings.WantsEncryption)
                Debug.LogError("[KnServer] A certificate and private key are set but the encrypted transport is not wired in yet: this server is running WITHOUT encryption.");

            KnBootstrap.Listen(server, (ushort)settings.port);
            Debug.Log($"[KnServer] listening on UDP {settings.port}, role {settings.role}, map {settings.map}, max {settings.maxPlayers} players, " +
                      (settings.UsesSaveService ? "saving through the save service." : "saving to files."));

#if UNITY_SERVER || UNITY_EDITOR
            StartPage(settings);
#endif
            if (!Application.isEditor) Debug.Log("[KnServer] " + KnSignals.Install());
        }

#if UNITY_SERVER || UNITY_EDITOR
        private static KnServerPage s_Page;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_Page?.Dispose();
            s_Page = null;
        }

        private static void StartPage(KnServerSettings settings)
        {
            if (settings.PagePort <= 0)
            {
                Debug.Log("[KnServer] The status page is off (pagePort 0).");
                return;
            }
            string key = settings.ReadStopKey();
            s_Page?.Dispose();
            s_Page = new KnServerPage(key);
            if (!s_Page.Start(settings.PagePort, listenOnAllAddresses: true, udpStatus: true, out string problem))
            {
                Debug.LogWarning("[KnServer] " + problem + ": the status and stop endpoint are off, joining still works.");
                s_Page = null;
                return;
            }
            if (problem != null) Debug.LogWarning("[KnServer] " + problem);
            Debug.Log($"[KnServer] Page: GET http://this-machine:{s_Page.TcpPort}/status (anyone), POST /stop " +
                      (key.Length > 0 ? "(this machine only, needs the save service key)." : "(off: this server has no save service key)."));
        }
#endif
    }
}
