using System.Collections.Generic;
using KryticalNova.Economy;
using KryticalNova.Hostiles;
using KryticalNova.Items;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Tests
{
    /// <summary>The Outfitter's shelf, which sells one colour a day, and the crafting materials a kill may drop.</summary>
    public sealed class RotatingShelfTests
    {
        private readonly List<Object> made = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in made) Object.DestroyImmediate(o);
            made.Clear();
        }

        private ItemDefinition Item(ushort id)
        {
            var item = ScriptableObject.CreateInstance<ItemDefinition>();
            item.itemId = id;
            item.displayName = "Item " + id;
            made.Add(item);
            return item;
        }

        private VendorStock Shelf(params ItemDefinition[] colours)
        {
            var stock = ScriptableObject.CreateInstance<VendorStock>();
            foreach (ItemDefinition colour in colours) stock.rotation.Add(new VendorListing { item = colour, price = 1_500 });
            made.Add(stock);
            return stock;
        }

        [Test]
        public void OneColourIsSoldEachDay_InTurn()
        {
            ItemDefinition cyan = Item(9310), magenta = Item(9311), yellow = Item(9312), black = Item(9313);
            VendorStock stock = Shelf(cyan, magenta, yellow, black);

            Assert.IsTrue(VendorRules.TryPrice(stock, null, cyan, out long price, day: 0));
            Assert.AreEqual(1_500, price);
            Assert.IsFalse(VendorRules.TryPrice(stock, null, magenta, out _, day: 0), "tomorrow's colour is not on the shelf today");
            Assert.IsTrue(VendorRules.TryPrice(stock, null, magenta, out _, day: 1));
            Assert.IsTrue(VendorRules.TryPrice(stock, null, black, out _, day: 3));
            Assert.IsTrue(VendorRules.TryPrice(stock, null, cyan, out _, day: 4), "after the fourth it starts again");
            Assert.IsTrue(VendorRules.TryPrice(stock, null, black, out _, day: -1), "a clock that runs backwards still names one colour");
        }

        [Test]
        public void TheShelfShowsTodaysColourOnly()
        {
            ItemDefinition cyan = Item(9310), magenta = Item(9311);
            VendorStock stock = Shelf(cyan, magenta);
            var offers = new List<VendorOffer>();
            VendorRules.List(stock, null, null, null, offers, day: 1);
            Assert.AreEqual(1, offers.Count);
            Assert.AreSame(magenta, offers[0].Item);
        }

        [Test]
        public void AShelfThatDoesNotRotate_SellsNothingOfTheDay()
        {
            VendorStock stock = Shelf();
            Assert.IsFalse(stock.TryGetOfTheDay(5, out _));
            Assert.IsFalse(VendorRules.TryPrice(stock, null, Item(9310), out _, day: 5));
        }

        [Test]
        public void AKillDropsEachMaterialOnItsOwnRoll_ByLevelAndRate()
        {
            ItemDefinition cell = Item(9310), page = Item(9401);
            var settings = ScriptableObject.CreateInstance<HostileSettings>();
            made.Add(settings);
            settings.materialDrops = new[]
            {
                new MaterialDrop { item = cell, chancePercent = 50f },
                new MaterialDrop { item = page, chancePercent = 50f, minLevel = 70 },
            };
            var dropped = new List<ItemDefinition>();

            HostileLoot.RollMaterials(10, settings, 100f, 1f, () => 0.4f, dropped);
            CollectionAssert.AreEqual(new[] { cell }, dropped, "a level 10 Hostile drops no Page");

            HostileLoot.RollMaterials(80, settings, 100f, 1f, () => 0.4f, dropped);
            CollectionAssert.AreEqual(new[] { cell, page }, dropped);

            HostileLoot.RollMaterials(80, settings, 100f, 1f, () => 0.6f, dropped);
            Assert.AreEqual(0, dropped.Count, "the roll was over the chance");

            HostileLoot.RollMaterials(80, settings, 50f, 1f, () => 0.4f, dropped);
            Assert.AreEqual(0, dropped.Count, "a Hostile far below the player drops half as often");

            HostileLoot.RollMaterials(80, settings, 100f, 0f, () => 0f, dropped);
            Assert.AreEqual(0, dropped.Count, "the server drop rate is off");
        }
    }
}
