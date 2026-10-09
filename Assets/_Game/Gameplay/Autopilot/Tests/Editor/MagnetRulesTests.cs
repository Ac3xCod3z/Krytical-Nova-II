using System.Collections.Generic;
using KryticalNova.Items;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Autopilot.Tests
{
    /// <summary>What Magnet takes, and that a character's saved choices come back inside their limits.</summary>
    public sealed class MagnetRulesTests
    {
        private readonly List<Object> made = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in made) Object.DestroyImmediate(o);
            made.Clear();
        }

        private ItemDefinition Item(string name, ItemRarity rarity, EquipSlot slot = EquipSlot.None, float health = 0f)
        {
            ItemDefinition item = ScriptableObject.CreateInstance<ItemDefinition>();
            made.Add(item);
            item.displayName = name;
            item.rarity = rarity;
            item.equipSlot = slot;
            item.restoreHealth = health;
            return item;
        }

        [Test]
        public void GearIsTakenFromTheChosenRarityUp()
        {
            var preferences = new AutopilotPreferences(); // Uncommon and better
            Assert.IsFalse(MagnetRules.Wants(preferences, Item("Rusty Blade", ItemRarity.Common, EquipSlot.Weapon)));
            Assert.IsTrue(MagnetRules.Wants(preferences, Item("Fine Blade", ItemRarity.Uncommon, EquipSlot.Weapon)));
            Assert.IsTrue(MagnetRules.Wants(preferences, Item("Rare Blade", ItemRarity.Rare, EquipSlot.Weapon)));
            preferences.minGearRarity = ItemRarity.Common;
            Assert.IsTrue(MagnetRules.Wants(preferences, Item("Rusty Blade", ItemRarity.Common, EquipSlot.Weapon)));
        }

        [Test]
        public void StimsFollowTheirSwitchAndOtherThingsAreAlwaysTaken()
        {
            var preferences = new AutopilotPreferences();
            ItemDefinition stim = Item("HP Stim", ItemRarity.Common, health: 100f);
            Assert.IsTrue(MagnetRules.Wants(preferences, stim));
            preferences.magnetStims = false;
            Assert.IsFalse(MagnetRules.Wants(preferences, stim));
            Assert.IsTrue(MagnetRules.Wants(preferences, Item("Alumite", ItemRarity.Common)), "a material");
        }

        [Test]
        public void TheNameListsWinAndNeverBeatsAlways()
        {
            var preferences = new AutopilotPreferences
            {
                alwaysTake = new[] { "rusty blade", "Alumite" },
                neverTake = new[] { "Alumite", "Fine Blade" },
            };
            Assert.IsTrue(MagnetRules.Wants(preferences, Item("Rusty Blade", ItemRarity.Common, EquipSlot.Weapon)), "always, whatever its rarity");
            Assert.IsFalse(MagnetRules.Wants(preferences, Item("Fine Blade", ItemRarity.Uncommon, EquipSlot.Weapon)), "never, whatever its rarity");
            Assert.IsFalse(MagnetRules.Wants(preferences, Item("Alumite", ItemRarity.Common)), "on both lists: never wins");
        }

        [Test]
        public void MagnetOffTakesNothing()
        {
            var preferences = new AutopilotPreferences { magnetOn = false, alwaysTake = new[] { "HP Stim" } };
            Assert.IsFalse(MagnetRules.Wants(preferences, Item("HP Stim", ItemRarity.Common, health: 100f)));
            Assert.IsFalse(MagnetRules.Wants(null, Item("HP Stim", ItemRarity.Common, health: 100f)));
        }

        [Test]
        public void SavedChoicesComeBackInsideTheirLimits()
        {
            AutopilotSettings settings = ScriptableObject.CreateInstance<AutopilotSettings>();
            made.Add(settings);
            var wild = new AutopilotPreferences
            {
                huntRadius = 900f, gatherCount = 50, stimHealthPercent = 400f, attackSlot = 99, areaSlot = -7,
                alwaysTake = new[] { "  HP Stim  ", "", null, new string('x', 200) },
            };
            AutopilotPreferences back = AutopilotPreferences.FromJson(wild.ToJson(), settings);
            Assert.AreEqual(settings.huntRadius.y, back.huntRadius);
            Assert.AreEqual(settings.maxGatherCount, back.gatherCount);
            Assert.AreEqual(95f, back.stimHealthPercent);
            Assert.AreEqual(AutopilotPreferences.NoSlot, back.attackSlot);
            Assert.AreEqual(AutopilotPreferences.NoSlot, back.areaSlot);
            Assert.AreEqual(2, back.alwaysTake.Length);
            Assert.AreEqual("HP Stim", back.alwaysTake[0]);
            Assert.AreEqual(40, back.alwaysTake[1].Length);
        }

        [Test]
        public void BrokenOrMissingJsonGivesTheDefaults()
        {
            AutopilotPreferences fresh = AutopilotPreferences.FromJson("{ not json", null);
            Assert.IsTrue(fresh.magnetOn);
            Assert.IsTrue(fresh.skipTagged);
            Assert.AreEqual(1, fresh.gatherCount);
            Assert.AreEqual(60f, AutopilotPreferences.FromJson(null, null).stimHealthPercent);
            Assert.AreEqual(ItemRarity.Uncommon, AutopilotPreferences.FromJson(string.Empty, null).minGearRarity);
        }

        [Test]
        public void TimeReadsAsHoursAndMinutes()
        {
            Assert.AreEqual("5 h 00 min", PlayerAutopilot.FormatTime(5f * 3600f));
            Assert.AreEqual("4 h 59 min", PlayerAutopilot.FormatTime(5f * 3600f - 30f));
            Assert.AreEqual("12 min 30 s", PlayerAutopilot.FormatTime(750f));
            Assert.AreEqual("0 min 00 s", PlayerAutopilot.FormatTime(-4f));
        }
    }
}
