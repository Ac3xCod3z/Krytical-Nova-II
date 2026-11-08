using System;
using KryticalNova.CharacterCreation;
using KryticalNova.Factions;
using NUnit.Framework;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace KryticalNova.Tests
{
    /// <summary>Glowing eyes and heterochromia (24 Oct 2026): saved, sent, owned by the Reshaper, painted in the right order.</summary>
    public sealed class EyeOptionTests
    {
        private static readonly Color32 Brown = new Color32(107, 74, 39, 255), Blue = new Color32(63, 127, 208, 255);

        private static CharacterRecipe Look() => new CharacterRecipe
        {
            version = CharacterRecipe.CurrentVersion, faction = FactionId.Hegemony, lineage = 1, bodyType = BodyType.Masculine,
            palette = 3, parts = new ushort[] { 101 }, colors = new[] { new ColorOverride(CharacterColorChannels.Eyes, Brown) },
            height = CharacterRecipe.DefaultHeight
        };

        [Serializable]
        private sealed class Holder
        {
            public CharacterRecipe recipe;
        }

        [Test]
        public void ALookFromBeforeTheEyeOptionsHasPlainEyes()
        {
            const string json = "{\"recipe\":{\"version\":2,\"faction\":1,\"lineage\":1,\"bodyType\":0,\"parts\":[101],\"palette\":3}}";
            CharacterRecipe old = JsonUtility.FromJson<Holder>(json).recipe;
            Assert.That(old.eyeGlow, Is.False);
            Assert.That(RecipeEdit.HasHeterochromia(old), Is.False);
        }

        [Test]
        public void HeterochromiaStartsAsTheEyeColourAndGoesCleanly()
        {
            CharacterRecipe look = Look();
            RecipeEdit.SetHeterochromia(ref look, true, Blue);
            Assert.That(RecipeEdit.TryGetColor(look, CharacterColorChannels.EyeRight, out var right) && right.Equals(Brown));
            RecipeEdit.SetColor(ref look, CharacterColorChannels.EyeRight, Blue);
            RecipeEdit.SetHeterochromia(ref look, false, Blue);
            Assert.That(RecipeEdit.HasHeterochromia(look), Is.False);
            Assert.That(RecipeEdit.TryGetColor(look, CharacterColorChannels.Eyes, out var eyes) && eyes.Equals(Brown));
        }

        [Test]
        public void TheEyeOptionsTravelOverTheNetwork()
        {
            CharacterRecipe look = Look();
            look.eyeGlow = true;
            RecipeEdit.SetHeterochromia(ref look, true, Blue);
            RecipeEdit.SetColor(ref look, CharacterColorChannels.EyeRight, Blue);
            CharacterNetState sent = CharacterNetState.From("Ayla", look);
            using var writer = new FastBufferWriter(1024, Allocator.Temp);
            writer.WriteNetworkSerializable(sent);
            using var reader = new FastBufferReader(writer, Allocator.Temp);
            reader.ReadNetworkSerializable(out CharacterNetState received);
            Assert.That(received.Equals(sent));
            CharacterRecipe back = received.ToRecipe();
            Assert.That(back.eyeGlow, Is.True);
            Assert.That(back.Equals(look));
        }

        [Test]
        public void GlowingEyesChangeTheLookAndBelongToTheReshaper()
        {
            CharacterRecipe plain = Look(), glowing = Look();
            glowing.eyeGlow = true;
            Assert.That(plain.Equals(glowing), Is.False);
            Assert.That(RestyleRules.Same(plain, glowing), Is.False);
            Assert.That(RestyleRules.Merge(plain, glowing, RestyleService.Reshaper, null).eyeGlow, Is.True);
            Assert.That(RestyleRules.Merge(plain, glowing, RestyleService.Stylist, null).eyeGlow, Is.False);
        }

        [Test]
        public void TheRightEyeIsPaintedAfterTheEyesWhateverTheOrder()
        {
            var database = UnityEditor.AssetDatabase.LoadAssetAtPath<CharacterCreationDatabase>(DatabasePath);
            Assert.That(database, Is.Not.Null, "Bake the character data first.");
            Assert.That(database.TryGetChannel(CharacterColorChannels.EyeRight, out _), "The Right Eye channel is baked.");
            CharacterRecipe look = Look();
            // the right eye picked first, the eye colour after it: the right eye must still keep its own colour
            look.colors = new[] { new ColorOverride(CharacterColorChannels.EyeRight, Blue), new ColorOverride(CharacterColorChannels.Eyes, Brown) };
            var pixels = new Color32[CharacterColorMap.PixelCount];
            CharacterColorMap.Fill(look, database, pixels);
            Assert.That(Cell(pixels, 5, 2), Is.EqualTo(Blue), "Eye Color Right");
            Assert.That(Cell(pixels, 4, 2), Is.EqualTo(Brown), "Eye Color Left");
        }

        [Test]
        public void GlowingEyesLightOnlyTheIrises()
        {
            var pixels = new Color32[CharacterColorMap.PixelCount];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(200, 200, 200, 255);
            var emission = new Color[CharacterColorMap.PixelCount];
            CharacterColorMap.FillEyeGlow(pixels, emission, 4f);
            int lit = 0;
            foreach (Color c in emission) if (c.r > 0f) lit++;
            Assert.That(lit, Is.EqualTo(12), "three cells of 2x2 texels: the two irises and the whole eye of a skeleton or robot");
            Assert.That(emission[2 * 2 * CharacterColorMap.Size + 4 * 2].r, Is.GreaterThan(1f), "brighter than white, for bloom");
            CharacterColorMap.FillEyeGlow(pixels, emission, 0f);
            foreach (Color c in emission) Assert.That(c.r, Is.EqualTo(0f));
        }

        private const string DatabasePath = "Assets/_Game/Gameplay/CharacterCreation/Data/Assets/CharacterCreationDatabase.asset";

        private static Color32 Cell(Color32[] pixels, int u, int v) => pixels[v * 2 * CharacterColorMap.Size + u * 2];
    }
}
