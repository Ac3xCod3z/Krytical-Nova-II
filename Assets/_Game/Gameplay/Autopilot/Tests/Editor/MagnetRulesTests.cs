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

        private ItemDefinition Item(string name, ItemRarity rarity, EquipSlot slot = EquipSlot.None, float health = 0f, ushort id = 0)
        {
            ItemDefinition item = ScriptableObject.CreateInstance<ItemDefinition>();
            made.Add(item);
            item.itemId = id;
            item.displayName = name;
            item.rarity = rarity;
            item.equipSlot = slot;
            item.restoreHealth = health;
            return item;
        }

        [Test]
        public void EachRarityOfGearHasItsOwnTick()
        {
            var preferences = new AutopilotPreferences(); // Common off, the rest on
            ItemDefinition common = Item("Rusty Blade", ItemRarity.Common, EquipSlot.Weapon);
            ItemDefinition uncommon = Item("Fine Blade", ItemRarity.Uncommon, EquipSlot.Weapon);
            ItemDefinition rare = Item("Rare Blade", ItemRarity.Rare, EquipSlot.Weapon);
            ItemDefinition epic = Item("Epic Blade", ItemRarity.Epic, EquipSlot.Weapon);
            Assert.IsFalse(MagnetRules.Wants(preferences, common));
            Assert.IsTrue(MagnetRules.Wants(preferences, uncommon));
            Assert.IsTrue(MagnetRules.Wants(preferences, rare));
            Assert.IsTrue(MagnetRules.Wants(preferences, epic));

            // Not a floor: any one can be left out on its own.
            preferences.takeCommon = true;
            preferences.takeRare = false;
            Assert.IsTrue(MagnetRules.Wants(preferences, common));
            Assert.IsTrue(MagnetRules.Wants(preferences, uncommon));
            Assert.IsFalse(MagnetRules.Wants(preferences, rare));
            Assert.IsTrue(MagnetRules.Wants(preferences, epic));
            preferences.takeEpic = false;
            Assert.IsFalse(MagnetRules.Wants(preferences, epic));
            Assert.IsFalse(MagnetRules.Wants(preferences, Item("Relic Blade", ItemRarity.Legendary, EquipSlot.Weapon)), "above Epic goes with Epic");
        }

        [Test]
        public void StimsAndOtherThingsEachFollowTheirTick()
        {
            var preferences = new AutopilotPreferences();
            ItemDefinition stim = Item("HP Stim", ItemRarity.Common, health: 100f);
            ItemDefinition material = Item("Alumite", ItemRarity.Common);
            Assert.IsTrue(MagnetRules.Wants(preferences, stim));
            Assert.IsTrue(MagnetRules.Wants(preferences, material), "a material is not gear: the Common tick does not touch it");
            preferences.magnetStims = false;
            Assert.IsFalse(MagnetRules.Wants(preferences, stim));
            Assert.IsTrue(MagnetRules.Wants(preferences, material));
            preferences.magnetMaterials = false;
            Assert.IsFalse(MagnetRules.Wants(preferences, material));
        }

        [Test]
        public void CredsAreTakenWhateverIsTicked()
        {
            var preferences = new AutopilotPreferences
            {
                takeCommon = false, takeUncommon = false, takeRare = false, takeEpic = false, magnetStims = false, magnetMaterials = false,
            };
            ItemDefinition creds = Item("Creds", ItemRarity.Common, id: LootRules.CredsItemId);
            Assert.IsTrue(MagnetRules.Wants(preferences, creds));
            preferences.magnetOn = false;
            Assert.IsFalse(MagnetRules.Wants(preferences, creds), "Magnet off takes nothing at all");
        }

        [Test]
        public void MagnetOffTakesNothing()
        {
            var preferences = new AutopilotPreferences { magnetOn = false };
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
                takeCommon = true, takeEpic = false,
            };
            AutopilotPreferences back = AutopilotPreferences.FromJson(wild.ToJson(), settings);
            Assert.AreEqual(settings.huntRadius.y, back.huntRadius);
            Assert.AreEqual(settings.maxGatherCount, back.gatherCount);
            Assert.AreEqual(95f, back.stimHealthPercent);
            Assert.AreEqual(AutopilotPreferences.NoSlot, back.attackSlot);
            Assert.AreEqual(AutopilotPreferences.NoSlot, back.areaSlot);
            Assert.IsTrue(back.takeCommon);
            Assert.IsFalse(back.takeEpic);
            Assert.IsTrue(back.takeRare);
        }

        [Test]
        public void BrokenOrMissingJsonGivesTheDefaults()
        {
            AutopilotPreferences fresh = AutopilotPreferences.FromJson("{ not json", null);
            Assert.IsTrue(fresh.magnetOn);
            Assert.IsTrue(fresh.skipTagged);
            Assert.AreEqual(1, fresh.gatherCount);
            Assert.AreEqual(60f, AutopilotPreferences.FromJson(null, null).stimHealthPercent);
            AutopilotPreferences empty = AutopilotPreferences.FromJson(string.Empty, null);
            Assert.IsFalse(empty.takeCommon);
            Assert.IsTrue(empty.takeUncommon && empty.takeRare && empty.takeEpic && empty.magnetStims && empty.magnetMaterials);
            // A save from before the tick boxes (a rarity floor and typed lists) still loads, on the defaults.
            AutopilotPreferences old = AutopilotPreferences.FromJson("{\"magnetOn\":true,\"minGearRarity\":2,\"alwaysTake\":[\"HP Stim\"]}", null);
            Assert.IsTrue(old.magnetOn && old.takeUncommon);
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
