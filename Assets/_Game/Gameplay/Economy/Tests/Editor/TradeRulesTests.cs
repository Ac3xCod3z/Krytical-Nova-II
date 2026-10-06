using System.Collections.Generic;
using KryticalNova.Economy;
using KryticalNova.Factions;
using KryticalNova.Items;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Tests
{
    public sealed class TradeRulesTests
    {
        private sealed class Lookup : IItemLookup
        {
            public readonly Dictionary<ushort, ItemDefinition> Items = new Dictionary<ushort, ItemDefinition>();
            public ItemDefinition Get(ushort id) => Items.TryGetValue(id, out var d) ? d : null;
        }

        private const ushort Sword = 1001, Armor = 2001, Stim = 9001, Pet = 8001;
        private const long PerTrade = 5_000_000, PerDay = 20_000_000, Cap = 999_999_999_999;
        private Lookup lookup;
        private TradeSide a, b;
        private List<TradeLine> linesA, linesB;

        [SetUp]
        public void SetUp()
        {
            lookup = new Lookup();
            Add(Sword, 1);
            Add(Armor, 1);
            Add(Pet, 1);
            Add(Stim, 20);
            linesA = new List<TradeLine>();
            linesB = new List<TradeLine>();
            a = new TradeSide { pack = new ItemStack[4], lines = linesA, wallet = 10_000_000 };
            b = new TradeSide { pack = new ItemStack[4], lines = linesB, wallet = 1_000 };
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var d in lookup.Items.Values) Object.DestroyImmediate(d);
        }

        private void Add(ushort id, ushort maxStack)
        {
            var d = ScriptableObject.CreateInstance<ItemDefinition>();
            d.itemId = id; d.displayName = "Item " + id; d.maxStack = maxStack;
            lookup.Items[id] = d;
        }

        private bool Commit(out TradeFailure failure, out bool onA) =>
            TradeRules.TryCommit(ref a, ref b, lookup, PerTrade, PerDay, Cap, out failure, out onA);

        [Test]
        public void OneFaction_TradesAnywhere_TwoFactionsOnlyWhereAZoneAllowsIt()
        {
            Assert.IsTrue(TradeRules.CanTrade(FactionId.Hegemony, FactionId.Hegemony, false));
            Assert.IsFalse(TradeRules.CanTrade(FactionId.Hegemony, FactionId.Null, false));
            Assert.IsTrue(TradeRules.CanTrade(FactionId.Hegemony, FactionId.Null, true));
        }

        [Test]
        public void ItemsAndCreds_ChangeHands_BothWays()
        {
            a.pack[0] = new ItemStack(Sword, 1);
            a.pack[1] = new ItemStack(Stim, 10);
            b.pack[2] = new ItemStack(Pet, 1, 0xBEEF);
            linesA.Add(new TradeLine(0, a.pack[0]));
            linesA.Add(new TradeLine(1, new ItemStack(Stim, 4)));
            linesB.Add(new TradeLine(2, b.pack[2]));
            a.creds = 2_000_000;
            b.creds = 500;

            Assert.IsTrue(Commit(out TradeFailure failure, out _), failure.ToString());
            Assert.AreEqual(new ItemStack(Pet, 1, 0xBEEF), a.pack[0], "the pet arrives with its own state");
            Assert.AreEqual(6, a.pack[1].count, "part of a stack was offered: the rest stays");
            Assert.AreEqual(new ItemStack(Sword, 1), b.pack[0]);
            Assert.AreEqual(new ItemStack(Stim, 4), b.pack[1]);
            Assert.IsTrue(b.pack[2].IsEmpty);
            Assert.AreEqual(10_000_000 - 2_000_000 + 500, a.wallet);
            Assert.AreEqual(1_000 - 500 + 2_000_000, b.wallet);
            Assert.AreEqual(2_000_000, a.sentToday);
            Assert.AreEqual(500, b.sentToday);
        }

        [Test]
        public void AFullPack_CanStillSwapOneThingForAnother()
        {
            for (int i = 0; i < 4; i++) a.pack[i] = new ItemStack(Sword, 1);
            for (int i = 0; i < 4; i++) b.pack[i] = new ItemStack(Armor, 1);
            linesA.Add(new TradeLine(3, a.pack[3]));
            linesB.Add(new TradeLine(0, b.pack[0]));
            Assert.IsTrue(Commit(out _, out _));
            Assert.AreEqual(new ItemStack(Armor, 1), a.pack[3]);
            Assert.AreEqual(new ItemStack(Sword, 1), b.pack[0]);
        }

        [Test]
        public void NoRoomForWhatComesBack_NothingMoves_AndItSaysWhoseSide()
        {
            for (int i = 0; i < 4; i++) b.pack[i] = new ItemStack(Armor, 1);
            a.pack[0] = new ItemStack(Sword, 1);
            linesA.Add(new TradeLine(0, a.pack[0]));
            a.creds = 100;

            Assert.IsFalse(Commit(out TradeFailure failure, out bool onA));
            Assert.AreEqual(TradeFailure.NoRoom, failure);
            Assert.IsFalse(onA, "it is B who has no room");
            Assert.AreEqual(new ItemStack(Sword, 1), a.pack[0]);
            Assert.AreEqual(10_000_000, a.wallet);
            Assert.AreEqual(0, a.sentToday);
        }

        [Test]
        public void AnOfferThatLeftThePack_StopsTheTrade()
        {
            a.pack[0] = new ItemStack(Sword, 1);
            linesA.Add(new TradeLine(0, a.pack[0]));
            a.pack[0] = new ItemStack(Armor, 1); // swapped for something else after offering
            Assert.IsFalse(Commit(out TradeFailure failure, out bool onA));
            Assert.AreEqual(TradeFailure.OfferChanged, failure);
            Assert.IsTrue(onA);
        }

        [Test]
        public void TheSameItemInAnotherState_IsNotWhatWasOffered()
        {
            a.pack[0] = new ItemStack(Pet, 1, 100);
            linesA.Add(new TradeLine(0, a.pack[0]));
            a.pack[0] = new ItemStack(Pet, 1, 1); // a younger pet put in its place
            Assert.IsFalse(Commit(out TradeFailure failure, out _));
            Assert.AreEqual(TradeFailure.OfferChanged, failure);
        }

        [Test]
        public void FewerThanOffered_StopsTheTrade()
        {
            a.pack[0] = new ItemStack(Stim, 10);
            linesA.Add(new TradeLine(0, new ItemStack(Stim, 10)));
            a.pack[0] = new ItemStack(Stim, 9);
            Assert.IsFalse(Commit(out TradeFailure failure, out _));
            Assert.AreEqual(TradeFailure.OfferChanged, failure);
        }

        [Test]
        public void CredsAreCappedPerTrade()
        {
            a.creds = PerTrade + 1;
            Assert.IsFalse(Commit(out TradeFailure failure, out bool onA));
            Assert.AreEqual(TradeFailure.OverTradeCap, failure);
            Assert.IsTrue(onA);
            a.creds = PerTrade;
            Assert.IsTrue(Commit(out _, out _));
        }

        [Test]
        public void CredsAreCappedPerDay_AcrossTrades()
        {
            a.wallet = 100_000_000;
            a.sentToday = PerDay - 1_000_000;
            a.creds = 1_000_001;
            Assert.IsFalse(Commit(out TradeFailure failure, out _));
            Assert.AreEqual(TradeFailure.OverDailyCap, failure);
            a.creds = 1_000_000;
            Assert.IsTrue(Commit(out _, out _));
            Assert.AreEqual(PerDay, a.sentToday);
            Assert.AreEqual(0, TradeRules.RemainingToday(a.sentToday, PerDay));
        }

        [Test]
        public void TheDayTotal_StartsAgainOnANewDay()
        {
            Assert.AreEqual(7_000_000, TradeRules.SentOn(today: 100, savedDay: 100, savedSent: 7_000_000));
            Assert.AreEqual(0, TradeRules.SentOn(today: 101, savedDay: 100, savedSent: 7_000_000));
        }

        [Test]
        public void TheMostThatCanBeOffered_IsTheSmallestOfWalletTradeCapAndDayLeft()
        {
            Assert.AreEqual(1_000, TradeRules.MaxCreds(1_000, 0, PerTrade, PerDay));
            Assert.AreEqual(PerTrade, TradeRules.MaxCreds(50_000_000, 0, PerTrade, PerDay));
            Assert.AreEqual(250_000, TradeRules.MaxCreds(50_000_000, PerDay - 250_000, PerTrade, PerDay));
            Assert.AreEqual(0, TradeRules.MaxCreds(50_000_000, PerDay, PerTrade, PerDay));
        }

        [Test]
        public void MoreCredsThanCarried_IsRefused()
        {
            b.creds = 1_001;
            Assert.IsFalse(Commit(out TradeFailure failure, out bool onA));
            Assert.AreEqual(TradeFailure.NotEnoughCreds, failure);
            Assert.IsFalse(onA);
        }

        [Test]
        public void AWalletNeverGoesOverItsCap()
        {
            b.wallet = Cap - 10;
            a.creds = 11;
            Assert.IsFalse(Commit(out TradeFailure failure, out bool onA));
            Assert.AreEqual(TradeFailure.WalletFull, failure);
            Assert.IsFalse(onA);
        }

        [Test]
        public void AnOffer_TakesEachCellOnce_UpToItsLimit()
        {
            a.pack[0] = new ItemStack(Stim, 10);
            a.pack[1] = new ItemStack(Sword, 1);
            Assert.IsNull(TradeRules.AddBlockedReason(linesA, a.pack, 0, 10, 2));
            Assert.AreEqual(StorageRules.ReasonNotThatMany, TradeRules.AddBlockedReason(linesA, a.pack, 0, 11, 2));
            Assert.AreEqual(InventoryRules.ReasonEmpty, TradeRules.AddBlockedReason(linesA, a.pack, 2, 1, 2));
            linesA.Add(new TradeLine(0, a.pack[0]));
            Assert.AreEqual(TradeRules.ReasonAlreadyOffered, TradeRules.AddBlockedReason(linesA, a.pack, 0, 1, 2));
            linesA.Add(new TradeLine(1, a.pack[1]));
            a.pack[2] = new ItemStack(Armor, 1);
            Assert.AreEqual(TradeRules.ReasonOfferFull, TradeRules.AddBlockedReason(linesA, a.pack, 2, 1, 2));
        }
    }
}
