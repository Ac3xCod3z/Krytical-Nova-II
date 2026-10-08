using System;
using KryticalNova.EditorTools;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Tests
{
    /// <summary>
    /// The comic icon treatment on pictures made for the test: the sticker every icon wears (ink outline, paper
    /// keyline, clear corners), colours that stay their colour, dots in the shade, ink that leaves bright strokes
    /// alone, a set's colour moved without touching the others, and a source that is never changed.
    /// </summary>
    public sealed class ComicIconTreatmentTests
    {
        private IconTreatmentSettings settings;
        private ComicIconLook look;

        [SetUp]
        public void SetUp()
        {
            settings = ScriptableObject.CreateInstance<IconTreatmentSettings>();
            look = settings.ToLook(null);
        }

        [TearDown]
        public void TearDown() => UnityEngine.Object.DestroyImmediate(settings);

        // ------------------------------------------------------------------ pictures

        private static byte[] Filled(int size, Func<int, int, Color32> at)
        {
            var pixels = new byte[size * size * 4];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Color32 c = at(x, y);
                    int i = (y * size + x) * 4;
                    pixels[i] = c.r; pixels[i + 1] = c.g; pixels[i + 2] = c.b; pixels[i + 3] = c.a;
                }
            }
            return pixels;
        }

        private Color32 At(byte[] icon, int x, int y)
        {
            int i = (y * look.size + x) * 4;
            return new Color32(icon[i], icon[i + 1], icon[i + 2], icon[i + 3]);
        }

        private static bool Near(Color32 a, Color colour, int tolerance)
        {
            Color32 b = colour;
            return Math.Abs(a.r - b.r) <= tolerance && Math.Abs(a.g - b.g) <= tolerance && Math.Abs(a.b - b.b) <= tolerance;
        }

        private static float HueOf(Color32 c)
        {
            ComicIconTreatment.ToHsv(c.r / 255f, c.g / 255f, c.b / 255f, out float h, out float _, out float _);
            return h;
        }

        // ------------------------------------------------------------------ the sticker

        [Test]
        public void ASquarePicture_BecomesARoundedTile_WithAnInkOutlineAndAPaperKeyline()
        {
            byte[] icon = ComicIconTreatment.Treat(Filled(64, (x, y) => new Color32(40, 110, 220, 255)), 64, 64, ComicIconShape.Square, look);
            Assert.AreEqual(look.size * look.size * 4, icon.Length);
            int centre = look.size / 2;

            Assert.AreEqual(255, At(icon, centre, centre).a, "the middle of the tile is solid");
            Assert.Greater(At(icon, centre, centre).b, At(icon, centre, centre).r, "and still blue");

            // Just outside the picture's top edge: ink, then paper, then nothing.
            int inInk = Mathf.RoundToInt(look.margin - look.outlineWidth * 0.5f);
            Assert.IsTrue(Near(At(icon, centre, inInk), settings.ink, 6), "ink outline above the tile: " + At(icon, centre, inInk));
            int inKeyline = Mathf.RoundToInt(look.margin - look.outlineWidth - look.keylineWidth * 0.5f);
            Assert.IsTrue(Near(At(icon, centre, inKeyline), settings.paper, 10), "paper keyline outside the ink: " + At(icon, centre, inKeyline));
            Assert.AreEqual(0, At(icon, centre, 0).a, "clear above the keyline");

            // The tile's corners are rounded: the very corner of the picture's box is not part of it.
            int corner = Mathf.RoundToInt(look.margin + 1f);
            Assert.IsFalse(At(icon, corner, corner).b > 150 && At(icon, corner, corner).r < 90, "the box's corner is cut round");
            Assert.AreEqual(0, At(icon, 0, 0).a, "and the icon's own corner is clear");
        }

        [Test]
        public void TheShadowFalls_DownAndToTheRight()
        {
            byte[] icon = ComicIconTreatment.Treat(Filled(64, (x, y) => new Color32(40, 110, 220, 255)), 64, 64, ComicIconShape.Square, look);
            int centre = look.size / 2, last = look.size - 1;
            // Beyond the keyline on the right and below there is shadow; on the left and above there is none.
            int beyond = Mathf.RoundToInt(look.margin - look.outlineWidth - look.keylineWidth - look.shadowOffset * 0.5f);
            Assert.Greater(At(icon, last - beyond, centre).a, 0, "shadow on the right");
            Assert.Greater(At(icon, centre, last - beyond).a, 0, "shadow below");
            Assert.AreEqual(0, At(icon, beyond, centre).a, "none on the left");
            Assert.AreEqual(0, At(icon, centre, beyond).a, "none above");
        }

        [Test]
        public void ACutOutPicture_KeepsItsOwnShape()
        {
            const int size = 128;
            byte[] disc = Filled(size, (x, y) =>
            {
                float dx = x - 63.5f, dy = y - 63.5f;
                return dx * dx + dy * dy < 40f * 40f ? new Color32(220, 40, 40, 255) : new Color32(0, 0, 0, 0);
            });
            byte[] icon = ComicIconTreatment.Treat(disc, size, size, ComicIconShape.CutOut, look);
            int centre = look.size / 2;
            Color32 middle = At(icon, centre, centre);
            Assert.AreEqual(255, middle.a);
            Assert.Greater(middle.r, 150, "the disc is still red: " + middle);
            Assert.Less(middle.g, 110);
            // The disc is fitted to the box, so it reaches the margin on every side; the box's corners stay clear.
            Assert.AreEqual(255, At(icon, centre, Mathf.RoundToInt(look.margin) + 6).a, "the disc fills the box top to bottom");
            int corner = Mathf.RoundToInt(look.margin) + 8;
            Assert.AreEqual(0, At(icon, corner, corner).a, "a disc has no corners");
        }

        [Test]
        public void NothingDrawn_GivesAnEmptyIcon()
        {
            byte[] icon = ComicIconTreatment.Treat(new byte[32 * 32 * 4], 32, 32, ComicIconShape.CutOut, look);
            for (int i = 3; i < icon.Length; i += 4) Assert.AreEqual(0, icon[i]);
        }

        [Test]
        public void TheSource_IsNeverChanged_AndTheSameSourceGivesTheSameIcon()
        {
            byte[] source = Filled(64, (x, y) => new Color32((byte)(x * 4), (byte)(y * 4), 128, 255));
            var before = (byte[])source.Clone();
            byte[] first = ComicIconTreatment.Treat(source, 64, 64, ComicIconShape.Square, look);
            byte[] second = ComicIconTreatment.Treat(source, 64, 64, ComicIconShape.Square, look);
            CollectionAssert.AreEqual(before, source);
            CollectionAssert.AreEqual(first, second);
        }

        [Test]
        public void APictureShorterThanItsSizeSays_IsRefused()
        {
            Assert.Throws<ArgumentException>(() => ComicIconTreatment.Treat(new byte[10], 64, 64, ComicIconShape.Square, look));
        }

        // ------------------------------------------------------------------ colour

        [Test]
        public void AStrongColour_StaysItsColour()
        {
            // A pure red is dark by its luma; it must not be drawn as a dark tone.
            byte[] icon = ComicIconTreatment.Treat(Filled(64, (x, y) => new Color32(255, 0, 0, 255)), 64, 64, ComicIconShape.Square, look);
            Color32 middle = At(icon, look.size / 2, look.size / 2);
            Assert.Greater(middle.r, 170, middle.ToString());
            Assert.Less(middle.g, 90, middle.ToString());
            Assert.Less(middle.b, 90, middle.ToString());
        }

        [Test]
        public void ASetsOwnColour_IsMoved_AndOtherColoursAreLeft()
        {
            look.palettePull = 0f;
            look.inkEdge = 0f;
            look.hueFrom = 220f;
            look.hueTo = 280f;
            look.hueBand = 40f;
            // Left half blue (the set's colour), right half orange (skin, metal: not the set's colour).
            byte[] icon = ComicIconTreatment.Treat(Filled(64, (x, y) => x < 32 ? new Color32(30, 90, 230, 255) : new Color32(230, 130, 30, 255)),
                64, 64, ComicIconShape.Square, look);
            int centre = look.size / 2;
            float left = HueOf(At(icon, look.size / 4, centre)), right = HueOf(At(icon, look.size * 3 / 4, centre));
            Assert.Less(Mathf.Abs(ComicIconTreatment.HueDelta(left, 280f)), 14f, "blue has moved to violet: hue " + left);
            Assert.Less(Mathf.Abs(ComicIconTreatment.HueDelta(right, 30f)), 12f, "orange is where it was: hue " + right);
        }

        [Test]
        public void EveryHue_IsPulledTowardThePalette()
        {
            look.inkEdge = 0f;
            look.palettePull = 1f;
            look.palette = new uint[] { 0x4FE3F0 }; // neon cyan, hue about 185
            byte[] icon = ComicIconTreatment.Treat(Filled(64, (x, y) => new Color32(40, 120, 230, 255)), 64, 64, ComicIconShape.Square, look);
            float hue = HueOf(At(icon, look.size / 2, look.size / 2));
            Assert.Less(Mathf.Abs(ComicIconTreatment.HueDelta(hue, 185f)), 8f, "hue " + hue);
        }

        // ------------------------------------------------------------------ tones, dots and ink

        /// <summary>How often the brightness along the icon's middle row steps up or down by more than a little.</summary>
        private int StepsAcross(byte[] icon)
        {
            int y = look.size / 2, steps = 0;
            int from = Mathf.RoundToInt(look.margin) + 4, to = look.size - from;
            int before = Mathf.Max(At(icon, from, y).r, Mathf.Max(At(icon, from, y).g, At(icon, from, y).b));
            for (int x = from + 1; x < to; x++)
            {
                Color32 c = At(icon, x, y);
                int now = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
                if (Mathf.Abs(now - before) > 12) steps++;
                before = now;
            }
            return steps;
        }

        [Test]
        public void SoftShading_BecomesAFewFlatTones_WithDotsInTheShade()
        {
            byte[] ramp = Filled(128, (x, y) => new Color32((byte)(x * 2), (byte)(x * 2), (byte)(x * 2), 255));
            look.inkEdge = 0f;
            look.palettePull = 0f;

            look.dotContrast = 0f;
            int flat = StepsAcross(ComicIconTreatment.Treat(ramp, 128, 128, ComicIconShape.Square, look));
            Assert.LessOrEqual(flat, look.tones * 3, "a smooth ramp is drawn as a few flat tones");
            Assert.GreaterOrEqual(flat, look.tones - 1, "and every tone is there");

            look.dotContrast = 1f;
            int dotted = StepsAcross(ComicIconTreatment.Treat(ramp, 128, 128, ComicIconShape.Square, look));
            Assert.Greater(dotted, flat + 4, "with dots on, the second darkest tone is drawn as dots");
        }

        [Test]
        public void Ink_IsDrawnWhereTwoColoursMeet_ButNeverOverTheLightestTone()
        {
            look.palettePull = 0f;
            look.dotContrast = 0f;
            // A dark ground with one thin white stroke down the middle, like a bolt of lightning.
            byte[] bolt = Filled(128, (x, y) => x >= 61 && x <= 66 ? new Color32(255, 255, 255, 255) : new Color32(20, 30, 90, 255));
            byte[] icon = ComicIconTreatment.Treat(bolt, 128, 128, ComicIconShape.Square, look);
            int centre = look.size / 2;
            Color32 stroke = At(icon, centre, centre);
            Assert.Greater(Mathf.Min(stroke.r, Mathf.Min(stroke.g, stroke.b)), 190, "the stroke keeps its light: " + stroke);

            bool inkBeside = false;
            for (int x = centre - 16; x <= centre + 16; x++)
                if (Near(At(icon, x, centre), settings.ink, 10)) inkBeside = true;
            Assert.IsTrue(inkBeside, "and is inked on its dark sides");
        }
    }
}
