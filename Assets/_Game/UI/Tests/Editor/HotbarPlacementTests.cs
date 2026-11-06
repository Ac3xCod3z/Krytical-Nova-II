using KryticalNova.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace KryticalNova.Tests
{
    /// <summary>
    /// Where the Hotbar stands: by default centred between the chat (bottom left) and the minimap (bottom right), and
    /// once the player has placed it, where they put it, also on a HUD whose tree was built again (a relog).
    /// </summary>
    public sealed class HotbarPlacementTests
    {
        private static readonly Rect Screen = new Rect(0f, 0f, 1280f, 720f);

        [Test]
        public void ByDefault_TheHotbarIsCentredBetweenTheChatAndTheMinimap()
        {
            var chat = new Rect(16f, 494f, 420f, 210f);      // right edge 436
            var minimap = new Rect(1074f, 514f, 190f, 190f); // left edge 1074
            Assert.AreEqual((436f + 1074f) * 0.5f, HudLayoutEditor.HotbarDefaultCentre(Screen, chat, minimap), 0.01f);
        }

        [Test]
        public void AMinimapAtTheTop_LeavesTheRightSideToTheScreensEdge()
        {
            var chat = new Rect(16f, 494f, 420f, 210f);
            var minimapTopRight = new Rect(1050f, 92f, 190f, 190f);
            Assert.AreEqual((436f + 1280f) * 0.5f, HudLayoutEditor.HotbarDefaultCentre(Screen, chat, minimapTopRight), 0.01f);
        }

        [Test]
        public void WithNeitherShown_TheHotbarIsInTheScreensMiddle()
        {
            Assert.AreEqual(640f, HudLayoutEditor.HotbarDefaultCentre(Screen, null, null), 0.01f);
            // A neighbour that has no size yet (hidden) does not count.
            Assert.AreEqual(640f, HudLayoutEditor.HotbarDefaultCentre(Screen, new Rect(16f, 700f, 0f, 0f), null), 0.01f);
        }

        [Test]
        public void APlacedHotbar_ComesBackWhereThePlayerPutIt_AlsoOnARebuiltHud()
        {
            string before = HudPreferences.LayoutJson;
            try
            {
                HudPreferences.LayoutJson =
                    "{\"entries\":[{\"name\":\"hotbar-dock\",\"x\":0.3125,\"y\":0.875,\"scale\":1.1}]}";

                var layout = new HudLayoutEditor();
                VisualElement first = Hud(out VisualElement firstRoot);
                layout.Collect(new[] { firstRoot });
                layout.LoadAndApply();
                Assert.IsTrue(layout.HasSavedPosition(HudLayoutEditor.HotbarName));
                AssertPlaced(first);

                // The HUD is enabled again: its document builds a new tree, which gets the same layout.
                VisualElement second = Hud(out VisualElement secondRoot);
                layout.Collect(new[] { secondRoot });
                layout.LoadAndApply();
                AssertPlaced(second);

                // RESET gives the Hotbar back to its default place: nothing of the player's placement is left on it.
                layout.ResetAll();
                Assert.IsFalse(layout.HasSavedPosition(HudLayoutEditor.HotbarName));
                Assert.AreEqual(StyleKeyword.Null, second.style.top.keyword);
                Assert.AreEqual(StyleKeyword.Null, second.style.scale.keyword);
                Assert.AreEqual(StyleKeyword.Null, second.style.translate.keyword, "the faction's Hotbar centres its main bar again");
            }
            finally
            {
                HudPreferences.LayoutJson = before;
            }
        }

        private static void AssertPlaced(VisualElement dock)
        {
            Assert.AreEqual(LengthUnit.Percent, dock.style.left.value.unit);
            Assert.AreEqual(31.25f, dock.style.left.value.value, 0.001f);
            Assert.AreEqual(87.5f, dock.style.top.value.value, 0.001f);
            Assert.AreEqual(1.1f, dock.style.scale.value.value.x, 0.001f);
            Assert.AreEqual(StyleKeyword.Auto, dock.style.bottom.keyword, "the sheet's bottom no longer holds it");
        }

        private static VisualElement Hud(out VisualElement root)
        {
            root = new VisualElement();
            var dock = new VisualElement { name = HudLayoutEditor.HotbarName };
            dock.AddToClassList(HudLayoutEditor.MovableClass);
            root.Add(dock);
            foreach (string name in new[] { HudLayoutEditor.ChatName, HudLayoutEditor.MinimapName })
            {
                var panel = new VisualElement { name = name };
                panel.AddToClassList(HudLayoutEditor.MovableClass);
                root.Add(panel);
            }
            return dock;
        }
    }
}
