using KryticalNova.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;

namespace KryticalNova.Tests
{
    /// <summary>
    /// What the HUD's document must hold for the layout editor: the column the moments of play stand in, as a panel
    /// the player may place, and the pieces their controllers move into it.
    /// </summary>
    public sealed class HudLayoutTests
    {
        private const string HudPath = "Assets/_Game/UI/HUD/ProgressionHUD.uxml";

        [Test]
        public void TheHudHasOneMovableColumnForItsMoments()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(HudPath);
            Assert.IsNotNull(tree, HudPath);
            VisualElement root = tree.Instantiate();

            VisualElement column = root.Q(HeraldWindowController.MilestoneColumn);
            Assert.IsNotNull(column, "the milestone column");
            Assert.IsTrue(column.ClassListContains(HudLayoutEditor.MovableClass), "placed in the layout editor like any panel");
            foreach (string piece in new[] { "toast", "nova-banner", "pet-evolved" })
                Assert.IsNotNull(root.Q(piece), piece + " is in the HUD for its controller to move into the column");
        }

        [Test]
        public void TheWindowsThatOnlyOpenAtAnNpcAreMovable()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(HudPath);
            VisualElement root = tree.Instantiate();
            foreach (string window in new[] { "herald-window" })
            {
                VisualElement found = root.Q(window);
                Assert.IsNotNull(found, window);
                Assert.IsTrue(found.ClassListContains(HudLayoutEditor.MovableClass), window);
            }
        }
    }
}
