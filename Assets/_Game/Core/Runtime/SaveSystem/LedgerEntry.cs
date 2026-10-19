using System;
using System.Collections.Generic;
using System.Text;

namespace KryticalNova.Core
{
    /// <summary>
    /// One line of the kept ledger (online phase 4, milestone 3): what one account gained or lost in one event, seen from
    /// that account. A trade is two lines (one for each side), saved in the same change as the trade itself, so the ledger
    /// always agrees with the saves. Kept for <c>ledgerDays</c> (the server's settings), then dropped.
    /// </summary>
    [Serializable]
    public sealed class LedgerEntry
    {
        /// <summary>Unique, so a line replayed after a crash is never counted twice.</summary>
        public string id;
        /// <summary>When, in UTC ticks.</summary>
        public long utc;
        /// <summary>What happened (<see cref="LedgerKinds"/>).</summary>
        public string kind;
        public string account;
        public string character;
        public string name;
        /// <summary>The other side, when there is one (the trade partner, the Kiosk's owner, the buyer).</summary>
        public string otherAccount;
        public string otherCharacter;
        public string otherName;
        /// <summary>Creds this account gained (positive) or paid (negative).</summary>
        public long creds;
        /// <summary>Items this account gained (positive counts) or gave up (negative counts).</summary>
        public List<LedgerItem> items = new List<LedgerItem>();
        public string note;

        /// <summary>A new line for <paramref name="account"/>, stamped now.</summary>
        public static LedgerEntry For(string kind, string account, string character, string name) => new LedgerEntry
        {
            id = Guid.NewGuid().ToString("N"),
            utc = DateTime.UtcNow.Ticks,
            kind = kind,
            account = account,
            character = character,
            name = name,
        };

        public LedgerEntry With(int itemId, int count, string itemName = null)
        {
            if (itemId != 0 && count != 0) items.Add(new LedgerItem { item = itemId, count = count, name = itemName });
            return this;
        }

        /// <summary>"18 Oct 14:05 trade Tolan: -500 Creds, -1 Arc Blade (1203), +3 Stim S (40) with Kael".</summary>
        public string Describe()
        {
            var sb = new StringBuilder();
            sb.Append(new DateTime(utc, DateTimeKind.Utc).ToLocalTime().ToString("d MMM HH:mm")).Append(' ').Append(kind).Append(' ')
              .Append(string.IsNullOrEmpty(name) ? character : name).Append(':');
            bool any = false;
            if (creds != 0) { sb.Append(' ').Append(creds > 0 ? "+" : "").Append(creds).Append(" Creds"); any = true; }
            foreach (LedgerItem line in items ?? new List<LedgerItem>())
            {
                sb.Append(any ? ", " : " ").Append(line.count > 0 ? "+" : "").Append(line.count).Append(' ')
                  .Append(string.IsNullOrEmpty(line.name) ? "item" : line.name).Append(" (").Append(line.item).Append(')');
                any = true;
            }
            if (!any) sb.Append(" nothing");
            if (!string.IsNullOrEmpty(otherName) || !string.IsNullOrEmpty(otherAccount))
                sb.Append(" with ").Append(string.IsNullOrEmpty(otherName) ? otherAccount : otherName);
            if (!string.IsNullOrEmpty(note)) sb.Append(" (").Append(note).Append(')');
            return sb.ToString();
        }
    }

    [Serializable]
    public struct LedgerItem
    {
        public int item;
        public int count;
        public string name;
    }

    /// <summary>The kinds of ledger line.</summary>
    public static class LedgerKinds
    {
        public const string Trade = "trade";
        public const string KioskOpen = "kiosk.open";
        public const string KioskBuy = "kiosk.buy";
        public const string KioskCollect = "kiosk.collect";
        public const string ShopBuy = "shop.buy";
        public const string ShopSell = "shop.sell";
        public const string Vault = "vault";
        public const string DeepVault = "deepvault";
        public const string CrewFound = "crew.found";
        public const string Forge = "forge";
        /// <summary>Items picked up off the ground, tallied per save (Creds picked up are in the wallet's tally).</summary>
        public const string Loot = "loot";
        /// <summary>Items a player dropped on the ground, tallied per save.</summary>
        public const string Drop = "drop";
        public const string Gm = "gm";
        /// <summary>Every Cred a wallet earns or pays, tallied per save by where it came from or went to: "creds.trade", "creds.vendor".</summary>
        public const string CredsPrefix = "creds.";
    }

    /// <summary>A store that keeps the ledger: read for a GM, trimmed after the days the server keeps it.</summary>
    public interface ILedgerStore
    {
        /// <summary>The newest lines first: of one account (null: every account), at or after <paramref name="sinceUtc"/>.</summary>
        List<LedgerEntry> ReadLedger(string account, long sinceUtc, int limit);

        /// <summary>Drops every line older than <paramref name="beforeUtc"/>; returns how many went (-1: the store could not be asked).</summary>
        int PruneLedger(long beforeUtc);
    }
}
