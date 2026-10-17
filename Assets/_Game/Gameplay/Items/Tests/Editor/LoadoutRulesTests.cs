using System.Collections.Generic;
using KryticalNova.Items;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Tests
{
    /// <summary>
    /// Equipment loadouts: finding a saved piece in the Pack, wearing a set in one go (what was worn goes into the
    /// freed cells, so a full Pack loses nothing), and naming what could not go on.
    /// </summary>
    public sealed class LoadoutRulesTests
    {
        private sealed class Lookup : IItemLookup
        {
            public readonly Dictionary<ushort, ItemDefinition> Items = new Dictionary<ushort, ItemDefinition>();
            public ItemDefinition Get(ushort id) => Items.TryGetValue(id, out var d) ? d : null;
        }

        private const ushort SwordA = 1001, SwordB = 1002, ArmorA = 2001, ArmorB = 2002, BootsA = 3001, Potion = 9001;
        private Lookup lookup;
        private ItemStack[] bag;
        private EquippedItems worn;
        private WearSetResult result;

        [SetUp]
        public void SetUp()
        {
            lookup = new Lookup();
            Add(SwordA, "Sword A", EquipSlot.Weapon);
            Add(SwordB, "Sword B", EquipSlot.Weapon);
            Add(ArmorA, "Armor A", EquipSlot.Armor);
            Add(ArmorB, "Armor B", EquipSlot.Armor);
            Add(BootsA, "Boots A", EquipSlot.Boots);
            Add(Potion, "Potion", EquipSlot.None, 20);
            bag = new ItemStack[4];
            worn = default;
            result = new WearSetResult();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var d in lookup.Items.Values) Object.DestroyImmediate(d);
        }

        private void Add(ushort id, string name, EquipSlot slot, ushort maxStack = 1)
        {
            var d = ScriptableObject.CreateInstance<ItemDefinition>();
            d.itemId = id; d.displayName = name; d.equipSlot = slot; d.maxStack = maxStack;
            lookup.Items[id] = d;
        }

        private void Wear(InventoryRules.EquipGate gate, params ItemStack[] pieces) =>
            LoadoutRules.WearSet(bag, ref worn, lookup, pieces, gate, result);

        [Test]
        public void TheVeryPieceIsFoundBeforeAnotherOfTheSameItem()
        {
            bag[0] = new ItemStack(SwordA, 1, 5);
            bag[2] = new ItemStack(SwordA, 1, 9);
            Assert.AreEqual(2, LoadoutRules.FindInPack(bag, new ItemStack(SwordA, 1, 9)));
            Assert.AreEqual(0, LoadoutRules.FindInPack(bag, new ItemStack(SwordA, 1, 7)), "worked on since: the same item will do");
            Assert.AreEqual(-1, LoadoutRules.FindInPack(bag, new ItemStack(SwordB, 1)));
        }

        [Test]
        public void WearingSwapsWhatWasWornIntoTheFreedCells()
        {
            worn[EquipSlot.Weapon] = new ItemStack(SwordA, 1);
            worn[EquipSlot.Armor] = new ItemStack(ArmorA, 1);
            bag[1] = new ItemStack(SwordB, 1, 3);
            bag[3] = new ItemStack(ArmorB, 1);
            Wear(null, new ItemStack(SwordB, 1, 3), new ItemStack(ArmorB, 1));
            Assert.AreEqual(2, result.Worn);
            Assert.AreEqual(SwordB, worn[EquipSlot.Weapon].itemId);
            Assert.AreEqual(3UL, worn[EquipSlot.Weapon].data, "the piece keeps its state");
            Assert.AreEqual(ArmorB, worn[EquipSlot.Armor].itemId);
            Assert.AreEqual(SwordA, bag[1].itemId);
            Assert.AreEqual(ArmorA, bag[3].itemId);
        }

        [Test]
        public void AFullPackLosesNothing()
        {
            worn[EquipSlot.Weapon] = new ItemStack(SwordA, 1);
            bag[0] = new ItemStack(Potion, 20);
            bag[1] = new ItemStack(SwordB, 1);
            bag[2] = new ItemStack(Potion, 20);
            bag[3] = new ItemStack(BootsA, 1);
            Wear(null, new ItemStack(SwordB, 1), new ItemStack(BootsA, 1));
            Assert.AreEqual(2, result.Worn);
            Assert.AreEqual(SwordA, bag[1].itemId, "the old weapon took the new one's cell");
            Assert.IsTrue(bag[3].IsEmpty, "the boots came off an empty slot");
            Assert.AreEqual(BootsA, worn[EquipSlot.Boots].itemId);
        }

        [Test]
        public void MissingPiecesAreSkippedAndNamed()
        {
            bag[0] = new ItemStack(ArmorB, 1);
            Wear(null, new ItemStack(SwordB, 1), new ItemStack(ArmorB, 1), new ItemStack(BootsA, 1));
            Assert.AreEqual(3, result.Wanted);
            Assert.AreEqual(1, result.Worn);
            CollectionAssert.AreEqual(new[] { SwordB, BootsA }, result.Missing);
            string line = LoadoutRules.Describe("Hunting", result, id => lookup.Get(id).displayName);
            Assert.AreEqual("Wore 1 of 3 of Hunting. Not in your Pack: Sword B, Boots A.", line);
        }

        [Test]
        public void WhatIsOnAlreadyStaysAndCounts()
        {
            worn[EquipSlot.Weapon] = new ItemStack(SwordA, 1, 4);
            Wear(null, new ItemStack(SwordA, 1, 1));
            Assert.AreEqual(1, result.AlreadyOn);
            Assert.AreEqual(4UL, worn[EquipSlot.Weapon].data, "not swapped for nothing");
            Assert.AreEqual("Wore Hunting.", LoadoutRules.Describe("Hunting", result, null));
        }

        [Test]
        public void APieceTheCharacterMayNotWearStaysInThePack()
        {
            bag[0] = new ItemStack(SwordB, 1);
            bag[1] = new ItemStack(ArmorB, 1);
            Wear(item => item.itemId == SwordB ? "Requires level 30." : null, new ItemStack(SwordB, 1), new ItemStack(ArmorB, 1));
            Assert.AreEqual(1, result.Worn);
            Assert.AreEqual(SwordB, bag[0].itemId);
            Assert.AreEqual(1, result.Blocked.Count);
            StringAssert.Contains("Sword B: Requires level 30.", LoadoutRules.Describe("Hunting", result, id => lookup.Get(id).displayName));
        }

        [Test]
        public void NamesAreCleanedAndShort()
        {
            Assert.AreEqual("Loadout 3", LoadoutRules.CleanName("   ", 2));
            Assert.AreEqual("Boss run", LoadoutRules.CleanName("  Boss \n run ", 0));
            Assert.AreEqual(LoadoutRules.MaxNameLength, LoadoutRules.CleanName(new string('x', 40), 0).Length);
            Assert.LessOrEqual(System.Text.Encoding.UTF8.GetByteCount(LoadoutRules.CleanName(new string('é', 20), 0)), 29);
            Assert.AreEqual("b", LoadoutRules.CleanName("<b>", 0), "no rich text tags");
        }
    }
}
