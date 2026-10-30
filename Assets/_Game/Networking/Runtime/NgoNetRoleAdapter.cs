using KryticalNova.Core;
using Unity.Netcode;
using UnityEngine;

namespace KryticalNova.Networking
{
    /// <summary>
    /// Answers <see cref="NetRole"/> from Netcode for GameObjects. It reads exactly what rule code used to read off its own
    /// NetworkBehaviour (<c>IsSpawned</c>, <c>IsServer</c>, <c>IsOwner</c>, <c>OwnerClientId</c>), so nothing behaves differently.
    /// </summary>
    public sealed class NgoNetRoleAdapter : INetRoleAdapter
    {
        // After Core's reset (SubsystemRegistration), before the first scene.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void InstallAtStartup() => NetRole.Install(new NgoNetRoleAdapter());

        public NetRoleKind Process
        {
            get
            {
                NetworkManager nm = NetworkManager.Singleton;
                if (nm == null || !nm.IsListening) return NetRoleKind.Offline;
                return nm.IsServer ? NetRoleKind.ServerBody : NetRoleKind.ClientView;
            }
        }

        public NetRoleKind Of(Component networked)
        {
            if (networked is NetworkBehaviour nb)
                return !nb.IsSpawned ? NetRoleKind.Offline : nb.IsServer ? NetRoleKind.ServerBody : NetRoleKind.ClientView;
            if (networked.TryGetComponent(out NetworkObject no))
            {
                if (!no.IsSpawned) return NetRoleKind.Offline;
                NetworkManager nm = no.NetworkManager;
                return nm != null && nm.IsServer ? NetRoleKind.ServerBody : NetRoleKind.ClientView;
            }
            return NetRoleKind.Offline;
        }

        public bool IsOwner(Component networked)
        {
            if (networked is NetworkBehaviour nb) return nb.IsSpawned && nb.IsOwner;
            return networked.TryGetComponent(out NetworkObject no) && no.IsSpawned && no.IsOwner;
        }

        public bool IsOwnedByServer(Component networked)
        {
            if (networked is NetworkBehaviour nb) return nb.IsOwnedByServer;
            return networked.TryGetComponent(out NetworkObject no) && no.IsOwnedByServer;
        }

        public bool IsRemoteOwned(Component networked)
        {
            if (networked is NetworkBehaviour nb) return nb.IsSpawned && nb.OwnerClientId != NetworkManager.ServerClientId;
            return networked.TryGetComponent(out NetworkObject no) && no.IsSpawned && no.OwnerClientId != NetworkManager.ServerClientId;
        }

        public ulong OwnerId(Component networked)
        {
            if (networked is NetworkBehaviour nb) return nb.OwnerClientId;
            return networked.TryGetComponent(out NetworkObject no) ? no.OwnerClientId : 0UL;
        }

        public void DisconnectOwner(Component networked, string reason)
        {
            NetworkManager nm = networked is NetworkBehaviour nb ? nb.NetworkManager : NetworkManager.Singleton;
            if (nm != null) nm.DisconnectClient(OwnerId(networked), reason);
        }
    }
}
