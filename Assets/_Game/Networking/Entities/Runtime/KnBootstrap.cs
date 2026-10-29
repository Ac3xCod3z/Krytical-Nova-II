using System;
using System.Collections.Generic;
using Unity.Entities;
using Unity.NetCode;
using Unity.Networking.Transport;
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

        /// <summary>Makes the server's join check when a server world is created (the real check is plugged in here in M2b). Null = accept all.</summary>
        public static Func<IKnJoinCheck> JoinCheckFactory;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => JoinCheckFactory = null;

        public override bool Initialize(string defaultWorldName)
        {
            AutoConnectPort = 0; // never connect or listen by itself: we ask for it
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
            KnNetDefaults.Apply(server);
            Listen(server, (ushort)settings.port);
            Debug.Log($"[KnServer] server world created, listening on UDP {settings.port}, role {settings.role}, map {settings.map}, page port {settings.PagePort}.");
            // TODO(M2b): status/control page on settings.PagePort (managed, reads a snapshot) and POST /stop on port+1 (loopback, X-KN-Key).
            // TODO(M2b): SIGTERM handler (libc signal(15), sets a flag only) + a main-thread system that saves everyone and calls
            //            Application.Quit(0) (spike: wantsToQuit is not called on SIGTERM). Core has no stop interface yet: add a small
            //            one (like IServerLeaving) when that lands; the POST /stop page calls the same path.
        }
#endif

        // ------------------------------------------------------------------ on demand (player build and Editor)

        /// <summary>A server world that listens on <paramref name="port"/> plus a client world that joins it over loopback.</summary>
        public static void StartHost(ushort port, string joinPayload = "host")
        {
            Stop();
            World server = CreateServerWorld(ServerWorldName);
            KnNetDefaults.Apply(server);
            Listen(server, port);
            StartClientWorld(NetworkEndpoint.LoopbackIpv4.WithPort(port), joinPayload);
        }

        /// <summary>A client world that connects to <paramref name="address"/>:<paramref name="port"/>; false when the address is not valid.</summary>
        public static bool StartClient(string address, ushort port, string joinPayload = "client")
        {
            if (!NetworkEndpoint.TryParse(address, port, out NetworkEndpoint endpoint))
            {
                Debug.LogError($"[KnClient] \"{address}\" is not a network address.");
                return false;
            }
            Stop();
            StartClientWorld(endpoint, joinPayload);
            return true;
        }

        /// <summary>Disposes every client, thin client and server world this bootstrap made (leaves the local world).</summary>
        public static void Stop()
        {
            var worlds = new List<World>();
            worlds.AddRange(ClientWorlds);
            worlds.AddRange(ThinClientWorlds);
            worlds.AddRange(ServerWorlds);
            foreach (World world in worlds)
            {
                if (world != null && world.IsCreated) world.Dispose();
            }
            ClientWorlds.Clear();
            ThinClientWorlds.Clear();
            ServerWorlds.Clear();
            if (World.DefaultGameObjectInjectionWorld != null && !World.DefaultGameObjectInjectionWorld.IsCreated)
                World.DefaultGameObjectInjectionWorld = null;
        }

        private static void StartClientWorld(NetworkEndpoint endpoint, string joinPayload)
        {
            World client = CreateClientWorld(ClientWorldName);
            KnNetDefaults.Apply(client);
            client.EntityManager.CreateSingleton(new KnJoinPayload { Text = new Unity.Collections.FixedString4096Bytes(joinPayload ?? string.Empty) });
            Entity request = client.EntityManager.CreateEntity(typeof(NetworkStreamRequestConnect));
            client.EntityManager.SetComponentData(request, new NetworkStreamRequestConnect { Endpoint = endpoint });
        }

        private static void Listen(World server, ushort port)
        {
            Entity request = server.EntityManager.CreateEntity(typeof(NetworkStreamRequestListen));
            server.EntityManager.SetComponentData(request, new NetworkStreamRequestListen { Endpoint = NetworkEndpoint.AnyIpv4.WithPort(port) });
        }
    }
}
