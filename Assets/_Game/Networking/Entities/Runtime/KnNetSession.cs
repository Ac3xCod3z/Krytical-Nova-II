using System.Collections.Generic;
using Unity.Entities;
using Unity.NetCode;
using KryticalNova.Core;

namespace KryticalNova.Networking.Entities
{
    /// <summary>
    /// What <see cref="NetSession"/> answers while Krytical Nova server or client worlds exist. Without them it asks the source
    /// that was there before (the Netcode for GameObjects one), so that path is unchanged. A host (both worlds) is out of scope
    /// for M3a-2b (decision D5); it is reported as a host anyway.
    /// </summary>
    public sealed class KnNetSession : INetSessionSource
    {
        private readonly INetSessionSource m_Fallback;

        public KnNetSession(INetSessionSource fallback = null) => m_Fallback = fallback;

        private static bool HasServer => BodyRoleNetRoleAdapter.HasLiveWorld(ClientServerBootstrap.ServerWorlds);
        private static bool HasClient => BodyRoleNetRoleAdapter.HasLiveWorld(ClientServerBootstrap.ClientWorlds);
        private static bool Own => HasServer || HasClient;

        public bool IsRunning => Own || (m_Fallback != null && m_Fallback.IsRunning);
        public bool IsServer => Own ? HasServer : m_Fallback != null && m_Fallback.IsServer;
        public bool IsClient => Own ? HasClient : m_Fallback != null && m_Fallback.IsClient;
        public bool IsHost => Own ? HasServer && HasClient : m_Fallback != null && m_Fallback.IsHost;
        public bool IsShuttingDown => !Own && m_Fallback != null && m_Fallback.IsShuttingDown;

        public bool IsConnectedClient
        {
            get
            {
                if (!Own) return m_Fallback != null && m_Fallback.IsConnectedClient;
                IReadOnlyList<World> clients = ClientServerBootstrap.ClientWorlds;
                for (int i = 0; i < clients.Count; i++)
                {
                    World world = clients[i];
                    if (world == null || !world.IsCreated) continue;
                    using var query = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<NetworkId>());
                    if (!query.IsEmpty) return true;
                }
                return false;
            }
        }

        public int PlayerCount => Own ? PlayerKeys.Count : m_Fallback != null ? m_Fallback.PlayerCount : 0;
        public int NetworkedObjectCount => Own ? -1 : m_Fallback != null ? m_Fallback.NetworkedObjectCount : -1;
        public int TickRate => Own ? KnNetDefaults.SimulationTickRate : m_Fallback != null ? m_Fallback.TickRate : 0;

        public bool TryGetRttMs(out long milliseconds)
        {
            if (!Own && m_Fallback != null) return m_Fallback.TryGetRttMs(out milliseconds);
            milliseconds = 0;
            return false;
        }
    }
}
