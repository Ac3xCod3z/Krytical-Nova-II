using System;
using System.Collections.Generic;
using Unity.Entities;
using Unity.NetCode;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Relay;
using UnityEngine;

namespace KryticalNova.Networking.Entities
{
    /// <summary>
    /// Creates the netcode worlds of Krytical Nova (plan section 2).
    /// Front end (title, menus, Editor, player build): <see cref="Initialize"/> makes only the local world, no client and no
    /// server world. Play starts them on demand: <see cref="StartHost"/>, <see cref="StartClient"/>, <see cref="Stop"/>.
    /// Dedicated server (<c>UNITY_SERVER</c>): only the server world, listening on the port of server-settings.json or <c>-port</c>.
    /// Ticks are 30/30 (<see cref="KnNetDefaults"/>). Every static here is reset for Enter Play Mode without domain reload.
    /// </summary>
    [UnityEngine.Scripting.Preserve]
    public class KnBootstrap : ClientServerBootstrap
    {
        public const string ServerWorldName = "KnServer";
        public const string ClientWorldName = "KnClient";

        /// <summary>Makes the join check of a server world created by <see cref="StartHost"/> or a test. The dedicated server does not use it: <see cref="KnServerEntry"/> sets the real <see cref="KnJoinCheck"/>. Null = accept all.</summary>
        public static Func<IKnJoinCheck> JoinCheckFactory;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => JoinCheckFactory = null;

        public override bool Initialize(string defaultWorldName)
        {
            AutoConnectPort = 0; // never connect or listen by itself: we ask for it
            KnTransport.Install(); // direct UDP, DTLS and Relay, chosen per world
#if UNITY_SERVER
            StartDedicatedServer();
#else
            CreateLocalWorld(defaultWorldName);
#endif
            return true;
        }

#if UNITY_SERVER
        private static void StartDedicatedServer()
        {
            KnServerSettings settings = KnServerSettings.LoadForThisServer();
#if KN_TEST_LANE
            // A test-lane build (the B3 Linux gate) refuses to start on anything of the real server's (plan section 43).
            string offLane = settings.TestLaneProblem();
            if (offLane != null)
            {
                Debug.LogError("[KnServer] This test-lane server does not start: " + offLane + ".");
                UnityEngine.Application.Quit(2);
                return;
            }
#endif
            World server = CreateServerWorld(ServerWorldName);
            BodyRoleNetRoleAdapter.Install();
            KnServerEntry.Start(server, settings); // tick rate, join rule, listen, status page, POST /stop, stop signals
            KnWorldHooks.RaiseServer(server);      // the game's admission and leave handler (M3a-2b)
        }
#endif

        // ------------------------------------------------------------------ on demand (player build and Editor)

        /// <summary>A server world that listens on <paramref name="port"/> plus a client world that joins it over loopback.</summary>
        public static void StartHost(ushort port, string joinPayload = "host")
        {
            Stop();
            World server = CreateServerWorld(ServerWorldName);
            BodyRoleNetRoleAdapter.Install();
            KnNetDefaults.Apply(server);
            Listen(server, port);
            // Host mode is out of M3a-2b (decision D5): the game's hooks are not raised, so a host stays a bare netcode session.
            StartClientWorld(NetworkEndpoint.LoopbackIpv4.WithPort(port), joinPayload, KnTransportMode.Plain, default, raiseHook: false);
        }

        /// <summary>
        /// The owner's "Play DOTS here" in the Editor (M3a feel checkpoint): a server world that listens on loopback only and a client
        /// world that joins it, both with the game's hooks (unlike <see cref="StartHost"/>, a bare session). Plain UDP. Refuses a port of
        /// the real server (<see cref="KnServerSettings.RealLanePorts"/>); false then, with the reason in the log.
        /// </summary>
        public static bool StartPlayHere(ushort port, string joinPayload)
        {
            if (Array.IndexOf(KnServerSettings.RealLanePorts, (int)port) >= 0)
            {
                Debug.LogError($"[KnPlayHere] Port {port} belongs to the real server: not started.");
                return false;
            }
            Stop();
            World server = CreateServerWorld(ServerWorldName);
            BodyRoleNetRoleAdapter.Install();
            KnNetDefaults.Apply(server);
            Entity listen = server.EntityManager.CreateEntity(typeof(NetworkStreamRequestListen));
            server.EntityManager.SetComponentData(listen, new NetworkStreamRequestListen { Endpoint = NetworkEndpoint.LoopbackIpv4.WithPort(port) });
            KnWorldHooks.RaiseServer(server);
            StartClientWorld(NetworkEndpoint.LoopbackIpv4.WithPort(port), joinPayload, KnTransportMode.Plain, default);
            return true;
        }

        /// <summary>A client world that connects to <paramref name="address"/>:<paramref name="port"/>; false when the address is not valid.</summary>
        /// <param name="encrypted">DTLS to a dedicated server (the default; every release server insists on it). False for a LAN or loopback test.</param>
        public static bool StartClient(string address, ushort port, string joinPayload = "client", bool encrypted = true)
        {
            if (!NetworkEndpoint.TryParse(address, port, out NetworkEndpoint endpoint))
            {
                Debug.LogError($"[KnClient] \"{address}\" is not a network address.");
                return false;
            }
            Stop();
            KnTrace.Write("start client: creating the world");
            StartClientWorld(endpoint, joinPayload, encrypted ? KnTransportMode.Secure : KnTransportMode.Plain, default);
            KnTrace.Write("start client: world created");
            return true;
        }

        /// <summary>A client world that joins through Unity Relay with the allocation the caller got from the Relay service.</summary>
        public static void StartClientRelay(RelayServerData relay, string joinPayload = "client")
        {
            Stop();
            StartClientWorld(relay.Endpoint, joinPayload, KnTransportMode.Relay, relay);
        }

        /// <summary>Disposes every client, thin client and server world this bootstrap made (leaves the local world).</summary>
        public static void Stop()
        {
            var worlds = new List<World>();
            worlds.AddRange(ClientWorlds);
            worlds.AddRange(ThinClientWorlds);
            worlds.AddRange(ServerWorlds);
            KnTrace.Write("bootstrap stop: " + worlds.Count + " world(s)");
            foreach (World world in worlds)
            {
                KnTrace.Write("bootstrap stop: disposing '" + (world != null && world.IsCreated ? world.Name : "(gone)") + "'");
                if (world != null && world.IsCreated) world.Dispose();
                KnTrace.Write("bootstrap stop: disposed");
            }
            ClientWorlds.Clear();
            ThinClientWorlds.Clear();
            ServerWorlds.Clear();
            if (World.DefaultGameObjectInjectionWorld != null && !World.DefaultGameObjectInjectionWorld.IsCreated)
                World.DefaultGameObjectInjectionWorld = null;
        }

        private static void StartClientWorld(NetworkEndpoint endpoint, string joinPayload, KnTransportMode mode, RelayServerData relay, bool raiseHook = true)
        {
            World client = CreateClientWorld(ClientWorldName);
            BodyRoleNetRoleAdapter.Install();
            KnTransport.Assign(client, new KnWorldTransport { Mode = mode, Relay = relay });
            KnNetDefaults.Apply(client);
            client.EntityManager.CreateSingleton(new KnJoinPayload { Text = new Unity.Collections.FixedString4096Bytes(joinPayload ?? string.Empty) });
            Entity request = client.EntityManager.CreateEntity(typeof(NetworkStreamRequestConnect));
            client.EntityManager.SetComponentData(request, new NetworkStreamRequestConnect { Endpoint = endpoint });
            if (raiseHook) KnWorldHooks.RaiseClient(client); // the game's body factory, clicks and character (M3a-2b)
        }

        internal static void Listen(World server, ushort port)
        {
            Entity request = server.EntityManager.CreateEntity(typeof(NetworkStreamRequestListen));
            server.EntityManager.SetComponentData(request, new NetworkStreamRequestListen { Endpoint = NetworkEndpoint.AnyIpv4.WithPort(port) });
        }
    }
}
