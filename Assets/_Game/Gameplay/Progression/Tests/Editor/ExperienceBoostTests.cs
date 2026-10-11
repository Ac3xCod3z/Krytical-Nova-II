using KryticalNova.Crafting;
using KryticalNova.Economy;
using KryticalNova.Items;
using KryticalNova.Progression;
using NUnit.Framework;
using UnityEditor;

namespace KryticalNova.Tests
{
    /// <summary>
    /// The XP boosters as the owner set them (11 Oct 2026): character x1.5, pet x2, both x1.5, an hour each; those
    /// that run together add up, and the same one used again adds its hour. And the nine Zenith Dossiers: one for
    /// each weapon class's Zenith technique, each unlocking that technique alone.
    /// </summary>
    public sealed class ExperienceBoostTests
    {
        private const string SettingsPath = "Assets/_Game/Gameplay/Economy/Data/Assets/EconomySettings.asset";
        private const string CatalogPath = "Assets/_Game/Gameplay/Items/Data/Assets/ItemCatalog.asset";
        private const string SkillsRoot = "Assets/_Game/Gameplay/Progression/Data/Assets/Skills/";
        private const float Tolerance = 0.0001f;

        private static readonly ExperienceBooster[] Three =
        {
            new ExperienceBooster { itemId = 9500, characterPercent = 50, petPercent = 0, minutes = 60 },
            new ExperienceBooster { itemId = 9501, characterPercent = 0, petPercent = 100, minutes = 60 },
            new ExperienceBooster { itemId = 9502, characterPercent = 50, petPercent = 50, minutes = 60 },
        };

        [Test]
        public void EachBoosterAlone_GivesItsOwnShare()
        {
            Assert.AreEqual(0.5f, ExperienceBoostRules.CharacterBonus(Three, new[] { 10f, 0f, 0f }), Tolerance, "Field: character x1.5");
            Assert.AreEqual(0f, ExperienceBoostRules.PetBonus(Three, new[] { 10f, 0f, 0f }), Tolerance);
            Assert.AreEqual(1f, ExperienceBoostRules.PetBonus(Three, new[] { 0f, 10f, 0f }), Tolerance, "Companion: pet x2");
            Assert.AreEqual(0f, ExperienceBoostRules.CharacterBonus(Three, new[] { 0f, 10f, 0f }), Tolerance);
            Assert.AreEqual(0.5f, ExperienceBoostRules.CharacterBonus(Three, new[] { 0f, 0f, 10f }), Tolerance, "Tandem: character x1.5");
            Assert.AreEqual(0.5f, ExperienceBoostRules.PetBonus(Three, new[] { 0f, 0f, 10f }), Tolerance, "Tandem: pet x1.5");
        }

        [Test]
        public void BoostersThatRunTogether_AddUp()
        {
            float[] all = { 10f, 10f, 10f };
            Assert.AreEqual(1f, ExperienceBoostRules.CharacterBonus(Three, all), Tolerance, "Field + Tandem: character x2, not x2.25");
            Assert.AreEqual(1.5f, ExperienceBoostRules.PetBonus(Three, all), Tolerance, "Companion + Tandem: pet x2.5");
            Assert.AreEqual(0f, ExperienceBoostRules.CharacterBonus(Three, new[] { 0f, 0f, 0f }), Tolerance, "nothing running: nothing added");
        }

        [Test]
        public void TheSameBoosterAgain_AddsItsHour_UpToTheMostACharacterHolds()
        {
            Assert.AreEqual(3600f, ExperienceBoostRules.AfterUse(0f, 60, 24), Tolerance);
            Assert.AreEqual(3600f + 1500f, ExperienceBoostRules.AfterUse(1500f, 60, 24), Tolerance, "its hour is added to what is left");
            Assert.IsFalse(ExperienceBoostRules.WouldWaste(23f * 3600f, 60, 24), "the twenty-fourth hour fits");
            Assert.IsTrue(ExperienceBoostRules.WouldWaste(23f * 3600f + 60f, 60, 24), "a minute more than a day does not");
        }

        [Test]
        public void APetsKill_IsWorthItsShareMore()
        {
            Assert.AreEqual(400u, ExperienceBoostRules.Boosted(400u, 0f));
            Assert.AreEqual(800u, ExperienceBoostRules.Boosted(400u, 1f), "x2");
            Assert.AreEqual(1000u, ExperienceBoostRules.Boosted(400u, 1.5f), "x2.5");
        }

        [Test]
        public void TheSettings_HoldTheThreeBoosters_AndEachHasItsItem()
        {
            var settings = AssetDatabase.LoadAssetAtPath<EconomySettings>(SettingsPath);
            var catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>(CatalogPath);
            Assert.NotNull(settings, SettingsPath);
            Assert.NotNull(catalog, CatalogPath);
            Assert.AreEqual(3, settings.boosters.Length, "Field, Companion, Tandem");
            foreach (ExperienceBooster booster in settings.boosters)
            {
                ItemDefinition item = catalog.Get(booster.itemId);
                Assert.NotNull(item, $"item {booster.itemId} is in the catalog (Items > 5. Generate Utility Items)");
                Assert.AreEqual(ItemUseEffect.ExperienceBoost, item.useEffect, item.displayName);
                Assert.Greater(booster.characterPercent + booster.petPercent, 0, item.displayName + " adds something");
                Assert.AreEqual(60, booster.minutes, item.displayName + " runs an hour (owner, 11 Oct 2026)");
            }
        }

        private static readonly (ushort dossier, string skill)[] Zeniths =
        {
            (CraftItemIds.ZenithMonoblade, "BladeSkills/Skill_ThunderousDragon"), (CraftItemIds.ZenithGlaive, "GlaiveSkills/Skill_Glaive_S8"),
            (CraftItemIds.ZenithCarbine, "CarbineSkills/Skill_Carbine_S8"), (CraftItemIds.ZenithHarvester, "HarvesterSkills/Skill_Harvester_S8"),
            (CraftItemIds.ZenithServoClaws, "ServoClawsSkills/Skill_ServoClaws_S8"), (CraftItemIds.ZenithArcCaster, "ArcCasterSkills/Skill_ArcCaster_S8"),
            (CraftItemIds.ZenithLongaxe, "LongaxeSkills/Skill_Longaxe_S8"), (CraftItemIds.ZenithClaymore, "ClaymoreSkills/Skill_Claymore_S8"),
            (CraftItemIds.ZenithWarbow, "WarbowSkills/Skill_Warbow_S8"),
        };

        [Test]
        public void EveryZenithTechnique_HasADossierOfItsOwn_ThatUnlocksItAlone()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>(CatalogPath);
            Assert.NotNull(catalog, CatalogPath);
            Assert.AreEqual(9, CraftItemIds.ZenithDossiers.Length);
            foreach ((ushort dossier, string path) in Zeniths)
            {
                var skill = AssetDatabase.LoadAssetAtPath<SkillDefinitionSO>(SkillsRoot + path + ".asset");
                ItemDefinition item = catalog.Get(dossier);
                Assert.NotNull(skill, path);
                Assert.NotNull(item, $"Zenith Dossier {dossier} is in the catalog");
                Assert.AreEqual(ItemUseEffect.LearnSkill, item.useEffect, item.displayName);
                Assert.AreEqual(dossier, skill.unlockItemId, $"{skill.displayName} is unlocked by its own Dossier (Items > 11. Build Crafting Recipes)");
                Assert.IsTrue(skill.requiresSkillBook, skill.displayName);
                Assert.AreEqual(ClassSkillSlot.S8, skill.classSlot, skill.displayName);
                Assert.AreEqual(skill.skillId, skill.LockKey, $"{skill.displayName}: one Dossier, one skill");
                Assert.AreEqual("slot:S8", skill.RankKey, $"{skill.displayName}: its rank is still its slot's");
                StringAssert.Contains(skill.displayName, item.displayName);
                StringAssert.DoesNotContain("War God", skill.description);
                StringAssert.DoesNotContain("War God", skill.acquisitionNote);
            }
        }
    }
}
