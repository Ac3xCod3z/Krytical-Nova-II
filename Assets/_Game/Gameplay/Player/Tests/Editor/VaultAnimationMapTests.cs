using System.Collections.Generic;
using KryticalNova.CharacterCreation;
using KryticalNova.Factions;
using KryticalNova.Player;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Tests
{
    /// <summary>
    /// Which vault animation a character uses: its faction's and body's own for the obstacle's height, and where
    /// there is none, the next set's for that same height, never one of another height.
    /// </summary>
    public sealed class VaultAnimationMapTests
    {
        private readonly List<Object> made = new List<Object>();
        private VaultAnimationMap map;
        private VaultAnimationSet defaults;

        [SetUp]
        public void Create()
        {
            defaults = Set("Default", "D");
            map = Make<VaultAnimationMap>("Map");
            map.defaultSet = defaults;
        }

        [TearDown]
        public void Destroy()
        {
            foreach (Object each in made) Object.DestroyImmediate(each);
            made.Clear();
        }

        private T Make<T>(string name) where T : ScriptableObject
        {
            var asset = ScriptableObject.CreateInstance<T>();
            asset.name = name;
            made.Add(asset);
            return asset;
        }

        private AnimationClip Clip(string name)
        {
            var clip = new AnimationClip { name = name };
            made.Add(clip);
            return clip;
        }

        /// <summary>A set whose clips are named prefix + the height's letter (L, M, T), for the heights asked for (all three when none are).</summary>
        private VaultAnimationSet Set(string name, string prefix, params VaultHeight[] heights)
        {
            var set = Make<VaultAnimationSet>(name);
            if (heights.Length == 0) heights = new[] { VaultHeight.Low, VaultHeight.Middle, VaultHeight.Tall };
            foreach (VaultHeight height in heights)
                set.Set(height, new VaultStyle { clip = Clip(prefix + height.ToString()[0]), poseClearance = 1f + (int)height });
            return set;
        }

        private void Entries(params VaultAnimationMap.Entry[] entries) => map.entries = entries;

        private string Resolve(FactionId faction, BodyType body, VaultHeight height)
        {
            Assert.IsTrue(map.TryResolve(faction, body, height, out VaultStyle style));
            return style.clip.name;
        }

        private static readonly VaultHeight[] Heights = { VaultHeight.Low, VaultHeight.Middle, VaultHeight.Tall };

        [Test]
        public void WithNoEntryEveryoneUsesTheDefaultSet()
        {
            foreach (FactionId faction in new[] { FactionId.None, FactionId.Hegemony, FactionId.Null, FactionId.Covenant })
                foreach (BodyType body in new[] { BodyType.Masculine, BodyType.Feminine })
                    foreach (VaultHeight height in Heights)
                        Assert.AreEqual("D" + height.ToString()[0], Resolve(faction, body, height));
        }

        [Test]
        public void AFactionsSetIsUsedByBothItsBodies_AndNobodyElse()
        {
            Entries(new VaultAnimationMap.Entry(FactionId.Null, VaultBody.Any, Set("Null", "N")));
            foreach (VaultHeight height in Heights)
            {
                char letter = height.ToString()[0];
                Assert.AreEqual("N" + letter, Resolve(FactionId.Null, BodyType.Masculine, height));
                Assert.AreEqual("N" + letter, Resolve(FactionId.Null, BodyType.Feminine, height));
                Assert.AreEqual("D" + letter, Resolve(FactionId.Hegemony, BodyType.Masculine, height));
                Assert.AreEqual("D" + letter, Resolve(FactionId.None, BodyType.Feminine, height), "a player with no faction yet");
            }
        }

        [Test]
        public void ABodysOwnSetComesBeforeItsFactions()
        {
            Entries(new VaultAnimationMap.Entry(FactionId.Covenant, VaultBody.Any, Set("Covenant", "C")),
                new VaultAnimationMap.Entry(FactionId.Covenant, VaultBody.Feminine, Set("Covenant F", "F")));
            foreach (VaultHeight height in Heights)
            {
                char letter = height.ToString()[0];
                Assert.AreEqual("F" + letter, Resolve(FactionId.Covenant, BodyType.Feminine, height));
                Assert.AreEqual("C" + letter, Resolve(FactionId.Covenant, BodyType.Masculine, height));
            }
        }

        [Test]
        public void AMissingHeightFallsBackToTheSameHeight_NeverAnother()
        {
            // The body's set has only a low vault, the faction's only low and tall: middle comes from the default.
            Entries(new VaultAnimationMap.Entry(FactionId.Hegemony, VaultBody.Any, Set("Hegemony", "H", VaultHeight.Low, VaultHeight.Tall)),
                new VaultAnimationMap.Entry(FactionId.Hegemony, VaultBody.Masculine, Set("Hegemony M", "M", VaultHeight.Low)));

            Assert.AreEqual("ML", Resolve(FactionId.Hegemony, BodyType.Masculine, VaultHeight.Low));
            Assert.AreEqual("DM", Resolve(FactionId.Hegemony, BodyType.Masculine, VaultHeight.Middle), "the default's middle, not the faction's low or tall");
            Assert.AreEqual("HT", Resolve(FactionId.Hegemony, BodyType.Masculine, VaultHeight.Tall), "the faction's tall, before the default's");
            Assert.AreEqual("HL", Resolve(FactionId.Hegemony, BodyType.Feminine, VaultHeight.Low));
            Assert.AreEqual("DM", Resolve(FactionId.Hegemony, BodyType.Feminine, VaultHeight.Middle));

            // Whatever is mapped, the style that comes back is always one made for the height asked for.
            foreach (BodyType body in new[] { BodyType.Masculine, BodyType.Feminine })
                foreach (VaultHeight height in Heights)
                {
                    Assert.IsTrue(map.TryResolve(FactionId.Hegemony, body, height, out VaultStyle style, out VaultAnimationSet from));
                    Assert.AreEqual(height.ToString()[0], style.clip.name[1]);
                    Assert.AreEqual(1f + (int)height, style.poseClearance, "the numbers travel with the clip");
                    Assert.IsTrue(from.Has(height));
                }
        }

        [Test]
        public void WithoutACompleteDefaultTheMapSaysSo()
        {
            Assert.IsTrue(map.IsUsable);
            map.defaultSet = Set("Half", "X", VaultHeight.Low);
            Assert.IsFalse(map.IsUsable);
            Assert.IsFalse(map.TryResolve(FactionId.Null, BodyType.Masculine, VaultHeight.Tall, out VaultStyle none));
            Assert.IsNull(none.clip);
            map.defaultSet = null;
            Assert.IsFalse(map.IsUsable);
            Assert.IsFalse(map.TryResolve(FactionId.Null, BodyType.Masculine, VaultHeight.Low, out _));
        }

        [Test]
        public void AnObstaclesHeightSortsItIntoItsClass()
        {
            var settings = Make<TraversalSettings>("Traversal");
            Assert.AreEqual(VaultHeight.Low, settings.HeightOf(0.18f));
            Assert.AreEqual(VaultHeight.Low, settings.HeightOf(settings.middleFromHeight - 0.01f));
            Assert.AreEqual(VaultHeight.Middle, settings.HeightOf(settings.middleFromHeight));
            Assert.AreEqual(VaultHeight.Middle, settings.HeightOf(settings.tallFromHeight - 0.01f));
            Assert.AreEqual(VaultHeight.Tall, settings.HeightOf(settings.tallFromHeight));
            Assert.AreEqual(VaultHeight.Tall, settings.HeightOf(1.78f));

            settings.middleFromHeight = 0.5f;
            settings.tallFromHeight = 1f;
            Assert.AreEqual(VaultHeight.Middle, settings.HeightOf(0.6f), "the thresholds are the asset's own");
            Assert.AreEqual(VaultHeight.Tall, settings.HeightOf(1.1f));
        }

        [Test]
        public void TheProjectsMap_GivesEveryFactionAVaultOfItsOwnAtEveryHeight()
        {
            var built = UnityEditor.AssetDatabase.LoadAssetAtPath<VaultAnimationMap>("Assets/_Game/Gameplay/Player/Data/Assets/VaultAnimationMap.asset");
            Assert.IsNotNull(built, "run Krytical Nova/Player/Build Vault Warp Asset");
            Assert.IsTrue(built.IsUsable, "the default set has all three heights");
            var seen = new HashSet<AnimationClip>();
            foreach (FactionId faction in new[] { FactionId.Hegemony, FactionId.Null, FactionId.Covenant })
                foreach (VaultHeight height in Heights)
                {
                    Assert.IsTrue(built.TryResolve(faction, BodyType.Masculine, height, out VaultStyle style, out VaultAnimationSet from));
                    Assert.IsTrue(built.TryResolve(faction, BodyType.Feminine, height, out VaultStyle other));
                    Assert.AreSame(style.clip, other.clip, "the pack has one performance for both bodies");
                    Assert.AreNotSame(built.defaultSet, from, $"{faction} has its own {height} vault");
                    Assert.IsTrue(seen.Add(style.clip), $"{faction} {height}: no vault is used twice");
                    Assert.Less(style.window.x, style.window.y);
                    Assert.LessOrEqual(style.window.y, style.clip.length + 0.01f, "the window lies inside the clip");
                    Assert.Greater(style.poseClearance, 0f);
                    Assert.Less(style.airTime.x, style.airTime.y);
                }
        }
    }
}
