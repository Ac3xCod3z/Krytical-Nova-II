using KryticalNova.CharacterCreation;
using KryticalNova.Social;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.Tests
{
    /// <summary>
    /// Build 17 item 15.3 (owner, 13 Nov 2026): from the saddle only upper-body emotes (waves, cheers, points, salutes, beckons,
    /// laughs, arm taunts); no dances, sits, flips, stances or anything that needs the legs.
    /// </summary>
    public sealed class MountedEmoteTests
    {
        private const string SettingsPath = "Assets/_Game/Gameplay/Social/Data/Assets/EmoteWheelSettings.asset";
        private EmoteWheelSettings defaults;

        [SetUp]
        public void SetUp() => defaults = ScriptableObject.CreateInstance<EmoteWheelSettings>();

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(defaults);

        [TestCase("E/Greet/Wave")]
        [TestCase("E/Greet/Wave Over")]
        [TestCase("E/Greet/Salute")]
        [TestCase("E/Greet/Beckon Finger")]
        [TestCase("E/Happy/Fist Pump")]
        [TestCase("E/Celebrate/Clapping Polite")]
        [TestCase("E/Point Hand/Index Forward")]
        [TestCase("E/Taunt/Laugh And Point")]
        [TestCase("E/Head Nod/Small")]
        [TestCase("E/Sad/Shrug")]
        public void UpperBodyEmotesAreAllowed(string key) => Assert.IsTrue(EmoteCatalog.AllowedMounted(defaults, key), key);

        [TestCase("E/Dance/Twist")]
        [TestCase("E/Sporty/Backflip")]
        [TestCase("E/Greet/Bow")]
        [TestCase("E/Greet/Salute Stomp")]
        [TestCase("E/Angry/Tantrum Stomp")]
        [TestCase("E/Stretch/Squat")]
        [TestCase("E/Pray/Kneeling")]
        [TestCase("E/Sad/Melodramatic Faint")]
        [TestCase("S/Lean/Back Base")]
        [TestCase("S/Wave/Large")]
        [TestCase("S/Use Phone/One Hand")]
        [TestCase("")]
        [TestCase("E/Wave")]
        public void LegsWholeBodyAndStancesAreRefused(string key) => Assert.IsFalse(EmoteCatalog.AllowedMounted(defaults, key), key);

        [Test]
        public void TheProjectAssetHoldsTheOwnersList()
        {
            var asset = AssetDatabase.LoadAssetAtPath<EmoteWheelSettings>(SettingsPath);
            Assert.IsNotNull(asset);
            CollectionAssert.AreEqual(EmoteWheelSettings.DefaultMountedAllowed(), asset.mountedAllowed);
            CollectionAssert.AreEqual(EmoteWheelSettings.DefaultMountedRefused(), asset.mountedRefused);
        }

        [Test]
        public void EveryListedEntryNamesARealGroupOrMotion()
        {
            var library = AssetDatabase.LoadAssetAtPath<CharacterExpressionLibrary>(
                "Assets/_Game/Gameplay/CharacterCreation/Data/Assets/CharacterExpressionLibrary.asset");
            Assert.IsNotNull(library);
            foreach (string entry in EmoteWheelSettings.DefaultMountedAllowed()) Assert.IsTrue(Names(library, entry), entry);
            foreach (string entry in EmoteWheelSettings.DefaultMountedRefused()) Assert.IsTrue(Names(library, entry), entry);
        }

        private static bool Names(CharacterExpressionLibrary library, string entry)
        {
            int slash = entry.IndexOf('/');
            string group = slash < 0 ? entry : entry.Substring(0, slash), name = slash < 0 ? null : entry.Substring(slash + 1);
            foreach (CharacterExpressionLibrary.Motion motion in library.Emotes)
                if (motion.group == group && (name == null || motion.name == name)) return true;
            return false;
        }
    }
}
