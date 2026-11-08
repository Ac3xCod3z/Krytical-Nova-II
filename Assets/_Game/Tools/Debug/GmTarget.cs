using System.Collections.Generic;
using KryticalNova.Core;
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

        /// <summary>
        /// A server of the entity netcode (M7.4, 2 Nov 2026): NGO is not listening and this process runs the server. A player is then the
        /// connection id <see cref="PlayerKeys"/> knows (the one the GM roster and the records use) and their server body.
        /// </summary>
        private static bool EntityServer => Session == null && NetRole.Process == NetRoleKind.ServerBody;

        /// <summary>The server body of the entity netcode's player on <paramref name="clientId"/>, or null.</summary>
        private static GameObject EntityBody(ulong clientId)
        {
            BodyRoleTag body = BodyRoleTag.ServerBodyOf(PlayerKeys.Find(clientId));
            return body != null ? body.gameObject : null;
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
        public static ulong? ClientId => hasTarget && (Session != null || EntityServer) ? targetClientId : (ulong?)null;

        /// <summary>Acts on this machine's own player again.</summary>
        public static void TargetSelf() => hasTarget = false;

        /// <summary>Picks a connected player by client id (host / server only).</summary>
        public static bool TrySetTarget(ulong clientId, out string error)
        {
            error = null;
            var nm = Session;
            if (nm == null && EntityServer)
            {
                if (EntityBody(clientId) == null) { error = $"No connected player with client id {clientId}. Try gm.players."; return false; }
                hasTarget = true;
                targetClientId = clientId;
                return true;
            }
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
            if (nm == null && EntityServer)
            {
                // a server of the entity netcode has no player of its own: only a picked player (a GM's line picks its own), or, with no
                // pick, the Play DOTS here host's own (GmRoster.Host: only the Editor's in-process host sets it, never a built server), so
                // the GM Tools window works there without a gm.target first (owner, 8 Nov 2026)
                GameObject body = hasTarget ? EntityBody(targetClientId) : null;
                if (body != null) { player = body; return true; }
                hasTarget = false;
                if (KryticalNova.Networking.GmRoster.Host is ulong host && EntityBody(host) is GameObject own) { player = own; return true; }
                error = "No local player on this server. Pick one with gm.target <clientId> (see gm.players).";
                return false;
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
            if (nm == null && EntityServer)
            {
                foreach (BodyRoleTag body in BodyRoleTag.ServerBodies)
                    if (body != null && PlayerKeys.TryGetConnection(body.Key, out ulong id))
                        result.Add((id, body.gameObject));
                return result;
            }
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
