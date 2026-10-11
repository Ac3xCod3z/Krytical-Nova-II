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

        [Test]
        public void HueDistance_GoesRoundTheWheel()
        {
            Assert.AreEqual(0.04f, WeaponPaintRules.HueDistance(0.98f, 0.02f), 1e-5f);
            Assert.AreEqual(0.5f, WeaponPaintRules.HueDistance(0f, 0.5f), 1e-5f);
        }
    }
}
