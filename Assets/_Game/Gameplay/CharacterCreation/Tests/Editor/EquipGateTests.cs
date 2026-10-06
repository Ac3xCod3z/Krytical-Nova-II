using System.Collections.Generic;
using KryticalNova.Items;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Tests
{
    /// <summary>The equip gate lets the caller refuse an equip (wrong faction or body) without changing any state.</summary>
    public sealed class EquipGateTests
    {
        private sealed class Lookup : IItemLookup
        {
            public readonly Dictionary<ushort, ItemDefinition> Items = new Dictionary<ushort, ItemDefinition>();
            public ItemDefinition Get(ushort id) => Items.TryGetValue(id, out var d) ? d : null;
        }

        private const ushort ArmorA = 2001, ArmorB = 2002;
        private const string Refusal = "Your faction cannot use this.";
        private Lookup lookup;
        private ItemStack[] bag;
        private EquippedItems loadout;

        [SetUp]
        public void SetUp()
        {
            lookup = new Lookup();
            foreach (ushort id in new[] { ArmorA, ArmorB })
            {
                var d = ScriptableObject.CreateInstance<ItemDefinition>();
                d.itemId = id;
                d.equipSlot = EquipSlot.Armor;
                lookup.Items[id] = d;
            }
            bag = new ItemStack[4];
            loadout = default;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var d in lookup.Items.Values) Object.DestroyImmediate(d);
        }

        [Test]
        public void RefusedEquipLeavesBagAndLoadoutUntouched()
        {
            bag[0] = new ItemStack(ArmorA, 1);
            Assert.IsFalse(InventoryRules.TryQuickEquip(bag, ref loadout, lookup, 0, out string reason, _ => Refusal));
            Assert.AreEqual(Refusal, reason);
            Assert.AreEqual(ArmorA, bag[0].itemId);
            Assert.AreEqual(0, loadout.ids.armor);
        }

        [Test]
        public void GateThatAllowsBehavesLikeNoGate()
        {
            bag[0] = new ItemStack(ArmorA, 1);
            Assert.IsTrue(InventoryRules.TryQuickEquip(bag, ref loadout, lookup, 0, out _, _ => null));
            Assert.AreEqual(ArmorA, loadout.ids.armor);
            Assert.IsTrue(bag[0].IsEmpty);
        }

        [Test]
        public void DragOntoASlotIsGatedToo()
        {
            bag[0] = new ItemStack(ArmorA, 1);
            Assert.IsFalse(InventoryRules.TryMove(bag, ref loadout, lookup, ItemLocation.Bag(0), ItemLocation.Equip(EquipSlot.Armor),
                out string reason, _ => Refusal));
            Assert.AreEqual(Refusal, reason);
            Assert.AreEqual(0, loadout.ids.armor);
        }

        [Test]
        public void SwappingWornGearForRefusedGearIsBlocked()
        {
            loadout.ids.armor = ArmorA;
            bag[0] = new ItemStack(ArmorB, 1);
            Assert.IsFalse(InventoryRules.TryMove(bag, ref loadout, lookup, ItemLocation.Equip(EquipSlot.Armor), ItemLocation.Bag(0),
                out string reason, item => item.itemId == ArmorB ? Refusal : null));
            Assert.AreEqual(Refusal, reason);
            Assert.AreEqual(ArmorA, loadout.ids.armor);
            Assert.AreEqual(ArmorB, bag[0].itemId);
        }
    }
}
