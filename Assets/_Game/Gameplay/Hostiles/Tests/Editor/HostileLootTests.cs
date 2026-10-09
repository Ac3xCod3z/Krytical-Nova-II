using System.Collections.Generic;
using KryticalNova.Items;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Hostiles.Tests
{
    /// <summary>What a kill drops, from fixed rolls: Creds by chance and spread, gear by level, nothing at a grey gap.</summary>
    public sealed class HostileLootTests
    {
        private HostileStatCurve curve;
        private HostileSettings settings;
        private ItemCatalog catalog;
        private readonly List<Object> made = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            curve = Make<HostileStatCurve>();
            settings = Make<HostileSettings>();
            catalog = Make<ItemCatalog>();
            var items = new List<ItemDefinition>
            {
                Gear(1, 1, ItemRarity.Common), Gear(2, 19, ItemRarity.Common), Gear(3, 22, ItemRarity.Common),
                Gear(4, 20, ItemRarity.Uncommon), Gear(5, 45, ItemRarity.Rare),
            };
            ItemDefinition stim = Make<ItemDefinition>();
            stim.itemId = 9001;
            items.Add(stim);
            catalog.SetContents(items, new List<ItemDefinition>(), new List<StarterItem>());
            settings.catalog = catalog;
            settings.stims = new[] { stim };
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in made) Object.DestroyImmediate(o);
            made.Clear();
        }

        private T Make<T>() where T : ScriptableObject
        {
            T asset = ScriptableObject.CreateInstance<T>();
            made.Add(asset);
            return asset;
        }

        private ItemDefinition Gear(ushort id, byte level, ItemRarity rarity)
        {
            ItemDefinition item = Make<ItemDefinition>();
            item.itemId = id;
            item.itemLevel = level;
            item.rarity = rarity;
            item.equipSlot = EquipSlot.Weapon;
            return item;
        }

        private HostileLootRoll Roll(HostileStats stats, float dropPercent, bool gear, float credsChance, float credsAmount, float stim, float gearRoll, float pick, float rate = 1f) =>
            HostileLoot.Roll(stats, curve, settings, dropPercent, gear, rate, credsChance, credsAmount, stim, gearRoll, pick);

        [Test]
        public void CredsDropByChanceAndWithinTheSpread()
        {
            var stats = new HostileStats { Level = 22, Creds = 100 };
            Assert.AreEqual(0, Roll(stats, 100f, true, 0.71f, 0.5f, 1f, 1f, 0f).Creds, "70% chance: a roll of 0.71 drops none");
            Assert.AreEqual(100, Roll(stats, 100f, true, 0.69f, 0.5f, 1f, 1f, 0f).Creds, "the middle of the spread");
            Assert.AreEqual(80, Roll(stats, 100f, true, 0f, 0f, 1f, 1f, 0f).Creds, "the bottom of the spread");
            Assert.AreEqual(120, Roll(stats, 100f, true, 0f, 1f, 1f, 1f, 0f).Creds, "the top of the spread");
        }

        [Test]
        public void TheLevelGapAndTheServerRateScaleTheChances()
        {
            var stats = new HostileStats { Level = 22, Creds = 100 };
            Assert.AreEqual(0, Roll(stats, 25f, true, 0.18f, 0.5f, 1f, 1f, 0f).Creds, "a quarter of 70% is 17.5%");
            Assert.Greater(Roll(stats, 25f, true, 0.17f, 0.5f, 1f, 1f, 0f).Creds, 0);
            Assert.Greater(Roll(stats, 25f, true, 0.3f, 0.5f, 1f, 1f, 0f, rate: 2f).Creds, 0, "a doubled server rate doubles it");

            HostileLootRoll grey = Roll(stats, 0f, false, 0f, 0f, 0f, 0f, 0f);
            Assert.AreEqual(0, grey.Creds);
            Assert.IsNull(grey.Stim);
            Assert.IsNull(grey.Gear);
        }

        [Test]
        public void GearIsOfTheHostilesLevelRangeAndABandWithNoGearDropsNone()
        {
            var stats = new HostileStats { Level = 22, Creds = 1 };
            for (float pick = 0f; pick < 1f; pick += 0.1f)
            {
                ItemDefinition gear = Roll(stats, 100f, true, 1f, 0f, 1f, 0f, pick).Gear;
                Assert.IsNotNull(gear);
                Assert.That((int)gear.itemLevel, Is.InRange(19, 22));
            }
            Assert.IsNull(Roll(stats, 25f, false, 1f, 0f, 1f, 0f, 0f).Gear, "the blue band drops no gear");
            Assert.IsNull(Roll(new HostileStats { Level = 10, Creds = 1 }, 100f, true, 1f, 0f, 1f, 0f, 0f).Gear, "no gear exists for levels 7 to 10 in this catalog");
        }

        [Test]
        public void ABruiserDropsTheBestRarityItsLevelHasAndFarMoreOften()
        {
            var stats = new HostileStats { Level = 22, Creds = 1, Tier = HostileTier.Bruiser };
            for (float pick = 0f; pick < 1f; pick += 0.1f)
                Assert.AreEqual(ItemRarity.Uncommon, Roll(stats, 100f, true, 1f, 0f, 1f, 0.2f, pick).Gear.rarity);
            Assert.IsNull(Roll(new HostileStats { Level = 22, Creds = 1 }, 100f, true, 1f, 0f, 1f, 0.2f, 0f).Gear, "a Normal Hostile does not at a roll of 0.2");
        }

        [Test]
        public void AStimDropsAtItsOwnChance()
        {
            var stats = new HostileStats { Level = 5, Creds = 1 };
            Assert.IsNotNull(Roll(stats, 100f, true, 1f, 0f, 0.05f, 1f, 0f).Stim);
            Assert.IsNull(Roll(stats, 100f, true, 1f, 0f, 0.07f, 1f, 0f).Stim);
        }
    }
}
