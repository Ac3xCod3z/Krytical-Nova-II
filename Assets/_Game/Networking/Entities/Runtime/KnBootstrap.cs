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
            World server = CreateServerWorld(ServerWorldName);
            BodyRoleNetRoleAdapter.Install();
            KnServerEntry.Start(server, settings); // tick rate, join rule, listen, status page, POST /stop, stop signals
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
            StartClientWorld(NetworkEndpoint.LoopbackIpv4.WithPort(port), joinPayload, KnTransportMode.Plain, default);
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

        private static void StartClientWorld(NetworkEndpoint endpoint, string joinPayload, KnTransportMode mode, RelayServerData relay)
        {
            World client = CreateClientWorld(ClientWorldName);
            BodyRoleNetRoleAdapter.Install();
            KnTransport.Assign(client, new KnWorldTransport { Mode = mode, Relay = relay });
            KnNetDefaults.Apply(client);
            client.EntityManager.CreateSingleton(new KnJoinPayload { Text = new Unity.Collections.FixedString4096Bytes(joinPayload ?? string.Empty) });
            Entity request = client.EntityManager.CreateEntity(typeof(NetworkStreamRequestConnect));
            client.EntityManager.SetComponentData(request, new NetworkStreamRequestConnect { Endpoint = endpoint });
        }

        internal static void Listen(World server, ushort port)
        {
            Entity request = server.EntityManager.CreateEntity(typeof(NetworkStreamRequestListen));
            server.EntityManager.SetComponentData(request, new NetworkStreamRequestListen { Endpoint = NetworkEndpoint.AnyIpv4.WithPort(port) });
        }
    }
}
