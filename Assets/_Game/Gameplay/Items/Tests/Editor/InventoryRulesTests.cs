using System.Collections.Generic;
using KryticalNova.Items;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Tests
{
    public sealed class InventoryRulesTests
    {
        private sealed class Lookup : IItemLookup
        {
            public readonly Dictionary<ushort, ItemDefinition> Items = new Dictionary<ushort, ItemDefinition>();
            public ItemDefinition Get(ushort id) => Items.TryGetValue(id, out var d) ? d : null;
        }

        private const ushort SwordA = 1001, SwordB = 1002, ArmorA = 2001, ArmorB = 2002, Potion = 9001;
        private Lookup lookup;
        private ItemStack[] bag;
        private EquippedItems loadout;

        [SetUp]
        public void SetUp()
        {
            lookup = new Lookup();
            Add(SwordA, "Sword A", EquipSlot.Weapon, ItemRarity.Common);
            Add(SwordB, "Sword B", EquipSlot.Weapon, ItemRarity.Epic);
            Add(ArmorA, "Armor A", EquipSlot.Armor, ItemRarity.Rare);
            Add(ArmorB, "Armor B", EquipSlot.Armor, ItemRarity.Common);
            Add(Potion, "Potion", EquipSlot.None, ItemRarity.Common, 20);
            bag = new ItemStack[6];
            loadout = default;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var d in lookup.Items.Values) Object.DestroyImmediate(d);
        }

        private void Add(ushort id, string name, EquipSlot slot, ItemRarity rarity, ushort maxStack = 1)
        {
            var d = ScriptableObject.CreateInstance<ItemDefinition>();
            d.itemId = id; d.displayName = name; d.equipSlot = slot; d.rarity = rarity; d.maxStack = maxStack;
            lookup.Items[id] = d;
        }

        private bool Move(ItemLocation from, ItemLocation to) =>
            InventoryRules.TryMove(bag, ref loadout, lookup, from, to, out _);

        [Test]
        public void BagToBag_SwapsCells()
        {
            bag[0] = new ItemStack(SwordA, 1);
            bag[1] = new ItemStack(ArmorA, 1);
            Assert.IsTrue(Move(ItemLocation.Bag(0), ItemLocation.Bag(1)));
            Assert.AreEqual(ArmorA, bag[0].itemId);
            Assert.AreEqual(SwordA, bag[1].itemId);
        }

        [Test]
        public void BagToBag_MergesStacksUpToMax()
        {
            bag[0] = new ItemStack(Potion, 15);
            bag[1] = new ItemStack(Potion, 10);
            Assert.IsTrue(Move(ItemLocation.Bag(0), ItemLocation.Bag(1)));
            Assert.AreEqual(20, bag[1].count);
            Assert.AreEqual(5, bag[0].count);
        }

        [Test]
        public void Equip_WrongSlot_IsRejectedAndStateUnchanged()
        {
            bag[0] = new ItemStack(SwordA, 1);
            Assert.IsFalse(InventoryRules.TryMove(bag, ref loadout, lookup, ItemLocation.Bag(0),
                ItemLocation.Equip(EquipSlot.Armor), out string reason));
            Assert.AreEqual(InventoryRules.ReasonWrongSlot, reason);
            Assert.AreEqual(SwordA, bag[0].itemId);
            Assert.AreEqual(0, loadout.ids.armor);
        }

        [Test]
        public void Equip_NonEquippable_IsRejected()
        {
            bag[0] = new ItemStack(Potion, 1);
            Assert.IsFalse(InventoryRules.TryQuickEquip(bag, ref loadout, lookup, 0, out string reason));
            Assert.AreEqual(InventoryRules.ReasonNotEquippable, reason);
        }

        [Test]
        public void QuickEquip_SwapsPreviousBackIntoSourceCell()
        {
            loadout.ids.weapon = SwordA;
            bag[3] = new ItemStack(SwordB, 1);
            Assert.IsTrue(InventoryRules.TryQuickEquip(bag, ref loadout, lookup, 3, out _));
            Assert.AreEqual(SwordB, loadout.ids.weapon);
            Assert.AreEqual(SwordA, bag[3].itemId);
        }

        [Test]
        public void EquipToBag_OntoCompatibleItem_Swaps()
        {
            loadout.ids.armor = ArmorA;
            bag[2] = new ItemStack(ArmorB, 1);
            Assert.IsTrue(Move(ItemLocation.Equip(EquipSlot.Armor), ItemLocation.Bag(2)));
            Assert.AreEqual(ArmorB, loadout.ids.armor);
            Assert.AreEqual(ArmorA, bag[2].itemId);
        }

        [Test]
        public void EquipToBag_OntoIncompatibleItem_IsRejected()
        {
            loadout.ids.armor = ArmorA;
            bag[2] = new ItemStack(SwordA, 1);
            Assert.IsFalse(Move(ItemLocation.Equip(EquipSlot.Armor), ItemLocation.Bag(2)));
            Assert.AreEqual(ArmorA, loadout.ids.armor);
        }

        [Test]
        public void QuickUnequip_GoesToFirstEmptyCell()
        {
            loadout.ids.weapon = SwordA;
            bag[0] = new ItemStack(Potion, 1);
            Assert.IsTrue(InventoryRules.TryQuickUnequip(bag, ref loadout, lookup, EquipSlot.Weapon, out _));
            Assert.AreEqual(0, loadout.ids.weapon);
            Assert.AreEqual(SwordA, bag[1].itemId);
        }

        [Test]
        public void QuickUnequip_FullBag_Fails()
        {
            for (int i = 0; i < bag.Length; i++) bag[i] = new ItemStack(Potion, 1);
            loadout.ids.weapon = SwordA;
            Assert.IsFalse(InventoryRules.TryQuickUnequip(bag, ref loadout, lookup, EquipSlot.Weapon, out string reason));
            Assert.AreEqual(InventoryRules.ReasonBagFull, reason);
            Assert.AreEqual(SwordA, loadout.ids.weapon);
        }

        [Test]
        public void Destroy_ClearsBagAndEquipment()
        {
            bag[0] = new ItemStack(SwordA, 1);
            loadout.ids.armor = ArmorA;
            Assert.IsTrue(InventoryRules.TryDestroy(bag, ref loadout, ItemLocation.Bag(0), out _));
            Assert.IsTrue(InventoryRules.TryDestroy(bag, ref loadout, ItemLocation.Equip(EquipSlot.Armor), out _));
            Assert.IsTrue(bag[0].IsEmpty);
            Assert.AreEqual(0, loadout.ids.armor);
        }

        [Test]
        public void Sort_PutsHigherTiersOfTheSameRarityFirst()
        {
            const ushort Low = 10000, High = 10003;
            Add(Low, "Tier Armor", EquipSlot.Armor, ItemRarity.Rare);
            Add(High, "Tier Armor", EquipSlot.Armor, ItemRarity.Rare);
            lookup.Items[Low].itemLevel = 45;
            lookup.Items[High].itemLevel = 75;
            bag[0] = new ItemStack(Low, 1);
            bag[1] = new ItemStack(High, 1);
            InventoryRules.Sort(bag, lookup);
            Assert.AreEqual(High, bag[0].itemId);
            Assert.AreEqual(Low, bag[1].itemId);
        }

        [Test]
        public void Sort_OrdersBySlotThenRarityAndMergesStacks()
        {
            bag[0] = new ItemStack(Potion, 3);
            bag[1] = new ItemStack(ArmorB, 1);
            bag[3] = new ItemStack(SwordA, 1);
            bag[4] = new ItemStack(Potion, 4);
            bag[5] = new ItemStack(SwordB, 1);
            InventoryRules.Sort(bag, lookup);
            Assert.AreEqual(SwordB, bag[0].itemId); // weapon, epic
            Assert.AreEqual(SwordA, bag[1].itemId); // weapon, common
            Assert.AreEqual(ArmorB, bag[2].itemId);
            Assert.AreEqual(Potion, bag[3].itemId);
            Assert.AreEqual(7, bag[3].count);
            Assert.IsTrue(bag[4].IsEmpty && bag[5].IsEmpty);
        }

        [Test]
        public void TryAdd_TopsUpStacksThenFillsEmpties()
        {
            bag[2] = new ItemStack(Potion, 18);
            int added = InventoryRules.TryAdd(bag, lookup, Potion, 5);
            Assert.AreEqual(5, added);
            Assert.AreEqual(20, bag[2].count);
            Assert.AreEqual(3, bag[0].count);
        }

        [Test]
        public void Consume_RemovesOneFromStack()
        {
            lookup.Items[Potion].restoreHealth = 300f;
            bag[2] = new ItemStack(Potion, 5);
            Assert.IsTrue(InventoryRules.TryConsume(bag, lookup, 2, out var used, out _));
            Assert.AreSame(lookup.Items[Potion], used);
            Assert.AreEqual(4, bag[2].count);
        }

        [Test]
        public void Consume_LastOne_EmptiesCell()
        {
            lookup.Items[Potion].restoreChi = 100f;
            bag[0] = new ItemStack(Potion, 1);
            Assert.IsTrue(InventoryRules.TryConsume(bag, lookup, 0, out _, out _));
            Assert.IsTrue(bag[0].IsEmpty);
        }

        [Test]
        public void Consume_NonConsumable_IsRejectedAndStateUnchanged()
        {
            bag[0] = new ItemStack(SwordA, 1);
            Assert.IsFalse(InventoryRules.TryConsume(bag, lookup, 0, out var used, out string reason));
            Assert.AreEqual(InventoryRules.ReasonNotUsable, reason);
            Assert.IsNull(used);
            Assert.AreEqual(SwordA, bag[0].itemId);
        }

        [Test]
        public void Consume_EmptyCell_IsRejected()
        {
            Assert.IsFalse(InventoryRules.TryConsume(bag, lookup, 1, out _, out string reason));
            Assert.AreEqual(InventoryRules.ReasonEmpty, reason);
        }

        // ---------------------------------------------------------------- an item's own data travels with it

        private const ulong Grown = 0xABCDEF, Other = 0x1234;

        [Test]
        public void EquipAndUnequip_KeepTheItemsOwnData()
        {
            bag[0] = new ItemStack(SwordA, 1, Grown);
            Assert.IsTrue(InventoryRules.TryQuickEquip(bag, ref loadout, lookup, 0, out _));
            Assert.AreEqual(Grown, loadout[EquipSlot.Weapon].data);
            Assert.IsTrue(bag[0].IsEmpty);

            Assert.IsTrue(InventoryRules.TryQuickUnequip(bag, ref loadout, lookup, EquipSlot.Weapon, out _));
            Assert.AreEqual(new ItemStack(SwordA, 1, Grown), bag[0]);
            Assert.AreEqual(0UL, loadout.data.weapon, "an emptied slot keeps nothing of the item that left");
        }

        [Test]
        public void SwappingWornItems_SwapsTheirData()
        {
            loadout[EquipSlot.Weapon] = new ItemStack(SwordA, 1, Grown);
            bag[3] = new ItemStack(SwordB, 1, Other);
            Assert.IsTrue(InventoryRules.TryQuickEquip(bag, ref loadout, lookup, 3, out _));
            Assert.AreEqual(new ItemStack(SwordB, 1, Other), loadout[EquipSlot.Weapon]);
            Assert.AreEqual(new ItemStack(SwordA, 1, Grown), bag[3]);
        }

        [Test]
        public void StacksWithDifferentData_NeverMerge()
        {
            bag[0] = new ItemStack(Potion, 5, Grown);
            bag[1] = new ItemStack(Potion, 5);
            Assert.IsTrue(Move(ItemLocation.Bag(0), ItemLocation.Bag(1)));
            Assert.AreEqual(new ItemStack(Potion, 5), bag[0], "different states swap cells instead of merging");
            Assert.AreEqual(new ItemStack(Potion, 5, Grown), bag[1]);

            Assert.AreEqual(3, InventoryRules.TryAdd(bag, lookup, Potion, 3, Grown));
            Assert.AreEqual(8, bag[1].count);
            Assert.AreEqual(5, bag[0].count);
        }

        [Test]
        public void Sort_KeepsEachItemsData()
        {
            bag[0] = new ItemStack(Potion, 2);
            bag[2] = new ItemStack(SwordA, 1, Other);
            bag[3] = new ItemStack(SwordA, 1, Grown);
            bag[5] = new ItemStack(Potion, 2);
            InventoryRules.Sort(bag, lookup);
            Assert.AreEqual(new ItemStack(SwordA, 1, Grown), bag[0]);
            Assert.AreEqual(new ItemStack(SwordA, 1, Other), bag[1]);
            Assert.AreEqual(new ItemStack(Potion, 4), bag[2]);
        }

        [Test]
        public void Take_HandsBackTheWholeStack()
        {
            loadout[EquipSlot.Armor] = new ItemStack(ArmorA, 1, Grown);
            Assert.IsTrue(InventoryRules.TryTake(bag, ref loadout, ItemLocation.Equip(EquipSlot.Armor), out ItemStack taken, out _));
            Assert.AreEqual(new ItemStack(ArmorA, 1, Grown), taken);
            Assert.IsTrue(loadout.IsEmpty(EquipSlot.Armor));
        }

        [Test]
        public void Fits_IsAllOrNothingAndLeavesTheBagAlone()
        {
            for (int i = 0; i < bag.Length - 1; i++) bag[i] = new ItemStack(SwordA, 1);
            var scratch = new ItemStack[bag.Length];
            Assert.IsTrue(InventoryRules.Fits(bag, scratch, lookup, new[] { new ItemStack(SwordB, 1, Grown) }));
            Assert.IsFalse(InventoryRules.Fits(bag, scratch, lookup, new[] { new ItemStack(SwordB, 1), new ItemStack(ArmorA, 1) }));
            Assert.IsTrue(bag[bag.Length - 1].IsEmpty);
        }
    }
}
