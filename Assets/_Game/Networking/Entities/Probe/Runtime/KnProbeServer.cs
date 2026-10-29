#if KN_PROBE || UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Threading;
using KryticalNova.Core;
using KryticalNova.Networking;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

namespace KryticalNova.Networking.Entities.Probe
{
    /// <summary>
    /// The probe's server side (M2c). The dedicated server build already started its world (<see cref="KnBootstrap"/>) and ran
    /// <see cref="KnServerEntry.Start"/> by itself; this adds what a bare server lacks in a test: a fake save
    /// (<see cref="IServerShutdown"/>: logs "SAVE ALL (probe)" and sleeps one second), a fake roster, a join check that needs no Unity
    /// sign-in, and the result file. It never touches a save service, a database or a real port: <see cref="KnProbeEntry"/> refuses to
    /// run on a port that is not on the probe list.
    /// </summary>
    public static class KnProbeServer
    {
        public const string SaveLine = "SAVE ALL (probe)";

        private static KnProbeResult s_Result;
        private static KnProbeArgs s_Args;
        private static string s_Folder;
        private static bool s_Quitting;

        /// <summary>True from <see cref="Install"/> on; the watch system does nothing before.</summary>
        public static bool Active => s_Result != null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            if (s_Result != null) Application.quitting -= OnQuitting;
            s_Result = null;
            s_Args = null;
            s_Folder = null;
            s_Quitting = false;
        }

        public static void Install(KnProbeArgs args, string folder, KnServerSettings settings)
        {
            s_Args = args;
            s_Folder = folder;
            s_Result = new KnProbeResult
            {
                role = "server",
                name = args.Name,
                build = GameBuild.Version,
                startedUtcMs = KnProbeResult.NowMs(),
                listenPort = settings.port,
                pagePort = settings.PagePort,
            };

            ServerShutdown.Handler = ServerShutdown.FromAction(SaveAll);
            RosterLookup.Current = new ProbeRoster();

            World server = ClientServerBootstrap.ServerWorld;
            if (server == null)
            {
#if UNITY_SERVER || UNITY_EDITOR
                server = ClientServerBootstrap.CreateServerWorld(KnBootstrap.ServerWorldName);
                KnServerEntry.Start(server, settings);
#else
                Fail("there is no server world: build the probe server with the Dedicated Server subtarget");
                return;
#endif
            }
            KnApprovalAssembler assembler = server.GetExistingSystemManaged<KnApprovalAssembler>();
            if (assembler == null)
            {
                Fail("the server world has no approval assembler");
                return;
            }
            // No Unity sign-in in a probe: an unsigned game plays as the made-up account it sends. Everything else of the real rule
            // (build match, capacity, bans from Core's slot, roster question) stays.
            KnJoinCheckOptions options = KnServerEntry.JoinOptions(settings, GameBuild.Version, isEditor: false);
            options.RequireProof = false;
            options.AllowDevBots = false;
            assembler.JoinCheck = new KnJoinCheck(options, null, new AcceptAllTickets(), () => assembler.ApprovedCount,
                log: line => Debug.Log("[KnServer] " + line));

            Application.quitting -= OnQuitting;
            Application.quitting += OnQuitting;
            Debug.Log($"[KnProbe] server ready t={KnProbeResult.NowMs()} udp={settings.port} page={settings.PagePort} build={GameBuild.Version}");
            Flush();
        }

        private static void Fail(string why)
        {
            Debug.LogError("[KnProbe] " + why);
            s_Result.exitReason = KnProbeExit.Name(KnProbeExitReason.Error);
            s_Result.exitCode = KnProbeExit.Code(KnProbeExitReason.Error);
            s_Result.error = why;
            s_Result.endedUtcMs = KnProbeResult.NowMs();
            Flush();
            Application.Quit(1);
        }

        private static void Flush()
        {
            if (s_Result != null) KnProbeFiles.Write(s_Folder, KnProbeRole.Server, s_Args?.Name, s_Result);
        }

        /// <summary>Main thread (the watch system): a connection got its PlayerKey.</summary>
        internal static void NotePlayer(ulong key, int networkId)
        {
            if (s_Result == null) return;
            s_Result.AddPlayer(key, networkId);
            Debug.Log($"[KnProbe] player joined key={key} networkId={networkId} t={KnProbeResult.NowMs()}");
            Flush();
        }

        internal static void NoteRoster(string account)
        {
            if (s_Result == null) return;
            s_Result.rosterAnswers++;
            Debug.Log($"[KnProbe] roster answered account={account} t={KnProbeResult.NowMs()}");
            Flush();
        }

        // The fake save: logs, takes a second (so "save before disconnect" is a real order), logs again.
        private static void SaveAll()
        {
            long begin = KnProbeResult.NowMs();
            if (s_Result != null)
            {
                s_Result.saveCalls++;
                if (s_Result.saveBeginUtcMs == 0) s_Result.saveBeginUtcMs = begin;
            }
            Debug.Log($"[KnProbe] {SaveLine} begin t={begin}");
            Thread.Sleep(1000);
            long end = KnProbeResult.NowMs();
            if (s_Result != null) s_Result.saveEndUtcMs = end;
            Debug.Log($"[KnProbe] {SaveLine} end t={end}");
            Flush();
        }

        private static void OnQuitting()
        {
            if (s_Result == null || s_Quitting) return;
            s_Quitting = true;
            KnProbeExitReason reason = KnStopRequests.Stopping ? KnProbeExitReason.Stopped : KnProbeExitReason.QuitAfter;
            if (s_Result.exitCode < 0)
            {
                s_Result.exitReason = KnProbeExit.Name(reason);
                s_Result.exitCode = KnProbeExit.Code(reason);
            }
            s_Result.endedUtcMs = KnProbeResult.NowMs();
            Debug.Log($"[KnProbe] server quitting reason={s_Result.exitReason} saves={s_Result.saveCalls} t={s_Result.endedUtcMs}");
            Flush();
        }

        private sealed class ProbeRoster : IServerRoster
        {
            public string Answer(string account, string oldAccount, string request)
            {
                NoteRoster(account);
                return "{\"probe\":\"roster-ok\",\"account\":\"" + (account ?? string.Empty) + "\"}";
            }
        }
    }

    /// <summary>Server, main thread: tells the probe about each new PlayerKey. Inert unless <see cref="KnProbeServer.Install"/> ran.</summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    public partial class KnProbeWatchSystem : SystemBase
    {
        private EntityQuery m_Players;
        private readonly System.Collections.Generic.HashSet<ulong> m_Seen = new System.Collections.Generic.HashSet<ulong>();

        protected override void OnCreate()
        {
            m_Players = GetEntityQuery(ComponentType.ReadOnly<ConnectionPlayerKey>(), ComponentType.ReadOnly<NetworkId>());
        }

        protected override void OnUpdate()
        {
            if (!KnProbeServer.Active || m_Players.IsEmpty) return;
            var keys = m_Players.ToComponentDataArray<ConnectionPlayerKey>(Allocator.Temp);
            var ids = m_Players.ToComponentDataArray<NetworkId>(Allocator.Temp);
            for (int i = 0; i < keys.Length; i++)
                if (m_Seen.Add(keys[i].Key)) KnProbeServer.NotePlayer(keys[i].Key, ids[i].Value);
            keys.Dispose();
            ids.Dispose();
        }
    }
}
#endif
