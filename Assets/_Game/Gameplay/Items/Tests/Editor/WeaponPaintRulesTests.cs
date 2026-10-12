using System.Collections.Generic;
using KryticalNova.Items;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Tests
{
    /// <summary>How a weapon's paints are found from the colours its model uses, and what a colourway puts in their place.</summary>
    public sealed class WeaponPaintRulesTests
    {
        private static readonly Color32 Steel = new Color32(150, 152, 158, 255);
        private static readonly Color32 Purple = new Color32(140, 60, 200, 255);
        private static readonly Color32 PurpleDark = new Color32(84, 36, 120, 255);
        private static readonly Color32 Teal = new Color32(40, 170, 160, 255);
        private static readonly Color32 Brown = new Color32(110, 78, 50, 255);
        private static readonly Color32 Black = new Color32(12, 12, 14, 255);

        private static GearThemeLibrary.Theme Theme() => new GearThemeLibrary.Theme
        {
            id = 7, name = "TEST_Theme",
            primary = new Color32(200, 30, 30, 255), secondary = new Color32(30, 30, 200, 255),
            under = new Color32(20, 20, 20, 255), accent = new Color32(240, 240, 40, 255),
            metal = new[] { new Color32(240, 210, 120, 255), new Color32(200, 160, 60, 255), new Color32(140, 100, 30, 255), new Color32(80, 55, 15, 255) },
            trim = new Color32(250, 250, 250, 255), leather = new Color32(60, 40, 30, 255), glow = new Color32(0, 255, 200, 255),
        };

        private static WeaponPaintBand[] Bands(params (Color32 colour, float weight)[] uses)
        {
            var samples = new List<WeaponPaintRules.Sample>();
            foreach (var use in uses) samples.Add(new WeaponPaintRules.Sample(use.colour, use.weight));
            return WeaponPaintRules.Bands(samples);
        }

        [Test]
        public void ThePaintThatCoversTheMost_IsThePrimary_AndShadesOfOneHueAreOnePaint()
        {
            WeaponPaintBand[] bands = Bands((Steel, 10f), (Purple, 4f), (PurpleDark, 2f), (Teal, 3f), (Black, 5f));
            Assert.AreEqual(2, bands.Length, "steel and black are no paint; the two purples are one");
            Assert.AreEqual(WeaponPaintRole.Primary, bands[0].role);
            Assert.AreEqual(WeaponPaintRole.Secondary, bands[1].role);
            Color.RGBToHSV(Purple, out float purpleHue, out _, out _);
            Assert.Less(WeaponPaintRules.HueDistance(bands[0].hue, purpleHue), 0.02f);
        }

        [Test]
        public void BrownIsLeather_WhateverItCovers()
        {
            WeaponPaintBand[] bands = Bands((Brown, 9f), (Teal, 1f));
            Assert.AreEqual(WeaponPaintRole.Leather, bands[0].role);
            Assert.AreEqual(WeaponPaintRole.Primary, bands[1].role, "the first paint that is not leather is the primary");
        }

        [Test]
        public void ASmallBrightLight_IsTheGlow()
        {
            WeaponPaintBand[] bands = Bands((Purple, 20f), (new Color32(0, 255, 240, 255), 1f));
            Assert.AreEqual(WeaponPaintRole.Primary, bands[0].role);
            Assert.AreEqual(WeaponPaintRole.Glow, bands[1].role);
        }

        [Test]
        public void AModelOfMetalOnly_HasNoPaints()
        {
            Assert.AreEqual(0, Bands((Steel, 5f), (Black, 2f)).Length);
            Assert.AreEqual(0, WeaponPaintRules.Bands(null).Length);
        }

        [Test]
        public void ADye_PutsTheColourwayOnEachPaint_AndKeepsTheShading()
        {
            WeaponPaintBand[] bands = Bands((Purple, 4f), (PurpleDark, 2f), (Teal, 3f), (Brown, 1f));
            GearThemeLibrary.Theme theme = Theme();

            Color32 dyed = WeaponPaintRules.Recolour(Purple, bands, theme, true);
            Color32 dyedDark = WeaponPaintRules.Recolour(PurpleDark, bands, theme, true);
            Assert.Greater(dyed.r, dyed.b, "the primary is red");
            Assert.Greater(dyed.r, dyedDark.r, "the darker part of the paint stays the darker");
            Assert.AreEqual(0, dyedDark.b / 60, "and takes the same colour");

            Color32 teal = WeaponPaintRules.Recolour(Teal, bands, theme, true);
            Assert.Greater(teal.b, teal.r, "the secondary is blue");
            Color32 strap = WeaponPaintRules.Recolour(Brown, bands, theme, true);
            Assert.Less(strap.r, 120, "leather takes the colourway's leather");
        }

        [Test]
        public void Greys_TakeTheMetal_OnlyWhenAsked_AndNearBlackIsLeftAlone()
        {
            WeaponPaintBand[] bands = Bands((Purple, 4f));
            GearThemeLibrary.Theme theme = Theme();
            Color32 plated = WeaponPaintRules.Recolour(Steel, bands, theme, true);
            Assert.Greater(plated.r, plated.b + 40, "steel turns to the colourway's gold");
            Assert.AreEqual(Steel, WeaponPaintRules.Recolour(Steel, bands, theme, false));
            Assert.AreEqual(Black, WeaponPaintRules.Recolour(Black, bands, theme, true));
        }

        [Test]
        public void AColourOfNoPaint_ComesBackAsItWas()
        {
            WeaponPaintBand[] bands = Bands((Purple, 4f));
            var green = new Color32(40, 200, 60, 255);
            Assert.AreEqual(green, WeaponPaintRules.Recolour(green, bands, Theme(), true));
            Assert.AreEqual(green, WeaponPaintRules.Recolour(green, null, Theme(), true));
        }

        // ------------------------------------------------------------------ every material of the weapon

        private static readonly Color32 DarkSteel = new Color32(92, 94, 98, 255);
        private static readonly Color32 PaleSteel = new Color32(214, 216, 220, 255);
        private static readonly Color32 Gold = new Color32(222, 170, 48, 255);
        private static readonly Color32 Grip = new Color32(20, 19, 22, 255);
        private static readonly Color32 Void = new Color32(4, 4, 4, 255);

        private static List<WeaponPaintRules.Sample> Samples(params (Color32 colour, float weight)[] uses)
        {
            var samples = new List<WeaponPaintRules.Sample>();
            foreach (var use in uses) samples.Add(new WeaponPaintRules.Sample(use.colour, use.weight));
            return samples;
        }

        [Test]
        public void TheMetalThatCoversTheMost_StaysMetal_AndTheNextTakesTheColourwaysMainColour()
        {
            List<WeaponPaintRules.Sample> samples = Samples((Steel, 10f), (DarkSteel, 4f), (Black, 2f));
            WeaponPaintBand[] bands = WeaponPaintRules.Bands(samples);
            WeaponPaintShade[] shades = WeaponPaintRules.Shades(samples, bands);
            Assert.AreEqual(0, bands.Length, "a weapon of metal only");
            Assert.AreEqual(2, shades.Length);
            Assert.AreEqual(WeaponPaintRole.Keep, shades[0].role, "the blade");
            Assert.AreEqual(WeaponPaintRole.Primary, shades[1].role, "the guard");

            GearThemeLibrary.Theme theme = Theme();
            Color32 blade = WeaponPaintRules.Recolour(Steel, bands, shades, theme, true, true);
            Color32 guard = WeaponPaintRules.Recolour(DarkSteel, bands, shades, theme, true, true);
            Assert.Greater(blade.r, blade.b + 40, "the blade is the colourway's gold metal");
            Assert.Greater(blade.g, 80, "and not its red");
            Assert.Greater(guard.r, guard.g + 80, "the guard is the colourway's red");
        }

        [Test]
        public void ASecondMetal_TakesTheFirstMainColourNoPaintWears()
        {
            List<WeaponPaintRules.Sample> samples = Samples((Steel, 10f), (DarkSteel, 4f), (PaleSteel, 3f), (Purple, 5f));
            WeaponPaintBand[] bands = WeaponPaintRules.Bands(samples);
            Assert.AreEqual(WeaponPaintRole.Primary, bands[0].role, "the paint has the main colour");
            WeaponPaintShade[] shades = WeaponPaintRules.Shades(samples, bands);
            Assert.AreEqual(3, shades.Length);
            Assert.AreEqual(WeaponPaintRole.Keep, shades[0].role);
            Assert.AreEqual(WeaponPaintRole.Secondary, shades[1].role, "so the larger second metal has the second");
            Assert.AreEqual(WeaponPaintRole.Keep, shades[2].role, "and there is none left for the third: it stays metal");
        }

        [Test]
        public void OneMetal_OrASecondThatIsOnlyRivets_IsNothingToTellApart()
        {
            Assert.AreEqual(0, WeaponPaintRules.Shades(Samples((Steel, 10f), (Black, 4f)), null).Length);
            Assert.AreEqual(0, WeaponPaintRules.Shades(Samples((Steel, 100f), (DarkSteel, 2f)), null).Length, "2 % of the model");
            Assert.AreEqual(0, WeaponPaintRules.Shades(null, null).Length);
        }

        [Test]
        public void GoldIsTheTrim_AndTakesNoPlaceAmongTheMainColours()
        {
            WeaponPaintBand[] bands = Bands((Purple, 6f), (Gold, 4f), (Teal, 3f));
            Assert.AreEqual(WeaponPaintRole.Primary, bands[0].role);
            Assert.AreEqual(WeaponPaintRole.Trim, bands[1].role, "gold fittings");
            Assert.AreEqual(WeaponPaintRole.Secondary, bands[2].role, "the teal is still the second colour");

            // Only gold on a steel blade: it is trim, where it used to be counted as the weapon's main colour.
            Assert.AreEqual(WeaponPaintRole.Trim, Bands((Steel, 10f), (Gold, 2f), (Brown, 2f))[0].role);
            // A weapon that is yellow all over is painted yellow: that is its colour.
            Assert.AreEqual(WeaponPaintRole.Primary, Bands((Gold, 9f), (Teal, 2f))[0].role);
            Assert.IsFalse(WeaponPaintRules.IsGold(0.08f, 0.5f, 0.4f), "brown is leather, not gold");
        }

        [Test]
        public void WhatTheModelLightsUp_IsTheGlow_WhateverItsColour()
        {
            var samples = new List<WeaponPaintRules.Sample>
            {
                new WeaponPaintRules.Sample(Purple, 10f),
                new WeaponPaintRules.Sample(new Color32(70, 130, 60, 255), 6f, lit: true), // dull and large: never guessed as a light
            };
            WeaponPaintBand[] bands = WeaponPaintRules.Bands(samples);
            Assert.AreEqual(WeaponPaintRole.Primary, bands[0].role);
            Assert.AreEqual(WeaponPaintRole.Glow, bands[1].role);

            GearThemeLibrary.Theme theme = Theme();
            Color32 lamp = WeaponPaintRules.RecolourLight(new Color32(255, 40, 0, 255), theme);
            Assert.Greater(lamp.g, 200, "a red light takes the colourway's glow");
            Assert.Less(lamp.r, 40);
            Color32 dim = WeaponPaintRules.RecolourLight(new Color32(120, 20, 0, 255), theme);
            Assert.Less(dim.g, lamp.g, "a dimmer light stays the dimmer");
            Assert.AreEqual(new Color32(0, 0, 0, 255), WeaponPaintRules.RecolourLight(new Color32(0, 0, 0, 255), theme), "what is unlit stays unlit");
        }

        [Test]
        public void ADarkGrip_TakesADarkShadeOfTheColourway_AndBlackIsLeftAlone()
        {
            GearThemeLibrary.Theme theme = Theme();
            theme.under = new Color32(20, 30, 120, 255);
            Color32 grip = WeaponPaintRules.Recolour(Grip, null, null, theme, true, true);
            Assert.Greater(grip.b, grip.r + 10, "it shows the under colour");
            Color.RGBToHSV(grip, out _, out _, out float value);
            Assert.LessOrEqual(value, WeaponPaintRules.DarkCeiling + 0.01f, "and is still dark");
            Assert.AreEqual(Void, WeaponPaintRules.Recolour(Void, null, null, theme, true, true));
            Assert.AreEqual(Grip, WeaponPaintRules.Recolour(Grip, null, null, theme, true, false), "only when asked");

            // An under colour with no hue of its own: the colourway's main colour stands in.
            Color32 red = WeaponPaintRules.Recolour(Grip, null, null, Theme(), true, true);
            Assert.Greater(red.r, red.b + 10);
        }

        [Test]
        public void HueDistance_GoesRoundTheWheel()
        {
            Assert.AreEqual(0.04f, WeaponPaintRules.HueDistance(0.98f, 0.02f), 1e-5f);
            Assert.AreEqual(0.5f, WeaponPaintRules.HueDistance(0f, 0.5f), 1e-5f);
        }
    }
}
