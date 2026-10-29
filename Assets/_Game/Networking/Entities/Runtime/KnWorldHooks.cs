using System;
using Unity.Entities;
using UnityEngine;

namespace KryticalNova.Networking.Entities
{
    /// <summary>
    /// The game's chance to wire its own pieces into a netcode world the moment <see cref="KnBootstrap"/> makes it (M3a-2b: the
    /// player admission and leave handler on a server, the body factory and click source on a client). Networking never names
    /// a Gameplay type: Gameplay fills these slots (like <see cref="KnGoInGame.Source"/>), registering after the reset below.
    /// Reset for Enter Play Mode without a domain reload.
    /// </summary>
    public static class KnWorldHooks
    {
        /// <summary>Called once for each server world, after it listens (dedicated server) or is made (tests, host).</summary>
        public static Action<World> ServerWorldMade;

        /// <summary>Called once for each client world, right after it is made and asked to connect.</summary>
        public static Action<World> ClientWorldMade;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            ServerWorldMade = null;
            ClientWorldMade = null;
        }

        /// <summary>Runs the server hook; a hook that throws is logged and the world goes on (it then admits nobody, with a reason).</summary>
        public static void RaiseServer(World server) => Raise(ServerWorldMade, server, "server");

        public static void RaiseClient(World client) => Raise(ClientWorldMade, client, "client");

        private static void Raise(Action<World> hook, World world, string what)
        {
            if (hook == null || world == null || !world.IsCreated) return;
            try { hook(world); }
            catch (Exception e) { Debug.LogError("[Kn] setting up the " + what + " world failed: " + e); }
        }
    }
}
