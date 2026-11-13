using KryticalNova.UI.Hud;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.UI.Tests
{
    /// <summary>Build 17 day list 16: the Mini Deck's beat arc and its cue.</summary>
    public sealed class MiniDeckBeatTests
    {
        private const float W = 134f, H = 186f, R = 18f;

        [Test]
        public void TheNotchIsTheTopCentre()
        {
            Vector2 notch = MiniDeckBeat.Point(0.5f, W, H, R);
            Assert.AreEqual(W * 0.5f, notch.x, 0.01f);
            Assert.AreEqual(0f, notch.y, 0.01f);
        }

        [Test]
        public void TheArcEndsTwoThirdsDownEachSide()
        {
            Vector2 left = MiniDeckBeat.Point(0f, W, H, R), right = MiniDeckBeat.Point(1f, W, H, R);
            Assert.AreEqual(0f, left.x, 0.01f);
            Assert.AreEqual(W, right.x, 0.01f);
            Assert.AreEqual(H * 2f / 3f, left.y, 0.01f);
            Assert.AreEqual(H * 2f / 3f, right.y, 0.01f);
        }

        [Test]
        public void TheArcIsEvenAndUnbroken()
        {
            float step = MiniDeckBeat.Length(W, H, R) / 200f;
            Vector2 last = MiniDeckBeat.Point(0f, W, H, R);
            for (int i = 1; i <= 200; i++)
            {
                Vector2 p = MiniDeckBeat.Point(i / 200f, W, H, R);
                Assert.AreEqual(step, Vector2.Distance(p, last), step * 0.12f, $"step {i}");
                last = p;
            }
        }

        [Test]
        public void TheTwoTicksMeetAtTheNotchOnTheBeat()
        {
            float t = 0.5f * MiniDeckBeat.TickTravel(1f);
            Assert.AreEqual(MiniDeckBeat.Point(0.5f, W, H, R), MiniDeckBeat.Point(t, W, H, R));
            Assert.AreEqual(MiniDeckBeat.Point(0.5f, W, H, R), MiniDeckBeat.Point(1f - t, W, H, R));
            Assert.AreEqual(0f, MiniDeckBeat.TickTravel(0f));
        }

        [TestCase(0f, 1f)]
        [TestCase(0.999f, 1f)]
        [TestCase(0.5f, 0f)]
        public void TheNotchFlashesOnTheBeat(float phase, float flash) =>
            Assert.AreEqual(flash, MiniDeckBeat.NotchFlash(phase, 0.6f, 0.09f), 0.05f);

        [Test]
        public void TheMinimapAndTheDeckNeverShowTogether()
        {
            float map = 1f, deck = 0f;
            for (int i = 0; i < 40; i++)
            {
                (map, deck) = MiniDeckBeat.SwapStep(true, map, deck, 0.1f);
                Assert.IsTrue(map <= 0f || deck <= 0f, "the Deck fades in only once the minimap is gone");
            }
            Assert.AreEqual(0f, map); Assert.AreEqual(1f, deck);
            for (int i = 0; i < 40; i++)
            {
                (map, deck) = MiniDeckBeat.SwapStep(false, map, deck, 0.1f);
                Assert.IsTrue(map <= 0f || deck <= 0f, "the minimap fades back in only once the Deck is gone");
            }
            Assert.AreEqual(1f, map); Assert.AreEqual(0f, deck);
        }
    }
}
