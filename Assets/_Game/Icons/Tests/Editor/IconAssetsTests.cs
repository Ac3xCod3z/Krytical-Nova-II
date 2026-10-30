using System.Collections.Generic;
using System.IO;
using KryticalNova.EditorTools;
using KryticalNova.Items;
using KryticalNova.Progression;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.Tests
{
    /// <summary>
    /// The icons as they stand in the project: every skill and every item without a model has an icon of its own
    /// from the library and shows it, no two skills look alike, and no item is left blank.
    /// </summary>
    public sealed class IconAssetsTests
    {
        private IconLibrary library;
        private IconTreatmentSettings settings;

        [OneTimeSetUp]
        public void Load()
        {
            library = AssetDatabase.LoadAssetAtPath<IconLibrary>(IconLibrary.AssetPath);
            settings = AssetDatabase.LoadAssetAtPath<IconTreatmentSettings>(IconTreatmentSettings.AssetPath);
            Assert.NotNull(library, IconLibrary.AssetPath);
            Assert.NotNull(settings, IconTreatmentSettings.AssetPath);
        }

        [Test]
        public void TheLibrary_ListsThePacksSets()
        {
            Assert.Greater(library.sets.Count, 100, "both packs' sets are listed (Scan Packs)");
            foreach (IconLibrary.Set set in library.sets)
            {
                Assert.IsFalse(string.IsNullOrEmpty(set.id));
                Assert.Greater(set.count, 0, set.id);
            }
        }

        [Test]
        public void EverySkillAndEveryItemWithoutAModel_HasAnIconOfItsOwn_AndShowsIt()
        {
            List<string> problems = IconPacks.Check(library);
            Assert.AreEqual(0, problems.Count, string.Join("\n", problems));
        }

        [Test]
        public void NoTwoSkills_ShowTheSameIcon()
        {
            var shown = new Dictionary<Sprite, string>();
            foreach (IconTarget skill in IconTargets.Skills())
            {
                Sprite icon = skill.Icon;
                Assert.NotNull(icon, skill.name + " has no icon");
                Assert.IsFalse(shown.TryGetValue(icon, out string other), $"{skill.name} and {other} show the same icon");
                shown[icon] = skill.name;
            }
            Assert.AreEqual(WeaponClasses.Count * WeaponClasses.SlotCount + 12, shown.Count, "every class skill, eight shared, Run, Jump, Wallstride and Waterstride");
        }

        [Test]
        public void NoItem_IsBlank()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:ItemDefinition", new[] { IconTargets.ItemRoot }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
                if (item != null) Assert.NotNull(item.icon, path + " has no icon");
            }
        }

        [Test]
        public void EveryRingAndAmuletOfTheLadder_WearsItsLooksLibraryIcon()
        {
            var looks = new HashSet<Sprite>();
            foreach (IconTarget item in IconTargets.Items())
                if (item.asset is ItemDefinition look && (look.equipSlot == EquipSlot.Ring || look.equipSlot == EquipSlot.Amulet)) looks.Add(look.icon);
            Assert.Greater(looks.Count, 0, "the jewellery looks take their icons from the library");
            // a Tripo set's ring and amulet wear the set's own painted icon (owner-approved art), not a library look's
            var painted = new HashSet<string>();
            foreach (string file in Directory.GetFiles("Assets/_Game/Icons/Art/Generated", "*.png", SearchOption.AllDirectories))
            {
                string name = Path.GetFileNameWithoutExtension(file);
                painted.Add(name.Substring(0, name.LastIndexOf('_')));
            }
            int worn = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:ItemDefinition", new[] { IconTargets.ItemRoot }))
            {
                var item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (item == null || !item.IsLadderGear || (item.equipSlot != EquipSlot.Ring && item.equipSlot != EquipSlot.Amulet)) continue;
                if (item.icon != null && painted.Contains(item.icon.name)) continue;
                Assert.IsTrue(looks.Contains(item.icon), item.name + " does not wear a jewellery look's icon");
                worn++;
            }
            Assert.Greater(worn, 0);
        }

        [Test]
        public void BakedIcons_AreTheSizeTheSettingsSay_AndSprites()
        {
            foreach (IconLibrary.Assignment assignment in library.assignments)
            {
                string path = IconBaker.OutputPath(assignment.kind, assignment.target);
                Assert.IsTrue(File.Exists(path), path + " is not baked");
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                Assert.NotNull(importer, path);
                Assert.AreEqual(TextureImporterType.Sprite, importer.textureType, path);
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                Assert.AreEqual(settings.size, texture.width, path);
                Assert.AreEqual(settings.size, texture.height, path);
            }
        }

        [Test]
        public void TheIconPacks_AreOnlyRead()
        {
            // The game shows treated copies in its own folders: nothing it shows points into a pack.
            foreach (IconTarget target in IconTargets.Skills())
                StringAssert.StartsWith(IconBaker.SkillIconFolder + "/", AssetDatabase.GetAssetPath(target.Icon), target.name);
            foreach (IconTarget target in IconTargets.Items())
                StringAssert.StartsWith(IconBaker.ItemIconFolder + "/", AssetDatabase.GetAssetPath(target.Icon), target.name);
        }
    }
}
