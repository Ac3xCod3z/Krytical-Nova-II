using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using Unity.Collections;
using Unity.Core;
using Unity.Entities;
using Unity.NetCode;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Utilities;
using UnityEngine.TestTools;
using Unity.Scenes;
using KryticalNova.Core;
using UnityEngine;
using Hash128 = UnityEngine.Hash128;

namespace KryticalNova.Networking.Entities.Tests
{
    /// <summary>
    /// Minimal netcode test world built ONLY from public Netcode for Entities API (NetCodeTestWorld is internal).
    /// One server world + N client worlds in one process, connected over IPC loopback (no sockets), ticked by hand
    /// with a fixed delta time so a test is deterministic. Our approval systems live in every world, so each join goes through
    /// the chunked approval: clients send <see cref="JoinPayload"/> (null = send nothing). Brought over from the M1 spike.
    ///
    /// Usage:
    ///   using var tw = new KnTestWorld { SimulationTickRate = 30 };
    ///   tw.CreateWorlds(numClients: 2);
    ///   tw.LoadSubScene(guid);              // optional, loads in every world
    ///   tw.Connect(goInGame: true);         // listen + connect + wait for NetworkId + NetworkStreamInGame
    ///   tw.Tick(30);
    /// </summary>
    public sealed class KnTestWorld : IDisposable, INetworkStreamDriverConstructor
    {
        // ---- settings (set before CreateWorlds) ----
        public int SimulationTickRate = 30;
        public int NetworkTickRate = 30;
        /// <summary>0 keeps the Netcode default (NetCodeConfig / UTP default).</summary>
        public int MaxMessageSize = 0;
        public int FragmentationPayloadCapacity = 16 * 1024;
        /// <summary>The text each client proves itself with (KnJoinPayload); null = clients send no approval chunks.</summary>
        public string JoinPayload = "kn-test-join";
        /// <summary>Handshake plus approval limit sent by the server (the game's default).</summary>
        public uint HandshakeApprovalTimeoutMs = KnNetDefaults.HandshakeApprovalTimeoutMs;
        public ushort Port = 7979;
        /// <summary>Simulated one-way delay on the CLIENT driver (UTP SimulatorUtility, deterministic: driver time is fixed per tick).</summary>
        public int SimulatedDelayMs = 0;
        public int SimulatedJitterMs = 0;
        /// <summary>Drop every Nth packet (0 = none).</summary>
        public int SimulatedDropInterval = 0;

        // ---- state ----
        public World Server { get; private set; }
        public readonly List<World> Clients = new List<World>();
        /// <summary>Every Unity log line (log, warning, error) raised while this test world lives.</summary>
        public readonly List<string> Logs = new List<string>();
        public float Dt => 1f / SimulationTickRate;
        public int TickCount { get; private set; }

        double m_Elapsed;
        uint m_StampMs = 1000;
        readonly INetworkStreamDriverConstructor m_OldConstructor;
        readonly bool m_OldIgnore;
        readonly bool m_OldRunInBackground;
        bool m_Disposed;

        static readonly PropertyInfo s_FixedStamp =
            typeof(NetworkTimeSystem).GetProperty("s_FixedTimestampMS", BindingFlags.NonPublic | BindingFlags.Static);

        public KnTestWorld()
        {
            m_OldConstructor = NetworkStreamReceiveSystem.DriverConstructor;
            NetworkStreamReceiveSystem.DriverConstructor = this;
            m_OldIgnore = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true; // netcode logs errors by design in negative tests; tests inspect Logs instead
            PlayerKeys.Reset();
            m_OldRunInBackground = Application.runInBackground;
            Application.runInBackground = true;   // silences the netcode "runInBackground" advice error
            Application.logMessageReceivedThreaded += OnLog;
        }

        /// <summary>Logs minus known harmless editor/test noise.</summary>
        public IEnumerable<string> Interesting { get { lock (Logs) return Logs.ToArray().Where(l => !l.Contains("runInBackground")).ToArray(); } }

        void OnLog(string condition, string stack, LogType type) { lock (Logs) Logs.Add($"[{type}] {condition}"); }

        public IEnumerable<World> AllWorlds
        {
            get
            {
                if (Server != null && Server.IsCreated) yield return Server;
                foreach (var c in Clients) if (c != null && c.IsCreated) yield return c;
            }
        }

        // ------------------------------------------------------------------ drivers (INetworkStreamDriverConstructor)
        NetworkSettings Settings(bool server)
        {
            var s = server ? DefaultDriverBuilder.GetNetworkServerSettings() : DefaultDriverBuilder.GetNetworkClientSettings();
            s.WithFragmentationStageParameters(payloadCapacity: FragmentationPayloadCapacity);
            // fixed driver time per tick = deterministic timeouts/simulator, independent of real time
            if (MaxMessageSize > 0)
                s.WithNetworkConfigParameters(maxMessageSize: MaxMessageSize, fixedFrameTimeMS: Mathf.RoundToInt(1000f / SimulationTickRate), maxFrameTimeMS: 100);
            else
                s.WithNetworkConfigParameters(fixedFrameTimeMS: Mathf.RoundToInt(1000f / SimulationTickRate), maxFrameTimeMS: 100);
            return s;
        }

        public void CreateClientDriver(World world, ref NetworkDriverStore driverStore, NetDebug netDebug)
        {
            var settings = Settings(false);
            if (SimulatedDelayMs > 0 || SimulatedJitterMs > 0 || SimulatedDropInterval > 0)
            {
                int maxPackets = 2 * (SimulationTickRate * 3 * (SimulatedDelayMs + SimulatedJitterMs) + 999) / 1000 + 8;
                var sim = new SimulatorUtility.Parameters
                {
                    Mode = ApplyMode.AllPackets,
                    MaxPacketSize = NetworkParameterConstants.MTU,
                    MaxPacketCount = maxPackets,
                    PacketDelayMs = SimulatedDelayMs,
                    PacketJitterMs = SimulatedJitterMs,
                    PacketDropInterval = SimulatedDropInterval,
                };
                settings.AddRawParameterStruct(ref sim);
                var instS = new NetworkDriverStore.NetworkDriverInstance { driver = NetworkDriver.Create(new IPCNetworkInterface(), settings) };
                DefaultDriverBuilder.CreateClientSimulatorPipelines(ref instS);
                driverStore.RegisterDriver(TransportType.Socket, instS); // "socket" type so the simulator path is used, as NetCodeTestWorld does
                return;
            }
            var inst = DefaultDriverBuilder.CreateClientNetworkDriver(new IPCNetworkInterface(), settings);
            driverStore.RegisterDriver(TransportType.IPC, inst);
        }

        public void CreateServerDriver(World world, ref NetworkDriverStore driverStore, NetDebug netDebug)
        {
            var inst = DefaultDriverBuilder.CreateServerNetworkDriver(new IPCNetworkInterface(), Settings(true));
            driverStore.RegisterDriver(TransportType.IPC, inst);
        }

        // ------------------------------------------------------------------ world creation
        public void CreateWorlds(int numClients)
        {
            Server = ClientServerBootstrap.CreateServerWorld("KnServer");
            Prepare(Server, isServer: true);
            for (int i = 0; i < numClients; i++)
            {
                var c = ClientServerBootstrap.CreateClientWorld("KnClient" + i);
                Prepare(c, isServer: false);
                Clients.Add(c);
            }
            Tick(1); // let collections / singletons initialise (NetworkStreamDriver singleton appears on the first update)
        }

        void Prepare(World w, bool isServer)
        {
            // never let the Editor's own player loop tick these worlds: we tick by hand
            ScriptBehaviourUpdateOrder.RemoveWorldFromCurrentPlayerLoop(w);
            var timeSys = w.GetExistingSystemManaged<UpdateWorldTimeSystem>();
            if (timeSys != null) timeSys.Enabled = false;

            var rate = new ClientServerTickRate
            {
                SimulationTickRate = SimulationTickRate,
                NetworkTickRate = NetworkTickRate,
                HandshakeApprovalTimeoutMS = HandshakeApprovalTimeoutMs,
            };
            rate.ResolveDefaults();
            w.EntityManager.CreateSingleton(rate);
            if (!isServer && JoinPayload != null)
                w.EntityManager.CreateSingleton(new KnJoinPayload { Text = new FixedString4096Bytes(JoinPayload) });

        }

        // ------------------------------------------------------------------ ticking
        public void Tick(int n = 1)
        {
            for (int i = 0; i < n; i++) TickOnce();
        }

        void TickOnce()
        {
            TickCount++;
            m_Elapsed += Dt;
            m_StampMs += (uint)Mathf.RoundToInt(Dt * 1000f);
            s_FixedStamp?.SetValue(null, m_StampMs); // deterministic RTT clock (internal in Netcode, set by reflection)
            var td = new TimeData(m_Elapsed, Dt);
            foreach (var w in AllWorlds) w.SetTime(td);
            foreach (var w in AllWorlds) Update<InitializationSystemGroup>(w);
            foreach (var w in AllWorlds) Update<SimulationSystemGroup>(w);
            foreach (var w in AllWorlds) Update<PresentationSystemGroup>(w);
        }

        static void Update<T>(World w) where T : ComponentSystemGroup
        {
            var g = w.GetExistingSystemManaged<T>();
            g?.Update();
        }

        /// <summary>Ticks until cond() is true; returns ticks used, or -1 on timeout.</summary>
        public int TickUntil(Func<bool> cond, int maxTicks = 300)
        {
            for (int i = 0; i < maxTicks; i++)
            {
                if (cond()) return i;
                TickOnce();
            }
            return cond() ? maxTicks : -1;
        }

        // ------------------------------------------------------------------ connection
        public void Connect(bool goInGame = true, int maxTicks = 600)
        {
            Listen();
            foreach (var c in Clients) ConnectClient(c);
            WaitConnected(maxTicks);
            if (goInGame) GoInGame();
        }

        public void Listen()
        {
            var ep = NetworkEndpoint.LoopbackIpv4.WithPort(Port);
            using var q = Server.EntityManager.CreateEntityQuery(ComponentType.ReadWrite<NetworkStreamDriver>());
            Assert.IsTrue(q.GetSingletonRW<NetworkStreamDriver>().ValueRW.Listen(ep), "server Listen failed");
        }

        /// <summary>Starts connecting one client world to the server (does not wait).</summary>
        public void ConnectClient(World c)
        {
            var ep = NetworkEndpoint.LoopbackIpv4.WithPort(Port);
            using var q = c.EntityManager.CreateEntityQuery(ComponentType.ReadWrite<NetworkStreamDriver>());
            q.GetSingletonRW<NetworkStreamDriver>().ValueRW.Connect(c.EntityManager, ep);
        }

        /// <summary>Creates one more client world after the others (a later join), optionally connecting it at once.</summary>
        public World AddClient(string joinPayload, bool connect = true)
        {
            string old = JoinPayload;
            JoinPayload = joinPayload;
            var c = ClientServerBootstrap.CreateClientWorld("KnClient" + Clients.Count);
            Prepare(c, isServer: false);
            JoinPayload = old;
            Clients.Add(c);
            Tick(1);
            if (connect) ConnectClient(c);
            return c;
        }

        public void WaitConnected(int maxTicks = 600)
        {
            int used = TickUntil(() =>
            {
                foreach (var c in Clients) if (!HasNetworkId(c)) return false;
                return true;
            }, maxTicks);
            Assert.GreaterOrEqual(used, 0, "clients did not get a NetworkId within " + maxTicks + " ticks. Logs: " + string.Join(" | ", Logs));
        }

        public static bool HasNetworkId(World w)
        {
            using var q = w.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<NetworkId>());
            return !q.IsEmpty;
        }

        public static int NetworkIdOf(World client)
        {
            using var q = client.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<NetworkId>());
            return q.GetSingleton<NetworkId>().Value;
        }

        /// <summary>Adds NetworkStreamInGame to every connection of every world (client and server side).</summary>
        public void GoInGame()
        {
            foreach (var w in AllWorlds)
            {
                using var q = w.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<NetworkId>(), ComponentType.Exclude<NetworkStreamInGame>());
                var ents = q.ToEntityArray(Allocator.Temp);
                foreach (var e in ents) w.EntityManager.AddComponent<NetworkStreamInGame>(e);
                ents.Dispose();
            }
        }

        // ------------------------------------------------------------------ subscenes
        /// <summary>Loads a subscene (by scene asset GUID) into every world, ticking until loaded in all.</summary>
        public void LoadSubScene(Hash128 sceneGuid, int maxTicks = 400)
        {
            foreach (var w in AllWorlds)
            {
                // resolve the scene entity first (imports the entity scene synchronously)
                var probe = SceneSystem.LoadSceneAsync(w.Unmanaged, sceneGuid,
                    new SceneSystem.LoadParameters { Flags = SceneLoadFlags.BlockOnImport | SceneLoadFlags.DisableAutoLoad });
                for (int i = 0; i < 100 && !w.EntityManager.HasComponent<ResolvedSectionEntity>(probe); i++)
                {
                    Update<InitializationSystemGroup>(w); Update<SimulationSystemGroup>(w); Thread.Sleep(20);
                }
            }
            var scenes = new List<(World, Entity)>();
            foreach (var w in AllWorlds)
                scenes.Add((w, SceneSystem.LoadSceneAsync(w.Unmanaged, sceneGuid,
                    new SceneSystem.LoadParameters { Flags = SceneLoadFlags.BlockOnImport | SceneLoadFlags.BlockOnStreamIn })));
            for (int i = 0; i < maxTicks; i++)
            {
                Thread.Sleep(10);
                TickOnce();
                bool all = true;
                foreach (var (w, e) in scenes) if (!SceneSystem.IsSceneLoaded(w.Unmanaged, e)) { all = false; break; }
                if (all) return;
            }
            Assert.Fail("subscene did not load in all worlds within " + maxTicks + " ticks");
        }

        // ------------------------------------------------------------------ helpers
        public static T Singleton<T>(World w) where T : unmanaged, IComponentData
        {
            using var q = w.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<T>());
            return q.GetSingleton<T>();
        }

        public static int Count<T>(World w)
        {
            using var q = w.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<T>());
            return q.CalculateEntityCount();
        }

        public void Dispose()
        {
            if (m_Disposed) return;
            m_Disposed = true;
            Application.logMessageReceivedThreaded -= OnLog;
            Application.runInBackground = m_OldRunInBackground;
            s_FixedStamp?.SetValue(null, 0u);
            PlayerKeys.Reset();
            foreach (var c in Clients) if (c != null && c.IsCreated) c.Dispose();
            Clients.Clear();
            if (Server != null && Server.IsCreated) Server.Dispose();
            Server = null;
            World.DefaultGameObjectInjectionWorld = null;
            NetworkStreamReceiveSystem.DriverConstructor = m_OldConstructor;
            LogAssert.ignoreFailingMessages = m_OldIgnore;
        }
    }
}
