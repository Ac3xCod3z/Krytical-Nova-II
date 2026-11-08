#if UNITY_SERVER || UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using KryticalNova.Core;
using KryticalNova.Networking;
using KryticalNova.Networking.Entities;
using UnityEngine;

namespace KryticalNova.Persistence.Entities
{
    /// <summary>
    /// The entity netcode's side of NGO's browser control page (8 Nov 2026 port of ServerControlPage): the password and files of the page,
    /// the members of its status that only NGO-assembly types know (the GMs, the bans in force, the server list, the saves), the backup,
    /// the connection mode and the server list buttons. The words and rules are NGO's (ServerControlPage:171-233, 349-351, 383-393, 510-558).
    /// The server options are <see cref="ServerLaunchOptions"/> (the same <c>server-settings.json</c> the entity server reads).
    /// </summary>
    public sealed class KnPageBrowserHost : KnPageBrowser.IHost
    {
        public const string MapServerAnswer = "This is a map server: players never see it in the list, and it is always reached directly. " +
                                              "The list and the way in are changed on the listed server's control page.";

        ServerLaunchOptions m_Options;
        string m_LastBackup = "none yet";
        List<string> m_Addresses;

        ServerLaunchOptions Options => m_Options ?? (m_Options = ServerLaunchOptions.LoadForThisServer());

        public string Password()
        {
            string password = Options.EnsurePagePassword(out bool isNew);
            if (isNew) Debug.Log("[Server] The control page had no password: one was made and written into server-settings.json (pagePassword).");
            return password;
        }

        public string Folder() => Path.Combine(Options.ServerFolder ?? string.Empty, "ControlPage");

        public string GmNameOf(ulong playerKey)
        {
            if (!ServerAccounts.TryGet(new PlayerKey(playerKey), out ServerAccounts.Entry entry) || string.IsNullOrEmpty(entry.Account)) return string.Empty;
            return GmRoster.FindByAccount(entry.Account)?.name ?? string.Empty;
        }

        public string StatusExtras()
        {
            ServerLaunchOptions o = Options;
            m_Addresses = m_Addresses ?? ControlPageRules.HomeAddresses();
            var masters = new List<string>();
            foreach (GmEntry entry in GmRoster.All)
                masters.Add("{\"name\":" + ControlPageRules.Json(entry.name) + ",\"online\":" + (GmRoster.IsSignedIn(entry.name) ? "true" : "false") +
                            ",\"bound\":" + (entry.IsBound ? "true" : "false") + "}");
            var world = new List<string>();
            foreach (WorldLink.MapServer server in WorldLink.Known ?? new WorldLink.MapServer[0])
                world.Add("{\"name\":" + ControlPageRules.Json(server.name) + ",\"map\":" + ControlPageRules.Json(server.map) +
                          ",\"port\":" + server.port + ",\"players\":" + server.players + "}");
            var sb = new StringBuilder(1024);
            sb.Append("\"port\":").Append(o.port)
              .Append(",\"mapServer\":").Append(o.IsMapServer ? "true" : "false")
              .Append(",\"world\":[").Append(string.Join(",", world))
              .Append("],\"worldProblem\":").Append(ControlPageRules.Json(o.UsesSaveService ? WorldLink.Problem ?? string.Empty : "this server saves to files: it has no world service"))
              .Append(",\"nextBackup\":-1")   // the entity server makes a backup when the page asks, not on a timer yet
              .Append(",\"lastBackup\":").Append(ControlPageRules.Json(m_LastBackup))
              .Append(",\"backupsKept\":").Append(o.backupsKept)
              .Append(",\"addresses\":[");
            for (int i = 0; i < m_Addresses.Count; i++) sb.Append(i > 0 ? "," : string.Empty).Append(ControlPageRules.Json(m_Addresses[i]));
            sb.Append("],\"gms\":[").Append(string.Join(",", masters))
              .Append("],\"bans\":[").Append(PageBansJson())
              .Append("],\"serverList\":[").Append(ListJson())
              .Append("],\"listPublished\":").Append(ControlPageRules.Json(Reach(0)))
              .Append(",\"statusPort\":").Append(o.StatusPort)
              .Append(",\"mode\":").Append(ControlPageRules.Json(o.connectionMode))
              .Append(",\"publicAddress\":").Append(ControlPageRules.Json(Reach(1)))
              .Append(",\"relayCode\":").Append(ControlPageRules.Json(Reach(2)))
              .Append(",\"relayNote\":").Append(ControlPageRules.Json(Reach(3)))
              .Append(",\"autoNote\":").Append(ControlPageRules.Json(Reach(4)))
              .Append(",\"load\":\"\",\"loadSlow\":0")
              .Append(",\"maxGms\":").Append(GmRoster.MaxGameMasters)
              .Append(",\"saves\":").Append(ControlPageRules.Json(SavesText(out bool savesOk)))
              .Append(",\"savesOk\":").Append(savesOk ? "true" : "false");
            return sb.ToString();
        }

        // ServerReach exists in a server build only (it looks the address up and talks to Unity Relay); the entity server does not start it yet
        static string Reach(int field)
        {
#if UNITY_SERVER
            switch (field)
            {
                case 0: return ServerReach.LastPublished;
                case 1: return ServerReach.PublicAddress;
                case 2: return ServerReach.RelayCode;
                case 3: return ServerReach.RelayNote;
                default: return ServerReach.AutoNote;
            }
#else
            return field == 0 ? "-" : string.Empty;
#endif
        }

        static string SavesText(out bool ok)
        {
            if (ServerHooks.Saves != null) return ServerHooks.Saves.Status(out ok);
            ok = false;
            return "saves: not loaded";
        }

        // the page's table: NGO's shape (the account id ends a ban), not the owner API's untilUtc list
        static string PageBansJson()
        {
            DateTime now = DateTime.UtcNow;
            var rows = new List<string>();
            if (ServerHooks.Saves != null)
                foreach (BanEntry ban in ServerBans.All(now))
                    rows.Add("{\"account\":" + ControlPageRules.Json(ban.account) + ",\"character\":" + ControlPageRules.Json(ban.character) +
                             ",\"by\":" + ControlPageRules.Json(ban.by) + ",\"reason\":" + ControlPageRules.Json(ban.reason) +
                             ",\"since\":" + ControlPageRules.Json(new DateTime(ban.sinceUtc, DateTimeKind.Utc).ToLocalTime().ToString("d MMM HH:mm", CultureInfo.InvariantCulture)) +
                             ",\"left\":" + ControlPageRules.Json(ban.IsForever ? "for good" : ServerBans.Describe(new DateTime(ban.untilUtc, DateTimeKind.Utc) - now)) + "}");
            return string.Join(",", rows);
        }

        // ------------------------------------------------------------------ backup

        /// <summary>NGO ServerLifetime.Backup("page") (ServerLifetime.cs:~140-155): saves everyone, copies the saves.</summary>
        public string Backup()
        {
            ServerLaunchOptions o = Options;
            if (o.IsMapServer)
            {
                ServerHooks.Saves?.FlushAll();
                return "Backup: Everyone here is saved. A map server makes no backups: the save service copies the database every night, and the listed server backs up the rest.";
            }
            string line = ServerHooks.Saves != null ? ServerHooks.Saves.Backup(o.BackupFolderPath, o.backupsKept, "page")
                : "[Backup] Nothing to back up: the saves are not loaded.";
            Debug.Log(line);
            if (!line.StartsWith("[Backup] Nothing", StringComparison.Ordinal)) m_LastBackup = line.Replace("[Backup] ", string.Empty);
            return line.Replace("[Backup] ", "Backup: ");
        }

        // ------------------------------------------------------------------ the way in and the list

        public string SwitchMode(string mode, float seconds)
        {
            if (Options.IsMapServer) return MapServerAnswer;
            string wanted = (mode ?? string.Empty).Trim().ToLowerInvariant();
            if (wanted != ServerLaunchOptions.Direct && wanted != ServerLaunchOptions.Relay) return "Choose direct or relay.";
            if (wanted == Options.connectionMode) return $"The server already runs in {wanted} mode.";
            if (!Options.SaveConnectionMode(wanted, out string error)) return error;
            string reason = wanted == ServerLaunchOptions.Relay
                ? "The server is switching to Unity Relay. Join again in a minute."
                : "The server is switching to direct connections. Join again in a minute.";
            float clamped = Mathf.Clamp(seconds, 0f, 3600f);
            KnShutdown.Schedule(clamped, reason, true);
            return $"Saved: {wanted} mode. The server restarts into it in {Mathf.CeilToInt(clamped)} seconds.";
        }

        string ListJson()
        {
            var rows = new List<string>();
            ServerListing[] list = Options.serverList ?? new ServerListing[0];
            for (int i = 0; i < list.Length; i++)
                rows.Add("{\"id\":" + i + ",\"name\":" + ControlPageRules.Json(list[i].name) + ",\"address\":" + ControlPageRules.Json(list[i].address) +
                         ",\"port\":" + list[i].port + ",\"auto\":" + (list[i].IsAuto ? "true" : "false") + "}");
            return string.Join(",", rows);
        }

        public string ListAdd(string name, string address, int port)
        {
            if (Options.IsMapServer) return MapServerAnswer;
            var list = new List<ServerListing>(Options.serverList ?? new ServerListing[0]);
            if (port == 0) port = 7777;
            string problem = ServerListFile.Problem(name, address, port);
            if (problem != null) return problem;
            if (list.Count >= ServerListFile.MaxServers) return $"The list holds {ServerListFile.MaxServers} servers at most.";
            var entry = new ServerListing { name = name.Trim(), address = address.Trim(), port = port };
            foreach (ServerListing other in list)
                if (other.Key == entry.Key) return $"{other.address} : {other.port} is on the list already ({other.name}).";
            list.Add(entry);
            if (!Options.SaveServerList(list.ToArray(), out string error)) return error;
            return $"{entry.name} is on the list. Press PUBLISH LIST so players see it.";
        }

        public string ListRemove(string id)
        {
            if (Options.IsMapServer) return MapServerAnswer;
            var list = new List<ServerListing>(Options.serverList ?? new ServerListing[0]);
            if (!int.TryParse(id, out int index) || index < 0 || index >= list.Count) return "That server is no longer on the list.";
            string gone = list[index].name;
            list.RemoveAt(index);
            if (!Options.SaveServerList(list.ToArray(), out string error)) return error;
            return $"{gone} is off the list. Press PUBLISH LIST so players stop seeing it.";
        }

        public string ListPublish()
        {
            if (Options.IsMapServer) return MapServerAnswer;
#if UNITY_SERVER
            return ServerReach.Publish(Options, out _);
#else
            return "Publishing the list works in the built server only.";
#endif
        }
    }
}
#endif
