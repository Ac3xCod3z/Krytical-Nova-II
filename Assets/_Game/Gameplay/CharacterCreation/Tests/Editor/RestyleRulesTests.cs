using KryticalNova.CharacterCreation;
using KryticalNova.Factions;
using KryticalNova.Items;
using KryticalNova.Items.Visuals;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Tests
{
    public sealed class RestyleRulesTests
    {
        private static readonly Color32 Black = new Color32(10, 10, 10, 255), Red = new Color32(200, 30, 30, 255),
            Blue = new Color32(30, 60, 220, 255), Green = new Color32(30, 200, 60, 255);

        private static CharacterRecipe Look() => new CharacterRecipe
        {
            version = CharacterRecipe.CurrentVersion, faction = FactionId.Hegemony, lineage = 1, bodyType = BodyType.Masculine,
            build = 10, muscle = 20, palette = 3, skinTone = 2, scars = 30, eyeliner = 0, grime = 5,
            parts = new ushort[] { 101, 102 },
            colors = new[] { new ColorOverride(CharacterColorChannels.Hair, Black), new ColorOverride(CharacterColorChannels.Eyes, Blue) },
        };

        private static CharacterRecipe Wanted()
        {
            CharacterRecipe wanted = Look();
            wanted.faction = FactionId.Null;
            wanted.lineage = 9;
            wanted.bodyType = BodyType.Feminine;
            wanted.palette = 7;
            wanted.build = -60;
            wanted.muscle = 90;
            wanted.skinTone = 4;
            wanted.scars = 80;
            wanted.eyeliner = 40;
            wanted.grime = 0;
            wanted.colors = new[] { new ColorOverride(CharacterColorChannels.Hair, Red), new ColorOverride(CharacterColorChannels.Eyes, Green) };
            return wanted;
        }

        // The part catalog is an asset; these tests use no parts of it (a null catalog owns nothing), so the two
        // parts of the look are kept by every service and the fields each service owns are what is told apart.
        private static CharacterRecipe Merge(RestyleService service) => RestyleRules.Merge(Look(), Wanted(), service, null);

        [Test]
        public void TheStylist_ChangesHairColourAndNothingElse()
        {
            CharacterRecipe result = Merge(RestyleService.Stylist);
            Assert.IsTrue(RecipeEdit.TryGetColor(result, CharacterColorChannels.Hair, out Color32 hair));
            Assert.AreEqual(Red, hair);
            Assert.IsTrue(RecipeEdit.TryGetColor(result, CharacterColorChannels.Eyes, out Color32 eyes));
            Assert.AreEqual(Blue, eyes, "eye colour is the Reshaper's");
            Assert.AreEqual(10, result.build);
            Assert.AreEqual(2, result.skinTone);
            Assert.AreEqual(30, result.scars);
        }

        [Test]
        public void TheReshaper_ChangesTheFaceColoursSkinAndMarks()
        {
            CharacterRecipe result = Merge(RestyleService.Reshaper);
            Assert.IsTrue(RecipeEdit.TryGetColor(result, CharacterColorChannels.Eyes, out Color32 eyes));
            Assert.AreEqual(Green, eyes);
            Assert.IsTrue(RecipeEdit.TryGetColor(result, CharacterColorChannels.Hair, out Color32 hair));
            Assert.AreEqual(Black, hair, "hair colour is the Stylist's");
            Assert.AreEqual(4, result.skinTone);
            Assert.AreEqual(80, result.scars);
            Assert.AreEqual(40, result.eyeliner);
            Assert.AreEqual(0, result.grime);
            Assert.AreEqual(10, result.build, "the body is the Coach's");
        }

        [Test]
        public void TheCoach_ChangesTheBodyOnly()
        {
            CharacterRecipe result = Merge(RestyleService.Coach);
            Assert.AreEqual(-60, result.build);
            Assert.AreEqual(90, result.muscle);
            Assert.AreEqual(2, result.skinTone);
            Assert.IsTrue(RecipeEdit.TryGetColor(result, CharacterColorChannels.Hair, out Color32 hair));
            Assert.AreEqual(Black, hair);
        }

        [Test]
        public void NoService_ChangesWhoTheCharacterIs()
        {
            foreach (RestyleService service in new[] { RestyleService.Stylist, RestyleService.Reshaper, RestyleService.Coach })
            {
                CharacterRecipe result = Merge(service);
                Assert.AreEqual(FactionId.Hegemony, result.faction, service.ToString());
                Assert.AreEqual(1, result.lineage, service.ToString());
                Assert.AreEqual(BodyType.Masculine, result.bodyType, service.ToString());
                Assert.AreEqual(3, result.palette, service.ToString());
                CollectionAssert.AreEquivalent(new ushort[] { 101, 102 }, result.parts, service.ToString());
            }
        }

        private static CharacterRecipe Sliders(BodyType bodyType, byte bust, byte butt, byte defined, byte height)
        {
            CharacterRecipe look = Look();
            look.bodyType = bodyType;
            look.bust = bust;
            look.butt = butt;
            look.defined = defined;
            look.height = height;
            return look;
        }

        [Test]
        public void TheCoach_ChangesBustButtAndHeight_OnAFeminineBody()
        {
            CharacterRecipe saved = Sliders(BodyType.Feminine, 0, 0, 0, 50);
            CharacterRecipe result = RestyleRules.Merge(saved, Sliders(BodyType.Feminine, 70, 40, 90, 80), RestyleService.Coach, null);
            Assert.AreEqual(70, result.bust);
            Assert.AreEqual(40, result.butt);
            Assert.AreEqual(0, result.defined, "definition is worn by a masculine body only");
            Assert.AreEqual(80, result.Height);
        }

        [Test]
        public void TheCoach_ChangesDefinitionAndHeight_OnAMasculineBody()
        {
            CharacterRecipe saved = Sliders(BodyType.Masculine, 0, 0, 0, 50);
            CharacterRecipe result = RestyleRules.Merge(saved, Sliders(BodyType.Masculine, 70, 40, 90, 20), RestyleService.Coach, null);
            Assert.AreEqual(0, result.bust, "bust is worn by a feminine body only");
            Assert.AreEqual(0, result.butt);
            Assert.AreEqual(90, result.defined);
            Assert.AreEqual(20, result.Height);
        }

        [Test]
        public void TheCoach_GoesByTheSavedBodyType_NotTheClients()
        {
            CharacterRecipe saved = Sliders(BodyType.Masculine, 0, 0, 0, 50);
            CharacterRecipe result = RestyleRules.Merge(saved, Sliders(BodyType.Feminine, 70, 40, 0, 50), RestyleService.Coach, null);
            Assert.AreEqual(BodyType.Masculine, result.bodyType);
            Assert.AreEqual(0, result.bust);
            Assert.AreEqual(0, result.butt);
        }

        [Test]
        public void TheBodySliders_StayWithinTheirLimits()
        {
            CharacterRecipe result = RestyleRules.Merge(Sliders(BodyType.Feminine, 0, 0, 0, 50), Sliders(BodyType.Feminine, 255, 200, 0, 255), RestyleService.Coach, null);
            Assert.AreEqual(100, result.bust);
            Assert.AreEqual(100, result.butt);
            Assert.AreEqual(100, result.Height);
            Assert.LessOrEqual(result.height, 100, "the validator rejects a stored value over 100");
        }

        [Test]
        public void OnlyTheCoach_ChangesTheBodySliders()
        {
            CharacterRecipe saved = Sliders(BodyType.Feminine, 10, 20, 0, 50), wanted = Sliders(BodyType.Feminine, 90, 90, 0, 90);
            foreach (RestyleService service in new[] { RestyleService.Stylist, RestyleService.Reshaper })
            {
                CharacterRecipe result = RestyleRules.Merge(saved, wanted, service, null);
                Assert.AreEqual(10, result.bust, service.ToString());
                Assert.AreEqual(20, result.butt, service.ToString());
                Assert.AreEqual(50, result.Height, service.ToString());
            }
        }

        [Test]
        public void ALookSavedBeforeTheSliders_StandsAtTheMiddleHeight_AndOnlyAChangeCounts()
        {
            CharacterRecipe old = Look();
            old.version = 1;
            old.bodyType = BodyType.Feminine;
            old.height = 0; // a version 1 save never wrote it

            CharacterRecipe same = old.Clone();
            same.Upgrade();
            CharacterRecipe merged = RestyleRules.Merge(old, same, RestyleService.Coach, null);
            Assert.AreEqual(CharacterRecipe.CurrentVersion, merged.version);
            Assert.AreEqual(CharacterRecipe.DefaultHeight, merged.Height);
            Assert.IsTrue(RestyleRules.Same(old, merged), "bringing an old look up to date is no change and costs no pass");

            CharacterRecipe taller = same.Clone();
            taller.height = 70;
            taller.bust = 30;
            merged = RestyleRules.Merge(old, taller, RestyleService.Coach, null);
            Assert.AreEqual(70, merged.Height);
            Assert.AreEqual(30, merged.bust);
            Assert.IsFalse(RestyleRules.Same(old, merged));
        }

        [Test]
        public void TheBody_StaysWithinItsLimits()
        {
            CharacterRecipe wanted = Look();
            wanted.build = 127;
            wanted.muscle = -128;
            CharacterRecipe result = RestyleRules.Merge(Look(), wanted, RestyleService.Coach, null);
            Assert.AreEqual(RestyleRules.BodyLimit, result.build);
            Assert.AreEqual(-RestyleRules.BodyLimit, result.muscle);
        }

        [Test]
        public void EachHeadRegion_BelongsToOneService()
        {
            Assert.IsTrue(RestyleRules.Owns(RestyleService.Stylist, BodyRegion.Hair));
            Assert.IsTrue(RestyleRules.Owns(RestyleService.Stylist, BodyRegion.FacialHair));
            Assert.IsTrue(RestyleRules.Owns(RestyleService.Stylist, BodyRegion.EyebrowRight));
            Assert.IsFalse(RestyleRules.Owns(RestyleService.Stylist, BodyRegion.Nose));
            Assert.IsTrue(RestyleRules.Owns(RestyleService.Reshaper, BodyRegion.Head));
            Assert.IsTrue(RestyleRules.Owns(RestyleService.Reshaper, BodyRegion.EarLeft));
            Assert.IsFalse(RestyleRules.Owns(RestyleService.Reshaper, BodyRegion.Hair));
            Assert.IsFalse(RestyleRules.Owns(RestyleService.Coach, BodyRegion.Head));
            Assert.IsFalse(RestyleRules.Owns(RestyleService.Reshaper, BodyRegion.Torso), "the body comes from gear");
        }

        [Test]
        public void TwoLooksAreTheSame_WhateverTheOrderOfTheirParts()
        {
            CharacterRecipe a = Look(), b = Look();
            b.parts = new ushort[] { 102, 101 };
            b.colors = new[] { a.colors[1], a.colors[0] };
            Assert.IsTrue(RestyleRules.Same(a, b));
            b.scars = 31;
            Assert.IsFalse(RestyleRules.Same(a, b));
            Assert.IsTrue(RestyleRules.Same(Look(), Merge(RestyleService.Coach)) == false);
        }
    }
}
