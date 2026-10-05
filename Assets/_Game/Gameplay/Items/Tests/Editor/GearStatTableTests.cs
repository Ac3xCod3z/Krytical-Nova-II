using System.Collections.Generic;
using KryticalNova.Items;
using KryticalNova.Progression;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Tests
{
    /// <summary>The stat formula itself, on a table built in the test: slopes, rounding, the ladder and exact rows.</summary>
    public sealed class GearStatTableTests
    {
        private GearStatTable table;
        private readonly List<ItemStatModifier> primary = new List<ItemStatModifier>();
        private readonly List<ItemStatModifier> secondary = new List<ItemStatModifier>();

        [SetUp]
        public void SetUp() => table = ScriptableObject.CreateInstance<GearStatTable>();

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(table);

        private float Value(List<ItemStatModifier> list, StatType stat)
        {
            foreach (var m in list) if (m.stat == stat) return m.value;
            return float.NaN;
        }

        [Test]
        public void WeaponAttackIsLevelTimesSlopeTimesRarity()
        {
            table.Resolve(EquipSlot.Weapon, ItemRarity.Common, 10, primary, secondary, out _);
            Assert.AreEqual(52f, Value(primary, StatType.AttackPower)); // 10 x 5.2 x 1.00
            Assert.AreEqual(25f, Value(secondary, StatType.HitRate));   // 10 x 2.5, never scaled by rarity

            table.Resolve(EquipSlot.Weapon, ItemRarity.Epic, 100, primary, secondary, out _);
            Assert.AreEqual(884f, Value(primary, StatType.AttackPower)); // 100 x 5.2 x 1.70
            Assert.AreEqual(250f, Value(secondary, StatType.HitRate));
        }

        [Test]
        public void ArmorDefenseAndEvasionFollowTheirSlopes()
        {
            table.Resolve(EquipSlot.Armor, ItemRarity.Rare, 45, primary, secondary, out _);
            Assert.AreEqual(137f, Value(primary, StatType.DefensePower)); // 45 x 2.1 x 1.45 = 137.03
            Assert.AreEqual(16f, Value(secondary, StatType.EvasionRate)); // 45 x 0.35 = 15.75
        }

        [Test]
        public void EvasionStartsAtLevelFive()
        {
            table.Resolve(EquipSlot.Boots, ItemRarity.Common, 4, primary, secondary, out _);
            Assert.IsNaN(Value(secondary, StatType.EvasionRate));
            table.Resolve(EquipSlot.Boots, ItemRarity.Common, 7, primary, secondary, out _);
            Assert.AreEqual(2f, Value(secondary, StatType.EvasionRate)); // 7 x 0.35 = 2.45
        }

        [Test]
        public void HalvesRoundUp()
        {
            table.Resolve(EquipSlot.Weapon, ItemRarity.Common, 103, primary, secondary, out _);
            Assert.AreEqual(258f, Value(secondary, StatType.HitRate)); // 257.5
        }

        [Test]
        public void GlovesCarryAShareOfTheWeaponsHitRate()
        {
            table.Resolve(EquipSlot.Gloves, ItemRarity.Common, 10, primary, secondary, out _);
            Assert.AreEqual(6f, Value(primary, StatType.DefensePower)); // 10 x 0.55 = 5.5
            Assert.AreEqual(10f, Value(secondary, StatType.HitRate));   // 40% of 25
        }

        [Test]
        public void AnExactRowWinsOverTheFormula()
        {
            table.overrides = new[]
            {
                new GearStatTable.Override { rarity = ItemRarity.Epic, slot = EquipSlot.Weapon, level = 109, stat = StatType.AttackPower, value = 963 },
                new GearStatTable.Override { rarity = ItemRarity.Epic, slot = EquipSlot.Weapon, level = 109, stat = StatType.ElementalAttack, value = 143 },
            };
            table.Resolve(EquipSlot.Weapon, ItemRarity.Epic, 109, primary, secondary, out _);
            Assert.AreEqual(963f, Value(primary, StatType.AttackPower)); // the formula gives 964
            Assert.AreEqual(143f, Value(secondary, StatType.ElementalAttack));
        }

        [Test]
        public void LadderRowsGrowBetweenTheirTwoLevels()
        {
            table.ladder = new[]
            {
                new GearStatTable.LadderRow { rarity = ItemRarity.Rare, slot = EquipSlot.Ring, stat = StatType.Agility, fromLevel = 45, fromValue = 11, toLevel = 105, toValue = 23 },
            };
            table.Resolve(EquipSlot.Ring, ItemRarity.Rare, 45, primary, secondary, out _);
            Assert.AreEqual(11f, Value(secondary, StatType.Agility));
            table.Resolve(EquipSlot.Ring, ItemRarity.Rare, 75, primary, secondary, out _);
            Assert.AreEqual(17f, Value(secondary, StatType.Agility));
            table.Resolve(EquipSlot.Ring, ItemRarity.Rare, 105, primary, secondary, out _);
            Assert.AreEqual(23f, Value(secondary, StatType.Agility));
            table.Resolve(EquipSlot.Ring, ItemRarity.Uncommon, 40, primary, secondary, out _);
            Assert.IsNaN(Value(secondary, StatType.Agility), "Another rarity's row does not apply");
        }

        [Test]
        public void SkillBonusComesFromItsLevelRange()
        {
            table.skillLevels = new[]
            {
                new GearStatTable.SkillRow { rarity = ItemRarity.Epic, slot = EquipSlot.Weapon, fromLevel = 100, toLevel = 106, levels = 4 },
                new GearStatTable.SkillRow { rarity = ItemRarity.Epic, slot = EquipSlot.Weapon, fromLevel = 109, toLevel = 112, levels = 5 },
            };
            table.Resolve(EquipSlot.Weapon, ItemRarity.Epic, 106, primary, secondary, out byte low);
            table.Resolve(EquipSlot.Weapon, ItemRarity.Epic, 109, primary, secondary, out byte high);
            table.Resolve(EquipSlot.Armor, ItemRarity.Epic, 109, primary, secondary, out byte none);
            Assert.AreEqual(4, low);
            Assert.AreEqual(5, high);
            Assert.AreEqual(0, none);
        }

        [Test]
        public void ResolverHandsOutEveryStatOfAnItemUnderOneSource()
        {
            var item = ScriptableObject.CreateInstance<ItemDefinition>();
            try
            {
                item.itemId = 10000;
                item.baseStats = new[] { new ItemStatModifier { stat = StatType.AttackPower, modType = ModifierType.Flat, value = 884 } };
                item.secondaryStats = new[] { new ItemStatModifier { stat = StatType.HitRate, modType = ModifierType.Flat, value = 250 } };
                item.statModifiers.Add(new ItemStatModifier { stat = StatType.Luck, modType = ModifierType.Flat, value = 3 });
                var source = new object();
                var result = new List<StatModifier>();
                GearStatResolver.Resolve(new ItemStack(10000, 1), item, source, result);
                Assert.AreEqual(3, result.Count);
                foreach (var modifier in result) Assert.AreSame(source, modifier.Source);
                Assert.AreEqual(884f, GearStatResolver.Total(item, StatType.AttackPower));

                result.Clear();
                GearStatResolver.Resolve(ItemStack.Empty, item, source, result);
                Assert.AreEqual(0, result.Count, "An empty slot grants nothing");
            }
            finally
            {
                Object.DestroyImmediate(item);
            }
        }
    }
}
