using System.Collections.Generic;
using KryticalNova.Items;
using Unity.Netcode;
using UnityEngine;

namespace KryticalNova.DevTools
{
    /// <summary>
    /// The player the GM and item commands act on. It is this machine's own player unless the host (or a server)
    /// picks a connected player with gm.target; a joined client can never use GM commands, on itself or anyone.
    /// </summary>
    public static class GmTarget
    {
        private const string NoPlayer = "No local player.";
        private const string NoAuthority = "Only the host/offline player can use GM commands.";

        private static bool hasTarget;
        private static ulong targetClientId;

        // Statics survive Play Mode when domain reload is disabled; every session starts on the local player.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            hasTarget = false;
            targetClientId = 0;
        }

        private static NetworkManager Session
        {
            get
            {
                var nm = NetworkManager.Singleton;
                return nm != null && nm.IsListening ? nm : null;
            }
        }

        /// <summary>Who the commands act on, kept aside while a joined GM's line runs (<see cref="GmCommandRelay"/>).</summary>
        public struct Saved
        {
            public bool HasTarget;
            public ulong ClientId;
        }

        public static Saved Save() => new Saved { HasTarget = hasTarget, ClientId = targetClientId };

        public static void Restore(Saved saved)
        {
            hasTarget = saved.HasTarget;
            targetClientId = saved.ClientId;
        }

        /// <summary>The picked player's client id, or null while the commands act on the local player.</summary>
        public static ulong? ClientId => hasTarget && Session != null ? targetClientId : (ulong?)null;

        /// <summary>Acts on this machine's own player again.</summary>
        public static void TargetSelf() => hasTarget = false;

        /// <summary>Picks a connected player by client id (host / server only).</summary>
        public static bool TrySetTarget(ulong clientId, out string error)
        {
            error = null;
            var nm = Session;
            if (nm == null) { error = "No network session is running."; return false; }
            if (!nm.IsServer) { error = NoAuthority; return false; }
            if (!nm.ConnectedClients.TryGetValue(clientId, out var client) || client.PlayerObject == null)
            { error = $"No connected player with client id {clientId}. Try gm.players."; return false; }

            hasTarget = clientId != nm.LocalClientId || !nm.IsClient;
            targetClientId = clientId;
            return true;
        }

        /// <summary>
        /// True where GM commands may run that act on no one player: the host, a dedicated server (which has no player of
        /// its own), or a game with no session.
        /// </summary>
        public static bool HasAuthority(out string error)
        {
            var nm = Session;
            error = nm != null && !nm.IsServer ? NoAuthority : null;
            return error == null;
        }

        /// <summary>The player the commands act on, where that player is simulated.</summary>
        public static bool TryGetPlayer(out GameObject player, out string error)
        {
            player = null;
            error = null;
            var nm = Session;
            if (nm != null && !nm.IsServer) { error = NoAuthority; return false; }

            if (hasTarget && nm != null)
            {
                if (nm.ConnectedClients.TryGetValue(targetClientId, out var client) && client.PlayerObject != null)
                {
                    player = client.PlayerObject.gameObject;
                    return true;
                }
                hasTarget = false; // they left: back to the local player
            }

            var local = PlayerInventory.Local;
            if (local == null)
            {
                error = nm != null ? "No local player on this server. Pick one with gm.target <clientId> (see gm.players)." : NoPlayer;
                return false;
            }
            player = local.gameObject;
            return true;
        }

        /// <summary>Connected players as (client id, player object). Empty outside a session this machine hosts.</summary>
        public static List<(ulong ClientId, GameObject Player)> ConnectedPlayers()
        {
            var result = new List<(ulong, GameObject)>();
            var nm = Session;
            if (nm == null || !nm.IsServer) return result;
            foreach (var client in nm.ConnectedClientsList)
                if (client.PlayerObject != null) result.Add((client.ClientId, client.PlayerObject.gameObject));
            return result;
        }

        /// <summary>"client 1" / "your own player", for command replies.</summary>
        public static string Describe()
        {
            ulong? id = ClientId;
            return id.HasValue ? $"client {id.Value}" : "your own player";
        }
    }
}
