using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using KryticalNova.CharacterCreation;
using KryticalNova.Factions;
using KryticalNova.Networking.Entities;
using KryticalNova.Progression;

namespace KryticalNova.Persistence.Entities.Tests
{
    /// <summary>
    /// M3a-2b step 5 (pure): the look as text for the go-in-game request and the look of observers. The largest legal recipe fits
    /// the request (the step-4 leftover), every field travels, and anything malformed is refused.
    /// </summary>
    public class RecipeTextTests
    {
        static CharacterRecipe Largest()
        {
            var parts = new ushort[CharacterRecipe.MaxParts];
            for (int i = 0; i < parts.Length; i++) parts[i] = (ushort)(ushort.MaxValue - i);
            var colors = new ColorOverride[CharacterRecipe.MaxColors];
            for (int i = 0; i < colors.Length; i++) colors[i] = new ColorOverride(byte.MaxValue, new Color32(255, (byte)(200 + i), 1, 128));
            return new CharacterRecipe
            {
                version = CharacterRecipe.CurrentVersion, faction = (FactionId)2, lineage = 255, bodyType = (BodyType)1,
                build = sbyte.MinValue, muscle = sbyte.MaxValue, palette = ushort.MaxValue, skinTone = 255, scars = 100, eyeliner = 100,
                grime = 100, weaponClass = (WeaponClassId)7, bust = 100, butt = 100, defined = 100, height = 100, eyeGlow = true,
                parts = parts, colors = colors,
            };
        }

        [Test]
        public void TheLargestRecipe_RoundTrips_AndFitsTheGoInGameRequest()
        {
            CharacterRecipe recipe = Largest();
            string text = RecipeText.Encode(recipe);
            Debug.Log("[M3A2B-LOOK] largest recipe text: " + text.Length + " characters (request carries " + GoInGamePages.MaxChars + ")");
            var pages = new List<string>();
            Assert.IsTrue(GoInGamePages.TrySplit(text, pages), "ASCII and within " + GoInGamePages.MaxChars + " characters");
            Assert.LessOrEqual(pages.Count, GoInGamePages.MaxPages);
            Assert.IsTrue(RecipeText.TryDecode(text, out CharacterRecipe back));
            Assert.IsTrue(recipe.Equals(back));
            Assert.AreEqual(JsonUtility.ToJson(recipe), JsonUtility.ToJson(back), "every serialized field is the same");
        }

        [Test]
        public void AnEmptyAndAnOldRecipe_RoundTrip()
        {
            var plain = new CharacterRecipe { version = 1, lineage = 3 };
            Assert.IsTrue(RecipeText.TryDecode(RecipeText.Encode(plain), out CharacterRecipe back));
            Assert.IsTrue(plain.Equals(back), "null lists read back as empty ones (Equals counts them the same)");
            Assert.AreEqual(0, back.PartCount);
            Assert.AreEqual(0, back.ColorCount);
        }

        [Test]
        public void EveryRecipeField_IsCarried()
        {
            string[] fields = typeof(CharacterRecipe).GetFields(BindingFlags.Public | BindingFlags.Instance).Select(f => f.Name).OrderBy(n => n).ToArray();
            CollectionAssert.AreEqual(RecipeText.Fields.OrderBy(n => n).ToArray(), fields,
                "CharacterRecipe gained or lost a field: carry it in RecipeText (and bump its prefix)");
        }

        [TestCase((string)null)]
        [TestCase("")]
        [TestCase("R2|0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0||")]
        [TestCase("R1|0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0||")]
        [TestCase("R1|256,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0||")]
        [TestCase("R1|0,0,0,0,-129,0,0,0,0,0,0,0,0,0,0,0,0||")]
        [TestCase("R1|0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,2||")]
        [TestCase("R1|0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0|70000|")]
        [TestCase("R1|0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0||1.FFFFFF")]
        [TestCase("R1|0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0||1.GG000000")]
        [TestCase("R1|0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0|")]
        public void Malformed_IsRefused(string text)
        {
            Assert.IsFalse(RecipeText.TryDecode(text, out _));
        }

        [Test]
        public void TooManyParts_IsRefused()
        {
            var parts = string.Join(",", Enumerable.Repeat("1", CharacterRecipe.MaxParts + 1));
            Assert.IsFalse(RecipeText.TryDecode("R1|0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0|" + parts + "|", out _));
        }

        [Test]
        public void TheLookOfObservers_CarriesTheName_AsAscii()
        {
            CharacterRecipe recipe = Largest();
            string text = RecipeText.EncodeLook("Zoë Vex|1", recipe);
            Assert.IsTrue(text.All(ch => ch < 128), "pages need ASCII");
            Assert.IsTrue(RecipeText.TryDecodeLook(text, out string name, out CharacterRecipe back));
            Assert.AreEqual("Zoë Vex|1", name);
            Assert.IsTrue(recipe.Equals(back));
            Assert.IsFalse(RecipeText.TryDecodeLook("no line break", out _, out _));
        }
    }
}
