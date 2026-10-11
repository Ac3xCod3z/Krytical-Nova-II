using System.Collections.Generic;
using KryticalNova.Crafting;
using KryticalNova.Items;
using KryticalNova.Progression;
using NUnit.Framework;
using UnityEditor;

namespace KryticalNova.Tests
{
    /// <summary>
    /// Skill Chips as the owner set them (11 Oct 2026): every class has a single hit and a multi hit of level 60 that
    /// open with a chip; the pair that was level 60 (slots 6 and 7) is level 80 and opens with a chip of the Prime
    /// grounds; Aegis, Killer Instinct and Hot Streak open with a chip too. One chip opens one skill.
    /// </summary>
    public sealed class SkillChipTests
    {
        private const string ItemCatalogPath = "Assets/_Game/Gameplay/Items/Data/Assets/ItemCatalog.asset";
        private const string ClassCatalogPath = "Assets/_Game/Gameplay/Items/Data/Assets/WeaponClassCatalog.asset";
        private const string SharedFolder = "Assets/_Game/Gameplay/Progression/Data/Assets/Skills/SelfBuffSkills/";

        [Test]
        public void EveryChipIsAnItemOfItsOwn()
        {
            var items = AssetDatabase.LoadAssetAtPath<ItemCatalog>(ItemCatalogPath);
            Assert.NotNull(items, ItemCatalogPath);
            var seen = new HashSet<ushort>();
            var pictures = new HashSet<UnityEngine.Sprite>();
            foreach (ushort chip in CraftItemIds.SkillChips())
            {
                Assert.AreNotEqual(0, chip);
                Assert.IsTrue(seen.Add(chip), $"chip {chip} twice");
                ItemDefinition item = items.Get(chip);
                Assert.NotNull(item, $"Skill Chip {chip} is in the catalog (Items > 5. Generate Utility Items)");
                Assert.AreEqual(ItemUseEffect.LearnSkill, item.useEffect, item.displayName);
                StringAssert.StartsWith("Skill Chip: ", item.displayName);
                Assert.NotNull(item.icon, $"{item.displayName} has an icon");
                pictures.Add(item.icon);
            }
            // Two pictures for all of them: one for the chips of attack skills, one for the chips of buffs.
            Assert.AreEqual(2, pictures.Count, "the Skill Chips share two icons");
            Assert.AreNotSame(items.Get(CraftItemIds.AegisChip).icon, items.Get(CraftItemIds.SkillChip(WeaponClassId.Monoblade, ClassSkillSlot.S6)).icon);
            Assert.AreSame(items.Get(CraftItemIds.AegisChip).icon, items.Get(CraftItemIds.HotStreakChip).icon);
            Assert.AreEqual(WeaponClasses.Count * CraftItemIds.SkillChipsPerClass + 3, seen.Count);
        }

        [Test]
        public void EveryClassHasItsLevel60PairAndItsLevel80Pair_EachOpenedByItsOwnChip()
        {
            var items = AssetDatabase.LoadAssetAtPath<ItemCatalog>(ItemCatalogPath);
            var classes = AssetDatabase.LoadAssetAtPath<WeaponClassCatalog>(ClassCatalogPath);
            Assert.NotNull(classes, ClassCatalogPath);
            foreach (WeaponClassId weaponClass in WeaponClasses.All)
            {
                Assert.IsTrue(classes.TryGet(weaponClass, out WeaponClassCatalog.Entry entry), weaponClass.ToString());
                foreach (ClassSkillSlot slot in CraftItemIds.SkillChipSlots)
                {
                    SkillDefinitionSO skill = entry.SkillIn(slot);
                    Assert.NotNull(skill, $"{weaponClass} {slot}");
                    bool prime = slot == ClassSkillSlot.S6 || slot == ClassSkillSlot.S7;
                    Assert.AreEqual(prime ? 80 : 60, skill.requiredCharacterLevel, skill.displayName);
                    Assert.IsTrue(skill.requiresSkillBook, skill.displayName);
                    ushort chip = CraftItemIds.SkillChip(weaponClass, slot);
                    Assert.AreEqual(chip, skill.unlockItemId, $"{skill.displayName} is opened by its own chip (Items > 11. Build Crafting Recipes)");
                    Assert.AreEqual(skill.skillId, skill.LockKey, $"{skill.displayName}: one chip, one skill");
                    StringAssert.Contains(skill.displayName, items.Get(chip).displayName);
                    Assert.NotNull(skill.icon, $"{skill.displayName} has an icon");
                }
                // A single hit and a multi hit, as the pair they stand beside.
                Assert.AreEqual(1, entry.SkillIn(ClassSkillSlot.Lv60Single).hitCount, weaponClass.ToString());
                Assert.Greater(entry.SkillIn(ClassSkillSlot.Lv60Multi).hitCount, 1, weaponClass.ToString());
                // The level 60 pair is no stronger than the level 80 pair at full rank.
                Assert.LessOrEqual(entry.SkillIn(ClassSkillSlot.Lv60Single).maxDamagePercent, entry.SkillIn(ClassSkillSlot.S6).maxDamagePercent, weaponClass.ToString());
                Assert.LessOrEqual(entry.SkillIn(ClassSkillSlot.Lv60Multi).maxDamagePercent, entry.SkillIn(ClassSkillSlot.S7).maxDamagePercent, weaponClass.ToString());
            }
        }

        [Test]
        public void TheThreeSharedSkillsOpenWithAChip()
        {
            foreach ((string asset, ushort chip, int level) in new[]
                     {
                         ("Skill_HeavenlyShield", CraftItemIds.AegisChip, 60), ("Skill_HeavenlyBlow", CraftItemIds.KillerInstinctChip, 60),
                         ("Skill_GarudasPrayer", CraftItemIds.HotStreakChip, 70),
                     })
            {
                var skill = AssetDatabase.LoadAssetAtPath<SkillDefinitionSO>(SharedFolder + asset + ".asset");
                Assert.NotNull(skill, asset);
                Assert.AreEqual(chip, skill.unlockItemId, skill.displayName);
                Assert.AreEqual(level, skill.requiredCharacterLevel, skill.displayName);
                Assert.IsTrue(skill.requiresSkillBook, skill.displayName);
            }
        }
    }
}
