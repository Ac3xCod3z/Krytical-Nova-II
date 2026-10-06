using System;
using System.Collections.Generic;
using KryticalNova.Economy;
using KryticalNova.Items;
using NUnit.Framework;

namespace KryticalNova.Tests
{
    public sealed class KioskRulesTests
    {
        private const ushort Sword = 1001, Stim = 9001, Pet = 8001;
        private List<KioskListing> shelf;

        [SetUp]
        public void SetUp()
        {
            shelf = new List<KioskListing>
            {
                new KioskListing(new ItemStack(Sword, 1), 40_000),
                new KioskListing(new ItemStack(Stim, 12), 950),
                new KioskListing(new ItemStack(Pet, 1, 0xBEEF), 2_000_000),
            };
        }

        [Test]
        public void AShopName_IsTidied_AndFallsBackToTheOwner()
        {
            Assert.AreEqual("Tolan's Kiosk", KioskRules.ShopName("   ", "Tolan"));
            Assert.AreEqual("Tolan's Kiosk", KioskRules.ShopName(null, "Tolan"));
            Assert.AreEqual("Cheap Stims Here", KioskRules.ShopName("  Cheap   Stims\nHere ", "Tolan"));
            Assert.AreEqual(KioskRules.MaxShopNameLength, KioskRules.ShopName(new string('x', 80), "Tolan").Length);
            Assert.IsFalse(KioskRules.ShopName("<b>Sale</b>", "Tolan").Contains("<"), "no rich text in a shop name");
        }

        [Test]
        public void APrice_IsAtLeastOne_AndAtMostTheCap()
        {
            Assert.IsNotNull(KioskRules.PriceBlockedReason(0, 50_000));
            Assert.IsNull(KioskRules.PriceBlockedReason(1, 50_000));
            Assert.IsNull(KioskRules.PriceBlockedReason(50_000, 50_000));
            Assert.IsNotNull(KioskRules.PriceBlockedReason(50_001, 50_000));
        }

        [Test]
        public void AKiosk_ClosesWhenItsDaysAreUp()
        {
            long opened = new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc).Ticks;
            long expires = KioskRules.ExpiryTicks(opened, 3);
            Assert.AreEqual(new DateTime(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc).Ticks, expires);
            Assert.IsFalse(KioskRules.IsExpired(expires - 1, expires));
            Assert.IsTrue(KioskRules.IsExpired(expires, expires));
        }

        [Test]
        public void TimeLeft_ReadsInTheLargestTwoUnits()
        {
            long now = 0;
            Assert.AreEqual("2d 4h", KioskRules.TimeLeft(now, TimeSpan.FromHours(52.5).Ticks));
            Assert.AreEqual("3h 12m", KioskRules.TimeLeft(now, TimeSpan.FromMinutes(192).Ticks));
            Assert.AreEqual("45m", KioskRules.TimeLeft(now, TimeSpan.FromMinutes(45.5).Ticks));
            Assert.AreEqual("under a minute", KioskRules.TimeLeft(now, TimeSpan.FromSeconds(20).Ticks));
            Assert.AreEqual("under a minute", KioskRules.TimeLeft(now, -5));
        }

        [Test]
        public void BuyingPartOfAStack_LeavesTheRestOnTheShelf()
        {
            Assert.IsTrue(KioskRules.TryBuy(shelf, 1, shelf[1], 5, 10_000, out ItemStack bought, out long total, out _));
            Assert.AreEqual(new ItemStack(Stim, 5), bought);
            Assert.AreEqual(4_750, total);
            Assert.AreEqual(3, shelf.Count);
            Assert.AreEqual(7, shelf[1].stack.count);
        }

        [Test]
        public void BuyingTheLastOne_TakesTheListingAway_AndTheItemKeepsItsState()
        {
            Assert.IsTrue(KioskRules.TryBuy(shelf, 2, shelf[2], 1, 5_000_000, out ItemStack bought, out long total, out _));
            Assert.AreEqual(new ItemStack(Pet, 1, 0xBEEF), bought);
            Assert.AreEqual(2_000_000, total);
            Assert.AreEqual(2, shelf.Count);
        }

        [Test]
        public void ABuyerWhoCannotPay_GetsNothing()
        {
            Assert.IsFalse(KioskRules.TryBuy(shelf, 0, shelf[0], 1, 39_999, out ItemStack bought, out _, out string reason));
            Assert.AreEqual(KioskRules.ReasonNotEnoughCreds, reason);
            Assert.IsTrue(bought.IsEmpty);
            Assert.AreEqual(3, shelf.Count);
        }

        [Test]
        public void AListingThatChangedSinceTheBuyerLooked_IsNotSold()
        {
            var seen = shelf[0];
            shelf[0] = new KioskListing(seen.stack, 400_000); // the price went up meanwhile
            Assert.IsFalse(KioskRules.TryBuy(shelf, 0, seen, 1, 1_000_000, out _, out _, out string reason));
            Assert.AreEqual(KioskRules.ReasonChanged, reason);

            var pet = shelf[2];
            shelf[2] = new KioskListing(new ItemStack(Pet, 1, 1), pet.unitPrice); // another pet in its place
            Assert.IsFalse(KioskRules.TryBuy(shelf, 2, pet, 1, 5_000_000, out _, out _, out reason));
            Assert.AreEqual(KioskRules.ReasonChanged, reason);
        }

        [Test]
        public void MoreThanIsLeft_OrAListingThatIsGone_IsRefused()
        {
            Assert.IsFalse(KioskRules.TryBuy(shelf, 1, shelf[1], 13, 1_000_000, out _, out _, out string reason));
            Assert.AreEqual(StorageRules.ReasonNotThatMany, reason);
            Assert.IsFalse(KioskRules.TryBuy(shelf, 7, shelf[1], 1, 1_000_000, out _, out _, out reason));
            Assert.AreEqual(KioskRules.ReasonGone, reason);
        }

        [Test]
        public void ATotal_NeverOverflows()
        {
            Assert.IsTrue(KioskRules.TryTotal(20_000_000, 999, out long total));
            Assert.AreEqual(19_980_000_000, total);
            Assert.IsFalse(KioskRules.TryTotal(long.MaxValue / 2, 3, out _));
        }

        [Test]
        public void ShortPrices_FitOnACell()
        {
            Assert.AreEqual("950", KioskRules.ShortPrice(950));
            Assert.AreEqual("12.5k", KioskRules.ShortPrice(12_500));
            Assert.AreEqual("1k", KioskRules.ShortPrice(1_000));
            Assert.AreEqual("1.5k", KioskRules.ShortPrice(1_500));
            Assert.AreEqual("999k", KioskRules.ShortPrice(999_999));
            Assert.AreEqual("3.2M", KioskRules.ShortPrice(3_200_000));
            Assert.AreEqual("20M", KioskRules.ShortPrice(20_000_000));
        }
    }
}
