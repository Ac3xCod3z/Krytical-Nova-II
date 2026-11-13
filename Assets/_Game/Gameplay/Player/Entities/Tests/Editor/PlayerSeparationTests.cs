using KryticalNova.Player.Entities;
using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;

namespace KryticalNova.Tests
{
    /// <summary>
    /// Build 17, item 11.1 (owner pick C): walking through another player is fine; standing inside one is not. The one who stopped last
    /// slides out, a step at a time, never further than touching.
    /// </summary>
    public sealed class PlayerSeparationTests
    {
        const int Radius = 400, Step = 25;

        static PlayerSeparation.Body At(int index, int x, int z, uint since) =>
            new PlayerSeparation.Body { Entity = new Entity { Index = index, Version = 1 }, XMm = x, ZMm = z, StoodSince = since };

        [Test]
        public void TheOneWhoStoppedLast_SlidesAway()
        {
            var first = At(1, 0, 0, since: 100);
            var last = At(2, 300, 0, since: 200);
            Assert.IsTrue(PlayerSeparation.SlideOut(first, last, Radius, Step, out int which, out int2 move));
            Assert.AreEqual(1, which, "the later one moves");
            Assert.AreEqual(new int2(Step, 0), move, "straight away from the other, one step");

            // the same pair given the other way round: still the later one, still away
            Assert.IsTrue(PlayerSeparation.SlideOut(last, first, Radius, Step, out which, out move));
            Assert.AreEqual(0, which);
            Assert.AreEqual(new int2(Step, 0), move);
        }

        [Test]
        public void TheSlideStopsAtTouching_AndNotBefore()
        {
            var a = At(1, 0, 0, 1);
            var b = At(2, 790, 0, 2);
            Assert.IsTrue(PlayerSeparation.SlideOut(a, b, Radius, Step, out _, out int2 move));
            Assert.AreEqual(new int2(10, 0), move, "only the 10 mm still overlapping");
            b.XMm = 800;
            Assert.IsFalse(PlayerSeparation.SlideOut(a, b, Radius, Step, out _, out _), "touching is fine");
        }

        [Test]
        public void ExactlyOnTop_StillComesApart_TheSameWayEveryTick()
        {
            var a = At(5, 1000, 1000, 1);
            var b = At(9, 1000, 1000, 2);
            Assert.IsTrue(PlayerSeparation.SlideOut(a, b, Radius, Step, out int which, out int2 first));
            Assert.AreEqual(1, which);
            Assert.IsTrue(PlayerSeparation.SlideOut(a, b, Radius, Step, out _, out int2 again));
            Assert.AreEqual(first, again);
            Assert.AreEqual(Step, math.sqrt(first.x * first.x + first.y * first.y), 1.5);
        }

        [Test]
        public void OneAboveTheOther_IsLeftAlone()
        {
            var a = At(1, 0, 0, 1);
            var b = At(2, 100, 0, 2);
            b.YMm = 2000; // on a ledge above
            Assert.IsFalse(PlayerSeparation.SlideOut(a, b, Radius, Step, out _, out _));
        }

        [Test]
        public void OnlyAStillBodyOnTheGroundCounts()
        {
            Assert.IsTrue(PlayerSeparation.IsStanding(new MoveState()));
            Assert.IsFalse(PlayerSeparation.IsStanding(new MoveState { Flags = MoveFlags.Moving }), "walking through is fine");
            Assert.IsFalse(PlayerSeparation.IsStanding(new MoveState { Water = 1 }));
            Assert.IsFalse(PlayerSeparation.IsStanding(new MoveState { Wall = 1 }));
        }
    }
}
