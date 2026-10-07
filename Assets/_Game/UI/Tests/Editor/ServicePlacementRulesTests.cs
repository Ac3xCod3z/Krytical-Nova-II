using KryticalNova.UI;
using NUnit.Framework;

namespace KryticalNova.Tests
{
    /// <summary>Where a service window docks and how far the camera slides so the character stays in view.</summary>
    public sealed class ServicePlacementRulesTests
    {
        private const float Screen = 1280f, Gap = 8f, Margin = 12f, Limit = 0.4f;

        [Test]
        public void AWindowDocksOnTheLeftOfThePack_WhenThereIsRoom()
        {
            // The Pack at 656 to 1028: a 336 wide bench fits on its left.
            Assert.AreEqual(656f - Gap - 336f, ServicePlacementRules.DockBeside(656f, 1028f, 336f, Screen, Gap, Margin));
        }

        [Test]
        public void WithNoRoomOnTheLeft_ItGoesOnTheRight()
        {
            // The Pack pushed to the left edge.
            Assert.AreEqual(400f + Gap, ServicePlacementRules.DockBeside(28f, 400f, 336f, Screen, Gap, Margin));
        }

        [Test]
        public void WithNoRoomEitherSide_ItStaysOnScreen()
        {
            float left = ServicePlacementRules.DockBeside(100f, 472f, 336f, 500f, Gap, Margin);
            Assert.GreaterOrEqual(left, Margin);
            Assert.LessOrEqual(left + 336f, 500f - Margin + 0.01f);
        }

        [Test]
        public void TheCameraStaysPut_WhenTheWindowsAreClearOfTheCharacter()
        {
            // Windows from 900 to 1270, a character 120 wide in the middle (580 to 700).
            Assert.AreEqual(0f, ServicePlacementRules.Framing(900f, 1270f, Screen, 60f, Limit));
            Assert.AreEqual(0f, ServicePlacementRules.Framing(10f, 300f, Screen, 60f, Limit));
        }

        [Test]
        public void WindowsOnTheRight_MoveTheCharacterToTheMiddleOfTheFreeLeft()
        {
            // Bench and Pack from 312 to 1028: the wider free side is the left, 312 wide; its middle is 156.
            float framing = ServicePlacementRules.Framing(312f, 1028f, Screen, 60f, Limit);
            Assert.AreEqual(156f / Screen - 0.5f, framing, 0.0001f);
            Assert.Less(framing, 0f, "to the left");
        }

        [Test]
        public void WindowsOnTheLeft_MoveTheCharacterToTheRight()
        {
            // Windows from 20 to 700: the free side is the right, 580 wide; its middle is 990.
            float framing = ServicePlacementRules.Framing(20f, 700f, Screen, 60f, Limit);
            Assert.AreEqual(990f / Screen - 0.5f, framing, 0.0001f);
            Assert.Greater(framing, 0f);
        }

        [Test]
        public void TheCharacterIsNeverPushedPastTheLimit()
        {
            // Windows covering all but a sliver on the left.
            Assert.AreEqual(-Limit, ServicePlacementRules.Framing(40f, 1270f, Screen, 60f, Limit), 0.0001f);
        }

        [Test]
        public void NoWindows_NoMove()
        {
            Assert.AreEqual(0f, ServicePlacementRules.Framing(0f, 0f, Screen, 60f, Limit));
            Assert.AreEqual(0f, ServicePlacementRules.Framing(300f, 900f, 0f, 60f, Limit));
        }
    }
}
