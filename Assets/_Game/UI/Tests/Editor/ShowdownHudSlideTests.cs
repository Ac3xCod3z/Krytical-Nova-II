using KryticalNova.UI;
using KryticalNova.UI.Hud;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace KryticalNova.Tests
{
    /// <summary>
    /// Build 17, 10.2: the status frame and the Hotbar ease away from the Showdown bars on the bars' own curve, and the
    /// player's saved layout is never changed by it.
    /// </summary>
    public sealed class ShowdownHudSlideTests
    {
        [Test]
        public void TheOffsetFollowsTheBarsAndIsNothingWhileEditing()
        {
            Assert.AreEqual(0f, HudSlide.Offset(0f, 0.11f, 0.025f, false));
            Assert.AreEqual(0.025f, HudSlide.Offset(0.11f, 0.11f, 0.025f, false), 1e-6f);
            Assert.AreEqual(0.0125f, HudSlide.Offset(0.055f, 0.11f, 0.025f, false), 1e-6f);
            Assert.AreEqual(0.03f, HudSlide.Offset(1f, 0.11f, 0.025f, false), 1e-6f, "the start punch is capped");
            Assert.AreEqual(0f, HudSlide.Offset(0.11f, 0.11f, 0.025f, true), "never while the layout editor is open");
        }

        [Test]
        public void PanelsMoveAwayFromTheirNearestBar()
        {
            Assert.AreEqual(1f, HudSlide.Direction(100f, 1080f), "status frame at the top goes down");
            Assert.AreEqual(-1f, HudSlide.Direction(1000f, 1080f), "Hotbar at the bottom goes up");
        }

        [Test]
        public void TheSavedLayoutIsUnchangedAfterABout()
        {
            string before = HudPreferences.LayoutJson;
            var root = new VisualElement();
            var summary = new VisualElement { name = "summary" };
            summary.style.left = Length.Percent(12f);
            summary.style.top = Length.Percent(4f);
            root.Add(summary);
            var slide = new HudSlide();

            slide.Apply(root, 0.025f); // no layout yet: nothing to measure, nothing written
            slide.Release();

            Assert.AreEqual(StyleKeyword.Null, summary.style.marginTop.keyword);
            Assert.AreEqual(StyleKeyword.Null, summary.style.marginBottom.keyword);
            Assert.AreEqual(Length.Percent(12f), summary.style.left.value);
            Assert.AreEqual(Length.Percent(4f), summary.style.top.value);
            Assert.AreEqual(before, HudPreferences.LayoutJson);
        }
    }
}
