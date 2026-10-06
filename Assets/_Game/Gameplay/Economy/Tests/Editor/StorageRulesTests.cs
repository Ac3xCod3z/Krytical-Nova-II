using System.Collections.Generic;
using KryticalNova.Economy;
using KryticalNova.Items;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Tests
{
    public sealed class StorageRulesTests
    {
        private sealed class Lookup : IItemLookup
        {
            public readonly Dictionary<ushort, ItemDefinition> Items = new Dictionary<ushort, ItemDefinition>();
            public ItemDefinition Get(ushort id) => Items.TryGetValue(id, out var d) ? d : null;
        }

        private const ushort Sword = 1001, Armor = 2001, Stim = 9001, Pet = 8001;
        private const string Full = "full";
        private Lookup lookup;
        private ItemStack[] pack;
        private ItemStack[] vault;

        [SetUp]
        public void SetUp()
        {
            lookup = new Lookup();
            Add(Sword, 1);
            Add(Armor, 1);
            Add(Pet, 1);
            Add(Stim, 20);
            pack = new ItemStack[4];
            vault = new ItemStack[3];
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

        private bool Move(ItemStack[] from, int fromCell, ItemStack[] to, int toCell, int count, out string reason) =>
            StorageRules.TryTransfer(from, fromCell, to, toCell, count, lookup, Full, out reason);

        [Test]
        public void Deposit_WithNoCellChosen_GoesToTheFirstFreeCell()
        {
            pack[2] = new ItemStack(Sword, 1);
            Assert.IsTrue(Move(pack, 2, vault, -1, 0, out _));
            Assert.IsTrue(pack[2].IsEmpty);
            Assert.AreEqual(new ItemStack(Sword, 1), vault[0]);
        }

        [Test]
        public void AnItemKeepsItsOwnState_OnTheWayInAndOut()
        {
            pack[0] = new ItemStack(Pet, 1, 0xABCDEF);
            Assert.IsTrue(Move(pack, 0, vault, 1, 0, out _));
            Assert.AreEqual(0xABCDEFul, vault[1].data);
            Assert.IsTrue(Move(vault, 1, pack, 3, 0, out _));
            Assert.AreEqual(new ItemStack(Pet, 1, 0xABCDEF), pack[3]);
        }

        [Test]
        public void PartOfAStack_CanBeMoved_TheRestStays()
        {
            pack[0] = new ItemStack(Stim, 12);
            Assert.IsTrue(Move(pack, 0, vault, -1, 5, out _));
            Assert.AreEqual(7, pack[0].count);
            Assert.AreEqual(5, vault[0].count);
        }

        [Test]
        public void MoreThanTheStackHolds_IsRefused()
        {
            pack[0] = new ItemStack(Stim, 3);
            Assert.IsFalse(Move(pack, 0, vault, -1, 4, out string reason));
            Assert.AreEqual(StorageRules.ReasonNotThatMany, reason);
            Assert.AreEqual(3, pack[0].count);
        }

        [Test]
        public void ItTopsUpAMatchingStack_BeforeTakingANewCell()
        {
            vault[1] = new ItemStack(Stim, 15);
            pack[0] = new ItemStack(Stim, 10);
            Assert.IsTrue(Move(pack, 0, vault, -1, 0, out _));
            Assert.AreEqual(20, vault[1].count);
            Assert.AreEqual(5, vault[0].count);
            Assert.IsTrue(pack[0].IsEmpty);
        }

        [Test]
        public void OntoAMatchingStack_WhatDoesNotFitStaysBehind()
        {
            vault[0] = new ItemStack(Stim, 18);
            pack[0] = new ItemStack(Stim, 10);
            Assert.IsTrue(Move(pack, 0, vault, 0, 0, out _));
            Assert.AreEqual(20, vault[0].count);
            Assert.AreEqual(8, pack[0].count);
        }

        [Test]
        public void ADifferentState_DoesNotStack()
        {
            vault[0] = new ItemStack(Stim, 5, 1);
            pack[0] = new ItemStack(Stim, 5);
            Assert.IsTrue(Move(pack, 0, vault, -1, 0, out _));
            Assert.AreEqual(5, vault[0].count);
            Assert.AreEqual(new ItemStack(Stim, 5), vault[1]);
        }

        [Test]
        public void AFullStorage_RefusesAndChangesNothing()
        {
            vault[0] = new ItemStack(Sword, 1);
            vault[1] = new ItemStack(Armor, 1);
            vault[2] = new ItemStack(Stim, 19);
            pack[0] = new ItemStack(Stim, 2);
            Assert.IsFalse(Move(pack, 0, vault, -1, 0, out string reason));
            Assert.AreEqual(Full, reason);
            Assert.AreEqual(2, pack[0].count);
            Assert.AreEqual(19, vault[2].count);
        }

        [Test]
        public void AWholeStackOntoADifferentItem_SwapsThem()
        {
            pack[1] = new ItemStack(Sword, 1);
            vault[2] = new ItemStack(Armor, 1);
            Assert.IsTrue(Move(pack, 1, vault, 2, 0, out _));
            Assert.AreEqual(new ItemStack(Sword, 1), vault[2]);
            Assert.AreEqual(new ItemStack(Armor, 1), pack[1]);
        }

        [Test]
        public void PartOfAStackOntoADifferentItem_IsRefused()
        {
            pack[0] = new ItemStack(Stim, 10);
            vault[0] = new ItemStack(Sword, 1);
            Assert.IsFalse(Move(pack, 0, vault, 0, 4, out string reason));
            Assert.AreEqual(StorageRules.ReasonOccupied, reason);
            Assert.AreEqual(10, pack[0].count);
        }

        [Test]
        public void InsideOneStorage_ItemsMoveAndSwap()
        {
            vault[0] = new ItemStack(Sword, 1);
            vault[2] = new ItemStack(Armor, 1);
            Assert.IsTrue(Move(vault, 0, vault, 1, 0, out _));
            Assert.AreEqual(new ItemStack(Sword, 1), vault[1]);
            Assert.IsTrue(Move(vault, 1, vault, 2, 0, out _));
            Assert.AreEqual(new ItemStack(Armor, 1), vault[1]);
            Assert.AreEqual(new ItemStack(Sword, 1), vault[2]);
            Assert.IsTrue(Move(vault, 2, vault, 2, 0, out _), "onto itself is no change");
        }

        [Test]
        public void AnEmptyCell_OrOneOutOfRange_IsRefused()
        {
            Assert.IsFalse(Move(pack, 0, vault, -1, 0, out string empty));
            Assert.AreEqual(InventoryRules.ReasonEmpty, empty);
            pack[0] = new ItemStack(Sword, 1);
            Assert.IsFalse(Move(pack, 9, vault, -1, 0, out _));
            Assert.IsFalse(Move(pack, 0, vault, 7, 0, out _));
            Assert.AreEqual(new ItemStack(Sword, 1), pack[0]);
        }

        [Test]
        public void TheVaultHoldsTwoPagesOfFortyEight()
        {
            Assert.AreEqual(96, StorageRules.Capacity);
        }
    }
}
