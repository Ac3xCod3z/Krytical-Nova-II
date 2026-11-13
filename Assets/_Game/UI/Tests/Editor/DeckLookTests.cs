using KryticalNova.UI;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Tests
{
    /// <summary>Build 17, 5.5c: the Deck's bar colours follow the Beat Ring's low / mid / high, and peak caps fall slowly.</summary>
    public sealed class DeckLookTests
    {
        private static readonly Color Low = Color.cyan, Mid = Color.yellow, High = Color.red;

        [Test]
        public void BarColourGoesLowToMidToHigh()
        {
            Assert.AreEqual(Low, DeckLook.BeatColour(0f, Low, Mid, High));
            Assert.AreEqual(Mid, DeckLook.BeatColour(0.5f, Low, Mid, High));
            Assert.AreEqual(High, DeckLook.BeatColour(1f, Low, Mid, High));
            Assert.AreEqual(High, DeckLook.BeatColour(3f, Low, Mid, High), "clamped");
        }

        [Test]
        public void PeakJumpsUpAndFallsBackSlowly()
        {
            Assert.AreEqual(0.8f, DeckLook.Peak(0.2f, 0.8f, 0.5f, 0.1f), 1e-6f, "jumps to a louder bar");
            Assert.AreEqual(0.75f, DeckLook.Peak(0.8f, 0.1f, 0.5f, 0.1f), 1e-6f, "falls 0.05 in 0.1 s");
            Assert.AreEqual(0.3f, DeckLook.Peak(0.32f, 0.3f, 0.5f, 0.1f), 1e-6f, "never below the bar");
        }
    }
}
