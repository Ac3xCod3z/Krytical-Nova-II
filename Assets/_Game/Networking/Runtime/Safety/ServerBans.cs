using System;
using System.Collections.Generic;
using System.Globalization;
using KryticalNova.Core;
using KryticalNova.Persistence;
using UnityEngine;

namespace KryticalNova.Networking
{
    /// <summary>One banned account: who it was, who banned it, why, and until when.</summary>
    [Serializable]
    public sealed class BanEntry
    {
        public string account = string.Empty;
        /// <summary>The character it played when banned (for people to read; the ban is on the account).</summary>
        public string character = string.Empty;
        public string by = string.Empty;
        public string reason = string.Empty;
        /// <summary>UTC ticks of the ban.</summary>
        public long sinceUtc;
        /// <summary>UTC ticks of the end of the ban; 0 means for good.</summary>
        public long untilUtc;

        public bool IsForever => untilUtc == 0;
        public bool IsOver(DateTime utcNow) => !IsForever && utcNow.Ticks >= untilUtc;
    }

    /// <summary>
    /// Banned accounts (online phase 2, milestone 4). A ban is on the account Unity's sign-in proves, so a new character
    /// or a reinstall of the game does not get round it. The list is the <c>world/bans</c> save document. A banned
    /// account is turned away when it asks to join, with the reason and how long is left; banning a player who is on
    /// sends them away at once. A ban runs for a time (30m, 12h, 3d, 2w) or for good, and ends early with gm.unban.
    /// Runs where the game is decided.
    /// </summary>
    public static class ServerBans
    {
        public const string Document = "bans";
        public const int LongestReason = 200;
        private const int LongestDays = 3650;

        [Serializable]
        private sealed class BanList
        {
            public List<BanEntry> bans = new List<BanEntry>();
        }

        private static BanList List => ServerSave.World<BanList>(Document);

        /// <summary>The bans in force now, oldest first (bans that ran out are dropped here).</summary>
        public static IReadOnlyList<BanEntry> All(DateTime utcNow)
        {
            DropOver(utcNow);
            return List.bans;
        }

        /// <summary>True, with the ban, when <paramref name="account"/> is banned now.</summary>
        public static bool IsBanned(string account, DateTime utcNow, out BanEntry ban)
        {
            ban = null;
            if (string.IsNullOrEmpty(account)) return false;
            foreach (BanEntry entry in List.bans)
                if (entry.account == account && !entry.IsOver(utcNow))
                {
                    ban = entry;
                    return true;
                }
            return false;
        }

        /// <summary>
        /// Bans <paramref name="account"/> for <paramref name="length"/> (null: for good). A ban already on the account is
        /// replaced. Returns the ban.
        /// </summary>
        public static BanEntry Ban(string account, string character, string by, string reason, TimeSpan? length, DateTime utcNow)
        {
            reason = (reason ?? string.Empty).Trim();
            if (reason.Length > LongestReason) reason = reason.Substring(0, LongestReason);
            List.bans.RemoveAll(b => b.account == account);
            var ban = new BanEntry
            {
                account = account,
                character = character ?? string.Empty,
                by = by ?? string.Empty,
                reason = reason,
                sinceUtc = utcNow.Ticks,
                untilUtc = length.HasValue ? (utcNow + length.Value).Ticks : 0,
            };
            List.bans.Add(ban);
            ServerSave.SaveWorld(Document);
            return ban;
        }

        /// <summary>
        /// Ends the ban of an account, named by its id or by the character it was banned on. True with the ban that ended;
        /// false when no ban matches.
        /// </summary>
        public static bool Unban(string who, DateTime utcNow, out BanEntry ended)
        {
            ended = null;
            if (string.IsNullOrWhiteSpace(who)) return false;
            who = who.Trim();
            foreach (BanEntry entry in All(utcNow))
                if (entry.account == who.ToLowerInvariant() || string.Equals(entry.character, who, StringComparison.OrdinalIgnoreCase))
                {
                    ended = entry;
                    break;
                }
            if (ended == null) return false;
            List.bans.Remove(ended);
            ServerSave.SaveWorld(Document);
            return true;
        }

        /// <summary>What a banned player reads when they are turned away or sent off.</summary>
        public static string Refusal(BanEntry ban, DateTime utcNow)
        {
            string why = string.IsNullOrEmpty(ban.reason) ? "." : ": " + ban.reason;
            if (ban.IsForever) return "This account is banned from this server" + why;
            DateTime until = new DateTime(ban.untilUtc, DateTimeKind.Utc);
            return $"This account is banned from this server for {Describe(until - utcNow)} more (until " +
                   until.ToLocalTime().ToString("d MMM yyyy, HH:mm", CultureInfo.InvariantCulture) + " server time)" + why;
        }

        /// <summary>"for good" or "3 days", "12 hours", "2 days 4 hours": a ban's length for people to read.</summary>
        public static string Describe(TimeSpan? length)
        {
            if (!length.HasValue) return "for good";
            TimeSpan span = length.Value < TimeSpan.Zero ? TimeSpan.Zero : length.Value;
            if (span.TotalHours < 1) return Plural(Math.Max(1, (int)Math.Ceiling(span.TotalMinutes)), "minute");
            if (span.TotalDays < 1) return Plural((int)Math.Ceiling(span.TotalHours), "hour");
            int days = (int)span.TotalDays, hours = span.Hours;
            return hours == 0 ? Plural(days, "day") : Plural(days, "day") + " " + Plural(hours, "hour");
        }

        /// <summary>
        /// Reads a ban length: <c>30m</c>, <c>12h</c>, <c>3d</c>, <c>2w</c>, a bare number of days, or empty / <c>forever</c>
        /// for good (null). False with the reason when it is not a length.
        /// </summary>
        public static bool TryParseLength(string text, out TimeSpan? length, out string error)
        {
            length = null;
            error = null;
            string t = (text ?? string.Empty).Trim().ToLowerInvariant();
            if (t.Length == 0 || t == "forever" || t == "permanent" || t == "perm" || t == "0") return true;
            char unit = t[t.Length - 1];
            string number = char.IsDigit(unit) ? t : t.Substring(0, t.Length - 1);
            if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out double amount) || amount <= 0 || double.IsInfinity(amount))
            {
                error = $"\"{text}\" is not a ban length: use 30m, 12h, 3d, 2w (a number alone is days) or forever.";
                return false;
            }
            TimeSpan span;
            switch (char.IsDigit(unit) ? 'd' : unit)
            {
                case 'm': span = TimeSpan.FromMinutes(Math.Min(amount, LongestDays * 1440d)); break;
                case 'h': span = TimeSpan.FromHours(Math.Min(amount, LongestDays * 24d)); break;
                case 'd': span = TimeSpan.FromDays(Math.Min(amount, LongestDays)); break;
                case 'w': span = TimeSpan.FromDays(Math.Min(amount * 7d, LongestDays)); break;
                default:
                    error = $"\"{text}\" is not a ban length: use 30m, 12h, 3d, 2w (a number alone is days) or forever.";
                    return false;
            }
            if (span < TimeSpan.FromMinutes(1)) span = TimeSpan.FromMinutes(1);
            length = span;
            return true;
        }

        /// <summary>One line per ban in force, for gm.bans.</summary>
        public static string Listing(DateTime utcNow)
        {
            IReadOnlyList<BanEntry> bans = All(utcNow);
            if (bans.Count == 0) return "Nobody is banned.";
            var lines = new List<string> { $"{bans.Count} banned:" };
            foreach (BanEntry ban in bans)
            {
                string left = ban.IsForever ? "for good" : Describe(new DateTime(ban.untilUtc, DateTimeKind.Utc) - utcNow) + " left";
                lines.Add($"  {(string.IsNullOrEmpty(ban.character) ? "(no character)" : ban.character)}  account {ban.account}  {left}" +
                          $"  by {ban.by}{(string.IsNullOrEmpty(ban.reason) ? string.Empty : ": " + ban.reason)}");
            }
            return string.Join("\n", lines);
        }

        // ------------------------------------------------------------------ the actions (gm.ban, gm.unban, the control page)

        /// <summary>
        /// Bans a player, named by connection id or character name when they are on, or by character name or account id
        /// when they are not, for <paramref name="lengthText"/> (see <see cref="TryParseLength"/>). A player who is on is
        /// sent away with the reason. GMs and the host cannot be banned. <paramref name="by"/> is who did it, kept with
        /// the ban. Returns the answer for whoever asked, with the account: the GM record writes it beside the command.
        /// </summary>
        public static string BanPlayer(string who, string lengthText, string reason, string by)
        {
            if (!ServerRecords.Keeping) return "No session is running here.";
            if (string.IsNullOrWhiteSpace(who)) return "Name the player: a character name, a connection id (gm.players) or an account id.";
            if (!TryParseLength(lengthText, out TimeSpan? length, out string lengthError)) return lengthError;
            who = who.Trim();

            ulong clientId = 0;
            string account, character;
            bool online = TryFindOnline(who, out clientId, out account, out character);
            if (!online)
            {
                if (SaveKeys.IsId(who.ToLowerInvariant()))
                {
                    account = who.ToLowerInvariant();
                    character = string.Empty;
                }
                else
                {
                    List<string> accounts = ServerSave.AccountsWithCharacter(who);
                    if (accounts.Count == 0) return $"Nobody on or saved here is called \"{who}\". gm.players lists who is on.";
                    if (accounts.Count > 1) return $"{accounts.Count} accounts have a character called \"{who}\": ban by account id ({string.Join(", ", accounts)}).";
                    account = accounts[0];
                    character = who;
                }
            }
            if (online && clientId == Unity.Netcode.NetworkManager.ServerClientId) return "That is the host's own player: the host cannot be banned.";
            if (string.IsNullOrEmpty(account)) return $"{character} has no proven account to ban (they joined without Unity sign-in). gm.kick sends them away.";
            GmEntry gm = GmRoster.FindByAccount(account);
            if (gm != null) return $"That account is GM {gm.name}: unbind or remove the GM on the control page first.";

            DateTime now = DateTime.UtcNow;
            BanEntry ban = Ban(account, character, by, reason, length, now);
            string name = string.IsNullOrEmpty(character) ? "Account " + account : $"{character} (account {account})";
            if (online)
            {
                Unity.Netcode.NetworkManager.Singleton.DisconnectClient(clientId, Refusal(ban, now));
                return $"{name} is banned {Describe(length)} and was sent away.";
            }
            return $"{name} is banned {Describe(length)}. They are not on now; they will be turned away when they try to join.";
        }

        /// <summary>Ends a ban, named by account id or by the character it was put on. Returns the answer, with the account.</summary>
        public static string UnbanPlayer(string who)
        {
            if (!ServerSave.HasAuthority) return "Bans are kept by the server.";
            if (string.IsNullOrWhiteSpace(who)) return "Name the ban: the character name or the account id (gm.bans lists them).";
            if (!Unban(who, DateTime.UtcNow, out BanEntry ended)) return $"No ban for \"{who.Trim()}\". gm.bans lists them.";
            string name = string.IsNullOrEmpty(ended.character) ? "Account " + ended.account : $"{ended.character} (account {ended.account})";
            return $"{name} is no longer banned and may join again.";
        }

        // A connection id or a character name (whole, any case) of a player who is on.
        private static bool TryFindOnline(string who, out ulong clientId, out string account, out string character)
        {
            clientId = 0;
            account = character = null;
            Unity.Netcode.NetworkManager network = Unity.Netcode.NetworkManager.Singleton;
            if (network == null || !network.IsServer) return false;
            foreach (Unity.Netcode.NetworkClient client in network.ConnectedClientsList)
            {
                if (client.PlayerObject == null) continue;
                KryticalNova.Player.PlayerIdentity identity = client.PlayerObject.GetComponent<KryticalNova.Player.PlayerIdentity>();
                string name = identity != null ? identity.CharacterName : client.PlayerObject.name;
                if (who != client.ClientId.ToString(CultureInfo.InvariantCulture) && !string.Equals(who, name, StringComparison.OrdinalIgnoreCase)) continue;
                clientId = client.ClientId;
                character = name;
                // The proven account; on an Editor host test without sign-in, the one the save is kept under.
                account = ServerAccounts.TryGet(clientId, out ServerAccounts.Entry proven) ? proven.Account
                    : identity != null ? identity.AccountId : string.Empty;
                return true;
            }
            return false;
        }

        private static void DropOver(DateTime utcNow)
        {
            if (List.bans.RemoveAll(b => b == null || b.IsOver(utcNow)) > 0) ServerSave.SaveWorld(Document);
        }

        private static string Plural(int n, string word) => n == 1 ? "1 " + word : n + " " + word + "s";
    }
}
