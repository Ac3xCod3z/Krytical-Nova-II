using KryticalNova.UI;
using KryticalNova.UI.Hud;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;

namespace KryticalNova.UI.Tests
{
    /// <summary>
    /// Build 17 3.3 (owner, 13 Nov 2026): while the Mini Deck is in, the WHOLE minimap block is gone (frame, backdrop, map, ring, north,
    /// zoom buttons, and the faction's dial drawn on its own canvas), nothing left behind the deck; and all of it comes back after.
    /// </summary>
    public sealed class MiniDeckSwapTests
    {
        const string MinimapUxml = "Assets/_Game/UI/HUD/Minimap/Minimap.uxml";

        [TearDown]
        public void TearDown() => MinimapChromeView.StepAside01 = 1f;

        static VisualElement Minimap(out VisualElement deck)
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(MinimapUxml);
            Assert.IsNotNull(tree, MinimapUxml);
            VisualElement minimap = tree.Instantiate().Q("minimap");
            Assert.IsNotNull(minimap);
            Assert.Greater(minimap.childCount, 5, "frame, backdrop, mask, ring, north, zoom buttons");
            deck = new VisualElement { name = "mini-deck" };
            minimap.Add(deck);
            return minimap;
        }

        [Test]
        public void WithTheDeckIn_EveryPartOfTheMinimapIsGone()
        {
            VisualElement minimap = Minimap(out VisualElement deck);
            MiniDeckController.ApplySwap(minimap, deck, 0f, 1f);
            foreach (VisualElement child in minimap.Children())
            {
                if (child == deck) continue;
                Assert.AreEqual(0f, child.style.opacity.value, 1e-6, child.name + " " + string.Join(" ", child.GetClasses()));
                Assert.AreEqual(Visibility.Hidden, child.style.visibility.value, child.name + " is hidden");
            }
            Assert.AreEqual(PickingMode.Ignore, minimap.Q<Button>("minimap-zoom-in").pickingMode, "no button to click behind the deck");
            Assert.AreEqual(0f, MinimapChromeView.StepAside01, "the faction's dial (its own canvas) fades too");
            Assert.AreEqual(DisplayStyle.Flex, deck.style.display.value);
        }

        [Test]
        public void HalfWay_TheDialAndThePartsFadeTogether_ThenAllComeBack()
        {
            VisualElement minimap = Minimap(out VisualElement deck);
            MiniDeckController.ApplySwap(minimap, deck, 0.5f, 0f);
            Assert.AreEqual(0.5f, MinimapChromeView.StepAside01, 1e-6);
            Assert.AreEqual(0.5f, minimap.Q("minimap-north").style.opacity.value, 1e-6);
            Assert.AreEqual(DisplayStyle.None, deck.style.display.value, "never both at once");

            MiniDeckController.ApplySwap(minimap, deck, 1f, 0f);
            foreach (VisualElement child in minimap.Children())
            {
                if (child == deck) continue;
                Assert.AreEqual(StyleKeyword.Null, child.style.opacity.keyword, child.name + " back to its own look");
                Assert.AreEqual(StyleKeyword.Null, child.style.visibility.keyword);
            }
            Assert.AreEqual(PickingMode.Position, minimap.Q<Button>("minimap-zoom-in").pickingMode);
            Assert.AreEqual(1f, MinimapChromeView.StepAside01);
        }
    }
}
