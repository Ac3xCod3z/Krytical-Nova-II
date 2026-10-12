using NUnit.Framework;

namespace KryticalNova.Audio.Tests
{
    /// <summary>Keeping time by arithmetic: where a moment falls against a track's beats, and which track is playing.</summary>
    public sealed class BeatMathTests
    {
        private const double First = 0.25;      // the track's first beat
        private const double Beat = 0.5;        // 120 beats a minute
        private const double Tiny = 1e-9;

        [Test]
        public void OnABeat_TheErrorIsNothing()
        {
            Assert.AreEqual(0.0, BeatMath.ErrorSeconds(First, First, Beat), Tiny);
            Assert.AreEqual(0.0, BeatMath.ErrorSeconds(First + 7 * Beat, First, Beat), Tiny);
        }

        [Test]
        public void BeforeABeat_IsEarly_AndAfterIt_Late()
        {
            Assert.AreEqual(-0.04, BeatMath.ErrorSeconds(First + 3 * Beat - 0.04, First, Beat), Tiny);
            Assert.AreEqual(0.06, BeatMath.ErrorSeconds(First + 3 * Beat + 0.06, First, Beat), Tiny);
        }

        [Test]
        public void TheError_IsNeverMoreThanHalfABeat()
        {
            for (double position = 0.0; position < 4.0; position += 0.013)
                Assert.LessOrEqual(System.Math.Abs(BeatMath.ErrorSeconds(position, First, Beat)), Beat / 2 + Tiny);
        }

        [Test]
        public void BeforeTheFirstBeat_TheGridStillRuns()
        {
            // The beat before the first one is where it would be: a press just ahead of the track's first beat is early, not lost.
            Assert.AreEqual(-0.05, BeatMath.ErrorSeconds(First - 0.05, First, Beat), Tiny);
            Assert.AreEqual(-1, BeatMath.BeatIndex(First - 0.05, First, Beat));
        }

        [Test]
        public void BeatIndexAndPhase_FollowThePosition()
        {
            Assert.AreEqual(0, BeatMath.BeatIndex(First, First, Beat));
            Assert.AreEqual(2, BeatMath.BeatIndex(First + 2 * Beat + 0.1, First, Beat));
            Assert.AreEqual(0.2, BeatMath.Phase01(First + 2 * Beat + 0.1, First, Beat), 1e-6);
        }

        [Test]
        public void TheBar_StartsOnItsOwnBeat_BeforeTheFirstBeatToo()
        {
            Assert.IsTrue(BeatMath.IsBarBeat(2, 2, 4));
            Assert.IsTrue(BeatMath.IsBarBeat(6, 2, 4));
            Assert.IsFalse(BeatMath.IsBarBeat(3, 2, 4));
            Assert.IsTrue(BeatMath.IsBarBeat(-2, 2, 4));
            Assert.IsFalse(BeatMath.IsBarBeat(-1, 2, 4));
        }

        [Test]
        public void ThePulse_IsFullOnTheBeat_AndFallsAway()
        {
            Assert.AreEqual(1f, BeatMath.Pulse(0.0, 6f), 1e-6f);
            Assert.Less(BeatMath.Pulse(0.5, 6f), 0.06f);
            Assert.Greater(BeatMath.Pulse(0.1, 6f), BeatMath.Pulse(0.2, 6f));
        }

        [Test]
        public void ARound_PlaysEachTrackInTurn_WithSilenceBetween()
        {
            double[] lengths = { 100.0, 60.0 };
            const double gap = 2.0;

            Assert.IsTrue(BeatMath.Locate(0.0, lengths, gap, out int track, out double position));
            Assert.AreEqual(0, track);
            Assert.AreEqual(0.0, position, Tiny);

            Assert.IsTrue(BeatMath.Locate(99.5, lengths, gap, out track, out position));
            Assert.AreEqual(0, track);
            Assert.AreEqual(99.5, position, Tiny);

            Assert.IsFalse(BeatMath.Locate(101.0, lengths, gap, out track, out _), "between two tracks there is no track");

            Assert.IsTrue(BeatMath.Locate(112.0, lengths, gap, out track, out position));
            Assert.AreEqual(1, track);
            Assert.AreEqual(10.0, position, Tiny);

            Assert.IsFalse(BeatMath.Locate(163.0, lengths, gap, out track, out _), "nor after the last one");
        }

        [Test]
        public void ARound_ComesRoundAgain()
        {
            double[] lengths = { 100.0, 60.0 };
            const double round = 100.0 + 2.0 + 60.0 + 2.0;

            Assert.IsTrue(BeatMath.Locate(round * 3 + 12.5, lengths, 2.0, out int track, out double position));
            Assert.AreEqual(0, track);
            Assert.AreEqual(12.5, position, 1e-6);

            Assert.IsTrue(BeatMath.Locate(-round + 30.0, lengths, 2.0, out track, out position), "a clock that starts below zero is still in the round");
            Assert.AreEqual(0, track);
            Assert.AreEqual(30.0, position, 1e-6);
        }

        [Test]
        public void ATrackWithNoLength_IsPassedOver()
        {
            double[] lengths = { 0.0, 60.0 };
            Assert.IsTrue(BeatMath.Locate(5.0, lengths, 2.0, out int track, out double position));
            Assert.AreEqual(1, track);
            Assert.AreEqual(5.0, position, Tiny);
            Assert.IsFalse(BeatMath.Locate(5.0, new double[] { 0.0 }, 2.0, out _, out _));
        }
    }
}
