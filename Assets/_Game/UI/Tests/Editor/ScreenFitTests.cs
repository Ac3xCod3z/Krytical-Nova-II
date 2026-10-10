using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.UI.Tests
{
    /// <summary>Where a tooltip, a menu or a window goes so that all of it is on screen.</summary>
    public sealed class ScreenFitTests
    {
        private static readonly Rect Screen = new Rect(0f, 0f, 1280f, 720f);
        private const float Gap = 8f, Margin = 12f;

        private static void AssertInside(Rect area, Vector2 topLeft, Vector2 size, string what)
        {
            Assert.GreaterOrEqual(topLeft.x, area.xMin - 0.001f, what + ": left");
            Assert.GreaterOrEqual(topLeft.y, area.yMin - 0.001f, what + ": top");
            Assert.LessOrEqual(topLeft.x + size.x, area.xMax + 0.001f, what + ": right");
            Assert.LessOrEqual(topLeft.y + size.y, area.yMax + 0.001f, what + ": bottom");
        }

        // The item card beside every cell of a Pack that stands anywhere on screen, at any card height that fits.
        [Test]
        public void A_card_beside_any_cell_is_wholly_on_screen()
        {
            Rect area = ScreenFit.Inset(Screen, Margin);
            var card = new Vector2(270f, 0f);
            for (float height = 120f; height <= 690f; height += 95f)
            {
                card.y = height;
                for (float x = 0f; x <= 1240f; x += 62f)
                    for (float y = 0f; y <= 680f; y += 57f)
                    {
                        var cell = new Rect(x, y, 40f, 40f);
                        AssertInside(area, ScreenFit.Beside(cell, card, area, Gap), card, $"cell at {x},{y}, card {height} tall");
                    }
            }
        }

        [Test]
        public void A_card_goes_to_the_right_of_its_cell_when_there_is_room_and_to_the_left_when_there_is_not()
        {
            Rect area = ScreenFit.Inset(Screen, Margin);
            var card = new Vector2(270f, 300f);
            var left = new Rect(200f, 100f, 40f, 40f);
            Assert.AreEqual(left.xMax + Gap, ScreenFit.Beside(left, card, area, Gap).x, 0.001f);
            var right = new Rect(1200f, 100f, 40f, 40f);
            Assert.AreEqual(right.xMin - Gap - card.x, ScreenFit.Beside(right, card, area, Gap).x, 0.001f);
        }

        // The card in the screenshot: a tall pet card beside a cell on the Pack's bottom row.
        [Test]
        public void A_tall_card_beside_a_bottom_cell_is_lifted_not_cut_off()
        {
            Rect area = ScreenFit.Inset(Screen, Margin);
            var card = new Vector2(270f, 520f);
            var cell = new Rect(600f, 560f, 40f, 40f);
            Vector2 at = ScreenFit.Beside(cell, card, area, Gap);
            Assert.AreEqual(area.yMax - card.y, at.y, 0.001f, "its foot is on the screen's edge");
        }

        [Test]
        public void A_box_that_fits_is_left_where_it_is()
        {
            Rect area = ScreenFit.Inset(Screen, 20f);
            var at = new Vector2(300f, 200f);
            Assert.AreEqual(at, ScreenFit.Clamp(area, at, new Vector2(336f, 400f)));
            Assert.AreEqual(1f, ScreenFit.ScaleToFit(area, new Vector2(336f, 400f)));
        }

        // The forge measured 784 units tall in the Null skin on a screen 686 high: it is made smaller, and then it fits.
        [TestCase(504f, 800f, 1219f, 790f)]
        [TestCase(336f, 784f, 1220f, 686f)]
        [TestCase(920f, 700f, 985f, 554f)]
        public void A_window_too_large_for_the_screen_is_shrunk_until_it_fits(float width, float height, float screenWidth, float screenHeight)
        {
            Rect area = ScreenFit.Inset(new Rect(0f, 0f, screenWidth, screenHeight), 20f);
            var size = new Vector2(width, height);
            float scale = ScreenFit.ScaleToFit(area, size);
            Assert.Less(scale, 1f);
            Assert.Greater(scale, 0.5f, "still readable");
            AssertInside(area, ScreenFit.Clamp(area, new Vector2(35f, 0f), size * scale), size * scale, "the shrunk window");
        }

        [Test]
        public void A_margin_larger_than_the_screen_does_not_turn_the_area_inside_out()
        {
            Rect area = ScreenFit.Inset(new Rect(0f, 0f, 30f, 30f), 20f);
            Assert.GreaterOrEqual(area.width, 0f);
            Assert.GreaterOrEqual(area.height, 0f);
        }
    }
}
