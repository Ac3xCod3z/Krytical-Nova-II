using System;
using System.Collections.Generic;
using KryticalNova.Factions;
using KryticalNova.Items;
using Unity.Netcode;

namespace KryticalNova.Economy
{
    /// <summary>One line of an offer: so many of the stack in a Pack cell, as it was when it was offered.</summary>
    [Serializable]
    public struct TradeLine : INetworkSerializable
    {
        /// <summary>The Pack cell it comes from. The other player is sent -1: where it lies is none of their business.</summary>
        public short cell;
        /// <summary>The item, its own state, and how many are offered.</summary>
        public ItemStack stack;

        public TradeLine(int cell, ItemStack stack)
        {
            this.cell = (short)cell;
            this.stack = stack;
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref cell);
            stack.NetworkSerialize(serializer);
        }
    }

    public enum TradeFailure : byte
    {
        None = 0,
        /// <summary>The Pack no longer holds what was offered.</summary>
        OfferChanged,
        /// <summary>No room in the Pack for what would be received.</summary>
        NoRoom,
        NotEnoughCreds,
        /// <summary>More Creds than one trade may move.</summary>
        OverTradeCap,
        /// <summary>More Creds than the account may still send today.</summary>
        OverDailyCap,
        /// <summary>The wallet would go over its cap.</summary>
        WalletFull
    }

    /// <summary>One player's half of a trade, as the rules see it. On success the Pack, wallet and day's total are the new ones.</summary>
    public struct TradeSide
    {
        public ItemStack[] pack;
        public IReadOnlyList<TradeLine> lines;
        /// <summary>Creds offered.</summary>
        public long creds;
        public long wallet;
        /// <summary>Creds this account has already sent in trades today.</summary>
        public long sentToday;
    }

    /// <summary>
    /// The trade rules, free of networking so they can be tested. A trade happens whole or not at all: both Packs
    /// must still hold what was offered and have room for what comes back, and the Creds must be there, within the
    /// per-trade cap, within what the account may still send today, and within the receiver's wallet cap.
    /// The caps are what keeps Creds from being sold for money in bulk; they sit in <c>EconomySettings</c>.
    /// </summary>
    public static class TradeRules
    {
        public const string ReasonAlreadyOffered = "That is already in your offer.";
        public const string ReasonOfferFull = "Your offer is full.";

        /// <summary>Members of one faction trade anywhere; different factions only where a zone allows it.</summary>
        public static bool CanTrade(FactionId a, FactionId b, bool crossFactionZone) => crossFactionZone || a == b;

        /// <summary>What an account has sent today, given what was saved for the day it was saved on.</summary>
        public static long SentOn(long today, long savedDay, long savedSent) => today == savedDay ? Math.Max(0, savedSent) : 0;

        public static long RemainingToday(long sentToday, long perDay) => Math.Max(0, perDay - Math.Max(0, sentToday));

        /// <summary>The most Creds a player may put in an offer right now.</summary>
        public static long MaxCreds(long wallet, long sentToday, long perTrade, long perDay) =>
            Math.Max(0, Math.Min(wallet, Math.Min(perTrade, RemainingToday(sentToday, perDay))));

        /// <summary>Why a Pack cell cannot join an offer (shown to the player), or null.</summary>
        public static string AddBlockedReason(IReadOnlyList<TradeLine> lines, ItemStack[] pack, int cell, int count, int maxLines)
        {
            if (pack == null || cell < 0 || cell >= pack.Length) return InventoryRules.ReasonInvalidLocation;
            if (pack[cell].IsEmpty) return InventoryRules.ReasonEmpty;
            if (count <= 0 || count > pack[cell].count) return StorageRules.ReasonNotThatMany;
            for (int i = 0; i < lines.Count; i++)
                if (lines[i].cell == cell) return ReasonAlreadyOffered;
            return lines.Count >= maxLines ? ReasonOfferFull : null;
        }

        /// <summary>Why this many Creds cannot be offered, or None.</summary>
        public static TradeFailure CredsFailure(long amount, long wallet, long sentToday, long perTrade, long perDay)
        {
            if (amount < 0 || amount > wallet) return TradeFailure.NotEnoughCreds;
            if (amount > perTrade) return TradeFailure.OverTradeCap;
            if (amount > RemainingToday(sentToday, perDay)) return TradeFailure.OverDailyCap;
            return TradeFailure.None;
        }

        /// <summary>
        /// Carries out the trade on the two sides, or changes nothing. On failure <paramref name="failedOnA"/> says
        /// whose side the problem is on.
        /// </summary>
        public static bool TryCommit(ref TradeSide a, ref TradeSide b, IItemLookup lookup, long perTrade, long perDay, long walletCap,
            out TradeFailure failure, out bool failedOnA)
        {
            failedOnA = true;
            if (!Holds(a.pack, a.lines)) { failure = TradeFailure.OfferChanged; return false; }
            failure = CredsFailure(a.creds, a.wallet, a.sentToday, perTrade, perDay);
            if (failure != TradeFailure.None) return false;
            long walletA = a.wallet - a.creds + b.creds;
            if (walletA > walletCap) { failure = TradeFailure.WalletFull; return false; }

            failedOnA = false;
            if (!Holds(b.pack, b.lines)) { failure = TradeFailure.OfferChanged; return false; }
            failure = CredsFailure(b.creds, b.wallet, b.sentToday, perTrade, perDay);
            if (failure != TradeFailure.None) return false;
            long walletB = b.wallet - b.creds + a.creds;
            if (walletB > walletCap) { failure = TradeFailure.WalletFull; return false; }

            // Everything offered leaves first, so a full Pack can still swap one thing for another.
            var packA = (ItemStack[])a.pack.Clone();
            var packB = (ItemStack[])b.pack.Clone();
            Take(packA, a.lines);
            Take(packB, b.lines);
            if (!Give(packA, b.lines, lookup)) { failure = TradeFailure.NoRoom; failedOnA = true; return false; }
            if (!Give(packB, a.lines, lookup)) { failure = TradeFailure.NoRoom; failedOnA = false; return false; }

            Array.Copy(packA, a.pack, packA.Length);
            Array.Copy(packB, b.pack, packB.Length);
            a.wallet = walletA;
            b.wallet = walletB;
            a.sentToday += a.creds;
            b.sentToday += b.creds;
            failure = TradeFailure.None;
            return true;
        }

        /// <summary>True while every line is still in its cell: the same item in the same state, at least as many.</summary>
        public static bool Holds(ItemStack[] pack, IReadOnlyList<TradeLine> lines)
        {
            for (int i = 0; i < lines.Count; i++)
            {
                TradeLine line = lines[i];
                if (line.cell < 0 || line.cell >= pack.Length || line.stack.IsEmpty) return false;
                ItemStack held = pack[line.cell];
                if (held.IsEmpty || !held.StacksWith(line.stack) || held.count < line.stack.count) return false;
                for (int j = 0; j < i; j++)
                    if (lines[j].cell == line.cell) return false;
            }
            return true;
        }

        private static void Take(ItemStack[] pack, IReadOnlyList<TradeLine> lines)
        {
            for (int i = 0; i < lines.Count; i++)
            {
                TradeLine line = lines[i];
                pack[line.cell] = pack[line.cell].WithCount(pack[line.cell].count - line.stack.count);
            }
        }

        private static bool Give(ItemStack[] pack, IReadOnlyList<TradeLine> lines, IItemLookup lookup)
        {
            for (int i = 0; i < lines.Count; i++)
                if (InventoryRules.TryAdd(pack, lookup, lines[i].stack) != lines[i].stack.count) return false;
            return true;
        }
    }
}
