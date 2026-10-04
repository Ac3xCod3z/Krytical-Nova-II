using System.Collections.Generic;
using KryticalNova.CharacterCreation;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.Tests
{
    /// <summary>The baked stances and emotes the character creator previews (Synty's Idles and Emotes &amp; Taunts packs).</summary>
    public sealed class ExpressionLibraryTests
    {
        private const string LibraryPath = "Assets/_Game/Gameplay/CharacterCreation/Data/Assets/CharacterExpressionLibrary.asset";

        private static CharacterExpressionLibrary Library()
        {
            var library = AssetDatabase.LoadAssetAtPath<CharacterExpressionLibrary>(LibraryPath);
            Assert.IsNotNull(library, "Bake it: Krytical Nova > Characters > 5. Bake Expressions.");
            return library;
        }

        [Test]
        public void TheLibraryHoldsThePacksStancesAndEmotes()
        {
            var library = Library();
            Assert.GreaterOrEqual(library.Idles.Count, 20, "stances");
            Assert.GreaterOrEqual(library.Emotes.Count, 60, "emotes");
            Assert.AreEqual("Base", library.Idles[0].group, "the relaxed base stance comes first: it is the default");
        }

        [Test]
        public void EveryMotionIsPerformedForAtLeastOneBodyTypeAndNeverBorrowed()
        {
            var library = Library();
            foreach (var list in new[] { library.Idles, library.Emotes })
            {
                foreach (var motion in list)
                {
                    Assert.IsFalse(string.IsNullOrEmpty(motion.name), "a motion without a name");
                    Assert.IsFalse(string.IsNullOrEmpty(motion.group), $"{motion.name} has no group");
                    Assert.IsTrue(motion.Has(BodyType.Masculine) || motion.Has(BodyType.Feminine), $"{motion.group}/{motion.name} has no clip at all");
                    foreach (BodyType body in new[] { BodyType.Masculine, BodyType.Feminine })
                    {
                        AnimationClip clip = motion.For(body);
                        if (!motion.Has(body)) { Assert.IsNull(clip, $"{motion.name}: a body must not borrow the other one's performance"); continue; }
                        string file = System.IO.Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(clip));
                        StringAssert.EndsWith(body == BodyType.Feminine ? "_Femn" : "_Masc", file, $"{motion.group}/{motion.name}: wrong performance for a {body} body");
                    }
                }
            }
        }

        [Test]
        public void BothBodyTypesHaveAFullSetAndTheSameDefaultStance()
        {
            var library = Library();
            Assert.IsTrue(library.Idles[0].Has(BodyType.Masculine) && library.Idles[0].Has(BodyType.Feminine), "the default stance");
            foreach (BodyType body in new[] { BodyType.Masculine, BodyType.Feminine })
            {
                var groups = new List<string>();
                var members = new List<List<int>>();
                CharacterExpressionLibrary.Group(library.Idles, body, groups, members);
                int stances = 0;
                foreach (var group in members) stances += group.Count;
                Assert.GreaterOrEqual(stances, 20, $"stances for a {body} body");
                CharacterExpressionLibrary.Group(library.Emotes, body, groups, members);
                int emotes = 0;
                foreach (var group in members) emotes += group.Count;
                Assert.GreaterOrEqual(emotes, 60, $"emotes for a {body} body");
            }
        }

        [Test]
        public void NoRootMotionVariantsAndPropMotionsComeWithTheirProp()
        {
            var library = Library();
            foreach (var list in new[] { library.Idles, library.Emotes })
            {
                foreach (var motion in list)
                {
                    AnimationClip any = motion.Has(BodyType.Masculine) ? motion.For(BodyType.Masculine) : motion.For(BodyType.Feminine);
                    string file = System.IO.Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(any));
                    StringAssert.DoesNotContain("_RM", file, "a root-motion variant would walk the character off its stage");
                    bool needsProp = motion.group == "Drink" || motion.group == "Eat" || motion.group == "Use Phone" || file.Contains("CombBack");
                    if (needsProp) Assert.IsNotNull(motion.prop, $"{motion.group}/{motion.name} is performed with a prop and has none");
                    else Assert.IsNull(motion.prop, $"{motion.group}/{motion.name} should be empty-handed");
                }
            }
        }

        [Test]
        public void GroupsKeepTheirMotionsTogether()
        {
            var library = Library();
            var groups = new List<string>();
            var members = new List<List<int>>();
            int total = 0, expected = 0;
            foreach (var motion in library.Emotes) if (motion.Has(BodyType.Feminine)) expected++;
            CharacterExpressionLibrary.Group(library.Emotes, BodyType.Feminine, groups, members);
            foreach (var group in members) total += group.Count;
            Assert.AreEqual(expected, total);
            CollectionAssert.AllItemsAreUnique(groups);
        }
    }
}
