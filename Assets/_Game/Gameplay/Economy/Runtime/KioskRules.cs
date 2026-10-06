using System;
using System.Collections.Generic;
using KryticalNova.Items;

namespace KryticalNova.Economy
{
    /// <summary>
    /// The Kiosk rules, free of networking so they can be tested: what a shop may be called, what a price may be,
    /// when a Kiosk runs out, and what a purchase does to the shelf.
    /// </summary>
    public static class KioskRules
    {
        public const int MaxShopNameLength = 24;
        public const string ReasonGone = "That is no longer for sale.";
        public const string ReasonChanged = "That offer changed. Look again before you buy.";
        public const string ReasonNotEnoughCreds = "You don't carry enough Creds.";

        /// <summary>The name over a Kiosk: trimmed, one line, not too long; the owner's name when nothing usable was typed.</summary>
        public static string ShopName(string typed, string ownerName)
        {
            string name = (typed ?? string.Empty).Replace('\n', ' ').Replace('\r', ' ').Replace('<', ' ').Replace('>', ' ').Trim();
            while (name.Contains("  ")) name = name.Replace("  ", " ");
            if (name.Length > MaxShopNameLength) name = name.Substring(0, MaxShopNameLength).Trim();
            return name.Length > 0 ? name : $"{ownerName}'s Kiosk";
        }

        /// <summary>Why a price cannot be asked (shown to the seller), or null.</summary>
        public static string PriceBlockedReason(long unitPrice, long cap)
        {
            if (unitPrice < 1) return "Ask at least 1 Cred.";
            if (unitPrice > cap) return $"The most that can be asked for this is {WalletRules.FormatCreds(cap)}.";
            return null;
        }

        public static long ExpiryTicks(long openedTicks, int days) => openedTicks + TimeSpan.FromDays(Math.Max(1, days)).Ticks;

        public static bool IsExpired(long nowTicks, long expiresTicks) => nowTicks >= expiresTicks;

        /// <summary>"2d 4h", "3h 12m", "45m", "under a minute".</summary>
        public static string TimeLeft(long nowTicks, long expiresTicks)
        {
            TimeSpan left = TimeSpan.FromTicks(Math.Max(0, expiresTicks - nowTicks));
            if (left.TotalDays >= 1) return $"{(int)left.TotalDays}d {left.Hours}h";
            if (left.TotalHours >= 1) return $"{(int)left.TotalHours}h {left.Minutes}m";
            return left.TotalMinutes >= 1 ? $"{(int)left.TotalMinutes}m" : "under a minute";
        }

        /// <summary>What a number of units costs, or false when it would overflow.</summary>
        public static bool TryTotal(long unitPrice, int count, out long total)
        {
            total = 0;
            if (unitPrice < 0 || count <= 0 || unitPrice > long.MaxValue / count) return false;
            total = unitPrice * count;
            return true;
        }

        /// <summary>
        /// Takes <paramref name="count"/> of a listing off the shelf, when it is still what the buyer saw
        /// (<paramref name="expected"/>: the same item in the same state at the same price) and they can pay.
        /// On success <paramref name="bought"/> is what they get and <paramref name="total"/> what they pay;
        /// a listing that runs out is removed. Nothing changes otherwise.
        /// </summary>
        public static bool TryBuy(List<KioskListing> listings, int index, in KioskListing expected, int count, long buyerCreds,
            out ItemStack bought, out long total, out string reason)
        {
            bought = ItemStack.Empty;
            total = 0;
            reason = null;
            if (listings == null || index < 0 || index >= listings.Count) { reason = ReasonGone; return false; }
            KioskListing listing = listings[index];
            if (listing.stack.IsEmpty) { reason = ReasonGone; return false; }
            if (!listing.stack.StacksWith(expected.stack) || listing.unitPrice != expected.unitPrice) { reason = ReasonChanged; return false; }
            if (count <= 0 || count > listing.stack.count) { reason = StorageRules.ReasonNotThatMany; return false; }
            if (!TryTotal(listing.unitPrice, count, out total) || total > buyerCreds) { reason = ReasonNotEnoughCreds; return false; }

            bought = listing.stack.WithCount(count);
            listing.stack = listing.stack.WithCount(listing.stack.count - count);
            if (listing.stack.IsEmpty) listings.RemoveAt(index);
            else listings[index] = listing;
            return true;
        }

        /// <summary>"950", "12.5k", "3.2M": a price short enough to sit on an item cell.</summary>
        public static string ShortPrice(long creds)
        {
            if (creds < 1_000) return creds.ToString();
            if (creds < 1_000_000) return Trim(creds / 1_000.0) + "k";
            if (creds < 1_000_000_000) return Trim(creds / 1_000_000.0) + "M";
            return Trim(creds / 1_000_000_000.0) + "B";
        }

        private static string Trim(double value)
        {
            double shown = value >= 100 ? Math.Floor(value) : Math.Floor(value * 10) / 10;
            return shown.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
