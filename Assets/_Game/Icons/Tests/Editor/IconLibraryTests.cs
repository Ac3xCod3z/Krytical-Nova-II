using System.Collections.Generic;
using KryticalNova.EditorTools;
using KryticalNova.Progression;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Tests
{
    /// <summary>
    /// The icon library's rules on a small library made for the test: an icon is never given twice, an icon is
    /// found for a skill from its tags (its use, its class, its faction, the kind of move), and an icon another
    /// skill has is never offered.
    /// </summary>
    public sealed class IconLibraryTests
    {
        private IconLibrary library;
        private readonly List<Object> made = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            library = ScriptableObject.CreateInstance<IconLibrary>();
            made.Add(library);
            library.sets.Add(new IconLibrary.Set
            {
                id = "Fantasy/Swordman Skill", count = 6, use = IconUse.Attack, factions = IconFactions.Hegemony,
                weaponClasses = new List<WeaponClassId> { WeaponClassId.Monoblade },
            });
            library.sets.Add(new IconLibrary.Set
            {
                id = "Fantasy/Berserker Skill", count = 6, use = IconUse.Attack, factions = IconFactions.Covenant,
                weaponClasses = new List<WeaponClassId> { WeaponClassId.Longaxe },
            });
            library.sets.Add(new IconLibrary.Set { id = "Fantasy/Buff Skill", count = 6, use = IconUse.Buff, factions = IconFactions.All });
            library.sets.Add(new IconLibrary.Set { id = "Cyberpunk/Minerals", count = 6, use = IconUse.Item, factions = IconFactions.All, shape = ComicIconShape.CutOut });
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object each in made) Object.DestroyImmediate(each);
            made.Clear();
        }

        private SkillDefinitionSO Skill(string id, WeaponClassId weaponClass, ClassSkillSlot slot, bool attack, string name = "Skill")
        {
            var skill = ScriptableObject.CreateInstance<SkillDefinitionSO>();
            made.Add(skill);
            skill.skillId = id;
            skill.displayName = name;
            skill.description = string.Empty;
            skill.weaponClass = weaponClass;
            skill.classSlot = slot;
            skill.maxDamagePercent = attack ? 200f : 0f;
            return skill;
        }

        // ------------------------------------------------------------------ giving icons out

        [Test]
        public void AnIcon_IsNeverGivenTwice()
        {
            Assert.IsTrue(library.Assign(IconTargetKind.Skill, "A", "Fantasy/Swordman Skill", 3, true, out _));
            Assert.IsFalse(library.Assign(IconTargetKind.Skill, "B", "Fantasy/Swordman Skill", 3, true, out string refusal));
            StringAssert.Contains("already the icon of A", refusal);
            Assert.IsFalse(library.Assign(IconTargetKind.Item, "Alloy", "Fantasy/Swordman Skill", 3, true, out _), "not to an item either");
            Assert.IsNull(library.AssignmentOf(IconTargetKind.Skill, "B"));
            Assert.AreEqual(0, library.Duplicates().Count);
        }

        [Test]
        public void GivingASkillAnotherIcon_FreesItsOldOne()
        {
            Assert.IsTrue(library.Assign(IconTargetKind.Skill, "A", "Fantasy/Swordman Skill", 3, true, out _));
            Assert.IsTrue(library.Assign(IconTargetKind.Skill, "A", "Fantasy/Swordman Skill", 4, true, out _));
            Assert.AreEqual(1, library.assignments.Count);
            Assert.IsNull(library.UserOf("Fantasy/Swordman Skill", 3));
            Assert.AreEqual("A", library.UserOf("Fantasy/Swordman Skill", 4).target);
            Assert.IsTrue(library.Assign(IconTargetKind.Skill, "B", "Fantasy/Swordman Skill", 3, true, out _), "the old icon is free again");
        }

        [Test]
        public void AnIconThatIsNotThere_IsRefused()
        {
            Assert.IsFalse(library.Assign(IconTargetKind.Skill, "A", "Fantasy/No Such Set", 1, true, out string noSet));
            StringAssert.Contains("no set", noSet);
            Assert.IsFalse(library.Assign(IconTargetKind.Skill, "A", "Fantasy/Swordman Skill", 7, true, out string noIcon));
            StringAssert.Contains("no icon 7", noIcon);
            Assert.IsFalse(library.Assign(IconTargetKind.Skill, "A", "Fantasy/Swordman Skill", 0, true, out _));
        }

        [Test]
        public void TwoRecordsOfOneIcon_AreReported()
        {
            library.assignments.Add(new IconLibrary.Assignment { kind = IconTargetKind.Skill, target = "A", set = "Fantasy/Buff Skill", number = 2 });
            library.assignments.Add(new IconLibrary.Assignment { kind = IconTargetKind.Skill, target = "B", set = "Fantasy/Buff Skill", number = 2 });
            List<string> problems = library.Duplicates();
            Assert.AreEqual(1, problems.Count);
            StringAssert.Contains("both A and B", problems[0]);
        }

        // ------------------------------------------------------------------ finding an icon from the tags

        [Test]
        public void AnAttack_IsOfferedItsOwnClasssIconsFirst_AndNeverABuffsIcon()
        {
            IconRequest request = IconLibrary.RequestFor(Skill("FuryStrike", WeaponClassId.Monoblade, ClassSkillSlot.S1, true));
            Assert.AreEqual(IconUse.Attack, request.use);
            Assert.AreEqual(IconFactions.Hegemony, request.faction);
            var found = library.Suggest(request, 0);
            Assert.AreEqual(12, found.Count, "six of its own class and six of another's; no buff and no item icon");
            for (int i = 0; i < 6; i++) Assert.AreEqual("Fantasy/Swordman Skill", found[i].set.id, "its own class first");
            Assert.Greater(found[5].score, found[6].score);
        }

        [Test]
        public void ABuff_IsOfferedBuffIcons()
        {
            IconRequest request = IconLibrary.RequestFor(Skill("Stance", WeaponClassId.Monoblade, ClassSkillSlot.Stance, false));
            Assert.AreEqual(IconUse.Buff, request.use);
            var found = library.Suggest(request, 0);
            Assert.AreEqual(6, found.Count);
            foreach (var each in found) Assert.AreEqual("Fantasy/Buff Skill", each.set.id);
        }

        [Test]
        public void AnIconTaggedAsABuff_InsideAnAttackSet_GoesToTheClasssBuff()
        {
            IconLibrary.Icon shield = library.TagsFor("Fantasy/Swordman Skill", 6);
            shield.ownUse = true;
            shield.use = IconUse.Buff;
            shield.keywords.Add("guard");
            var found = library.Suggest(IconLibrary.RequestFor(Skill("AzureWall", WeaponClassId.Monoblade, ClassSkillSlot.Guard, false)), 1);
            Assert.AreEqual("Fantasy/Swordman Skill", found[0].set.id);
            Assert.AreEqual(6, found[0].number);
            // ... and is no longer offered to the class's attacks.
            foreach (var each in library.Suggest(IconLibrary.RequestFor(Skill("FuryStrike", WeaponClassId.Monoblade, ClassSkillSlot.S1, true)), 0))
                Assert.IsFalse(each.set.id == "Fantasy/Swordman Skill" && each.number == 6);
        }

        [Test]
        public void TheKindOfMove_PicksAmongAClasssIcons()
        {
            library.TagsFor("Fantasy/Swordman Skill", 4).keywords.Add("sweep");
            library.TagsFor("Fantasy/Swordman Skill", 2).keywords.Add("triple");
            var sweep = library.Suggest(IconLibrary.RequestFor(Skill("S1", WeaponClassId.Monoblade, ClassSkillSlot.S1, true)), 1);
            Assert.AreEqual(4, sweep[0].number, "S1 is the sweep");
            var triple = library.Suggest(IconLibrary.RequestFor(Skill("S3", WeaponClassId.Monoblade, ClassSkillSlot.S3, true)), 1);
            Assert.AreEqual(2, triple[0].number, "S3 is the three-hit");
        }

        [Test]
        public void AnIconAnotherSkillHas_IsNeverOffered()
        {
            Assert.IsTrue(library.Assign(IconTargetKind.Skill, "Other", "Fantasy/Swordman Skill", 1, true, out _));
            var found = library.Suggest(IconLibrary.RequestFor(Skill("FuryStrike", WeaponClassId.Monoblade, ClassSkillSlot.S1, true)), 0);
            foreach (var each in found) Assert.IsFalse(each.set.id == "Fantasy/Swordman Skill" && each.number == 1);
            Assert.AreEqual(11, found.Count);
        }

        [Test]
        public void GivingIconsFromTheTags_ToEverySkillOfTwoClasses_NeverRepeatsOne()
        {
            var skills = new List<SkillDefinitionSO>();
            foreach (WeaponClassId weaponClass in new[] { WeaponClassId.Monoblade, WeaponClassId.Longaxe })
                for (int slot = 1; slot <= 5; slot++)
                    skills.Add(Skill(weaponClass + "_S" + slot, weaponClass, (ClassSkillSlot)slot, true));
            foreach (SkillDefinitionSO skill in skills)
            {
                var best = library.Suggest(IconLibrary.RequestFor(skill), 1);
                Assert.AreEqual(1, best.Count, skill.skillId);
                Assert.IsTrue(library.Assign(IconTargetKind.Skill, skill.skillId, best[0].set.id, best[0].number, false, out string refusal), refusal);
            }
            Assert.AreEqual(10, library.assignments.Count);
            Assert.AreEqual(0, library.Duplicates().Count);
            foreach (IconLibrary.Assignment each in library.assignments)
                StringAssert.StartsWith(each.target.StartsWith("Monoblade") ? "Fantasy/Swordman" : "Fantasy/Berserker", each.set, each.target);
        }

        [Test]
        public void AnItem_IsOfferedItemIcons()
        {
            var found = library.Suggest(IconLibrary.RequestForItem("Alumite", "Alloy. Tempers a piece of gear by 3% on a success."), 0);
            Assert.AreEqual(6, found.Count);
            foreach (var each in found) Assert.AreEqual("Cyberpunk/Minerals", each.set.id);
        }

        [Test]
        public void SetNames_ComeFromThePacksFolderNames()
        {
            Assert.AreEqual("Swordman Skill", IconPacks.SetName("Swordman Skill Icons"));
            Assert.AreEqual("Resource 2", IconPacks.SetName("Resource Icons 2"));
            Assert.AreEqual("Gloves and Cloak", IconPacks.SetName("Gloves and Cloak Icons"));
            Assert.AreEqual("Demon Skill 1", IconPacks.SetName("Demon Skill Icons 1"));
        }
    }
}
