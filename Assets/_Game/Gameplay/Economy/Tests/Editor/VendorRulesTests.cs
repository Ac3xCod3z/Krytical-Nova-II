using System.Collections.Generic;
using KryticalNova.Economy;
using KryticalNova.Items;
using KryticalNova.NPC;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.Tests
{
    /// <summary>
    /// The rules of NPC shops: what a vendor asks, what a vendor pays, which sales must be confirmed, and that the
    /// shelves built for the game are whole. The first part works on items made here; the last reads the assets.
    /// </summary>
    public sealed class VendorRulesTests
    {
        private const string CatalogPath = "Assets/_Game/Gameplay/Economy/Data/Assets/Vendors/VendorCatalog.asset";

        private readonly List<Object> made = new List<Object>();
        private EconomySettings settings;
        private VendorStock stock;
        private VendorCatalog catalog;
        private ItemDefinition stim, blade, rareBlade, robe, pet, serum, creds;

        [SetUp]
        public void SetUp()
        {
            settings = Make<EconomySettings>();
            settings.vendorGearCredsPerLevel = 100;
            settings.vendorGearRarityMultiplier = new[] { 1f, 4f, 20f, 100f, 100f };
            settings.vendorFallbackValueByRarity = new long[] { 20, 100, 2_000, 20_000, 20_000 };
            settings.vendorSellBackPercent = 20f;
            settings.vendorConfirmFromRarity = ItemRarity.Rare;

            stim = Item(9001, ItemRarity.Common, EquipSlot.None, 0, stack: 99);
            blade = Item(1001, ItemRarity.Common, EquipSlot.Weapon, 10, line: 7);
            rareBlade = Item(1002, ItemRarity.Rare, EquipSlot.Weapon, 30, line: 7);
            robe = Item(1003, ItemRarity.Common, EquipSlot.Armor, 4, line: 9);
            pet = Item(8001, ItemRarity.Common, EquipSlot.Pet, 0);
            serum = Item(9100, ItemRarity.Uncommon, EquipSlot.None, 0, stack: 99);
            creds = Item(LootRules.CredsItemId, ItemRarity.Common, EquipSlot.None, 0, stack: 999);

            stock = Make<VendorStock>();
            stock.listings.Add(new VendorListing { item = stim, price = 30 });
            stock.sellsLadderGear = true;
            stock.gearRarity = ItemRarity.Common;
            stock.gearMinLevel = 1;
            stock.gearMaxLevel = 28;
            catalog = Make<VendorCatalog>();
            catalog.stocks.Add(stock);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object each in made) Object.DestroyImmediate(each);
            made.Clear();
        }

        private T Make<T>() where T : ScriptableObject
        {
            T asset = ScriptableObject.CreateInstance<T>();
            made.Add(asset);
            return asset;
        }

        private ItemDefinition Item(ushort id, ItemRarity rarity, EquipSlot slot, int level, int line = 0, int stack = 1)
        {
            ItemDefinition item = Make<ItemDefinition>();
            item.itemId = id;
            item.displayName = "Item " + id;
            item.rarity = rarity;
            item.equipSlot = slot;
            item.itemLevel = (byte)level;
            item.lineId = (ushort)line;
            item.maxStack = (ushort)stack;
            return item;
        }

        // ---------------------------------------------------------------- what a vendor asks

        [Test]
        public void AListedItem_CostsWhatItsListingSays()
        {
            Assert.IsTrue(VendorRules.TryPrice(stock, settings, stim, out long price));
            Assert.AreEqual(30, price);
        }

        [Test]
        public void GearOffTheRule_IsPricedByItsLevelAndRarity()
        {
            Assert.AreEqual(1_000, VendorRules.GearPrice(settings, blade), "level 10 x 100 x Common");
            Assert.AreEqual(60_000, VendorRules.GearPrice(settings, rareBlade), "level 30 x 100 x Rare (20)");
            Assert.IsTrue(VendorRules.TryPrice(stock, settings, blade, out long price));
            Assert.AreEqual(1_000, price);
        }

        [Test]
        public void WhatIsNotOnTheShelf_IsNotSold()
        {
            Assert.IsFalse(VendorRules.TryPrice(stock, settings, rareBlade, out _), "the rule sells Common only");
            Assert.IsFalse(VendorRules.TryPrice(stock, settings, serum, out _), "not listed, not gear");
            Assert.IsFalse(VendorRules.TryPrice(stock, settings, pet, out _));
            Assert.IsFalse(VendorRules.TryPrice(null, settings, stim, out _));
            stock.gearMaxLevel = 9;
            Assert.IsFalse(VendorRules.TryPrice(stock, settings, blade, out _), "level 10 is past the rule's range");
        }

        [Test]
        public void TheShelf_ShowsListedItemsFirst_ThenTheGearTheBuyerCanWear_BySlotAndLevel()
        {
            var all = new List<ItemDefinition> { robe, rareBlade, blade, stim, serum, pet };
            var seen = new List<VendorOffer>();

            VendorRules.List(stock, settings, all, null, seen);
            Assert.AreEqual(3, seen.Count);
            Assert.AreSame(stim, seen[0].Item, "listed items first");
            Assert.AreSame(blade, seen[1].Item, "then gear: the weapon slot before the armor slot");
            Assert.AreSame(robe, seen[2].Item);

            VendorRules.List(stock, settings, all, item => item != blade, seen);
            Assert.AreEqual(2, seen.Count, "a piece the buyer cannot wear is not shown");
            Assert.AreSame(robe, seen[1].Item);
        }

        // ---------------------------------------------------------------- what a vendor pays

        [Test]
        public void AVendorPaysAShareOfWhatTheShopsValueTheItemAt()
        {
            Assert.AreEqual(6, VendorRules.SellValue(catalog, settings, stim), "20 % of its listed 30");
            Assert.AreEqual(200, VendorRules.SellValue(catalog, settings, blade), "20 % of level 10 x 100");
            Assert.AreEqual(12_000, VendorRules.SellValue(catalog, settings, rareBlade), "gear no shelf sells still has its level's worth");
            Assert.AreEqual(20, VendorRules.SellValue(catalog, settings, serum), "20 % of what an Uncommon thing is worth by itself");
        }

        [Test]
        public void NoVendorBuysCreds_OrAnythingWorthNothing()
        {
            Assert.AreEqual(0, VendorRules.SellValue(catalog, settings, creds));
            Assert.AreEqual(0, VendorRules.SellValue(catalog, settings, null));
            settings.vendorSellBackPercent = 0f;
            Assert.AreEqual(0, VendorRules.SellValue(catalog, settings, blade));
        }

        // ---------------------------------------------------------------- what must be confirmed

        [Test]
        public void RareAndBetter_IsNeverSoldWithoutAConfirm()
        {
            Assert.IsTrue(VendorRules.NeedsConfirm(settings, rareBlade, new ItemStack(rareBlade.itemId, 1)));
            rareBlade.rarity = ItemRarity.Epic;
            Assert.IsTrue(VendorRules.NeedsConfirm(settings, rareBlade, new ItemStack(rareBlade.itemId, 1)));
            Assert.IsFalse(VendorRules.NeedsConfirm(settings, blade, new ItemStack(blade.itemId, 1)), "an untouched Common piece sells at once");
            Assert.IsFalse(VendorRules.NeedsConfirm(settings, stim, new ItemStack(stim.itemId, 40)));
        }

        [Test]
        public void APet_AndWorkedGear_AreConfirmedWhateverTheirRarity()
        {
            Assert.IsTrue(VendorRules.NeedsConfirm(settings, pet, new ItemStack(pet.itemId, 1, 0xBEEF)), "a pet's growth goes with it");
            ulong tempered = new GearItemState(24, 0, 0).Pack();
            Assert.IsTrue(VendorRules.NeedsConfirm(settings, blade, new ItemStack(blade.itemId, 1, tempered)), "a Common piece that was Tempered");
        }

        [Test]
        public void TheConfirmThreshold_IsANumberInTheSettings()
        {
            settings.vendorConfirmFromRarity = ItemRarity.Uncommon;
            Assert.IsTrue(VendorRules.NeedsConfirm(settings, serum, new ItemStack(serum.itemId, 3)));
            Assert.IsTrue(VendorRules.NeedsConfirm(null, rareBlade, new ItemStack(rareBlade.itemId, 1)), "with no settings, Rare is the line");
        }

        [Test]
        public void ATotal_IsRefusedRatherThanAllowedToOverflow()
        {
            Assert.IsTrue(VendorRules.TryTotal(30, 99, out long total));
            Assert.AreEqual(2_970, total);
            Assert.IsFalse(VendorRules.TryTotal(long.MaxValue / 2, 3, out _));
            Assert.IsFalse(VendorRules.TryTotal(30, 0, out _));
            Assert.IsFalse(VendorRules.TryTotal(-1, 5, out _));
        }

        // ---------------------------------------------------------------- the shelves the game ships

        [Test]
        public void EveryBuiltVendor_HasAnNpcThatOffersItsShop_AndAShelfWithPrices()
        {
            var built = AssetDatabase.LoadAssetAtPath<VendorCatalog>(CatalogPath);
            Assert.IsNotNull(built, "run Krytical Nova > Items > 10. Build Vendor Stock");
            Assert.IsNotEmpty(built.stocks);
            foreach (VendorStock shelf in built.stocks)
            {
                Assert.IsNotNull(shelf, "an empty row in the catalog");
                Assert.IsNotNull(shelf.vendor, shelf.name + " has no NPC");
                Assert.AreNotEqual(NpcOffer.None, shelf.vendor.offers & NpcOffer.Shop, shelf.vendor.name + " does not offer the Shop service");
                Assert.IsTrue(shelf.listings.Count > 0 || shelf.sellsLadderGear, shelf.name + " sells nothing");
                var seen = new HashSet<ItemDefinition>();
                foreach (VendorListing listing in shelf.listings)
                {
                    Assert.IsNotNull(listing.item, shelf.name + " lists a missing item");
                    Assert.Greater(listing.price, 0, $"{shelf.name}: {listing.item.displayName} has no price");
                    Assert.IsTrue(seen.Add(listing.item), $"{shelf.name}: {listing.item.displayName} is listed twice");
                }
            }
        }
    }
}
