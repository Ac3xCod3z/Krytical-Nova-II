#if UNITY_SERVER || UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text;
using KryticalNova.Core;
using KryticalNova.Networking;
using KryticalNova.Networking.Entities;
using KryticalNova.Player.Entities.Presentation;
using UnityEngine;

namespace KryticalNova.Persistence.Entities
{
    /// <summary>
    /// The entity netcode's side of the rest of NGO's control page (8 Nov 2026 port of ServerControlPage:346-416): save everyone
    /// (ServerHooks.Saves), announce (ServerHooks.Announce: every server body's player, PlayerChat.ServerAnnounce), ban / unban
    /// (ServerBans: the same list the join check reads), the bans in force, the security flags, and every press in the GM record.
    /// A player picked from the page's list is banned by their PlayerKey (their proven account) and sent away through the kick; a name or an
    /// account id goes through NGO's own <see cref="ServerBans.BanPlayer"/> (not online on NGO: it bans the saved account).
    /// </summary>
    public sealed class KnPageServerDesk : KnPageDesk.IDesk
    {
        /// <summary>Now, for the bans (tests put their own).</summary>
        public Func<DateTime> UtcNow = () => DateTime.UtcNow;

        /// <summary>The character name of a server body (NGO's page: ServerHooks.Describe); null when the player has none yet.</summary>
        public static string CharacterOf(PlayerKey key) =>
            GhostBodyRegistry.TryGetServerBody(key, out GameObject body) && ServerHooks.Describe(body) is { Found: true } facts ? facts.Character : null;

        /// <summary>The level and faction of a server body (NGO ServerControlPage:183-187); 0 and empty when unknown.</summary>
        public static (int level, string faction) FactsOf(PlayerKey key) =>
            GhostBodyRegistry.TryGetServerBody(key, out GameObject body) && ServerHooks.Describe(body) is { Found: true } facts
                ? (facts.Level, facts.Faction ?? string.Empty) : (0, string.Empty);

        public string Save()
        {
            if (ServerHooks.Saves == null) return "Saves are not ready yet.";
            ServerHooks.Saves.FlushAll();
            return "Everyone is saved.";
        }

        public string Announce(string text) =>
            $"Announced to {(ServerHooks.Announce != null ? ServerHooks.Announce(text, "Server") : 0)} player(s).";

        public string Ban(string who, ulong playerKey, string length, string reason, string from)
        {
            if (ServerHooks.Saves == null || !ServerHooks.Saves.HasAuthority) return "Bans are kept by the server.";
            if (!ServerBans.TryParseLength(length, out TimeSpan? span, out string lengthError)) return lengthError;
            if (playerKey == 0)
            {
                // NGO's BanPlayer by name: a player who is on (character name or connection id), else the saved account (NGO's words);
                // NGO's own BanPlayer needs NGO listening (ServerRecords.Keeping), so the entity server does its steps here
                if (string.IsNullOrWhiteSpace(who)) return "Name the player: a character name, a connection id (gm.players) or an account id.";
                who = who.Trim();
                if (!TryFindOnline(who, out playerKey)) return BanSaved(who, span, reason, from);
            }
            var key = new PlayerKey(playerKey);
            if (!PlayerKeys.TryGetConnection(key, out _)) return "That player is no longer here.";
            string character = GhostBodyRegistry.TryGetServerBody(key, out GameObject body) && ServerHooks.Describe(body) is { Found: true } facts
                ? facts.Character : string.Empty;
            if (!ServerAccounts.TryGet(key, out ServerAccounts.Entry entry) || string.IsNullOrEmpty(entry.Account))
                return $"{(character.Length > 0 ? character : "That player")} has no proven account to ban (they joined without Unity sign-in). Kick sends them away.";
            GmEntry gm = GmRoster.FindByAccount(entry.Account);
            if (gm != null) return $"That account is GM {gm.name}: unbind or remove the GM on the control page first.";
            DateTime now = UtcNow();
            BanEntry ban = ServerBans.Ban(entry.Account, character, from, reason, span, now);
            // sent away now with NGO's words (ServerBans.Refusal: why and how long), then the join check refuses them
            KnPlayerRefusals.Enqueue(key, ServerBans.Refusal(ban, now));
            string name = character.Length == 0 ? "Account " + entry.Account : $"{character} (account {entry.Account})";
            return $"{name} is banned {ServerBans.Describe(span)} and was sent away.";
        }

        // a server body whose character is called who (any case) or whose connection id is who
        static bool TryFindOnline(string who, out ulong playerKey)
        {
            playerKey = 0;
            foreach (BodyRoleTag tag in BodyRoleTag.ServerBodies)
            {
                if (tag == null || !PlayerKeys.TryGetConnection(tag.Key, out ulong connection)) continue;
                string character = ServerHooks.Describe(tag.gameObject) is { Found: true } facts ? facts.Character : string.Empty;
                if (who == connection.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    || string.Equals(who, character, StringComparison.OrdinalIgnoreCase)) { playerKey = tag.Key.Value; return true; }
            }
            return false;
        }

        // NGO BanPlayer's not-online half: an account id, or the one saved account with a character of that name
        string BanSaved(string who, TimeSpan? span, string reason, string from)
        {
            string account, character;
            if (SaveKeys.IsId(who.ToLowerInvariant())) { account = who.ToLowerInvariant(); character = string.Empty; }
            else
            {
                List<string> accounts = ServerHooks.Saves.AccountsWithCharacter(who);
                if (accounts.Count == 0) return $"Nobody on or saved here is called \"{who}\". gm.players lists who is on.";
                if (accounts.Count > 1) return $"{accounts.Count} accounts have a character called \"{who}\": ban by account id ({string.Join(", ", accounts)}).";
                account = accounts[0];
                character = who;
            }
            GmEntry gm = GmRoster.FindByAccount(account);
            if (gm != null) return $"That account is GM {gm.name}: unbind or remove the GM on the control page first.";
            ServerBans.Ban(account, character, from, reason, span, UtcNow());
            string name = string.IsNullOrEmpty(character) ? "Account " + account : $"{character} (account {account})";
            return $"{name} is banned {ServerBans.Describe(span)}. They are not on now; they will be turned away when they try to join.";
        }

        public string Unban(string who) => ServerBans.UnbanPlayer(who);

        public string BansJson()
        {
            var sb = new StringBuilder("[");
            if (ServerHooks.Saves != null)
                foreach (BanEntry ban in ServerBans.All(UtcNow()))
                {
                    if (sb.Length > 1) sb.Append(',');
                    sb.Append("{\"account\":").Append(ControlPageRules.Json(ban.account))
                      .Append(",\"character\":").Append(ControlPageRules.Json(ban.character))
                      .Append(",\"by\":").Append(ControlPageRules.Json(ban.by))
                      .Append(",\"reason\":").Append(ControlPageRules.Json(ban.reason))
                      .Append(",\"untilUtc\":").Append(ban.untilUtc).Append('}');
                }
            return sb.Append(']').ToString();
        }

        public string FlagsJson(long after) => SecurityFlags.JsonAfter(after);

        /// <summary>
        /// NGO's GM record line for a button press ("Control page (...): pressed -> answer"). ServerRecords.Write keeps records only while NGO
        /// listens, so the entity server writes the same line into the same file itself (gm.log in the record folder of the server settings).
        /// </summary>
        public void Record(string pressed, string answer)
        {
            string line = $"{KnPageDesk.From}: {pressed} -> {ServerRecords.Short(answer)}";
            Debug.Log("[GM log] " + line);
            string folder = ServerRecords.LogFolder;
            if (string.IsNullOrEmpty(folder) && ServerLaunchOptions.Current != null) folder = ServerLaunchOptions.Current.RecordFolderPath;
            ServerRecords.WriteFile(folder, RecordKind.Gm, line, DateTime.Now);
            Recorded.Add(line);
            if (Recorded.Count > 50) Recorded.RemoveAt(0);
        }

        /// <summary>The last GM record lines written here (tests).</summary>
        public readonly List<string> Recorded = new List<string>();
    }
}
#endif
