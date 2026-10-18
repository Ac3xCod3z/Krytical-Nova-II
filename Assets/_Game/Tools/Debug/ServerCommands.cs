using System;
using KryticalNova.Networking;
using KryticalNova.Player;
using QFSW.QC;
using Unity.Netcode;
using UnityEngine;

namespace KryticalNova.DevTools
{
    /// <summary>
    /// GM commands for running a server: send a player away, ban an account, stop the server with a countdown, back up the saves.
    /// They need no player of the GM's own, so they work in a dedicated server's console window (typed there, see
    /// <c>ServerConsoleInput</c>) as well as on a host. Shutdown and backup belong to a dedicated server.
    /// </summary>
    public static class ServerCommands
    {
        [Command("gm.kick", "Send a player away from the server: gm.kick <client id or character name> \"reason\". " +
                            "They can join again (gm.ban keeps them out). gm.players lists who is here.")]
        private static string Kick(string who, string reason = "")
        {
            if (!GmTarget.HasAuthority(out string error)) return error;
            NetworkManager network = NetworkManager.Singleton;
            if (network == null || !network.IsServer) return "No session is running here.";
            if (!TryFind(who, out ulong clientId, out string name)) return $"No connected player called \"{who}\". Try gm.players.";
            if (clientId == NetworkManager.ServerClientId) return "That is the host's own player: the host cannot be sent away.";
            string told = string.IsNullOrWhiteSpace(reason) ? "A GM removed you from the server." : "A GM removed you from the server: " + reason.Trim();
            string removed = ServerRecords.Who(clientId);
            network.DisconnectClient(clientId, told);
            return $"Removed {removed}.";
        }

        [Command("gm.ban", "Keep a player's account off this server: gm.ban <character name, connection id or account id> <length> " +
                           "\"reason\". Length: 30m, 12h, 3d, 2w (a number alone is days) or forever. A player who is on is sent " +
                           "away; one who is not is turned away when they try to join, and reads the reason and how long is left. " +
                           "GMs cannot be banned. gm.bans lists the bans, gm.unban ends one.")]
        private static string Ban(string who, string length = "forever", string reason = "")
        {
            if (!GmTarget.HasAuthority(out string error)) return error;
            return ServerBans.BanPlayer(who, length, reason, GmCommandRelay.RunningFor);
        }

        [Command("gm.unban", "End a ban early: gm.unban <character name or account id> (gm.bans lists them).")]
        private static string Unban(string who)
        {
            if (!GmTarget.HasAuthority(out string error)) return error;
            return ServerBans.UnbanPlayer(who);
        }

        [Command("gm.bans", "List the bans in force: the character, the account, the time left, who banned them and why.")]
        private static string Bans()
        {
            if (!GmTarget.HasAuthority(out string error)) return error;
            return ServerBans.Listing(DateTime.UtcNow);
        }

        [Command("gm.shutdown", "Stop the dedicated server after a countdown the players are told about, saving everyone and backing " +
                                "up first: gm.shutdown <seconds> \"reason\" (60 if none given). gm.shutdown cancel calls it off. " +
                                "Closing the server's window or Ctrl+C also saves first, without the countdown.")]
        private static string Shutdown(string seconds = "60", string reason = "")
        {
            if (!GmTarget.HasAuthority(out string error)) return error;
#if UNITY_SERVER
            if (string.Equals(seconds, "cancel", StringComparison.OrdinalIgnoreCase)) return KryticalNova.Networking.ServerLifetime.CancelShutdown();
            if (!float.TryParse(seconds, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float wait))
                return $"\"{seconds}\" is not a number of seconds.";
            return KryticalNova.Networking.ServerLifetime.ScheduleShutdown(wait, reason);
#else
            return "Only a dedicated server shuts down this way. A host leaves from the game menu.";
#endif
        }

        [Command("gm.restart", "Restart the dedicated server after a countdown the players are told about: it saves, backs up, stops " +
                               "and starts again (through Start Server.bat): gm.restart <seconds> \"reason\" (60 if none given). " +
                               "gm.shutdown cancel calls it off.")]
        private static string Restart(string seconds = "60", string reason = "")
        {
            if (!GmTarget.HasAuthority(out string error)) return error;
#if UNITY_SERVER
            if (!float.TryParse(seconds, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float wait))
                return $"\"{seconds}\" is not a number of seconds.";
            return KryticalNova.Networking.ServerLifetime.ScheduleShutdown(wait, reason, restart: true);
#else
            return "Only a dedicated server restarts this way.";
#endif
        }

        [Command("gm.backup", "Back up the dedicated server's saves now, after saving everyone (do it before an update). " +
                              "Backups also happen at start, at stop and every few hours (server-settings.json).")]
        private static string Backup()
        {
            if (!GmTarget.HasAuthority(out string error)) return error;
#if UNITY_SERVER
            return KryticalNova.Networking.ServerLifetime.Backup("gm");
#else
            return "Backups are made by a dedicated server.";
#endif
        }

        // A client id, or a character's name (whole, any case).
        private static bool TryFind(string who, out ulong clientId, out string name)
        {
            clientId = 0;
            name = null;
            if (string.IsNullOrWhiteSpace(who)) return false;
            who = who.Trim();
            foreach (var (id, player) in GmTarget.ConnectedPlayers())
            {
                string character = player.TryGetComponent(out PlayerIdentity identity) ? identity.CharacterName : player.name;
                if (who == id.ToString() || string.Equals(who, character, StringComparison.OrdinalIgnoreCase))
                {
                    clientId = id;
                    name = character;
                    return true;
                }
            }
            return false;
        }
    }
}
