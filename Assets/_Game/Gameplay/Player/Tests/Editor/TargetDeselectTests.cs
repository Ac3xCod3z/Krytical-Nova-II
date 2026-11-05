using KryticalNova.Player;
using KryticalNova.Progression;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Player.Tests
{
    /// <summary>
    /// The deselect rule (owner, 5 Nov 2026): one click away from the selected target walks there and keeps the target
    /// and its ring; only a quick double-click away (two clicks within the window) drops it.
    /// </summary>
    public sealed class TargetDeselectTests
    {
        private const float Window = 0.5f;

        [Test]
        public void SingleClickAway_KeepsTheTarget()
        {
            var clicks = new PointAndClickPlayer.ClickAwayDeselect();
            Assert.IsFalse(clicks.Clears(10f, Window));
        }

        [Test]
        public void DoubleClickWithinTheWindow_ClearsTheTarget()
        {
            var clicks = new PointAndClickPlayer.ClickAwayDeselect();
            Assert.IsFalse(clicks.Clears(10f, Window));
            Assert.IsTrue(clicks.Clears(10.3f, Window));
        }

        [Test]
        public void SecondClickRightAtTheWindow_StillClears()
        {
            var clicks = new PointAndClickPlayer.ClickAwayDeselect();
            clicks.Clears(10f, Window);
            Assert.IsTrue(clicks.Clears(10f + Window, Window));
        }

        [Test]
        public void TwoClicksSlowerThanTheWindow_KeepTheTarget()
        {
            var clicks = new PointAndClickPlayer.ClickAwayDeselect();
            Assert.IsFalse(clicks.Clears(10f, Window));
            Assert.IsFalse(clicks.Clears(10.8f, Window));
            // A steady walk, one click a second, never drops it.
            Assert.IsFalse(clicks.Clears(11.8f, Window));
            Assert.IsFalse(clicks.Clears(12.8f, Window));
        }

        [Test]
        public void SlowClickThenQuickOne_TheQuickPairClears()
        {
            var clicks = new PointAndClickPlayer.ClickAwayDeselect();
            clicks.Clears(10f, Window);
            clicks.Clears(11f, Window);
            Assert.IsTrue(clicks.Clears(11.2f, Window));
        }

        [Test]
        public void AfterAClear_TheNextClickIsASingleOneAgain()
        {
            var clicks = new PointAndClickPlayer.ClickAwayDeselect();
            clicks.Clears(10f, Window);
            Assert.IsTrue(clicks.Clears(10.2f, Window));
            Assert.IsFalse(clicks.Clears(10.4f, Window), "a third quick click is a first click on the next target");
        }

        [Test]
        public void ANewSelection_ForgetsTheEarlierClickAway()
        {
            // A click away, then a new target picked (Select resets), then one click away: the new target stays.
            var clicks = new PointAndClickPlayer.ClickAwayDeselect();
            clicks.Clears(10f, Window);
            clicks.Reset();
            Assert.IsFalse(clicks.Clears(10.2f, Window));
        }

        [Test]
        public void TheWindowIsASetting_HalfASecondByDefault()
        {
            var settings = ScriptableObject.CreateInstance<MovementSettings>();
            try
            {
                Assert.AreEqual(0.5f, MovementSettings.DefaultDeselectDoubleClickWindow);
                Assert.AreEqual(MovementSettings.DefaultDeselectDoubleClickWindow, settings.deselectDoubleClickWindow);
            }
            finally { Object.DestroyImmediate(settings); }
        }
    }
}
