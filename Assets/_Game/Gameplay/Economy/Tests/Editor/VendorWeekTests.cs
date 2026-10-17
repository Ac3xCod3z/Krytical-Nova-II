using KryticalNova.Economy;
using KryticalNova.Items;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.Tests
{
    /// <summary>
    /// A vendor's weekly limit (owner, 17 Oct 2026): which week an in-game day is in, how many are left, that a new
    /// week starts again, what the refusal says, and that the Armorer sells one of each forge material a week.
    /// </summary>
    public sealed class VendorWeekTests
    {
        private const string ArmorerPath = "Assets/_Game/Gameplay/Economy/Data/Assets/Vendors/VS_Armorer.asset";

        [Test]
        public void DaysFallInWeeksOfSeven()
        {
            Assert.AreEqual(0, VendorRules.WeekOf(0, 7));
            Assert.AreEqual(0, VendorRules.WeekOf(6, 7));
            Assert.AreEqual(1, VendorRules.WeekOf(7, 7));
            Assert.AreEqual(41, VendorRules.WeekOf(290, 7));
            Assert.AreEqual(-1, VendorRules.WeekOf(-1, 7), "a day before 0 is in a week before 0");
            Assert.AreEqual(-1, VendorRules.WeekOf(-7, 7));
            Assert.AreEqual(-2, VendorRules.WeekOf(-8, 7));
            Assert.AreEqual(3, VendorRules.WeekOf(3, 0), "a week of 0 days counts as one day");
        }

        [Test]
        public void TheNextWeekStartsOnItsFirstDay()
        {
            Assert.AreEqual(7, VendorRules.NextWeekStarts(0, 7));
            Assert.AreEqual(7, VendorRules.NextWeekStarts(6, 7));
            Assert.AreEqual(14, VendorRules.NextWeekStarts(7, 7));
        }

        [Test]
        public void WhatIsLeftNeverGoesBelowNothing()
        {
            Assert.AreEqual(1, VendorRules.RemainingThisWeek(1, 0));
            Assert.AreEqual(0, VendorRules.RemainingThisWeek(1, 1));
            Assert.AreEqual(0, VendorRules.RemainingThisWeek(1, 5));
            Assert.AreEqual(3, VendorRules.RemainingThisWeek(5, 2));
            Assert.AreEqual(int.MaxValue, VendorRules.RemainingThisWeek(0, 40), "0 is no limit");
        }

        [Test]
        public void ANewWeekForgetsTheLastOnesPurchases()
        {
            Assert.AreEqual(1, VendorRules.BoughtIn(4, 4, 1));
            Assert.AreEqual(0, VendorRules.BoughtIn(5, 4, 1));
            Assert.AreEqual(0, VendorRules.BoughtIn(4, 4, -3));
        }

        [Test]
        public void TheRefusalSaysWhenMoreComes()
        {
            string line = VendorRules.SoldOutThisWeek("Iridion", 1, 3 * 3600 + 20 * 60);
            StringAssert.Contains("this week's 1 Iridion", line);
            StringAssert.Contains("3 h 20 min", line);
            StringAssert.EndsWith("next week.", VendorRules.SoldOutThisWeek("Iridion", 1, -1), "no clock: no time named");
            Assert.AreEqual("under a minute", VendorRules.Wait(20));
            Assert.AreEqual("45 min", VendorRules.Wait(45 * 60));
        }

        [Test]
        public void TheListShowsALimit()
        {
            var item = ScriptableObject.CreateInstance<ItemDefinition>();
            var stock = ScriptableObject.CreateInstance<VendorStock>();
            try
            {
                item.itemId = 9200;
                stock.listings.Add(new VendorListing { item = item, price = 100, weeklyLimit = 2 });
                var offers = new System.Collections.Generic.List<VendorOffer>();
                VendorRules.List(stock, null, null, null, offers);
                Assert.AreEqual(2, offers[0].WeeklyLimit);
                Assert.AreEqual(2, stock.WeeklyLimitOf(item));
            }
            finally
            {
                Object.DestroyImmediate(stock);
                Object.DestroyImmediate(item);
            }
        }

        [Test]
        public void TheArmorerSellsOneOfEachForgeMaterialAWeek()
        {
            var armorer = AssetDatabase.LoadAssetAtPath<VendorStock>(ArmorerPath);
            Assert.IsNotNull(armorer, "run Krytical Nova > Items > 10. Build Vendor Stock");
            foreach (string name in new[] { "Alloy_Alumite", "Alloy_Iridion", "Fusion_Catalyst", "Tuning_Chip" })
            {
                VendorListing listing = armorer.listings.Find(l => l.item != null && l.item.name == name);
                Assert.IsNotNull(listing.item, $"{name} is on the Armorer's shelf");
                Assert.AreEqual(1, listing.weeklyLimit, $"{name}: one a week");
            }
        }
    }
}
