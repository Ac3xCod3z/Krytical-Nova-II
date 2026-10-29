#if KN_PROBE || UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Net;
using KryticalNova.Networking;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

namespace KryticalNova.Networking.Entities.Probe
{
    /// <summary>
    /// The scripted probe client (M2c): optionally asks the roster, joins the server, watches its client world, and ends with an exit
    /// code (0 ok, 2 refused, 3 timeout, 4 kicked) after writing <c>kn-probe-client[-name].json</c>. The decisions are in
    /// <see cref="KnProbeClientMachine"/> (tested without a world); this component only reads the world and feeds the machine.
    /// A drop after connecting is a kick when the server's page still says <c>/health</c> 200, a server stop otherwise.
    /// </summary>
    public sealed class KnProbeClient : MonoBehaviour
    {
        private KnProbeArgs m_Args;
        private KnProbeResult m_Result;
        private KnProbeClientMachine m_Machine;
        private string m_Folder;
        private KnRosterQuery m_Roster;
        private bool m_ConnectStarted;
        private bool m_Ended;
        private World m_World;
        private EntityQuery m_NetworkId;
        private EntityQuery m_Refusal;
        private bool m_HaveQueries;

        private static double Now => Time.realtimeSinceStartupAsDouble;

        public static KnProbeClient Create(KnProbeArgs args, string folder)
        {
            var go = new GameObject("KN Probe Client");
            DontDestroyOnLoad(go);
            var client = go.AddComponent<KnProbeClient>();
            client.m_Args = args;
            client.m_Folder = folder;
            client.m_Result = new KnProbeResult
            {
                role = "client",
                name = args.Name,
                build = GameBuild.Version,
                networkId = -1,
            };
            client.m_Machine = new KnProbeClientMachine(args, client.m_Result);
            client.m_Machine.Begin(Now);
            Log($"client {args.Name} starting action={args.Action} target={args.Address}:{args.Port} account={args.Account}");
            client.Flush();
            return client;
        }

        private static void Log(string line) => Debug.Log($"[KnProbe] {line} t={KnProbeResult.NowMs()}");

        private void Flush() => KnProbeFiles.Write(m_Folder, KnProbeRole.Client, m_Args.Name, m_Result);

        private void Update()
        {
            if (m_Ended) return;
            try
            {
                Step();
            }
            catch (Exception e)
            {
                m_Machine.OnError(e.GetType().Name + ": " + e.Message);
            }
            m_Machine.Tick(Now);
            if (m_Machine.IsDone) End();
        }

        private void Step()
        {
            switch (m_Machine.Phase)
            {
                case KnProbePhase.Roster:
                    if (m_Roster == null)
                    {
                        m_Roster = KnRosterClient.Ask(new KnRosterAsk
                        {
                            Address = m_Args.Address,
                            Port = (ushort)m_Args.Port,
                            Encrypted = false,
                            Build = GameBuild.Version,
                            IsEditor = Application.isEditor,
                            Token = string.Empty,
                            OldAccount = m_Args.Account,
                            Request = "{\"probe\":\"roster\"}",
                            TimeoutSeconds = Math.Max(1, m_Args.TimeoutSeconds - 1),
                        });
                        Log("roster question sent");
                    }
                    else if (m_Roster.IsDone)
                    {
                        KnRosterResult r = m_Roster.Result;
                        Log($"roster done ok={r.Ok} reason={r.Reason}");
                        m_Machine.OnRosterDone(r.Ok, r.Answer, r.Error, r.Reason.ToString(), Now);
                        Flush();
                    }
                    break;

                case KnProbePhase.Connecting:
                    if (!m_ConnectStarted)
                    {
                        m_ConnectStarted = true;
                        string join = JoinRules.PayloadText(GameBuild.Version, Application.isEditor, string.Empty, m_Args.Account);
                        if (!KnBootstrap.StartClient(m_Args.Address, (ushort)m_Args.Port, join, encrypted: false))
                            m_Machine.OnError("\"" + m_Args.Address + "\" is not a network address");
                        else Log("connecting");
                        break;
                    }
                    if (!FindWorld()) break;
                    if (m_Refusal.TryGetSingleton(out KnJoinRefusal refusal))
                    {
                        Log($"refused reason={(KnRefuseReason)refusal.Reason}");
                        m_Machine.OnRefused(((KnRefuseReason)refusal.Reason).ToString(), refusal.Detail.ToString());
                    }
                    else if (m_NetworkId.TryGetSingleton(out NetworkId id))
                    {
                        Log($"connected networkId={id.Value}");
                        m_Machine.OnConnected(id.Value, Now);
                        Flush();
                    }
                    break;

                case KnProbePhase.Connected:
                    if (m_World == null || !m_World.IsCreated || m_NetworkId.IsEmpty)
                    {
                        bool answered = TryHealth(m_Args.Address, m_Args.PagePort, out int status);
                        KnDropCause cause = KnProbeLogic.ClassifyDrop(answered, status);
                        Log($"connection ended page={(answered ? status.ToString() : "no answer")} cause={cause}");
                        m_Machine.OnDropped(cause);
                    }
                    break;
            }
        }

        private bool FindWorld()
        {
            if (m_HaveQueries) return true;
            foreach (World world in ClientServerBootstrap.ClientWorlds)
            {
                if (world == null || !world.IsCreated || world.Name != KnBootstrap.ClientWorldName) continue;
                m_World = world;
                m_NetworkId = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<NetworkId>());
                m_Refusal = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<KnJoinRefusal>());
                m_HaveQueries = true;
                return true;
            }
            return false;
        }

        /// <summary>GET /health of the server page: false when nothing answers. A 503 (stopping) is an answer.</summary>
        private static bool TryHealth(string address, int pagePort, out int status)
        {
            status = 0;
            if (pagePort <= 0) return false;
            try
            {
                var request = (HttpWebRequest)WebRequest.Create($"http://{address}:{pagePort}/health");
                request.Timeout = 1500;
                request.ReadWriteTimeout = 1500;
                using (var response = (HttpWebResponse)request.GetResponse())
                {
                    status = (int)response.StatusCode;
                    return true;
                }
            }
            catch (WebException e) when (e.Response is HttpWebResponse http)
            {
                status = (int)http.StatusCode;
                http.Close();
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private void End()
        {
            if (m_Ended) return;
            m_Ended = true;
            Log($"END reason={m_Result.exitReason} exit={m_Machine.ExitCode}");
            Flush();
            try
            {
                // Queries go before their world, never after (disposing a query of a destroyed world fails).
                if (m_HaveQueries && m_World != null && m_World.IsCreated)
                {
                    m_NetworkId.Dispose();
                    m_Refusal.Dispose();
                }
                m_HaveQueries = false;
                m_Roster?.Cancel();
                KnBootstrap.Stop();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[KnProbe] clean-up failed (the result is already written): " + e.GetType().Name + ": " + e.Message);
            }
            Application.Quit(m_Machine.ExitCode);
        }
    }
}
#endif
