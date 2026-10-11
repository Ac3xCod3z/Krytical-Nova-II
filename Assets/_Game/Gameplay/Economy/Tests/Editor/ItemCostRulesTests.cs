using KryticalNova.Economy;
using KryticalNova.Items;
using NUnit.Framework;

namespace KryticalNova.Tests
{
    public sealed class ItemCostRulesTests
    {
        private const ushort Alloy = 9100, Failsafe = 9110, Sword = 10001;
        private ItemStack[] pack;
        private ItemStack[] vault;
        private ItemStack[] deepVault;

        [SetUp]
        public void SetUp()
        {
            pack = new ItemStack[4];
            vault = new ItemStack[3];
            deepVault = new ItemStack[3];
        }

        private bool Spend(params ItemCost[] costs) => ItemCostRules.TrySpend(new[] { pack, vault, deepVault }, costs);

        [Test]
        public void ThePackIsEmptiedFirst_ThenTheVault_ThenTheDeepVault()
        {
            pack[1] = new ItemStack(Alloy, 2);
            vault[0] = new ItemStack(Alloy, 3);
            deepVault[2] = new ItemStack(Alloy, 5);

            Assert.IsTrue(Spend(new ItemCost(Alloy, 6)));

            Assert.IsTrue(pack[1].IsEmpty);
            Assert.IsTrue(vault[0].IsEmpty);
            Assert.AreEqual(new ItemStack(Alloy, 4), deepVault[2]);
        }

        [Test]
        public void WhatIsCarriedIsEnough_NothingStoredIsTouched()
        {
            pack[0] = new ItemStack(Alloy, 4);
            vault[0] = new ItemStack(Alloy, 9);

            Assert.IsTrue(Spend(new ItemCost(Alloy, 3)));

            Assert.AreEqual(new ItemStack(Alloy, 1), pack[0]);
            Assert.AreEqual(new ItemStack(Alloy, 9), vault[0]);
        }

        [Test]
        public void ShortByOne_NothingIsTakenAnywhere()
        {
            pack[0] = new ItemStack(Alloy, 1);
            vault[0] = new ItemStack(Alloy, 1);
            pack[1] = new ItemStack(Failsafe, 1);

            Assert.IsFalse(Spend(new ItemCost(Failsafe, 1), new ItemCost(Alloy, 3)));

            Assert.AreEqual(new ItemStack(Alloy, 1), pack[0]);
            Assert.AreEqual(new ItemStack(Alloy, 1), vault[0]);
            Assert.AreEqual(new ItemStack(Failsafe, 1), pack[1]);
        }

        [Test]
        public void TheSameItemNamedTwice_IsOwedTwice()
        {
            pack[0] = new ItemStack(Alloy, 3);
            Assert.IsFalse(Spend(new ItemCost(Alloy, 2), new ItemCost(Alloy, 2)));
            Assert.AreEqual(new ItemStack(Alloy, 3), pack[0]);

            vault[0] = new ItemStack(Alloy, 1);
            Assert.IsTrue(Spend(new ItemCost(Alloy, 2), new ItemCost(Alloy, 2)));
            Assert.IsTrue(pack[0].IsEmpty);
            Assert.IsTrue(vault[0].IsEmpty);
        }

        [Test]
        public void AnItemWithStateOfItsOwn_NeverPaysACost()
        {
            pack[0] = new ItemStack(Sword, 1, 0x2A); // a tempered piece
            Assert.AreEqual(0, ItemCostRules.Count(pack, Sword));
            Assert.IsFalse(Spend(new ItemCost(Sword, 1)));
            Assert.AreEqual(new ItemStack(Sword, 1, 0x2A), pack[0]);

            vault[1] = new ItemStack(Sword, 1); // a plain one does
            Assert.IsTrue(Spend(new ItemCost(Sword, 1)));
            Assert.AreEqual(new ItemStack(Sword, 1, 0x2A), pack[0]);
            Assert.IsTrue(vault[1].IsEmpty);
        }

        [Test]
        public void NoItemOrNoCount_OwesNothing()
        {
            pack[0] = new ItemStack(Alloy, 1);
            Assert.IsTrue(Spend(new ItemCost(0, 1), new ItemCost(Alloy, 0)));
            Assert.IsTrue(ItemCostRules.TrySpend(new[] { pack }, null));
            Assert.AreEqual(new ItemStack(Alloy, 1), pack[0]);
        }

        [Test]
        public void ACostSpreadOverSeveralStacks_TakesThemInOrder()
        {
            pack[0] = new ItemStack(Alloy, 2);
            pack[3] = new ItemStack(Alloy, 2);
            Assert.IsTrue(Spend(new ItemCost(Alloy, 3)));
            Assert.IsTrue(pack[0].IsEmpty);
            Assert.AreEqual(new ItemStack(Alloy, 1), pack[3]);
        }
    }
}
