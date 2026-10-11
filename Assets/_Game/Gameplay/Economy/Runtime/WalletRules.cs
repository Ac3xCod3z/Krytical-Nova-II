using System;

namespace KryticalNova.Economy
{
    /// <summary>Where Creds come into a wallet from. Append only: the ledger is keyed on these.</summary>
    public enum CredsSource : byte
    {
        Gm = 0,
        Trade = 1,
        KioskSale = 2,
        VaultWithdraw = 3,
        /// <summary>Not paid by anything yet: Hostile drops.</summary>
        HostileKill = 4,
        /// <summary>Not paid by anything yet: Contracts.</summary>
        Contract = 5,
        /// <summary>Not paid by anything yet: selling to an NPC.</summary>
        VendorSale = 6,
        Refund = 7,
    }

    /// <summary>Where Creds leave a wallet to. Append only.</summary>
    public enum CredsSink : byte
    {
        Gm = 0,
        Trade = 1,
        KioskPurchase = 2,
        VaultDeposit = 3,
        CrewFounding = 4,
        /// <summary>Not charged by anything yet: NPC shops and services.</summary>
        Vendor = 5,
        /// <summary>Not charged by anything yet: Tempering, Fusion, Tuning.</summary>
        GearWork = 6,
        /// <summary>Ascension attempts at the Herald.</summary>
        Herald = 7,
        /// <summary>The Holoweaver, the Outfitter, the Stylist, the Reshaper and the Coach.</summary>
        Appearance = 8,
        /// <summary>What the Fabricator takes for a craft.</summary>
        Crafting = 9,
    }

    /// <summary>
    /// The wallet rules, free of networking so they can be tested: a balance never goes below zero or above its
    /// cap, and no amount can overflow. Every Try* leaves the balances alone when it returns false.
    /// </summary>
    public static class WalletRules
    {
        /// <summary>Adds what fits under the cap. Returns how much was added.</summary>
        public static long Add(ref long balance, long amount, long cap)
        {
            if (amount <= 0) return 0;
            long room = Math.Max(0, cap - balance);
            long added = Math.Min(amount, room);
            balance += added;
            return added;
        }

        /// <summary>True when the whole amount fits under the cap.</summary>
        public static bool Fits(long balance, long amount, long cap) => amount >= 0 && amount <= Math.Max(0, cap - balance);

        public static bool CanAfford(long balance, long amount) => amount >= 0 && amount <= balance;

        /// <summary>Takes the whole amount, or nothing.</summary>
        public static bool TrySpend(ref long balance, long amount)
        {
            if (!CanAfford(balance, amount)) return false;
            balance -= amount;
            return true;
        }

        /// <summary>Moves the whole amount from one balance to another, or nothing: the payer must have it and it must fit.</summary>
        public static bool TryTransfer(ref long from, ref long to, long amount, long toCap)
        {
            if (amount < 0 || !CanAfford(from, amount) || !Fits(to, amount, toCap)) return false;
            from -= amount;
            to += amount;
            return true;
        }

        /// <summary>"1,234,567": Creds are always shown with thousands separators and no decimals.</summary>
        public static string Format(long creds) => creds.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>"1,234,567 Creds".</summary>
        public static string FormatCreds(long creds) => Format(creds) + (creds == 1 ? " Cred" : " Creds");

        /// <summary>The day a moment falls in, for allowances that reset every day (UTC).</summary>
        public static long DayOf(DateTime utc) => utc.Ticks / TimeSpan.TicksPerDay;
    }
}
