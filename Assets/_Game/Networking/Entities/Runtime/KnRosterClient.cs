using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Relay;
using UnityEngine;

namespace KryticalNova.Networking.Entities
{
    /// <summary>The outcome of one roster question.</summary>
    public sealed class KnRosterResult
    {
        public bool Ok;
        /// <summary>The server's answer (the roster JSON the character code reads) when <see cref="Ok"/>.</summary>
        public string Answer;
        /// <summary>When not Ok: what the player reads.</summary>
        public string Error;
        /// <summary>When the server turned the question away: why. None for a local failure (timeout, bad address).</summary>
        public KnRefuseReason Reason;
    }

    /// <summary>What to ask, and where. Fill what you have; the rest has defaults.</summary>
    public sealed class KnRosterAsk
    {
        /// <summary>A server address and port, direct. Ignored when <see cref="Relay"/> is set.</summary>
        public string Address;
        public ushort Port = KnServerSettings.DefaultPort;
        /// <summary>The Relay allocation the caller got from the Relay service for the server's join code (<c>RelayServerData</c>).</summary>
        public RelayServerData? Relay;
        /// <summary>DTLS to a direct dedicated server (default). False for a LAN or loopback test server that has no certificate.</summary>
        public bool Encrypted = true;
        public string Build;
        public bool IsEditor;
        /// <summary>The Unity sign-in token that proves who asks.</summary>
        public string Token;
        /// <summary>The account id the game made up before sign-in (carried over once), or empty.</summary>
        public string OldAccount;
        /// <summary>The roster request (JSON text the character code wrote). Packed by <see cref="RosterWire"/> here.</summary>
        public string Request;
        public double TimeoutSeconds = 15;
    }

    /// <summary>
    /// One roster question in flight: owns the short-lived client world, watches it, and hands back a <see cref="Task"/>. The world
    /// is destroyed as soon as the answer is whole, the server refuses, the connection ends or time runs out. <see cref="Pump"/>
    /// must be called from the main thread outside the world's own update (a MonoBehaviour's Update is fine);
    /// <see cref="KnRosterClient.Ask"/> arranges that by itself in a running game.
    /// </summary>
    public sealed class KnRosterQuery
    {
        private readonly TaskCompletionSource<KnRosterResult> m_Completion = new TaskCompletionSource<KnRosterResult>();
        private readonly Func<double> m_Clock;
        private readonly double m_Deadline;
        private readonly Action m_OnDone;
        private bool m_SawConnection;
        private World m_World;

        public Task<KnRosterResult> Task => m_Completion.Task;
        public bool IsDone => m_Completion.Task.IsCompleted;
        /// <summary>Null until done.</summary>
        public KnRosterResult Result => IsDone ? m_Completion.Task.Result : null;
        /// <summary>The world of this question, null once it is destroyed.</summary>
        public World World => m_World != null && m_World.IsCreated ? m_World : null;

        /// <summary>Watches an existing client world. Tests use this with a world the test world made; the game uses <see cref="KnRosterClient.Ask"/>.</summary>
        public static KnRosterQuery Attach(World clientWorld, double timeoutSeconds, Func<double> clock = null, Action onDone = null) =>
            new KnRosterQuery(clientWorld, timeoutSeconds, clock, onDone);

        internal static KnRosterQuery Failed(string error, Action onDone = null)
        {
            var q = new KnRosterQuery(null, 0, null, onDone);
            q.Finish(new KnRosterResult { Ok = false, Error = error });
            return q;
        }

        private KnRosterQuery(World world, double timeoutSeconds, Func<double> clock, Action onDone)
        {
            m_World = world;
            m_Clock = clock ?? (() => Time.realtimeSinceStartupAsDouble);
            m_Deadline = m_Clock() + timeoutSeconds;
            m_OnDone = onDone;
        }

        /// <summary>Looks at the world once. Returns true when the question is over.</summary>
        public bool Pump()
        {
            if (IsDone) return true;
            if (m_World == null || !m_World.IsCreated)
                return Finish(new KnRosterResult { Error = "The question to the server was cancelled." });

            var system = m_World.GetExistingSystemManaged<KnRosterClientSystem>();
            var em = m_World.EntityManager;
            if (system != null && system.IsBad)
                return Finish(new KnRosterResult { Error = "The server's answer was damaged on the way. Try again." });
            if (system != null && system.Text != null)
            {
                if (RosterWire.TryReadReply(system.Text, out string answer)) return Finish(new KnRosterResult { Ok = true, Answer = answer });
                return Finish(new KnRosterResult { Error = "The server's answer could not be read. It may run another build of the game." });
            }

            // The queries are disposed BEFORE Finish destroys the world: disposing a query of a destroyed world fails.
            KnRosterResult done = null;
            using (var refused = em.CreateEntityQuery(ComponentType.ReadOnly<KnJoinRefusal>()))
            {
                if (refused.TryGetSingleton(out KnJoinRefusal refusal))
                    done = new KnRosterResult { Error = refusal.Detail.ToString(), Reason = (KnRefuseReason)refusal.Reason };
            }
            if (done != null) return Finish(done);

            using (var connections = em.CreateEntityQuery(ComponentType.ReadOnly<NetworkStreamConnection>()))
            {
                if (!connections.IsEmpty) m_SawConnection = true;
                else if (m_SawConnection)
                    done = new KnRosterResult { Error = "The server closed the connection without answering. It may run another build of the game." };
            }
            if (done != null) return Finish(done);

            if (m_Clock() > m_Deadline)
                return Finish(new KnRosterResult { Error = "The server did not answer. It may be off, or not reachable from here." });
            return false;
        }

        /// <summary>Gives up now (the player left the screen). The world is destroyed.</summary>
        public void Cancel()
        {
            if (!IsDone) Finish(new KnRosterResult { Error = "The question to the server was cancelled." });
        }

        private bool Finish(KnRosterResult result)
        {
            if (IsDone) return true;
            World world = m_World;
            m_World = null;
            KnTrace.Write("roster finish: ok=" + result.Ok + " before world dispose, worldCreated=" + (world != null && world.IsCreated));
            if (world != null && world.IsCreated)
            {
                if (World.DefaultGameObjectInjectionWorld == world) World.DefaultGameObjectInjectionWorld = null;
                world.Dispose();
            }
            KnTrace.Write("roster finish: world disposed");
            m_Completion.TrySetResult(result);
            m_OnDone?.Invoke();
            return true;
        }
    }

    /// <summary>
    /// How the menus ask a server about an account's characters (roster path, plan section 4 M2b-2). One question at a time: it makes
    /// a client world of its own, joins with the question in the join text, collects the answer pages and destroys the world.
    /// <para>For the front-end UI (not wired yet): <c>var q = KnRosterClient.Ask(new KnRosterAsk { Address = ..., Port = ..., Build = GameBuild.Version,
    /// IsEditor = Application.isEditor, Token = &lt;Unity access token&gt;, OldAccount = LocalAccount.MadeUpId, Request = json });</c>
    /// then either <c>var r = await q.Task;</c> or, in a coroutine, <c>while (!q.IsDone) yield return null;</c> and read
    /// <c>q.Result</c> (Ok, Answer, Error, Reason). Nothing else is needed: the pump that ends the question runs by itself.</para>
    /// </summary>
    public static class KnRosterClient
    {
        public const string BusyText = "Still waiting for the server's last answer.";

        /// <summary>True while a question is out.</summary>
        public static bool Busy { get; private set; }

        private static readonly List<KnRosterQuery> s_Live = new List<KnRosterQuery>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            foreach (KnRosterQuery q in s_Live.ToArray()) q.Cancel();
            s_Live.Clear();
            Busy = false;
        }

        /// <summary>The join text of a roster question, or null (with <paramref name="error"/>) when it cannot be sent.</summary>
        public static string BuildJoinText(KnRosterAsk ask, out string error)
        {
            error = null;
            if (ask == null || string.IsNullOrEmpty(ask.Request)) { error = "There is nothing to ask the server."; return null; }
            string packed = RosterWire.Pack(ask.Request);
            if (packed.Length > RosterWire.MaxRequestChars) { error = "That request is too long to send."; return null; }
            string text = JoinRules.PayloadText(ask.Build ?? string.Empty, ask.IsEditor, ask.Token ?? string.Empty, ask.OldAccount ?? string.Empty, packed);
            if (!KnApprovalChunks.TrySplit(text, KnApprovalLimits.Default.MaxChunks, new List<string>()))
            {
                error = "The question and your sign-in are too long to send together.";
                return null;
            }
            return text;
        }

        public static KnRosterQuery Ask(KnRosterAsk ask)
        {
            if (Busy) return KnRosterQuery.Failed(BusyText);
            string text = BuildJoinText(ask, out string error);
            if (text == null) return KnRosterQuery.Failed(error);

            NetworkEndpoint endpoint;
            KnTransportMode mode = KnTransportPlanner.ChooseClient(ask.Relay.HasValue, ask.Encrypted);
            if (ask.Relay.HasValue) endpoint = ask.Relay.Value.Endpoint;
            else if (!NetworkEndpoint.TryParse(ask.Address ?? string.Empty, ask.Port, out endpoint))
                return KnRosterQuery.Failed($"\"{ask.Address}\" is not a network address.");

            Busy = true;
            World world = ClientServerBootstrap.CreateClientWorld("KnRoster");
            var transport = new KnWorldTransport { Mode = mode };
            if (ask.Relay.HasValue) transport.Relay = ask.Relay.Value;
            KnTransport.Assign(world, transport);
            KnNetDefaults.Apply(world);
            world.EntityManager.CreateSingleton(new KnJoinPayload { Text = new FixedString4096Bytes(text) });
            Entity request = world.EntityManager.CreateEntity(typeof(NetworkStreamRequestConnect));
            world.EntityManager.SetComponentData(request, new NetworkStreamRequestConnect { Endpoint = endpoint });

            KnRosterQuery query = null;
            query = KnRosterQuery.Attach(world, ask.TimeoutSeconds, null, () =>
            {
                s_Live.Remove(query);
                Busy = false;
            });
            s_Live.Add(query);
            KnRosterPump.EnsureRunning();
            return query;
        }

        internal static void PumpAll()
        {
            for (int i = s_Live.Count - 1; i >= 0; i--)
                if (i < s_Live.Count) s_Live[i].Pump();
        }
    }

    /// <summary>A hidden object that pumps the live questions once a frame (a menu should not have to).</summary>
    internal sealed class KnRosterPump : MonoBehaviour
    {
        private static KnRosterPump s_Instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => s_Instance = null;

        public static void EnsureRunning()
        {
            if (s_Instance != null || !Application.isPlaying) return;
            var go = new GameObject("KN Roster Pump") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(go);
            s_Instance = go.AddComponent<KnRosterPump>();
        }

        private void Update() => KnRosterClient.PumpAll();
    }
}
