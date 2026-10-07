using System.Collections.Generic;
using KryticalNova.CharacterCreation;
using KryticalNova.Social;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.Tests
{
    /// <summary>The emote wheel's contents, read from the project's own expression library with the default categories.</summary>
    public sealed class EmoteCatalogTests
    {
        private const string LibraryPath = "Assets/_Game/Gameplay/CharacterCreation/Data/Assets/CharacterExpressionLibrary.asset";
        private EmoteWheelSettings settings;

        [SetUp]
        public void SetUp()
        {
            settings = ScriptableObject.CreateInstance<EmoteWheelSettings>();
            settings.library = AssetDatabase.LoadAssetAtPath<CharacterExpressionLibrary>(LibraryPath);
            Assert.IsNotNull(settings.library, "the expression library is baked");
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(settings);

        [Test]
        public void AKeyNamesAMotionByKindGroupAndName()
        {
            Assert.AreEqual("E/Greet/Bow", EmoteCatalog.Key(false, "Greet", "Bow"));
            Assert.AreEqual("S/Lean/Back Base", EmoteCatalog.Key(true, "Lean", "Back Base"));
        }

        [Test]
        public void AOneWordNameIsShownWithItsGroup()
        {
            Assert.AreEqual("Head Nod Large", EmoteCatalog.Label("Head Nod", "Large"));
            Assert.AreEqual("Blow Kiss", EmoteCatalog.Label("Affection", "Blow Kiss"), "a name that says enough stands alone");
            Assert.AreEqual("Yawn", EmoteCatalog.Label("Yawn", "Yawn"), "never 'Yawn Yawn'");
            Assert.AreEqual("Bow", EmoteCatalog.Label("Greet", "Bow", "Greet"), "the category already says it");
            Assert.AreEqual("Wave Double", EmoteCatalog.Label("Wave", "Double", "Greet"), "another group of the same category still needs its name");
        }

        [Test]
        public void PagesAreCountedUp_AndThereIsAlwaysOne()
        {
            Assert.AreEqual(1, EmoteCatalog.PageCount(0, 8));
            Assert.AreEqual(1, EmoteCatalog.PageCount(8, 8));
            Assert.AreEqual(2, EmoteCatalog.PageCount(9, 8));
            Assert.AreEqual(5, EmoteCatalog.PageCount(38, 8));
        }

        [Test]
        public void EveryCategoryHasSomethingForBothBodies_AndEveryKeyFindsItsMotion()
        {
            foreach (BodyType body in new[] { BodyType.Masculine, BodyType.Feminine })
            {
                var wheel = new List<List<EmoteEntry>>();
                EmoteCatalog.Collect(settings, body, wheel);
                Assert.AreEqual(settings.categories.Length, wheel.Count);
                var seen = new HashSet<string>();
                for (int c = 0; c < wheel.Count; c++)
                {
                    Assert.Greater(wheel[c].Count, 0, $"{settings.categories[c].name} is empty for a {body} body");
                    foreach (EmoteEntry entry in wheel[c])
                    {
                        Assert.IsTrue(seen.Add(entry.Key), $"{entry.Key} is on the wheel twice");
                        Assert.LessOrEqual(System.Text.Encoding.UTF8.GetByteCount(entry.Key), 61, $"{entry.Key} fits the network field");
                        Assert.IsTrue(EmoteCatalog.TryResolve(settings.library, entry.Key, out var motion, out bool stance), entry.Key);
                        Assert.AreEqual(entry.Stance, stance, entry.Key);
                        Assert.IsNotNull(motion.For(body), $"{entry.Key} was performed for a {body} body");
                    }
                }
            }
        }

        [Test]
        public void EveryGroupNamedByACategoryExistsInTheLibrary()
        {
            var groups = new HashSet<string>();
            foreach (var motion in settings.library.Emotes) groups.Add(motion.group);
            foreach (var motion in settings.library.Idles) groups.Add(motion.group);
            foreach (EmoteCategory category in settings.categories)
                foreach (string group in category.groups)
                    Assert.IsTrue(groups.Contains(group), $"{category.name} lists '{group}', which the library does not have");
        }

        [Test]
        public void AKeyNoMotionHasIsRefused()
        {
            Assert.IsFalse(EmoteCatalog.TryResolve(settings.library, "E/Greet/No Such Emote", out _, out _));
            Assert.IsFalse(EmoteCatalog.TryResolve(settings.library, "X/Greet/Bow", out _, out _), "an unknown kind");
            Assert.IsFalse(EmoteCatalog.TryResolve(settings.library, "Bow", out _, out _), "not a key at all");
            Assert.IsFalse(EmoteCatalog.TryResolve(settings.library, string.Empty, out _, out _));
            Assert.IsFalse(EmoteCatalog.IsOffered(settings, "Base"), "the default stance is not an emote");
            Assert.IsTrue(EmoteCatalog.IsOffered(settings, "Taunt"));
        }
    }
}
