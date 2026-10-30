using System.Collections.Generic;
using KryticalNova.Core;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

namespace KryticalNova.Networking
{
    /// <summary>
    /// Answers Core's <see cref="NetSession"/> and <see cref="WorldTargets"/> over Netcode for GameObjects, reading
    /// <see cref="NetworkManager.Singleton"/> on every call (so it follows the manager the front end creates and destroys).
    /// Registered once at startup. This is the only place that turns a NetworkObject into a Core type for UI and GameFeel.
    /// </summary>
    public sealed class NgoNetSession : INetSessionSource, IWorldTargets
    {
        public static readonly NgoNetSession Instance = new NgoNetSession();

        // Domain reload is off: Core resets its slots at SubsystemRegistration, this fills them straight after.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            NetSession.Use(Instance);
            WorldTargets.Use(Instance);
        }

        private static NetworkManager Manager => NetworkManager.Singleton;

        // ---------------------------------------------------------------- INetSessionSource

        public bool IsRunning { get { var m = Manager; return m != null && m.IsListening; } }
        public bool IsServer { get { var m = Manager; return m != null && m.IsServer; } }
        public bool IsClient { get { var m = Manager; return m != null && m.IsClient; } }
        public bool IsHost { get { var m = Manager; return m != null && m.IsHost; } }
        public bool IsConnectedClient { get { var m = Manager; return m != null && m.IsConnectedClient; } }
        public bool IsShuttingDown { get { var m = Manager; return m != null && m.ShutdownInProgress; } }

        public int PlayerCount { get { var m = Manager; return m != null && m.IsServer ? m.ConnectedClientsIds.Count : 0; } }

        public int NetworkedObjectCount
        {
            get
            {
                var m = Manager;
                return m != null && m.SpawnManager != null ? m.SpawnManager.SpawnedObjectsList.Count : -1;
            }
        }

        public int TickRate
        {
            get
            {
                var m = Manager;
                return m != null && m.NetworkTickSystem != null ? (int)m.NetworkTickSystem.TickRate : 0;
            }
        }

        public bool TryGetRttMs(out long milliseconds)
        {
            milliseconds = 0;
            var m = Manager;
            if (m == null || !m.IsListening || !m.IsClient || m.IsServer) return false;
            milliseconds = (long)m.NetworkConfig.NetworkTransport.GetCurrentRtt(NetworkManager.ServerClientId);
            return true;
        }

        // ---------------------------------------------------------------- IWorldTargets

        public bool TryGet(TargetId id, out WorldTarget target)
        {
            target = default;
            var m = Manager;
            if (m == null || m.SpawnManager == null) return false;
            if (!m.SpawnManager.SpawnedObjects.TryGetValue(id.Value, out NetworkObject spawned) || spawned == null) return false;
            target = Describe(spawned);
            return true;
        }

        public bool TryFind(Transform anyPart, out WorldTarget target)
        {
            target = default;
            if (anyPart == null) return false;
            var net = anyPart.GetComponentInParent<NetworkObject>();
            if (net == null) return false;
            target = Describe(net);
            return true;
        }

        public void Collect(List<WorldTarget> into)
        {
            var m = Manager;
            if (into == null || m == null || m.SpawnManager == null) return;
            foreach (NetworkObject spawned in m.SpawnManager.SpawnedObjectsList)
                if (spawned != null) into.Add(Describe(spawned));
        }

        public bool SyncsTransform(GameObject body) => body != null && body.GetComponent<NetworkTransform>() != null;

        /// <summary>What Core knows of a networked object. Same rules GameFeel applied to the object directly.</summary>
        public static WorldTarget Describe(NetworkObject net)
        {
            bool live = net.IsSpawned;
            TargetKind kind = net.IsPlayerObject ? TargetKind.Player
                : net.GetComponentInChildren<IHostile>() != null ? TargetKind.Hostile : TargetKind.Other;
            bool local = live && net.IsPlayerObject && net.IsOwner;
            return new WorldTarget(new TargetId(live ? net.NetworkObjectId : 0UL), kind, net.transform, live, local);
        }
    }
}
