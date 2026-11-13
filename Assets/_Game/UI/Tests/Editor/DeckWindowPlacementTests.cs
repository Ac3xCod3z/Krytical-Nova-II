using KryticalNova.UI;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace KryticalNova.Tests
{
    /// <summary>Build 17: the Deck window is placed in the HUD layout editor like any window, and keeps its place.</summary>
    public sealed class DeckWindowPlacementTests
    {
        [Test]
        public void TheDeckWindow_BuiltAfterTheHud_TakesItsSavedPlace_AndResetGivesItsDefaultBack()
        {
            string before = HudPreferences.LayoutJson;
            try
            {
                HudPreferences.LayoutJson =
                    "{\"entries\":[{\"name\":\"" + DeckWindowController.WindowName + "\",\"x\":0.25,\"y\":0.5,\"scale\":1.0}]}";
                var layout = new HudLayoutEditor();
                var root = new VisualElement();
                layout.Collect(new[] { root });
                layout.LoadAndApply();

                // The Deck builds its window later than the HUD was collected.
                var deck = new VisualElement { name = DeckWindowController.WindowName };
                deck.AddToClassList(HudLayoutEditor.MovableClass);
                root.Add(deck);
                layout.Adopt(deck);

                Assert.IsTrue(layout.HasSavedPosition(DeckWindowController.WindowName));
                Assert.AreEqual(LengthUnit.Percent, deck.style.left.value.unit);
                Assert.AreEqual(25f, deck.style.left.value.value, 0.001f);
                Assert.AreEqual(50f, deck.style.top.value.value, 0.001f);
                Assert.AreEqual("THE DECK", layout.LabelOf(DeckWindowController.WindowName));

                layout.ResetAll();
                Assert.IsFalse(layout.HasSavedPosition(DeckWindowController.WindowName));
                Assert.AreEqual(StyleKeyword.Null, deck.style.left.keyword, "back to Deck.uss's default place");
                Assert.AreEqual(StyleKeyword.Null, deck.style.top.keyword);

                layout.Forget(deck);
                Assert.AreEqual("CHOOSE", layout.LabelOf(DeckWindowController.WindowName), "a switched-off Deck is no longer held");
            }
            finally
            {
                HudPreferences.LayoutJson = before;
            }
        }
    }
}
